"""Eye geometry, hysteresis, and optional calibration; no UI or camera dependencies."""

import math
from dataclasses import replace
from statistics import median

from eyepause.domain import EyeState, Observation
from eyepause.settings import Settings

# Six ordered eyelid points: corners followed by paired upper/lower lid points.
LEFT_EYE = (33, 160, 158, 133, 153, 144)
RIGHT_EYE = (362, 385, 387, 263, 373, 380)


def eye_aspect_ratio(points: list[tuple[float, float]]) -> float:
    if len(points) != 6 or not all(math.isfinite(v) for point in points for v in point):
        raise ValueError("Six finite eye landmarks are required.")
    width = math.dist(points[0], points[3])
    if width < 1e-6:
        raise ValueError("Eye landmarks have zero width.")
    return (math.dist(points[1], points[5]) + math.dist(points[2], points[4])) / (2 * width)


class EyeClassifier:
    def __init__(self, settings: Settings):
        self.settings = settings
        self.previous = EyeState.UNKNOWN

    def classify(self, sample: Observation) -> EyeState:
        left, right = sample.left_ear, sample.right_ear
        if (
            sample.face_count != 1
            or left is None
            or right is None
            or not all(math.isfinite(x) and 0 <= x <= 1 for x in (left, right))
        ):
            self.previous = EyeState.UNKNOWN
        elif left <= self.settings.left_closed and right <= self.settings.right_closed:
            self.previous = EyeState.CLOSED
        elif left >= self.settings.left_open and right >= self.settings.right_open:
            self.previous = EyeState.OPEN
        elif (left >= self.settings.left_open and right <= self.settings.right_closed) or (
            right >= self.settings.right_open and left <= self.settings.left_closed
        ):
            # A wink cannot inherit a previous bilateral closed state.
            self.previous = EyeState.UNKNOWN
        else:
            # Preserve the state only inside BOTH eyes' hysteresis bands, or when
            # the other eye remains consistent with that state.
            if self.previous == EyeState.CLOSED and (
                left >= self.settings.left_open or right >= self.settings.right_open
            ):
                self.previous = EyeState.UNKNOWN
            if self.previous == EyeState.OPEN and (
                left <= self.settings.left_closed or right <= self.settings.right_closed
            ):
                self.previous = EyeState.UNKNOWN
        return self.previous


class Calibration:
    """Collect contiguous valid samples; callers explicitly advance each guided step."""

    def __init__(self) -> None:
        self.phase = "open"
        self.samples: dict[str, list[Observation]] = {"open": [], "closed": []}
        self.started: float | None = None
        self.last: float | None = None
        self.ready = False

    @property
    def duration(self) -> float:
        return 3.0 if self.phase == "open" else 2.0

    def add(self, sample: Observation) -> float:
        if self.ready:
            return 1.0
        if self.last is not None and sample.timestamp <= self.last:
            return 0.0
        valid = (
            sample.face_count == 1
            and sample.left_ear is not None
            and sample.right_ear is not None
            and all(math.isfinite(x) and 0 < x < 1 for x in (sample.left_ear, sample.right_ear))
        )
        if not valid or (self.last is not None and sample.timestamp - self.last > 0.75):
            self.samples[self.phase].clear()
            self.started = None
        self.last = sample.timestamp
        if not valid:
            return 0.0
        if self.started is None:
            self.started = sample.timestamp
        self.samples[self.phase].append(sample)
        progress = min(1.0, (sample.timestamp - self.started) / self.duration)
        self.ready = progress >= 1.0
        return progress

    def advance(self) -> None:
        if self.phase != "open" or not self.ready:
            raise ValueError("Finish the open-eye step first.")
        self.phase = "closed"
        self.started = self.last = None
        self.ready = False

    def apply(self, settings: Settings) -> Settings:
        if self.phase != "closed" or not self.ready:
            raise ValueError("Finish both calibration steps first.")
        thresholds = {}
        for eye in ("left", "right"):
            opened = median(getattr(s, f"{eye}_ear") for s in self.samples["open"])
            closed = median(getattr(s, f"{eye}_ear") for s in self.samples["closed"])
            if opened - closed < 0.05:
                raise ValueError("Eyes did not separate clearly. Improve lighting and try again.")
            gap = opened - closed
            thresholds[f"{eye}_closed"] = closed + 0.35 * gap
            thresholds[f"{eye}_open"] = closed + 0.65 * gap
        return replace(settings, **thresholds)
