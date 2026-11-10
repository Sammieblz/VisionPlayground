"""Control configuration binding must fail closed without leaking into perception."""

import hashlib
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace

import pytest

spec = importlib.util.spec_from_file_location(
    "vehicle_configuration",
    Path(__file__).parents[1] / "scripts/comma_vehicle_config.py",
)
adapter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(adapter)


def header(profile="gls-simulation-v1"):
    mass, wheelbase, ratio, camera = adapter.PROFILES[profile]
    pose = [1.7, 0, 1.22] if profile == "civic-comparison-v1" else [0.78, 0, 1.54]
    encoded = json.dumps(
        dict(
            version=1,
            profile_id=profile,
            mass_kg=mass,
            wheelbase_m=wheelbase,
            steering_ratio=ratio,
            camera_height_m=camera,
            fx=455,
            fy=455,
            width=964,
            height=604,
            camera_position_ego_m=pose,
        )
    )
    return dict(
        configuration_json=encoded,
        configuration_sha256=hashlib.sha256(encoded.encode()).hexdigest(),
        calibration=dict(fx=455, fy=455, height_m=camera),
        pose=dict(position_ego_m=pose),
    )


def test_configuration_rejects_mixed_camera_and_vehicle():
    packet = header()
    packet["calibration"]["height_m"] = 1.22
    with pytest.raises(ValueError, match="Frame calibration"):
        adapter.validate_configuration(packet)


def test_configuration_rejects_modified_parameters():
    packet = header()
    packet["configuration_json"] = packet["configuration_json"].replace("2660", "1462")
    with pytest.raises(ValueError, match="digest"):
        adapter.validate_configuration(packet)
    packet["configuration_sha256"] = hashlib.sha256(
        packet["configuration_json"].encode()
    ).hexdigest()
    with pytest.raises(ValueError, match="Unrecognized vehicle"):
        adapter.validate_configuration(packet)


def test_gls_parameters_preserve_control_and_honda_transport():
    config, _ = adapter.validate_configuration(header())
    controller = object()
    params = SimpleNamespace(
        mass=1462,
        wheelbase=2.7,
        steerRatio=15.38,
        tireStiffnessFactor=1,
        lateralTuning=controller,
        carFingerprint="HONDA_CIVIC_2022",
    )
    adapter.configure_params(
        params, config, lambda m, w: m * w * w, lambda m, w, c, f: (m * (1 - c / w), m * c / w)
    )
    assert (params.mass, params.wheelbase, params.steerRatio) == (2660, 3.134, 18.7)
    assert params.lateralTuning is controller
    assert params.carFingerprint == "HONDA_CIVIC_2022"
    assert params.tireStiffnessFront > params.tireStiffnessRear > 0


def test_honda_baseline_stays_unchanged():
    config, _ = adapter.validate_configuration(header("civic-comparison-v1"))
    params = object()
    assert adapter.configure_params(params, config, None, None) is params
