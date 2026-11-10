#!/usr/bin/env bash
set -euo pipefail
root="${COMMA_OPENPILOT_DIR:-$HOME/VisionPlayground-experiments/openpilot-onnx-v0.9.7}"
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
exec "$root/.venv/bin/python" "$script_dir/supervise-comma.py" --root "$root" "$@"
