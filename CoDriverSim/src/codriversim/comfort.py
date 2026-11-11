"""Advisory motion evidence from permitted ego measurements and camera tracks.

Thresholds are illustrative comfort heuristics, not vehicle safety limits. A
monocular range is uncertain; late-braking advice requires sustained closing
and still uses the far end of its supplied range interval.
"""

from codriversim.contracts import RiskEvent

GROUND_HAZARDS = {
    "car",
    "bus",
    "truck",
    "train",
    "motorcycle",
    "person",
    "cat",
    "dog",
    "horse",
    "sheep",
    "cow",
    "elephant",
    "bear",
    "zebra",
    "giraffe",
}


class MotionEvidence:
    def __init__(self):
        self.session = None
        self.previous_at = -1.0
        self.accelerating_since = None
        self.braking_since = None
        self.tracks = {}

    def update(self, frame, tracks):
        stamp, ego = frame.timestamp, frame.ego
        continuous = self.session == frame.session_id and 0 < stamp - self.previous_at <= 0.6
        if not continuous or ego.source == "unavailable":
            self.accelerating_since = self.braking_since = None
            self.tracks.clear()
        self.session, self.previous_at = frame.session_id, stamp
        if ego.source == "unavailable":
            return []
        risks = []
        if ego.acceleration_mps2 > 3 and ego.speed_mps > 2:
            if self.accelerating_since is None:
                self.accelerating_since = stamp
            if stamp - self.accelerating_since >= 0.6:
                risks.append(
                    RiskEvent(
                        stamp,
                        "advisory",
                        "rapid_acceleration",
                        "Accelerate more gently",
                        source="ego_sensors",
                        provenance=f"comfort-v1;source={ego.source};accel={ego.acceleration_mps2:.2f}",
                        frame_id=frame.frame_id,
                    )
                )
        else:
            self.accelerating_since = None
        if ego.acceleration_mps2 < -3.5 and ego.speed_mps > 2:
            if self.braking_since is None:
                self.braking_since = stamp
            if stamp - self.braking_since >= 0.3:
                risks.append(
                    RiskEvent(
                        stamp,
                        "advisory",
                        "harsh_braking",
                        "Brake more smoothly when safe",
                        source="ego_sensors",
                        provenance=f"comfort-v1;source={ego.source};accel={ego.acceleration_mps2:.2f}",
                        frame_id=frame.frame_id,
                    )
                )
        else:
            self.braking_since = None
        current = {}
        for track in tracks:
            centre = (track.box[0] + track.box[2]) / 2 / frame.width
            distance = track.distance_m
            if track.label not in GROUND_HAZARDS or not 0.35 < centre < 0.65 or not distance:
                continue
            old = self.tracks.get(track.track_id)
            closing = (old[1] - distance) / (stamp - old[0]) if old else 0
            count = old[2] + 1 if old and closing > 2 else 0
            current[track.track_id] = stamp, distance, count
            far_distance = distance + (track.distance_uncertainty_m or distance)
            if (
                count >= 2
                and ego.speed_mps > 4
                and ego.acceleration_mps2 > -2
                and far_distance / closing < 3
                and closing * closing / (2 * far_distance) > 2.5
            ):
                risks.append(
                    RiskEvent(
                        stamp,
                        "caution",
                        "late_braking",
                        "Brake earlier. Check the hazard ahead",
                        (track.track_id,),
                        True,
                        "camera+ego_sensors",
                        f"closing-three-frame-v1;uncertain-ground-range;far_m={far_distance:.2f};"
                        f"closing_mps={closing:.2f};accel={ego.acceleration_mps2:.2f}",
                        frame.frame_id,
                    )
                )
        self.tracks = current
        return risks
