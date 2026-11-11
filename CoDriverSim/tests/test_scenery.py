import math

import pytest

from codriversim.scenery import plan_buildings

pytest.importorskip("shapely", reason="Building placement uses the optional simulator dependencies")


def test_buildings_leave_the_street_and_sidewalk_clear():
    # A rectangular 120 m street, 12 m wide, including every driving lane.
    roads = [[(0, -6), (120, -6), (120, 6), (0, 6)]]
    buildings = plan_buildings(roads, seed=21)
    assert buildings
    for building in buildings:
        left, right = building.x - building.width / 2, building.x + building.width / 2
        bottom, top = building.y - building.depth / 2, building.y + building.depth / 2
        dx = max(0, left - 120, -right)
        dy = max(0, bottom - 6, -6 - top)
        assert math.hypot(dx, dy) >= 8


def test_buildings_do_not_overlap_and_reuse_the_same_seed():
    roads = [[(0, -6), (120, -6), (120, 6), (0, 6)]]
    buildings = plan_buildings(roads, seed=21)
    assert buildings == plan_buildings(roads, seed=21)
    assert buildings != plan_buildings(roads, seed=42)
    for index, first in enumerate(buildings):
        for second in buildings[index + 1 :]:
            separated_x = abs(first.x - second.x) >= (first.width + second.width) / 2
            separated_y = abs(first.y - second.y) >= (first.depth + second.depth) / 2
            assert separated_x or separated_y


def test_no_road_data_means_no_buildings():
    assert plan_buildings([], seed=21) == []
