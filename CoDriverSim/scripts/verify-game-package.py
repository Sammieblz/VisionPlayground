"""Verify a packaged build's hashes and exercise its isolated CLI outside the checkout."""

import argparse
import hashlib
import json
import subprocess
import tempfile
from datetime import UTC, datetime
from pathlib import Path


def validate_shell_launcher(path: Path):
    """Hash integrity alone cannot establish that Bash can read a launcher."""
    if b"\r" in path.read_bytes():
        raise ValueError("Shell launcher requires LF line endings: " + str(path))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--public", action="store_true")
    args = parser.parse_args()
    root = args.root.resolve(strict=True)
    manifest = json.loads((root / "package-manifest.json").read_text(encoding="utf-8"))
    identity = json.loads((root / "BuildIdentity.json").read_text(encoding="utf-8"))
    audit = json.loads((root / "BuildAssetAudit.json").read_text(encoding="utf-8"))
    if manifest["asset_profile"] != identity["asset_profile"]:
        raise ValueError("Package and Unity asset profiles disagree")
    if args.public:
        if identity["asset_profile"] != "redistributable" or audit["local_assets"]:
            raise ValueError("Public package includes local-only art or lacks a public build")
        if not audit["packed_sources"]:
            raise ValueError("Public build has no packed-asset evidence")
    for relative, expected in manifest["files"].items():
        path = (root / relative).resolve(strict=True)
        if not path.is_relative_to(root):
            raise ValueError("Manifest path escapes the package")
        with path.open("rb") as stream:
            actual = hashlib.file_digest(stream, "sha256").hexdigest()
        if actual != expected:
            raise ValueError("Package hash mismatch: " + relative)
        if path.suffix.lower() == ".sh":
            validate_shell_launcher(path)
    python = root / "runtime/python.exe"
    commands = []
    with tempfile.TemporaryDirectory(prefix="codriver-package-check-") as directory:
        for arguments in (
            ["--version"],
            ["--help"],
            ["demo", "--output-dir", str(Path(directory) / "demo")],
        ):
            result = subprocess.run(
                [str(python), "-I", "-m", "codriversim", *arguments],
                cwd=directory,
                text=True,
                capture_output=True,
                check=True,
                timeout=60,
            )
            commands.append(
                {
                    "arguments": arguments,
                    "returncode": result.returncode,
                    "stdout": result.stdout,
                    "stderr": result.stderr,
                }
            )
        if not (Path(directory) / "demo/result.json").is_file():
            raise ValueError("Packaged CLI did not produce its demonstration result")
    report = {
        "verified_utc": datetime.now(UTC).isoformat(),
        "asset_profile": identity["asset_profile"],
        "code_sha256": identity["code_sha256"],
        "checked_files": len(manifest["files"]),
        "commands": commands,
        "release_qualified": False,
        "scope": (
            "Package integrity and isolated CLI; graphics, controllers and driving "
            "require separate checks"
        ),
    }
    output = root / "outputs/qualification/package-verification.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(output)


if __name__ == "__main__":
    main()
