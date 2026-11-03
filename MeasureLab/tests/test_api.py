import json
from importlib.resources import files

import pytest

from measurelab import measure_quad
from measurelab.domain import PlanarReference


def test_identity_plane_and_units():
    reference = PlanarReference(((0, 0), (100, 0), (100, 100), (0, 100)), 100, 100)
    result = measure_quad(reference, ((10, 20), (50, 20), (50, 40), (10, 40)))
    assert result.width_mm == pytest.approx(40)
    assert result.height_mm == pytest.approx(20)
    assert result.units == "mm"


def test_independently_specified_perspective_fixture():
    data = json.loads(files("measurelab").joinpath("examples/demo.json").read_text())
    reference = data["reference"]
    result = measure_quad(
        PlanarReference(tuple(reference["image_corners"]), 100, 100), data["object_corners"]
    )
    assert result.width_mm == pytest.approx(40, abs=1e-8)
    assert result.height_mm == pytest.approx(20, abs=1e-8)
    assert result.rectified_corners[0] == pytest.approx((10, 20))


def test_reference_scaling_changes_units_consistently():
    corners = ((0, 0), (10, 0), (10, 10), (0, 10))
    result = measure_quad(PlanarReference(corners, 200, 100), corners)
    assert result.width_mm == pytest.approx(200)
    assert result.height_mm == pytest.approx(100)


@pytest.mark.parametrize(
    "corners",
    [
        ((0, 0), (1, 0), (2, 0), (3, 0)),
        ((0, 0), (10, 10), (10, 0), (0, 10)),
        ((0, 0),) * 4,
        ((0, 0), (10, 0), (10, float("nan")), (0, 10)),
    ],
)
def test_degenerate_or_unordered_references_are_rejected(corners):
    with pytest.raises(ValueError):
        measure_quad(PlanarReference(corners, 100, 100), ((1, 1), (2, 1), (2, 2), (1, 2)))


@pytest.mark.parametrize("width", [0, -1, float("inf")])
def test_invalid_reference_dimensions_are_rejected(width):
    with pytest.raises(ValueError):
        measure_quad(
            PlanarReference(((0, 0), (10, 0), (10, 10), (0, 10)), width, 100),
            ((1, 1), (2, 1), (2, 2), (1, 2)),
        )


def test_invalid_object_corners_are_rejected():
    with pytest.raises(ValueError):
        measure_quad(PlanarReference(((0, 0), (10, 0), (10, 10), (0, 10)), 100, 100), ())
