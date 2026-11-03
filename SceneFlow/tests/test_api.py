import pytest

from sceneflow import analyze_tracks
from sceneflow.domain import DirectedLine, Frame, Track

LINE = DirectedLine((0, -10), (0, 10))


def frame(time, *positions):
    return Frame(
        time, tuple(Track(name, (x - 0.5, y - 0.5, x + 0.5, y + 0.5)) for name, x, y in positions)
    )


def test_touch_then_cross_then_reverse_counts_each_transition_once():
    report = analyze_tracks(
        [
            frame(0, ("a", -2, 0)),
            frame(1, ("a", 0, 0)),
            frame(2, ("a", 2, 0)),
            frame(3, ("a", 3, 0)),
            frame(4, ("a", -2, 0)),
        ],
        LINE,
    )
    assert report.positive_to_negative == 1
    assert report.negative_to_positive == 1
    assert [(e.timestamp, e.direction) for e in report.events] == [
        (2, "positive_to_negative"),
        (4, "negative_to_positive"),
    ]


def test_touch_and_return_does_not_cross():
    assert not analyze_tracks(
        [frame(0, ("a", -1, 0)), frame(1, ("a", 0, 0)), frame(2, ("a", -1, 0))], LINE
    ).events


def test_missing_observation_breaks_continuity():
    assert not analyze_tracks(
        [frame(0, ("a", -1, 0)), frame(1), frame(2, ("a", 1, 0))], LINE
    ).events


def test_line_is_a_finite_segment():
    assert not analyze_tracks([frame(0, ("a", -1, 20)), frame(1, ("a", 1, 20))], LINE).events


def test_crossing_through_segment_endpoint_is_counted():
    assert len(analyze_tracks([frame(0, ("a", -1, 10)), frame(1, ("a", 1, 10))], LINE).events) == 1


def test_following_boundary_outside_segment_does_not_invent_crossing():
    assert not analyze_tracks(
        [frame(0, ("a", -1, 20)), frame(1, ("a", 0, 20)), frame(2, ("a", 1, 20))], LINE
    ).events


def test_starting_on_line_does_not_establish_prior_side():
    assert not analyze_tracks([frame(0, ("a", 0, 0)), frame(1, ("a", 1, 0))], LINE).events


def test_empty_tracks_and_reversed_line_direction():
    assert analyze_tracks([], LINE).events == ()
    report = analyze_tracks(
        [frame(0, ("a", -1, 0)), frame(1, ("a", 1, 0))], DirectedLine(LINE.end, LINE.start)
    )
    assert report.negative_to_positive == 1


@pytest.mark.parametrize(
    "frames",
    [
        [frame(0), frame(0)],
        [frame(1), frame(0)],
        [frame(-1)],
        [frame(float("nan"))],
        [frame(0, ("a", 0, 0), ("a", 2, 2))],
        [Frame(0, (Track("a", (0, 0, 0, 2)),))],
    ],
)
def test_invalid_track_data_is_rejected(frames):
    with pytest.raises(ValueError):
        analyze_tracks(frames, LINE)


def test_degenerate_line_is_rejected():
    with pytest.raises(ValueError):
        analyze_tracks([], DirectedLine((0, 0), (0, 0)))
