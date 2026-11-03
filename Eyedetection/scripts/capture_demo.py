"""Capture real Qt-rendered demo screenshots and an animated walkthrough."""

import os
import sys
from pathlib import Path

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "src"))


def main() -> int:
    from PIL import Image
    from PySide6.QtCore import QTimer
    from PySide6.QtWidgets import QApplication

    from eyepause.ui import MainWindow, configure_application

    destination = ROOT / "docs" / "media"
    destination.mkdir(parents=True, exist_ok=True)
    app = QApplication([])
    configure_application(app)
    app.setQuitOnLastWindowClosed(False)
    window = MainWindow(demo=True)
    window.resize(1210, 1040)
    window.show()
    frames = []

    def capture(name):
        target = destination / f"{name}.png"
        if not window.grab().save(str(target)):
            raise RuntimeError(f"Could not save {target}")
        with Image.open(target) as source:
            frames.append(source.convert("RGB").resize((908, 780)))

    def finish():
        frames[0].save(
            destination / "walkthrough.gif",
            save_all=True,
            append_images=frames[1:],
            duration=[1800, 1200, 2000, 1800, 1600],
            loop=0,
        )
        window.shutdown()

    QTimer.singleShot(600, window._start)
    for delay, name in (
        (2600, "eyes-open"),
        (6400, "eyes-closed"),
        (8200, "auto-paused"),
        (10700, "resumed"),
        (14100, "tracking-lost"),
    ):
        QTimer.singleShot(delay, lambda n=name: capture(n))
    QTimer.singleShot(14500, finish)
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
