"""Console entry point; importing this module never starts the application."""

import argparse
import os
import sys
from pathlib import Path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="EyePause · VisionPlayground")
    parser.add_argument(
        "--demo", action="store_true", help="Simulate eyes and playback; no devices"
    )
    parser.add_argument(
        "--self-test", action="store_true", help="Check bundled model and imports, then exit"
    )
    parser.add_argument("--screenshot", type=Path, help="Save a demo screenshot and exit")
    parser.add_argument("--quit-after", type=float, help=argparse.SUPPRESS)
    args = parser.parse_args(argv)
    # MediaPipe imports matplotlib; keep its font cache in the normal user cache.
    cache = Path(os.environ.get("LOCALAPPDATA", str(Path.home() / ".cache"))) / "EyePause" / "cache"
    os.environ.setdefault("MPLCONFIGDIR", str(cache))
    if args.self_test:
        import json

        from PySide6.QtWidgets import QApplication

        from eyepause.camera import FaceDetector

        app = QApplication.instance() or QApplication([])
        detector = FaceDetector()
        try:
            import numpy as np

            result = detector.detect(np.zeros((480, 640, 3), dtype=np.uint8), 0.0)
            assert result.observation.face_count == 0
        finally:
            detector.close()
        if sys.platform == "win32":
            from winrt.windows.media.control import GlobalSystemMediaTransportControlsSessionManager

            assert GlobalSystemMediaTransportControlsSessionManager is not None
        payload = json.dumps(
            {
                "model": "verified",
                "qt": "loaded",
                "mediapipe": "loaded",
                "inference": "completed",
                "windows_projection": sys.platform == "win32",
            }
        )
        if sys.stdout:
            print(payload)
        # Windowed portable builds have no stdout, so also emit a reviewable artifact.
        Path("eyepause-self-test.json").write_text(payload + "\n", encoding="utf-8")
        app.quit()
        return 0
    from eyepause.ui import run

    return run(
        demo=args.demo or args.screenshot is not None,
        screenshot=args.screenshot,
        quit_after=args.quit_after,
    )


if __name__ == "__main__":
    raise SystemExit(main())
