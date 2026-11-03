from dataclasses import replace

import pytest

from eyepause.domain import Action, CommandResult, EyeState, Observation, PlaybackStatus, Session
from eyepause.policy import PlaybackPolicy
from eyepause.settings import Settings


@pytest.fixture
def policy():
    result = PlaybackPolicy(Settings())
    result.start("player")
    result.update_session(Session("player", "Browser", "Film", "film", PlaybackStatus.PLAYING))
    return result


def sample(timestamp, ear=0.1, right=None, faces=1):
    return Observation(timestamp, ear, ear if right is None else right, faces)


def feed(policy, start=0.0, end=2.1, ear=0.1):
    commands = []
    for i in range(round((end - start) * 10) + 1):
        command = policy.observe(sample(round(start + i / 10, 6), ear))
        if command:
            commands.append(command)
    return commands


def paused(policy):
    command = feed(policy)[0]
    player = replace(policy.session, status=PlaybackStatus.PAUSED, revision=1)
    policy.complete(command, CommandResult(True, player))
    return command


def test_sustained_closure_and_reopening(policy):
    command = paused(policy)
    assert command.action == Action.PAUSE
    assert policy.owned_pause
    assert not feed(policy, 2.2, 4.0)
    play = feed(policy, 4.1, 4.8, 0.3)
    assert len(play) == 1 and play[0].action == Action.PLAY
    policy.complete(
        play[0],
        CommandResult(True, replace(policy.session, status=PlaybackStatus.PLAYING, revision=2)),
    )
    assert policy.owned_pause is None


def test_normal_blinks_and_winks_do_not_pause(policy):
    assert not feed(policy, 0, 0.2)
    assert not feed(policy, 0.3, 1, 0.3)
    for i in range(11, 51):
        assert policy.observe(sample(i / 10, 0.1, right=0.3)) is None


@pytest.mark.parametrize("faces", [0, 2])
def test_lost_or_multiple_faces_reset_closure_timer(policy, faces):
    assert not feed(policy, 0, 1.8)
    assert policy.observe(sample(1.9, faces=faces)) is None
    assert not feed(policy, 2, 3.8)
    assert len(feed(policy, 3.9, 4.1)) == 1


def test_unknown_tracking_preserves_existing_pause_without_resuming(policy):
    paused(policy)
    for i in range(22, 90):
        assert policy.observe(sample(i / 10, faces=0)) is None
    assert policy.owned_pause
    assert feed(policy, 9, 9.6, 0.3)[0].action == Action.PLAY


def test_already_paused_media_is_never_owned_or_resumed(policy):
    policy.update_session(replace(policy.session, status=PlaybackStatus.PAUSED))
    assert not feed(policy)
    assert not feed(policy, 2.2, 3, 0.3)
    assert not policy.owned_pause


def test_failed_pause_is_attempted_once_per_closure_cycle(policy):
    command = feed(policy)[0]
    policy.complete(command, CommandResult(False, message="rejected"))
    assert not policy.owned_pause
    assert not feed(policy, 2.2, 5)
    assert not feed(policy, 5.1, 6, 0.3)
    assert len(feed(policy, 6.1, 8.3)) == 1


def test_failed_resume_does_not_retry_each_frame(policy):
    paused(policy)
    command = feed(policy, 2.2, 3, 0.3)[0]
    policy.complete(command, CommandResult(False))
    assert not feed(policy, 3.1, 8, 0.3)


def test_stop_invalidates_pending_completion(policy):
    command = feed(policy)[0]
    session = replace(policy.session, status=PlaybackStatus.PAUSED)
    policy.stop()
    policy.complete(command, CommandResult(True, session))
    assert not policy.enabled and not policy.owned_pause


def test_content_change_clears_ownership(policy):
    paused(policy)
    policy.update_session(replace(policy.session, content_id="next-film", revision=2))
    assert not policy.owned_pause
    assert not feed(policy, 2.2, 3, 0.3)


def test_target_disappearance_disarms_instead_of_following_another_player(policy):
    paused(policy)
    policy.update_session(None)
    assert not policy.enabled and policy.target is None
    assert not policy.owned_pause


def test_external_play_does_not_immediately_repause_closed_eyes(policy):
    paused(policy)
    policy.update_session(replace(policy.session, status=PlaybackStatus.PLAYING, revision=2))
    assert not policy.owned_pause
    assert not feed(policy, 2.2, 5)


def test_external_play_pause_between_polls_clears_ownership(policy):
    paused(policy)
    policy.update_session(replace(policy.session, revision=3))
    assert not policy.owned_pause
    assert not feed(policy, 2.2, 3, 0.3)


def test_stale_session_snapshot_does_not_clear_ownership(policy):
    old = policy.session
    paused(policy)
    policy.update_session(old)
    assert policy.owned_pause


def test_duplicate_and_backwards_timestamps_do_not_advance_timer(policy):
    policy.observe(sample(1))
    for timestamp in (1, 1, 0.5, float("nan"), float("inf")):
        assert policy.observe(sample(timestamp)) is None
    assert not feed(policy, 1.1, 2.9)
    assert len(feed(policy, 3, 3.1)) == 1


def test_long_gap_does_not_count_as_closed_time(policy):
    policy.observe(sample(0))
    assert policy.observe(sample(20)) is None
    assert policy.progress == 0


@pytest.mark.parametrize("fps", [5, 15, 30, 60])
def test_pause_delay_is_independent_of_frame_rate(policy, fps):
    seen = []
    for i in range(fps * 3):
        command = policy.observe(sample(i / fps))
        if command:
            seen.append(i / fps)
    assert seen == pytest.approx([2.0])


def test_hysteresis_band_does_not_flicker(policy):
    policy.observe(sample(0, 0.3))
    assert policy.observe(sample(0.1, 0.22)) is None
    assert policy.eye_state == EyeState.OPEN
    policy.observe(sample(0.2, 0.1))
    policy.observe(sample(0.3, 0.22))
    assert policy.eye_state == EyeState.CLOSED
