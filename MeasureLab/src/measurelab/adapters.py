from pathlib import Path
from typing import Protocol

from .domain import PlanarReference, Point


class ReferenceDetector(Protocol):
    def locate(self, image_path: Path) -> PlanarReference: ...


class OutlineDetector(Protocol):
    def locate(self, image_path: Path) -> tuple[Point, ...]: ...
