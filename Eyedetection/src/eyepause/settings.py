"""Validated, atomic, local-only settings. No frames or media titles are persisted."""

import json
import math
import os
from dataclasses import asdict, dataclass
from pathlib import Path


@dataclass(frozen=True)
class Settings:
    camera_index: int = 0
    preview: bool = True
    pause_seconds: float = 2.0
    resume_seconds: float = 0.5
    left_closed: float = 0.20
    left_open: float = 0.24
    right_closed: float = 0.20
    right_open: float = 0.24

    def __post_init__(self) -> None:
        if type(self.camera_index) is not int or not 0 <= self.camera_index <= 20:
            raise ValueError("Camera index must be between 0 and 20.")
        if type(self.preview) is not bool:
            raise ValueError("Preview must be a boolean.")
        for value, lo, hi in ((self.pause_seconds, 0.5, 10), (self.resume_seconds, 0.2, 3)):
            if isinstance(value, bool) or not math.isfinite(value) or not lo <= value <= hi:
                raise ValueError("Playback delays are outside the supported range.")
        for closed, opened in (
            (self.left_closed, self.left_open),
            (self.right_closed, self.right_open),
        ):
            if not all(math.isfinite(x) for x in (closed, opened)):
                raise ValueError("Eye thresholds must be finite.")
            if not 0.01 <= closed < opened <= 0.8:
                raise ValueError("Each closed-eye threshold must be below its open-eye threshold.")


def settings_path() -> Path:
    base = Path(os.environ.get("LOCALAPPDATA", str(Path.home() / ".config")))
    return base / "VisionPlayground" / "EyePause" / "settings.json"


def load_settings(path: Path | None = None) -> tuple[Settings, str]:
    path = path or settings_path()
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        if not isinstance(data, dict) or data.get("version") != 1:
            raise ValueError("Unsupported settings format")
        return Settings(**data["settings"]), ""
    except FileNotFoundError:
        return Settings(), ""
    except (OSError, ValueError, TypeError, KeyError):
        return Settings(), "Saved settings could not be read. Defaults are active."


def save_settings(settings: Settings, path: Path | None = None) -> None:
    path = path or settings_path()
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(".tmp")
    temporary.write_text(
        json.dumps({"version": 1, "settings": asdict(settings)}, indent=2), encoding="utf-8"
    )
    temporary.replace(path)
