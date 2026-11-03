"""CLI for supplied observations. Importing it never opens a camera or downloads a model."""

import argparse
import json
from importlib.resources import as_file, files
from pathlib import Path

from . import __version__
from .validation import mapping
from .workflow import process

PACKAGE = "framefind"


def _constant(value):
    raise ValueError(f"Non-finite JSON constant is not supported: {value}")


def _load(path: Path) -> dict:
    data = mapping(
        json.loads(path.read_text(encoding="utf-8"), parse_constant=_constant), "manifest"
    )
    if type(data.get("schema_version")) is not int or data["schema_version"] != 1:
        raise ValueError("Manifest schema_version must be 1.")
    if "synthetic" in data and type(data["synthetic"]) is not bool:
        raise ValueError("Manifest synthetic must be a boolean.")
    return data


def _execute(path: Path, *, demo: bool, output_dir: Path | None) -> dict:
    data = _load(path)
    result, artifacts, sources = process(data, path.parent)
    report = {
        "schema_version": 1,
        "project": PACKAGE,
        "version": __version__,
        "mode": "synthetic_demo" if demo else "imported_observations",
        "synthetic": True if demo else data.get("synthetic", False),
        "model_inference": False,
        "source": "packaged demo.json" if demo else path.name,
        "result": result,
        "available_artifacts": sorted(artifacts),
        "exported_artifacts": ["result.json", *sorted(artifacts)] if output_dir else [],
    }
    # Serialize before creating files so invalid computations cannot leave misleading results.
    payload = json.dumps(report, indent=2, sort_keys=True, allow_nan=False) + "\n"
    if output_dir is not None:
        destination = output_dir.resolve()
        protected = {p.resolve() for p in (path, *sources)}
        for name in ("result.json", *artifacts):
            if Path(name).name != name or name in ("", ".", ".."):
                raise ValueError("Artifact names must be simple filenames.")
            if (destination / name).resolve() in protected:
                raise ValueError("Output would overwrite a source manifest or image.")
        destination.mkdir(parents=True, exist_ok=True)
        for name, content in artifacts.items():
            (destination / name).write_bytes(content)
        (destination / "result.json").write_text(payload, encoding="utf-8")
    return report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        prog=PACKAGE,
        description=(
            "FrameFind runnable foundation: process supplied observations; no model inference."
        ),
    )
    parser.add_argument("--version", action="version", version=f"{PACKAGE} {__version__}")
    commands = parser.add_subparsers(dest="command", required=True)
    demo = commands.add_parser("demo", help="Process bundled synthetic observations")
    run = commands.add_parser("run", help="Process an imported observation manifest")
    run.add_argument("input", type=Path)
    for command in (demo, run):
        command.add_argument(
            "--output-dir", type=Path, help="Export result.json and visual artifacts"
        )
    args = parser.parse_args(argv)
    try:
        if args.command == "demo":
            with as_file(files(PACKAGE).joinpath("examples")) as directory:
                report = _execute(directory / "demo.json", demo=True, output_dir=args.output_dir)
        else:
            report = _execute(args.input.resolve(), demo=False, output_dir=args.output_dir)
        print(json.dumps(report, indent=2, sort_keys=True, allow_nan=False))
        return 0
    except (OSError, ValueError, TypeError, KeyError, OverflowError) as error:
        parser.exit(2, f"{PACKAGE}: {error}\n")
