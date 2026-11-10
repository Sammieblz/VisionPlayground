"""Package a local Windows development build with an isolated Python runtime.

The package runs the town and camera observer without the repository or Python
installation. Stock Comma still uses the separately installed WSL environment.
Never marks an unqualified build as a release, and never overwrites a package.
"""

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
from datetime import UTC, datetime
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--destination", type=Path)
    parser.add_argument(
        "--public",
        action="store_true",
        help="Require a Unity build with local-only assets excluded",
    )
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    source = root / "outputs/game"
    if sys.platform != "win32" or sys.version_info[:2] != (3, 11):
        raise RuntimeError("Package with the project's Windows Python 3.11 environment")
    if not (source / "CoDriverSim.exe").is_file():
        raise RuntimeError("Build the Unity player before packaging")
    identity = json.loads((source / "BuildIdentity.json").read_text(encoding="utf-8"))
    audit = json.loads((source / "BuildAssetAudit.json").read_text(encoding="utf-8"))
    if args.public and identity.get("asset_profile") != "redistributable":
        raise RuntimeError("Build Unity with -public-assets before creating a public package")
    if args.public and (audit.get("local_assets") or not audit.get("packed_sources")):
        raise RuntimeError("Public build requires packed-asset evidence excluding local-only art")
    timestamp = datetime.now(UTC).strftime("%Y%m%d-%H%M%S")
    target = (args.destination or root / "outputs" / ("CoDriverSim-dev-" + timestamp)).resolve()
    target.mkdir(parents=True, exist_ok=False)
    shutil.copytree(
        source,
        target,
        dirs_exist_ok=True,
        ignore=shutil.ignore_patterns(
            "*_BurstDebugInformation_DoNotShip", "*_BackUpThisFolder_ButDontShipItWithYourGame"
        ),
    )
    runtime = target / "runtime"
    runtime.mkdir()
    base = Path(sys.base_prefix)
    for pattern in ("*.dll", "python*.exe", "LICENSE.txt"):
        for path in base.glob(pattern):
            shutil.copy2(path, runtime / path.name)
    shutil.copytree(base / "DLLs", runtime / "DLLs")
    shutil.copytree(
        base / "Lib",
        runtime / "Lib",
        ignore=shutil.ignore_patterns(
            "site-packages", "__pycache__", "test", "tests", "idlelib", "ensurepip"
        ),
    )
    (runtime / "python311._pth").write_text(
        ".\nDLLs\nLib\nLib/site-packages\nimport site\n", encoding="utf-8"
    )
    packages = runtime / "Lib/site-packages"
    subprocess.run(
        [
            sys.executable,
            "-m",
            "pip",
            "install",
            "--no-compile",
            "--target",
            str(packages),
            "--only-binary=:all:",
            "-r",
            str(root / "requirements-game-lock.txt"),
        ],
        check=True,
    )
    wheels = target / "build-metadata"
    wheels.mkdir()
    subprocess.run(
        [
            sys.executable,
            "-m",
            "pip",
            "wheel",
            "--no-deps",
            "--no-build-isolation",
            "--wheel-dir",
            str(wheels),
            str(root),
        ],
        check=True,
    )
    wheel = next(wheels.glob("visionplayground_codriversim-*.whl"))
    subprocess.run(
        [
            sys.executable,
            "-m",
            "pip",
            "install",
            "--no-deps",
            "--no-compile",
            "--target",
            str(packages),
            str(wheel),
        ],
        check=True,
    )
    (target / "models").mkdir()
    for model in root.joinpath("models").glob("*.onnx"):
        shutil.copy2(model, target / "models" / model.name)
    (target / "scripts").mkdir()
    for name in (
        "unity-comma.py",
        "unity-comma.sh",
        "launch-wsl-bridge.py",
        "supervise-comma.py",
        "comma_vehicle_config.py",
        "comma_sim_card.py",
        "comma_sim_manager.py",
    ):
        shutil.copy2(root / "scripts" / name, target / "scripts" / name)
    notices = target / "Notices"
    notices.mkdir()
    shutil.copy2(root / "LICENSE", target / "LICENSE.txt")
    shutil.copy2(root / "THIRD_PARTY_NOTICES.md", notices)
    shutil.copytree(root / "src/codriversim/licenses", notices / "licenses")
    (notices / "BUNDLED-PATHS.txt").write_text(
        "Source references in THIRD_PARTY_NOTICES.md use repository paths.\n"
        "Bundled license texts are under Notices/licenses/.\n"
        "Asset provenance manifests are beside this file.\n"
        "Unity package pins are under build-metadata/.\n"
        "Python dependency notices remain with runtime/Lib/site-packages/.\n",
        encoding="utf-8",
    )
    for name in ("manifest.json", "packages-lock.json"):
        shutil.copy2(root / "game/Packages" / name, wheels / ("unity-" + name))
    shutil.copy2(source / "BuildIdentity.json", wheels / "BuildIdentity.json")
    for name in (
        "assets-manifest.json",
        "quaternius-manifest.json",
        "realism-manifest.json",
        "surface-rail-manifest.json",
        "overhaul-manifest.json",
        "dynamics-provenance.json",
        "world-art-sources.json",
        "fabric-source.json",
        "leather-source.json",
    ):
        shutil.copy2(root / "game" / name, notices)
    district = target / "DistrictData"
    district.mkdir()
    shutil.copy2(
        root / "game/Assets/Resources/Maps/davis-turn-limits.json",
        district / "davis-turn-limits.json",
    )
    shutil.copy2(root / "scripts/validate-district.py", district / "validate-district.py")
    shutil.copy2(
        root / "game/physical-turn-exclusions.json", district / "physical-turn-exclusions.json"
    )
    shutil.copy2(
        root / "game/Assets/Resources/Maps/davis-downtown.json", district / "davis-downtown.json"
    )
    for name in ("osm.json", "source.json"):
        shutil.copy2(root / "outputs/maps/davis-downtown" / name, district / name)
    shutil.copy2(root / "scripts/compile-district.py", district / "compile-district.py")
    shutil.copy2(root / "game/maps/davis-downtown.json", district / "district-definition.json")
    shutil.copy2(root / "requirements-map-lock.txt", district / "requirements-map-lock.txt")
    shutil.copy2(
        root / "outputs/maps/davis-downtown/playable/generation-report.json",
        district / "generation-report.json",
    )
    (district / "README.txt").write_text(
        "Downtown Davis derived map database - ODbL 1.0\n"
        "Copyright OpenStreetMap contributors: https://www.openstreetmap.org/copyright\n\n"
        "osm.json is the source extract; source.json records its retrieval and hash.\n"
        "davis-downtown.json contains the derived geometry, source IDs and assumptions.\n"
        "Lane widths, height estimates, junction curves and schedules are simulation parameters.\n"
        "The file is not a surveyed road model or a statement of current city traffic rules.\n\n"
        "Reproduce in the VisionPlayground checkout using the pinned map environment,\n"
        "scripts/fetch-overhaul.py and scripts/compile-district.py. The latter is also\n"
        "included here for inspection; it expects the repository directory structure.\n"
        "SUMO 1.28.0 is an offline build dependency and is not bundled.\n",
        encoding="utf-8",
    )
    for index, path in enumerate(root.joinpath("outputs/assets").rglob("*")):
        if (
            path.is_file()
            and "license" in path.name.lower()
            and path.suffix.lower() in {".txt", ".md"}
        ):
            shutil.copy2(path, notices / f"actor-{index}-{path.name}")
    config = {
        "schema_version": 1,
        "python": "runtime/python.exe",
        "wsl_distribution": "Ubuntu-22.04",
        "openpilot_directory": "",
    }
    (target / "codriversim-app.json").write_text(json.dumps(config, indent=2), encoding="utf-8")
    shutil.copy2(root / "docs/UNITY_GAME.md", target / "DEVELOPMENT-STATUS.md")
    shutil.copy2(root / "docs/REALISM_REBUILD.md", target / "REALISM-STATUS.md")
    (target / "START-HERE.txt").write_text(
        "CoDriverSim development build - release qualification incomplete\n\n"
        "Run CoDriverSim.exe. The bundled Python runtime starts automatically.\n"
        "Keep this entire folder together in a location you can write to.\n"
        "Recordings and session diagnostics are saved under outputs.\n"
        "Stock Comma requires the existing WSL v0.9.7 installation.\n"
        "Set wsl_distribution and openpilot_directory in codriversim-app.json if needed.\n"
        "An empty openpilot_directory uses ~/VisionPlayground-experiments/openpilot-onnx-v0.9.7.\n"
        "Human control is the default. Camera warnings cannot control a real car.\n"
        "See DEVELOPMENT-STATUS.md for measured checks and remaining work.\n",
        encoding="utf-8",
    )
    validation = subprocess.run(
        [
            str(runtime / "python.exe"),
            "-I",
            "-c",
            "import sys,cv2,numpy,codriversim; print(sys.executable); "
            "print(codriversim.__file__); print(cv2.__version__,numpy.__version__)",
        ],
        cwd=target,
        check=True,
        capture_output=True,
        text=True,
    )
    if str(target).lower() not in validation.stdout.lower():
        raise RuntimeError("Bundled runtime did not resolve inside its package")
    manifest = {
        "status": "development-unqualified",
        "asset_profile": identity["asset_profile"],
        "created_utc": timestamp,
        "python": sys.version,
        "runtime_check": validation.stdout,
        "files": {
            str(p.relative_to(target)).replace("\\", "/"): hashlib.sha256(
                p.read_bytes()
            ).hexdigest()
            for p in target.rglob("*")
            if p.is_file() and "__pycache__" not in p.parts
        },
    }
    (target / "package-manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(target)


if __name__ == "__main__":
    main()
