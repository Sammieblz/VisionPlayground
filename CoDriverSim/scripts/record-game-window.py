"""Capture only an identified CoDriverSim window for visual review, not an FPS benchmark."""

import argparse
import ctypes
import json
import time
from pathlib import Path

import cv2
import numpy as np
from PIL import ImageGrab


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
