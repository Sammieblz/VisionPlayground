from dataclasses import dataclass


@dataclass(frozen=True)
class IndexedItem:
    item_id: str
    embedding: tuple[float, ...]
    uri: str = ""
    timestamp: float | None = None


@dataclass(frozen=True)
class SearchMatch:
    item_id: str
    score: float
    uri: str
    timestamp: float | None
