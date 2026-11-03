from dataclasses import asdict
from pathlib import Path

from .api import measure_quad
from .domain import PlanarReference
from .validation import mapping, number, point, sequence


def process(data: dict, base: Path) -> tuple[dict, dict[str, bytes], tuple[Path, ...]]:
    reference = mapping(data.get("reference"), "reference")
    corners = tuple(
        point(p, "reference corner")
        for p in sequence(reference.get("image_corners"), "image_corners")
    )
    result = measure_quad(
        PlanarReference(
            corners,
            number(reference.get("width_mm"), "width_mm"),
            number(reference.get("height_mm"), "height_mm"),
        ),
        tuple(
            point(p, "object corner")
            for p in sequence(data.get("object_corners"), "object_corners")
        ),
    )
    return asdict(result), {}, ()
