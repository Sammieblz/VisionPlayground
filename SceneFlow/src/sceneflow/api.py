"""Count observed crossings; no detector, tracker, camera, or external service is invoked."""

import math
from collections.abc import Sequence

from .domain import Crossing, DirectedLine, Frame, Point, TrackAnalysis
from .validation import box, number, point, text


def analyze_tracks(frames: Sequence[Frame], line: DirectedLine) -> TrackAnalysis:
    """Count track centers crossing a finite directed segment.

    A frame is a complete observation set. Absence in a frame breaks a track's
    continuity. Points on the boundary retain the preceding side; touching and
    returning to the same side is not a crossing. Event time is the timestamp
    of the first observed frame on the opposite side, not a sub-frame estimate.
    """
    start, end = point(line.start, "line.start"), point(line.end, "line.end")
    dx, dy = end[0] - start[0], end[1] - start[1]
    length = math.hypot(dx, dy)
    if length <= 1e-12 or not math.isfinite(length):
        raise ValueError("The directed line must have two distinct finite endpoints.")
    # Unit direction keeps the side tolerance in pixels rather than squared pixels.
    ux, uy = dx / length, dy / length

    def distance(p: Point) -> float:
        return ux * (p[1] - start[1]) - uy * (p[0] - start[0])

    previous: dict[str, tuple[Point, int]] = {}
    events = []
    last_time = -math.inf
    for frame in frames:
        timestamp = number(frame.timestamp, "frame.timestamp")
        if timestamp < 0 or timestamp <= last_time:
            raise ValueError("Frame timestamps must be nonnegative and strictly increasing.")
        last_time = timestamp
        current = {}
        for track in sorted(frame.tracks, key=lambda item: str(item.track_id)):
            track_id = text(track.track_id, "track_id")
            if track_id in current:
                raise ValueError("A track ID may occur only once in each frame.")
            left, top, right, bottom = box(track.box, "track.box")
            center = (left / 2 + right / 2, top / 2 + bottom / 2)
            signed = distance(center)
            side = 1 if signed > 1e-9 else -1 if signed < -1e-9 else 0
            old = previous.get(track_id)
            remembered_side = side
            if old:
                anchor, old_side = old
                if not side:
                    remembered_side = old_side
                elif old_side and side != old_side:
                    before = distance(anchor)
                    denominator = before - signed
                    if abs(denominator) > 1e-12:
                        fraction = before / denominator
                        crossing = (
                            anchor[0] + fraction * (center[0] - anchor[0]),
                            anchor[1] + fraction * (center[1] - anchor[1]),
                        )
                        along = (crossing[0] - start[0]) * ux + (crossing[1] - start[1]) * uy
                        if -1e-9 <= fraction <= 1 + 1e-9 and -1e-9 <= along <= length + 1e-9:
                            direction = (
                                "negative_to_positive" if side > 0 else "positive_to_negative"
                            )
                            events.append(Crossing(track_id, timestamp, direction))
            current[track_id] = (center, remembered_side)
        previous = current
    return TrackAnalysis(
        sum(event.direction == "negative_to_positive" for event in events),
        sum(event.direction == "positive_to_negative" for event in events),
        tuple(events),
    )
