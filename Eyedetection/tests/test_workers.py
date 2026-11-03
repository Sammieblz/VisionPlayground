import threading
import time

import numpy as np

from eyepause.camera import CameraPipeline, LatestValue, VisionResult
from eyepause.domain import Observation
from eyepause.engine import Engine
from eyepause.media import DemoBackend
from eyepause.settings import Settings


def wait_until(predicate, timeout=3):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return
        time.sleep(0.01)
    raise AssertionError("Worker did not reach the expected state")


class Capture:
    def __init__(self, opened=True, disconnect=False):
        self.opened = opened
        self.disconnect = disconnect
        self.released = False

    def isOpened(self):
        return self.opened

    def set(self, *args):
        return True

    def read(self):
        time.sleep(0.005)
        return (False, None) if self.disconnect else (True, np.zeros((20, 20, 3), np.uint8))

    def release(self):
        self.released = True


class Detector:
    def __init__(self):
        self.closed = False

    def detect(self, rgb, timestamp):
        time.sleep(0.03)  # Intentionally slower than capture: results must not queue.
        return VisionResult(Observation(timestamp, 0.3, 0.3, 1), rgb)

    def close(self):
        self.closed = True


def test_camera_permission_failure_releases_device():
    capture = Capture(opened=False)
    pipeline = CameraPipeline(0, capture_factory=lambda _: capture, detector_factory=Detector)
    pipeline.start()
    wait_until(lambda: pipeline.errors.get())
    assert pipeline.stop() and capture.released
    assert "permissions" in pipeline.errors.get()


def test_camera_disconnection_is_reported_and_released():
    capture = Capture(disconnect=True)
    pipeline = CameraPipeline(0, capture_factory=lambda _: capture, detector_factory=Detector)
    pipeline.start()
    wait_until(lambda: pipeline.errors.get())
    assert "disconnected" in pipeline.errors.get()
    assert pipeline.stop() and capture.released


def test_latest_frame_pipeline_drops_backlog_and_stops_cleanly():
    capture, detector = Capture(), Detector()
    pipeline = CameraPipeline(
        0, capture_factory=lambda _: capture, detector_factory=lambda: detector
    )
    pipeline.start()
    wait_until(lambda: pipeline.results.get())
    time.sleep(0.1)
    assert time.monotonic() - pipeline.results.get().observation.timestamp < 0.15
    assert pipeline.stop()
    assert capture.released and detector.closed


def test_latest_mailbox_keeps_only_newest_value():
    latest = LatestValue()
    for i in range(1000):
        latest.put(i)
    assert latest.get() == 999


def test_demo_engine_never_opens_camera_and_stops_cleanly():
    backend = DemoBackend()
    engine = Engine(
        Settings(),
        demo=True,
        backend=backend,
        pipeline_factory=lambda _: (_ for _ in ()).throw(AssertionError("Camera opened")),
    )
    engine.start()
    try:
        wait_until(lambda: engine.status.get().sessions)
        engine.request("start", "demo")
        wait_until(lambda: engine.status.get().monitoring)
        engine.request("stop")
        assert not engine.guard.is_set()
        wait_until(lambda: not engine.status.get().monitoring)
    finally:
        assert engine.close()
    assert not backend.commands


def test_sensor_failure_disarms_monitoring_without_media_commands():
    class FailedPipeline:
        def __init__(self, _index):
            self.results, self.errors = LatestValue(), LatestValue()
            self.stop_event = threading.Event()

        def start(self):
            self.errors.put("Camera permission denied")

        def stop(self):
            return True

    backend = DemoBackend()
    engine = Engine(Settings(), backend=backend, pipeline_factory=FailedPipeline)
    engine.start()
    try:
        wait_until(lambda: engine.status.get().sessions)
        engine.request("start", "demo")
        wait_until(lambda: "permission denied" in engine.status.get().message)
        assert not engine.status.get().monitoring
        assert not engine.guard.is_set() and not backend.commands
    finally:
        assert engine.close()


def test_engine_executes_one_complete_pause_resume_cycle(monkeypatch):
    from eyepause.domain import Action

    monkeypatch.setattr(
        "eyepause.engine.demo_observation",
        lambda elapsed, now: Observation(
            now, 0.1 if elapsed < 0.8 else 0.3, 0.1 if elapsed < 0.8 else 0.3, 1
        ),
    )
    backend = DemoBackend()
    engine = Engine(Settings(pause_seconds=0.5, resume_seconds=0.2), demo=True, backend=backend)
    engine.start()
    try:
        wait_until(lambda: engine.status.get().sessions)
        engine.request("start", "demo")
        wait_until(lambda: len(backend.commands) == 2 and not engine.status.get().owned_pause)
        assert backend.commands == [Action.PAUSE, Action.PLAY]
        assert not engine.status.get().owned_pause
    finally:
        assert engine.close()


def test_stop_cancels_pending_command_without_ownership(monkeypatch):
    import asyncio

    monkeypatch.setattr(
        "eyepause.engine.demo_observation", lambda elapsed, now: Observation(now, 0.1, 0.1, 1)
    )

    class SlowBackend(DemoBackend):
        def __init__(self):
            super().__init__()
            self.requested = threading.Event()
            self.cancelled = threading.Event()

        async def execute(self, command, still_valid):
            self.requested.set()
            try:
                await asyncio.sleep(60)
            except asyncio.CancelledError:
                self.cancelled.set()
                raise

    backend = SlowBackend()
    engine = Engine(Settings(pause_seconds=0.5), demo=True, backend=backend)
    engine.start()
    try:
        wait_until(lambda: engine.status.get().sessions)
        engine.request("start", "demo")
        wait_until(backend.requested.is_set)
        engine.request("stop")
        wait_until(backend.cancelled.is_set)
        wait_until(lambda: not engine.status.get().monitoring)
        assert not engine.status.get().owned_pause
        assert not backend.commands
    finally:
        assert engine.close()


def test_preferences_are_rejected_while_monitoring_without_disarming_commands():
    engine = Engine(Settings(), demo=True)
    engine.start()
    try:
        wait_until(lambda: engine.status.get().sessions)
        engine.request("start", "demo")
        wait_until(lambda: engine.status.get().monitoring)
        engine.request("settings", Settings(pause_seconds=3.0))
        time.sleep(0.1)
        assert engine.guard.is_set()
        assert engine.status.get().settings.pause_seconds == 2.0
    finally:
        assert engine.close()
