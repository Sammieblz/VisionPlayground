"""Capture only an identified CoDriverSim window for visual review, not an FPS benchmark."""

import argparse
import ctypes
import hashlib
import json
import time
from contextlib import nullcontext
from pathlib import Path

import cv2
import numpy as np
from PIL import ImageGrab


class Rect(ctypes.Structure):
    _fields_ = [(name, ctypes.c_long) for name in ("left", "top", "right", "bottom")]


class Point(ctypes.Structure):
    _fields_ = [("x", ctypes.c_long), ("y", ctypes.c_long)]


def focus_game(user32, window):
    user32.ShowWindow(ctypes.c_void_p(window), 9)
    user32.SetForegroundWindow(ctypes.c_void_p(window))
    if user32.GetForegroundWindow() != window:
        foreground = user32.GetForegroundWindow()
        thread = user32.GetWindowThreadProcessId(ctypes.c_void_p(foreground), None)
        own_thread = ctypes.windll.kernel32.GetCurrentThreadId()
        attached = user32.AttachThreadInput(own_thread, thread, True)
        try:
            user32.BringWindowToTop(ctypes.c_void_p(window))
            user32.SetForegroundWindow(ctypes.c_void_p(window))
        finally:
            if attached:
                user32.AttachThreadInput(own_thread, thread, False)
    time.sleep(0.2)
    if user32.GetForegroundWindow() != window:
        raise RuntimeError("The owned game must have focus before capture")


def capture_client(user32, window, grabber=None):
    # PrintWindow/window= can return a stale D3D surface. Read only the visible,
    # verified foreground client; refuse focus changes rather than film another app.
    if user32.GetForegroundWindow() != window:
        raise RuntimeError("Game lost focus; capture stopped")
    rect, origin = Rect(), Point()
    user32.GetClientRect(ctypes.c_void_p(window), ctypes.byref(rect))
    user32.ClientToScreen(ctypes.c_void_p(window), ctypes.byref(origin))
    bounds = (origin.x, origin.y, origin.x + rect.right, origin.y + rect.bottom)
    if grabber is None:
        image = np.asarray(ImageGrab.grab(bbox=bounds, all_screens=True))
    else:
        # MSS copies this region directly, avoiding a full multi-monitor bitmap.
        image = cv2.cvtColor(np.asarray(grabber.grab(bounds)), cv2.COLOR_BGRA2RGB)
    if user32.GetForegroundWindow() != window:
        raise RuntimeError("Focus changed during capture; frame discarded")
    return np.asarray(image)


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
    parser.add_argument("--backend", choices=("mss", "pillow"), default="mss")
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
    user32.GetForegroundWindow.restype = ctypes.c_void_p
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
    if args.backend == "mss":
        try:
            from mss import MSS
        except ImportError as exc:
            raise RuntimeError("Install requirements-capture-lock.txt for region capture") from exc
        capture_context = MSS()
    else:
        capture_context = nullcontext(None)
    with capture_context as grabber:
        record(args, output, user32, window, owner, installation, identity, session, grabber)


def record(args, output, user32, window, owner, installation, identity, session, grabber):
    focus_game(user32, window)
    first = capture_client(user32, window, grabber)
    height, width = first.shape[:2]
    writer = cv2.VideoWriter(
        str(output), cv2.VideoWriter_fourcc(*"mp4v"), args.fps, (width, height)
    )
    if not writer.isOpened():
        raise RuntimeError("Cannot open video storage")
    start = time.monotonic()
    samples = []
    distinct_frames = set()
    frames = 0
    failure = None
    try:
        while time.monotonic() - start < args.seconds:
            user32.GetWindowThreadProcessId(ctypes.c_void_p(window), ctypes.byref(owner))
            if not user32.IsWindow(ctypes.c_void_p(window)) or owner.value != args.pid:
                break
            image = capture_client(user32, window, grabber)
            if image.shape != first.shape:
                raise RuntimeError("Window size changed during capture")
            now = time.monotonic() - start
            distinct_frames.add(hashlib.sha256(image.tobytes()).hexdigest())
            frame = cv2.cvtColor(image, cv2.COLOR_RGB2BGR)
            # Preserve wall-clock playback duration if screen capture falls behind.
            target = int(now * args.fps) + 1
            while frames < target:
                writer.write(frame)
                frames += 1
            samples.append(now)
            time.sleep(max(0, frames / args.fps - (time.monotonic() - start)))
    except Exception as exc:
        failure = str(exc)
        raise
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
            capture_method="verified_foreground_client_pixels",
            capture_backend=args.backend,
            effective_capture_hz=len(samples) / (time.monotonic() - start),
            duplicated_encoded_frames=frames - len(samples),
            distinct_captured_frames=len(distinct_frames),
            seconds=time.monotonic() - start,
            requested_seconds=args.seconds,
            completed=failure is None and time.monotonic() - start >= args.seconds,
            capture_error=failure,
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
