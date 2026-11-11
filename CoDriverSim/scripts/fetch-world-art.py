"""Fetch the CC0 facade kit, verifying the pinned upstream file digests.

The first preparation saves its catalog under game/world-art-sources.json;
subsequent invocations use that catalog, not a changing API response.
"""

import hashlib
import io
import json
import subprocess
import urllib.request
import zipfile
from pathlib import Path


def fetch(url):
    request = urllib.request.Request(
        url, headers={"User-Agent": "CoDriverSim/0.1 (simulation asset preparation)"}
    )
    with urllib.request.urlopen(request, timeout=90) as response:
        return response.read()


def main():
    root = Path(__file__).resolve().parents[1]
    catalog = root / "game/world-art-sources.json"
    destination = root / "game/Assets/External/Resources/WorldArt/FacadeSource"
    if catalog.exists():
        entry = json.loads(catalog.read_text(encoding="utf-8"))
    else:
        asset = "modular_urban_apartments_facade"
        metadata = json.loads(fetch(f"https://api.polyhaven.com/files/{asset}"))
        model = metadata["gltf"]["2k"]["gltf"]
        files = dict(model["include"])
        files[asset + ".gltf"] = {k: v for k, v in model.items() if k != "include"}
        entry = {
            "asset": asset,
            "license": "CC0-1.0",
            "source": f"https://polyhaven.com/a/{asset}",
            "license_url": "https://polyhaven.com/license",
            "files": files,
        }
        catalog.write_text(json.dumps(entry, indent=2) + "\n", encoding="utf-8")
    for relative, item in entry["files"].items():
        target = (destination / relative).resolve()
        if not target.is_relative_to(destination.resolve()):
            raise ValueError("Asset path escapes destination")
        if target.exists() and hashlib.md5(target.read_bytes()).hexdigest() == item["md5"]:
            continue
        data = fetch(item["url"])
        if len(data) != item["size"] or hashlib.md5(data).hexdigest() != item["md5"]:
            raise ValueError(f"Source verification failed: {relative}")
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
        print("Verified", relative, len(data), flush=True)
    pinned_surface(
        root,
        "fabric-source.json",
        "Fabric034_2K-JPG.zip",
        root / "game/Assets/External/Resources/WorldArt/Fabric",
    )
    pinned_surface(
        root,
        "leather-source.json",
        "Leather026_2K-JPG.zip",
        root / "outputs/asset-downloads/leather",
    )


def pinned_surface(root, manifest, archive_name, destination):
    fabric = json.loads((root / "game" / manifest).read_text(encoding="utf-8"))
    cache = root / "outputs/asset-downloads" / archive_name
    if not cache.exists() or hashlib.sha256(cache.read_bytes()).hexdigest() != fabric["sha256"]:
        cache.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(
            [
                "curl.exe",
                "--fail",
                "--location",
                "--silent",
                "--show-error",
                "-A",
                "Mozilla/5.0 CoDriverSim asset preparation",
                fabric["url"],
                "-o",
                str(cache),
            ],
            check=True,
        )
    data = cache.read_bytes()
    if hashlib.sha256(data).hexdigest() != fabric["sha256"]:
        raise ValueError(f"Source checksum mismatch: {manifest}")
    destination.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        for name in archive.namelist():
            if name.endswith(("_Color.jpg", "_NormalGL.jpg", "_Roughness.jpg")):
                (destination / Path(name).name).write_bytes(archive.read(name))


if __name__ == "__main__":
    main()
