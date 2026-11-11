"""Read-only adapter for stock Comma model outputs, with strict age/session checks."""

from codriversim.contracts import CameraFrame, RiskEvent, fields
from codriversim.domain import number


def validate_road(value: dict, frame: CameraFrame) -> dict | None:
    allowed = {
        "schema_version",
        "session_id",
        "frame_id",
        "timestamp",
        "model",
        "lanes",
        "lane_probabilities",
        "edges",
        "path",
        "leads",
        "execution_ms",
    }
    fields(value, allowed, allowed, "Comma observation")
    if value["schema_version"] != 2 or value["model"] != "openpilot-v0.9.7-50dd024":
        raise ValueError("Unexpected Comma observation provenance")
    timestamp = number(value["timestamp"], "Comma timestamp")
    if value["session_id"] != frame.session_id or not 0 <= frame.timestamp - timestamp <= 0.5:
        return None
    if type(value["frame_id"]) is not int or not 0 <= value["frame_id"] <= frame.frame_id:
        return None
    number(value["execution_ms"], "Comma inference latency")
    for name in ("lanes", "edges"):
        if not isinstance(value[name], list) or len(value[name]) > 4:
            raise ValueError("Invalid road geometry")
    for geometry in [*value["lanes"], *value["edges"], value["path"]]:
        fields(geometry, {"x", "y", "z"}, {"x", "y", "z"}, "road geometry")
        for axis in geometry.values():
            if not isinstance(axis, list) or len(axis) > 100:
                raise ValueError("Unbounded geometry")
            for coordinate in axis:
                number(coordinate, "coordinate", nonnegative=False)
    for confidence in value["lane_probabilities"]:
        number(confidence, "lane confidence")
    if len(value["lane_probabilities"]) != len(value["lanes"]) or len(value["leads"]) > 3:
        raise ValueError("Inconsistent road outputs")
    for lead in value["leads"]:
        fields(lead, {"x", "y", "v", "prob", "x_std"}, {"x", "y", "v", "prob"}, "model lead")
        number(lead["prob"], "lead confidence")
        for key in ("x", "y", "v", "x_std"):
            values = lead.get(key, [])
            if not isinstance(values, list) or len(values) > 100:
                raise ValueError("Invalid lead trajectory")
            for val in values:
                number(val, "lead coordinate", nonnegative=False)
    return value


def road_risks(road: dict | None, frame: CameraFrame) -> list[RiskEvent]:
    if road is None:
        return []
    risks = []
    if frame.ego.speed_mps > 5 and len(road["lanes"]) >= 3:
        for idx in (1, 2):
            lane = road["lanes"][idx]
            if road["lane_probabilities"][idx] > 0.7 and lane["y"] and abs(lane["y"][0]) < 0.6:
                risks.append(
                    RiskEvent(
                        frame.timestamp,
                        "caution",
                        "lane_departure",
                        "Approaching a visible lane boundary",
                    )
                )
    for lead in road["leads"]:
        if lead["prob"] > 0.7 and lead["x"] and 0 < lead["x"][0] < frame.ego.speed_mps * 1.5:
            risks.append(
                RiskEvent(
                    frame.timestamp,
                    "caution",
                    "following",
                    "Comma camera model suggests a short following gap",
                )
            )
            break
    return risks
