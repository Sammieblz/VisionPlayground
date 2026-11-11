"""Conservative U.S. speed-sign proposals and CRNN OCR; pixels only.

An unrecognized rectangle stays a proposal. Require SPEED, LIMIT, an allowed
number and temporal agreement. This baseline needs held-out validation.
"""

from pathlib import Path

from codriversim.contracts import Track


class SpeedSignReader:
    def __init__(self, path: Path):
        import cv2

        self.cv = cv2
        self.net = cv2.dnn.readNet(str(path))
        self.previous = []
        self.previous_at = -1.0

    def text(self, crop):
        import numpy as np

        cv = self.cv
        if crop.size == 0:
            return ""
        gray = cv.cvtColor(crop, cv.COLOR_BGR2GRAY)
        # The recognition model expects a word crop, not the sign's margins.
        # Keep one antialiasing pixel around dark letter strokes.
        x, y, w, h = cv.boundingRect(cv.inRange(gray, 0, 120))
        if w == 0 or h == 0:
            return ""
        gray = gray[max(0, y - 1) : y + h + 1, max(0, x - 1) : x + w + 1]
        self.net.setInput(cv.dnn.blobFromImage(gray, 1 / 127.5, (100, 32), 127.5))
        output = self.net.forward()
        indices = np.argmax(output.reshape(-1, 37), axis=1)
        result = []
        last = -1
        for idx in indices:
            if idx and idx != last:
                result.append("0123456789abcdefghijklmnopqrstuvwxyz"[idx - 1])
            last = idx
        return "".join(result)

    def detect(self, image, timestamp):
        from codriversim.perception import iou

        cv = self.cv
        if not 0 < timestamp - self.previous_at <= 1.2:
            self.previous = []
        self.previous_at = timestamp
        scale = min(1, 960 / image.shape[1])
        small = cv.resize(image, None, fx=scale, fy=scale)
        hsv = cv.cvtColor(small, cv.COLOR_BGR2HSV)
        mask = cv.inRange(hsv, (0, 0, 170), (180, 45, 255))
        contours, _ = cv.findContours(mask, cv.RETR_EXTERNAL, cv.CHAIN_APPROX_SIMPLE)
        candidates = []
        for contour in contours:
            x, y, w, h = cv.boundingRect(contour)
            if (
                14 <= w <= 120
                and 25 <= h <= 180
                and 0.45 < w / h < 0.9
                and y < small.shape[0] * 0.8
            ):
                if cv.contourArea(contour) / (w * h) > 0.6:
                    candidates.append((x, y, w, h))
        results = []
        observations = []
        for x, y, w, h in sorted(candidates, key=lambda b: -b[2] * b[3])[:3]:
            crop = small[y : y + h, x : x + w]
            top = self.text(crop[int(h * 0.06) : int(h * 0.28)])
            # Detailed façades produce many white rectangles. Reject a failed
            # header before spending two more CRNN passes on the same proposal.
            # The required words, threshold and temporal confirmation stay identical.
            if top != "speed":
                continue
            middle = self.text(crop[int(h * 0.28) : int(h * 0.47)])
            if middle != "limit":
                continue
            digits = self.text(crop[int(h * 0.47) : int(h * 0.93)])
            if not digits.isdigit():
                continue
            speed = int(digits)
            if speed not in range(5, 90, 5):
                continue
            box = (x / scale, y / scale, (x + w) / scale, (y + h) / scale)
            observations.append((box, speed))
            confirmed = any(
                old_speed == speed and iou(old_box, box) > 0.35
                for old_box, old_speed in self.previous
            )
            results.append(
                Track(0, "speed limit sign", box, 0.0, speed_limit_mph=speed if confirmed else None)
            )
        self.previous = observations
        return results
