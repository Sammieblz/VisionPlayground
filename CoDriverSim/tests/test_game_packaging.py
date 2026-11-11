"""Windows checkouts must not ship Bash launchers with carriage returns."""

import hashlib
import importlib.util
import json
from pathlib import Path

import pytest


def script(name):
    path = Path(__file__).resolve().parents[1] / "scripts" / name
    spec = importlib.util.spec_from_file_location(name.replace("-", "_"), path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


packaging = script("package-game.py")
verification = script("verify-game-package.py")


@pytest.mark.parametrize("mixed", [False, True])
def test_package_copy_normalizes_crlf_shell_without_changing_source(tmp_path, mixed):
    source = tmp_path / "source.sh"
    target = tmp_path / "unity-comma.sh"
    expected = b'#!/usr/bin/env bash\nset -euo pipefail\nprintf "ready\\n"\n'
    original = expected.replace(b"\n", b"\r\n", 1 if mixed else -1)
    source.write_bytes(original)
    packaging.copy_script(source, target)
    assert source.read_bytes() == original
    assert target.read_bytes() == expected
    verification.validate_shell_launcher(target)


def test_package_copy_preserves_non_shell_script_bytes(tmp_path):
    source = tmp_path / "source.ps1"
    target = tmp_path / "copied.ps1"
    source.write_bytes(b"Write-Output 'ready'\r\n")
    packaging.copy_script(source, target)
    assert target.read_bytes() == source.read_bytes()


@pytest.mark.parametrize("ending", [b"\r\n", b"\r"])
def test_shell_validator_rejects_carriage_returns(tmp_path, ending):
    launcher = tmp_path / "unity-comma.sh"
    launcher.write_bytes(ending.join((b"#!/usr/bin/env bash", b"set -euo pipefail", b"")))
    with pytest.raises(ValueError, match="LF line endings"):
        verification.validate_shell_launcher(launcher)


def test_package_verification_rejects_crlf_even_when_manifest_hash_matches(tmp_path, monkeypatch):
    launcher = tmp_path / "scripts/unity-comma.sh"
    launcher.parent.mkdir()
    launcher.write_bytes(b"#!/usr/bin/env bash\r\nset -euo pipefail\r\n")
    (tmp_path / "BuildIdentity.json").write_text(json.dumps({"asset_profile": "local-prototype"}))
    (tmp_path / "BuildAssetAudit.json").write_text(json.dumps({"local_assets": []}))
    (tmp_path / "package-manifest.json").write_text(
        json.dumps(
            {
                "asset_profile": "local-prototype",
                "files": {
                    "scripts/unity-comma.sh": hashlib.sha256(launcher.read_bytes()).hexdigest()
                },
            }
        )
    )
    monkeypatch.setattr("sys.argv", ["verify-game-package.py", "--root", str(tmp_path)])
    monkeypatch.setattr(
        verification.subprocess,
        "run",
        lambda *args, **kwargs: pytest.fail("Invalid package must fail before launch"),
    )
    with pytest.raises(ValueError, match="LF line endings"):
        verification.main()
