"""Fetch pinned physics references, cockpit geometry and the offline road compiler."""

import hashlib
import json
from pathlib import Path
from urllib.parse import quote
from urllib.request import Request, urlopen
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[1]
RVP = "192ba860c3c92a16363ffe87fb943dc282e62398"
CAR = "7d4ba189827916452eeadc82d4b712dbc6280a6f"


def fetch(url, path, expected=None):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists():
        data = path.read_bytes()
    else:
        with urlopen(Request(url, headers={"User-Agent": "CoDriverSim/0.1"}), timeout=120) as r:
            data = r.read()
    digest = hashlib.sha256(data).hexdigest()
    if expected and digest != expected:
        raise ValueError(f"Hash mismatch: {path}")
    if not path.exists():
        path.write_bytes(data)
    print(path.name, digest, flush=True)
    return digest


def main():
    manifest = json.loads((ROOT / "game/overhaul-manifest.json").read_text(encoding="utf-8"))
    provenance = json.loads((ROOT / "game/dynamics-provenance.json").read_text(encoding="utf-8"))
    references = ROOT / "outputs/sources/RVP"
    for source in provenance["files"]:
        fetch(source["url"], references / source["path"], source["sha256"])
    fetch(
        f"https://raw.githubusercontent.com/JustInvoke/Randomation-Vehicle-Physics/{RVP}/"
        + quote("MIT LICENSE.txt"),
        ROOT / "src/codriversim/licenses/RVP-MIT.txt",
    )
    for asset in manifest["files"]:
        fetch(asset["url"], ROOT / asset["local"], asset["sha256"])
    for name in ("LICENSE.md", "metadata.json"):
        fetch(
            f"https://raw.githubusercontent.com/KhronosGroup/glTF-Sample-Assets/{CAR}/Models/CarConcept/{name}",
            ROOT / "outputs/sources/CarConcept" / name,
        )
    archive = ROOT / "outputs/tools/sumo-win64-1.28.0.zip"
    fetch(
        "https://sumo.dlr.de/releases/1.28.0/sumo-win64-1.28.0.zip",
        archive,
        "822aa9c57a07a895200d536fd2b3fe8785e36baa9b97ecbe75688e08a636fb8e",
    )
    destination = archive.parent / "sumo-1.28.0"
    if not destination.exists():
        with ZipFile(archive) as bundle:
            for member in bundle.infolist():
                if (
                    not (destination / member.filename)
                    .resolve()
                    .is_relative_to(destination.resolve())
                ):
                    raise ValueError("Unsafe archive path")
            bundle.extractall(destination)


if __name__ == "__main__":
    main()
