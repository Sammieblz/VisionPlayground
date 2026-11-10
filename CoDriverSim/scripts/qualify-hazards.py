"""Evaluate fixed rendered warning probes; keep evaluator labels out of perception."""

import hashlib
import json
from dataclasses import asdict
from pathlib import Path

from codriversim.contracts import Calibration, CameraFrame, EgoTelemetry
from codriversim.perception import Observer
from codriversim.perception_evaluation import evaluate_camera


def main():
    root = Path(__file__).resolve().parents[1]
    folder = root / "outputs/qualification/rendered-hazards"
    labels = json.loads((folder / "labels.json").read_text())
    predictions, hashes = [], {}
    observer, session = None, None
    for row in labels["frames"]:
        if row["session_id"] != session:
            observer = Observer(root / "models/object_detection_nanodet_2022nov.onnx")
            session = row["session_id"]
        image = (folder / row["image"]).read_bytes()
        hashes[row["image"]] = hashlib.sha256(image).hexdigest()
        # Only the JPEG, capture metadata, measured speed and calibration enter
        # Observer. Object boxes and hazard windows are read by evaluate_camera only.
        frame = CameraFrame(
            session,
            row["frame_id"],
            row["timestamp"],
            964,
            604,
            Calibration(455, 455, 482, 302, height_m=1.3),
            EgoTelemetry(speed_mps=row["speed_mps"], source="simulated_sensors"),
            image,
        )
        predictions.append(asdict(observer.process(frame)))
    result = evaluate_camera(labels, predictions)
    result["image_sha256"] = hashes
    result["limitations"] = [
        "Four controlled rendered probes, not natural driving or real-road validation",
        "Warning deadlines are predeclared script checkpoints, not safe reaction-time claims",
        "No Comma road stream: this probe covers the general detector/risk rules only",
        "Projected object bounds are evaluator-only labels; visual review is required",
        "Cold CPU processing timings are not a sustained throughput benchmark",
    ]
    (folder / "predictions.json").write_text(json.dumps(predictions, indent=2), encoding="utf-8")
    (folder / "result.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(folder / "result.json")


if __name__ == "__main__":
    main()
