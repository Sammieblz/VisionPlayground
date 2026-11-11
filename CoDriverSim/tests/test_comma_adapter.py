"""Simulation bridge liveness; no openpilot installation or Unity needed."""

import importlib.util
from pathlib import Path
from types import SimpleNamespace

import pytest

spec = importlib.util.spec_from_file_location(
    "unity_comma", Path(__file__).resolve().parents[1] / "scripts" / "unity-comma.py"
)
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)


@pytest.mark.parametrize("age", [0.251, 4, -0.01])
def test_live_socket_cannot_replay_commands_from_dead_control_loop(age):
    original = {"active": True, "healthy": True, "engageable": True, "steering_deg": 15}
    result = bridge.fresh_command(original, 10, 10 + age)
    assert not result["healthy"]
    assert not result["active"]
    assert not result["engageable"]
    assert "stale" in result["status"]
    assert original["healthy"]


def test_fresh_bridge_preserves_upstream_rejection():
    original = {"active": False, "healthy": False, "status": "controlsdLagging"}
    assert bridge.fresh_command(original, 10, 10.02) == original


@pytest.mark.parametrize(
    ("force", "rotation", "expected_acc", "expected_gyro"),
    [
        ([0, 0, -9.81], [0, 0, 0], (9.81, 0, 0), (0, 0, 0)),
        ([2, 0, -9.81], [0, 0, 0.2], (9.81, 0, -2), (-0.2, 0, 0)),
        ([-3, -1, -9.7], [0.1, -0.3, -0.2], (9.7, 1, 3), (0.2, 0.3, -0.1)),
    ],
)
def test_stock_sensor_axes_preserve_acceleration_and_yaw(
    force, rotation, expected_acc, expected_gyro
):
    acc, gyro = bridge.stock_imu_axes(
        dict(specific_force_device_mps2=force, angular_velocity_device_rps=rotation)
    )
    assert acc == expected_acc
    assert gyro == expected_gyro
    # The untouched upstream decoder must recover the device measurements.
    assert tuple(-v for v in reversed(acc)) == tuple(force)
    assert tuple(-v for v in reversed(gyro)) == tuple(rotation)


def test_stock_bridge_rejects_absent_or_invalid_imu_instead_of_inventing_measurements():
    for ego in (
        None,
        [],
        {},
        {"specific_force_device_mps2": [0, 0, float("nan")]},
        {"specific_force_device_mps2": [0, True, -9.81]},
    ):
        with pytest.raises(ValueError):
            bridge.stock_imu_axes(ego)


def stock_messages():
    class Messages(dict):
        pass

    messages = Messages(
        controlsState=SimpleNamespace(lateralPlanMonoTime=123),
        carControl=SimpleNamespace(
            hudControl=SimpleNamespace(leftLaneDepart=False, rightLaneDepart=True)
        ),
        onroadEvents=[SimpleNamespace(name="fcw")],
    )
    messages.alive = dict.fromkeys(messages, True)
    messages.valid = dict.fromkeys(messages, True)
    messages.recv_time = dict.fromkeys(messages, 10)
    messages.logMonoTime = dict.fromkeys(messages, 567)
    header = dict(session_id="a", frame_id=30, timestamp=3)
    sources = {123: dict(session_id="a", frame_id=28, timestamp=2.8)}
    return messages, header, sources


def test_stock_warnings_copy_actual_events_and_sides_with_associated_model_frame():
    messages, header, sources = stock_messages()
    result = bridge.stock_warning_snapshot(messages, header, 10.1, sources)
    assert [r["state"] for r in result] == ["active", "inactive", "active"]
    assert [r["kind"] for r in result] == ["fcw", "ldw_left", "ldw_right"]
    assert all(r["frame_id"] == 28 and r["timestamp"] == 2.8 for r in result)
    assert all(
        r["source_log_mono_time"] == 567 and "not event camera ID" in r["frame_association"]
        for r in result
    )
    assert result[0]["source"] == "onroadEvents.fcw"
    messages["onroadEvents"] = []
    messages["carControl"].hudControl.rightLaneDepart = False
    assert all(
        r["state"] == "inactive"
        for r in bridge.stock_warning_snapshot(messages, header, 10.1, sources)
    )


@pytest.mark.parametrize(
    "failure",
    [
        "stale_controls",
        "stale_model",
        "different_session",
        "missing_mapping",
        "invalid_control",
        "future_model",
    ],
)
def test_stock_warnings_are_unknown_without_fresh_valid_attributable_upstream_data(failure):
    messages, header, sources = stock_messages()
    if failure == "stale_controls":
        messages.recv_time["controlsState"] = 9
    elif failure == "stale_model":
        sources[123]["timestamp"] = 1
    elif failure == "different_session":
        sources[123]["session_id"] = "b"
    elif failure == "missing_mapping":
        sources.clear()
    elif failure == "invalid_control":
        messages.valid["carControl"] = False
    else:
        sources[123]["frame_id"] = 31
    assert all(
        r["state"] == "unknown" and not r["active"]
        for r in bridge.stock_warning_snapshot(messages, header, 10.1, sources)
    )


def test_stale_event_or_unsupported_side_does_not_become_inactive_safe():
    messages, header, sources = stock_messages()
    messages.recv_time["onroadEvents"] = 8
    del messages["carControl"].hudControl.leftLaneDepart
    result = bridge.stock_warning_snapshot(messages, header, 10.1, sources)
    assert [r["state"] for r in result] == ["unknown", "unknown", "active"]
