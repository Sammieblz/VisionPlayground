"""State-aware Windows media control. No global media keys or foreground-window input."""

import asyncio
import hashlib
import sys
from dataclasses import dataclass, replace
from uuid import uuid4

from eyepause.domain import Action, Command, CommandResult, PlaybackStatus, Session, ValidityCheck


class DemoBackend:
    """An isolated fake player used by the walkthrough, replay, and tests."""

    def __init__(self) -> None:
        self.player = Session(
            "demo", "Demo player", "A quieter way to watch", "demo-film", PlaybackStatus.PLAYING
        )
        self.commands: list[Action] = []

    async def sessions(self) -> list[Session]:
        return [self.player]

    async def execute(self, command: Command, still_valid: ValidityCheck) -> CommandResult:
        await asyncio.sleep(0.08)
        if not still_valid() or not matches(command, self.player):
            return CommandResult(False, message="Player changed before the command.")
        expected = (
            PlaybackStatus.PLAYING if command.action == Action.PAUSE else PlaybackStatus.PAUSED
        )
        if self.player.status != expected:
            return CommandResult(False, self.player, "Playback changed outside EyePause.")
        status = PlaybackStatus.PAUSED if command.action == Action.PAUSE else PlaybackStatus.PLAYING
        self.player = replace(self.player, status=status, revision=self.player.revision + 1)
        self.commands.append(command.action)
        return CommandResult(True, self.player)

    async def close(self) -> None:
        pass


def matches(command: Command, session: Session) -> bool:
    return (command.session_token, command.content_id, command.revision) == (
        session.token,
        session.content_id,
        session.revision,
    )


@dataclass
class _NativeSession:
    native: object
    token: str
    playback_handler: object = None
    media_handler: object = None
    snapshot: Session | None = None
    revision: int = 0
    last_status: PlaybackStatus | None = None


class WindowsBackend:
    """All async access runs on one engine event loop in an MTA worker thread.

    Object identity distinguishes concurrent sessions from the same application.
    Revision counters retain transport changes even between discovery polls.
    """

    def __init__(self) -> None:
        self.manager = None
        self.entries: dict[str, _NativeSession] = {}
        self.lock = asyncio.Lock()
        self.loop = None

    async def _initialize(self) -> None:
        if self.manager is not None:
            return
        if sys.platform != "win32":
            raise RuntimeError("Live media control requires Windows. Try --demo instead.")
        from winrt.windows.media.control import GlobalSystemMediaTransportControlsSessionManager

        self.loop = asyncio.get_running_loop()
        async with asyncio.timeout(3):
            self.manager = await GlobalSystemMediaTransportControlsSessionManager.request_async()

    @staticmethod
    def _status(native_status: object) -> PlaybackStatus:
        # Avoid binding enum values at import time: demo mode never activates WinRT.
        name = getattr(native_status, "name", "").upper()
        return {
            "PLAYING": PlaybackStatus.PLAYING,
            "PAUSED": PlaybackStatus.PAUSED,
            "STOPPED": PlaybackStatus.STOPPED,
            "CLOSED": PlaybackStatus.STOPPED,
        }.get(name, PlaybackStatus.UNKNOWN)

    def _observe_status(self, entry: _NativeSession, status: PlaybackStatus) -> None:
        if entry.last_status is not None and status != entry.last_status:
            entry.revision += 1
        entry.last_status = status
        if entry.snapshot:
            entry.snapshot = replace(entry.snapshot, status=status, revision=entry.revision)

    @staticmethod
    def _observe_metadata_change(entry: _NativeSession) -> None:
        # Conservatively revoke resume even if content changes away and back
        # between polls, or the player publishes indistinguishable metadata.
        entry.revision += 1
        if entry.snapshot:
            entry.snapshot = replace(entry.snapshot, revision=entry.revision)

    def _register(self, native: object) -> _NativeSession:
        entry = _NativeSession(native, uuid4().hex)

        def playback_changed(sender, _args):
            try:
                status = self._status(sender.get_playback_info().playback_status)
                self.loop.call_soon_threadsafe(self._observe_status, entry, status)
            except (OSError, RuntimeError):
                # A disappearing session is removed by the next discovery pass.
                pass

        entry.playback_handler = native.add_playback_info_changed(playback_changed)

        def metadata_changed(_sender, _args):
            try:
                self.loop.call_soon_threadsafe(self._observe_metadata_change, entry)
            except RuntimeError:
                # A final native callback may race event-loop shutdown.
                pass

        entry.media_handler = native.add_media_properties_changed(metadata_changed)
        self.entries[entry.token] = entry
        return entry

    @staticmethod
    def _unregister(entry: _NativeSession) -> None:
        try:
            entry.native.remove_playback_info_changed(entry.playback_handler)
        except (OSError, RuntimeError):
            pass
        try:
            entry.native.remove_media_properties_changed(entry.media_handler)
        except (OSError, RuntimeError):
            pass

    async def _read(self, entry: _NativeSession) -> Session:
        async with asyncio.timeout(2):
            props = await entry.native.try_get_media_properties_async()
        if props is None:
            raise RuntimeError("Player did not provide media information.")
        playback = entry.native.get_playback_info()
        signature = (props.title, props.artist, props.album_title, props.track_number)
        content_id = hashlib.sha256(repr(signature).encode("utf-8")).hexdigest()
        if entry.snapshot and entry.snapshot.content_id != content_id:
            entry.revision += 1
        status = self._status(playback.playback_status)
        self._observe_status(entry, status)
        controls = playback.controls
        snapshot = Session(
            entry.token,
            entry.native.source_app_user_model_id,
            props.title or "Untitled media",
            content_id,
            status,
            controls.is_pause_enabled,
            controls.is_play_enabled,
            entry.revision,
        )
        entry.snapshot = snapshot
        return snapshot

    async def sessions(self) -> list[Session]:
        await self._initialize()
        native_sessions = list(self.manager.get_sessions())
        active = []
        for native in native_sessions:
            # PyWinRT compares projected objects by COM identity, not the app name.
            entry = next((e for e in self.entries.values() if e.native == native), None)
            active.append(entry or self._register(native))
        active_tokens = {e.token for e in active}
        for token in list(self.entries):
            if token not in active_tokens:
                self._unregister(self.entries.pop(token))
        results = await asyncio.gather(*(self._read(e) for e in active), return_exceptions=True)
        return [result for result in results if isinstance(result, Session)]

    async def execute(self, command: Command, still_valid: ValidityCheck) -> CommandResult:
        async with self.lock:
            try:
                entry = self.entries.get(command.session_token)
                if not entry:
                    return CommandResult(False, message="Selected player disconnected.")
                before = await self._read(entry)
                expected = (
                    PlaybackStatus.PLAYING
                    if command.action == Action.PAUSE
                    else PlaybackStatus.PAUSED
                )
                allowed = before.can_pause if command.action == Action.PAUSE else before.can_play
                if (
                    not still_valid()
                    or not matches(command, before)
                    or before.status != expected
                    or not allowed
                ):
                    return CommandResult(False, before, "Player changed; command cancelled.")
                operation = (
                    entry.native.try_pause_async
                    if command.action == Action.PAUSE
                    else entry.native.try_play_async
                )
                async with asyncio.timeout(2):
                    accepted = await operation()
                if not accepted:
                    return CommandResult(
                        False, message="Player rejected the command. Stop and retry."
                    )
                desired = (
                    PlaybackStatus.PAUSED
                    if command.action == Action.PAUSE
                    else PlaybackStatus.PLAYING
                )
                deadline = asyncio.get_running_loop().time() + 2
                while still_valid():
                    after = await self._read(entry)
                    if (
                        after.content_id != command.content_id
                        or after.revision > before.revision + 1
                    ):
                        break
                    if after.status == desired:
                        return CommandResult(True, after)
                    if asyncio.get_running_loop().time() >= deadline:
                        break
                    await asyncio.sleep(0.08)
                return CommandResult(False, message="Playback was not confirmed. Stop and retry.")
            except (OSError, RuntimeError, TimeoutError) as error:
                return CommandResult(False, message=f"Media control unavailable: {error}")

    async def close(self) -> None:
        for entry in self.entries.values():
            self._unregister(entry)
        self.entries.clear()
        self.manager = None
