import math

import pytest

from codriversim.domain import WorldState, analyze, evaluate


def test_closing_vehicle_uses_relative_speed():
    warning = evaluate(WorldState(0, 20, 12, 8))
    assert warning.level == "urgent"
    assert warning.ttc_s == 1.5
    assert warning.headway_s == 0.6


def test_receding_vehicle_has_no_collision_time_but_short_gap_warns():
    warning = evaluate(WorldState(0, 20, 10, -5))
    assert warning.level == "caution"
    assert warning.ttc_s is None


def test_stopped_vehicle_does_not_divide_by_zero():
    warning = evaluate(WorldState(0, 0, 5, 0))
    assert warning.level == "quiet"
    assert warning.ttc_s is warning.headway_s is None


def test_absent_lead_is_unknown_not_a_safety_claim():
    assert evaluate(WorldState(0, 20)).level == "unknown"


@pytest.mark.parametrize("value", [True, -1, math.nan, math.inf, "20"])
def test_invalid_speed(value):
    with pytest.raises(ValueError):
        WorldState(0, value)


def test_partial_lead_observation_rejected():
    with pytest.raises(ValueError, match="both"):
        WorldState(0, 10, 20)


def test_timestamp_order_and_provenance():
    manifest = {"schema_version": 1, "frames": [{"timestamp": 0, "speed_mps": 10}]}
    report = analyze(manifest)
    assert report["synthetic"] is False
    assert report["model_inference"] is report["vehicle_control"] is False
    manifest["frames"].append({"timestamp": 0, "speed_mps": 10})
    with pytest.raises(ValueError, match="strictly increase"):
        analyze(manifest)


@pytest.mark.parametrize("manifest", [[], {}, {"schema_version": True, "frames": []}])
def test_invalid_manifest(manifest):
    with pytest.raises(ValueError):
        analyze(manifest)
