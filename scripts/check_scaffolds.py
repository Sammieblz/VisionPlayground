"""Build, install, and exercise each scaffold independently of its source checkout."""

import argparse
import json
import math
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import venv
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECTS = ("SceneFlow", "DocLens", "InspectLab", "MeasureLab", "FrameFind", "RedactLab")


def check_links() -> None:
    documents = [ROOT / "README.md", ROOT / "ROADMAP.md", ROOT / "CONTRIBUTING.md"]
    for name in PROJECTS:
        documents.append(ROOT / name / "README.md")
        documents.extend((ROOT / name / "docs").rglob("*.md"))
    for document in documents:
        if any(
            part.startswith(".") or part in ("build", "dist")
            for part in document.relative_to(ROOT).parts
        ):
            continue
        content = re.sub(r"```.*?```", "", document.read_text(encoding="utf-8"), flags=re.S)
        for target in re.findall(r"\]\(([^)]+)\)", content):
            target = target.strip("<>").split("#", 1)[0]
            if not target or "://" in target or target.startswith("mailto:"):
                continue
            if not (document.parent / target).is_file():
                raise ValueError(f"Broken local link in {document.relative_to(ROOT)}: {target}")


def check_result(name: str, report: dict, artifacts: Path) -> None:
    if (
        report.get("project") != name.lower()
        or report.get("mode") != "synthetic_demo"
        or report.get("synthetic") is not True
        or report.get("model_inference") is not False
    ):
        raise ValueError(f"{name} demo provenance is missing or incorrect.")
    result = report["result"]
    if name == "SceneFlow":
        assert result["negative_to_positive"] == result["positive_to_negative"] == 1
        assert [(e["track_id"], e["timestamp"]) for e in result["events"]] == [
            ("alpha", 2),
            ("alpha", 3),
        ]
    elif name == "DocLens":
        assert result["merchant"]["value"] == "Vision Cafe"
        assert result["date"]["value"] == "2026-11-03"
        assert result["total"]["value"] == "4.40"
        assert result["currency"]["value"] is None
    elif name == "InspectLab":
        normal, defective = result["inspections"]
        assert normal["decision"] == "pass" and normal["anomalous_pixels"] == 0
        assert defective["decision"] == "review" and defective["anomalous_pixels"] == 3
        assert len(defective["regions"]) == 2
    elif name == "MeasureLab":
        assert math.isclose(result["width_mm"], 40, abs_tol=1e-8)
        assert math.isclose(result["height_mm"], 20, abs_tol=1e-8)
    elif name == "FrameFind":
        assert [match["item_id"] for match in result["matches"]] == ["red-car", "blue-car"]
    else:
        assert result["redacted_pixels"] == 2565 and len(result["regions"]) == 3
        for filename in ("mask.png", "redacted.png"):
            assert (artifacts / filename).read_bytes().startswith(b"\x89PNG\r\n\x1a\n")
    if json.loads((artifacts / "result.json").read_text(encoding="utf-8")) != report:
        raise ValueError("Exported result differs from CLI stdout.")


def verify(name: str, workspace: Path, output: Path, *, check_tools: bool) -> dict:
    project = ROOT / name
    package = name.lower()
    destination = workspace / name
    environment_dir = destination / "venv"
    work = destination / "outside-project"
    work.mkdir(parents=True)
    artifacts = output / name
    artifacts.mkdir(parents=True, exist_ok=True)
    log_path = artifacts / "verification.log"
    log_path.write_text("", encoding="utf-8")
    environment = os.environ.copy()
    for key in ("PYTHONHOME", "PYTHONPATH", "VIRTUAL_ENV"):
        environment.pop(key, None)
    environment["PYTEST_DISABLE_PLUGIN_AUTOLOAD"] = "1"
    environment["PYTHONIOENCODING"] = "utf-8"
    environment["PIP_DISABLE_PIP_VERSION_CHECK"] = "1"
    started = time.perf_counter()
    print(f"[{name}] Creating an independent environment", flush=True)
    venv.EnvBuilder(with_pip=True).create(environment_dir)
    binaries = environment_dir / ("Scripts" if os.name == "nt" else "bin")
    python = binaries / ("python.exe" if os.name == "nt" else "python")

    def run(arguments: list[str], *, capture=False) -> str:
        with log_path.open("a", encoding="utf-8") as log:
            log.write("\n$ " + subprocess.list2cmdline(arguments) + "\n")
            log.flush()
            if capture:
                result = subprocess.run(
                    arguments,
                    cwd=work,
                    env=environment,
                    text=True,
                    encoding="utf-8",
                    stdout=subprocess.PIPE,
                    stderr=log,
                    timeout=600,
                    check=True,
                )
                log.write(result.stdout)
                return result.stdout
            subprocess.run(
                arguments,
                cwd=work,
                env=environment,
                stdout=log,
                stderr=subprocess.STDOUT,
                timeout=600,
                check=True,
            )
            return ""

    run([str(python), "-m", "pip", "install", "-r", str(project / "requirements-lock.txt")])
    if check_tools:
        run(
            [
                str(python),
                "-m",
                "ruff",
                "check",
                str(ROOT / "scripts"),
                "--config",
                str(ROOT / ".ruff.toml"),
            ]
        )
        run(
            [
                str(python),
                "-m",
                "ruff",
                "format",
                "--check",
                str(ROOT / "scripts"),
                "--config",
                str(ROOT / ".ruff.toml"),
            ]
        )
    for operation in (["check"], ["format", "--check"]):
        run(
            [
                str(python),
                "-m",
                "ruff",
                *operation,
                str(project / "src"),
                str(project / "tests"),
                "--config",
                str(project / "pyproject.toml"),
            ]
        )
    wheels = artifacts / "wheels"
    wheels.mkdir(exist_ok=True)
    run(
        [
            str(python),
            "-m",
            "pip",
            "wheel",
            "--no-deps",
            "--no-build-isolation",
            "--wheel-dir",
            str(wheels),
            str(project),
        ]
    )
    wheel = wheels / f"visionplayground_{package}-0.1.0.dev0-py3-none-any.whl"
    run([str(python), "-m", "pip", "install", "--no-deps", "--force-reinstall", str(wheel)])
    run([str(python), "-m", "pip", "check"])
    run(
        [
            str(python),
            "-I",
            "-m",
            "pytest",
            "-q",
            str(project / "tests"),
            "--import-mode=importlib",
            "-o",
            "pythonpath=",
            "-o",
            f"cache_dir={work / 'pytest-cache'}",
            f"--junitxml={artifacts / 'tests.xml'}",
        ]
    )
    executable = binaries / (package + (".exe" if os.name == "nt" else ""))
    run([str(executable), "--help"])
    run([str(executable), "--version"])
    report = json.loads(
        run([str(executable), "demo", "--output-dir", str(artifacts)], capture=True)
    )
    check_result(name, report, artifacts)
    # The module entry point must also find package resources outside the checkout.
    module_report = json.loads(run([str(python), "-I", "-m", package, "demo"], capture=True))
    assert module_report["result"] == report["result"]
    # Confirm the actual imported module came from the fresh installation.
    imported = run(
        [str(python), "-I", "-c", f"import {package}; print({package}.__file__)"], capture=True
    ).strip()
    if not Path(imported).resolve().is_relative_to(environment_dir.resolve()):
        raise ValueError(f"{name} imported its source checkout instead of the installed wheel.")
    duration = time.perf_counter() - started
    print(f"[{name}] Wheel, tests, CLI, and independent demo passed ({duration:.1f}s)", flush=True)
    return {
        "project": name,
        "status": "passed",
        "python": sys.version.split()[0],
        "platform": sys.platform,
        "seconds": duration,
        "installed_from_wheel": True,
        "outside_source_directory": True,
        "demo_mode": report["mode"],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--project",
        action="append",
        choices=PROJECTS,
        help="Check selected projects; default: all six",
    )
    args = parser.parse_args()
    if sys.version_info[:2] not in ((3, 12), (3, 13)):
        parser.error("Use Python 3.12 or 3.13.")
    selected = list(dict.fromkeys(args.project or PROJECTS))
    cache = ROOT / ".cache" / "scaffold-verification"
    cache.mkdir(parents=True, exist_ok=True)
    workspace = Path(tempfile.mkdtemp(prefix="run-", dir=cache))
    output = ROOT / "build" / "scaffolds"
    output.mkdir(parents=True, exist_ok=True)
    reports = []

    def write_summary(status: str, error: str | None = None) -> None:
        summary = {
            "status": status,
            "python": sys.version.split()[0],
            "platform": sys.platform,
            "projects": reports,
        }
        if error is not None:
            summary["error"] = error
        (output / "verification.json").write_text(
            json.dumps(summary, indent=2) + "\n", encoding="utf-8"
        )

    write_summary("running")
    try:
        check_links()
        for index, name in enumerate(selected):
            reports.append(verify(name, workspace, output, check_tools=index == 0))
    except (OSError, ValueError, AssertionError, subprocess.SubprocessError) as error:
        write_summary("failed", str(error))
        print(
            f"Verification failed: {error}\nInspect {output} for per-project logs.", file=sys.stderr
        )
        return 1
    finally:
        # Delete only the fresh verification workspace after resolving its target.
        if workspace.resolve() == cache.resolve() or not workspace.resolve().is_relative_to(
            cache.resolve()
        ):
            raise RuntimeError("Refusing to clean an environment outside the verification cache.")
        shutil.rmtree(workspace)
    write_summary("passed")
    print(f"Verified {len(reports)} independent projects. Artifacts: {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
