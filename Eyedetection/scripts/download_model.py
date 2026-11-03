"""Fetch the pinned upstream model once; runtime inference never downloads resources."""

import hashlib
import json
from pathlib import Path
from urllib.request import urlopen

ASSETS = Path(__file__).resolve().parents[1] / "src" / "eyepause" / "assets"


def main() -> None:
    metadata = json.loads((ASSETS / "model.json").read_text(encoding="utf-8"))
    target = ASSETS / "face_landmarker.task"
    if target.is_file() and hashlib.sha256(target.read_bytes()).hexdigest() == metadata["sha256"]:
        print("Face model is already present and verified.")
        return
    with urlopen(metadata["url"], timeout=60) as response:
        payload = response.read(32 * 1024 * 1024)
    if hashlib.sha256(payload).hexdigest() != metadata["sha256"]:
        raise ValueError("Downloaded model does not match the pinned SHA-256 checksum.")
    temporary = target.with_suffix(".tmp")
    temporary.write_bytes(payload)
    temporary.replace(target)
    print("Downloaded and verified face_landmarker.task.")


if __name__ == "__main__":
    main()
