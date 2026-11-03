"""Pure playback policy. All state belongs to the engine thread, never the UI."""

import math

from eyepause.domain import (
    Action,
    Command,
    CommandResult,
    EyeState,
    Observation,
    PlaybackStatus,
    Session,
)
from eyepause.settings import Settings
from eyepause.vision import EyeClassifier


class PlaybackPolicy:
    MAX_SAMPLE_GAP = 0.75

    def __init__(self, settings: Settings):
        self.settings = settings
        self.generation = 0
        self.enabled = False
        self.target: str | None = None
        self.session: Session | None = None
        self.pending: Command | None = None
        self.owned_pause: tuple[str, str, int] | None = None
        self.message = "Choose a player, then start monitoring."
        self._reset_tracking()

    def _reset_tracking(self) -> None:
        self.classifier = EyeClassifier(self.settings)
        self.eye_state = EyeState.UNKNOWN
        self.since: float | None = None
        self.last_timestamp: float | None = None
        self.progress = 0.0
        self.attempted_cycle = False

    def start(self, target: str) -> None:
        self.stop()
        self.target = target
        self.enabled = True
        self.message = "Monitoring selected player."

    def stop(self) -> None:
        self.generation += 1
        self.enabled = False
        self.pending = None
        self.owned_pause = None
        self.target = None
        self.session = None
        self._reset_tracking()
        self.message = "Monitoring stopped."

    def update_session(self, session: Session | None) -> None:
        old = self.session
        if old and session and old.token == session.token and session.revision < old.revision:
            return
        self.session = session
        if not self.enabled:
            return
        if session is None or session.token != self.target:
            self.stop()
            self.message = "Selected player disconnected. Select a player and start again."
            return
        if old and (old.token, old.content_id) != (session.token, session.content_id):
            self.generation += 1
            self.pending = None
            self.owned_pause = None
            self._reset_tracking()
            self.message = "Media changed. Previous automatic resume cleared."
        if self.owned_pause is not None:
            expected = (session.token, session.content_id, session.revision)
            if expected != self.owned_pause or session.status != PlaybackStatus.PAUSED:
                self.owned_pause = None
                self.attempted_cycle = True
                self.message = "Playback changed outside EyePause. Waiting for open eyes."

    def observe(self, sample: Observation) -> Command | None:
        if not self.enabled or not math.isfinite(sample.timestamp):
            return None
        if self.last_timestamp is not None and sample.timestamp <= self.last_timestamp:
            return None
        if (
            self.last_timestamp is not None
            and sample.timestamp - self.last_timestamp > self.MAX_SAMPLE_GAP
        ):
            self.since = None
            self.classifier.previous = EyeState.UNKNOWN
        self.last_timestamp = sample.timestamp
        state = self.classifier.classify(sample)
        if state != self.eye_state or self.since is None:
            self.since = sample.timestamp
        self.eye_state = state
        elapsed = sample.timestamp - self.since
        delay = (
            self.settings.pause_seconds
            if state == EyeState.CLOSED
            else self.settings.resume_seconds
        )
        self.progress = min(1.0, elapsed / delay) if state != EyeState.UNKNOWN else 0.0
        if state == EyeState.UNKNOWN:
            self.since = None
            return None
        if state == EyeState.OPEN and elapsed >= self.settings.resume_seconds:
            self.attempted_cycle = False
        session = self.session
        if self.pending is not None or session is None or elapsed + 1e-9 < delay:
            return None
        action = None
        if state == EyeState.CLOSED and not self.attempted_cycle:
            self.attempted_cycle = True
            if session.status == PlaybackStatus.PLAYING and session.can_pause:
                action = Action.PAUSE
            else:
                self.message = "Player is already paused or cannot pause."
        if state == EyeState.OPEN and self.owned_pause is not None:
            if session.status == PlaybackStatus.PAUSED and session.can_play:
                action = Action.PLAY
        if action is None:
            return None
        command = Command(
            action, session.token, session.content_id, session.revision, self.generation
        )
        self.pending = command
        self.message = f"Requesting {action.value}…"
        return command

    def complete(self, command: Command, result: CommandResult) -> None:
        # Completion after Stop/target change can never acquire resume ownership.
        if command != self.pending or command.generation != self.generation or not self.enabled:
            return
        self.pending = None
        session = result.session
        expected = (
            PlaybackStatus.PAUSED if command.action == Action.PAUSE else PlaybackStatus.PLAYING
        )
        valid = (
            result.success
            and session is not None
            and (session.token, session.content_id) == (command.session_token, command.content_id)
            and session.status == expected
        )
        if valid:
            self.session = session
            if command.action == Action.PAUSE:
                self.owned_pause = (session.token, session.content_id, session.revision)
                self.message = "Paused by EyePause. Reopen your eyes to resume."
            else:
                self.owned_pause = None
                self.message = "Eyes open. Playback resumed."
        else:
            self.owned_pause = None
            self.message = result.message or "Playback could not be confirmed. Stop and retry."
            # A failed resume must not be retried on every open-eye frame.
            self.attempted_cycle = True
