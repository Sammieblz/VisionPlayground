"""Apply supplied regions to a copy of an image; no detector or OCR runs here."""

import math
from collections.abc import Collection, Sequence

from PIL import Image, ImageDraw

from .domain import DetectionRegion, RedactionResult, SelectedRegion
from .validation import box, sequence, text

SUPPORTED_CATEGORIES = frozenset({"face", "plate", "sensitive_text"})


def create_redactions(
    image: Image.Image,
    regions: Sequence[DetectionRegion],
    categories: Collection[str] | None = None,
) -> RedactionResult:
    """Create a binary L-mode mask and blacked-out RGB copy.

    Boxes use exclusive right/bottom boundaries, round outward, and are clipped
    to the image. Unselected categories and wholly off-image boxes are skipped.
    Source pixels and the input detection records are never modified.
    """
    if not isinstance(image, Image.Image) or image.width <= 0 or image.height <= 0:
        raise ValueError("A nonempty Pillow image is required.")
    selected = (
        SUPPORTED_CATEGORIES
        if categories is None
        else frozenset(text(c, "category") for c in sequence(categories, "categories"))
    )
    if not selected.issubset(SUPPORTED_CATEGORIES):
        raise ValueError("Categories must be face, plate, or sensitive_text.")
    mask = Image.new("L", image.size, 0)
    painter = ImageDraw.Draw(mask)
    output_regions = []
    seen = set()
    for region in regions:
        region_id = text(region.region_id, "region_id")
        if region_id in seen:
            raise ValueError("Detection region IDs must be unique.")
        seen.add(region_id)
        category = text(region.category, "category")
        left, top, right, bottom = box(region.box, "region.box")
        if category not in selected:
            continue
        clipped = (
            max(0, min(image.width, math.floor(left))),
            max(0, min(image.height, math.floor(top))),
            max(0, min(image.width, math.ceil(right))),
            max(0, min(image.height, math.ceil(bottom))),
        )
        x1, y1, x2, y2 = clipped
        if x1 >= x2 or y1 >= y2:
            continue
        painter.rectangle((x1, y1, x2 - 1, y2 - 1), fill=255)
        output_regions.append(SelectedRegion(region_id, category, clipped))
    redacted = image.convert("RGB")
    redacted.paste((0, 0, 0), (0, 0, image.width, image.height), mask)
    return RedactionResult(tuple(output_regions), mask, redacted)
