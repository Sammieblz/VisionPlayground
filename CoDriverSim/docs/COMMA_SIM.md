# Separate upstream Comma simulator experiment

This runs Comma's own openpilot stack, camera bridge, and MetaDrive fork. It does not replace
CoDriverSim's observer-only warning engine or grant that product vehicle-control authority.
Openpilot's controls here apply to the virtual car in the separate experiment.

## ONNX CPU profile: official v0.9.7

The default launcher now selects the separate checkout at
`~/VisionPlayground-experiments/openpilot-onnx-v0.9.7`, tag `v0.9.7`, commit
`50dd024081496cc5f036a99c8f62d2887112dbb7`. Its tracked source is unmodified.
It uses the tag's Python 3.11.4, exact submodules, LFS assets, and exported Poetry lock,
including ONNX Runtime 1.18.0, NumPy 1.26.4, and MetaDrive commit
`233a3a1698be7038ec3dd050ca10b547b4b3324c`. The locked `onnxruntime-gpu` wheel also includes
`CPUExecutionProvider`; `ONNXCPU=1` selects that provider without requiring CUDA.

The native `scons --minimal -j4` build passed on Ubuntu 22.04. In a standalone benchmark using
the upstream ONNX session configuration and model, three warmups followed by ten timed zero-input
inferences took 28.1–30.7 ms each (median 29.3 ms). This measures the model alone, rather than the
complete simulator or engagement.

On November 8, 2026, on the target Ryzen 7 2700X/RX 580 computer:

- The original upstream integration test passed: `1 passed, 1 skipped in 83.30s`.
  The skipped item is the abstract test base; the MetaDrive driving test ran. It required
  100 active-controls observations and completed its 60-second drive without lane departure,
  a stopped-car failure, or another simulator termination failure. No test assertions were changed.
- With the stock Qt UI visible, a subsequent 30-second message capture recorded controls active
  in all 3,000 observed controls updates, speed 10.30–11.02 m/s (about 23–25 mph), no blocking
  events, and no missing required processes. The UI showed road textures, lane markings, the
  green planned path, and the engaged steering icon.
- During that capture, `modelV2.modelExecutionTime` had a median of 52.1 ms and a maximum of
  127.0 ms. There were 508 model updates (about 16.9 Hz); median reported frame drop was 7.1%.
  Engagement works, but this is not a steady 20 Hz performance guarantee or a long-duration
  reliability result. The standalone 29.3 ms result should not be presented as full-stack speed.

The Windows launcher was updated to select this profile by default and export the checkout's
`PYTHONPATH`, which its separately installed environment requires. Evidence is saved locally at
`CoDriverSim/outputs/comma-onnx-check/engaged-ui.png` and `interactive-status.json`.
A clean relaunch through Windows Terminal also engaged automatically after warmup. Its
45-second startup capture included temporary calibration/radar/localization/communication
warnings before engagement; later five-second samples showed active controls and no blocking
events. That capture is saved separately as `relaunch-status.json`. Launcher syntax checks,
dependency compatibility, and CoDriverSim's 21 tests plus Ruff checks passed.

This profile uses PoCL for CPU OpenCL image preprocessing, limited to four worker threads with
`POCL_MAX_PTHREAD_COUNT=4`, and `PYOPENCL_CTX=0` selects its device without an interactive prompt.
`BLOCK=mapsd,navd` omits online map/navigation services, which require a Mapbox token or
registered-device credentials. The first test run found that `navd` otherwise exits while
looking for a device private key. These services are not needed for the simulator loop.

## Newer CPU:LLVM profile

Tested November 8, 2026, using the existing Ubuntu-22.04 WSL 2 distribution on the Ryzen 7 2700X,
RX 580, and 32 GB RAM computer:

- Upstream openpilot is installed at `~/VisionPlayground-experiments/openpilot` inside WSL,
  at commit `f5dfddb72afdc5182cb28b63b3c692a9f00535fe`. Its tracked source is unmodified.
- Its pinned submodules, frozen Python dependencies, and upstream LFS model/assets were installed.
  The environment uses managed Python 3.12.13 and LLVM 20. The native `scons --minimal -j4`
  build completed, including driving-model compilation.
- The simulator dependency is Comma's `metadrive` minimal fork at commit
  `2716f55a9c7b928ce957a497a15c2c19840c08bc` (MetaDrive 0.4.2.3, Panda3D 1.10.14),
  installed separately because the current openpilot tools extra comments it out.
- WSL graphics report `D3D12 (Radeon RX 580 Series)`, Mesa 23.2.1, accelerated rendering,
  and OpenGL 4.2. Native road textures render without CoDriverSim's compatibility shader.
- The upstream camera bridge and large openpilot UI were launched. Camera images, speed, and
  the model path are visible. Audio startup was repaired by installing PortAudio/ALSA plugins.
- This revision's standard Linux-PC model backend is `CPU:LLVM`; it does not automatically use
  this RX 580 for inference. Observed model execution was about 0.50–0.54 seconds per update,
  with substantial frame drops and communication/localization errors. Autonomous engagement
  **has not been achieved**. This is not a verified real-time driving setup.
- Single-camera mode is the default tested launcher. A dual-camera trial also produced timestamp
  synchronization warnings. Upstream currently skips its simulator driving test.

The stock bridge creates a simple loop with traffic density zero. It does not include a detailed
city, intersections populated with traffic, or a hazard suite. Its `--high_quality` option should
not be interpreted as a city-asset pack or a measured performance improvement.

## Launch the installed experiment

From the Windows repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\CoDriverSim\scripts\launch-comma.ps1
```

The default profile is `onnx` (v0.9.7). Add `-Profile latest` to run the previously installed
CPU:LLVM revision. Run one profile at a time.

The launcher opens Windows Terminal tabs for `Comma-controls` and `Comma-manager`. The manager
waits for the camera bridge. Focus **Comma-controls** for keyboard input; the road-view window
does not receive the bridge's terminal keyboard commands.

Keep both terminal tabs open and allow up to a minute for startup. Comma's default bridge
renders offscreen and sends the simulated road camera into the `ui` window. The openpilot
home screen with **NO PANDA** is the off-road screen, rather than the driving view. On a
subsequent reported missing-view check, neither the manager nor bridge was running; relaunching
both restored the road view. The launcher now saves separate timestamped logs under
`~/VisionPlayground-experiments/logs/onnx/` (or `logs/latest/`) for diagnosing later shutdowns.
An orange **Turn Exceeds Steering Limit** warning was also observed in a bend during that run;
the earlier short driving-test pass does not establish reliable autonomous driving in every turn.

Upstream controls: **2** requests engagement; **1** increases cruise speed; **3** cancels;
**W/A/S/D** supply manual input; **R** resets; **Q** exits the bridge. Engagement can be rejected
while health errors remain. The upstream v0.9.7 bridge also automatically requests its first
engagement when the stack becomes engageable. Stop both terminal tabs with **Ctrl+C** when finished.

Equivalent WSL commands, each in its own terminal, use the repository's `scripts/comma-sim.sh`
with `bridge` or `manager`, followed optionally by `onnx` or `latest`. The wrapper uses isolated
prefixes (`codriver_onnx` or `codriver_sim`), selects WSL's D3D12 renderer, and sets `SCALE=0.6`.
Set `COMMA_OPENPILOT_DIR` if the upstream checkout lives elsewhere. The PowerShell launcher
accepts `-Distribution` for a different installed WSL distribution.

## v0.9.7 setup record

The older release was installed independently, keeping the newer checkout and CoDriverSim's
Windows environment intact. Its upstream `tools/install_ubuntu_dependencies.sh` was run with
`INSTALL_EXTRA_PACKAGES=no`; `pocl-opencl-icd` and `clinfo` were also installed. The bundled
MapLibre library requires `libicuuc.so.66`, which is absent from Ubuntu 22.04's default runtime.
Ubuntu's official archive package
[`libicu66_66.1-2ubuntu2.1_amd64.deb`](https://archive.ubuntu.com/ubuntu/pool/main/i/icu/libicu66_66.1-2ubuntu2.1_amd64.deb)
was installed alongside ICU 70; no existing ICU package was replaced.

With the tag and submodules checked out, the main environment commands were:

```bash
git lfs install --local --skip-smudge
git lfs pull --include=poetry.lock
git lfs pull --exclude=poetry.lock
~/.local/bin/uv python install 3.11.4
~/.local/bin/uv venv --python 3.11.4
~/.local/bin/uvx --python 3.11.4 --from poetry==1.7.0 poetry export \
  --format requirements.txt --without-hashes --with dev \
  --output ../onnx-requirements-locked.txt
~/.local/bin/uv pip install --python .venv/bin/python -r ../onnx-requirements-locked.txt
source .venv/bin/activate
PYTHONPATH=$PWD ONNXCPU=1 scons --minimal -j4
```

The focused upstream integration check is:

```bash
PYTHONPATH=$PWD ONNXCPU=1 CI=1 BLOCK=mapsd,navd GALLIUM_DRIVER=d3d12 \
  PYOPENCL_CTX=0 POCL_MAX_PTHREAD_COUNT=4 \
  python -m pytest tools/sim/tests/test_metadrive_bridge.py -q -n0 -s
```

`CI=1` suppresses the UI for this test. Its original assertions require 100 active-controls
observations and reject lane departure, failure to move, and simulator termination failures.
No engagement health checks are disabled. Setup/build/test logs and the benchmark JSON are
stored in `~/VisionPlayground-experiments/`.

References: [v0.9.7 simulator instructions](https://github.com/commaai/openpilot/blob/v0.9.7/tools/sim/README.md),
[ONNX runtime selection](https://github.com/commaai/openpilot/blob/v0.9.7/selfdrive/modeld/runners/onnxmodel.py),
and [driving test](https://github.com/commaai/openpilot/blob/v0.9.7/tools/sim/tests/test_sim_bridge.py).

## Newer revision setup record

System dependencies installed in WSL included `build-essential`, `libcurl4-openssl-dev`,
`libegl1-mesa-dev`, `libgles2-mesa-dev`, `mesa-utils`, `pkg-config`, `locales`, `libportaudio2`,
and `libasound2-plugins`. LLVM 20 was installed from the official [LLVM APT repository](https://apt.llvm.org/).
The distribution's LLVM 14 failed the tinygrad runtime preflight. No WSL distribution upgrade
or graphics-driver change was made.

After cloning and checking out the recorded openpilot revision, the main setup commands were:

```bash
GIT_LFS_SKIP_SMUDGE=1 git submodule update --init --recursive --jobs 4
~/.local/bin/uv sync --frozen --all-extras
~/.local/bin/uv pip install --python .venv/bin/python \
  'git+https://github.com/commaai/metadrive.git@2716f55a9c7b928ce957a497a15c2c19840c08bc'
source .venv/bin/activate
./lfs.py install
./tools/lfs.sh pull
export LD_LIBRARY_PATH=/usr/lib/llvm-20/lib
export LLVM_PATH=/usr/lib/llvm-20/lib/libLLVM.so
scons --minimal -j4
```

Do not run `uv sync` after the separate simulator installation without reinstalling that simulator:
it is not in the current frozen openpilot dependency set. CoDriverSim's Windows `.venv` and lock
file are independent and were not changed by this setup.

Reference: [upstream simulator instructions](https://github.com/commaai/openpilot/blob/f5dfddb72afdc5182cb28b63b3c692a9f00535fe/openpilot/tools/sim/README.md),
[bridge configuration](https://github.com/commaai/openpilot/blob/f5dfddb72afdc5182cb28b63b3c692a9f00535fe/openpilot/tools/sim/bridge/metadrive/metadrive_bridge.py),
and [PC model backend](https://github.com/commaai/openpilot/blob/f5dfddb72afdc5182cb28b63b3c692a9f00535fe/openpilot/selfdrive/modeld/helpers.py).
