#!/usr/bin/env bash
# Run the separate, unmodified upstream Comma experiment in WSL.
set -euo pipefail
component="${1:-}"
profile="${2:-onnx}"
case "$component" in
  manager|bridge) ;;
  *)
    echo "Usage: $0 manager|bridge [onnx|latest]" >&2
    exit 2
    ;;
esac
case "$profile" in
  onnx)
    default_root="$HOME/VisionPlayground-experiments/openpilot-onnx-v0.9.7"
    native_module=common/params_pyx.so
    export OPENPILOT_PREFIX=codriver_onnx
    export ONNXCPU=1
    export PYOPENCL_CTX="${PYOPENCL_CTX:-0}"
    export POCL_MAX_PTHREAD_COUNT="${POCL_MAX_PTHREAD_COUNT:-4}"
    export QT_QPA_PLATFORM="${QT_QPA_PLATFORM:-xcb}"
    export BLOCK="${BLOCK:+$BLOCK,}mapsd,navd"
    mkdir -p /dev/shm/codriver_onnx
    ;;
  latest)
    default_root="$HOME/VisionPlayground-experiments/openpilot"
    native_module=openpilot/common/libparams_c.so
    export OPENPILOT_PREFIX=codriver_sim
    export LD_LIBRARY_PATH="/usr/lib/llvm-20/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
    export LLVM_PATH="${LLVM_PATH:-/usr/lib/llvm-20/lib/libLLVM.so}"
    export BIG="${BIG:-1}"
    mkdir -p /dev/shm/msgq_codriver_sim
    ;;
  *)
    echo "Unknown Comma profile: $profile (use onnx or latest)" >&2
    exit 2
    ;;
esac
upstream_root="${COMMA_OPENPILOT_DIR:-$default_root}"
if [[ ! -f "$upstream_root/.venv/bin/activate" || ! -f "$upstream_root/$native_module" ]]; then
  echo "The upstream openpilot checkout must be installed and built first: $upstream_root" >&2
  exit 1
fi
cd "$upstream_root"
source .venv/bin/activate
export PYTHONPATH="$upstream_root${PYTHONPATH:+:$PYTHONPATH}"
export GALLIUM_DRIVER="${GALLIUM_DRIVER:-d3d12}"
export PYTHONUNBUFFERED=1
export SCALE="${SCALE:-0.6}"
log_dir="$HOME/VisionPlayground-experiments/logs/$profile"
mkdir -p "$log_dir"
log_file="$log_dir/$component-$(date +%Y%m%d-%H%M%S)-$$.log"
exec > >(tee "$log_file") 2>&1
echo "Comma $profile $component log: $log_file"
case "$component" in
  manager)
    echo 'Waiting for the Comma-controls camera bridge...'
    python - <<'PY'
import time
from msgq.visionipc import VisionIpcClient
deadline = time.monotonic() + 90
while not VisionIpcClient.available_streams('camerad', block=False):
    if time.monotonic() >= deadline:
        raise SystemExit('Camera bridge did not start. Check the Comma-controls terminal.')
    time.sleep(1)
PY
    exec ./openpilot/tools/sim/launch_openpilot.sh
    ;;
  bridge)
    echo 'Comma simulator controls: 2 engage, 1 increase speed, 3 cancel, WASD manual, q quit bridge.'
    exec python -m openpilot.tools.sim.run_bridge
    ;;
  *)
    echo "Usage: $0 manager|bridge [onnx|latest]" >&2
    exit 2
    ;;
esac
