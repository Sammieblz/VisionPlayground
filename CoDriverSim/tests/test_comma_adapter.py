"""Simulation bridge liveness; no openpilot installation or Unity needed."""

import importlib.util
from pathlib import Path

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
