from dataclasses import asdict
from pathlib import Path

from .api import search_embeddings
from .domain import IndexedItem
from .validation import integer, mapping, number, sequence, text


def process(data: dict, base: Path) -> tuple[dict, dict[str, bytes], tuple[Path, ...]]:
    items = []
    for value in sequence(data.get("items"), "items"):
        value = mapping(value, "item")
        uri = value.get("uri", "")
        if not isinstance(uri, str):
            raise ValueError("Item URI must be a string.")
        items.append(
            IndexedItem(
                text(value.get("item_id"), "item_id"),
                tuple(
                    number(v, "embedding") for v in sequence(value.get("embedding"), "embedding")
                ),
                uri,
                value.get("timestamp"),
            )
        )
    matches = search_embeddings(data.get("query"), items, integer(data.get("k", 5), "k", minimum=1))
    return {"matches": [asdict(match) for match in matches]}, {}, ()
