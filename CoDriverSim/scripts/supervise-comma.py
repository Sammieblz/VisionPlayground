"""Linux owner for one bridge tree, including cleanup after a bridge hard crash."""

import argparse
import ctypes
import json
import os
import signal
import subprocess
import sys
import time
from pathlib import Path


def descendants(parent):
    pairs = []
    for item in Path("/proc").iterdir():
        if not item.name.isdigit():
            continue
        try:
            status = (item / "stat").read_text().rsplit(")", 1)[1].split()
            pairs.append((int(item.name), int(status[1])))
        except (OSError, ValueError):
            pass
    owned = {parent}
    for _ in range(16):
        owned.update(pid for pid, ppid in pairs if ppid in owned)
    return owned - {parent}


def send(owned, signum):
    for pid in owned:
        try:
            os.kill(pid, signum)
        except ProcessLookupError:
            pass


def reap():
    try:
        while os.waitpid(-1, os.WNOHANG)[0]:
            pass
    except ChildProcessError:
        pass


def main():
    from comma_ipc import cleanup, prepare

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True)
    parser.add_argument("--ready", type=Path, required=True)
    parser.add_argument("--token", required=True)
    args = parser.parse_args()
    # Orphans from the bridge's separate manager session reparent here, never to
    # an unrelated WSL session. No global process-name or distribution shutdown.
    if ctypes.CDLL(None).prctl(36, 1, 0, 0, 0) != 0:  # PR_SET_CHILD_SUBREAPER
        raise RuntimeError("Cannot establish Comma process ownership")
    stopping = False

    def stop(*_):
        nonlocal stopping
        stopping = True

    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    command = [sys.executable, str(Path(__file__).with_name("unity-comma.py")), *sys.argv[1:]]
    prepare(args.token)
    child = None
    try:
        child = subprocess.Popen(command)
        while child.poll() is None and not stopping:
            time.sleep(0.1)
    finally:
        send(descendants(os.getpid()), signal.SIGTERM)
        deadline = time.monotonic() + 8
        while time.monotonic() < deadline:
            if child is None or child.poll() is not None:
                reap()
            if not descendants(os.getpid()):
                break
            time.sleep(0.1)
        send(descendants(os.getpid()), signal.SIGKILL)
        if child is not None:
            child.wait()
        for _ in range(20):
            reap()
            if not descendants(os.getpid()):
                break
            time.sleep(0.05)
        try:
            if descendants(os.getpid()):
                raise RuntimeError("Owned processes remain; preserving IPC")
            result = {"status": "cleaned", **cleanup(args.token)}
        except (OSError, ValueError, RuntimeError) as exc:
            result = {"status": "preserved", "reason": str(exc)}
        try:
            args.ready.with_suffix(".ipc-cleanup.json").write_text(
                json.dumps(result, indent=2), encoding="utf-8"
            )
        except OSError as exc:
            print("Cannot record IPC cleanup:", exc, file=sys.stderr)
        for path in (
            args.ready,
            args.ready.with_suffix(".road.json"),
            args.ready.with_suffix(".ui.json"),
            args.ready.with_suffix(".health.json"),
            args.ready.with_suffix(".stop"),
        ):
            path.unlink(missing_ok=True)
    return child.returncode if child is not None and child.returncode is not None else 1


if __name__ == "__main__":
    raise SystemExit(main())
