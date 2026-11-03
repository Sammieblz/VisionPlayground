# Build with: python scripts/build.py
from pathlib import Path

from PyInstaller.utils.hooks import collect_all, collect_data_files

root = Path(SPECPATH).parent
mp_data, mp_binaries, mp_imports = collect_all("mediapipe")
a = Analysis(
    [str(root / "packaging" / "entrypoint.py")],
    pathex=[str(root / "src")],
    binaries=mp_binaries,
    datas=mp_data + collect_data_files("eyepause", includes=["assets/*"]),
    hiddenimports=mp_imports + [
        "winrt.windows.media.control", "winrt.windows.foundation",
        "winrt.windows.foundation.collections", "winrt.windows.media",
        "winrt.windows.storage.streams",
    ],
    hookspath=[],
    runtime_hooks=[],
    excludes=["tkinter", "PyQt5", "PyQt6", "IPython", "pytest"],
    noarchive=False,
)
pyz = PYZ(a.pure)
exe = EXE(
    pyz, a.scripts, [], exclude_binaries=True, name="EyePause", debug=False,
    bootloader_ignore_signals=False, strip=False, upx=False, console=False,
    icon=str(root / "packaging" / "eyepause.ico"),
)
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=False, name="EyePause")

