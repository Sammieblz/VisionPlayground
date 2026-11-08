"""Simulator-independent observations and illustrative warning rules.

Gap is bumper-to-bumper metres; speed and closing speed are metres per second.
Positive closing speed means the gap is shrinking. These are demonstration
thresholds, not calibrated driver-assistance safety decisions.
"""

import math
from dataclasses import asdict, dataclass


def number(value: object, name: str, *, nonnegative: bool = True) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"{name} must be a finite number")
    value = float(value)
    if not math.isfinite(value) or (nonnegative and value < 0):
        raise ValueError(f"{name} must be finite" + (" and nonnegative" if nonnegative else ""))
    return value


@dataclass(frozen=True)
class WorldState:
    timestamp: float
    speed_mps: float
    gap_m: float | None = None
    closing_mps: float | None = None

    def __post_init__(self) -> None:
        number(self.timestamp, "timestamp")
        number(self.speed_mps, "speed_mps")
        if (self.gap_m is None) != (self.closing_mps is None):
            raise ValueError("gap_m and closing_mps must both be numbers or both be null")
        if self.gap_m is not None:
            number(self.gap_m, "gap_m")
            number(self.closing_mps, "closing_mps", nonnegative=False)


@dataclass(frozen=True)
class Warning:
    level: str
    message: str
    ttc_s: float | None
    headway_s: float | None


def evaluate(state: WorldState) -> Warning:
    if state.gap_m is None:
        return Warning("unknown", "No lead vehicle in this adapter's coverage", None, None)
    ttc = state.gap_m / state.closing_mps if state.closing_mps > 0 else None
    headway = state.gap_m / state.speed_mps if state.speed_mps > 0 else None
    if ttc is not None and ttc < 2.0:
        return Warning("urgent", "Closing rapidly on the vehicle ahead", ttc, headway)
    if state.speed_mps >= 2.0 and headway is not None and headway < 1.5:
        return Warning("caution", "Following gap is short", ttc, headway)
    return Warning("quiet", "No following warning triggered", ttc, headway)


def analyze(manifest: object, *, demo: bool = False) -> dict:
    if not isinstance(manifest, dict):
        raise ValueError("Input must be a JSON object")
    if type(manifest.get("schema_version")) is not int or manifest["schema_version"] != 1:
        raise ValueError("schema_version must be 1")
    synthetic = manifest.get("synthetic", False)
    if type(synthetic) is not bool:
        raise ValueError("synthetic must be a boolean")
    frames = manifest.get("frames")
    if not isinstance(frames, list) or not frames:
        raise ValueError("frames must be a nonempty array")
    results = []
    previous = -1.0
    for index, frame in enumerate(frames):
        if not isinstance(frame, dict):
            raise ValueError(f"frames[{index}] must be an object")
        if set(frame) - {"timestamp", "speed_mps", "gap_m", "closing_mps"}:
            raise ValueError(f"frames[{index}] contains unknown fields")
        if not {"timestamp", "speed_mps"} <= set(frame):
            raise ValueError(f"frames[{index}] requires timestamp and speed_mps")
        state = WorldState(**frame)
        if state.timestamp <= previous:
            raise ValueError("Frame timestamps must strictly increase")
        previous = state.timestamp
        results.append({"state": asdict(state), "warning": asdict(evaluate(state))})
    return {
        "project": "codriversim",
        "mode": "synthetic_demo" if demo else "imported_observations",
        "synthetic": synthetic,
        "model_inference": False,
        "vehicle_control": False,
        "result": {"frames": results},
    }
