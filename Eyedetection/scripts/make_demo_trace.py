"""Generate a documented synthetic trace, not evidence of real-camera accuracy."""

import json
from pathlib import Path


def main() -> None:
    target = Path(__file__).resolve().parents[1] / "examples" / "demo-trace.jsonl"
    target.parent.mkdir(parents=True, exist_ok=True)
    rows = []
    for index in range(161):
        timestamp = index / 10
        label = "closed" if 5 <= timestamp < 8.5 or 3.5 <= timestamp < 3.7 else "open"
        faces = 0 if 12.5 <= timestamp < 14 else 1
        ear = 0.10 if label == "closed" else 0.31
        rows.append(
            json.dumps(
                {
                    "timestamp": timestamp,
                    "left_ear": ear if faces else None,
                    "right_ear": ear if faces else None,
                    "faces": faces,
                    "label": label if faces else "unknown",
                }
            )
        )
    target.write_text("\n".join(rows) + "\n", encoding="utf-8")
    print(target)


if __name__ == "__main__":
    main()
