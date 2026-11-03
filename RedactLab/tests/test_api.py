import json
from importlib.resources import as_file, files

import pytest
from PIL import Image

from redactlab import create_redactions
from redactlab.cli import main
from redactlab.domain import DetectionRegion


def test_outward_rounding_clipping_and_exclusive_boundaries_preserve_source():
    image = Image.new("RGB", (5, 5), "white")
    original = image.tobytes()
    result = create_redactions(image, [DetectionRegion("a", "face", (-0.2, 0.2, 2.1, 2))])
    assert result.regions[0].mask_box == (0, 0, 3, 2)
    assert result.summary()["redacted_pixels"] == 6
    assert result.mask.mode == "L" and set(result.mask.tobytes()) == {0, 255}
    assert result.redacted.getpixel((2, 1)) == (0, 0, 0)
    assert result.redacted.getpixel((3, 1)) == (255, 255, 255)
    assert image.tobytes() == original


def test_overlap_counts_union_and_selection_filters_categories():
    image = Image.new("RGB", (5, 5), "white")
    regions = [
        DetectionRegion("a", "face", (0, 0, 3, 3)),
        DetectionRegion("b", "face", (1, 1, 4, 4)),
        DetectionRegion("c", "plate", (0, 0, 5, 5)),
    ]
    result = create_redactions(image, regions, ["face"])
    assert result.summary()["redacted_pixels"] == 14
    assert len(result.regions) == 2


def test_empty_offscreen_and_unselected_regions_leave_image_unchanged():
    image = Image.new("RGB", (5, 5), "white")
    result = create_redactions(
        image,
        [DetectionRegion("a", "face", (6, 6, 8, 8)), DetectionRegion("b", "car", (0, 0, 2, 2))],
    )
    assert not result.regions and result.summary()["redacted_pixels"] == 0
    assert result.redacted.tobytes() == image.tobytes()
    assert create_redactions(image, []).summary()["redacted_pixels"] == 0


@pytest.mark.parametrize(
    "regions,categories",
    [
        ([DetectionRegion("a", "face", (0, 0, 0, 1))], None),
        ([DetectionRegion("a", "face", (0, 0, float("inf"), 1))], None),
        ([DetectionRegion("a", "face", (0, 0, 1, 1))] * 2, None),
        ([], ["unknown"]),
        ([], "face"),
    ],
)
def test_invalid_regions_are_rejected(regions, categories):
    with pytest.raises(ValueError):
        create_redactions(Image.new("RGB", (5, 5)), regions, categories)


def test_export_cannot_overwrite_source_image(tmp_path, capsys):
    source = tmp_path / "redacted.png"
    Image.new("RGB", (5, 5), "white").save(source)
    original = source.read_bytes()
    manifest = tmp_path / "input.json"
    manifest.write_text(json.dumps({"schema_version": 1, "image": "redacted.png", "regions": []}))
    with pytest.raises(SystemExit):
        main(["run", str(manifest), "--output-dir", str(tmp_path)])
    assert "overwrite" in capsys.readouterr().err
    assert source.read_bytes() == original
    assert not (tmp_path / "mask.png").exists()


def test_packaged_demo_redacts_expected_area():
    from redactlab.workflow import process

    with as_file(files("redactlab").joinpath("examples")) as directory:
        report, artifacts, _ = process(json.loads((directory / "demo.json").read_text()), directory)
    assert report["redacted_pixels"] == 2565
    assert set(artifacts) == {"mask.png", "redacted.png"}
    assert all(content.startswith(b"\x89PNG\r\n\x1a\n") for content in artifacts.values())
