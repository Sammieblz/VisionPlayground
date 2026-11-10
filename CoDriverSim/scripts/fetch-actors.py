"""Fetch free Quaternius packs through their ordinary free-download endpoints.

Archives stay outside source control. Never requests a paid file or bypasses a
quarantine/authorization response. Archives must match the reviewed manifest.
"""

import hashlib
import json
import re
import shutil
import zipfile
from pathlib import Path
from urllib.parse import parse_qs, urlparse

import requests

ROOT = Path(__file__).resolve().parents[1]
PACKS = [
    "universal-animation-library",
    "universal-base-characters",
    "lowpoly-animated-animals",
    "textured-lowpoly-trees",
    "lowpoly-cars",
]
RECORDS = json.loads((ROOT / "game/quaternius-manifest.json").read_text(encoding="utf-8"))
EXPECTED = {record["name"]: record["sha256"] for record in RECORDS}


def verify_archive(archive, name):
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    if digest != EXPECTED[name]:
        raise ValueError(f"Asset hash differs from reviewed manifest: {name}")


def save_download(archive, content, name):
    temporary = archive.with_suffix(".download")
    temporary.write_bytes(content)
    verify_archive(temporary, name)
    temporary.replace(archive)


def download(slug):
    archive = ROOT / "outputs/assets" / f"{slug}.zip"
    url = "https://quaternius.itch.io/" + slug
    archive.parent.mkdir(parents=True, exist_ok=True)
    if not archive.exists():
        session = requests.Session()
        page = session.get(url + "/purchase", timeout=60)
        page.raise_for_status()
        csrf = re.search(r'<meta name="csrf_token" value="([^"]+)"', page.text).group(1)
        result = session.post(url + "/download_url", data={"csrf_token": csrf}, timeout=60)
        result.raise_for_status()
        location = result.json()["url"]
        page = session.get(location, timeout=60)
        page.raise_for_status()
        csrf = re.search(r'<meta name="csrf_token" value="([^"]+)"', page.text).group(1)
        upload_id = re.search(r'data-upload_id="(\d+)"', page.text).group(1)
        params = {"source": "game_download"}
        key = parse_qs(urlparse(location).query).get("key")
        if key:
            params["key"] = key[0]
        result = session.post(
            url + "/file/" + upload_id, params=params, data={"csrf_token": csrf}, timeout=60
        )
        result.raise_for_status()
        payload = result.json()
        if "errors" in payload or "lightbox" in payload:
            raise RuntimeError("Download needs review")
        response = session.get(payload["url"], timeout=180)
        response.raise_for_status()
        save_download(archive, response.content, slug)
    verify_archive(archive, slug)
    target = (ROOT / "outputs/assets" / slug).resolve()
    target.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as zipped:
        for member in zipped.infolist():
            if not (target / member.filename).resolve().is_relative_to(target):
                raise ValueError("Archive path escapes asset directory")
        zipped.extractall(target)
    print(slug, archive.stat().st_size, "bytes", flush=True)


def main():
    for slug in PACKS:
        download(slug)
    animal_url = (
        "https://opengameart.org/sites/default/files/Animal%20Pack%20Vol.2%20by%20%40Quaternius.zip"
    )
    animal_archive = ROOT / "outputs/assets/animals-vol2.zip"
    if not animal_archive.exists():
        response = requests.get(animal_url, timeout=90)
        response.raise_for_status()
        save_download(animal_archive, response.content, "Quaternius Animal Pack Vol.2")
    verify_archive(animal_archive, "Quaternius Animal Pack Vol.2")
    animal_folder = (ROOT / "outputs/assets/animals-vol2").resolve()
    animal_folder.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(animal_archive) as zipped:
        for member in zipped.infolist():
            if not (animal_folder / member.filename).resolve().is_relative_to(animal_folder):
                raise ValueError("Archive path escapes asset directory")
        zipped.extractall(animal_folder)
    target = ROOT / "game/Assets/External/Resources/Actors"
    target.mkdir(parents=True, exist_ok=True)
    for name in (
        "UAL1_Standard.fbx",
        "Superhero_Male_FullBody.fbx",
        "Superhero_Female_FullBody.fbx",
    ):
        source = next(p for p in (ROOT / "outputs/assets").rglob(name) if "Unity" in p.parts)
        shutil.copy2(source, target / name)
    props = ROOT / "game/Assets/External/Resources/Scenery"
    props.mkdir(parents=True, exist_ok=True)
    for source in animal_folder.rglob("*.fbx"):
        if source.stem.lower() in {"cat", "dog", "eagle"}:
            shutil.copy2(source, props / source.name)
    for name in (
        "Pug.fbx",
        "Tree_1.fbx",
        "Birch_1.fbx",
        "Pine_2.fbx",
        "NormalCar1.fbx",
        "SUV.fbx",
        "Cop.fbx",
    ):
        source = next((ROOT / "outputs/assets").rglob(name))
        shutil.copy2(source, props / name)
    textures = ROOT / "outputs/assets/textured-lowpoly-trees"
    for texture in textures.rglob("*.png"):
        if "preview" not in texture.name.lower():
            shutil.copy2(texture, props / texture.name)


if __name__ == "__main__":
    main()
