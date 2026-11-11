"""Application and replay adapters. Importing this module does not launch anything."""

import json
import math
import subprocess
from dataclasses import asdict
from pathlib import Path


def project_root() -> Path:
    for parent in Path(__file__).resolve().parents:
        if (parent / "game/ProjectSettings/ProjectVersion.txt").is_file() or (
            parent / "codriversim-app.json"
        ).is_file():
            return parent
    raise RuntimeError("Game commands require a CoDriverSim application or checkout")


def launch(executable: Path | None = None) -> dict:
    root = project_root() if executable is None else None
    target = executable or root / (
        "CoDriverSim.exe"
        if (root / "codriversim-app.json").is_file()
        else "outputs/game/CoDriverSim.exe"
    )
    if not target.is_file():
        raise RuntimeError("Build the game using scripts/build-game.ps1 first")
    process = subprocess.Popen([str(target.resolve()), "-force-d3d11"])
    return {
        "project": "codriversim",
        "mode": "game",
        "pid": process.pid,
        "executable": str(target.resolve()),
    }


def replay(
    path: Path,
    model: Path | None = None,
    calibration: Path | None = None,
    *,
    max_frames: int = 3000,
    sample_hz: float = 5,
    start_seconds: float = 0,
) -> dict:
    if (
        not 1 <= max_frames <= 100000
        or not math.isfinite(sample_hz)
        or not 0 < sample_hz <= 120
        or not math.isfinite(start_seconds)
        or start_seconds < 0
    ):
        raise ValueError("Invalid replay frame limit, sample rate, or start time")
    from codriversim.contracts import Calibration, CameraFrame, EgoTelemetry
    from codriversim.perception import Observer
    from codriversim.recording import read_recording

    model = model or project_root() / "models/object_detection_nanodet_2022nov.onnx"
    observer = Observer(model)
    results = []
    if path.suffix.lower() == ".json":
        next_sample = start_seconds
        for frame in read_recording(path):
            if frame.timestamp < next_sample:
                continue
            next_sample = frame.timestamp + 1 / sample_hz
            results.append(asdict(observer.process(frame)))
            if len(results) >= max_frames:
                break
    else:
        import cv2

        capture = cv2.VideoCapture(str(path))
        if not capture.isOpened():
            raise ValueError("Could not open recorded camera video")
        fps = capture.get(cv2.CAP_PROP_FPS)
        if not math.isfinite(fps) or fps <= 0:
            capture.release()
            raise ValueError("Video has no valid frame rate")
        calibration_data = json.loads(calibration.read_text()) if calibration else None
        index = 0
        next_sample = start_seconds
        try:
            while True:
                ok, image = capture.read()
                if not ok:
                    break
                timestamp = index / fps
                index += 1
                if timestamp < next_sample:
                    continue
                next_sample = timestamp + 1 / sample_hz
                h, w = image.shape[:2]
                camera = (
                    Calibration(**calibration_data)
                    if calibration_data
                    else Calibration(w, w, w / 2, h / 2, source="uncalibrated")
                )
                payload = cv2.imencode(".jpg", image)[1].tobytes()
                frame = CameraFrame(
                    "recorded-video", index - 1, timestamp, w, h, camera, EgoTelemetry(), payload
                )
                results.append(asdict(observer.process(frame)))
                if len(results) >= max_frames:
                    break
        finally:
            capture.release()
    return {
        "schema_version": 2,
        "project": "codriversim",
        "mode": "sensor_replay",
        "vehicle_control": False,
        "model_inference": True,
        "results": results,
        "real_road_accuracy_established": False,
        "frame_limit": max_frames,
        "sample_hz": sample_hz,
    }


def evaluate_manifest(path: Path) -> dict:
    from codriversim.evaluation import Criterion, score_scenario

    data = json.loads(path.read_text(encoding="utf-8-sig"))
    if data.get("schema_version") != 2 or not isinstance(data.get("criteria"), list):
        raise ValueError("Expected a v2 scenario criteria manifest")
    try:
        criteria = [Criterion(**row) for row in data["criteria"]]
    except TypeError as exc:
        raise ValueError("Invalid criterion fields") from exc
    return score_scenario(
        data["scenario"],
        criteria,
        seed=data.get("seed", 21),
        mode=data.get("mode", "human_ai"),
        perception_metrics=data.get("perception_metrics"),
        timing=data.get("timing"),
    )


def evaluate_camera_files(labels: Path, predictions: Path) -> dict:
    from codriversim.perception_evaluation import evaluate_camera

    annotations = json.loads(labels.read_text(encoding="utf-8-sig"))
    output = json.loads(predictions.read_text(encoding="utf-8-sig"))
    if not isinstance(output.get("results"), list):
        raise ValueError("Expected a replay result containing frame predictions")
    report = evaluate_camera(annotations, output["results"])
    report["prediction_provenance"] = (
        output["results"][0].get("provenance", {}) if output["results"] else {}
    )
    return report
