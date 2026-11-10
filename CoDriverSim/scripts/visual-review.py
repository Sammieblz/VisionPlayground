"""Build a local comparison page from retained, unmodified Unity captures."""

import hashlib
import html
import json
from pathlib import Path


def main():
    root = Path(__file__).resolve().parents[1]
    folder = root / "outputs/qualification/realism"
    panels = [
        ("Cockpit", "cockpit-720.png", "cockpit-720.png"),
        ("Vehicle exterior", "exterior-720.png", "exterior-720.png"),
        ("Ordinary traffic model", "fleet-720.png", "fleet-720.png"),
        ("Bridge extension", "bridge-720.png", "bridge-720.png"),
        ("Storefront", "storefront-720.png", "storefront-720.png"),
        ("Modeled low-rise", "lowrise-720.png", "lowrise-720.png"),
        ("Railway", "railway-720.png", "railway-720.png"),
        ("Clothed civilian A", "civilian-720.png", "citizen_a-720.png"),
        ("Clothed civilian B", "civilian-720.png", "citizen_b-720.png"),
        ("Cat", "cat-720.png", "cat-720.png"),
    ]
    records, sections = [], []
    for title, before, after in panels:
        images = []
        for label, relative in (
            ("Earlier capture", Path("visual-before-material-pass") / before),
            ("Current capture", Path("visual") / after),
        ):
            path = folder / relative
            if not path.is_file():
                continue
            records.append(
                {
                    "file": relative.as_posix(),
                    "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                    "modified_unix": path.stat().st_mtime,
                }
            )
            uri = html.escape(relative.as_posix(), quote=True)
            images.append(
                f'<figure><a href="{uri}"><img loading="lazy" src="{uri}" '
                f'alt="{html.escape(title)} — {label}"></a><figcaption>{label}'
                "</figcaption></figure>"
            )
        sections.append(
            f'<section><h2>{html.escape(title)}</h2><div class="pair">'
            + "".join(images)
            + "</div></section>"
        )
    page = """<!doctype html><html lang="en"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>CoDriverSim visual review</title><style>
body{background:#141719;color:#edf1f4;font:16px/1.5 system-ui;
margin:0 auto;padding:32px;max-width:1500px}
h1{font-size:32px}h2{font-size:23px}p{max-width:900px;color:#bfc9ce}
.pair{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}
figure{margin:0;background:#20272b;border-radius:10px;overflow:hidden}img{width:100%;display:block}
figcaption{padding:10px 16px}section{margin:36px 0}a{color:#80d6e7}
@media(max-width:850px){.pair{grid-template-columns:1fr}body{padding:16px}}
</style><h1>CoDriverSim visual review</h1>
<p>Unmodified Unity presentation captures. Click an image to inspect it at its
original resolution. Cameras and character poses can differ between revisions;
these are visual references, not controlled accuracy comparisons.</p>
<p>Development candidate. These images do not establish driving, controller,
perception or sustained performance acceptance. The local cockpit uses the supplied
Mercedes asset; the public package uses its separate eligible cabin.</p>"""
    (folder / "VISUAL_REVIEW.html").write_text(
        page + "".join(sections) + "</html>", encoding="utf-8"
    )
    (folder / "visual-review-manifest.json").write_text(
        json.dumps(records, indent=2), encoding="utf-8"
    )
    print(folder / "VISUAL_REVIEW.html")


if __name__ == "__main__":
    main()
