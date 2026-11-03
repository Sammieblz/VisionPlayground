"""Replaceable OCR boundary; implementations must group words into text lines."""

from pathlib import Path
from typing import Protocol

from .domain import OCRDocument


class OCREngine(Protocol):
    def recognize(self, image_path: Path) -> OCRDocument: ...
