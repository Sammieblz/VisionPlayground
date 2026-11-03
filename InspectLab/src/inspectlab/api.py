"""Threshold supplied score maps and localize four-connected anomalous regions."""

import math
from collections.abc import Sequence

from .domain import AnomalyRegion, Inspection
from .validation import number, sequence


def inspect_map(scores: Sequence[Sequence[float]], threshold: float) -> Inspection:
    threshold = number(threshold, "threshold")
    rows = [
        [number(score, "score") for score in sequence(row, "score row")]
        for row in sequence(scores, "scores")
    ]
    if not rows or not rows[0] or any(len(row) != len(rows[0]) for row in rows):
        raise ValueError("The score map must be a nonempty rectangular 2D array.")
    height, width = len(rows), len(rows[0])
    visited = set()
    regions = []
    for y in range(height):
        for x in range(width):
            if (x, y) in visited or rows[y][x] < threshold:
                continue
            pending = [(x, y)]
            visited.add((x, y))
            pixels = []
            while pending:
                px, py = pending.pop()
                pixels.append((px, py))
                for nx, ny in ((px - 1, py), (px + 1, py), (px, py - 1), (px, py + 1)):
                    if (
                        0 <= nx < width
                        and 0 <= ny < height
                        and (nx, ny) not in visited
                        and rows[ny][nx] >= threshold
                    ):
                        visited.add((nx, ny))
                        pending.append((nx, ny))
            values = [rows[py][px] for px, py in pixels]
            regions.append(
                AnomalyRegion(
                    len(regions) + 1,
                    (
                        min(px for px, _ in pixels),
                        min(py for _, py in pixels),
                        max(px for px, _ in pixels) + 1,
                        max(py for _, py in pixels) + 1,
                    ),
                    len(pixels),
                    max(values),
                    math.fsum(value / len(values) for value in values),
                )
            )
    return Inspection(
        "review" if regions else "pass",
        width,
        height,
        threshold,
        sum(region.area for region in regions),
        tuple(regions),
    )
