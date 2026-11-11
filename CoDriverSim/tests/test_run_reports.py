"""Reports must cover the whole configured population, not a legacy index subset."""

import importlib.util
from pathlib import Path

spec = importlib.util.spec_from_file_location(
    "run_report", Path(__file__).resolve().parents[1] / "scripts" / "summarize-game-run.py"
)
report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(report)


def test_stalls_cover_twenty_cars_and_classify_services_by_recorded_role():
    roles = ["Emergency", "Parking", "Bus"] + ["Car"] * 19 + ["Truck"]
    world = [
        dict(
            timestamp=t,
            traffic=[r + ":waiting" for r in roles],
            vehicle_positions=[dict(x=0, y=0, z=0) for _ in roles],
            emergency="phase 0: At station",
        )
        for t in (0, 60, 130)
    ]
    result = report.vehicle_stalls(world)
    assert len(result) == 22
    assert len([s for s in result if s["role"] in {"Car", "Truck"}]) == 20
    assert result[0]["longest_stationary_seconds"] == 0
    assert result[-1]["longest_stationary_seconds"] == 130
    assert next(s for s in result if s["role"] == "Bus")["longest_stationary_seconds"] == 130


def test_unfinished_physical_crossing_is_not_hidden_by_moving_traffic():
    world = [
        dict(
            timestamp=t,
            people=["Person:Crossing", "Person:Waiting for transit", "Cat:Idling"],
            actor_positions=[dict(x=-341.45282, y=0, z=113.36115)] * 3,
        )
        for t in (0, 60, 121)
    ]
    assert report.crossing_stalls(world) == [
        {"actor_index": 0, "role": "Person", "longest_stationary_seconds": 121}
    ]


def test_crossing_measurement_resets_after_motion_and_between_separate_crossings():
    world = [
        dict(timestamp=t, people=["Dog:" + state], actor_positions=[dict(x=x, y=0, z=0)])
        for t, x, state in (
            (0, 0, "Crossing"),
            (40, 0, "Crossing"),
            (60, 1, "Crossing"),
            (80, 1, "Crossing"),
            (100, 1, "Idling"),
            (900, 1, "Crossing"),
            (930, 1, "Crossing"),
        )
    ]
    assert report.crossing_stalls(world)[0]["longest_stationary_seconds"] == 40
    del world[-1]["actor_positions"]
    assert report.crossing_stalls(world) is None


def test_separate_engagements_are_not_reported_as_one_continuous_drive():
    def row(t, active, reason):
        return dict(
            timestamp=t,
            automation_accepted=active,
            control=reason,
            position=dict(x=0, y=0, z=t),
            speed_mps=1,
        )

    episodes = report.automation_episodes(
        [
            row(1, True, "Stock Comma"),
            row(2, True, "Stock Comma"),
            row(3, False, "Comma connection stale"),
            row(20, True, "Stock Comma"),
            row(22, True, "Stock Comma"),
            row(23, False, "Human takeover"),
        ]
    )
    assert [e["confirmed_span_seconds"] for e in episodes] == [1, 2]
    assert [e["sampled_travel_metres"] for e in episodes] == [1, 2]
    assert [e["end_reason"] for e in episodes] == ["Comma connection stale", "Human takeover"]


def test_cached_ready_flag_cannot_hide_a_lost_model_stream_in_legacy_recordings():
    rows = [dict(comma_healthy=True, model_frame_age=age) for age in (0.2, 0.3, 15, 300)]
    result = report.service_freshness(rows)
    assert result["healthy_command_fraction"] == 0.5
    assert result["model_fresh_fraction"] == 0.5
    assert result["observer_fresh_fraction"] is None


def test_command_observer_and_model_freshness_are_independent():
    rows = [
        dict(comma_healthy=True, comma_command_age=0.1, model_frame_age=0.2, observer_fresh=True),
        dict(comma_healthy=True, comma_command_age=10, model_frame_age=0.2, observer_fresh=False),
        dict(comma_healthy=True, comma_command_age=0.1, model_frame_age=10, observer_fresh=False),
    ]
    result = report.service_freshness(rows)
    assert result["healthy_command_fraction"] == 2 / 3
    assert result["model_fresh_fraction"] == 2 / 3
    assert result["observer_fresh_fraction"] == 1 / 3
