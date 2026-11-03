"""Extract explicitly labeled English receipt lines from supplied OCR output."""

import datetime
import re
from decimal import Decimal

from .domain import Field, OCRDocument, Receipt
from .validation import box, integer, text

TOTAL = re.compile(
    r"total\s*:\s*(?:(USD|EUR|GBP)\s+)?(\d+(?:\.\d{1,2})?)(?:\s+(USD|EUR|GBP))?", re.IGNORECASE
)


def extract_fields(document: OCRDocument) -> Receipt:
    """Recognize Merchant:, Date:, Total:, Currency: lines, preserving provenance.

    Each region is a whole OCR line. Dates must be valid ISO dates. Amounts use
    a decimal point, and supported explicit currency codes are USD/EUR/GBP.
    Conflicting candidates are marked ambiguous rather than guessed.
    """
    width = integer(document.width, "width", minimum=1)
    height = integer(document.height, "height", minimum=1)
    candidates: dict[str, list[tuple[str, str]]] = {
        k: [] for k in ("merchant", "date", "total", "currency")
    }
    seen = set()
    for region in document.regions:
        region_id = text(region.region_id, "region_id")
        if region_id in seen:
            raise ValueError("OCR region IDs must be unique.")
        seen.add(region_id)
        left, top, right, bottom = box(region.box, "region.box")
        if left < 0 or top < 0 or right > width or bottom > height:
            raise ValueError("OCR region boxes must lie inside the document.")
        if not isinstance(region.text, str):
            raise ValueError("OCR region text must be a string.")
        content = region.text.strip()
        merchant = re.fullmatch(r"merchant\s*:\s*(.+)", content, re.IGNORECASE)
        date = re.fullmatch(r"date\s*:\s*(\d{4}-\d{2}-\d{2})", content, re.IGNORECASE)
        currency = re.fullmatch(r"currency\s*:\s*(USD|EUR|GBP)", content, re.IGNORECASE)
        total = TOTAL.fullmatch(content)
        if merchant:
            candidates["merchant"].append((merchant[1].strip(), region_id))
        if date:
            try:
                value = datetime.date.fromisoformat(date[1]).isoformat()
                candidates["date"].append((value, region_id))
            except ValueError:
                pass
        if currency:
            candidates["currency"].append((currency[1].upper(), region_id))
        if total:
            candidates["total"].append((format(Decimal(total[2]), ".2f"), region_id))
            for code in (total[1], total[3]):
                if code:
                    candidates["currency"].append((code.upper(), region_id))

    def resolve(name: str) -> Field:
        entries = candidates[name]
        values = {value for value, _ in entries}
        sources = tuple(sorted({source for _, source in entries}))
        if not values:
            return Field(None, (), "missing")
        if len(values) > 1:
            return Field(None, sources, "ambiguous")
        return Field(next(iter(values)), sources, "found")

    return Receipt(*(resolve(name) for name in ("merchant", "date", "total", "currency")))
