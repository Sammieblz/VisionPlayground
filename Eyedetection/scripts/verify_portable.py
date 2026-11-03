"""Exercise an extracted ZIP with Python/venv paths removed from the child environment."""

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main() -> None:
    parser = argparse.ArgumentParser(description="Verify an EyePause portable Windows ZIP")
    parser.add_argument("archive", type=Path)
    args = parser.parse_args()
    if sys.platform != "win32":
        parser.error("Portable verification requires Windows.")
    cache = ROOT / ".cache"
    cache.mkdir(exist_ok=True)
    destination = Path(tempfile.mkdtemp(prefix="portable-", dir=cache))
    with zipfile.ZipFile(args.archive) as archive:
        for member in archive.namelist():
            if not (destination / member).resolve().is_relative_to(destination):
                raise ValueError("Archive member escapes the extraction directory.")
        archive.extractall(destination)
    bundle = destination / "EyePause"
    executable = bundle / "EyePause.exe"
    environment = os.environ.copy()
    for name in ("PYTHONHOME", "PYTHONPATH", "VIRTUAL_ENV"):
        environment.pop(name, None)
    # The frozen interpreter must find its own libraries. Keep only Windows
    # system executables in PATH; this is still the build host, not a clean VM.
    environment["PATH"] = str(Path(environment["WINDIR"]) / "System32")
    environment["QT_QPA_PLATFORM"] = "offscreen"
    environment["MPLCONFIGDIR"] = str(destination / "matplotlib-cache")
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    for arguments in (["--self-test"], ["--screenshot", "portable-demo.png"]):
        subprocess.run(
            [str(executable), *arguments],
            cwd=bundle,
            env=environment,
            startupinfo=startup,
            timeout=30,
            check=True,
        )
    payload = json.loads((bundle / "eyepause-self-test.json").read_text(encoding="utf-8"))
    if payload.get("inference") != "completed" or not payload.get("windows_projection"):
        raise RuntimeError("Portable self-test did not verify all runtime components.")
    screenshot = bundle / "portable-demo.png"
    if not screenshot.is_file() or screenshot.stat().st_size == 0:
        raise RuntimeError("Portable demo did not produce a screenshot.")
    with args.archive.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    output = ROOT / "build" / "portable-verification.json"
    output.parent.mkdir(exist_ok=True)
    shutil.copyfile(screenshot, output.parent / "portable-demo.png")
    payload.update(
        {
            "archive_sha256": digest,
            "python_paths_removed": True,
            "screenshot": "build/portable-demo.png",
            "limitation": "Same-host isolated process; clean-machine and network-disabled "
            "validation still require a separate environment.",
        }
    )
    output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(payload, indent=2))
    # Only delete this freshly created extraction folder, after checking its
    # resolved location. Keep the stable report and screenshot for inspection.
    if destination.resolve() == cache.resolve() or not destination.resolve().is_relative_to(
        cache.resolve()
    ):
        raise RuntimeError("Refusing to clean an extraction outside the project cache.")
    shutil.rmtree(destination)


if __name__ == "__main__":
    main()
