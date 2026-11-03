from dataclasses import dataclass

Point = tuple[float, float]


@dataclass(frozen=True)
class PlanarReference:
    image_corners: tuple[Point, ...]
    width_mm: float
    height_mm: float


@dataclass(frozen=True)
class Measurement:
    width_mm: float
    height_mm: float
    rectified_corners: tuple[Point, ...]
    units: str = "mm"
