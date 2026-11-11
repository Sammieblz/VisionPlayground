"""Fetch exact licensed asset revisions from the checked-in hash manifest."""

import hashlib
import json
from pathlib import Path
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]


def fetch(asset):
    path = (ROOT / asset["local"].replace("\\", "/")).resolve()
    if not path.is_relative_to(ROOT):
        raise ValueError("Asset path escapes the project")
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        data = path.read_bytes()
    else:
        req = Request(asset["url"], headers={"User-Agent": "CoDriverSim-local-development/0.1"})
        with urlopen(req, timeout=90) as response:
            data = response.read()
    actual = hashlib.sha256(data).hexdigest()
    if actual != asset["sha256"]:
        raise ValueError(
            f"Hash mismatch for {path.name}: expected {asset['sha256']}, received {actual}"
        )
    if not path.exists():
        temporary = path.with_suffix(path.suffix + ".part")
        temporary.write_bytes(data)
        temporary.replace(path)
    print(asset["name"], actual, flush=True)


def main():
    for name in ("assets-manifest.json", "realism-manifest.json", "surface-rail-manifest.json"):
        manifest = json.loads((ROOT / "game" / name).read_text(encoding="utf-8"))
        assets = manifest["files"] if isinstance(manifest, dict) else manifest
        for asset in assets:
            fetch(asset)


if __name__ == "__main__":
    main()
