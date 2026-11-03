"""Device-independent observations and media contracts."""

from dataclasses import dataclass
from enum import StrEnum
from typing import Protocol


class EyeState(StrEnum):
    OPEN = "Open"
    CLOSED = "Closed"
    UNKNOWN = "Tracking unavailable"


class PlaybackStatus(StrEnum):
    PLAYING = "Playing"
    PAUSED = "Paused"
    STOPPED = "Stopped"
    UNKNOWN = "Unavailable"


class Action(StrEnum):
    PAUSE = "pause"
    PLAY = "play"


@dataclass(frozen=True)
class Observation:
    timestamp: float
    left_ear: float | None = None
    right_ear: float | None = None
    face_count: int = 0
    reason: str = ""
    processing_ms: float = 0.0


@dataclass(frozen=True)
class Session:
    token: str
    app: str
    title: str
    content_id: str
    status: PlaybackStatus
    can_pause: bool = True
    can_play: bool = True
    # Increments on observable transport changes. It is deliberately not an app ID.
    revision: int = 0


@dataclass(frozen=True)
class Command:
    action: Action
    session_token: str
    content_id: str
    revision: int
    generation: int


@dataclass(frozen=True)
class CommandResult:
    success: bool
    session: Session | None = None
    message: str = ""


class MediaBackend(Protocol):
    async def sessions(self) -> list[Session]: ...

    async def execute(self, command: Command, still_valid: "ValidityCheck") -> CommandResult: ...

    async def close(self) -> None: ...


class ValidityCheck(Protocol):
    def __call__(self) -> bool: ...
