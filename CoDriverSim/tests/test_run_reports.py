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
