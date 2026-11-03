"""Future inference boundaries; no model implementation is supplied by the scaffold."""

from pathlib import Path
from typing import Protocol

from .domain import Box, Frame


class ObjectDetector(Protocol):
    def detect(self, image_path: Path) -> list[Box]: ...


class ObjectTracker(Protocol):
    def update(self, timestamp: float, detections: list[Box]) -> Frame: ...
