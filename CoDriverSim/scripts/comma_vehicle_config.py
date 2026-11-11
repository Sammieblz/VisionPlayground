"""Versioned, simulator-only vehicle configuration. No observer dependency."""

import hashlib
import json
import math

PROFILES = {
    "civic-comparison-v1": (1462, 2.7, 15.38, 1.22),
    "gls-simulation-v1": (2660, 3.134, 18.7, 1.54),
}


def validate_configuration(header):
    encoded = header.get("configuration_json", "")
    digest = hashlib.sha256(encoded.encode()).hexdigest()
    if digest != header.get("configuration_sha256"):
        raise ValueError("Vehicle configuration digest mismatch")
    config = json.loads(encoded)
    if config.get("version") != 1 or config.get("profile_id") not in PROFILES:
        raise ValueError("Unsupported simulation vehicle configuration")
    expected = PROFILES[config["profile_id"]]
    for key, value in zip(
        ("mass_kg", "wheelbase_m", "steering_ratio", "camera_height_m"), expected, strict=True
    ):
        if not math.isclose(config.get(key, float("nan")), value, abs_tol=1e-4):
            raise ValueError(f"Unrecognized vehicle parameter: {key}")
    for key, value in {"fx": 455, "fy": 455, "width": 964, "height": 604}.items():
        if config.get(key) != value:
            raise ValueError("Camera dimensions/intrinsics do not match the adapter")
    calibration = header.get("calibration", {})
    for key, value in {"fx": 455, "fy": 455, "height_m": expected[3]}.items():
        if not math.isclose(calibration.get(key, float("nan")), value, abs_tol=1e-4):
            raise ValueError("Frame calibration does not match the vehicle configuration")
    expected_pose = (
        [1.7, 0, 1.22] if config["profile_id"] == "civic-comparison-v1" else [0.78, 0, 1.54]
    )
    pose = header.get("pose", {}).get("position_ego_m", [])
    configured_pose = config.get("camera_position_ego_m", [])
    if (
        len(pose) != 3
        or len(configured_pose) != 3
        or any(
            not math.isclose(actual, expected_value, abs_tol=1e-4)
            for values in (pose, configured_pose)
            for actual, expected_value in zip(values, expected_pose, strict=True)
        )
    ):
        raise ValueError("Camera pose does not match the simulation adapter")
    return config, digest


def configure_params(params, config, scale_inertia, scale_tires):
    """Alter physical parameters only; retain upstream controllers and safety checks."""
    if config["profile_id"] == "civic-comparison-v1":
        return params
    params.mass = config["mass_kg"]
    params.wheelbase = config["wheelbase_m"]
    params.steerRatio = config["steering_ratio"]
    # Estimated static 55% front axle load; tuning assumption, not a measured GLS port.
    params.centerToFront = params.wheelbase * 0.45
    params.rotationalInertia = scale_inertia(params.mass, params.wheelbase)
    params.tireStiffnessFront, params.tireStiffnessRear = scale_tires(
        params.mass, params.wheelbase, params.centerToFront, params.tireStiffnessFactor
    )
    return params
