import json
import shutil
from importlib.resources import as_file, files

import pytest

from measurelab.cli import main


def test_demo_exports_labeled_results(tmp_path, capsys):
    assert main(["demo", "--output-dir", str(tmp_path)]) == 0
    report = json.loads(capsys.readouterr().out)
    assert report["project"] == "measurelab"
    assert report["mode"] == "synthetic_demo"
    assert report["synthetic"] is True
    assert report["model_inference"] is False
    assert json.loads((tmp_path / "result.json").read_text()) == report
    for name in report["available_artifacts"]:
        assert (tmp_path / name).stat().st_size > 0


def test_imported_manifest_resolves_resources_outside_working_directory(
    tmp_path, monkeypatch, capsys
):
    inputs = tmp_path / "inputs"
    with as_file(files("measurelab").joinpath("examples")) as directory:
        shutil.copytree(directory, inputs)
    elsewhere = tmp_path / "elsewhere"
    elsewhere.mkdir()
    monkeypatch.chdir(elsewhere)
    assert main(["run", str(inputs / "demo.json"), "--output-dir", str(tmp_path / "out")]) == 0
    report = json.loads(capsys.readouterr().out)
    assert report["mode"] == "imported_observations"
    assert report["synthetic"] is True
    assert report["model_inference"] is False


def test_demo_without_export_creates_no_files(tmp_path, monkeypatch, capsys):
    monkeypatch.chdir(tmp_path)
    assert main(["demo"]) == 0
    assert json.loads(capsys.readouterr().out)["exported_artifacts"] == []
    assert list(tmp_path.iterdir()) == []


@pytest.mark.parametrize(
    "contents",
    [
        "not json",
        "[]",
        "{}",
        '{"schema_version":true}',
        '{"schema_version":1,"synthetic":"yes"}',
        '{"schema_version":1,"value":NaN}',
    ],
)
def test_malformed_manifest_has_clear_cli_error(tmp_path, capsys, contents):
    path = tmp_path / "bad.json"
    path.write_text(contents, encoding="utf-8")
    with pytest.raises(SystemExit) as error:
        main(["run", str(path)])
    assert error.value.code != 0
    assert "measurelab:" in capsys.readouterr().err


def test_missing_input_has_clear_cli_error(tmp_path, capsys):
    with pytest.raises(SystemExit) as error:
        main(["run", str(tmp_path / "missing.json")])
    assert error.value.code != 0
    assert capsys.readouterr().err


@pytest.mark.parametrize("flag", ["--help", "--version"])
def test_entry_point_information(flag, capsys):
    with pytest.raises(SystemExit) as error:
        main([flag])
    assert error.value.code == 0
    assert "measurelab" in capsys.readouterr().out
