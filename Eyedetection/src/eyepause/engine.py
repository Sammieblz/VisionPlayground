"""Application orchestration without Qt: UI polls a bounded latest-status mailbox."""

import asyncio
import queue
import threading
import time
from dataclasses import dataclass

from eyepause.camera import CameraPipeline, LatestValue, VisionResult
from eyepause.domain import EyeState, Observation, Session
from eyepause.media import DemoBackend, WindowsBackend
from eyepause.policy import PlaybackPolicy
from eyepause.settings import Settings, save_settings
from eyepause.vision import Calibration


def demo_observation(elapsed: float, now: float) -> Observation:
    phase = elapsed % 14
    if phase >= 12.5:
        return Observation(now, reason="Demo: viewer out of frame")
    ear = 0.10 if 5 <= phase < 8.5 or 3.5 <= phase < 3.67 else 0.31
    return Observation(now, ear, ear, 1, processing_ms=0.0)


@dataclass(frozen=True)
class EngineStatus:
    settings: Settings
    sessions: tuple[Session, ...] = ()
    monitoring: bool = False
    calibrating: bool = False
    busy: bool = False
    eye_state: EyeState = EyeState.UNKNOWN
    progress: float = 0.0
    message: str = "Choose a player, then start monitoring."
    vision: VisionResult | None = None
    owned_pause: bool = False
    calibration_phase: str = ""
    calibration_progress: float = 0.0
    calibration_ready: bool = False
    camera_active: bool = False


class Engine:
    def __init__(
        self,
        settings: Settings,
        *,
        demo: bool = False,
        backend=None,
        pipeline_factory=CameraPipeline,
    ):
        self.settings = settings
        self.demo = demo
        self.backend = (
            backend if backend is not None else (DemoBackend() if demo else WindowsBackend())
        )
        self.pipeline_factory = pipeline_factory
        self.policy = PlaybackPolicy(settings)
        self.status: LatestValue[EngineStatus] = LatestValue()
        self.status.put(EngineStatus(settings))
        self.requests: queue.Queue = queue.Queue()
        self.guard = threading.Event()
        self.quit_event = threading.Event()
        self.thread = threading.Thread(target=self._thread, name="EyePause engine", daemon=True)
        self.pipeline = None
        self.calibration: Calibration | None = None
        self.cal_progress = 0.0
        self._sessions: list[Session] = []
        self._vision: VisionResult | None = None
        self._message = "Choose a player, then start monitoring."
        self._busy = False
        self.command_task = None
        self.started_at = 0.0

    def start(self) -> None:
        self.thread.start()

    def request(self, action: str, value=None) -> None:
        if action in ("stop", "shutdown", "start", "calibrate"):
            # Stop invalidates in-flight commands immediately, including during an await.
            self.guard.clear()
        self.requests.put((action, value))

    def close(self) -> bool:
        self.guard.clear()
        self.quit_event.set()
        if self.pipeline:
            self.pipeline.stop_event.set()
        self.thread.join(timeout=7)
        return not self.thread.is_alive()

    def _thread(self) -> None:
        try:
            asyncio.run(self._run())
        except Exception as error:
            self.guard.clear()
            self.policy.stop()
            self._busy = False
            self._message = f"Application worker stopped: {error}. Restart EyePause."
            self._publish()

    async def _halt(self) -> bool:
        self.guard.clear()
        self.policy.stop()
        if self.command_task is not None:
            self.command_task[1].cancel()
            await asyncio.gather(self.command_task[1], return_exceptions=True)
            self.command_task = None
        self.calibration = None
        self.cal_progress = 0.0
        self._vision = None
        if self.pipeline:
            stopped = await asyncio.to_thread(self.pipeline.stop)
            if not stopped:
                self._message = "Camera driver is still stopping. Wait, or exit EyePause."
                return False
            self.pipeline = None
        return True

    async def _handle(self, action: str, value) -> None:
        if action == "shutdown":
            self.quit_event.set()
        elif action == "stop":
            self._busy = True
            self._publish()
            if await self._halt():
                self._message = "Monitoring stopped. Camera released."
            self._busy = False
        elif action == "settings":
            if self.policy.enabled or self.calibration:
                return
            self.settings = value
            self.policy = PlaybackPolicy(value)
        elif action in ("start", "calibrate"):
            self._busy = True
            self._publish()
            if not await self._halt():
                self._busy = False
                return
            if action == "start":
                player = next((s for s in self._sessions if s.token == value), None)
                if not player or not (player.can_pause or player.can_play):
                    self._message = "Choose a player with supported playback controls first."
                    self._busy = False
                    return
                self.policy.start(value)
                self.policy.update_session(player)
                self._message = "Watching for sustained eye closure."
            else:
                if self.demo:
                    self._message = "Calibration uses your camera. Leave demo mode to calibrate."
                    self._busy = False
                    return
                self.calibration = Calibration()
                self._message = "Step 1 of 2 · Face the camera and keep both eyes open."
            self.started_at = time.monotonic()
            self.guard.set()
            if not self.demo:
                self.pipeline = self.pipeline_factory(self.settings.camera_index)
                self.pipeline.start()
            self._busy = False
        elif action == "calibration_next" and self.calibration and self.calibration.ready:
            if self.calibration.phase == "open":
                self.calibration.advance()
                self.cal_progress = 0.0
                self._message = "Step 2 of 2 · Gently close both eyes for two seconds."
            else:
                try:
                    settings = self.calibration.apply(self.settings)
                    save_settings(settings)
                    self.settings = settings
                    self.policy = PlaybackPolicy(settings)
                    self._message = "Calibration saved. Select a player and start monitoring."
                except (ValueError, OSError) as error:
                    self._message = f"Calibration not saved: {error} Previous settings retained."
                await self._halt()

    async def _refresh(self) -> list[Session]:
        return await self.backend.sessions()

    def _publish(self) -> None:
        self.status.put(
            EngineStatus(
                self.settings,
                tuple(self._sessions),
                self.policy.enabled,
                bool(self.calibration),
                self._busy,
                self.policy.eye_state,
                self.policy.progress,
                self._message,
                self._vision,
                self.policy.owned_pause is not None,
                self.calibration.phase if self.calibration else "",
                self.cal_progress,
                self.calibration.ready if self.calibration else False,
                self.pipeline is not None and not self.demo,
            )
        )

    async def _run(self) -> None:
        refresh_task = None
        next_refresh = 0.0
        last_sample = -1.0
        try:
            while not self.quit_event.is_set():
                now = time.monotonic()
                while not self.requests.empty():
                    action, value = self.requests.get_nowait()
                    await self._handle(action, value)
                    last_sample = -1.0
                if refresh_task is None and now >= next_refresh:
                    refresh_task = asyncio.create_task(self._refresh())
                if refresh_task is not None and refresh_task.done():
                    try:
                        sessions = refresh_task.result()
                        current = {s.token: s for s in self._sessions}
                        self._sessions = [
                            current.get(s.token, s)
                            if (s.token in current and current[s.token].revision > s.revision)
                            else s
                            for s in sessions
                        ]
                        if self.policy.enabled:
                            selected = next(
                                (s for s in self._sessions if s.token == self.policy.target), None
                            )
                            self.policy.update_session(selected)
                            if not self.policy.enabled:
                                message = self.policy.message
                                await self._halt()
                                self._message = message
                    except Exception as error:
                        await self._halt()
                        self._sessions = []
                        self._message = f"Media sessions unavailable: {error}"
                    refresh_task = None
                    next_refresh = now + 0.75
                if self.command_task is not None and self.command_task[1].done():
                    command, task = self.command_task
                    try:
                        result = task.result()
                        self.policy.complete(command, result)
                        if result.session:
                            self._sessions = [
                                result.session if s.token == result.session.token else s
                                for s in self._sessions
                            ]
                        if self.policy.enabled:
                            self._message = self.policy.message
                    except Exception as error:
                        from eyepause.domain import CommandResult

                        self.policy.complete(command, CommandResult(False, message=str(error)))
                        self._message = self.policy.message
                    self.command_task = None
                if self.pipeline and self.pipeline.errors.get():
                    message = self.pipeline.errors.get()
                    await self._halt()
                    self._message = message
                if self.policy.enabled or self.calibration:
                    result = (
                        VisionResult(demo_observation(now - self.started_at, now))
                        if self.demo
                        else self.pipeline.results.get()
                        if self.pipeline
                        else None
                    )
                    if result and now - result.observation.timestamp > 0.75:
                        result = VisionResult(
                            Observation(now, reason="Waiting for fresh camera frames")
                        )
                    if result and result.observation.timestamp > last_sample:
                        last_sample = result.observation.timestamp
                        self._vision = result
                        if self.calibration:
                            self.cal_progress = self.calibration.add(result.observation)
                        else:
                            command = self.policy.observe(result.observation)
                            if command and self.command_task is None:
                                guard = self.guard

                                def valid(c=command, run_guard=guard):
                                    return (
                                        run_guard.is_set()
                                        and self.policy.pending == c
                                        and self.policy.generation == c.generation
                                        and not self.quit_event.is_set()
                                    )

                                task = asyncio.create_task(self.backend.execute(command, valid))
                                self.command_task = (command, task)
                            self._message = self.policy.message
                self._publish()
                await asyncio.sleep(0.03)
        finally:
            self.guard.clear()
            await self._halt()
            tasks = [
                t for t in (refresh_task, self.command_task[1] if self.command_task else None) if t
            ]
            for task in tasks:
                task.cancel()
            if tasks:
                await asyncio.gather(*tasks, return_exceptions=True)
            await self.backend.close()
