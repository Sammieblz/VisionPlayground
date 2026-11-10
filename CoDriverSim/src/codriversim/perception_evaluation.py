"""Offline labelled-camera evaluation. Truth stays outside the observer process."""

import math
from collections import Counter, defaultdict

from codriversim.evaluation import detection_metrics


def _number(value, name):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value):
        raise ValueError(f"Invalid {name}")
    return value


def _box(box):
    if not isinstance(box, (list, tuple)) or len(box) != 4:
        raise ValueError("Expected a four-coordinate pixel box")
    x0, y0, x1, y1 = [_number(v, "box coordinate") for v in box]
    if x0 < 0 or y0 < 0 or x1 <= x0 or y1 <= y0:
        raise ValueError("Invalid exclusive pixel box")
    return x0, y0, x1, y1


def _iou(a, b):
    a, b = _box(a), _box(b)
    intersection = max(0, min(a[2], b[2]) - max(a[0], b[0])) * max(
        0, min(a[3], b[3]) - max(a[1], b[1])
    )
    return intersection / (
        (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - intersection
    )


def _key(frame):
    session, frame_id = frame["session_id"], frame["frame_id"]
    if not isinstance(session, str) or not session or type(frame_id) is not int or frame_id < 0:
        raise ValueError("Frame requires a session and nonnegative integer ID")
    return session, frame_id


def evaluate_camera(labels: dict, predictions: list[dict], *, iou_threshold=0.5) -> dict:
    """Match class/IoU one-to-one; count missing inference as missed detections.

    Labels need complete object annotations for each selected frame. Hazard labels
    are separate time windows; warning accuracy is only scored when their coverage
    is explicitly declared complete. Signal unknowns never count as correct colors.
    """
    if labels.get("schema_version") != 2 or labels.get("annotations_complete") is not True:
        raise ValueError("Evaluation requires complete v2 held-out frame annotations")
    if not 0 < _number(iou_threshold, "IoU threshold") <= 1:
        raise ValueError("IoU threshold must be in (0, 1]")
    frames = labels.get("frames")
    if not isinstance(frames, list) or not frames:
        raise ValueError("No annotated frames")
    indexed = {}
    for prediction in predictions:
        key = _key(prediction)
        if key in indexed:
            raise ValueError("Duplicate prediction frame")
        indexed[key] = prediction
    seen, counts = set(), defaultdict(Counter)
    previous_tracks = {}
    switches = signal_correct = signal_total = signal_unknown = matched_frames = 0
    speed_correct = speed_total = 0
    latencies = []
    warnings = []
    matched_objects = {}
    last_time = {}
    for frame in sorted(frames, key=lambda f: (f["session_id"], f["timestamp"])):
        key = _key(frame)
        if key in seen:
            raise ValueError("Duplicate annotation frame")
        seen.add(key)
        timestamp = _number(frame["timestamp"], "annotation time")
        if timestamp < 0 or timestamp <= last_time.get(key[0], -1):
            raise ValueError("Annotation timestamps must increase within each session")
        last_time[key[0]] = timestamp
        prediction = indexed.get(key)
        if prediction is not None:
            if abs(_number(prediction["timestamp"], "prediction time") - timestamp) > 0.01:
                raise ValueError("Prediction and annotation capture times disagree")
            matched_frames += 1
            latency = _number(prediction["latency_ms"], "latency")
            if latency < 0:
                raise ValueError("Negative latency")
            latencies.append(latency)
            warnings.extend((key[0], timestamp, risk) for risk in prediction.get("risks", []))
        truth = frame.get("objects", [])
        tracks = prediction.get("tracks", []) if prediction else []
        ids = [obj["object_id"] for obj in truth]
        if len(set(ids)) != len(ids):
            raise ValueError("Duplicate labelled object identity in a frame")
        for obj in (*truth, *tracks):
            _box(obj["box"])
            if not isinstance(obj["label"], str) or not obj["label"]:
                raise ValueError("Missing object class")
        # Greedy maximum IoU matching is explicit and reproducible, per class.
        pairs = sorted(
            (
                (_iou(obj["box"], track["box"]), i, j)
                for i, obj in enumerate(truth)
                for j, track in enumerate(tracks)
                if obj["label"] == track["label"]
            ),
            reverse=True,
        )
        used_truth, used_tracks, matches = set(), set(), {}
        for overlap, i, j in pairs:
            if overlap >= iou_threshold and i not in used_truth and j not in used_tracks:
                used_truth.add(i)
                used_tracks.add(j)
                matches[i] = j
                counts[truth[i]["label"]]["tp"] += 1
                identity = key[0], truth[i]["object_id"]
                old = previous_tracks.get(identity)
                if old and timestamp - old[0] <= 1 and old[1] != tracks[j]["track_id"]:
                    switches += 1
                previous_tracks[identity] = timestamp, tracks[j]["track_id"]
                matched_objects[key[0], timestamp, tracks[j]["track_id"]] = truth[i]["object_id"]
        for i, obj in enumerate(truth):
            if i not in used_truth:
                counts[obj["label"]]["fn"] += 1
            track = tracks[matches[i]] if i in matches else {}
            if obj["label"] == "traffic light" and obj.get("signal_state") in {
                "red",
                "yellow",
                "green",
            }:
                signal_total += 1
                state = track.get("signal_state", "unknown")
                signal_correct += state == obj["signal_state"]
                signal_unknown += state not in {"red", "yellow", "green"}
            if obj["label"] == "speed limit sign" and obj.get("speed_limit_mph") is not None:
                speed_total += 1
                speed_correct += track.get("speed_limit_mph") == obj["speed_limit_mph"]
        for j, track in enumerate(tracks):
            if j not in used_tracks:
                counts[track["label"]]["fp"] += 1
    totals = Counter()
    classes = {}
    for label, count in sorted(counts.items()):
        totals.update(count)
        classes[label] = dict(count) | detection_metrics(count["tp"], count["fp"], count["fn"], [])
    hazards = labels.get("hazards", [])
    hazard_results = []
    assigned = set()
    if labels.get("hazards_complete") is True:
        if set(indexed) - seen:
            raise ValueError(
                "Complete hazard scoring needs annotation coverage for all prediction frames"
            )
        for hazard in hazards:
            start = _number(hazard["observable_from"], "hazard start")
            deadline = _number(hazard["deadline"], "hazard deadline")
            end = _number(hazard["end"], "hazard end")
            if not 0 <= start <= deadline <= end:
                raise ValueError("Invalid hazard timing window")
            kinds = hazard.get("acceptable_kinds", [hazard["kind"]])
            if (
                not isinstance(kinds, list)
                or not kinds
                or any(not isinstance(kind, str) or not kind for kind in kinds)
            ):
                raise ValueError("Hazard acceptable_kinds must contain warning kinds")
            candidates = [
                (i, timestamp)
                for i, (session, timestamp, risk) in enumerate(warnings)
                if session == hazard["session_id"]
                and risk["kind"] in kinds
                and start <= timestamp <= end
                and (
                    "object_id" not in hazard
                    or any(
                        matched_objects.get((session, timestamp, track_id)) == hazard["object_id"]
                        for track_id in risk.get("evidence", [])
                    )
                )
            ]
            assigned.update(i for i, _ in candidates)
            first = min((t for _, t in candidates), default=None)
            hazard_results.append(
                {
                    "hazard_id": hazard["hazard_id"],
                    "missed": first is None or first > deadline,
                    "warning_lead_seconds": deadline - first if first is not None else None,
                }
            )
    return {
        "schema_version": 2,
        "mode": "labelled_camera_evaluation",
        "source": labels.get("source", "unspecified"),
        "split": labels.get("split", "unspecified"),
        "iou_threshold": iou_threshold,
        "frames": len(frames),
        "frames_with_inference": matched_frames,
        "counts": dict(totals),
        **detection_metrics(totals["tp"], totals["fp"], totals["fn"], latencies),
        "per_class": classes,
        "tracking_id_switches_within_one_second": switches,
        "signal_accuracy_including_misses": signal_correct / signal_total if signal_total else None,
        "signal_labelled_instances": signal_total,
        "signal_unknown_or_missed": signal_unknown,
        "speed_sign_accuracy_including_misses": speed_correct / speed_total
        if speed_total
        else None,
        "hazard_results": hazard_results if labels.get("hazards_complete") is True else None,
        "unnecessary_warning_samples": len(warnings) - len(assigned)
        if labels.get("hazards_complete") is True
        else None,
        "warning_count_unit": "per annotated frame, not merged warning episodes",
        "real_road_accuracy_established": False,
    }
