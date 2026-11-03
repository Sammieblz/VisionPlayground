import pytest

from framefind import search_embeddings
from framefind.domain import IndexedItem


def test_cosine_ranking_is_normalized_and_stably_tied_by_id():
    items = [
        IndexedItem("z", (2, 0)),
        IndexedItem("a", (1, 0)),
        IndexedItem("orthogonal", (0, 1)),
        IndexedItem("opposite", (-1, 0)),
    ]
    matches = search_embeddings((100, 0), items, 10)
    assert [m.item_id for m in matches] == ["a", "z", "orthogonal", "opposite"]
    assert [m.score for m in matches] == pytest.approx([1, 1, 0, -1])


def test_uri_and_timestamp_are_returned_without_opening_media():
    match = search_embeddings((1, 0), [IndexedItem("frame", (1, 0), "missing.mp4", 3.5)], 1)[0]
    assert match.uri == "missing.mp4" and match.timestamp == 3.5


def test_empty_index_and_k_larger_than_index():
    assert search_embeddings((1,), [], 1) == ()
    assert len(search_embeddings((1,), [IndexedItem("a", (1,))], 9)) == 1


@pytest.mark.parametrize("scale", [1e-308, 1e308])
def test_extreme_finite_vectors_are_normalized_without_overflow(scale):
    match = search_embeddings((scale, scale), [IndexedItem("a", (1, 1))], 1)[0]
    assert match.score == pytest.approx(1)


@pytest.mark.parametrize(
    "query,items,k",
    [
        ((), [], 1),
        ((0, 0), [], 1),
        ((float("nan"),), [], 1),
        ((1,), [], 0),
        ((1,), [IndexedItem("a", (1, 2))], 1),
        ((1,), [IndexedItem("a", (0,))], 1),
        ((1,), [IndexedItem("a", (1,)), IndexedItem("a", (1,))], 1),
        ((1,), [IndexedItem("a", (1,), timestamp=-1)], 1),
    ],
)
def test_invalid_vectors_and_index_records_are_rejected(query, items, k):
    with pytest.raises(ValueError):
        search_embeddings(query, items, k)
