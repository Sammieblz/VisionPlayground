"""Camera-only CPU perception; no game state, simulation, or control dependencies.

NanoDet decoding follows OpenCV Zoo (Apache-2.0), pinned in assets-manifest.json.
Ranges are ground-plane monocular proposals, not calibrated measurements.
"""

import hashlib
import time
from dataclasses import replace
from pathlib import Path

from codriversim.comfort import GROUND_HAZARDS, MotionEvidence
from codriversim.contracts import CameraFrame, PerceptionSnapshot, RiskEvent, Track
from codriversim.visual_cues import VisualCues

CLASSES = {
    0: "person",
    2: "car",
    3: "motorcycle",
    5: "bus",
    6: "train",
    7: "truck",
    9: "traffic light",
    11: "stop sign",
    14: "bird",
    15: "cat",
    16: "dog",
    17: "horse",
    18: "sheep",
    19: "cow",
    20: "elephant",
    21: "bear",
    22: "zebra",
    23: "giraffe",
}


def iou(a, b):
    overlap = max(0, min(a[2], b[2]) - max(a[0], b[0])) * max(0, min(a[3], b[3]) - max(a[1], b[1]))
    union = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - overlap
    return overlap / union if union > 0 else 0


class Tracker:
    def __init__(self):
        self.recent = {}
        self.next_id = 1
        self.timestamp = -1

    def update(self, tracks, timestamp):
        # Retain identities across skipped detector cycles, but never emit a
        # remembered object as a current observation. OCR runs at a lower rate.
        if timestamp <= self.timestamp:
            self.recent.clear()
        self.recent = {
            ident: entry for ident, entry in self.recent.items() if timestamp - entry[1] <= 0.8
        }
        old = [entry[0] for entry in self.recent.values()]
        result = []
        for track in sorted(tracks, key=lambda t: -t.score):
            matches = [
                (iou(track.box, prev.box), prev) for prev in old if prev.label == track.label
            ]
            best = max(matches, key=lambda v: v[0]) if matches else None
            if best and best[0] > 0.25:
                ident = best[1].track_id
                old.remove(best[1])
            else:
                ident = self.next_id
                self.next_id += 1
            result.append(replace(track, track_id=ident))
        for track in result:
            self.recent[track.track_id] = (track, timestamp)
        self.timestamp = timestamp
        return result


class Observer:
    def __init__(self, model_path: Path):
        import cv2
        import numpy as np

        self.cv, self.np = cv2, np
        cv2.setNumThreads(2)
        self.net = cv2.dnn.readNet(str(model_path))
        self.model = (
            "NanoDet-M-Plus-416:sha256:" + hashlib.sha256(model_path.read_bytes()).hexdigest()
        )
        self.anchors = []
        for stride in (8, 16, 32, 64):
            x, y = np.meshgrid(np.arange(416 // stride), np.arange(416 // stride))
            self.anchors.append(
                np.column_stack((x.flatten(), y.flatten())) * stride + 0.5 * (stride - 1)
            )
        self.tracker = Tracker()
        self.history = {}
        self.lights = {}
        from codriversim.signs import SpeedSignReader

        ocr_path = model_path.with_name("text_recognition_CRNN_EN_2021sep.onnx")
        self.signs = SpeedSignReader(ocr_path) if ocr_path.is_file() else None
        self.cues = VisualCues(self.signs.text if self.signs else None)
        self.motion = MotionEvidence()
        self.session = None
        self.previous_at = -1.0
        self.provenance = {
            "detector": self.model,
            "light_classifier": "HSV-conservative-two-frame-v1",
            "tracker": "IoU-observed-only-memory-0.8s-v2",
            "range": "ground-plane-proposal-50-percent-uncertainty",
            "comfort": "permitted-ego-sensors-sustained-comfort-v1",
            "workzone": "orange-white-tapered-cluster-two-frame-v1;heuristic-unvalidated",
            "wrong_direction": "red-sign-exact-ocr-two-frame-v1;orientation-unverified"
            if self.signs
            else "unavailable-without-ocr",
        }
        if self.signs:
            self.provenance["speed_sign_reader"] = "white-rectangle-tight-word-two-frame-v2"
            self.provenance["speed_sign_ocr"] = (
                "OpenCV-CRNN-EN:sha256:" + hashlib.sha256(ocr_path.read_bytes()).hexdigest()
            )
        self.cycle = 0

    def _detect(self, image):
        cv, np = self.cv, self.np
        h, w = image.shape[:2]
        ratio = min(416 / w, 416 / h)
        nw, nh = round(w * ratio), round(h * ratio)
        resized = cv.resize(image, (nw, nh))
        left, top = (416 - nw) // 2, (416 - nh) // 2
        padded = np.full((416, 416, 3), 114, dtype=np.uint8)
        padded[top : top + nh, left : left + nw] = resized
        normalized = (padded.astype(np.float32) - np.array([103.53, 116.28, 123.675])) / np.array(
            [57.375, 57.12, 58.395]
        )
        self.net.setInput(cv.dnn.blobFromImage(normalized.astype(np.float32)))
        outputs = self.net.forward(self.net.getUnconnectedOutLayersNames())
        boxes, scores, labels = [], [], []
        # OpenCV 5 returns all class heads before all regression heads. Match
        # explicit tensor shapes, not runtime-dependent output enumeration order.
        class_heads = {v.shape[-2]: v for v in outputs if v.shape[-1] == 80}
        regression_heads = {v.shape[-2]: v for v in outputs if v.shape[-1] == 32}
        if not class_heads or class_heads.keys() != regression_heads.keys():
            raise ValueError("Unexpected NanoDet output signature")
        for stride, anchors in zip((8, 16, 32, 64), self.anchors, strict=True):
            count = len(anchors)
            if count not in class_heads:
                continue
            cls, reg = class_heads[count], regression_heads[count]
            cls, reg = cls.reshape(-1, 80), reg.reshape(-1, 8)
            exponent = np.exp(reg - reg.max(axis=1, keepdims=True))
            dist = (exponent / exponent.sum(axis=1, keepdims=True) @ np.arange(8)).reshape(
                -1, 4
            ) * stride
            ids, conf = cls.argmax(axis=1), cls.max(axis=1)
            for idx in np.where(conf > 0.38)[0]:
                if int(ids[idx]) not in CLASSES:
                    continue
                x1, y1 = anchors[idx] - dist[idx, :2]
                x2, y2 = anchors[idx] + dist[idx, 2:]
                x1, x2 = np.clip((np.array([x1, x2]) - left) / ratio, 0, w)
                y1, y2 = np.clip((np.array([y1, y2]) - top) / ratio, 0, h)
                if x2 > x1 and y2 > y1:
                    boxes.append([float(x1), float(y1), float(x2 - x1), float(y2 - y1)])
                    scores.append(float(conf[idx]))
                    labels.append(int(ids[idx]))
        if not boxes:
            return []
        keep = cv.dnn.NMSBoxesBatched(boxes, scores, labels, 0.38, 0.55)
        return [
            Track(
                0,
                CLASSES[labels[i]],
                (boxes[i][0], boxes[i][1], boxes[i][0] + boxes[i][2], boxes[i][1] + boxes[i][3]),
                scores[i],
                provenance=self.model,
            )
            for i in keep
        ]

    def light_state(self, image, box):
        cv, np = self.cv, self.np
        x1, y1, x2, y2 = map(int, box)
        crop = image[max(0, y1) : y2, max(0, x1) : x2]
        if crop.size == 0 or min(crop.shape[:2]) < 5:
            return "unknown"
        hsv = cv.cvtColor(crop, cv.COLOR_BGR2HSV)
        hue, sat, val = cv.split(hsv)
        vivid = (sat > 100) & (val > 150)
        counts = {
            "red": int(np.count_nonzero(vivid & ((hue < 12) | (hue > 165)))),
            "yellow": int(np.count_nonzero(vivid & (hue >= 15) & (hue < 36))),
            "green": int(np.count_nonzero(vivid & (hue >= 38) & (hue < 95))),
        }
        ordered = sorted(counts, key=counts.get, reverse=True)
        return (
            ordered[0]
            if counts[ordered[0]] >= 5 and counts[ordered[0]] > 2 * counts[ordered[1]]
            else "unknown"
        )

    def process(self, frame: CameraFrame, road: dict | None = None) -> PerceptionSnapshot:
        start = time.perf_counter()
        cv, np = self.cv, self.np
        image = (
            cv.cvtColor(
                np.frombuffer(frame.image, np.uint8).reshape(frame.height, frame.width, 3),
                cv.COLOR_RGB2BGR,
            )
            if frame.encoding.startswith("rgb24")
            else cv.imdecode(np.frombuffer(frame.image, np.uint8), cv.IMREAD_COLOR)
        )
        if frame.encoding == "rgb24-bottom-up":
            image = cv.flip(image, 0)
        if image is None or image.shape[:2] != (frame.height, frame.width):
            raise ValueError("Decoded image does not match declared dimensions")
        from codriversim.road import road_risks, validate_road

        road = validate_road(road, frame) if road is not None else None
        if self.session != frame.session_id or frame.timestamp <= self.previous_at:
            self.tracker = Tracker()
            self.history.clear()
            self.lights.clear()
            self.cues.reset()
            if self.signs:
                self.signs.previous = []
                self.signs.previous_at = -1
        self.session, self.previous_at = frame.session_id, frame.timestamp
        detected = self._detect(image)
        self.cycle += 1
        if self.signs and self.cycle % 3 == 0:
            detected.extend(
                replace(track, provenance=self.provenance["speed_sign_reader"])
                for track in self.signs.detect(image, frame.timestamp)
            )
        detected.extend(self.cues.detect(image, frame.timestamp, read_text=self.cycle % 3 == 0))
        tracks = self.tracker.update(detected, frame.timestamp)
        enriched, risks = [], road_risks(road, frame)
        for track in tracks:
            distance = uncertainty = None
            bottom = track.box[3] - frame.calibration.cy
            if (
                bottom > 15
                and track.label in GROUND_HAZARDS
                and frame.calibration.source != "uncalibrated"
            ):
                distance = frame.calibration.height_m * frame.calibration.fy / bottom
                uncertainty = distance * 0.5
            state = (
                self.light_state(image, track.box) if track.label == "traffic light" else "unknown"
            )
            if track.label == "traffic light":
                previous = self.lights.get(track.track_id)
                self.lights[track.track_id] = state
                if previous != state:
                    state = "unknown"
            track = replace(
                track, distance_m=distance, distance_uncertainty_m=uncertainty, signal_state=state
            )
            enriched.append(track)
            centre = (track.box[0] + track.box[2]) / 2 / frame.width
            in_path = 0.35 < centre < 0.65
            prev = self.history.get(track.track_id)
            closing = None
            if prev and distance and prev[1] and 0 < frame.timestamp - prev[0] < 0.6:
                closing = (prev[1] - distance) / (frame.timestamp - prev[0])
            self.history[track.track_id] = (frame.timestamp, distance)
            if distance and in_path and track.label in GROUND_HAZARDS:
                ttc = distance / closing if closing and closing > 1 else None
                if ttc and ttc < 2.5 and frame.ego.speed_mps > 2:
                    risks.append(
                        RiskEvent(
                            frame.timestamp,
                            "urgent",
                            "closing",
                            "Closing rapidly ahead. Check the road",
                            (track.track_id,),
                            True,
                        )
                    )
                elif distance < frame.ego.speed_mps * 1.5:
                    risks.append(
                        RiskEvent(
                            frame.timestamp,
                            "caution",
                            "following",
                            "Leave more space ahead",
                            (track.track_id,),
                            False,
                        )
                    )
                elif (
                    track.label not in {"car", "bus", "truck", "train", "motorcycle"}
                    and distance < 25
                ):
                    risks.append(
                        RiskEvent(
                            frame.timestamp,
                            "caution",
                            "crossing",
                            f"Visible {track.label} near your path",
                            (track.track_id,),
                            True,
                        )
                    )
            if state in {"red", "yellow"}:
                risks.append(
                    RiskEvent(
                        frame.timestamp,
                        "advisory",
                        "signal",
                        f"Visible {state} signal. Check your lane",
                        (track.track_id,),
                    )
                )
            if track.label == "stop sign":
                risks.append(
                    RiskEvent(
                        frame.timestamp, "advisory", "sign", "Stop sign visible", (track.track_id,)
                    )
                )
            if track.speed_limit_mph is not None:
                risks.append(
                    RiskEvent(
                        frame.timestamp,
                        "advisory",
                        "speed_sign",
                        f"Sign reads {track.speed_limit_mph} mph. Confirm the limit",
                        (track.track_id,),
                    )
                )
            if track.label in {"workzone cue", "wrong direction sign"}:
                workzone = track.label == "workzone cue"
                risks.append(
                    RiskEvent(
                        frame.timestamp,
                        "caution",
                        "workzone" if workzone else "wrong_direction",
                        "Possible work zone. Check ahead"
                        if workzone
                        else "Wrong-way sign visible. Check your direction",
                        (track.track_id,),
                        not workzone,
                        "camera",
                        track.provenance,
                        frame.frame_id,
                    )
                )
        risks.extend(self.motion.update(frame, enriched))
        risks = [
            replace(
                risk,
                frame_id=frame.frame_id if risk.frame_id < 0 else risk.frame_id,
                provenance=risk.provenance or self.model + ";monocular-heuristic-v1",
            )
            for risk in risks
        ]
        live = {t.track_id for t in enriched}
        self.history = {k: v for k, v in self.history.items() if k in live}
        self.lights = {k: v for k, v in self.lights.items() if k in live}
        return PerceptionSnapshot(
            frame.session_id,
            frame.frame_id,
            frame.timestamp,
            tuple(enriched),
            tuple(risks),
            (time.perf_counter() - start) * 1000,
            self.model,
            road=road,
            provenance=self.provenance,
        )
