from dataclasses import asdict
from pathlib import Path

from .api import analyze_tracks
from .domain import DirectedLine, Frame, Track
from .validation import box, mapping, number, point, sequence, text


def process(data: dict, base: Path) -> tuple[dict, dict[str, bytes], tuple[Path, ...]]:
    line = mapping(data.get("line"), "line")
    frames = []
    for value in sequence(data.get("frames"), "frames"):
        frame = mapping(value, "frame")
        tracks = []
        for item in sequence(frame.get("tracks"), "frame.tracks"):
            item = mapping(item, "track")
            tracks.append(
                Track(text(item.get("track_id"), "track_id"), box(item.get("box"), "box"))
            )
        frames.append(Frame(number(frame.get("timestamp"), "timestamp"), tuple(tracks)))
    result = analyze_tracks(
        frames,
        DirectedLine(point(line.get("start"), "line.start"), point(line.get("end"), "line.end")),
    )
    return asdict(result), {}, ()
