import json
from dataclasses import replace

import pytest

from eyepause.domain import EyeState, Observation
from eyepause.settings import Settings, load_settings, save_settings
from eyepause.vision import Calibration, EyeClassifier, eye_aspect_ratio


def test_ear_uses_geometry_and_is_scale_invariant():
    points = [(0, 0), (1, 1), (3, 1), (4, 0), (3, -1), (1, -1)]
    assert eye_aspect_ratio(points) == 0.5
    assert eye_aspect_ratio([(x * 3, y * 3) for x, y in points]) == 0.5


@pytest.mark.parametrize("points", [[], [(0, 0)] * 6, [(float("nan"), 0)] * 6])
def test_degenerate_eyes_are_rejected(points):
    with pytest.raises(ValueError):
        eye_aspect_ratio(points)


@pytest.mark.parametrize("ear", [None, -0.1, float("nan"), float("inf"), 1.5])
def test_invalid_measurements_never_establish_closure(ear):
    classifier = EyeClassifier(Settings())
    classifier.classify(Observation(0, 0.1, 0.1, 1))
    assert classifier.classify(Observation(1, ear, 0.1, 1)) == EyeState.UNKNOWN


def collect(calibration, duration, ear, start=0):
    for i in range(duration * 10 + 1):
        calibration.add(Observation(start + i / 10, ear, ear, 1))


def test_guided_calibration_uses_separated_personal_baselines():
    calibration = Calibration()
    collect(calibration, 3, 0.32)
    assert calibration.ready
    calibration.advance()
    collect(calibration, 2, 0.08, 5)
    settings = calibration.apply(Settings())
    assert settings.left_closed == pytest.approx(0.164)
    assert settings.left_open == pytest.approx(0.236)


def test_failed_calibration_preserves_original_settings():
    calibration = Calibration()
    original = Settings()
    collect(calibration, 3, 0.30)
    calibration.advance()
    collect(calibration, 2, 0.29, 5)
    with pytest.raises(ValueError, match="separate"):
        calibration.apply(original)
    assert original == Settings()


def test_missing_face_resets_calibration_step():
    calibration = Calibration()
    collect(calibration, 2, 0.30)
    calibration.add(Observation(2.1))
    assert calibration.add(Observation(2.2, 0.3, 0.3, 1)) == 0
    assert not calibration.ready


def test_settings_round_trip_contains_no_camera_or_media_content(tmp_path):
    path = tmp_path / "settings.json"
    settings = replace(Settings(), camera_index=2, pause_seconds=3.0)
    save_settings(settings, path)
    assert load_settings(path) == (settings, "")
    assert set(json.loads(path.read_text())) == {"version", "settings"}


@pytest.mark.parametrize(
    "data",
    ['{"version": 9}', "{}", "[]", "not json", '{"version":1,"settings":{"pause_seconds":-1}}'],
)
def test_corrupt_settings_fall_back_without_crashing(tmp_path, data):
    path = tmp_path / "settings.json"
    path.write_text(data)
    settings, message = load_settings(path)
    assert settings == Settings() and message


@pytest.mark.parametrize(
    "changes",
    [
        dict(pause_seconds=float("nan")),
        dict(resume_seconds=0),
        dict(left_closed=0.3, left_open=0.2),
        dict(camera_index=-1),
    ],
)
def test_settings_validation(changes):
    with pytest.raises(ValueError):
        replace(Settings(), **changes)
