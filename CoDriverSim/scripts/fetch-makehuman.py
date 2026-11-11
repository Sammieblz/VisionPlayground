"""Fetch only pinned CC0 character assets; MPFB remains an offline build tool."""

import argparse
import concurrent.futures
import hashlib
import io
import json
import struct
import subprocess
import urllib.request
import zipfile
import zlib
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[1]
REVISION = "6ff1eb358f6c1959ced77e79e6dd2406d9a71f49"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cache", type=Path, required=True)
    args = parser.parse_args()
    manifest = json.loads((PROJECT / "game/makehuman-core-manifest.json").read_text())
    root = args.cache.resolve()
    root.mkdir(parents=True, exist_ok=True)
    tool = root / "mpfb2"
    if not tool.exists():
        subprocess.run(["git", "init", str(tool)], check=True)
        subprocess.run(
            [
                "git",
                "-C",
                str(tool),
                "fetch",
                "--depth",
                "1",
                "https://github.com/makehumancommunity/mpfb2.git",
                REVISION,
            ],
            check=True,
        )
        subprocess.run(["git", "-C", str(tool), "checkout", "--detach", "FETCH_HEAD"], check=True)
    revision = subprocess.check_output(
        ["git", "-C", str(tool), "rev-parse", "HEAD"], text=True
    ).strip()
    if (
        revision != REVISION
        or subprocess.check_output(
            ["git", "-C", str(tool), "status", "--porcelain", "--untracked-files=no"], text=True
        ).strip()
    ):
        raise ValueError("MPFB cache differs from the pinned source; use a fresh cache directory")

    def fetch(start, length):
        request = urllib.request.Request(
            manifest["url"],
            headers={"Range": f"bytes={start}-{start + length - 1}", "If-Range": manifest["etag"]},
        )
        with urllib.request.urlopen(request, timeout=120) as response:
            if response.status != 206 or response.headers.get("ETag") != manifest["etag"]:
                raise ValueError("Core asset pack changed, or server ignored the byte range")
            data = response.read(length + 1)
            if len(data) != length:
                raise ValueError("Incomplete archive range")
            return data

    target = root / "mpfb-user/data"

    def valid(item):
        destination = (target / item["path"]).resolve()
        if not destination.is_relative_to(target.resolve()):
            raise ValueError("Asset path escapes cache")
        return (
            destination.is_file()
            and hashlib.sha256(destination.read_bytes()).hexdigest() == item["sha256"]
        )

    missing = [item for item in manifest["files"] if not valid(item)]
    if not missing:
        print(f"Verified {len(manifest['files'])} cached core assets and MPFB {REVISION}")
        return
    size = manifest["archive_bytes"]
    tail = fetch(size - 131072, 131072)

    class RemoteZip(io.RawIOBase):
        def __init__(self):
            self.position = 0

        def seekable(self):
            return True

        def tell(self):
            return self.position

        def seek(self, offset, whence=0):
            self.position = offset + (0 if whence == 0 else self.position if whence == 1 else size)
            return self.position

        def read(self, length=-1):
            start = self.position
            length = min(size - start, length if length >= 0 else size - start)
            self.position += length
            if start >= size - len(tail):
                offset = start - size + len(tail)
                return tail[offset : offset + length]
            return fetch(start, length)

    with zipfile.ZipFile(RemoteZip()) as archive:
        index = {entry.filename: entry for entry in archive.infolist()}

    def extract(item):
        entry = index[item["path"]]
        if entry.file_size != item["bytes"]:
            raise ValueError("Core archive member changed")
        header = fetch(entry.header_offset, 30)
        if header[:4] != b"PK\x03\x04":
            raise ValueError("Invalid archive header")
        name_length, extra_length = struct.unpack_from("<HH", header, 26)
        data = fetch(entry.header_offset + 30 + name_length + extra_length, entry.compress_size)
        if entry.compress_type == 8:
            data = zlib.decompress(data, -15)
        elif entry.compress_type != 0:
            raise ValueError("Unsupported archive compression")
        if len(data) != item["bytes"] or hashlib.sha256(data).hexdigest() != item["sha256"]:
            raise ValueError("Core asset checksum mismatch: " + item["path"])
        destination = target / item["path"]
        destination.parent.mkdir(parents=True, exist_ok=True)
        temporary = destination.with_name(destination.name + ".download")
        temporary.write_bytes(data)
        temporary.replace(destination)
        print("Verified " + item["path"], flush=True)

    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        list(pool.map(extract, missing))


if __name__ == "__main__":
    main()
