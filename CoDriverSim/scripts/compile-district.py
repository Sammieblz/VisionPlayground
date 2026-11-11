"""Build the shared, source-attributed Unity map using pinned offline SUMO.

Run in the map tooling environment (Shapely 2.2.0, pyproj 3.7.2).
OSM facts remain in the survey; estimates and netconvert decisions are emitted
separately. The generated map is navigation/evaluation data, never perception input.
"""

import hashlib
import heapq
import json
import math
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

from PIL import Image, ImageDraw, PngImagePlugin
from shapely import constrained_delaunay_triangles
from shapely.geometry import LineString, Point, Polygon, box
from shapely.geometry.polygon import orient
from shapely.ops import polygonize, split, unary_union

from codriversim.map_controls import assign_stop_lanes, stop_parent_roads
from codriversim.maps import prepare

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "outputs/maps/davis-downtown/playable"


def xyz(x, z, y=0):
    return {"x": round(x, 4), "y": round(y, 4), "z": round(z, 4)}


def parts(shape):
    return list(shape.geoms) if hasattr(shape, "geoms") else [shape]


def mesh(shape, elevation=0, subdivide=True):
    if subdivide and not shape.is_empty:
        x0, z0, x1, z1 = shape.bounds
        if x1 - x0 > 250 or z1 - z0 > 250:
            vertices, indices = [], []
            for x in range(math.floor(x0 / 100) * 100, math.ceil(x1 / 100) * 100, 100):
                for z in range(math.floor(z0 / 100) * 100, math.ceil(z1 / 100) * 100, 100):
                    piece = mesh(shape.intersection(box(x, z, x + 100, z + 100)), elevation, False)
                    indices.extend(i + len(vertices) for i in piece["triangles"])
                    vertices.extend(piece["vertices"])
            return {"vertices": vertices, "triangles": indices}
    if callable(elevation):
        shape = shape.segmentize(4)
    vertices, indices = [], []
    for polygon in parts(shape):
        if not isinstance(polygon, Polygon) or polygon.area < 0.02:
            continue
        # Constrained edges retain concave corners and holes. Filtering an
        # unconstrained triangulation can leave unfilled drivable surfaces.
        for tri in constrained_delaunay_triangles(polygon).geoms:
            points = list(tri.exterior.coords)[:3]
            # x/z plane: clockwise from above gives Unity an upward normal.
            if tri.exterior.is_ccw:
                points.reverse()
            indices.extend(range(len(vertices), len(vertices) + 3))
            vertices.extend(
                xyz(x, z, elevation(x, z) if callable(elevation) else elevation) for x, z in points
            )
    return {"vertices": vertices, "triangles": indices}


def osm_xml(payload, path):
    root = ET.Element("osm", version="0.6", generator="CoDriverSim")
    for kind in ("node", "way", "relation"):
        for item in payload["elements"]:
            if item["type"] != kind:
                continue
            attrs = {"id": str(item["id"]), "version": str(item.get("version", 1))}
            if kind == "node":
                attrs.update(lat=str(item["lat"]), lon=str(item["lon"]))
            element = ET.SubElement(root, kind, attrs)
            for reference in item.get("nodes", []):
                ET.SubElement(element, "nd", ref=str(reference))
            for member in item.get("members", []):
                ET.SubElement(
                    element,
                    "member",
                    type=member["type"],
                    ref=str(member["ref"]),
                    role=member.get("role", ""),
                )
            for key, value in item.get("tags", {}).items():
                ET.SubElement(element, "tag", k=key, v=value)
    ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)


def building_roof(polygon, height, residential):
    """Estimated gable roof follows the source footprint; commercial roofs stay flat."""
    if not residential or polygon.area > 900:
        return mesh(polygon, height), "flat"
    corners = list(polygon.minimum_rotated_rectangle.exterior.coords)
    edges = [
        (math.dist(a, b), (b[0] - a[0], b[1] - a[1]))
        for a, b in zip(corners, corners[1:], strict=False)
    ]
    length, (dx, dz) = max(edges)
    width = min(edge[0] for edge in edges)
    if width < 3 or width > 25:
        return mesh(polygon, height), "flat"
    dx, dz = dx / length, dz / length
    centre = polygon.minimum_rotated_rectangle.centroid
    ridge = LineString(
        [
            (centre.x - dx * length, centre.y - dz * length),
            (centre.x + dx * length, centre.y + dz * length),
        ]
    )
    rise = min(2.4, width * 0.23)

    def elevation(x, z):
        return height + rise * max(
            0, 1 - abs((x - centre.x) * dz - (z - centre.y) * dx) / (width / 2)
        )

    vertices, triangles = [], []
    for piece in split(polygon, ridge).geoms:
        result = mesh(piece, elevation)
        triangles.extend(i + len(vertices) for i in result["triangles"])
        vertices.extend(result["vertices"])
    return {"vertices": vertices, "triangles": triangles}, "estimated gable"


def map_raster(data, destination):
    """Bake only static map geometry; never live actors or signal states."""
    size = 2048
    lo, hi = data["boundsMin"], data["boundsMax"]

    def pixel(p):
        return (
            (p["x"] - lo["x"]) / (hi["x"] - lo["x"]) * (size - 1),
            (hi["z"] - p["z"]) / (hi["z"] - lo["z"]) * (size - 1),
        )

    picture = Image.new("RGB", (size, size), (31, 39, 37))
    draw = ImageDraw.Draw(picture)

    def surface(mesh, colour):
        v, t = mesh["vertices"], mesh["triangles"]
        for i in range(0, len(t), 3):
            draw.polygon([pixel(v[k]) for k in t[i : i + 3]], fill=colour)

    surface(data["parkingSurface"], (85, 93, 93))
    surface(data["parkingAccessSurface"], (85, 93, 93))
    surface(data["sidewalkSurface"], (106, 114, 111))
    surface(data["roadSurface"], (169, 175, 175))
    surface(data["underpassSurface"], (137, 150, 151))
    for b in data["buildings"]:
        surface(b["roof"], (69, 76, 77))
    for rail in data["rail"]:
        draw.line([pixel(p) for p in rail["points"]], fill=(210, 195, 156), width=3)
    # The reserved corner texel lets the same Canvas mesh draw solid route/icon
    # colours. Point filtering and disabled mipmaps keep it out of the map image.
    picture.putpixel((0, size - 1), (255, 255, 255))
    meta = PngImagePlugin.PngInfo()
    meta.add_text("Attribution", "OpenStreetMap contributors, ODbL 1.0")
    meta.add_text("Source SHA256", data["sourceSha256"])
    meta.add_text("Content", "Static shared MapDefinition geometry; no actor or signal-state truth")
    picture.save(destination, pnginfo=meta)


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    raw = ROOT / "outputs/maps/davis-downtown/osm.json"
    control_roads = stop_parent_roads(json.loads(raw.read_text(encoding="utf-8"))["elements"])
    district = json.loads((ROOT / "game/maps/davis-downtown.json").read_text(encoding="utf-8"))
    layout = prepare(
        json.loads(raw.read_text(encoding="utf-8")),
        district,
        hashlib.sha256(raw.read_bytes()).hexdigest(),
    )
    lower, upper = layout["bounds_xz_m"]
    extent = box(*lower, *upper)
    lat, lon = district["origin_lat_lon"]
    executable = next((ROOT / "outputs/tools/sumo-1.28.0").rglob("netconvert.exe"))
    version = subprocess.check_output([str(executable), "--version"], text=True)
    if "1.28.0" not in version:
        raise ValueError("Require SUMO 1.28.0")
    osm = OUTPUT / "district.osm.xml"
    osm_xml(json.loads(raw.read_text(encoding="utf-8")), osm)
    netfile = OUTPUT / "district.net.xml"
    command = [
        str(executable),
        "--osm-files",
        str(osm),
        "--output-file",
        str(netfile),
        "--proj",
        f"+proj=eqc +lat_ts={lat} +lat_0={lat} +lon_0={lon} +datum=WGS84 +units=m +no_defs",
        "--offset.disable-normalization",
        "true",
        "--keep-edges.by-vclass",
        "passenger,bus",
        "--geometry.remove",
        "true",
        "--junctions.join",
        "true",
        "--junctions.join-dist",
        "25",
        "--no-turnarounds",
        "true",
        "--default.junctions.radius",
        "9",
        "--junctions.small-radius",
        "6",
        "--junctions.corner-detail",
        "12",
        "--junctions.internal-link-detail",
        "24",
        "--osm.turn-lanes",
        "true",
        "--junctions.limit-turn-speed",
        "2.2",
        "--output.street-names",
        "true",
        "--osm.lane-access",
        "true",
    ]
    result = subprocess.run(command, capture_output=True, text=True, check=False)
    (OUTPUT / "netconvert.log").write_text(result.stdout + result.stderr, encoding="utf-8")
    if result.returncode:
        raise RuntimeError("netconvert failed; inspect its log")
    network = ET.parse(netfile).getroot()
    location = network.find("location")
    offset = [float(v) for v in location.attrib["netOffset"].split(",")]

    def coordinates(text):
        return [
            (float(p.split(",")[0]) - offset[0], float(p.split(",")[1]) - offset[1])
            for p in text.split()
        ]

    features = layout["features"]
    facts = {f["source_id"]: f for f in features}
    tunnel = LineString(facts["way/25313910"]["paths_xz_m"][0])

    def height(x, z):
        # Assumption: a 5.8 m depression with <=7.25% approaches; no surveyed Z.
        return -5.8 * max(0, 1 - Point(x, z).distance(tunnel) / 80)

    roads, lanes, lane_shapes, edges = [], {}, {}, {}
    for edge in network.findall("edge"):
        edge_id = edge.attrib["id"]
        internal = edge.attrib.get("function") == "internal"
        source = "way/" + re.sub(r"^(-?\d+).*$", r"\1", edge_id).lstrip("-")
        fact = facts.get(source, {})
        if not internal and fact.get("osm_tags", {}).get("motor_vehicle", fact.get("access")) in {
            "no",
            "private",
        }:
            continue
        name = edge.attrib.get("name", fact.get("name") or "Access road")
        grade = source == "way/25313910" or name == "Richards Boulevard"
        ids = []
        for lane in edge.findall("lane"):
            allowed = lane.attrib.get("allow", "")
            forbidden = lane.attrib.get("disallow", "")
            if (
                allowed and "passenger" not in allowed.split() and "bus" not in allowed.split()
            ) or "passenger" in forbidden.split():
                continue
            source_line = LineString(coordinates(lane.attrib["shape"]))
            points = list(
                (source_line.segmentize(4) if source_line.length > 0.001 else source_line).coords
            )
            line = LineString(points)
            if not extent.intersects(line):
                continue
            lane_id = lane.attrib["id"]
            lane_shapes[lane_id] = line
            ids.append(lane_id)
            lanes[lane_id] = {
                "id": lane_id,
                "sourceId": "sumo/" + edge_id if internal else source,
                "name": name,
                "internalLane": internal,
                "estimatedConnection": internal,
                "width": float(lane.attrib.get("width", 3.2)),
                "speedMps": float(lane.attrib["speed"]),
                "points": [xyz(x, z, height(x, z) if grade else 0) for x, z in points],
                "next": [],
                "boundary": not extent.buffer(0.1).covers(line),
                "signal": "",
                "signalIndex": -1,
            }
            lanes[lane_id]["junction"] = edge.attrib.get("to", "")
            lanes[lane_id]["access"] = fact.get("access", "unknown")
        if ids and not internal:
            edges[edge_id] = ids
            roads.append(
                {
                    "id": edge_id,
                    "sourceId": source,
                    "name": name,
                    "laneIds": ids,
                    "tunnel": fact.get("osm_tags", {}).get("tunnel") == "yes",
                    "estimatedWidth": "width" not in fact.get("osm_tags", {}),
                    "access": fact.get("access", "unknown"),
                    "oneway": fact.get("oneway", "unknown"),
                    "bridge": fact.get("osm_tags", {}).get("bridge", "no") != "no",
                    "sourceLayer": fact.get("osm_tags", {}).get("layer", "0"),
                }
            )
    via_fallback = []
    for connection in network.findall("connection"):
        a = connection.attrib
        source = a["from"] + "_" + a["fromLane"]
        target = a["to"] + "_" + a["toLane"]
        via = a.get("via")
        if source not in lanes or target not in lanes:
            continue
        if via in lanes:
            lanes[source]["next"].append(via)
            via_fallback.append((via, target))
        elif not via:
            lanes[source]["next"].append(target)
        if "tl" in a:
            lanes[source]["signal"] = a["tl"]
            lanes[source]["signalIndex"] = int(a["linkIndex"])
    for via, target in via_fallback:
        if not lanes[via]["next"]:
            lanes[via]["next"].append(target)
    # Grade propagates along connected roads, including service-road junctions.
    # Shared endpoints are one height; disconnected rail/overpasses remain at grade.
    graph, levels = {}, {}

    def key(point):
        return (round(point["x"], 2), round(point["z"], 2))

    queue = []
    for lane in lanes.values():
        for p in lane["points"]:
            k = key(p)
            graph.setdefault(k, [])
            levels.setdefault(k, 0.0)
            if p["y"] < levels[k]:
                levels[k] = p["y"]
                heapq.heappush(queue, (p["y"], k))
        for a, b in zip(lane["points"], lane["points"][1:], strict=False):
            u, v = key(a), key(b)
            cost = math.dist(u, v) * 0.0725
            graph[u].append((v, cost))
            graph[v].append((u, cost))
        for successor in lane["next"]:
            u, v = key(lane["points"][-1]), key(lanes[successor]["points"][0])
            cost = math.dist(u, v) * 0.0725
            graph.setdefault(u, []).append((v, cost))
            graph.setdefault(v, []).append((u, cost))
    while queue:
        level, u = heapq.heappop(queue)
        if level > levels[u] + 1e-8:
            continue
        for v, cost in graph[u]:
            candidate = min(0.0, level + cost)
            if candidate < levels.get(v, 0.0) - 1e-8:
                levels[v] = candidate
                heapq.heappush(queue, (candidate, v))
    graded = []
    for lane in lanes.values():
        for p in lane["points"]:
            p["y"] = round(levels[key(p)], 4)
        if any(p["y"] < -0.001 for p in lane["points"]):
            graded.append(lane)

    def road_height(x, z):
        point = Point(x, z)
        lane = min(graded, key=lambda item: lane_shapes[item["id"]].distance(point))
        line = lane_shapes[lane["id"]]
        along = line.project(point)
        total = 0.0
        for a, b in zip(lane["points"], lane["points"][1:], strict=False):
            length = math.dist((a["x"], a["z"]), (b["x"], b["z"]))
            if total + length >= along:
                t = max(0.0, min(1.0, (along - total) / max(length, 1e-6)))
                return a["y"] + (b["y"] - a["y"]) * t
            total += length
        return lane["points"][-1]["y"]

    signals = []
    junctions = []
    for junction in network.findall("junction"):
        if junction.attrib["type"] in {"internal", "dead_end"}:
            continue
        x, z = float(junction.attrib["x"]) - offset[0], float(junction.attrib["y"]) - offset[1]
        if extent.covers(Point(x, z)):
            junctions.append(
                {
                    "id": junction.attrib["id"],
                    "position": xyz(x, z),
                    "type": junction.attrib["type"],
                }
            )
    for logic in network.findall("tlLogic"):
        junction = next(
            (j for j in network.findall("junction") if j.attrib["id"] == logic.attrib["id"]), None
        )
        if junction is None:
            continue
        x, z = float(junction.attrib["x"]) - offset[0], float(junction.attrib["y"]) - offset[1]
        if extent.covers(Point(x, z)):
            signals.append(
                {
                    "id": logic.attrib["id"],
                    "position": xyz(x, z),
                    "phases": [
                        {"duration": float(p.attrib["duration"]), "state": p.attrib["state"]}
                        for p in logic.findall("phase")
                    ],
                    "estimatedTiming": True,
                }
            )
    normal, depressed = [], []
    for identity, lane in lanes.items():
        shape = (
            lane_shapes[identity]
            .buffer(lane["width"] / 2 + 0.2, cap_style=2, join_style=2)
            .intersection(extent)
        )
        (depressed if any(p["y"] < -0.01 for p in lane["points"]) else normal).append(shape)
    for junction in network.findall("junction"):
        if junction.attrib.get("type") == "internal" or not junction.attrib.get("shape"):
            continue
        points = coordinates(junction.attrib["shape"])
        if len(points) >= 3:
            polygon = Polygon(points).buffer(0).intersection(extent)
            if polygon.is_empty:
                continue
            is_depressed = any(
                lanes.get(i, {}).get("points", [{"y": 0}])[-1]["y"] < -0.01
                for i in junction.attrib.get("incLanes", "").split()
            )
            (depressed if is_depressed else normal).append(polygon)
    normal, depressed = unary_union(normal), unary_union(depressed)
    road_geometry = unary_union([normal, depressed])
    sidewalks = road_geometry.buffer(2.8, join_style=2).difference(road_geometry.buffer(0.1))
    buildings, building_shapes = [], []
    missing = []
    for f in features:
        if f["kind"] != "building":
            continue
        outer = [
            LineString(p)
            for p, role in zip(f["paths_xz_m"], f["path_roles"], strict=True)
            if role != "inner" and len(p) > 2
        ]
        inner = [
            LineString(p)
            for p, role in zip(f["paths_xz_m"], f["path_roles"], strict=True)
            if role == "inner" and len(p) > 2
        ]
        polygon = unary_union(list(polygonize(unary_union(outer)))) if outer else Polygon()
        if inner:
            polygon = polygon.difference(unary_union(list(polygonize(unary_union(inner)))))
        polygon = polygon.intersection(extent)
        if polygon.is_empty:
            missing.append(f["source_id"])
            continue
        # Relation-member duplicate outlines are removed by geometry equivalence.
        if any(polygon.equals(p) for p in building_shapes):
            continue
        building_shapes.append(polygon)
        value = f.get("height_m") or (f.get("levels") or 2) * 3.2
        usage = f.get("osm_tags", {}).get("building", "yes")
        for poly in parts(polygon):
            if not isinstance(poly, Polygon) or poly.area < 4:
                continue
            poly = orient(poly, sign=1.0)
            roof, roof_style = building_roof(
                poly,
                value,
                usage in {"house", "houses", "residential", "detached", "static_caravan"},
            )
            buildings.append(
                {
                    "sourceId": f["source_id"],
                    "name": f["name"] or "",
                    "height": value,
                    "usage": usage,
                    "roofStyle": roof_style,
                    "estimatedHeight": not bool(f.get("height_m")),
                    "roof": roof,
                    "rings": [
                        {"points": [xyz(x, z) for x, z in ring.coords]}
                        for ring in [poly.exterior, *poly.interiors]
                    ],
                }
            )
    sidewalks = sidewalks.difference(unary_union(building_shapes))
    paths, rail, landmarks, parking = [], [], [], []
    for f in features:
        if f["kind"] not in {
            "path",
            "rail",
            "crossing",
            "stop_sign",
            "transit_stop",
            "rail_point",
            "park",
            "parking",
        }:
            continue
        for points in f["paths_xz_m"]:
            if not points or not any(extent.covers(Point(p)) for p in points):
                continue
            if f["kind"] == "rail" and len(points) > 1:
                rail.append(
                    {
                        "id": f["source_id"],
                        "name": f["name"] or "Rail",
                        "points": [xyz(x, z, 0.12) for x, z in points],
                    }
                )
            elif (
                f["kind"] == "path"
                and len(points) > 1
                and f["osm_tags"].get("highway") in {"footway", "pedestrian", "path"}
            ):
                paths.append(LineString(points).buffer(1.2).intersection(extent))
            elif f["kind"] != "path":
                if f["kind"] == "parking" and len(points) >= 4 and points[0] == points[-1]:
                    parking.append(Polygon(points).buffer(0).intersection(extent))
                centre = Point(points[0]) if len(points) == 1 else LineString(points).centroid
                lane_id = ""
                controlled_lanes = []
                if f["kind"] == "stop_sign":
                    controlled_lanes = assign_stop_lanes(f, control_roads, facts, lanes.values())
                    lane_id = controlled_lanes[0] if controlled_lanes else ""
                landmarks.append(
                    {
                        "sourceId": f["source_id"],
                        "kind": f["kind"],
                        "subtype": f["osm_tags"].get("railway", f["osm_tags"].get("highway", "")),
                        "laneId": lane_id,
                        "controlledLaneIds": controlled_lanes,
                        "controlRoadSources": sorted(control_roads.get(f["source_id"], [])),
                        "sourceDirection": f["osm_tags"].get("direction", ""),
                        "name": f["name"] or f["kind"].replace("_", " "),
                        "position": xyz(centre.x, centre.y),
                    }
                )
    # Parking activity uses physical approach curves. Give those estimated
    # driveway mouths a continuous paved surface and a curb cut, rather than
    # asking the car to drive across a raised, uninterrupted sidewalk strip.
    # These additions are explicitly separate from OSM roads and source facts.
    access_shapes, access_bays = [], []
    obstacles = unary_union(building_shapes).buffer(0.25)
    eligible = [
        item
        for item in lanes.values()
        if not item["internalLane"] and not item["boundary"] and item["next"]
    ]
    for place in landmarks:
        if place["kind"] != "parking":
            continue
        centre = Point(place["position"]["x"], place["position"]["z"])
        lane = min(eligible, key=lambda item: lane_shapes[item["id"]].distance(centre))
        line = lane_shapes[lane["id"]]
        if line.distance(centre) > 35:
            continue
        along = line.project(centre)
        # A bay approach must fit on its own street, not wrap through a junction.
        if along < 35 or line.length - along < 20:
            continue
        approaches = []
        for offset in (-30, -15, 15):
            at = max(0, min(line.length, along + offset))
            join = line.interpolate(at)
            before, after = (
                line.interpolate(max(0, at - 0.2)),
                line.interpolate(min(line.length, at + 0.2)),
            )
            dx, dz = after.x - before.x, after.y - before.y
            norm = math.hypot(dx, dz)
            if norm < 1e-5:
                continue
            forward = (dx / norm, dz / norm)
            # Forward arrival from behind the bay, or forward departure ahead.
            # Reverse parking uses the same curves in the opposite direction.
            # Do not pave impossible forward/reverse combinations into loops.
            start, end = (join, centre) if offset < 0 else (centre, join)
            handle = max(5, start.distance(end) * 0.55)
            a = (start.x + forward[0] * handle, start.y + forward[1] * handle)
            b = (end.x - forward[0] * handle, end.y - forward[1] * handle)
            curve = []
            for step in range(65):
                t = step / 64
                s = 1 - t
                curve.append(
                    (
                        s**3 * start.x + 3 * s * s * t * a[0] + 3 * s * t * t * b[0] + t**3 * end.x,
                        s**3 * start.y + 3 * s * s * t * a[1] + 3 * s * t * t * b[1] + t**3 * end.y,
                    )
                )
            apron = LineString(curve).buffer(2.75).intersection(extent)
            approaches.append(apron)
        if len(approaches) == 3 and not any(a.intersects(obstacles) for a in approaches):
            access_shapes.extend(approaches)
            access_bays.append(place["sourceId"])
    parking_access = unary_union(access_shapes).difference(depressed.buffer(1))
    sidewalks = (
        unary_union([sidewalks, *paths])
        # Mapped footways can cross a road. Their walking connectivity is
        # represented by crossing areas, not a raised concrete strip that
        # catches vehicle bodies across the drivable surface.
        .difference(road_geometry.buffer(0.1))
        .difference(depressed.buffer(0.3))
        .difference(parking_access.buffer(0.1))
    )
    ground = extent.difference(depressed.buffer(2))
    data = {
        "schemaVersion": 1,
        "id": district["id"],
        "name": district["name"],
        "latitude": lat,
        "longitude": lon,
        "sourceSha256": layout["source_sha256"],
        "attribution": district["attribution"],
        "boundsMin": xyz(*lower),
        "boundsMax": xyz(*upper),
        "roads": roads,
        "lanes": list(lanes.values()),
        "signals": signals,
        "junctions": junctions,
        "buildings": buildings,
        "rail": rail,
        "landmarks": landmarks,
        "ground": mesh(ground, -0.14),
        "parkingSurface": mesh(unary_union(parking), 0.01),
        "parkingAccessSurface": mesh(parking_access, 0.01),
        "parkingAccessBayIds": access_bays,
        "roadSurface": mesh(normal.difference(depressed), 0),
        "underpassSurface": mesh(depressed, road_height),
        "sidewalkSurface": mesh(sidewalks, 0.10),
        "assumptions": [
            "SUMO 1.28.0 inferred lane connections; source OSM retained separately",
            "Estimated junction corner radii 9 m, minor junctions 6 m; "
            "source turn-lane restrictions retained; junction merge distance 25 m",
            "Lane widths default to 3.2 m where unknown",
            "Parking access aprons and curb cuts are estimated simulation additions; "
            "building footprints are preserved",
            "Signal schedules and added eight-second pedestrian clearance phases "
            "are simulated, not City of Davis timings",
            "Building height defaults to two storeys at 3.2 m",
            "Richards underpass modeled 5.8 m deep with 80 m transition; "
            "vertical survey unavailable",
        ],
        "spawnLane": min(
            (
                item
                for item in lanes.values()
                if item["name"] == "3rd Street"
                and not item["internalLane"]
                and not item["boundary"]
                and len(item["next"]) > 0
            ),
            key=lambda item: item["points"][0]["x"] ** 2 + item["points"][0]["z"] ** 2,
        )["id"],
    }
    destination = ROOT / "game/Assets/Resources/Maps/davis-downtown.json"
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(data, separators=(",", ":")) + "\n", encoding="utf-8")
    map_raster(data, destination.with_name("davis-downtown-minimap.png"))
    report = {
        "sumo": version.splitlines()[0],
        "command": command,
        "source_sha256": layout["source_sha256"],
        "map_sha256": hashlib.sha256(destination.read_bytes()).hexdigest(),
        "lanes": len(lanes),
        "roads": len(roads),
        "signals": len(signals),
        "buildings": len(buildings),
        "rails": len(rail),
        "stop_controls": {
            "mapped": sum(
                p["kind"] == "stop_sign" and bool(p["controlledLaneIds"]) for p in landmarks
            ),
            "unassigned": [
                p["sourceId"]
                for p in landmarks
                if p["kind"] == "stop_sign" and not p["controlledLaneIds"]
            ],
            "basis": "OSM parent road and direction; "
            "signs on omitted roads do not control nearby roads",
        },
        "unassembled_or_outside_buildings": missing,
        "status": "generated; runtime geometry, access, envelope and continuity checks required",
    }
    (OUTPUT / "generation-report.json").write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8"
    )
    # Routing must never silently keep limits for an older geometry hash.
    subprocess.run(
        [sys.executable, str(ROOT / "scripts/validate-district.py"), "--write-routing"],
        check=True,
    )
    print(
        json.dumps(
            {
                k: v
                for k, v in report.items()
                if k not in {"command", "unassembled_or_outside_buildings"}
            },
            indent=2,
        )
    )


if __name__ == "__main__":
    main()
