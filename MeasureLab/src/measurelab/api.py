"""Projective measurement on one plane; no camera or marker detector is activated."""

import math
from collections.abc import Sequence

import numpy as np

from .domain import Measurement, PlanarReference, Point
from .validation import number, point, sequence


def _quad(value: Sequence[Point], name: str) -> tuple[Point, ...]:
    points = tuple(point(p, name) for p in sequence(value, name))
    if len(points) != 4:
        raise ValueError(f"{name} must contain four ordered corners: TL, TR, BR, BL.")
    turns = []
    for i in range(4):
        a, b, c = points[i], points[(i + 1) % 4], points[(i + 2) % 4]
        turns.append((b[0] - a[0]) * (c[1] - b[1]) - (b[1] - a[1]) * (c[0] - b[0]))
    if not all(math.isfinite(turn) for turn in turns) or not (
        all(turn > 1e-10 for turn in turns) or all(turn < -1e-10 for turn in turns)
    ):
        raise ValueError(f"{name} must be a convex nondegenerate ordered quadrilateral.")
    return points


def measure_quad(reference: PlanarReference, object_corners: Sequence[Point]) -> Measurement:
    """Map image coordinates to millimeters using a reference on the object's plane.

    Both quadrilaterals use corresponding TL/TR/BR/BL order. Width and height
    are averages of opposite rectified edge lengths. The caller is responsible
    for supplying a rectangular object on the reference plane.
    """
    corners = _quad(reference.image_corners, "reference.image_corners")
    object_points = _quad(object_corners, "object_corners")
    width = number(reference.width_mm, "reference.width_mm")
    height = number(reference.height_mm, "reference.height_mm")
    if width <= 0 or height <= 0:
        raise ValueError("Reference dimensions must be positive millimeters.")
    # Normalize source coordinates before solving to avoid large pixel offsets
    # dominating the eight-equation system.
    source = np.asarray(corners, dtype=float)
    center = source.mean(axis=0)
    scale = float(np.max(np.abs(source - center)))
    if not math.isfinite(scale) or scale <= 1e-12:
        raise ValueError("Reference corners are numerically degenerate.")
    normalized = (source - center) / scale
    target_scale = max(width, height)
    targets = (
        (0, 0),
        (width / target_scale, 0),
        (width / target_scale, height / target_scale),
        (0, height / target_scale),
    )
    matrix, values = [], []
    for (x, y), (u, v) in zip(normalized, targets, strict=True):
        matrix.extend(((x, y, 1, 0, 0, 0, -u * x, -u * y), (0, 0, 0, x, y, 1, -v * x, -v * y)))
        values.extend((u, v))
    try:
        if np.linalg.cond(matrix) > 1e12:
            raise ValueError("Reference corners cannot establish a stable planar transform.")
        solution = np.linalg.solve(matrix, values)
    except np.linalg.LinAlgError as error:
        raise ValueError("Reference corners cannot establish a planar transform.") from error
    transform = np.append(solution, 1.0).reshape(3, 3)
    rectified = []
    denominators = []
    for x, y in object_points:
        projected = transform @ np.array(((x - center[0]) / scale, (y - center[1]) / scale, 1))
        denominator = float(projected[2])
        denominators.append(denominator)
        if not np.all(np.isfinite(projected)) or abs(denominator) < 1e-12:
            raise ValueError("Object corners lie on an unstable projective horizon.")
        rectified.append(
            (
                float(projected[0] / denominator * target_scale),
                float(projected[1] / denominator * target_scale),
            )
        )
    if min(denominators) < 0 < max(denominators):
        raise ValueError("Object corners straddle the projective horizon.")
    measured_width = (
        math.dist(rectified[0], rectified[1]) / 2 + math.dist(rectified[3], rectified[2]) / 2
    )
    measured_height = (
        math.dist(rectified[0], rectified[3]) / 2 + math.dist(rectified[1], rectified[2]) / 2
    )
    if not all(math.isfinite(x) and x > 0 for x in (measured_width, measured_height)):
        raise ValueError("Object corners do not produce finite positive measurements.")
    return Measurement(measured_width, measured_height, tuple(rectified))
