"""OCR observations and receipt fields with explicit source attribution."""

from dataclasses import dataclass

Box = tuple[float, float, float, float]


@dataclass(frozen=True)
class OCRRegion:
    region_id: str
    text: str
    box: Box


@dataclass(frozen=True)
class OCRDocument:
    width: int
    height: int
    regions: tuple[OCRRegion, ...]


@dataclass(frozen=True)
class Field:
    value: str | None
    source_ids: tuple[str, ...]
    status: str


@dataclass(frozen=True)
class Receipt:
    merchant: Field
    date: Field
    total: Field
    currency: Field
