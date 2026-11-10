"""Windows GUI process helper for this application's single owned WSL bridge."""

import argparse
import ctypes
import subprocess
import time
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--distribution", required=True)
    parser.add_argument("--script", required=True)
    parser.add_argument("--ready", type=Path, required=True)
    parser.add_argument("--token", required=True)
    parser.add_argument("--parent", type=int, required=True)
    parser.add_argument("--openpilot", default="")
    args = parser.parse_args()
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.restype = ctypes.c_void_p
    kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_uint]
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    parent = kernel.OpenProcess(0x00100000, False, args.parent)  # SYNCHRONIZE only
    if not parent:
        raise OSError("The owning game process is no longer available")
    stop_file = args.ready.with_suffix(".stop")
    ready_linux = "/mnt/" + args.ready.drive[0].lower() + args.ready.as_posix()[2:]
    command = ["wsl.exe", "--distribution", args.distribution, "--exec"]
    if args.openpilot:
        command.extend(["env", "COMMA_OPENPILOT_DIR=" + args.openpilot])
    command.extend(["bash", args.script, "--ready", ready_linux, "--token", args.token])
    try:
        with subprocess.Popen(
            command, stdin=subprocess.DEVNULL, creationflags=subprocess.CREATE_NO_WINDOW
        ) as child:
            while child.poll() is None:
                if kernel.WaitForSingleObject(parent, 0) == 0:
                    stop_file.write_text(args.token)
                time.sleep(0.2)
            if child.returncode:
                raise SystemExit(child.returncode)
    finally:
        kernel.CloseHandle(parent)


if __name__ == "__main__":
    main()
