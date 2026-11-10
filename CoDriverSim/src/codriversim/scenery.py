"""Seeded decorative buildings. Scenery never enters the observer or controls traffic."""

import math
import random
from dataclasses import dataclass


@dataclass(frozen=True)
class Building:
    x: float
    y: float
    width: float
    depth: float
    height: float
    color: tuple[float, float, float]


def plan_buildings(road_polygons: list, seed: int) -> list[Building]:
    """Keep complete building footprints at least eight metres from road polygons.

    Shapely is supplied by the optional simulator dependency. This function has no
    simulator objects, rendering, vehicle policies, or control authority.
    """
    from shapely.geometry import Polygon, box
    from shapely.ops import unary_union

    if not road_polygons:
        return []
    roads = unary_union([Polygon(points) for points in road_polygons])
    exclusion = roads.buffer(8)
    neighborhood = roads.buffer(65)
    left, bottom, right, top = neighborhood.bounds
    palette = ((0.68, 0.53, 0.39), (0.72, 0.73, 0.69), (0.47, 0.57, 0.64), (0.68, 0.43, 0.34))
    rng = random.Random(seed)
    buildings = []
    for x in range(math.floor(left / 28) * 28, math.ceil(right), 28):
        for y in range(math.floor(bottom / 28) * 28, math.ceil(top), 28):
            width, depth = rng.uniform(13, 21), rng.uniform(13, 21)
            footprint = box(x - width / 2, y - depth / 2, x + width / 2, y + depth / 2)
            if footprint.intersects(exclusion) or not neighborhood.contains(footprint):
                continue
            buildings.append(
                Building(x, y, width, depth, rng.choice((9, 13, 17, 21)), rng.choice(palette))
            )
    return buildings


def render_buildings(parent, buildings: list[Building]):
    """Batch the entire neighborhood into one colored mesh; no downloaded models."""
    from panda3d.core import (
        Geom,
        GeomNode,
        GeomTriangles,
        GeomVertexData,
        GeomVertexFormat,
        GeomVertexWriter,
    )

    data = GeomVertexData("city", GeomVertexFormat.getV3n3c4(), Geom.UHStatic)
    positions = GeomVertexWriter(data, "vertex")
    normals = GeomVertexWriter(data, "normal")
    colors = GeomVertexWriter(data, "color")
    triangles = GeomTriangles(Geom.UHStatic)
    vertex_count = 0

    def quad(points, normal, color):
        nonlocal vertex_count
        for point in points:
            positions.addData3(*point)
            normals.addData3(*normal)
            colors.addData4(*color, 1)
        triangles.addVertices(vertex_count, vertex_count + 1, vertex_count + 2)
        triangles.addVertices(vertex_count, vertex_count + 2, vertex_count + 3)
        vertex_count += 4

    for building in buildings:
        x0, x1 = building.x - building.width / 2, building.x + building.width / 2
        y0, y1 = building.y - building.depth / 2, building.y + building.depth / 2
        z = building.height
        # Counterclockwise faces, seen from the outside.
        faces = (
            (((x0, y0, 0), (x1, y0, 0), (x1, y0, z), (x0, y0, z)), (0, -1, 0)),
            (((x1, y1, 0), (x0, y1, 0), (x0, y1, z), (x1, y1, z)), (0, 1, 0)),
            (((x0, y1, 0), (x0, y0, 0), (x0, y0, z), (x0, y1, z)), (-1, 0, 0)),
            (((x1, y0, 0), (x1, y1, 0), (x1, y1, z), (x1, y0, z)), (1, 0, 0)),
            (((x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z)), (0, 0, 1)),
        )
        for points, normal in faces:
            shade = 0.8 if normal[0] else 1.0
            color = tuple(channel * shade for channel in building.color)
            quad(points, normal, color if normal[2] == 0 else (0.25, 0.27, 0.29))
        # Windows are geometry in the same batch, offset to avoid depth fighting.
        for floor in range(2, int(z) - 1, 4):
            for x in range(math.ceil(x0 + 2), math.floor(x1 - 2), 4):
                quad(
                    (
                        (x, y0 - 0.03, floor),
                        (x + 1.7, y0 - 0.03, floor),
                        (x + 1.7, y0 - 0.03, floor + 1.8),
                        (x, y0 - 0.03, floor + 1.8),
                    ),
                    (0, -1, 0),
                    (0.22, 0.35, 0.42),
                )
                quad(
                    (
                        (x + 1.7, y1 + 0.03, floor),
                        (x, y1 + 0.03, floor),
                        (x, y1 + 0.03, floor + 1.8),
                        (x + 1.7, y1 + 0.03, floor + 1.8),
                    ),
                    (0, 1, 0),
                    (0.22, 0.35, 0.42),
                )
            for y in range(math.ceil(y0 + 2), math.floor(y1 - 2), 4):
                quad(
                    (
                        (x0 - 0.03, y + 1.7, floor),
                        (x0 - 0.03, y, floor),
                        (x0 - 0.03, y, floor + 1.8),
                        (x0 - 0.03, y + 1.7, floor + 1.8),
                    ),
                    (-1, 0, 0),
                    (0.22, 0.35, 0.42),
                )
                quad(
                    (
                        (x1 + 0.03, y, floor),
                        (x1 + 0.03, y + 1.7, floor),
                        (x1 + 0.03, y + 1.7, floor + 1.8),
                        (x1 + 0.03, y, floor + 1.8),
                    ),
                    (1, 0, 0),
                    (0.22, 0.35, 0.42),
                )
    geom = Geom(data)
    geom.addPrimitive(triangles)
    node = GeomNode("CoDriverSim city buildings (decorative)")
    node.addGeom(geom)
    result = parent.attachNewNode(node)
    # This colored mesh has no PBR material/textures. Use vertex colors directly
    # instead of inheriting MetaDrive's model shader, which renders it black.
    result.setShaderOff(1)
    result.setLightOff(1)
    return result
