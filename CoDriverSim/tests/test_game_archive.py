"""Public downloads must contain exactly the checked build, never session data."""

import hashlib
import importlib.util
import json
from pathlib import Path
from zipfile import ZipFile

import pytest

spec = importlib.util.spec_from_file_location(
    "game_archive", Path(__file__).resolve().parents[1] / "scripts" / "archive-game.py"
)
archive = importlib.util.module_from_spec(spec)
spec.loader.exec_module(archive)


def package(tmp_path, *, profile="redistributable", local_assets=None):
    root = tmp_path / "package"
    root.mkdir()
    (root / "CoDriverSim.exe").write_bytes(b"test fixture; not an executable")
    (root / "BuildIdentity.json").write_text(json.dumps({"asset_profile": profile}))
    (root / "BuildAssetAudit.json").write_text(
        json.dumps({"packed_sources": ["eligible-model"], "local_assets": local_assets or []})
    )
    manifest = {
        "asset_profile": profile,
        "files": {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in root.iterdir()},
    }
    (root / "package-manifest.json").write_text(json.dumps(manifest))
    return root


def test_archive_excludes_runtime_recordings_and_has_matching_checksum(tmp_path):
    root = package(tmp_path)
    (root / "private-recording.mp4").write_bytes(b"never distribute")
    (root / "outputs").mkdir()
    (root / "outputs/session.json").write_text('{"private": true}')
    output = tmp_path / "game.zip"
    report = archive.archive_package(root, output)
    with ZipFile(output) as zipped:
        assert set(zipped.namelist()) == {
            "CoDriverSim/CoDriverSim.exe",
            "CoDriverSim/BuildIdentity.json",
            "CoDriverSim/BuildAssetAudit.json",
            "CoDriverSim/package-manifest.json",
        }
        assert zipped.read("CoDriverSim/CoDriverSim.exe") == (root / "CoDriverSim.exe").read_bytes()
    assert report["sha256"] == hashlib.sha256(output.read_bytes()).hexdigest()
    assert output.with_suffix(".zip.sha256").read_text().startswith(report["sha256"])
    assert report["release_qualified"] is False


@pytest.mark.parametrize("profile,assets", [("local", []), ("redistributable", ["restricted"])])
def test_local_art_cannot_be_archived_for_public_download(tmp_path, profile, assets):
    root = package(tmp_path, profile=profile, local_assets=assets)
    with pytest.raises(ValueError):
        archive.archive_package(root, tmp_path / "game.zip")
    assert not (tmp_path / "game.zip").exists()


def test_modified_packaged_file_removes_incomplete_archive(tmp_path):
    root = package(tmp_path)
    (root / "CoDriverSim.exe").write_bytes(b"changed after packaging")
    output = tmp_path / "game.zip"
    with pytest.raises(ValueError, match="hash mismatch"):
        archive.archive_package(root, output)
    assert not output.exists()
    assert not output.with_suffix(".zip.sha256").exists()


@pytest.mark.parametrize("name", ["../outside.txt", "/outside.txt", "C:/outside.txt"])
def test_manifest_cannot_add_files_outside_package(tmp_path, name):
    root = package(tmp_path)
    path = root / "package-manifest.json"
    manifest = json.loads(path.read_text())
    manifest["files"][name] = "irrelevant"
    path.write_text(json.dumps(manifest))
    with pytest.raises(ValueError, match="manifest path"):
        archive.archive_package(root, tmp_path / "game.zip")


def test_existing_download_is_preserved(tmp_path):
    root = package(tmp_path)
    output = tmp_path / "game.zip"
    output.write_bytes(b"previous download")
    with pytest.raises(FileExistsError):
        archive.archive_package(root, output)
    assert output.read_bytes() == b"previous download"
