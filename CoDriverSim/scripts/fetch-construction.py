"""Fetch the pinned CC0 construction model without changing the original."""

import hashlib
import json
import urllib.request
from pathlib import Path


def main():
    root = Path(__file__).resolve().parents[1]
    entry = json.loads((root / "game/construction-manifest.json").read_text(encoding="utf-8"))
    target = root / "outputs/asset-downloads/construction/compact-tracked-excavator.glb"
    if target.exists() and hashlib.sha256(target.read_bytes()).hexdigest() == entry["sha256"]:
        print(target)
        return
    request = urllib.request.Request(entry["url"], headers={"User-Agent": "CoDriverSim/0.1"})
    with urllib.request.urlopen(request, timeout=90) as response:
        data = response.read(entry["size"] + 1)
    if len(data) != entry["size"] or hashlib.sha256(data).hexdigest() != entry["sha256"]:
        raise ValueError("Construction model differs from the pinned source")
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
    print(target)


if __name__ == "__main__":
    main()
