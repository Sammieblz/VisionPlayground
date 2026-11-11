"""Build a local comparison page from retained, unmodified Unity captures."""

import argparse
import hashlib
import html
import json
import shutil
from pathlib import Path


def packaged_run(folder, package, session, title, records):
    """Copy original evidence; never enhance or composite screenshots."""
    if not session.isalnum():
        raise ValueError("Expected an opaque session ID")
    package = Path(package).resolve(strict=True)
    source = package / "outputs/qualification"
    report_path = source / f"run-review-{session}.json"
    report = json.loads(report_path.read_text(encoding="utf-8"))
    performance = report["performance"]
    destination = folder / "packaged" / session
    destination.mkdir(parents=True, exist_ok=True)
    shutil.copy2(report_path, destination / report_path.name)
    shutil.copy2(package / "BuildIdentity.json", destination / "BuildIdentity.json")
    figures = []
    for filename in (
        f"{session}-view1-1280x720.png",
        f"{session}-forward.png",
    ):
        original = source / "overhaul" / filename
        if not original.is_file():
            continue
        copy = destination / filename
        shutil.copy2(original, copy)
        relative = copy.relative_to(folder).as_posix()
        records.append(
            {
                "file": relative,
                "source": str(original),
                "sha256": hashlib.sha256(original.read_bytes()).hexdigest(),
                "session": session,
                "code_sha256": performance["build"]["code_sha256"],
            }
        )
        label = "Driver presentation" if "view1" in filename else "Independent observer camera"
        uri = html.escape(relative, quote=True)
        figures.append(
            f'<figure><a href="{uri}"><img loading="lazy" src="{uri}" '
            f'alt="{label}"></a><figcaption>{label}</figcaption></figure>'
        )
    metrics = [
        ("Advancing simulation", f"{report['observed_simulation_seconds']:.1f} s"),
        ("Mean rendering rate (includes startup)", f"{performance['mean_fps']:.2f} FPS"),
        ("Frame time p95 / p99", f"{performance['p95_ms']:.2f} / {performance['p99_ms']:.2f} ms"),
        (
            "Camera / general detection",
            f"{performance['camera_hz']:.2f} / {performance['detection_hz']:.2f} Hz",
        ),
        (
            "Sensor-to-warning latency p95",
            f"{performance['sensor_to_warning_p95_ms']:.1f} ms "
            f"({performance['warning_samples']} warning samples)"
            if performance["warning_samples"]
            else "Unavailable: no warning samples",
        ),
        ("Player contacts", str(performance["collision_count"])),
        (
            "Fresh Comma healthy samples",
            f"{report['comma_healthy_sample_fraction']:.1%}"
            if report["comma_healthy_sample_fraction"] is not None
            else "Unavailable",
        ),
        ("Driver mode", performance["driver_mode"]),
    ]
    table = "".join(
        f"<tr><th>{html.escape(name)}</th><td>{html.escape(value)}</td></tr>"
        for name, value in metrics
    )
    failed = ", ".join(name for name, passed in report["checks"].items() if passed is False)
    unavailable = ", ".join(name for name, passed in report["checks"].items() if passed is None)
    review_uri = f"packaged/{session}/{report_path.name}"
    return (
        f"<section><h2>{html.escape(title)}</h2>"
        f"<p>Package {html.escape(package.name)} · session {session}. "
        "Recorded evidence for this build only; release qualification remains separate.</p>"
        f'<div class="pair">{"".join(figures)}</div><table>{table}</table>'
        f"<p>Unmet run checks: {html.escape(failed or 'None in this sampled run')}.</p>"
        f"<p>Unavailable checks: {html.escape(unavailable or 'None')}. "
        "Legacy service-reported rates may cover only the period before a disconnect; "
        "whole-session rate coverage is checked separately.</p>"
        f'<p><a href="{review_uri}">Full run report and limitations</a> · '
        f'<a href="packaged/{session}/BuildIdentity.json">Build identity</a></p></section>'
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--run", action="append", nargs=3, default=[], metavar=("PACKAGE", "SESSION", "LABEL")
    )
    parser.add_argument("--video", action="append", nargs=2, default=[], metavar=("FILE", "LABEL"))
    parser.add_argument(
        "--before", type=Path, help="Retained capture folder below the review folder"
    )
    parser.add_argument(
        "--after",
        type=Path,
        default=Path("visual"),
        help="Current capture folder below the review folder",
    )
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    folder = root / "outputs/qualification/realism"
    for selected in (args.before, args.after):
        if selected is not None:
            candidate = (folder / selected).resolve(strict=True)
            if not candidate.is_dir() or not candidate.is_relative_to(folder.resolve()):
                parser.error("Capture folders must be inside outputs/qualification/realism")
    panels = [
        ("Cockpit", "cockpit-720.png", "cockpit-720.png"),
        ("Public instrument placement", "public-instruments-720.png", "public-instruments-720.png"),
        ("Vehicle exterior", "exterior-720.png", "exterior-720.png"),
        ("Ordinary traffic model", "fleet-720.png", "fleet-720.png"),
        ("Bridge extension", "bridge-720.png", "bridge-720.png"),
        ("Storefront", "storefront-720.png", "storefront-720.png"),
        ("Modeled low-rise", "lowrise-720.png", "lowrise-720.png"),
        ("Articulated construction machine", "construction-00-720.png", "construction-00-720.png"),
        ("Railway", "railway-720.png", "railway-720.png"),
        ("Clothed civilian A", "civilian-720.png", "citizen_a-720.png"),
        ("Clothed civilian B", "civilian-720.png", "citizen_b-720.png"),
        ("Cat", "cat-720.png", "cat-720.png"),
        ("Foliage at 24 metres", "vegetation-24m-720.png", "vegetation-24m-720.png"),
        ("Foliage at 65 metres", "vegetation-65m-720.png", "vegetation-65m-720.png"),
    ]
    records = []
    sections = [
        packaged_run(folder, package, session, label, records)
        for package, session, label in args.run
    ]
    for filename, label in args.video:
        original = Path(filename).resolve(strict=True)
        metadata = original.with_suffix(".json")
        capture = json.loads(metadata.read_text(encoding="utf-8"))
        destination = folder / "videos" / original.name
        destination.parent.mkdir(exist_ok=True)
        for source, target in (
            (original, destination),
            (metadata, destination.with_suffix(".json")),
        ):
            if source.resolve() != target.resolve():
                shutil.copy2(source, target)
            with source.open("rb") as stream:
                digest = hashlib.file_digest(stream, "sha256").hexdigest()
            records.append(
                {
                    "file": target.relative_to(folder).as_posix(),
                    "source": str(source),
                    "sha256": digest,
                }
            )
        uri = html.escape(destination.relative_to(folder).as_posix(), quote=True)
        metadata_uri = html.escape(
            destination.with_suffix(".json").relative_to(folder).as_posix(), quote=True
        )
        sample_hz = len(capture.get("capture_times", [])) / max(0.001, capture["seconds"])
        sections.append(
            f"<section><h2>{html.escape(label)}</h2>"
            f'<video controls preload="metadata" style="width:100%" src="{uri}"></video>'
            f"<p>{capture['seconds']:.1f} seconds of the owned game window, "
            f"encoded at {capture['encoded_fps']} FPS. "
            f"Actual screen sampling: {sample_hz:.2f} Hz; duplicated frames preserve duration. "
            "Low capture rates limit animation assessment. "
            "Capture overhead excludes this session from performance qualification. "
            f'<a href="{metadata_uri}">'
            "Capture metadata</a>.</p></section>"
        )
    for title, before, after in panels:
        images = []
        before_folder = args.before or (
            "visual-before-architecture-pass"
            if title in {"Storefront", "Modeled low-rise"}
            else "visual-before-material-pass"
        )
        for label, relative in (
            ("Earlier capture", Path(before_folder) / before),
            ("Scene asset inspection", args.after / after),
        ):
            path = folder / relative
            if not path.is_file():
                continue
            records.append(
                {
                    "file": relative.as_posix(),
                    "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                    "modified_unix": path.stat().st_mtime,
                }
            )
            uri = html.escape(relative.as_posix(), quote=True)
            large = relative.with_name(relative.name.replace("720.png", "1080.png"))
            resolution_link = ""
            if large != relative and (folder / large).is_file():
                records.append(
                    {
                        "file": large.as_posix(),
                        "sha256": hashlib.sha256((folder / large).read_bytes()).hexdigest(),
                    }
                )
                resolution_link = (
                    f' · <a href="{html.escape(large.as_posix(), quote=True)}">1080p capture</a>'
                )
            images.append(
                f'<figure><a href="{uri}"><img loading="lazy" src="{uri}" '
                f'alt="{html.escape(title)} — {label}"></a><figcaption>{label}'
                f"{resolution_link}</figcaption></figure>"
            )
        if images:
            sections.append(
                f'<section><h2>{html.escape(title)}</h2><div class="pair">'
                + "".join(images)
                + "</div></section>"
            )
    page = """<!doctype html><html lang="en"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>CoDriverSim visual review</title><style>
body{background:#141719;color:#edf1f4;font:16px/1.5 system-ui;
margin:0 auto;padding:32px;max-width:1500px}
h1{font-size:32px}h2{font-size:23px}p{max-width:900px;color:#bfc9ce}
.pair{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}
figure{margin:0;background:#20272b;border-radius:10px;overflow:hidden}img{width:100%;display:block}
figcaption{padding:10px 16px}section{margin:36px 0}a{color:#80d6e7}
table{margin:20px 0;border-collapse:collapse}th,td{padding:7px 20px 7px 0;text-align:left}
th{font-weight:500;color:#bfc9ce}tr{border-bottom:1px solid #30383d}
@media(max-width:850px){.pair{grid-template-columns:1fr}body{padding:16px}}
</style><h1>CoDriverSim visual review</h1>
<p>Unmodified Unity presentation captures. Click an image to inspect it at its
original resolution. Cameras and character poses can differ between revisions;
these are visual references, not controlled accuracy comparisons.</p>
<p>Development candidate. These images do not establish driving, controller,
perception or sustained performance acceptance. The local cockpit uses the supplied
Mercedes asset; the public package uses its separate eligible cabin.</p>"""
    (folder / "VISUAL_REVIEW.html").write_text(
        page + "".join(sections) + "</html>", encoding="utf-8"
    )
    (folder / "visual-review-manifest.json").write_text(
        json.dumps(records, indent=2), encoding="utf-8"
    )
    print(folder / "VISUAL_REVIEW.html")


if __name__ == "__main__":
    main()
