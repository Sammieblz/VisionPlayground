"""Hand-authored geometry; no downloaded map data or network access."""

from copy import deepcopy

import pytest

from codriversim.maps import building_height, prepare, project, speed_mph


def district():
    return {
        "id": "fixture",
        "name": "Synthetic map fixture",
        "origin_lat_lon": [0, 0],
        "bbox_south_west_north_east": [-0.01, -0.01, 0.01, 0.01],
        "attribution": "Hand-authored test data",
        "license": "fixture",
        "license_url": "",
    }


def test_projection_declares_east_north_metres_and_rejects_invalid_coordinates():
    assert project(0, 0, [0, 0]) == [0, 0]
    east = project(0, 0.001, [0, 0])
    north = project(0.001, 0, [0, 0])
    assert east == pytest.approx([111.31949, 0], abs=0.001)
    assert north == pytest.approx([0, 111.31949], abs=0.001)
    with pytest.raises(ValueError):
        project(float("nan"), 0, [0, 0])
    with pytest.raises(ValueError):
        project(91, 0, [0, 0])


def test_units_and_unknown_attributes_are_preserved():
    assert speed_mph("25 mph") == 25
    assert speed_mph("40") == pytest.approx(24.85485, abs=0.00001)
    assert speed_mph("40 km/h") == speed_mph("40")
    assert speed_mph("signals") is None
    assert speed_mph("25;35") is None
    assert building_height({"height": "10 ft"}) == pytest.approx(3.048)
    assert building_height({"height": "0"}) is None
    assert building_height({"height": None}) is None
    assert building_height({"building:levels": "3"}) is None


def test_node_references_form_junction_without_inventing_lanes_or_heights():
    payload = {
        "elements": [
            {"type": "node", "id": 1, "lat": 0, "lon": 0},
            {"type": "node", "id": 2, "lat": 0, "lon": 0.001},
            {"type": "node", "id": 3, "lat": 0, "lon": -0.001},
            {"type": "node", "id": 4, "lat": 0.001, "lon": 0},
            {"type": "way", "id": 10, "nodes": [3, 1, 2], "tags": {"highway": "residential"}},
            {
                "type": "way",
                "id": 11,
                "nodes": [1, 4],
                "tags": {"highway": "residential", "maxspeed": "25 mph", "lanes": "2"},
            },
            {
                "type": "way",
                "id": 12,
                "nodes": [1, 2, 4, 1],
                "tags": {"building": "yes", "building:levels": "3"},
            },
        ]
    }
    before = deepcopy(payload)
    layout = prepare(payload, district(), "test-source-hash")
    assert payload == before
    assert layout["source_sha256"] == "test-source-hash"
    assert layout["report"]["road_junction_candidate_nodes"] == 1
    assert layout["report"]["road_sections_with_lane_count"] == 1
    assert layout["report"]["road_sections_with_speed_limit"] == 1
    assert layout["report"]["buildings_with_height"] == 0
    road, known_road, building = layout["features"]
    assert road["lane_count"] is road["maxspeed_mph"] is None
    assert road["oneway"] == road["access"] == "unknown"
    assert known_road["lane_count"] == 2
    assert known_road["maxspeed_mph"] == 25
    assert building["height_m"] is None
    assert building["levels"] == 3
    assert building["path_roles"] == ["outline"]


def test_missing_nodes_or_relation_segments_never_create_geometry_across_gaps():
    payload = {
        "elements": [
            {"type": "node", "id": 1, "lat": 0, "lon": 0},
            {"type": "node", "id": 2, "lat": 0.001, "lon": 0.001},
            {"type": "way", "id": 10, "nodes": [1, 99, 2], "tags": {"highway": "residential"}},
            {
                "type": "relation",
                "id": 20,
                "tags": {"building": "yes"},
                "members": [{"type": "way", "ref": 10, "role": "outer"}],
            },
            {
                "type": "relation",
                "id": 21,
                "tags": {"building": "yes"},
                "members": [
                    {
                        "type": "way",
                        "ref": 11,
                        "role": "outer",
                        "geometry": [{"lat": 0, "lon": 0}, None, {"lat": 0.001, "lon": 0.001}],
                    }
                ],
            },
        ]
    }
    layout = prepare(payload, district(), "fixture")
    assert layout["features"] == []
    assert layout["report"]["missing_geometry"] == ["way/10", "relation/20", "relation/21"]


def test_pedestrian_area_is_not_a_drivable_road_and_relation_roles_survive():
    ring = [{"lat": 0, "lon": 0}, {"lat": 0.001, "lon": 0.001}, {"lat": 0, "lon": 0}]
    payload = {
        "elements": [
            {
                "type": "way",
                "id": 1,
                "geometry": ring,
                "tags": {"highway": "service", "area": "yes"},
            },
            {
                "type": "relation",
                "id": 2,
                "tags": {"building": "yes"},
                "members": [
                    {"type": "way", "ref": 3, "role": "outer", "geometry": ring},
                    {"type": "way", "ref": 4, "role": "inner", "geometry": ring},
                ],
            },
        ]
    }
    layout = prepare(payload, district(), "fixture")
    assert layout["features"][0]["kind"] == "path"
    assert layout["features"][1]["path_roles"] == ["outer", "inner"]
    assert layout["report"]["road_junction_candidate_nodes"] == 0
