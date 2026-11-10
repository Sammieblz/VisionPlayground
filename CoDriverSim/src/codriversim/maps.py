"""Offline map preparation; navigation/source geometry, never camera perception.

Local coordinates use metres east/north about a declared origin. Missing map
attributes remain unknown. This module neither creates actors nor controls vehicles.
"""

import math
import re
from collections import Counter, defaultdict

RADIUS_M = 6378137.0
ROAD_TYPES = {
    "primary",
    "secondary",
    "tertiary",
    "residential",
    "unclassified",
    "living_street",
    "service",
    "primary_link",
    "secondary_link",
    "tertiary_link",
}


def project(latitude, longitude, origin):
    if not all(math.isfinite(v) for v in (latitude, longitude, *origin)):
        raise ValueError("Coordinates must be finite")
    if not (-90 <= latitude <= 90 and -180 <= longitude <= 180):
        raise ValueError("Coordinate outside WGS84 range")
    lat0, lon0 = origin
    if not (-85 < lat0 < 85 and -180 <= lon0 <= 180):
        raise ValueError("Unsupported local origin")
    return [
        RADIUS_M * math.cos(math.radians(lat0)) * math.radians(longitude - lon0),
        RADIUS_M * math.radians(latitude - lat0),
    ]


def number(value):
    try:
        result = float(value)
        return result if math.isfinite(result) and result > 0 else None
    except (ValueError, TypeError):
        return None


def speed_mph(value):
    if not isinstance(value, str):
        return None
    match = re.fullmatch(r"\s*(\d+(?:\.\d+)?)\s*(mph|km/h|kmh)?\s*", value)
    if not match:
        return None
    speed = number(match[1])
    return speed * (1 if match[2] == "mph" else 1 / 1.609344) if speed else None


def building_height(tags):
    value = tags.get("height", "")
    if not isinstance(value, str):
        return None
    match = re.fullmatch(r"\s*(\d+(?:\.\d+)?)\s*(m|ft)?\s*", value)
    if not match:
        return None
    height = number(match[1])
    return height * (0.3048 if match[2] == "ft" else 1) if height else None


def geometry_elements(payload):
    """Resolve API node references without joining across absent geometry."""
    elements = [dict(e) for e in payload["elements"]]
    nodes = {e["id"]: e for e in elements if e["type"] == "node"}
    ways = {e["id"]: e for e in elements if e["type"] == "way"}
    for way in ways.values():
        if "geometry" not in way:
            references = way.get("nodes", [])
            if references and all(n in nodes for n in references):
                way["geometry"] = [
                    {"lat": nodes[n]["lat"], "lon": nodes[n]["lon"]} for n in references
                ]
    for element in elements:
        if element["type"] != "relation":
            continue
        members = [dict(m) for m in element.get("members", [])]
        for member in members:
            if member["type"] == "way" and "geometry" not in member:
                member["geometry"] = ways.get(member["ref"], {}).get("geometry")
        element["members"] = members
    return elements


def category(tags):
    if "building" in tags and tags["building"] != "no":
        return "building"
    if tags.get("railway") in {"rail", "light_rail", "tram", "subway"}:
        return "rail"
    if tags.get("highway") == "traffic_signals":
        return "signal"
    if tags.get("highway") == "stop":
        return "stop_sign"
    if tags.get("highway") == "crossing":
        return "crossing"
    if tags.get("highway") == "bus_stop" or tags.get("public_transport") in {
        "platform",
        "stop_position",
    }:
        return "transit_stop"
    if tags.get("railway") in {"station", "halt", "level_crossing", "crossing"}:
        return "rail_point"
    if "highway" in tags:
        return "road" if tags["highway"] in ROAD_TYPES and tags.get("area") != "yes" else "path"
    if tags.get("amenity") == "parking":
        return "parking"
    if tags.get("leisure") == "park":
        return "park"
    return "landuse" if "landuse" in tags else None


def prepare(payload, district, source_sha256):
    origin = district["origin_lat_lon"]
    south, west, north, east = district["bbox_south_west_north_east"]
    if not south < north or not west < east:
        raise ValueError("Invalid map extent")
    lower, upper = project(south, west, origin), project(north, east, origin)
    features, missing_geometry = [], []
    neighbours = defaultdict(set)
    for item in geometry_elements(payload):
        tags = item.get("tags", {})
        kind = category(tags)
        if kind is None:
            continue
        paths, roles = [], []
        if item["type"] == "node" and "lat" in item:
            paths = [[project(item["lat"], item["lon"], origin)]]
            roles = ["point"]
        elif item["type"] == "way" and item.get("geometry"):
            if all(item["geometry"]):
                paths = [[project(p["lat"], p["lon"], origin) for p in item["geometry"]]]
                roles = ["outline" if paths[0][0] == paths[0][-1] else "line"]
        elif item["type"] == "relation":
            if any(
                m["type"] == "way" and (not m.get("geometry") or not all(m["geometry"]))
                for m in item.get("members", [])
            ):
                missing_geometry.append(f"{item['type']}/{item['id']}")
                continue
            for member in item.get("members", []):
                if member.get("geometry"):
                    paths.append([project(p["lat"], p["lon"], origin) for p in member["geometry"]])
                    roles.append(member.get("role", ""))
        if not paths or any(not p for p in paths):
            missing_geometry.append(f"{item['type']}/{item['id']}")
            continue
        feature = {
            "source_id": f"{item['type']}/{item['id']}",
            "kind": kind,
            "name": tags.get("name"),
            "paths_xz_m": paths,
            "path_roles": roles,
            "osm_tags": tags,
        }
        if kind == "road":
            count = number(tags.get("lanes"))
            feature.update(
                lane_count=int(count) if count and count.is_integer() and count <= 16 else None,
                maxspeed_mph=speed_mph(tags.get("maxspeed")),
                oneway=tags.get("oneway", "unknown"),
                access=tags.get("access", "unknown"),
            )
            for a, b in zip(item.get("nodes", []), item.get("nodes", [])[1:], strict=False):
                neighbours[a].add(b)
                neighbours[b].add(a)
        if kind == "building":
            feature.update(
                height_m=building_height(tags), levels=number(tags.get("building:levels"))
            )
        features.append(feature)
    roads = [f for f in features if f["kind"] == "road"]
    buildings = [f for f in features if f["kind"] == "building"]
    report = {
        "counts": dict(Counter(f["kind"] for f in features)),
        "width_m": upper[0] - lower[0],
        "depth_m": upper[1] - lower[1],
        "area_km2": (upper[0] - lower[0]) * (upper[1] - lower[1]) / 1e6,
        "road_junction_candidate_nodes": sum(len(n) >= 3 for n in neighbours.values()),
        "road_sections_with_lane_count": sum(f["lane_count"] is not None for f in roads),
        "road_sections_with_speed_limit": sum(f["maxspeed_mph"] is not None for f in roads),
        "buildings_with_height": sum(f["height_m"] is not None for f in buildings),
        "buildings_with_levels": sum(f["levels"] is not None for f in buildings),
        "missing_geometry": missing_geometry,
        "extent_note": (
            "Counts describe source records, not unique physical objects. Selected features "
            "may extend beyond the requested box; paths retain full source geometry."
        ),
    }
    return {
        "schema_version": 1,
        "map_id": district["id"],
        "name": district["name"],
        "status": "survey-only; not a validated lane graph or playable game map",
        "source_sha256": source_sha256,
        "attribution": district["attribution"],
        "license": district["license"],
        "license_url": district["license_url"],
        "projection": {
            "kind": "local_equirectangular",
            "origin_lat_lon": origin,
            "radius_m": RADIUS_M,
            "axes": "x=east, z=north",
            "elevation": "unknown; no vertical coordinates inferred",
        },
        "bounds_xz_m": [lower, upper],
        "report": report,
        "features": features,
    }
