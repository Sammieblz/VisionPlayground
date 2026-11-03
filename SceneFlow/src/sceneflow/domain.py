"""Pixel-coordinate tracks and directed-boundary events."""

from dataclasses import dataclass

Point = tuple[float, float]
Box = tuple[float, float, float, float]


@dataclass(frozen=True)
class Track:
    track_id: str
    box: Box


@dataclass(frozen=True)
class Frame:
    timestamp: float
    tracks: tuple[Track, ...]


@dataclass(frozen=True)
class DirectedLine:
    start: Point
    end: Point


@dataclass(frozen=True)
class Crossing:
    track_id: str
    timestamp: float
    direction: str


@dataclass(frozen=True)
class TrackAnalysis:
    negative_to_positive: int
    positive_to_negative: int
    events: tuple[Crossing, ...]
