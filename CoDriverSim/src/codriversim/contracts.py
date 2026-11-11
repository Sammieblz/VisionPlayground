"""Portable camera-only contracts. No simulator imports or actuation fields.

Wire v2 is separate from the original v1 observation manifest. Timestamps are
monotonic capture seconds within a session; pixel boxes use exclusive maxima.
Camera coordinates: right, down, forward. Ego: forward, left, up (SI units).
"""

from dataclasses import asdict, dataclass, field

from codriversim.domain import number

VERSION = 2


def fields(value: object, allowed: set[str], required: set[str], name: str) -> dict:
    if not isinstance(value, dict) or set(value) - allowed or not required <= set(value):
        raise ValueError(f"Invalid {name} fields")
    return value


@dataclass(frozen=True)
class Calibration:
    fx: float
    fy: float
    cx: float
    cy: float
    height_m: float = 1.22
    pitch_deg: float = 0.0
    source: str = "pinhole"

    def __post_init__(self):
        for key in ("fx", "fy", "height_m"):
            if number(getattr(self, key), key) == 0:
                raise ValueError(f"{key} must be positive")
        for key in ("cx", "cy", "pitch_deg"):
            number(getattr(self, key), key, nonnegative=False)
        if self.source not in {"pinhole", "measured", "uncalibrated"}:
            raise ValueError("Unknown calibration source")


@dataclass(frozen=True)
class EgoTelemetry:
    speed_mps: float = 0.0
    steering_deg: float = 0.0
    acceleration_mps2: float = 0.0
    yaw_rate_rps: float = 0.0
    latitude: float = 37.0
    longitude: float = -122.0
    bearing_deg: float = 0.0
    source: str = "unavailable"
    # Optional measured IMU, in device axes: forward, right, down. Specific
    # force includes the accelerometer's gravity response (stationary Z=-g).
    specific_force_device_mps2: tuple[float, float, float] | None = None
    angular_velocity_device_rps: tuple[float, float, float] | None = None

    def __post_init__(self):
        for key, val in asdict(self).items():
            if key in {"source", "specific_force_device_mps2", "angular_velocity_device_rps"}:
                continue
            number(val, key, nonnegative=key == "speed_mps")
        for key in ("specific_force_device_mps2", "angular_velocity_device_rps"):
            values = getattr(self, key)
            if values is not None:
                if not isinstance(values, (tuple, list)) or len(values) != 3:
                    raise ValueError("IMU measurements require three device-axis components")
                for value in values:
                    number(value, key, nonnegative=False)
                object.__setattr__(self, key, tuple(values))
        if self.source not in {"unavailable", "simulated_sensors", "recorded_sensors"}:
            raise ValueError("Unknown telemetry source")
        if not -90 <= self.latitude <= 90 or not -180 <= self.longitude <= 180:
            raise ValueError("GPS outside geographic bounds")


@dataclass(frozen=True)
class RouteContext:
    instruction: str = "Explore the town"
    source: str = "navigation_map"

    def __post_init__(self):
        if self.source != "navigation_map" or not isinstance(self.instruction, str):
            raise ValueError("Route must be explicitly identified as navigation map information")


@dataclass(frozen=True)
class CameraPose:
    """Rigid camera-to-ego transform; ego axes are forward, left, up."""

    position_ego_m: tuple[float, float, float] = (0.0, 0.0, 0.0)
    rotation: tuple[float, ...] = (0, 0, 1, -1, 0, 0, 0, -1, 0)
    source: str = "uncalibrated"

    def __post_init__(self):
        if len(self.position_ego_m) != 3 or len(self.rotation) != 9:
            raise ValueError("Camera pose requires translation and a 3x3 rotation")
        object.__setattr__(self, "position_ego_m", tuple(self.position_ego_m))
        object.__setattr__(self, "rotation", tuple(self.rotation))
        for value in (*self.position_ego_m, *self.rotation):
            number(value, "camera pose", nonnegative=False)
        if self.source not in {"uncalibrated", "measured", "simulated_mount"}:
            raise ValueError("Unknown camera pose source")
        r = self.rotation
        for i in range(3):
            for j in range(3):
                if abs(sum(r[i * 3 + k] * r[j * 3 + k] for k in range(3)) - (i == j)) > 1e-4:
                    raise ValueError("Camera rotation must be orthonormal")
        determinant = (
            r[0] * (r[4] * r[8] - r[5] * r[7])
            - r[1] * (r[3] * r[8] - r[5] * r[6])
            + r[2] * (r[3] * r[7] - r[4] * r[6])
        )
        if abs(determinant - 1) > 1e-4:
            raise ValueError("Camera rotation must preserve orientation")


@dataclass(frozen=True)
class CameraFrame:
    session_id: str
    frame_id: int
    timestamp: float
    width: int
    height: int
    calibration: Calibration
    ego: EgoTelemetry
    image: bytes = field(repr=False)
    route: RouteContext = field(default_factory=RouteContext)
    encoding: str = "jpeg"
    schema_version: int = VERSION
    pose: CameraPose = field(default_factory=CameraPose)

    def __post_init__(self):
        if self.schema_version != VERSION or type(self.schema_version) is not int:
            raise ValueError("Camera schema_version must be 2")
        if not isinstance(self.session_id, str) or not 1 <= len(self.session_id) <= 80:
            raise ValueError("Invalid session identifier")
        if type(self.frame_id) is not int or self.frame_id < 0:
            raise ValueError("Invalid frame identifier")
        number(self.timestamp, "timestamp")
        if any(type(v) is not int or not 32 <= v <= 4096 for v in (self.width, self.height)):
            raise ValueError("Invalid image dimensions")
        if (
            self.encoding not in {"jpeg", "rgb24", "rgb24-bottom-up"}
            or not self.image
            or len(self.image) > 8_000_000
        ):
            raise ValueError("Expected bounded JPEG or RGB24 payload")
        if self.encoding.startswith("rgb24") and len(self.image) != self.width * self.height * 3:
            raise ValueError("RGB24 payload dimensions do not match")

    def metadata(self) -> dict:
        return {k: v for k, v in asdict(self).items() if k != "image"}

    @classmethod
    def parse(cls, header: dict, image: bytes):
        allowed = set(cls.__dataclass_fields__) - {"image"}
        data = fields(header, allowed, allowed - {"route", "pose"}, "camera header").copy()
        for key, model in (("calibration", Calibration), ("ego", EgoTelemetry)):
            spec = fields(data[key], set(model.__dataclass_fields__), set(), key)
            try:
                data[key] = model(**spec)
            except TypeError as exc:
                raise ValueError(f"Invalid {key}") from exc
        route = data.get("route", {})
        fields(route, {"instruction", "source"}, set(), "route")
        data["route"] = RouteContext(**route)
        pose = fields(
            data.get("pose", {}), set(CameraPose.__dataclass_fields__), set(), "camera pose"
        )
        data["pose"] = CameraPose(**pose)
        return cls(image=image, **data)


@dataclass(frozen=True)
class Track:
    track_id: int
    label: str
    box: tuple[float, float, float, float]
    score: float
    distance_m: float | None = None
    distance_uncertainty_m: float | None = None
    signal_state: str = "unknown"
    speed_limit_mph: int | None = None
    source: str = "camera"
    provenance: str = ""


@dataclass(frozen=True)
class RiskEvent:
    timestamp: float
    severity: str
    kind: str
    message: str
    evidence: tuple[int, ...] = ()
    record: bool = False
    source: str = "camera"
    provenance: str = ""
    frame_id: int = -1


@dataclass(frozen=True)
class PerceptionSnapshot:
    session_id: str
    frame_id: int
    timestamp: float
    tracks: tuple[Track, ...]
    risks: tuple[RiskEvent, ...]
    latency_ms: float
    model: str
    status: str = "camera inference"
    schema_version: int = VERSION
    vehicle_control: bool = False
    road: dict | None = None
    provenance: dict = field(default_factory=dict)


class FrameOrder:
    """Reject duplicate/reordered frames, rather than silently mixing sessions."""

    def __init__(self):
        self.session = None
        self.frame_id = -1
        self.timestamp = -1.0

    def accept(self, frame: CameraFrame):
        if self.session is not None and frame.session_id != self.session:
            raise ValueError("Session changed on an established connection")
        if frame.frame_id <= self.frame_id or frame.timestamp <= self.timestamp:
            raise ValueError("Frame identifiers and capture timestamps must strictly increase")
        self.session, self.frame_id, self.timestamp = (
            frame.session_id,
            frame.frame_id,
            frame.timestamp,
        )
