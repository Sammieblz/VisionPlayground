from __future__ import annotations

from dataclasses import asdict, dataclass
from typing import TYPE_CHECKING

if TYPE_CHECKING:
    from PIL import Image

Box = tuple[float, float, float, float]


@dataclass(frozen=True)
class DetectionRegion:
    region_id: str
    category: str
    box: Box


@dataclass(frozen=True)
class SelectedRegion:
    region_id: str
    category: str
    mask_box: tuple[int, int, int, int]


@dataclass(frozen=True)
class RedactionResult:
    regions: tuple[SelectedRegion, ...]
    mask: Image.Image
    redacted: Image.Image

    def summary(self) -> dict:
        return {
            "width": self.mask.width,
            "height": self.mask.height,
            "redacted_pixels": self.mask.histogram()[255],
            "regions": [asdict(region) for region in self.regions],
        }
