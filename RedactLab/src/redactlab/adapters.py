from pathlib import Path
from typing import Protocol

from .domain import DetectionRegion


class RegionDetector(Protocol):
    """Future face/plate/OCR adapters use pixel xyxy regions without identity inference."""

    def detect(self, image_path: Path) -> list[DetectionRegion]: ...
