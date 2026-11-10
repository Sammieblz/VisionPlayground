"""Offline OSM control assignment. No simulator or observer dependency."""

import math


def stop_parent_roads(elements):
    stops = {
        e["id"]
        for e in elements
        if e["type"] == "node" and e.get("tags", {}).get("highway") == "stop"
    }
    parents = {"node/" + str(node): set() for node in stops}
    for way in elements:
        if way["type"] != "way" or "highway" not in way.get("tags", {}):
            continue
        for node in stops.intersection(way.get("nodes", [])):
            parents["node/" + str(node)].add("way/" + str(way["id"]))
    return parents


def line_measure(point, points):
    """Nearest distance, remaining length and local direction on an ordered line."""
    total = sum(math.dist(a, b) for a, b in zip(points, points[1:], strict=False))
    along = 0.0
    best = (float("inf"), 0.0, (0.0, 0.0))
    for a, b in zip(points, points[1:], strict=False):
        delta = (b[0] - a[0], b[1] - a[1])
        length = math.hypot(*delta)
        if length == 0:
            continue
        t = max(
            0.0,
            min(1.0, sum((p - x) * d for p, x, d in zip(point, a, delta, strict=True)) / length**2),
        )
        distance = math.dist(point, (a[0] + t * delta[0], a[1] + t * delta[1]))
        if distance < best[0]:
            best = (distance, total - along - t * length, (delta[0] / length, delta[1] / length))
        along += length
    return best


def assign_stop_lanes(feature, parents, facts, lanes):
    """Bind to the source road and direction; never invent control of a nearby road."""
    direction = feature["osm_tags"].get("direction", "")
    if direction not in {"", "forward", "backward", "both", "all"}:
        return []
    point = feature["paths_xz_m"][0][0]
    candidates = []
    for lane in lanes:
        road = lane.get("sourceId")
        if lane["internalLane"] or road not in parents.get(feature["source_id"], set()):
            continue
        source = facts.get(road)
        if not source:
            continue
        paths = [p for p in source["paths_xz_m"] if len(p) >= 2]
        if not paths:
            continue
        source_direction = min((line_measure(point, p) for p in paths), key=lambda m: m[0])[2]
        distance, remaining, heading = line_measure(
            point, [(p["x"], p["z"]) for p in lane["points"]]
        )
        dot = sum(a * b for a, b in zip(source_direction, heading, strict=True))
        if (
            distance > 8
            or direction == "forward"
            and dot <= 0
            or direction == "backward"
            and dot >= 0
        ):
            continue
        candidates.append((distance + 0.15 * remaining, lane["id"]))
    if not candidates:
        return []
    best_edge = min(candidates)[1].rsplit("_", 1)[0]
    return sorted(lane for _, lane in candidates if lane.rsplit("_", 1)[0] == best_edge)
