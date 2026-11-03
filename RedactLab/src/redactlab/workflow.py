from io import BytesIO
from pathlib import Path

from PIL import Image

from .api import create_redactions
from .domain import DetectionRegion
from .validation import box, mapping, sequence, text


def process(data: dict, base: Path) -> tuple[dict, dict[str, bytes], tuple[Path, ...]]:
    image_path = (base / text(data.get("image"), "image")).resolve()
    regions = []
    for value in sequence(data.get("regions"), "regions"):
        value = mapping(value, "region")
        regions.append(
            DetectionRegion(
                text(value.get("region_id"), "region_id"),
                text(value.get("category"), "category"),
                box(value.get("box"), "box"),
            )
        )
    with Image.open(image_path) as image:
        result = create_redactions(image, regions, data.get("categories"))
    artifacts = {}
    for name, image in (("mask.png", result.mask), ("redacted.png", result.redacted)):
        stream = BytesIO()
        image.save(stream, format="PNG")
        artifacts[name] = stream.getvalue()
    return result.summary(), artifacts, (image_path,)
