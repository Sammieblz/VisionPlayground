from pathlib import Path
from typing import Protocol


class EmbeddingEncoder(Protocol):
    """Future encoders must use a single compatible vector space and model version."""

    def encode_text(self, text: str) -> tuple[float, ...]: ...

    def encode_image(self, image_path: Path) -> tuple[float, ...]: ...
