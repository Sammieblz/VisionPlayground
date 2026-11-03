import pytest

from doclens import extract_fields
from doclens.domain import OCRDocument, OCRRegion


def document(*lines):
    return OCRDocument(
        500,
        500,
        tuple(
            OCRRegion(str(i), line, (10, 10 + i * 25, 450, 30 + i * 25))
            for i, line in enumerate(lines)
        ),
    )


def test_explicit_fields_preserve_sources_and_decimal_format():
    result = extract_fields(
        document(
            "Merchant: Example Shop", "Date: 2024-02-29", "Subtotal: USD 99.00", "Total: USD 12.5"
        )
    )
    assert result.merchant.value == "Example Shop"
    assert result.date.value == "2024-02-29"
    assert result.total.value == "12.50"
    assert result.total.source_ids == ("3",)
    assert result.currency.value == "USD"
    assert result.currency.source_ids == ("3",)


def test_subtotal_is_not_total_and_missing_fields_are_unset():
    result = extract_fields(document("Subtotal: 10.00", "Thank you"))
    assert result.total.value is None and result.total.status == "missing"
    assert result.merchant.value is None


def test_conflicting_fields_are_ambiguous_with_both_sources():
    result = extract_fields(document("Total: 10.00", "Total: 11.00"))
    assert result.total.value is None
    assert result.total.status == "ambiguous"
    assert result.total.source_ids == ("0", "1")


def test_repeated_same_value_has_all_sources_without_becoming_ambiguous():
    result = extract_fields(document("Total: 10", "Total: 10.00"))
    assert result.total.value == "10.00"
    assert result.total.source_ids == ("0", "1")


def test_currency_conflict_and_invalid_date_are_not_guessed():
    result = extract_fields(document("Date: 2023-02-29", "Currency: EUR", "Total: USD 8.00"))
    assert result.date.value is None
    assert result.currency.status == "ambiguous"


def test_empty_document_returns_four_missing_fields():
    result = extract_fields(document())
    assert all(
        getattr(result, key).status == "missing"
        for key in ("merchant", "date", "total", "currency")
    )


@pytest.mark.parametrize(
    "doc",
    [
        OCRDocument(0, 10, ()),
        OCRDocument(10, -1, ()),
        OCRDocument(10, 10, (OCRRegion("a", "Total: 1", (0, 0, 20, 5)),)),
        OCRDocument(
            10, 10, (OCRRegion("a", "x", (0, 0, 5, 5)), OCRRegion("a", "y", (0, 5, 5, 10)))
        ),
    ],
)
def test_invalid_document_geometry_and_provenance_are_rejected(doc):
    with pytest.raises(ValueError):
        extract_fields(doc)
