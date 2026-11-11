from copy import deepcopy

import pytest

from codriversim.perception_evaluation import evaluate_camera


def sample():
    labels = {
        "schema_version": 2,
        "annotations_complete": True,
        "frames": [
            {
                "session_id": "s",
                "frame_id": 1,
                "timestamp": 1.0,
                "objects": [
                    {"object_id": "a", "label": "person", "box": [0, 0, 10, 20]},
                    {"object_id": "b", "label": "dog", "box": [20, 0, 30, 20]},
                ],
            }
        ],
    }
    predictions = [
        {
            "session_id": "s",
            "frame_id": 1,
            "timestamp": 1.0,
            "latency_ms": 80,
            "tracks": [
                {"track_id": 1, "label": "person", "box": [0, 0, 10, 20]},
                {"track_id": 2, "label": "train", "box": [40, 0, 50, 20]},
            ],
        }
    ]
    return labels, predictions


def test_precision_recall_do_not_reward_wrong_class_or_duplicate():
    labels, predictions = sample()
    result = evaluate_camera(labels, predictions)
    assert result["precision"] == result["recall"] == 0.5
    assert result["counts"] == {"tp": 1, "fp": 1, "fn": 1}
    predictions[0]["tracks"].append(deepcopy(predictions[0]["tracks"][0]))
    assert evaluate_camera(labels, predictions)["precision"] == pytest.approx(1 / 3)


def test_dropped_inference_is_a_miss_not_a_removed_ground_truth_frame():
    labels, _ = sample()
    result = evaluate_camera(labels, [])
    assert result["frames_with_inference"] == 0
    assert result["recall"] == 0
    assert result["precision"] is None
    assert result["latency_p95_ms"] is None


def test_live_dataclass_tracks_and_json_tracks_have_identical_metrics():
    labels, predictions = sample()
    expected = evaluate_camera(labels, predictions)
    predictions[0]["tracks"] = tuple(predictions[0]["tracks"])
    assert evaluate_camera(labels, predictions) == expected


def test_session_capture_identity_must_match():
    labels, predictions = sample()
    predictions[0]["session_id"] = "other"
    assert evaluate_camera(labels, predictions)["recall"] == 0
    predictions[0]["session_id"] = "s"
    predictions[0]["timestamp"] = 2
    with pytest.raises(ValueError, match="capture times"):
        evaluate_camera(labels, predictions)


def test_unknown_signal_is_not_correct_and_track_switch_is_counted():
    labels, predictions = sample()
    labels["frames"][0]["objects"] = [
        {
            "object_id": "light",
            "label": "traffic light",
            "signal_state": "red",
            "box": [0, 0, 10, 20],
        }
    ]
    predictions[0]["tracks"] = [
        {"track_id": 1, "label": "traffic light", "signal_state": "unknown", "box": [0, 0, 10, 20]}
    ]
    labels["frames"].append(deepcopy(labels["frames"][0]))
    predictions.append(deepcopy(predictions[0]))
    for row in (labels["frames"][1], predictions[1]):
        row["frame_id"] = 2
        row["timestamp"] = 1.2
    predictions[1]["tracks"][0].update(track_id=2, signal_state="red")
    result = evaluate_camera(labels, predictions)
    assert result["signal_accuracy_including_misses"] == 0.5
    assert result["signal_unknown_or_missed"] == 1
    assert result["tracking_id_switches_within_one_second"] == 1


def test_warning_metrics_require_complete_hazard_labels():
    labels, predictions = sample()
    predictions[0]["risks"] = [{"kind": "crossing"}, {"kind": "following"}]
    assert evaluate_camera(labels, predictions)["unnecessary_warning_samples"] is None
    labels.update(
        hazards_complete=True,
        hazards=[
            {
                "hazard_id": "crossing-a",
                "session_id": "s",
                "kind": "crossing",
                "observable_from": 0.8,
                "deadline": 1.5,
                "end": 2,
            }
        ],
    )
    result = evaluate_camera(labels, predictions)
    assert result["hazard_results"] == [
        {"hazard_id": "crossing-a", "missed": False, "warning_lead_seconds": 0.5}
    ]
    assert result["unnecessary_warning_samples"] == 1
    labels["hazards"][0]["deadline"] = 0.9
    assert evaluate_camera(labels, predictions)["hazard_results"][0]["missed"] is True


def test_duplicate_and_incomplete_annotations_rejected():
    labels, predictions = sample()
    with pytest.raises(ValueError, match="Duplicate prediction"):
        evaluate_camera(labels, predictions * 2)
    labels["annotations_complete"] = False
    with pytest.raises(ValueError, match="complete"):
        evaluate_camera(labels, predictions)


def test_hazard_credit_requires_warning_evidence_for_the_correct_object():
    labels, predictions = sample()
    labels.update(
        hazards_complete=True,
        hazards=[
            {
                "hazard_id": "person-crossing",
                "object_id": "a",
                "session_id": "s",
                "kind": "crossing",
                "acceptable_kinds": ["crossing", "closing"],
                "observable_from": 0,
                "deadline": 1.5,
                "end": 2,
            }
        ],
    )
    predictions[0]["risks"] = [{"kind": "closing", "evidence": [2]}]
    result = evaluate_camera(labels, predictions)
    assert result["hazard_results"][0]["missed"] is True
    assert result["unnecessary_warning_samples"] == 1
    predictions[0]["risks"][0]["evidence"] = [1]
    result = evaluate_camera(labels, predictions)
    assert result["hazard_results"][0]["missed"] is False
    assert result["hazard_results"][0]["warning_lead_seconds"] == 0.5
    assert result["unnecessary_warning_samples"] == 0


def test_hazard_acceptable_kinds_rejects_a_string_instead_of_a_list():
    labels, predictions = sample()
    labels.update(
        hazards_complete=True,
        hazards=[
            {
                "hazard_id": "bad-kind",
                "session_id": "s",
                "kind": "crossing",
                "acceptable_kinds": "crossing",
                "observable_from": 0,
                "deadline": 1,
                "end": 2,
            }
        ],
    )
    with pytest.raises(ValueError, match="acceptable_kinds"):
        evaluate_camera(labels, predictions)
