"""Audit every generated Davis lane connection and physical turn envelope.

This is a geometry/access report, not a claim of surveyed traffic rules. Re-run
after map changes; unresolved envelopes remain explicit release gates.
"""

import argparse
import hashlib
import json
import math
from pathlib import Path

from shapely.geometry import LineString, Polygon
from shapely.ops import unary_union

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--write-routing",
        action="store_true",
        help="Exclude geometrically unfit inferred turns from automatic routes",
    )
    args = parser.parse_args()
    path = ROOT / "game/Assets/Resources/Maps/davis-downtown.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    lanes = {lane["id"]: lane for lane in data["lanes"]}
    errors = []
    links = []
    for lane in lanes.values():
        for successor in lane["next"]:
            if successor not in lanes:
                errors.append(f"{lane['id']}: missing {successor}")
                continue
            end = lane["points"][-1]
            start = lanes[successor]["points"][0]
            gap = math.dist(
                tuple(end[k] for k in ("x", "y", "z")), tuple(start[k] for k in ("x", "y", "z"))
            )
            links.append(
                {
                    "from": lane["id"],
                    "to": successor,
                    "gap_m": round(gap, 4),
                    "source_from": lane["sourceId"],
                    "source_to": lanes[successor]["sourceId"],
                }
            )
            if gap > 1.5:
                errors.append(f"{lane['id']} -> {successor}: {gap:.3f} m gap")
        if lane.get("access") in ("no", "private"):
            errors.append(f"{lane['id']}: forbidden public routing access")
    triangles = []
    for key in ("roadSurface", "underpassSurface"):
        mesh = data[key]
        v = mesh["vertices"]
        t = mesh["triangles"]
        triangles.extend(
            Polygon([(v[i]["x"], v[i]["z"]) for i in t[j : j + 3]]) for j in range(0, len(t), 3)
        )
    pavement = unary_union(triangles).buffer(0.03)

    def horizontal_surface(key):
        surface = data[key]
        vertices, indices = surface["vertices"], surface["triangles"]
        return unary_union(
            [
                Polygon([(vertices[i]["x"], vertices[i]["z"]) for i in indices[j : j + 3]])
                for j in range(0, len(indices), 3)
            ]
        )

    raised_overlap = (
        horizontal_surface("sidewalkSurface").intersection(horizontal_surface("roadSurface")).area
    )
    if raised_overlap > 0.01:
        errors.append(
            f"Raised pedestrian surface overlaps road by {raised_overlap:.3f} square metres"
        )
    envelopes = []
    for lane in lanes.values():
        if not lane["internalLane"] or lane["boundary"]:
            continue
        points = [(p["x"], p["z"]) for p in lane["points"]]
        line = LineString(points)
        if line.length < 0.05:
            continue
        turns = []
        for width, length, wheelbase, steering, name in (
            (1.82, 4.4, 2.7, 34, "car"),
            (2.35, 10, 5.5, 38, "bus"),
        ):
            outside = 0.0
            minimum_radius = 1e6
            for step in range(math.ceil(line.length) + 1):
                d = min(step, line.length)
                p = line.interpolate(d)
                a = line.interpolate(max(0, d - 0.5))
                b = line.interpolate(min(line.length, d + 0.5))
                dx, dz = b.x - a.x, b.y - a.y
                mag = math.hypot(dx, dz)
                if mag < 1e-6:
                    continue
                fx, fz = dx / mag, dz / mag
                rx, rz = fz, -fx
                shell = Polygon(
                    [
                        (
                            p.x + fx * length * s / 2 + rx * width * q / 2,
                            p.y + fz * length * s / 2 + rz * width * q / 2,
                        )
                        for s, q in ((-1, -1), (-1, 1), (1, 1), (1, -1))
                    ]
                )
                outside = max(outside, shell.difference(pavement).area)
                if d >= 1 and d <= line.length - 1:
                    a = line.interpolate(d - 1)
                    b = line.interpolate(d + 1)
                    va = (p.x - a.x, p.y - a.y)
                    vb = (b.x - p.x, b.y - p.y)
                    angle = abs(
                        math.atan2(va[0] * vb[1] - va[1] * vb[0], va[0] * vb[0] + va[1] * vb[1])
                    )
                    if angle > 1e-4:
                        minimum_radius = min(minimum_radius, 1 / angle)
            required = wheelbase / math.tan(math.radians(steering))
            turns.append(
                {
                    "vehicle": name,
                    "minimum_sampled_radius_m": round(minimum_radius, 2),
                    "minimum_vehicle_radius_m": round(required, 2),
                    "max_shell_area_outside_pavement_m2": round(outside, 3),
                    "review_required": minimum_radius < required * 0.9 or outside > 0.3,
                }
            )
        envelopes.append({"lane": lane["id"], "source": lane["sourceId"], "vehicles": turns})
    tunnel = [item for item in lanes.values() if item["sourceId"] == "way/25313910"]
    if not tunnel or min(p["y"] for item in tunnel for p in item["points"]) > -5:
        errors.append("Richards underpass missing vertical separation")
    report = {
        "raised_sidewalk_road_overlap_m2": round(raised_overlap, 6),
        "map_sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
        "source_sha256": data["sourceSha256"],
        "connection_count": len(links),
        "errors": errors,
        "connections": links,
        "turn_envelopes": envelopes,
        "review_counts": {
            kind: sum(
                v["review_required"]
                for e in envelopes
                for v in e["vehicles"]
                if v["vehicle"] == kind
            )
            for kind in ("car", "bus")
        },
        "limitations": [
            "Conservative centred-rectangle envelope; "
            "moving-vehicle swept envelopes require separate Unity checks.",
            "Source access and SUMO connections are separate from width/timing estimates.",
            "This report cannot certify real City of Davis traffic rules.",
        ],
        "qualified": False,
    }
    out = ROOT / "outputs/qualification/overhaul/map-audit.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    if args.write_routing:
        if errors:
            raise ValueError(
                "Map continuity/access errors must be fixed before generating routing limits"
            )
        limits = {
            "schemaVersion": 1,
            "mapSha256": report["map_sha256"],
            "basis": "Conservative simulation vehicle-envelope exclusions; "
            "these are not source traffic prohibitions",
            "carExcluded": [e["lane"] for e in envelopes if e["vehicles"][0]["review_required"]],
            "busExcluded": [e["lane"] for e in envelopes if e["vehicles"][1]["review_required"]],
        }
        physical_path = ROOT / "game/physical-turn-exclusions.json"
        if physical_path.is_file():
            physical = json.loads(physical_path.read_text(encoding="utf-8"))
            signatures = {
                "mapSha256": report["map_sha256"],
                "dynamicsSha256": hashlib.sha256(
                    (ROOT / "game/Assets/Scripts/VehicleDynamics.cs").read_bytes()
                ).hexdigest(),
                "driverSha256": hashlib.sha256(
                    (ROOT / "game/Assets/Scripts/PhysicalDriver.cs").read_bytes()
                ).hexdigest(),
            }
            if all(physical.get(k) == value for k, value in signatures.items()):
                for kind in ("carExcluded", "busExcluded"):
                    if any(lane not in lanes for lane in physical[kind]):
                        raise ValueError("Physical review references an unknown lane")
                    limits[kind] = sorted(set(limits[kind]) | set(physical[kind]))
                limits["basis"] += "; includes hash-matched physical swept-turn review"
                limits["physicalReviewSha256"] = hashlib.sha256(
                    physical_path.read_bytes()
                ).hexdigest()
            else:
                limits["physicalReviewStatus"] = "Stale: rerun physical qualification"
                # A changed map must not silently re-enable previously rejected
                # turns. Retain known restrictions for surviving lane IDs, without
                # claiming that the changed geometry has passed physical review.
                for kind in ("carExcluded", "busExcluded"):
                    limits[kind] = sorted(
                        set(limits[kind]) | {lane for lane in physical[kind] if lane in lanes}
                    )
                limits["basis"] += (
                    "; retains earlier restrictive physical exclusions pending review"
                )
                limits["priorPhysicalReviewSha256"] = hashlib.sha256(
                    physical_path.read_bytes()
                ).hexdigest()
        path.with_name("davis-turn-limits.json").write_text(
            json.dumps(limits, indent=2) + "\n", encoding="utf-8"
        )
    print(
        json.dumps(
            {k: report[k] for k in ("connection_count", "errors", "review_counts", "qualified")},
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
