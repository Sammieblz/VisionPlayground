"""Evidence rules and negative controls; no claims of trained-model accuracy."""

from dataclasses import replace

import pytest

from codriversim.comfort import MotionEvidence
from codriversim.contracts import Calibration, CameraFrame, EgoTelemetry, Track
from codriversim.perception import CLASSES, Observer
from codriversim.road import road_risks, validate_road
from codriversim.visual_cues import VisualCues


def frame(stamp, *, acceleration=0, speed=10, source="recorded_sensors", session="a"):
    return CameraFrame(
        session,
        int(stamp * 100),
        stamp,
        400,
        300,
        Calibration(300, 300, 200, 150),
        EgoTelemetry(speed_mps=speed, acceleration_mps2=acceleration, source=source),
        b"image",
    )


def test_rapid_acceleration_requires_sustained_measured_motion_and_resets_after_gaps():
    evidence = MotionEvidence()
    assert evidence.update(frame(0, acceleration=4), []) == []
    assert evidence.update(frame(0.3, acceleration=4), []) == []
    (risk,) = evidence.update(frame(0.7, acceleration=4), [])
    assert risk.kind == "rapid_acceleration" and risk.source == "ego_sensors"
    assert risk.frame_id == 70 and "recorded_sensors" in risk.provenance
    assert evidence.update(frame(2, acceleration=4), []) == []
    assert evidence.update(frame(2.3, acceleration=4, session="b"), []) == []
    assert evidence.update(frame(2.7, acceleration=4, source="unavailable"), []) == []
    assert evidence.update(frame(3, acceleration=4), []) == []


def test_harsh_braking_is_comfort_advice_not_late_braking_without_visual_hazard():
    evidence = MotionEvidence()
    assert evidence.update(frame(0, acceleration=-4), []) == []
    (risk,) = evidence.update(frame(0.4, acceleration=-4), [])
    assert risk.kind == "harsh_braking" and "when safe" in risk.message
    assert evidence.update(frame(0.6, acceleration=-4, speed=0), []) == []


def lead(distance, **changes):
    return replace(
        Track(
            8,
            "car",
            (160, 100, 240, 230),
            0.8,
            distance_m=distance,
            distance_uncertainty_m=distance * 0.5,
        ),
        **changes,
    )


def test_late_braking_requires_three_closing_frames_even_at_far_range_bound():
    evidence = MotionEvidence()
    assert evidence.update(frame(0, speed=20), [lead(25)]) == []
    assert evidence.update(frame(0.2, speed=20), [lead(21)]) == []
    (risk,) = evidence.update(frame(0.4, speed=20), [lead(17)])
    assert risk.kind == "late_braking" and risk.evidence == (8,)
    assert risk.source == "camera+ego_sensors" and "uncertain-ground-range" in risk.provenance


@pytest.mark.parametrize(
    "changes",
    [
        {"box": (0, 100, 80, 230)},
        {"label": "traffic light"},
        {"distance_m": None},
        {"distance_uncertainty_m": 1000},
    ],
)
def test_ambiguous_or_off_path_tracks_do_not_claim_late_braking(changes):
    evidence = MotionEvidence()
    for i, distance in enumerate((25, 21, 17)):
        assert evidence.update(frame(i * 0.2, speed=20), [lead(distance, **changes)]) == []


def test_braking_response_and_stale_or_disappeared_objects_clear_late_braking():
    evidence = MotionEvidence()
    evidence.update(frame(0, speed=20), [lead(25)])
    evidence.update(frame(0.2, speed=20), [lead(21)])
    assert evidence.update(frame(0.4, speed=20, acceleration=-3), [lead(17)]) == []
    assert evidence.update(frame(0.6, speed=20), []) == []
    assert evidence.update(frame(0.8, speed=20), [lead(13)]) == []
    assert evidence.update(frame(3, speed=20), [lead(5)]) == []


def image():
    np = pytest.importorskip("numpy")
    return np.zeros((300, 400, 3), dtype=np.uint8)


def cone_pixels(scene, x):
    cv = pytest.importorskip("cv2")
    np = pytest.importorskip("numpy")
    cv.fillConvexPoly(scene, np.array([[x + 14, 160], [x + 28, 220], [x, 220]]), (0, 140, 255))
    # A five-pixel white band inside the tapered silhouette.
    cv.fillConvexPoly(
        scene, np.array([[x + 8, 187], [x + 20, 187], [x + 21, 192], [x + 7, 192]]), (255, 255, 255)
    )


def test_workzone_is_two_frame_cluster_cue_with_no_fabricated_confidence():
    scene = image()
    cone_pixels(scene, 130)
    cone_pixels(scene, 230)
    cues = VisualCues()
    assert cues.detect(scene, 0) == []
    tracks = cues.detect(scene, 0.2)
    assert len(tracks) == 2
    assert all(t.label == "workzone cue" and t.score == 0 for t in tracks)
    assert all("heuristic-unvalidated" in t.provenance for t in tracks)
    assert cues.detect(scene, 2) == []
    cues.reset()
    assert cues.detect(scene, 2.2) == []


@pytest.mark.parametrize("shape", ["single_cone", "orange_vehicle", "sunset", "rectangles"])
def test_everyday_or_unconfirmed_orange_pixels_do_not_claim_workzone(shape):
    cv = pytest.importorskip("cv2")
    scene = image()
    if shape == "single_cone":
        cone_pixels(scene, 150)
    elif shape == "orange_vehicle":
        cv.rectangle(scene, (90, 150), (280, 240), (0, 140, 255), -1)
        cv.rectangle(scene, (120, 160), (250, 180), (220, 220, 220), -1)
    elif shape == "sunset":
        scene[:160] = (0, 140, 255)
    else:
        for x in (100, 200):
            cv.rectangle(scene, (x, 160), (x + 30, 220), (0, 140, 255), -1)
            cv.rectangle(scene, (x, 185), (x + 30, 191), (255, 255, 255), -1)
    cues = VisualCues()
    assert cues.detect(scene, 0) == cues.detect(scene, 0.2) == []


def red_sign():
    cv = pytest.importorskip("cv2")
    scene = image()
    scene[40:100, 150:250] = (0, 0, 220)
    cv.putText(scene, "WRONG", (154, 62), cv.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 2)
    cv.putText(scene, "WAY", (168, 88), cv.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 2)
    return scene


def test_direction_requires_exact_repeated_camera_text_not_red_color_alone():
    from itertools import cycle

    words = cycle(("wrong", "way"))
    cues = VisualCues(lambda _: next(words))
    assert cues.detect(red_sign(), 0) == []
    (risk_track,) = cues.detect(red_sign(), 0.6)
    assert risk_track.label == "wrong direction sign" and risk_track.score == 0
    assert "orientation-unverified" in risk_track.provenance
    assert cues.detect(image(), 0.8) == []
    assert cues.detect(red_sign(), 1.0) == []
    assert VisualCues().detect(red_sign(), 1) == []


@pytest.mark.parametrize("text", ["stop", "wrong", "enter", "work", "", "wrongway"])
def test_partial_or_other_red_sign_text_is_not_wrong_direction(text):
    cues = VisualCues(lambda _: text)
    assert cues.detect(red_sign(), 0) == cues.detect(red_sign(), 0.2) == []


def test_supported_animal_classes_do_not_invent_cone_or_construction_model_classes():
    assert {"bird", "cat", "dog", "horse", "cow", "bear"} <= set(CLASSES.values())
    assert not {"cone", "construction", "deer", "workzone cue"} & set(CLASSES.values())


def test_camera_pipeline_emits_cue_provenance_and_resets_between_sessions(tmp_path, monkeypatch):
    cv = pytest.importorskip("cv2")
    np = pytest.importorskip("numpy")

    class EmptyDetector:
        def setInput(self, _):
            pass

        def getUnconnectedOutLayersNames(self):
            return []

        def forward(self, _):
            return [np.zeros((1, 52 * 52, 80)), np.zeros((1, 52 * 52, 32))]

    monkeypatch.setattr(cv.dnn, "readNet", lambda _: EmptyDetector())
    model = tmp_path / "model.onnx"
    model.write_bytes(b"synthetic detector stub;not trained accuracy")
    observer = Observer(model)
    scene = image()
    cone_pixels(scene, 130)
    cone_pixels(scene, 230)
    rgb = cv.cvtColor(scene, cv.COLOR_BGR2RGB).tobytes()

    def observe(stamp, session="a"):
        return observer.process(
            replace(frame(stamp, acceleration=4, session=session), encoding="rgb24", image=rgb)
        )

    assert observe(0).risks == ()
    confirmed = observe(0.3)
    assert len(confirmed.tracks) == 2 and len(confirmed.risks) == 2
    assert all(
        r.kind == "workzone"
        and r.frame_id == 30
        and r.evidence
        and "heuristic-unvalidated" in r.provenance
        for r in confirmed.risks
    )
    assert "rapid_acceleration" in {r.kind for r in observe(0.7).risks}
    assert observe(0.8, "b").risks == ()
    assert observe(2.2, "b").risks == ()


def test_road_heuristics_keep_source_frame_and_reject_stale_session():
    geometry = {"x": [0], "y": [-0.3], "z": [0]}
    road = dict(
        schema_version=2,
        session_id="a",
        frame_id=10,
        timestamp=0.1,
        model="openpilot-v0.9.7-50dd024",
        lanes=[geometry] * 3,
        lane_probabilities=[0.9] * 3,
        edges=[],
        path=geometry,
        leads=[],
        execution_ms=12,
    )
    camera = frame(0.2)
    assert validate_road(road, camera) is road
    assert all(
        r.frame_id == 10 and "not-stock-ldw" in r.provenance for r in road_risks(road, camera)
    )
    assert validate_road(road, frame(1)) is None
    assert validate_road(road, frame(0.2, session="b")) is None
