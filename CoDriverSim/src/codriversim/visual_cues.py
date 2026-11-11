"""Conservative camera proposals, explicitly separate from NanoDet classes.

Orange/white tapered clusters are an unvalidated workzone cue, never a trained
construction classifier. Direction evidence requires exact red-sign OCR and
two observations; map bearings and simulator actors are not inputs.
"""

from codriversim.contracts import Track


class VisualCues:
    def __init__(self, text_reader=None):
        self.text_reader = text_reader
        self.previous = []
        self.previous_at = -1.0
        self.previous_text_at = -1.0

    def reset(self):
        self.previous = []
        self.previous_at = -1.0
        self.previous_text_at = -1.0

    def detect(self, image, timestamp, *, read_text=True):
        import cv2 as cv
        import numpy as np

        from codriversim.perception import iou

        if not 0 < timestamp - self.previous_at <= 1.2:
            self.previous = []
        self.previous_at = timestamp
        if not 0 < timestamp - self.previous_text_at <= 1.2:
            self.previous = [p for p in self.previous if p[1] != "wrong direction sign"]
        scale = min(1, 640 / image.shape[1])
        small = cv.resize(image, None, fx=scale, fy=scale)
        h, w = small.shape[:2]
        hsv = cv.cvtColor(small, cv.COLOR_BGR2HSV)
        orange = cv.inRange(hsv, (5, 130, 90), (25, 255, 255))
        white = cv.inRange(hsv, (0, 0, 170), (180, 60, 255))
        joined = cv.morphologyEx(orange, cv.MORPH_CLOSE, np.ones((11, 3), np.uint8))
        contours, _ = cv.findContours(joined, cv.RETR_EXTERNAL, cv.CHAIN_APPROX_SIMPLE)
        cones = []
        for contour in contours:
            x, y, cw, ch = cv.boundingRect(contour)
            if not (12 <= ch <= h * 0.5 and 0.3 < cw / ch < 1.2 and y > h * 0.35):
                continue
            crop = orange[y : y + ch, x : x + cw] > 0
            upper = np.count_nonzero(crop[ch // 5 : ch * 2 // 5], axis=1)
            lower = np.count_nonzero(crop[ch * 3 // 4 :], axis=1)
            stripe = white[y + ch // 3 : y + ch * 2 // 3, x + cw // 4 : x + cw * 3 // 4]
            if (
                upper.size
                and lower.size
                and upper.mean() < lower.mean() * 0.8
                and lower.mean() > cw * 0.5
                and np.count_nonzero(stripe) > stripe.size * 0.1
            ):
                cones.append((x / scale, y / scale, (x + cw) / scale, (y + ch) / scale))
        proposals = []
        if len(cones) >= 2:
            proposals.extend(
                (
                    box,
                    "workzone cue",
                    "orange-white-tapered-cluster-two-frame-v1;heuristic-unvalidated",
                )
                for box in cones[:8]
            )
        if self.text_reader is not None and read_text:
            self.previous_text_at = timestamp
            red = cv.bitwise_or(
                cv.inRange(hsv, (0, 110, 65), (5, 255, 255)),
                cv.inRange(hsv, (170, 110, 65), (180, 255, 255)),
            )
            contours, _ = cv.findContours(red, cv.RETR_EXTERNAL, cv.CHAIN_APPROX_SIMPLE)
            candidates = sorted(contours, key=cv.contourArea, reverse=True)[:2]
            for contour in candidates:
                x, y, cw, ch = cv.boundingRect(contour)
                if not (
                    24 <= cw <= w * 0.4
                    and 20 <= ch <= h * 0.5
                    and 0.7 < cw / ch < 2.4
                    and y + ch < h * 0.85
                    and cv.contourArea(contour) / (cw * ch) > 0.65
                ):
                    continue
                white_fraction = np.count_nonzero(white[y : y + ch, x : x + cw]) / (cw * ch)
                if not 0.03 < white_fraction < 0.5:
                    continue
                # CRNN expects dark text on a light field, unlike these red signs.
                text_image = cv.cvtColor(
                    cv.bitwise_not(white[y : y + ch, x : x + cw]), cv.COLOR_GRAY2BGR
                )
                words = tuple(
                    self.text_reader(text_image[int(ch * a) : int(ch * b)])
                    for a, b in ((0.08, 0.5), (0.5, 0.92))
                )
                if words != ("wrong", "way"):
                    words = tuple(
                        self.text_reader(text_image[int(ch * a) : int(ch * b)])
                        for a, b in ((0.05, 0.34), (0.34, 0.66), (0.66, 0.95))
                    )
                if words in (("wrong", "way"), ("do", "not", "enter")):
                    box = (x / scale, y / scale, (x + cw) / scale, (y + ch) / scale)
                    proposals.append(
                        (
                            box,
                            "wrong direction sign",
                            "red-sign-exact-ocr-two-frame-v1;"
                            + " ".join(words)
                            + ";orientation-unverified",
                        )
                    )
        results = []
        for box, label, provenance in proposals:
            if any(
                old_label == label and old_provenance == provenance and iou(box, old_box) > 0.35
                for old_box, old_label, old_provenance in self.previous
            ):
                results.append(Track(0, label, box, 0.0, source="camera", provenance=provenance))
        # OCR runs less frequently; retain only fresh sign evidence between its
        # scheduled passes, without emitting an unobserved sign on those frames.
        retained = (
            [p for p in self.previous if p[1] == "wrong direction sign"] if not read_text else []
        )
        self.previous = proposals + retained
        return results
