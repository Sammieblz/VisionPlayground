"""Temporal confirmation tests; these do not measure OCR accuracy."""

from itertools import cycle

import pytest

from codriversim.signs import SpeedSignReader


def test_speed_sign_confirmation_expires_across_missing_camera_time():
    cv = pytest.importorskip("cv2")
    np = pytest.importorskip("numpy")
    image = np.zeros((300, 400, 3), dtype=np.uint8)
    image[40:140, 100:160] = 255
    reader = SpeedSignReader.__new__(SpeedSignReader)
    reader.cv = cv
    reader.previous = []
    reader.previous_at = -1
    words = cycle(("speed", "limit", "30"))
    reader.text = lambda _: next(words)
    assert reader.detect(image, 1)[0].speed_limit_mph is None
    assert reader.detect(image, 1.6)[0].speed_limit_mph == 30
    assert reader.detect(image, 10)[0].speed_limit_mph is None
    assert reader.detect(image, 10.6)[0].speed_limit_mph == 30
