"""Download the licensed qualification clip; keep media out of source control."""

import hashlib
import json
from pathlib import Path

import requests

ROOT = Path(__file__).resolve().parents[1]
REVISION = "de35185262bda34c104a626f866bab9d9aaf67ed"
SOURCE = "https://huggingface.co/datasets/visaitech/vehicle-mixed-traffic-detection"
URL = SOURCE + "/resolve/" + REVISION + "/videos/clip_140.mp4"
EXPECTED = "dcb4515c87e3e81e008ba95107b20e7ec9e334e0e23b4f1783dac5d0c14901f8"


def main():
    directory = ROOT / "outputs/real-footage"
    directory.mkdir(parents=True, exist_ok=True)
    target = directory / "visaitech-clip-140.mp4"
    if not target.exists():
        response = requests.get(
            URL,
            stream=True,
            timeout=90,
            headers={"User-Agent": "CoDriverSim/0.1 qualification (VisionPlayground)"},
        )
        response.raise_for_status()
        with target.with_suffix(".download").open("wb") as output:
            for chunk in response.iter_content(1024 * 1024):
                output.write(chunk)
        target.with_suffix(".download").replace(target)
    checksum = hashlib.sha256(target.read_bytes()).hexdigest()
    if checksum != EXPECTED:
        raise ValueError("Qualification footage checksum mismatch")
    attribution = {
        "title": "Visaitech Mixed-Traffic Vehicle Detection Dataset v0.1 / clip 140",
        "author": "Visaitech",
        "source": SOURCE,
        "revision": REVISION,
        "license": "CC-BY-4.0",
        "license_url": "https://creativecommons.org/licenses/by/4.0/",
        "sha256": checksum,
        "changes": (
            "Published anonymized file unchanged. Qualification temporally samples the first "
            "20 seconds; calibration and ego telemetry are unavailable. Timestamps are media "
            "timestamps, not an independently validated physical clock."
        ),
    }
    (directory / "attribution.json").write_text(json.dumps(attribution, indent=2), encoding="utf-8")
    print(target)


if __name__ == "__main__":
    main()
