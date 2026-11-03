from pathlib import Path
from typing import Protocol


class AnomalyModel(Protocol):
    """A future implementation supplies raw scores and documents their scale."""

    def score(self, image_path: Path) -> list[list[float]]: ...
