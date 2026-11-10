"""Create a local metre-coordinate map survey and preview from a verified OSM cache.

Matplotlib is optional and needed only for --preview. Neither output is a playable
map: lane widths, traffic rules, heights, signal phases and scenarios need adaptation.
"""

import argparse
import hashlib
import json
from pathlib import Path

from codriversim.maps import prepare


def draw(layout, path):
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.lines import Line2D
    from matplotlib.patches import Patch

    figure, axis = plt.subplots(figsize=(11, 11))
    figure.subplots_adjust(left=0.09, right=0.98, bottom=0.10, top=0.91)
    figure.patch.set_facecolor("#f5f3ef")
    axis.set_facecolor("#f5f3ef")
    colours = {"building": "#b7b0a4", "park": "#d6e3cc", "parking": "#e4ded4", "landuse": "#eeece7"}
    lines = {"road": ("#617381", 2.8), "path": ("#b7aea2", 0.65), "rail": ("#b65939", 2.2)}
    symbols = {
        "signal": ("#d18a20", "o"),
        "crossing": ("#748e67", "+"),
        "stop_sign": ("#a43d3d", "s"),
        "transit_stop": ("#247d9c", "^"),
        "rail_point": ("#b65939", "D"),
    }
    layers = {"landuse": 0, "park": 1, "parking": 2, "building": 3}
    for feature in sorted(layout["features"], key=lambda f: layers.get(f["kind"], 4)):
        kind = feature["kind"]
        for points, role in zip(feature["paths_xz_m"], feature["path_roles"], strict=True):
            x, z = zip(*points, strict=True)
            if kind in colours and len(points) > 2:
                if points[0] == points[-1]:
                    axis.fill(
                        x,
                        z,
                        color=axis.get_facecolor() if role == "inner" else colours[kind],
                        linewidth=0.35,
                        edgecolor="#f5f3ef",
                    )
                else:
                    axis.plot(x, z, color=colours[kind], linewidth=0.7)
            elif kind in lines:
                colour, width = lines[kind]
                axis.plot(x, z, color=colour, linewidth=width, solid_capstyle="round")
            elif kind in symbols:
                colour, marker = symbols[kind]
                axis.scatter(x, z, c=colour, marker=marker, s=16, linewidths=0.6, zorder=5)
    lower, upper = layout["bounds_xz_m"]
    labels = set()
    for feature in layout["features"]:
        name = feature["name"]
        if feature["kind"] not in {"road", "park", "rail_point"} or not name or name in labels:
            continue
        points = feature["paths_xz_m"][0]
        x, z = points[len(points) // 2]
        if lower[0] + 30 < x < upper[0] - 30 and lower[1] + 30 < z < upper[1] - 30:
            axis.text(
                x + 5,
                z + 7,
                name,
                fontsize=7,
                color="#20323e",
                bbox={"facecolor": "#f5f3ef", "alpha": 0.8, "pad": 1, "edgecolor": "none"},
                zorder=7,
            )
            labels.add(name)
    axis.set_xlim(lower[0], upper[0])
    axis.set_ylim(lower[1], upper[1])
    axis.set_aspect("equal")
    axis.set_xlabel("Metres east of map origin")
    axis.set_ylabel("Metres north of map origin")
    axis.set_title(
        layout["name"].upper()
        + f"\nReal-map candidate · {layout['report']['area_km2']:.2f} square kilometres",
        loc="left",
        fontsize=15,
        pad=15,
    )
    handles = [Patch(facecolor=colours["building"], label="Building outlines")]
    handles += [
        Line2D([0], [0], color=colour, linewidth=width, label=label)
        for (colour, width), label in zip(
            lines.values(), ["Road centrelines", "Paths", "Rail"], strict=True
        )
    ]
    handles += [
        Line2D([0], [0], color=colour, marker=marker, linestyle="none", label=label, markersize=5)
        for (colour, marker), label in zip(
            symbols.values(),
            ["Signal node", "Crossing node", "Stop-sign node", "Transit-stop node", "Rail point"],
            strict=True,
        )
    ]
    axis.legend(handles=handles, loc="upper right", fontsize=7, framealpha=0.95)
    figure.text(
        0.02,
        0.012,
        layout["attribution"]
        + " · ODbL · openstreetmap.org/copyright\n"
        + "Source survey only. Symbols are schematic; "
        + "elevations and lane geometry are not validated.",
        fontsize=8,
        color="#46515a",
    )
    figure.savefig(path, dpi=150, bbox_inches="tight", pad_inches=0.3)
    plt.close(figure)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source_dir", type=Path)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--preview", action="store_true")
    args = parser.parse_args()
    raw = (args.source_dir / "osm.json").read_bytes()
    manifest = json.loads((args.source_dir / "source.json").read_text(encoding="utf-8"))
    digest = hashlib.sha256(raw).hexdigest()
    if digest != manifest["sha256"]:
        raise ValueError("OSM cache hash does not match its source manifest")
    layout = prepare(json.loads(raw), manifest["district"], digest)
    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=False)
    (output / "layout.json").write_text(
        json.dumps(layout, indent=2, ensure_ascii=False), encoding="utf-8"
    )
    (output / "source.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False), encoding="utf-8"
    )
    if args.preview:
        draw(layout, output / "district-preview.png")
    print(json.dumps(layout["report"], indent=2))


if __name__ == "__main__":
    main()
