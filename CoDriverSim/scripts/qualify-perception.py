"""Class-coverage smoke evaluation on a fixed COCO 2017 validation subset.

Five image occurrences per supported COCO class, selected before inference using
seed 1049. Excludes crowd annotations and selects only metadata licensed CC BY 2.0.
This is not the full COCO benchmark, road validation, or a tracking/warning test.
Images stay in the ignored cache and retain their source/license attribution.
"""

import hashlib
import json
import random
import zipfile
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor
from dataclasses import asdict
from pathlib import Path

import requests

from codriversim.contracts import Calibration, CameraFrame, EgoTelemetry
from codriversim.perception import CLASSES, Observer
from codriversim.perception_evaluation import evaluate_camera

ROOT = Path(__file__).resolve().parents[1]
CACHE = ROOT / "outputs/assets/coco"
ANNOTATIONS_SHA256 = "113a836d90195ee1f884e704da6304dfaaecff1f023f49b6ca93c4aaae470268"
SOURCE = "https://s3.amazonaws.com/images.cocodataset.org/"


def download(url, path):
    if not path.exists():
        with requests.get(url, stream=True, timeout=60) as response:
            response.raise_for_status()
            temporary = path.with_suffix(".download")
            with temporary.open("wb") as output:
                for chunk in response.iter_content(1024 * 1024):
                    output.write(chunk)
            temporary.replace(path)
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    CACHE.mkdir(parents=True, exist_ok=True)
    archive = CACHE / "annotations_trainval2017.zip"
    digest = download(SOURCE + "annotations/" + archive.name, archive)
    if digest != ANNOTATIONS_SHA256:
        raise ValueError("COCO annotation archive changed")
    with zipfile.ZipFile(archive) as zipped:
        dataset = json.loads(zipped.read("annotations/instances_val2017.json"))
    scope = set(CLASSES.values())
    categories = {c["id"]: c["name"] for c in dataset["categories"] if c["name"] in scope}
    objects = defaultdict(list)
    for obj in dataset["annotations"]:
        if obj["category_id"] in categories:
            objects[obj["image_id"]].append(obj)
    candidates = [
        i
        for i in dataset["images"]
        if i["license"] == 4
        and objects[i["id"]]
        and not any(o["iscrowd"] for o in objects[i["id"]])
    ]
    random.Random(1049).shuffle(candidates)
    selected, coverage = [], Counter()
    for item in candidates:
        present = {categories[o["category_id"]] for o in objects[item["id"]]}
        if any(coverage[c] < 5 for c in present):
            selected.append(item)
            coverage.update(present)
        if all(coverage[c] >= 5 for c in scope):
            break
    if not all(coverage[c] >= 5 for c in scope):
        raise ValueError("Not enough eligible validation images for class coverage")

    def fetch(item):
        filename = f"{item['id']:012d}.jpg"
        path = CACHE / filename
        url = SOURCE + "val2017/" + filename
        image_hash = download(url, path)
        return item | {
            "download": url,
            "sha256": image_hash,
            "license_url": "https://creativecommons.org/licenses/by/2.0/",
            "photo_page": "https://www.flickr.com/photo.gne?id="
            + item["flickr_url"].split("/")[-1].split("_")[0],
        }

    with ThreadPoolExecutor(max_workers=4) as pool:
        manifest = list(pool.map(fetch, selected))
    manifest_path = CACHE / "smoke-manifest.json"
    if manifest_path.is_file() and json.loads(manifest_path.read_text()) != manifest:
        raise ValueError(
            "Cached evaluation fixtures changed; preserve and review the previous manifest"
        )
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"Selected {len(selected)} images before inference: {dict(coverage)}", flush=True)
    output = ROOT / "outputs/qualification/coco-smoke"
    output.mkdir(parents=True, exist_ok=True)
    labels = {
        "schema_version": 2,
        "annotations_complete": True,
        "source": "COCO Consortium",
        "split": "val2017, CC BY 2.0 metadata only, seed 1049, no crowd images",
        "class_scope": sorted(scope),
        "frames": [],
    }
    observer = Observer(ROOT / "models/object_detection_nanodet_2022nov.onnx")
    predictions = []
    for index, item in enumerate(selected):
        width, height = item["width"], item["height"]
        frame = CameraFrame(
            "coco-validation-1049",
            index,
            index * 2.0,
            width,
            height,
            Calibration(width, width, width / 2, height / 2, source="uncalibrated"),
            EgoTelemetry(),
            (CACHE / item["file_name"]).read_bytes(),
        )
        predictions.append(asdict(observer.process(frame)))
        truth = []
        for obj in objects[item["id"]]:
            x, y, w, h = obj["bbox"]
            truth.append(
                {
                    "object_id": str(obj["id"]),
                    "label": categories[obj["category_id"]],
                    "box": [max(0, x), max(0, y), min(width, x + w), min(height, y + h)],
                }
            )
        labels["frames"].append(
            {
                "session_id": frame.session_id,
                "frame_id": index,
                "timestamp": frame.timestamp,
                "objects": truth,
                "coco_image_id": item["id"],
            }
        )
    # COCO does not label speed-limit signs or signal colors. Preserve full outputs,
    # but only score classes for which this dataset supplies complete annotations.
    scoped = [p | {"tracks": [t for t in p["tracks"] if t["label"] in scope]} for p in predictions]
    report = evaluate_camera(labels, scoped)
    report["scope"] = {
        "classes": sorted(scope),
        "images": len(selected),
        "threshold": 0.38,
        "selection_seed": 1049,
        "image_occurrences": dict(coverage),
        "limitations": [
            "Class-stratified smoke subset, not the COCO AP benchmark",
            "No signal color or speed-sign labels",
            "Independent still images, not tracking or hazard validation",
            "Historical CC BY 2.0 image licenses from COCO metadata; source photo links retained",
        ],
    }
    for name, value in (
        ("labels", labels),
        ("predictions-full", predictions),
        ("predictions-scoped", scoped),
        ("result", report),
        ("attribution", manifest),
    ):
        (output / (name + ".json")).write_text(json.dumps(value, indent=2), encoding="utf-8")
    print(output / "result.json")


if __name__ == "__main__":
    main()
