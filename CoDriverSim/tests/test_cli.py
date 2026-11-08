import json
import subprocess
import sys

from codriversim.cli import main


def test_demo_exports_same_report(tmp_path, capsys):
    assert main(["demo", "--output-dir", str(tmp_path)]) == 0
    report = json.loads(capsys.readouterr().out)
    assert json.loads((tmp_path / "result.json").read_text()) == report
    assert report["mode"] == "synthetic_demo"
    assert report["synthetic"] is True
    assert [frame["warning"]["level"] for frame in report["result"]["frames"]] == [
        "quiet",
        "caution",
        "urgent",
        "quiet",
    ]


def test_does_not_overwrite_input(tmp_path, capsys):
    source = tmp_path / "result.json"
    text = '{"schema_version": 1, "frames": [{"timestamp": 0, "speed_mps": 10}]}'
    source.write_text(text)
    assert main(["run", str(source), "--output-dir", str(tmp_path)]) == 1
    assert source.read_text() == text
    assert "overwrite" in capsys.readouterr().err


def test_module_demo_does_not_import_simulator():
    code = (
        "import sys; from codriversim.cli import main; "
        "assert main(['demo']) == 0; assert 'metadrive' not in sys.modules"
    )
    result = subprocess.run([sys.executable, "-c", code], capture_output=True, text=True)
    assert result.returncode == 0, result.stderr


def test_invalid_drive_arguments_fail_before_import(capsys):
    assert main(["drive", "--traffic-density", "nan"]) == 1
    assert "traffic-density" in capsys.readouterr().err
