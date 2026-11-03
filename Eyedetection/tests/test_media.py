import asyncio
from dataclasses import replace
from types import SimpleNamespace

import pytest

from eyepause.domain import Action, Command, PlaybackStatus
from eyepause.media import DemoBackend, WindowsBackend


class NativePlayer:
    source_app_user_model_id = "same-browser"

    def __init__(self, title="Film"):
        self.title = title
        self.state = "PLAYING"
        self.handlers = []
        self.media_handlers = []
        self.calls = []
        self.accept = True

    def add_playback_info_changed(self, callback):
        self.handlers.append(callback)
        return callback

    def remove_playback_info_changed(self, callback):
        self.handlers.remove(callback)

    def add_media_properties_changed(self, callback):
        self.media_handlers.append(callback)
        return callback

    def remove_media_properties_changed(self, callback):
        self.media_handlers.remove(callback)

    def change(self, status):
        self.state = status
        for handler in self.handlers:
            handler(self, None)

    def get_playback_info(self):
        return SimpleNamespace(
            playback_status=SimpleNamespace(name=self.state),
            controls=(SimpleNamespace(is_pause_enabled=True, is_play_enabled=True)),
        )

    async def try_get_media_properties_async(self):
        return SimpleNamespace(title=self.title, artist="", album_title="", track_number=0)

    async def try_pause_async(self):
        self.calls.append("pause")
        if self.accept:
            self.change("PAUSED")
        return self.accept

    async def try_play_async(self):
        self.calls.append("play")
        self.change("PLAYING")
        return True


def native_backend(players):
    backend = WindowsBackend()
    backend.manager = SimpleNamespace(get_sessions=lambda: players)
    backend.loop = asyncio.get_running_loop()
    return backend


async def test_sessions_from_same_application_have_distinct_stable_tokens():
    players = [NativePlayer("Film 1"), NativePlayer("Film 2")]
    backend = native_backend(players)
    first, second = await backend.sessions()
    assert first.token != second.token
    assert [s.token for s in await backend.sessions()] == [first.token, second.token]
    players.pop()
    assert len(await backend.sessions()) == 1
    await backend.close()
    assert not players[0].handlers


async def test_native_explicit_pause_and_confirmation():
    native = NativePlayer()
    backend = native_backend([native])
    session = (await backend.sessions())[0]
    command = Command(Action.PAUSE, session.token, session.content_id, session.revision, 1)
    result = await backend.execute(command, lambda: True)
    assert result.success and result.session.status == PlaybackStatus.PAUSED
    assert native.calls == ["pause"]
    assert result.session.revision == 1
    await backend.close()


@pytest.mark.parametrize("changed", ["guard", "content", "revision", "status"])
async def test_stale_native_command_is_not_dispatched(changed):
    native = NativePlayer()
    backend = native_backend([native])
    session = (await backend.sessions())[0]
    command = Command(Action.PAUSE, session.token, session.content_id, session.revision, 1)
    if changed == "content":
        native.title = "Next film"
    if changed == "revision":
        native.change("PAUSED")
        native.change("PLAYING")
        await asyncio.sleep(0)
    if changed == "status":
        native.change("PAUSED")
    result = await backend.execute(command, lambda: changed != "guard")
    assert not result.success and not native.calls
    await backend.close()


async def test_native_rejected_command_is_not_confirmed():
    native = NativePlayer()
    native.accept = False
    backend = native_backend([native])
    session = (await backend.sessions())[0]
    result = await backend.execute(
        Command(Action.PAUSE, session.token, session.content_id, session.revision, 1), lambda: True
    )
    assert not result.success
    assert native.calls == ["pause"]
    await backend.close()


async def test_transport_events_between_polls_increment_revision():
    native = NativePlayer()
    backend = native_backend([native])
    await backend.sessions()
    native.change("PAUSED")
    native.change("PLAYING")
    native.change("PAUSED")
    await asyncio.sleep(0)
    assert (await backend.sessions())[0].revision == 3
    await backend.close()


async def test_metadata_events_revoke_resume_even_with_identical_titles():
    native = NativePlayer()
    backend = native_backend([native])
    first = (await backend.sessions())[0]
    for callback in native.media_handlers:
        callback(native, None)
    await asyncio.sleep(0)
    second = (await backend.sessions())[0]
    assert second.content_id == first.content_id
    assert second.revision > first.revision
    await backend.close()


async def test_demo_backend_cancellation_does_not_change_player():
    backend = DemoBackend()
    command = Command(Action.PAUSE, "demo", "demo-film", 0, 1)
    result = await backend.execute(command, lambda: False)
    assert not result.success and not backend.commands


async def test_demo_backend_detects_external_change_during_request():
    backend = DemoBackend()
    task = asyncio.create_task(
        backend.execute(Command(Action.PAUSE, "demo", "demo-film", 0, 1), lambda: True)
    )
    backend.player = replace(backend.player, revision=1)
    assert not (await task).success
