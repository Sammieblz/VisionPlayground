"""Evaluate predeclared rendered sign fixtures; never pass labels to inference."""

import argparse
import hashlib
import json
from dataclasses import asdict
from pathlib import Path

from codriversim.contracts import Calibration, CameraFrame, EgoTelemetry
from codriversim.perception import Observer
from codriversim.perception_evaluation import evaluate_camera

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--holdout", action="store_true")
    args = parser.parse_args()
    folder = (
        ROOT
        / "outputs/qualification"
        / ("rendered-signs-holdout" if args.holdout else "rendered-signs")
    )
    fixtures = json.loads((folder / "fixtures.json").read_text())
    labels = {
        "schema_version": 2,
        "annotations_complete": True,
        "source": fixtures["source"],
        "split": ("Held-out" if args.holdout else "Development")
        + " stationary views, 6 samples each at 5 Hz",
        "frames": [],
    }
    predictions = []
    hashes = {}
    for i, item in enumerate(fixtures["cases"]):
        image = (folder / item["image"]).read_bytes()
        hashes[item["image"]] = hashlib.sha256(image).hexdigest()
        observer = Observer(ROOT / "models/object_detection_nanodet_2022nov.onnx")
        session = f"rendered-sign-{i}"
        for index in range(6):
            frame = CameraFrame(
                session,
                index,
                index * 0.2,
                964,
                604,
                Calibration(455, 455, 482, 302, height_m=1.3),
                EgoTelemetry(),
                image,
            )
            predictions.append(asdict(observer.process(frame)))
            obj = {"object_id": str(i), "label": item["label"], "box": item["box"]}
            if item.get("signal_state"):
                obj["signal_state"] = item["signal_state"]
            if item.get("speed_limit_mph"):
                obj["speed_limit_mph"] = item["speed_limit_mph"]
            labels["frames"].append(
                {
                    "session_id": session,
                    "frame_id": index,
                    "timestamp": frame.timestamp,
                    "objects": [obj],
                }
            )
    report = evaluate_camera(labels, predictions)
    report["limitations"] = [
        "Small rendered smoke set; not real-road accuracy or a driving test",
        "Includes cold-start and temporal confirmation frames as missed readings",
        "No complete hazard annotations; warning quality is not measured",
        "Unknown/off light labels are retained but excluded from known-color accuracy",
    ]
    report["image_sha256"] = hashes
    for name, value in (("labels", labels), ("predictions", predictions), ("result", report)):
        (folder / (name + ".json")).write_text(json.dumps(value, indent=2), encoding="utf-8")
    print(folder / "result.json")


if __name__ == "__main__":
    main()
