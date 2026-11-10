"""Summarize a completed game run without conflating throughput and correctness."""

import argparse
import hashlib
import json
import math
from pathlib import Path


def rows(path):
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line]


def displacement(a, b):
    return math.sqrt(sum((a[key] - b[key]) ** 2 for key in ("x", "y", "z")))


def vehicle_stalls(world):
    """Use recorded actor roles, never indices from an older population budget."""
    result = []
    for index, activity in enumerate(world[0]["traffic"]):
        role = activity.split(":", 1)[0]
        if role not in {"Car", "Truck", "Bus", "Emergency"}:
            continue
        position = world[0]["vehicle_positions"][index]
        last_motion = world[0]["timestamp"]
        longest = 0
        for row in world:
            current = row["vehicle_positions"][index]
            planned_wait = role == "Emergency" and row["emergency"].startswith(
                ("phase 0:", "phase 4:")
            )
            if displacement(position, current) > 0.5 or planned_wait:
                last_motion, position = row["timestamp"], current
            longest = max(longest, row["timestamp"] - last_motion)
        result.append({"actor_index": index, "role": role, "longest_stationary_seconds": longest})
    return result


def automation_episodes(driving):
    episodes = []
    active = []
    for row in [*driving, None]:
        if row is not None and row.get("automation_accepted", False):
            active.append(row)
        elif active:
            episodes.append(
                {
                    "first_confirmed_seconds": active[0]["timestamp"],
                    "last_confirmed_seconds": active[-1]["timestamp"],
                    "confirmed_span_seconds": active[-1]["timestamp"] - active[0]["timestamp"],
                    "sampled_travel_metres": sum(
                        displacement(a["position"], b["position"])
                        for a, b in zip(active, active[1:], strict=False)
                    ),
                    "peak_speed_mps": max(r["speed_mps"] for r in active),
                    "end_reason": row["control"] if row else "Recording ended while active",
                    "samples": len(active),
                }
            )
            active = []
    return episodes


def service_freshness(driving):
    """Cached readiness is insufficient; older recordings still expose model age."""

    def within(value, limit):
        return isinstance(value, (int, float)) and math.isfinite(value) and 0 <= value < limit

    model_coverage = all("model_frame_age" in r for r in driving)
    command_coverage = all("comma_command_age" in r for r in driving)
    observer_coverage = all("observer_fresh" in r for r in driving)
    healthy = sum(
        bool(r["comma_healthy"])
        and (
            within(r["comma_command_age"], 0.250001)
            if command_coverage
            else within(r.get("model_frame_age"), 1)
        )
        for r in driving
    ) / len(driving)
    return {
        "healthy_command_fraction": healthy if command_coverage or model_coverage else None,
        "command_age_coverage": command_coverage,
        "model_fresh_fraction": (
            sum(within(r["model_frame_age"], 1) for r in driving) / len(driving)
            if model_coverage
            else None
        ),
        "observer_fresh_fraction": (
            sum(bool(r["observer_fresh"]) for r in driving) / len(driving)
            if observer_coverage
            else None
        ),
        "note": (
            "Legacy command health requires fresh model output; "
            "this does not supply missing observer-age measurements."
        ),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--session", required=True)
    args = parser.parse_args()
    if not args.session.isalnum():
        raise ValueError("Expected an opaque session identifier")
    root = args.root.resolve()
    session = root / "outputs/sessions" / args.session
    qualification = root / "outputs/qualification"
    performance = json.loads((qualification / f"performance-{args.session}.json").read_text())
    driving = rows(session / "driving-diagnostics.jsonl")
    world = rows(session / "world-activities.jsonl")
    if not driving or not world:
        raise ValueError("Run has no diagnostic evidence")
    # Unity's first unscaled frame can include scene loading. Require an actual
    # interval of advancing world time rather than crediting loading or pauses.
    simulation_seconds = world[-1]["timestamp"] - world[0]["timestamp"]
    overlaps = [
        {"timestamp": r["timestamp"], "pairs": r["traffic_overlaps"]}
        for r in world
        if r["traffic_overlaps"]
    ]
    rail_coverage = all("rail_overlaps" in r for r in world)
    rail_overlaps = [
        {"timestamp": r["timestamp"], "pairs": r["rail_overlaps"]}
        for r in world
        if r.get("rail_overlaps")
    ]
    freshness = service_freshness(driving)
    healthy = freshness["healthy_command_fraction"]
    maps = sorted({r.get("map", "unrecorded") for r in driving})
    had_qualification = performance.get("driver_mode") == "qualification_driver" or any(
        r["control"] == "Scripted physics/performance test driver" for r in driving
    )
    whole_session_rates = performance.get("throughput_scope") == "whole_session"
    player_anchor = driving[0]["position"]
    player_last_motion = driving[0]["timestamp"]
    player_stationary = 0
    for row in driving:
        if displacement(player_anchor, row["position"]) > 0.5:
            player_last_motion, player_anchor = row["timestamp"], row["position"]
        player_stationary = max(player_stationary, row["timestamp"] - player_last_motion)
    stalls = vehicle_stalls(world)
    ordinary_stalls = [s for s in stalls if s["role"] in {"Car", "Truck"}]
    service_stalls = [s for s in stalls if s["role"] in {"Bus", "Emergency"}]
    emergency_phases = {r["emergency"].split(":")[0] for r in world}
    fingerprints = {}
    for relative in (
        "CoDriverSim_Data/Managed/CoDriver.Game.dll",
        "runtime/Lib/site-packages/codriversim/perception.py",
        "runtime/Lib/site-packages/codriversim/signs.py",
        "scripts/unity-comma.py",
        "scripts/launch-wsl-bridge.py",
        "models/object_detection_nanodet_2022nov.onnx",
    ):
        path = root / relative
        if path.is_file():
            fingerprints[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
    checks = {
        "resolution_1280x720": performance.get("width") == 1280
        and performance.get("height") == 720,
        "qualification_driver_uninterrupted": (
            not performance.get("qualification_interrupted", True) if had_qualification else None
        ),
        "duration_at_least_30_minutes": simulation_seconds >= 1800,
        "mean_fps_at_least_30": performance["mean_fps"] >= 30,
        "single_map_throughout_run": len(maps) == 1 and maps != ["unrecorded"],
        "whole_session_throughput_coverage": whole_session_rates,
        "camera_delivery_near_20_hz": performance["camera_hz"] >= 19.5
        if whole_session_rates
        else None,
        "general_detection_at_least_5_hz": performance["detection_hz"] >= 5
        if whole_session_rates
        else None,
        "comma_healthy_in_at_least_95_percent_of_samples": healthy >= 0.95
        if healthy is not None
        else None,
        "model_fresh_in_at_least_95_percent_of_samples": (
            freshness["model_fresh_fraction"] >= 0.95
            if freshness["model_fresh_fraction"] is not None
            else None
        ),
        "observer_fresh_in_at_least_95_percent_of_samples": (
            freshness["observer_fresh_fraction"] >= 0.95
            if freshness["observer_fresh_fraction"] is not None
            else None
        ),
        "player_contact_free": performance["collision_count"] == 0,
        "player_not_stationary_for_two_minutes": player_stationary < 120,
        "no_logged_traffic_overlaps": not overlaps,
        "no_logged_rail_overlaps": not rail_overlaps if rail_coverage else None,
        "no_ordinary_actor_stationary_for_two_minutes": all(
            s["longest_stationary_seconds"] < 120 for s in ordinary_stalls
        ),
        "bus_and_active_emergency_waits_below_two_minutes": all(
            s["longest_stationary_seconds"] < 120 for s in service_stalls
        ),
        "complete_emergency_cycle_observed": all(
            f"phase {phase}" in emergency_phases for phase in range(8)
        ),
        "parking_departure_and_return_observed": (
            max(r.get("parking_visits", 0) for r in world) > 0
            if all("parking_visits" in r for r in world)
            else None
        ),
    }
    report = {
        "schema_version": 1,
        "session": args.session,
        "release_qualified": False,
        "scope": "Sampled sustained-run evidence; other acceptance gates remain separate",
        "checks": checks,
        "performance": performance,
        "observed_simulation_seconds": simulation_seconds,
        "reviewer_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "comma_healthy_sample_fraction": healthy,
        "service_freshness": freshness,
        "maps_observed": maps,
        "automation_episodes": automation_episodes(driving),
        "player_longest_stationary_seconds": player_stationary,
        "model_geometry": {
            "diagnostic_coverage": all("model_geometry_vertices" in r for r in driving),
            "matching_rendered_samples": sum(
                bool(r.get("matching_model_geometry")) and r.get("model_geometry_vertices", 0) > 0
                for r in driving
            ),
            "total_samples": len(driving),
            "note": "Read-only display availability, not a model accuracy metric",
        },
        "travelled_metres": sum(
            displacement(a["position"], b["position"])
            for a, b in zip(driving, driving[1:], strict=False)
        ),
        "traffic_overlap_samples": overlaps,
        "rail_overlap_samples": rail_overlaps if rail_coverage else None,
        "ordinary_vehicle_stalls": ordinary_stalls,
        "bus_and_active_emergency_stalls": service_stalls,
        "nearby_population_samples": [
            {
                "timestamp": r["timestamp"],
                "people": r["nearby_people"],
                "moving_vehicles": r["nearby_moving_vehicles"],
            }
            for r in world
            if "nearby_people" in r and "nearby_moving_vehicles" in r
        ],
        "reverse_motion_without_reverse_request": [
            {
                "timestamp": r["timestamp"],
                "actor_index": i,
                "activity": r["traffic"][i],
                "signed_speed_mps": speed,
                "contact": r.get("contacts", [""] * len(r["traffic"]))[i],
            }
            for r in world
            for i, speed in enumerate(r.get("signed_vehicle_speeds", []))
            if speed < -0.5 and not r["reverse_requested"][i]
        ],
        "activity_states_observed": {
            key: sorted({r[key] for r in world})
            for key in ("bus", "train", "tram", "parking", "emergency", "construction")
        },
        "individual_actor_states_observed": [
            {"actor_index": index, "states": sorted({r["people"][index] for r in world})}
            for index in range(len(world[0]["people"]))
        ],
        "transit_boarding_samples": {
            key: sum(r[key] > 0 for r in world)
            for key in ("bus_boarding", "train_boarding", "tram_boarding")
        },
        "transit_stop_visits": {
            key: world[-1][key]
            for key in ("bus_stop_visits", "train_stop_visits", "tram_stop_visits")
        },
        "parking_completed_visits": max(r.get("parking_visits", 0) for r in world),
        "files_at_review_sha256": fingerprints,
        "limitations": [
            "Junction and actor observations are sampled, not continuous collision proof",
            "A null rail-overlap check means that build did not record rail overlap diagnostics",
            "Parking state names alone do not establish that a return maneuver finished",
            "Long stationary intervals need review; a queue can be legitimate",
            "Camera target is 20 Hz; 19.5 Hz is the explicitly recorded near-target tolerance",
            "Simulation-only qualification driver is not human or Comma driving performance",
            "File hashes describe files at review time, not proof of the original running build",
            "Legacy service-reported throughput can freeze at disconnect; "
            "missing whole-session coverage cannot pass the rate gates",
        ],
    }
    destination = qualification / f"run-review-{args.session}.json"
    destination.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"report": str(destination), "checks": checks}, indent=2))


if __name__ == "__main__":
    main()
