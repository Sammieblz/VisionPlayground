"""JSON reporting commands and an optional interactive simulator launcher."""

import argparse
import json
import sys
from importlib.resources import files
from pathlib import Path

from codriversim import __version__
from codriversim.domain import analyze


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Observer-only co-driver simulation experiments")
    parser.add_argument("--version", action="version", version=__version__)
    commands = parser.add_subparsers(dest="command", required=True)
    demo = commands.add_parser("demo", help="Analyze packaged synthetic observations; no graphics")
    demo.add_argument("--output-dir", type=Path)
    run = commands.add_parser("run", help="Analyze a JSON observation manifest")
    run.add_argument("input", type=Path)
    run.add_argument("--output-dir", type=Path)
    sim = commands.add_parser(
        "drive", help="Open a MetaDrive world with truth-based warning overlay"
    )
    sim.add_argument("--seed", type=int, default=21)
    sim.add_argument("--traffic-density", type=float, default=0.1)
    sim.add_argument(
        "--scene",
        choices=("city", "road"),
        default="city",
        help="City streets and buildings (default), or the original road course",
    )
    sim.add_argument("--steps", type=int, help="Exit after this many simulation steps")
    sim.add_argument(
        "--headless", action="store_true", help="Physics-only smoke test with a test driver"
    )
    sim.add_argument("--output-dir", type=Path)
    game = commands.add_parser("game", help="Launch the built Unity driving application")
    game.add_argument("--executable", type=Path)
    replay = commands.add_parser("replay", help="Run the camera observer on a recording or video")
    replay.add_argument("input", type=Path)
    replay.add_argument("--model", type=Path)
    replay.add_argument("--calibration", type=Path)
    replay.add_argument("--max-frames", type=int, default=3000)
    replay.add_argument("--sample-hz", type=float, default=5)
    replay.add_argument("--start-seconds", type=float, default=0)
    replay.add_argument("--output-dir", type=Path)
    evaluation = commands.add_parser("evaluate", help="Score a v2 scenario criteria manifest")
    evaluation.add_argument("input", type=Path)
    evaluation.add_argument("--output-dir", type=Path)
    camera_eval = commands.add_parser(
        "evaluate-ai", help="Compare camera replay with held-out image annotations"
    )
    camera_eval.add_argument("input", type=Path, help="v2 frame and hazard annotations")
    camera_eval.add_argument("predictions", type=Path, help="replay result.json")
    camera_eval.add_argument("--output-dir", type=Path)
    args = parser.parse_args(argv)
    try:
        output_dir = getattr(args, "output_dir", None)
        target = output_dir / "result.json" if output_dir is not None else None
        if target is not None and args.command in {"run", "replay", "evaluate", "evaluate-ai"}:
            if target.resolve() == args.input.resolve() or (
                target.exists() and args.input.exists() and target.samefile(args.input)
            ):
                raise ValueError("Output would overwrite the input manifest")
            if args.command == "evaluate-ai" and (
                target.resolve() == args.predictions.resolve()
                or (
                    target.exists()
                    and args.predictions.exists()
                    and target.samefile(args.predictions)
                )
            ):
                raise ValueError("Output would overwrite the predictions")
        if args.command == "game":
            from codriversim.application import launch

            report = launch(args.executable)
        elif args.command == "replay":
            from codriversim.application import replay

            report = replay(
                args.input,
                args.model,
                args.calibration,
                max_frames=args.max_frames,
                sample_hz=args.sample_hz,
                start_seconds=args.start_seconds,
            )
        elif args.command == "evaluate":
            from codriversim.application import evaluate_manifest

            report = evaluate_manifest(args.input)
        elif args.command == "evaluate-ai":
            from codriversim.application import evaluate_camera_files

            report = evaluate_camera_files(args.input, args.predictions)
        elif args.command == "drive":
            if not 0 <= args.seed <= 2**31 - 101:
                raise ValueError("seed must be between 0 and 2147483547")
            if not 0 <= args.traffic_density <= 1:
                raise ValueError("traffic-density must be between 0 and 1")
            if args.steps is not None and args.steps < 1:
                raise ValueError("steps must be positive")
            from codriversim.simulation import drive

            report = drive(
                seed=args.seed,
                density=args.traffic_density,
                steps=args.steps or (100 if args.headless else None),
                headless=args.headless,
                scene=args.scene,
            )
        else:
            text = (
                files("codriversim").joinpath("examples/demo.json").read_text(encoding="utf-8")
                if args.command == "demo"
                else args.input.read_text(encoding="utf-8-sig")
            )
            report = analyze(json.loads(text), demo=args.command == "demo")
        output = json.dumps(report, indent=2, allow_nan=False)
        if target is not None:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(output + "\n", encoding="utf-8")
        print(output)
        return 0
    except (OSError, ValueError, RuntimeError) as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("Simulation stopped.", file=sys.stderr)
        return 130
