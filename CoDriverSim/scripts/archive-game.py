"""Archive a verified public package, excluding post-package logs and recordings.

This creates a development download and checksum, not a release qualification.
It never overwrites an archive and refuses local-only asset profiles.
"""

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
from zipfile import ZIP_DEFLATED, ZipFile


def archive_package(root: Path, output: Path) -> dict:
    root = root.resolve(strict=True)
    output = output.resolve()
    checksum = output.with_suffix(output.suffix + ".sha256")
    if output.is_relative_to(root):
        raise ValueError("Keep the archive outside the package being archived")
    if output.exists() or checksum.exists():
        raise FileExistsError("Archive or checksum already exists")
    manifest_bytes = (root / "package-manifest.json").read_bytes()
    manifest = json.loads(manifest_bytes)
    identity = json.loads((root / "BuildIdentity.json").read_bytes())
    audit = json.loads((root / "BuildAssetAudit.json").read_bytes())
    if any(p.get("asset_profile") != "redistributable" for p in (manifest, identity)):
        raise ValueError("Only redistributable packages can become public archives")
    if audit.get("local_assets") or not audit.get("packed_sources"):
        raise ValueError("Packed-asset evidence must exclude local-only assets")
    files = manifest.get("files", {})
    required = {"CoDriverSim.exe", "BuildIdentity.json", "BuildAssetAudit.json"}
    if not isinstance(files, dict) or not required.issubset(files):
        raise ValueError("Package manifest is missing build evidence")
    members = []
    seen = set()
    for name, expected in sorted(files.items()):
        relative = PurePosixPath(name)
        if (
            relative.is_absolute()
            or ".." in relative.parts
            or "\\" in name
            or ":" in name
            or relative.as_posix() != name
            or name.casefold() in seen
        ):
            raise ValueError("Invalid or duplicate manifest path: " + name)
        seen.add(name.casefold())
        path = (root / name).resolve(strict=True)
        if not path.is_relative_to(root) or not path.is_file():
            raise ValueError("Manifest path escapes package or is not a file: " + name)
        if name == "package-manifest.json":
            raise ValueError("Manifest must not contain itself")
        members.append((name, path, expected))
    output.parent.mkdir(parents=True, exist_ok=True)
    created = False
    checksum_created = False
    try:
        with ZipFile(output, "x", compression=ZIP_DEFLATED, compresslevel=6) as archive:
            created = True
            for name, path, expected in members:
                digest = hashlib.sha256()
                # Hash exactly the bytes written, including a file changed after packaging.
                with (
                    path.open("rb") as source,
                    archive.open("CoDriverSim/" + name, "w", force_zip64=True) as target,
                ):
                    while chunk := source.read(1024 * 1024):
                        digest.update(chunk)
                        target.write(chunk)
                if digest.hexdigest() != expected:
                    raise ValueError("Package hash mismatch: " + name)
            archive.writestr("CoDriverSim/package-manifest.json", manifest_bytes)
        with output.open("rb") as source:
            digest = hashlib.file_digest(source, "sha256").hexdigest()
        with checksum.open("x", encoding="utf-8") as target:
            checksum_created = True
            target.write(f"{digest}  {output.name}\n")
    except BaseException:
        if created:
            output.unlink(missing_ok=True)
        if checksum_created:
            checksum.unlink(missing_ok=True)
        raise
    return {
        "archive": str(output),
        "bytes": output.stat().st_size,
        "sha256": digest,
        "manifest_files": len(members),
        "asset_profile": "redistributable",
        "release_qualified": False,
        "scope": "Manifest files only; excludes runtime logs, recordings and unlisted files",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(archive_package(args.root, args.output), indent=2))


if __name__ == "__main__":
    main()
