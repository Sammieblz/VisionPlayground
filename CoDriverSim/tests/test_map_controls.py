from codriversim.map_controls import assign_stop_lanes, stop_parent_roads


def fixture(direction="forward"):
    elements = [
        dict(type="node", id=7, tags=dict(highway="stop", direction=direction)),
        dict(type="way", id=11, nodes=[6, 7, 8], tags=dict(highway="service")),
        dict(type="way", id=12, nodes=[2, 3], tags=dict(highway="residential")),
    ]
    feature = dict(source_id="node/7", osm_tags=dict(direction=direction), paths_xz_m=[[[0, 0]]])
    facts = {"way/11": dict(paths_xz_m=[[[-10, 0], [10, 0]]])}
    east = dict(
        id="11#0_0",
        sourceId="way/11",
        internalLane=False,
        points=[dict(x=-10, z=-1), dict(x=10, z=-1)],
    )
    west = dict(
        id="-11#0_0",
        sourceId="way/11",
        internalLane=False,
        points=[dict(x=10, z=1), dict(x=-10, z=1)],
    )
    return feature, stop_parent_roads(elements), facts, [east, west]


def test_stop_sign_on_omitted_alley_never_controls_adjacent_main_road():
    feature, parents, facts, lanes = fixture()
    for lane in lanes:
        lane["sourceId"] = "way/12"
    assert assign_stop_lanes(feature, parents, facts, lanes) == []


def test_osm_forward_and_backward_signs_control_their_own_approaches():
    assert assign_stop_lanes(*fixture("forward")) == ["11#0_0"]
    assert assign_stop_lanes(*fixture("backward")) == ["-11#0_0"]


def test_parallel_lanes_share_control_but_unknown_direction_is_not_guessed():
    feature, parents, facts, lanes = fixture()
    lanes.append(dict(lanes[0], id="11#0_1"))
    assert assign_stop_lanes(feature, parents, facts, lanes) == ["11#0_0", "11#0_1"]
    assert assign_stop_lanes(*fixture("unsupported")) == []
