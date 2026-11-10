"""Capture only an identified CoDriverSim window for visual review, not an FPS benchmark."""

import argparse
import ctypes
import json
import time
from pathlib import Path

import cv2
import numpy as np
from PIL import ImageGrab


def process_installation(pid):
    """Resolve the actual window owner's executable, without guessing its package."""
    kernel = ctypes.windll.kernel32
    kernel.OpenProcess.restype = ctypes.c_void_p
    process = kernel.OpenProcess(0x1000, False, pid)
    if not process:
        raise OSError("Cannot inspect the requested game process")
    try:
        path = ctypes.create_unicode_buffer(32768)
        size = ctypes.c_ulong(len(path))
        if not kernel.QueryFullProcessImageNameW(
            ctypes.c_void_p(process), 0, path, ctypes.byref(size)
        ):
            raise OSError("Cannot resolve the game executable")
        executable = Path(path.value).resolve(strict=True)
        if executable.name.casefold() != "codriversim.exe":
            raise ValueError("The window does not belong to a CoDriverSim executable")
        return executable.parent
    finally:
        kernel.CloseHandle(ctypes.c_void_p(process))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--seconds", type=float, default=60)
    parser.add_argument("--fps", type=int, default=15)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if not 1 <= args.seconds <= 600 or not 1 <= args.fps <= 30:
        raise ValueError("Capture requires 1–600 seconds and 1–30 FPS")
    output = args.output.resolve()
    if output.exists() or output.with_suffix(".json").exists():
        raise FileExistsError("Capture evidence must not be overwritten")
    output.parent.mkdir(parents=True, exist_ok=True)
    user32 = ctypes.windll.user32
    user32.SetProcessDPIAware()
    user32.FindWindowW.restype = ctypes.c_void_p
    window = user32.FindWindowW(None, "CoDriverSim")
    owner = ctypes.c_ulong()
    user32.GetWindowThreadProcessId(ctypes.c_void_p(window), ctypes.byref(owner))
    if not window or owner.value != args.pid:
        raise ValueError("The requested process does not own the CoDriverSim window")
    installation = process_installation(args.pid)
    identity = json.loads((installation / "BuildIdentity.json").read_text(encoding="utf-8"))
    sessions = installation / "outputs/sessions"
    active_sessions = [
        p
        for p in sessions.glob("*/driving-diagnostics.jsonl")
        if p.stat().st_mtime >= time.time() - 10
    ]
    if len(active_sessions) != 1:
        raise ValueError("Expected one live diagnostic session before recording")
    session = active_sessions[0].parent.name
    first = np.asarray(ImageGrab.grab(window=int(window)))
    height, width = first.shape[:2]
    writer = cv2.VideoWriter(
        str(output), cv2.VideoWriter_fourcc(*"mp4v"), args.fps, (width, height)
    )
    if not writer.isOpened():
        raise RuntimeError("Cannot open video storage")
    start = time.monotonic()
    samples = []
    frames = 0
    try:
        while time.monotonic() - start < args.seconds:
            user32.GetWindowThreadProcessId(ctypes.c_void_p(window), ctypes.byref(owner))
            if not user32.IsWindow(ctypes.c_void_p(window)) or owner.value != args.pid:
                break
            image = np.asarray(ImageGrab.grab(window=int(window)))
            if image.shape != first.shape:
                raise RuntimeError("Window size changed during capture")
            now = time.monotonic() - start
            frame = cv2.cvtColor(image, cv2.COLOR_RGB2BGR)
            # Preserve wall-clock playback duration if screen capture falls behind.
            target = int(now * args.fps) + 1
            while frames < target:
                writer.write(frame)
                frames += 1
            samples.append(now)
            time.sleep(max(0, frames / args.fps - (time.monotonic() - start)))
    finally:
        writer.release()
        report = dict(
            pid=args.pid,
            installation=str(installation),
            session=session,
            build=identity,
            width=width,
            height=height,
            encoded_fps=args.fps,
            encoded_frames=frames,
            capture_times=samples,
            seconds=time.monotonic() - start,
            scope=(
                "Owned game-window visual evidence; capture overhead excludes this "
                "from performance qualification"
            ),
        )
        output.with_suffix(".json").write_text(
            json.dumps(report, indent=2) + "\n", encoding="utf-8"
        )
    print(output)


if __name__ == "__main__":
    main()
