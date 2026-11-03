from dataclasses import asdict
from pathlib import Path

from .api import inspect_map
from .validation import mapping, number, sequence, text


def process(data: dict, base: Path) -> tuple[dict, dict[str, bytes], tuple[Path, ...]]:
    results = []
    seen = set()
    for value in sequence(data.get("cases"), "cases"):
        value = mapping(value, "case")
        name = text(value.get("case_id"), "case_id")
        if name in seen:
            raise ValueError("Inspection case IDs must be unique.")
        seen.add(name)
        result = inspect_map(value.get("scores"), number(value.get("threshold"), "threshold"))
        results.append({"case_id": name, **asdict(result)})
    return {"inspections": results}, {}, ()
