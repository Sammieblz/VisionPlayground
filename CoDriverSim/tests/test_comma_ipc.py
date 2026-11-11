"""IPC lifecycle regression independent of Unity or an installed Comma model."""

import importlib.util
import json
import os
import shutil
import signal
import subprocess
import sys
import time
import uuid
from pathlib import Path

import pytest

spec = importlib.util.spec_from_file_location(
    "comma_ipc", Path(__file__).resolve().parents[1] / "scripts/comma_ipc.py"
)
ipc = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ipc)
TOKEN = "4a" * 16


def fixture(tmp_path):
    shm, proc = tmp_path / "shm", tmp_path / "proc"
    shm.mkdir()
    proc.mkdir()
    own = shm / ipc.namespace(TOKEN)
    own.mkdir()
    (own / "modelV2").write_bytes(b"message queue" * 100)
    other = shm / "unrelated"
    other.mkdir()
    (other / "modelV2").write_bytes(b"keep")
    return shm, proc, own, other


def test_closed_session_reclaims_only_its_own_ipc(tmp_path):
    shm, proc, own, other = fixture(tmp_path)
    result = ipc.cleanup(TOKEN, shm, proc)
    assert result["removed_files"] == 1
    assert result["released_bytes"] >= 1300
    assert not own.exists()
    assert (other / "modelV2").read_bytes() == b"keep"
    assert ipc.cleanup(TOKEN, shm, proc)["removed_files"] == 0


@pytest.mark.parametrize("ownership", ["environment", "mapping"])
def test_live_session_is_preserved_even_without_a_supervisor_child(tmp_path, ownership):
    shm, proc, own, _ = fixture(tmp_path)
    process = proc / "123"
    process.mkdir()
    prefix = ipc.namespace(TOKEN)
    (process / "environ").write_bytes(
        f"OPENPILOT_PREFIX={prefix}\0".encode() if ownership == "environment" else b""
    )
    (process / "maps").write_text(
        f"abcd /dev/shm/{prefix}/modelV2\n" if ownership == "mapping" else ""
    )
    with pytest.raises(RuntimeError, match="still in use"):
        ipc.cleanup(TOKEN, shm, proc)
    assert (own / "modelV2").is_file()


def test_unexpected_contents_are_preserved_without_partial_deletion(tmp_path):
    shm, proc, own, _ = fixture(tmp_path)
    (own / "unexpected-recording-folder").mkdir()
    with pytest.raises(ValueError, match="Unexpected"):
        ipc.cleanup(TOKEN, shm, proc)
    assert (own / "modelV2").is_file()


@pytest.mark.parametrize("token", ["", "../outside", "a" * 31, "Z" * 32])
def test_bad_token_cannot_select_a_filesystem_path(token, tmp_path):
    with pytest.raises(ValueError, match="session token"):
        ipc.cleanup(token, tmp_path, tmp_path)


@pytest.mark.skipif(sys.platform != "linux", reason="Linux subreaper and shared memory")
@pytest.mark.parametrize("crash", [False, True])
def test_supervisor_reaps_orphans_and_releases_ipc_after_bridge_exit(tmp_path, crash):
    scripts = Path(__file__).resolve().parents[1] / "scripts"
    for name in ("supervise-comma.py", "comma_ipc.py"):
        shutil.copy2(scripts / name, tmp_path / name)
    (tmp_path / "unity-comma.py").write_text(
        "import json, os, subprocess, sys, time\n"
        "from pathlib import Path\n"
        "from comma_ipc import namespace\n"
        "args=sys.argv[1:]\n"
        "ready=Path(args[args.index('--ready')+1])\n"
        "token=args[args.index('--token')+1]\n"
        "prefix=namespace(token)\n"
        "os.environ['OPENPILOT_PREFIX']=prefix\n"
        "(Path('/dev/shm')/prefix/'modelV2').write_bytes(b'queue'*1000)\n"
        "child=subprocess.Popen([sys.executable,'-c','import time; time.sleep(120)'])\n"
        "ready.write_text(json.dumps({'pid':os.getpid(),'child':child.pid}))\n"
        "while not ready.with_suffix('.stop').exists(): time.sleep(.05)\n",
        encoding="utf-8",
    )
    token = uuid.uuid4().hex
    ready = tmp_path / "bridge.json"
    supervisor = subprocess.Popen(
        [
            sys.executable,
            str(tmp_path / "supervise-comma.py"),
            "--root",
            str(tmp_path),
            "--token",
            token,
            "--ready",
            str(ready),
        ],
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
    )
    try:
        deadline = time.monotonic() + 10
        while not ready.exists() and time.monotonic() < deadline and supervisor.poll() is None:
            time.sleep(0.05)
        assert ready.exists(), "Stub bridge did not start"
        process = json.loads(ready.read_text())
        if crash:
            os.kill(process["pid"], signal.SIGKILL)
        else:
            ready.with_suffix(".stop").touch()
        output, _ = supervisor.communicate(timeout=15)
        result = json.loads(ready.with_suffix(".ipc-cleanup.json").read_text())
        assert result["status"] == "cleaned", output + str(result)
        assert result["removed_files"] == 1
        assert not (Path("/dev/shm") / ipc.namespace(token)).exists()
        assert not Path(f"/proc/{process['child']}").exists(), "Adopted worker was not reaped"
    finally:
        if supervisor.poll() is None:
            supervisor.terminate()
            supervisor.communicate(timeout=15)
