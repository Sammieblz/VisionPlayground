"""Own only one game's ephemeral openpilot message queues in Linux shared memory."""

import os
import re
import stat
from pathlib import Path


def namespace(token):
    if not re.fullmatch(r"[0-9a-f]{32}", token):
        raise ValueError("Expected a 32-character game session token")
    return "codriver_unity_" + token[:12]


def active_users(prefix, proc=Path("/proc")):
    """Check environment ownership and actual mappings, without logging environments."""
    users = []
    marker = b"OPENPILOT_PREFIX=" + prefix.encode()
    mapping = b"/dev/shm/" + prefix.encode() + b"/"
    for entry in proc.iterdir():
        if not entry.name.isdigit():
            continue
        try:
            environment = (entry / "environ").read_bytes().split(b"\0")
            maps = (entry / "maps").read_bytes()
        except FileNotFoundError:
            continue  # Process exited while inspecting it.
        except PermissionError:
            # Protected login/service helpers are outside our process tree.
            # The supervisor separately requires all adopted descendants to
            # exit before cleanup; this scan also catches accessible peers.
            continue
        if marker in environment or mapping in maps:
            users.append(int(entry.name))
    return users


def cleanup(token, base=Path("/dev/shm"), proc=Path("/proc")):
    """Remove a known session's flat IPC files only after its users have exited.

    The caller must hold that session's token. No prefix glob, recursive delete,
    global IPC cleanup, or WSL shutdown is used. Recordings are never here.
    """
    return cleanup_namespace(namespace(token), base, proc)


def cleanup_namespace(prefix, base=Path("/dev/shm"), proc=Path("/proc")):
    """Recovery entry point; callers must first verify a retained session record."""
    if not re.fullmatch(r"codriver_unity_[0-9a-f]{12}", prefix):
        raise ValueError("Not an exact CoDriverSim session namespace")
    base = base.resolve(strict=True)
    directory = base / prefix
    if directory.is_symlink():
        raise ValueError("Refusing a redirected IPC directory")
    if not directory.exists():
        return {"removed_files": 0, "released_bytes": 0}
    if directory.resolve(strict=True).parent != base or not directory.is_dir():
        raise ValueError("IPC path is outside its owning shared-memory directory")
    uid = getattr(os, "geteuid", lambda: directory.stat().st_uid)()
    if directory.stat().st_uid != uid:
        raise ValueError("IPC directory belongs to a different user")
    if active_users(prefix, proc):
        raise RuntimeError("Session IPC is still in use")
    entries = list(directory.iterdir())
    released = 0
    for path in entries:
        info = path.lstat()
        if not stat.S_ISREG(info.st_mode) or info.st_uid != uid:
            raise ValueError("Unexpected IPC entry; preserve the session directory")
        released += getattr(info, "st_blocks", 0) * 512 or info.st_size
    for path in entries:
        path.unlink()
    directory.rmdir()
    return {"removed_files": len(entries), "released_bytes": released}


def prepare(token):
    """A restart may reuse its own token after the prior owner has fully exited."""
    cleanup(token)
    directory = Path("/dev/shm") / namespace(token)
    directory.mkdir(mode=0o700)
    return directory
