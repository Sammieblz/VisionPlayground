from dataclasses import asdict
from pathlib import Path

from .api import extract_fields
from .domain import OCRDocument, OCRRegion
from .validation import box, integer, mapping, sequence, text


def process(data: dict, base: Path) -> tuple[dict, dict[str, bytes], tuple[Path, ...]]:
    regions = []
    for value in sequence(data.get("regions"), "regions"):
        value = mapping(value, "region")
        if not isinstance(value.get("text"), str):
            raise ValueError("Region text must be a string.")
        regions.append(
            OCRRegion(
                text(value.get("region_id"), "region_id"),
                value["text"],
                box(value.get("box"), "box"),
            )
        )
    document = OCRDocument(
        integer(data.get("width"), "width", minimum=1),
        integer(data.get("height"), "height", minimum=1),
        tuple(regions),
    )
    return asdict(extract_fields(document)), {}, ()
