"""Reproducible portable Windows build; run in the project Python 3.13 environment."""

import importlib.metadata as metadata
import os
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "src"))


def third_party_licenses() -> str:
    notices = [(ROOT / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8")]
    python_license = Path(sys.base_prefix) / "LICENSE.txt"
    if python_license.exists():
        notices.extend(["\n--- Python ---\n", python_license.read_text(encoding="utf-8")])
    for distribution in sorted(metadata.distributions(), key=lambda d: d.metadata["Name"]):
        notices.append(f"\n--- {distribution.metadata['Name']} {distribution.version} ---\n")
        for file in distribution.files or []:
            normalized = str(file).replace("\\", "/").lower()
            if ".dist-info/" in normalized and any(
                word in Path(normalized).name for word in ("license", "copying", "notice")
            ):
                try:
                    notices.append(distribution.locate_file(file).read_text(encoding="utf-8"))
                except (OSError, UnicodeDecodeError):
                    pass
    return "\n".join(notices)


def main() -> None:
    if sys.platform != "win32" or sys.version_info[:2] != (3, 13):
        raise SystemExit("Build with 64-bit Python 3.13 on Windows.")
    from eyepause import __version__
    from eyepause.resources import verify_model

    verify_model()
    # PyInstaller can replace its existing output. Check every destination first,
    # including resolved symlinks, before allowing that recursive cleanup.
    for directory in (
        ROOT / "build",
        ROOT / "build" / "EyePause",
        ROOT / "dist",
        ROOT / "dist" / "EyePause",
    ):
        if not directory.resolve().is_relative_to(ROOT):
            raise SystemExit(f"Build destination escapes project: {directory}")
    os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
    from PySide6.QtWidgets import QApplication

    from eyepause.ui import app_icon

    app = QApplication.instance() or QApplication([])
    app.setApplicationName("EyePause build")
    if not app_icon().pixmap(64).save(str(ROOT / "packaging" / "eyepause.ico")):
        raise SystemExit("Could not generate the application icon.")
    subprocess.run(
        [
            sys.executable,
            "-m",
            "PyInstaller",
            "--noconfirm",
            "--distpath",
            str(ROOT / "dist"),
            "--workpath",
            str(ROOT / "build"),
            str(ROOT / "packaging" / "EyePause.spec"),
        ],
        cwd=ROOT,
        check=True,
    )
    output = ROOT / "dist" / "EyePause"
    (output / "THIRD_PARTY_LICENSES.txt").write_text(third_party_licenses(), encoding="utf-8")
    (output / "README.txt").write_text(
        "EyePause · VisionPlayground\n\n"
        "Run EyePause.exe to select a player and start monitoring.\n"
        "Run EyePause.exe --demo for the simulated walkthrough (no camera or media access).\n"
        "Keep the entire EyePause folder together. No Python installation is required.\n"
        "Closing the window hides it to the tray; use the tray menu to Exit.\n"
        "Only Windows-registered media sessions are supported.\n"
        "Documentation: https://github.com/Sammieblz/VisionPlayground/tree/main/Eyedetection\n",
        encoding="utf-8",
    )
    archive = ROOT / "dist" / f"EyePause-{__version__}-windows-x64.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as bundle:
        for file in output.rglob("*"):
            if file.is_file():
                bundle.write(file, file.relative_to(output.parent))
    print(f"Portable app: {archive}")


if __name__ == "__main__":
    main()
