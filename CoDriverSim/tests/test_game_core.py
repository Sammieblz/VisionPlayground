"""Portable contracts, recording boundaries, evaluation, and tracker regressions."""

import json
import threading
from dataclasses import asdict, dataclass

import pytest

from codriversim.contracts import (
    Calibration,
    CameraFrame,
    CameraPose,
    EgoTelemetry,
    FrameOrder,
    Track,
)
from codriversim.evaluation import Criterion, detection_metrics, score_scenario
from codriversim.perception import Tracker
from codriversim.recording import EventRecorder, RecordingWorker, read_recording


def frame(i, **changes):
    values = dict(
        session_id="test-session",
        frame_id=i,
        timestamp=float(i),
        width=640,
        height=400,
        calibration=Calibration(300, 300, 320, 200),
        ego=EgoTelemetry(),
        image=b"jpeg",
    )
    return CameraFrame(**(values | changes))


def test_optional_measured_imu_survives_recording_contract_without_simulator_truth():
    ego = EgoTelemetry(
        specific_force_device_mps2=[2, -1, -9.81],
        angular_velocity_device_rps=[0, 0, 0.2],
        source="recorded_sensors",
    )
    original = frame(0, ego=ego)
    parsed = CameraFrame.parse(original.metadata(), original.image)
    assert parsed.ego == ego
    assert parsed.ego.specific_force_device_mps2 == (2, -1, -9.81)
    assert CameraFrame.parse(frame(0).metadata(), b"jpeg").ego.specific_force_device_mps2 is None
    for values in ([0, 0], [0, 0, float("inf")], [0, True, 0], "0,0,0"):
        with pytest.raises(ValueError):
            EgoTelemetry(specific_force_device_mps2=values)


def test_active_event_has_replayable_atomic_checkpoint_before_clean_shutdown(tmp_path):
    recorder = EventRecorder(tmp_path)
    for i in range(11):
        recorder.push(frame(i), {"frame_id": i})
    recorder.trigger(10, "Driver saved", explicit=True)
    manifest = next(tmp_path.glob("clip-*/recording.json"))
    initial = json.loads(manifest.read_text())
    assert initial["complete"] is False
    assert initial["explicitly_saved"] is True
    assert len(list(read_recording(manifest))) == 11
    recorder.push(frame(11), {"frame_id": 11})
    # Read the durable file without close(), as a replay after a process crash would.
    assert len(list(read_recording(manifest))) == 12
    assert json.loads(manifest.read_text())["available_post_seconds"] == 1
    recorder.close()


def test_scenario_capture_and_event_clip_have_independent_durations(tmp_path):
    recorder = EventRecorder(tmp_path)
    recorder.push(frame(0), {"frame_id": 0})
    recorder.start_capture(0, "c" * 32)
    for i in range(1, 36):
        recorder.push(frame(i), {"frame_id": i})
        if i == 12:
            recorder.trigger(12, "Visible hazard")
    recorder.stop_capture(35)
    manifests = [json.loads(p.read_text()) for p in tmp_path.glob("clip-*/recording.json")]
    capture = next(m for m in manifests if m["purpose"] == "scenario")
    event = next(m for m in manifests if m["purpose"] == "event")
    assert capture["complete"] is True
    assert capture["pre_seconds"] == 0
    assert capture["available_post_seconds"] == 35
    assert capture["frame_count"] == 36
    assert event["complete"] is True
    assert event["available_pre_seconds"] == event["available_post_seconds"] == 10
    assert len(list(read_recording(tmp_path / ("clip-" + "c" * 32) / "recording.json"))) == 36


def test_scenario_capture_is_bounded_and_requires_an_opaque_identifier(tmp_path):
    recorder = EventRecorder(tmp_path)
    with pytest.raises(ValueError, match="opaque"):
        recorder.start_capture(0, "../outside")
    recorder.push(frame(0), {})
    recorder.start_capture(0, "d" * 32)
    recorder.push(frame(600), {})
    assert not recorder.active
    assert "ten-minute" in recorder.error
    report = json.loads(next(tmp_path.glob("clip-*/recording.json")).read_text())
    assert report["complete"] is False
    assert report["purpose"] == "scenario"


def test_journal_checkpoint_stays_small_and_torn_tail_preserves_prior_frames(tmp_path):
    recorder = EventRecorder(tmp_path)
    recorder.start_capture(0, "e" * 32)
    for i in range(40):
        recorder.push(frame(i), {"provenance": "x" * 10000})
    manifest = next(tmp_path.glob("clip-*/recording.json"))
    assert manifest.stat().st_size < 4096
    journal = manifest.with_name("frames.jsonl")
    with journal.open("a") as output:
        output.write('{"camera":')  # A process can die during the final append.
    assert [f.frame_id for f in read_recording(manifest)] == list(range(40))
    assert json.loads(manifest.read_text())["complete"] is False
    recorder.close()


def test_original_embedded_recording_remains_replayable_and_journal_cannot_escape(tmp_path):
    original = tmp_path / "recording.json"
    (tmp_path / "camera.jpg").write_bytes(b"jpeg")
    original.write_text(
        json.dumps(
            {
                "schema_version": 2,
                "frames": [
                    {"camera": frame(0).metadata(), "image": "camera.jpg", "observation": {}}
                ],
            }
        )
    )
    assert list(read_recording(original))[0].frame_id == 0
    original.write_text(json.dumps({"schema_version": 2, "frames_file": "../outside.jsonl"}))
    with pytest.raises(ValueError, match="escapes"):
        list(read_recording(original))


def test_recording_replay_honors_sampling_start_and_frame_limit(tmp_path, monkeypatch):
    from codriversim.application import replay

    @dataclass
    class Seen:
        frame_id: int
        timestamp: float

    class Observer:
        def __init__(self, model):
            pass

        def process(self, image):
            return Seen(image.frame_id, image.timestamp)

    monkeypatch.setattr("codriversim.perception.Observer", Observer)
    recorder = EventRecorder(tmp_path)
    recorder.start_capture(0, "b" * 32)
    for i, stamp in enumerate((0, 0.05, 0.2, 0.23, 0.4, 0.5, 0.7)):
        recorder.push(frame(i, timestamp=stamp), {})
    recorder.stop_capture(0.7)
    result = replay(
        next(tmp_path.glob("clip-*/recording.json")),
        model=tmp_path / "unused.onnx",
        start_seconds=0.21,
        sample_hz=5,
        max_frames=2,
    )
    assert result["results"] == [
        {"frame_id": 3, "timestamp": 0.23},
        {"frame_id": 5, "timestamp": 0.5},
    ]


def test_slow_storage_does_not_block_submit_and_overflow_marks_capture_partial(tmp_path):
    entered, release = threading.Event(), threading.Event()

    class SlowStorage(EventRecorder):
        def push(self, image, observation):
            if image.frame_id == 0:
                entered.set()
                assert release.wait(3), "Test storage gate was not released"
            super().push(image, observation)

    worker = RecordingWorker(SlowStorage(tmp_path), max_bytes=8)
    try:
        worker.request("start_capture", 0, "f" * 32)
        assert worker.request("push", frame(0), {})
        assert entered.wait(2)
        # Disk worker is still blocked, but the camera producer remains usable.
        assert worker.request("push", frame(1), {})
        assert worker.request("push", frame(2), {})
        assert not worker.request("push", frame(3), {})
        assert worker.bytes <= 8
        assert "queue full" in worker.error
        assert worker.request("stop_capture", 3)
    finally:
        release.set()
        worker.close()
    assert not worker.thread.is_alive()
    manifest = next(tmp_path.glob("clip-*/recording.json"))
    report = json.loads(manifest.read_text())
    assert not report["complete"] and report["recording_queue_dropped_frames"] > 0
    assert [f.frame_id for f in read_recording(manifest)] == [0, 1, 2]


@pytest.mark.parametrize("hidden", ["objects", "traffic_light_state", "ground_truth", "controls"])
def test_camera_rejects_hidden_truth_and_control(hidden):
    header = frame(0).metadata() | {hidden: []}
    with pytest.raises(ValueError, match="fields"):
        CameraFrame.parse(header, b"jpeg")


def test_ego_cannot_smuggle_hidden_truth():
    header = frame(0).metadata()
    header["ego"]["collision_prediction"] = True
    with pytest.raises(ValueError):
        CameraFrame.parse(header, b"jpeg")


@pytest.mark.parametrize("value", [float("nan"), float("inf"), True, -1])
def test_invalid_camera_timestamp(value):
    with pytest.raises(ValueError):
        frame(0, timestamp=value)


def test_frame_order_rejects_replay_and_cross_session():
    order = FrameOrder()
    order.accept(frame(2))
    for invalid in (frame(2), frame(3, timestamp=1), frame(4, session_id="another")):
        with pytest.raises(ValueError):
            order.accept(invalid)


def test_event_clip_includes_ten_seconds_each_side_and_replays(tmp_path):
    recorder = EventRecorder(tmp_path)
    for i in range(21):
        recorder.push(frame(i), {"frame_id": i})
    recorder.trigger(20, "hazard")
    for i in range(21, 31):
        recorder.push(frame(i), {"frame_id": i})
    recording = next(tmp_path.glob("*/recording.json"))
    data = json.loads(recording.read_text())
    assert data["complete"]
    assert [f.timestamp for f in read_recording(recording)] == list(range(10, 31))
    assert data["frame_count"] == 21
    recorder.close()
    assert recording.exists()


def test_delayed_inference_trigger_retains_its_full_pre_event_window(tmp_path):
    recorder = EventRecorder(tmp_path)
    for i in range(13):
        recorder.push(frame(i), {})
    recorder.trigger(10, "Risk from a frame processed two seconds later")
    for i in range(13, 21):
        recorder.push(frame(i), {})
    manifest = next(tmp_path.glob("clip-*/recording.json"))
    assert json.loads(manifest.read_text())["complete"]
    assert [f.timestamp for f in read_recording(manifest)] == list(range(21))


def test_ring_memory_is_bounded(tmp_path):
    recorder = EventRecorder(tmp_path, max_bytes=12)
    for i in range(20):
        recorder.push(frame(i), {})
    assert recorder.bytes <= 12
    assert len(recorder.frames) == 3


def test_full_automatic_cache_preserves_explicit_saves(tmp_path):
    recorder = EventRecorder(tmp_path, max_automatic_bytes=1)
    recorder.push(frame(0), {})
    recorder.trigger(0, "automatic hazard")
    assert not list(tmp_path.glob("clip-*"))
    assert "cache full" in recorder.error
    recorder.trigger(0, "Driver saved", explicit=True)
    recorder.close()
    path = next(tmp_path.glob("clip-*/recording.json"))
    before = path.read_bytes()
    assert json.loads(before)["explicitly_saved"]
    again = EventRecorder(tmp_path, max_automatic_bytes=1)
    again.trigger(1, "another automatic hazard")
    again.close()
    assert path.read_bytes() == before


def test_automatic_quota_reloads_existing_recordings(tmp_path):
    recorder = EventRecorder(tmp_path)
    recorder.push(frame(0), {})
    recorder.trigger(0, "automatic hazard")
    recorder.close()
    stored = list(tmp_path.glob("clip-*/recording.json"))
    again = EventRecorder(tmp_path, max_automatic_bytes=10)
    again.trigger(1, "next automatic hazard")
    assert list(tmp_path.glob("clip-*/recording.json")) == stored
    assert "cache full" in again.error


def test_warmup_clip_reports_missing_pre_roll(tmp_path):
    recorder = EventRecorder(tmp_path)
    for i in range(4):
        recorder.push(frame(i), {})
    recorder.trigger(3, "warmup event")
    for i in range(4, 14):
        recorder.push(frame(i), {})
    report = json.loads(next(tmp_path.glob("*/recording.json")).read_text())
    assert not report["complete"]
    assert report["available_pre_seconds"] == 3
    assert report["available_post_seconds"] == 10


def test_camera_pose_roundtrip_and_rejects_nonrigid_or_hidden_fields():
    original = frame(2, pose=CameraPose((1.7, 0, 1.22), source="simulated_mount"))
    assert CameraFrame.parse(json.loads(json.dumps(original.metadata())), b"jpeg") == original
    with pytest.raises(ValueError, match="orthonormal"):
        CameraPose(rotation=(2, 0, 0, 0, 1, 0, 0, 0, 1))
    metadata = original.metadata()
    metadata["pose"]["objects"] = []
    with pytest.raises(ValueError, match="fields"):
        CameraFrame.parse(metadata, b"jpeg")


def test_storage_failure_is_reported(tmp_path):
    path = tmp_path / "file"
    path.write_text("occupied")
    recorder = EventRecorder(path)
    recorder.push(frame(0), {})
    recorder.trigger(0, "test")
    assert recorder.error
    assert not recorder.active


def test_critical_failure_overrides_percentage_and_unavoidable_excluded():
    criteria = [
        Criterion("collision", False, critical=True),
        Criterion("speed", True),
        Criterion("unavoidable script", False, critical=True, unavoidable=True),
    ]
    result = score_scenario("crossing", criteria)
    assert result["score"] == 50
    assert result["passed"] is False
    assert result["critical_failures"] == ["collision"]


def test_empty_evaluation_does_not_claim_success():
    assert score_scenario("empty", [])["passed"] is None
    assert detection_metrics(0, 0, 0, [])["recall"] is None


def test_scenario_result_keeps_perception_separate_from_driver_score(tmp_path):
    from codriversim.application import evaluate_manifest

    source = tmp_path / "criteria.json"
    metrics = {"precision": None, "recall": 0, "warning_lead_seconds": -0.2}
    source.write_text(
        json.dumps(
            {
                "schema_version": 2,
                "scenario": "Recorded crossing",
                "criteria": [{"name": "Yield", "passed": True}],
                "perception_metrics": metrics,
                "timing": {"duration_seconds": 12, "latency_p95_ms": 220},
            }
        )
    )
    result = evaluate_manifest(source)
    assert result["schema_version"] == 2
    assert result["score"] == 100
    assert result["passed"] is True
    assert result["perception_metrics"] == metrics
    assert result["timing"] == {"duration_seconds": 12, "latency_p95_ms": 220}
    assert result["source"] == "evaluation_only"
    assert result["physics_replay_deterministic"] is False


@pytest.mark.parametrize(
    "measurements",
    [
        {"timing": {"duration": 1}},
        {"timing": {"duration_seconds": -1}},
        {"timing": {"duration_seconds": True}},
        {"perception_metrics": {"recall": float("nan")}},
    ],
)
def test_scenario_result_rejects_invalid_measurements(measurements):
    with pytest.raises(ValueError):
        score_scenario("Crossing", [Criterion("Yield", True)], **measurements)


def test_tracker_retains_identity_but_not_after_stale_observation():
    tracker = Tracker()
    first = Track(0, "person", (10, 10, 30, 60), 0.8)
    a = tracker.update([first], 0)[0]
    b = tracker.update([Track(0, "person", (12, 10, 32, 60), 0.9)], 0.2)[0]
    c = tracker.update([first], 2)[0]
    assert a.track_id == b.track_id
    assert c.track_id != a.track_id
    assert asdict(c)["distance_m"] is None


def test_intermittent_detections_keep_identity_without_inventing_observations():
    tracker = Tracker()
    sign = Track(0, "speed limit sign", (10, 10, 30, 60), 0.8)
    first = tracker.update([sign], 0)[0]
    assert tracker.update([], 0.2) == []
    assert tracker.update([], 0.4) == []
    assert tracker.update([sign], 0.6)[0].track_id == first.track_id
    assert tracker.update([], 1.0) == []
    assert tracker.update([sign], 1.5)[0].track_id != first.track_id


def test_tracking_never_assigns_one_identity_to_two_current_objects():
    tracker = Tracker()
    person = Track(0, "person", (10, 10, 30, 60), 0.9)
    first = tracker.update([person], 1)[0]
    pair = tracker.update([person, person], 1.2)
    assert len({t.track_id for t in pair}) == 2
    assert pair[0].track_id == first.track_id
    assert tracker.update([person], 0.5)[0].track_id != first.track_id
