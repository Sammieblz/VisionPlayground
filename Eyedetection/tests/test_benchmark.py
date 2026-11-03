import json

import pytest

from eyepause.benchmark import AnnotatedSample, evaluate, main, read_segments, read_trace
from eyepause.domain import EyeState, Observation


def annotated_trace():
    rows = []
    for i in range(101):
        timestamp = i / 10
        state = EyeState.CLOSED if 1 <= timestamp < 4 else EyeState.OPEN
        ear = 0.1 if state == EyeState.CLOSED else 0.3
        rows.append(AnnotatedSample(Observation(timestamp, ear, ear, 1), state))
    return rows


def test_replay_reports_events_dwell_and_no_real_commands():
    report = evaluate(annotated_trace())
    assert report["sustained_closure_events"] == 1
    assert report["closure_event_precision"] == 1
    assert report["closure_event_recall"] == 1
    assert report["false_pauses"] == 0
    assert report["closure_to_pause"]["median_ms"] == pytest.approx(2000)
    assert report["reopening_to_resume"]["median_ms"] == pytest.approx(500)
    assert report["actions"] == [
        {"action": "pause", "timestamp": 3.0},
        {"action": "play", "timestamp": 4.5},
    ]


def test_false_pause_is_counted_against_independent_labels():
    rows = [AnnotatedSample(s.observation, EyeState.OPEN) for s in annotated_trace()]
    report = evaluate(rows)
    assert report["false_pauses"] == 1
    assert report["closure_event_precision"] == 0
    assert report["closure_event_recall"] is None


def test_trace_cli_writes_json(tmp_path):
    trace = tmp_path / "trace.jsonl"
    rows = [
        {
            "timestamp": s.observation.timestamp,
            "left_ear": s.observation.left_ear,
            "right_ear": s.observation.right_ear,
            "label": s.label.value.lower(),
        }
        for s in annotated_trace()
    ]
    trace.write_text("\n".join(json.dumps(r) for r in rows))
    output = tmp_path / "report.json"
    assert main(["trace", str(trace), "--output", str(output)]) == 0
    assert json.loads(output.read_text())["reports"][0]["samples"] == 101


@pytest.mark.parametrize(
    "contents", ["", '{"timestamp":NaN,"label":"open"}', '{"timestamp":0,"label":"other"}']
)
def test_bad_trace_rejected(tmp_path, contents):
    path = tmp_path / "trace.jsonl"
    path.write_text(contents)
    with pytest.raises(ValueError):
        read_trace(path)


def test_annotations_must_be_contiguous(tmp_path):
    path = tmp_path / "annotations.json"
    path.write_text('{"version":1,"segments":[{"start":1,"end":2,"label":"open"}]}')
    with pytest.raises(ValueError, match="contiguous"):
        read_segments(path)


def test_historical_cascade_asset_loads_without_opencv_wheel_data():
    import cv2

    from eyepause.resources import ASSETS

    cascade = cv2.CascadeClassifier(str(ASSETS / "haarcascade_eye.xml"))
    assert not cascade.empty()
