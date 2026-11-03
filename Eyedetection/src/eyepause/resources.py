"""Resource paths work both from source and inside the portable directory build."""

import hashlib
import json
from pathlib import Path

ASSETS = Path(__file__).resolve().parent / "assets"
MODEL_PATH = ASSETS / "face_landmarker.task"


def verify_model(path: Path = MODEL_PATH) -> None:
    if not path.is_file():
        raise FileNotFoundError(
            "Face model is missing. Run python scripts/download_model.py first."
        )
    metadata = json.loads((ASSETS / "model.json").read_text(encoding="utf-8"))
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    if digest != metadata["sha256"]:
        raise ValueError("Face model checksum does not match. Download it again.")
