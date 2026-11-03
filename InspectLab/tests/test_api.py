import pytest

from inspectlab import inspect_map


def test_four_connected_regions_and_exclusive_boxes():
    result = inspect_map([[0.9, 0.8, 0], [0, 0, 0], [0, 0, 1]], 0.7)
    assert result.decision == "review"
    assert result.anomalous_pixels == 3
    assert [r.box for r in result.regions] == [(0, 0, 2, 1), (2, 2, 3, 3)]
    assert result.regions[0].area == 2
    assert result.regions[0].mean_score == pytest.approx(0.85)


def test_diagonal_pixels_are_separate_regions():
    assert len(inspect_map([[1, 0], [0, 1]], 0.5).regions) == 2


def test_threshold_is_inclusive_and_scores_are_not_probabilities():
    result = inspect_map([[5.0, -1.0]], 5.0)
    assert result.anomalous_pixels == 1
    assert result.regions[0].max_score == 5.0


def test_normal_map_passes_without_regions():
    result = inspect_map([[0, 0.1], [0.2, 0.1]], 0.7)
    assert result.decision == "pass" and result.regions == ()


@pytest.mark.parametrize(
    "scores,threshold",
    [
        ([], 0.5),
        ([[]], 0.5),
        ([[1], [1, 2]], 0.5),
        ([[float("nan")]], 0.5),
        ([[1]], float("inf")),
        ([[True]], 0.5),
        ("bad", 0.5),
    ],
)
def test_invalid_maps_are_rejected(scores, threshold):
    with pytest.raises(ValueError):
        inspect_map(scores, threshold)
