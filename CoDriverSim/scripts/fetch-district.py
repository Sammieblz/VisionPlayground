"""Fetch a bounded OSM district once, retaining its query, source timestamp and hash.

The downloaded ODbL data stays outside source control. Existing output is reused only
when its recorded hash and district definition match; this command never replaces it.
"""

import argparse
import hashlib
import json
import math
from datetime import UTC, datetime
from pathlib import Path
from urllib.parse import urlencode
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]
ENDPOINT = "https://overpass.private.coffee/api/interpreter"
LIMIT = 32 * 1024 * 1024


def query_for(district):
    south, west, north, east = district["bbox_south_west_north_east"]
    if not all(math.isfinite(v) for v in (south, west, north, east)):
        raise ValueError("Bounds must be finite")
    if not (-85 < south < north < 85 and -180 <= west < east <= 180):
        raise ValueError("Invalid district bounds")
    if north - south > 0.03 or east - west > 0.04:
        raise ValueError("District is too large for this bounded public-API fetch")
    return f"""[out:json][timeout:45][bbox:{south},{west},{north},{east}];
(
  way[highway];
  way[building];
  relation[building];
  way[railway];
  way[landuse];
  way[leisure=park];
  way[amenity=parking];
  node[highway~"^(traffic_signals|stop|crossing|bus_stop)$"];
  node[railway~"^(station|halt|level_crossing|crossing)$"];
  node[public_transport];
);
out body geom;
"""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--district", type=Path, default=ROOT / "game/maps/davis-downtown.json")
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--source", choices=("overpass", "osm-api"), default="overpass")
    args = parser.parse_args()
    district = json.loads(args.district.read_text(encoding="utf-8"))
    query = query_for(district)
    output = args.output_dir.resolve()
    raw_path, manifest_path = output / "osm.json", output / "source.json"
    if raw_path.exists() or manifest_path.exists():
        if not raw_path.is_file() or not manifest_path.is_file():
            raise ValueError("Incomplete previous fetch; use a new output directory")
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        digest = hashlib.sha256(raw_path.read_bytes()).hexdigest()
        if (
            manifest["sha256"] != digest
            or manifest["query"] != query
            or manifest["district"] != district
        ):
            raise ValueError("Existing data does not match; use a new output directory")
        print(json.dumps({"status": "verified-cache", "path": str(raw_path), "sha256": digest}))
        return
    south, west, north, east = district["bbox_south_west_north_east"]
    endpoint = (
        ENDPOINT
        if args.source == "overpass"
        else (f"https://api.openstreetmap.org/api/0.6/map.json?bbox={west},{south},{east},{north}")
    )
    request = Request(
        endpoint,
        data=urlencode({"data": query}).encode() if args.source == "overpass" else None,
        headers={
            "User-Agent": "CoDriverSim-district-survey/0.1 (+https://github.com/Sammieblz/VisionPlayground)"
        },
    )
    with urlopen(request, timeout=90) as response:
        data = response.read(LIMIT + 1)
    if len(data) > LIMIT:
        raise ValueError("Map exceeds the 32 MiB download limit")
    payload = json.loads(data)
    if payload.get("remark") or not payload.get("elements"):
        raise ValueError(
            "Map source returned an incomplete/empty result: " + str(payload.get("remark"))
        )
    digest = hashlib.sha256(data).hexdigest()
    timestamp = payload.get("osm3s", {}).get("timestamp_osm_base")
    manifest = {
        "schema_version": 1,
        "district": district,
        "endpoint": endpoint,
        "source_type": args.source,
        "query": query,
        "snapshot_query": query.replace("[out:json]", f'[out:json][date:"{timestamp}"]')
        if timestamp
        else None,
        "retrieved_utc": datetime.now(UTC).isoformat(),
        "osm_timestamp_utc": timestamp,
        "sha256": digest,
        "bytes": len(data),
        "elements": len(payload["elements"]),
        "attribution": district["attribution"],
        "license": district["license"],
        "license_url": district["license_url"],
        "terms": "Retain attribution and the ODbL notice with the extract and derived databases.",
    }
    output.mkdir(parents=True, exist_ok=True)
    with raw_path.open("xb") as stream:
        stream.write(data)
    with manifest_path.open("x", encoding="utf-8") as stream:
        json.dump(manifest, stream, indent=2)
    print(json.dumps({"path": str(raw_path), "sha256": digest, "elements": manifest["elements"]}))


if __name__ == "__main__":
    main()
