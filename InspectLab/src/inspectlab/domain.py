"""Anomaly scores are model-specific scores, not probabilities."""

from dataclasses import dataclass


@dataclass(frozen=True)
class AnomalyRegion:
    region_id: int
    box: tuple[int, int, int, int]
    area: int
    max_score: float
    mean_score: float


@dataclass(frozen=True)
class Inspection:
    decision: str
    width: int
    height: int
    threshold: float
    anomalous_pixels: int
    regions: tuple[AnomalyRegion, ...]
