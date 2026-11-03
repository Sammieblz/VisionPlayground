"""Cosine search over supplied vectors; this scaffold contains no semantic encoder."""

import math
from collections.abc import Sequence

from .domain import IndexedItem, SearchMatch
from .validation import integer, number, sequence, text


def _unit(value: Sequence[float], name: str) -> tuple[float, ...]:
    vector = tuple(number(component, name) for component in sequence(value, name))
    if not vector:
        raise ValueError(f"{name} cannot be empty.")
    scale = max(abs(component) for component in vector)
    if scale == 0:
        raise ValueError(f"{name} cannot have zero magnitude.")
    scaled = tuple(component / scale for component in vector)
    norm = math.sqrt(math.fsum(component * component for component in scaled))
    return tuple(component / norm for component in scaled)


def search_embeddings(
    query: Sequence[float], items: Sequence[IndexedItem], k: int = 5
) -> tuple[SearchMatch, ...]:
    """Rank by descending cosine similarity, breaking ties by item ID.

    The index is in memory and URIs are returned without being opened. Normalized
    arithmetic supports very small/large finite components without norm overflow.
    """
    k = integer(k, "k", minimum=1)
    query_unit = _unit(query, "query")
    matches = []
    seen = set()
    for item in items:
        item_id = text(item.item_id, "item_id")
        if item_id in seen:
            raise ValueError("Indexed item IDs must be unique.")
        seen.add(item_id)
        unit = _unit(item.embedding, "embedding")
        if len(unit) != len(query_unit):
            raise ValueError("Query and indexed embeddings must have matching dimensions.")
        if not isinstance(item.uri, str):
            raise ValueError("Item URI must be a string.")
        timestamp = None if item.timestamp is None else number(item.timestamp, "timestamp")
        if timestamp is not None and timestamp < 0:
            raise ValueError("Timestamps must be nonnegative seconds.")
        score = max(-1.0, min(1.0, math.fsum(a * b for a, b in zip(query_unit, unit, strict=True))))
        matches.append(SearchMatch(item_id, score, item.uri, timestamp))
    return tuple(sorted(matches, key=lambda item: (-item.score, item.item_id))[:k])
