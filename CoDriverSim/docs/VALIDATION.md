# Local validation evidence

## Linux foundation

Initial implementation: November 7, 2026. These results describe a Linux development environment,
not the user's Windows/RX 580 machine.

- Python 3.11.17 and 3.12.14: 18 unit/CLI tests passed on each; Ruff lint and format checks passed.
- Pinned MetaDrive 0.4.3: a 500-step physics-only run with seed 21 and traffic density 0.1
  completed (50 simulated seconds). It observed lead vehicles through the truth adapter and
  exported the report. No camera inference or graphical frame-rate result is implied.
- The packaged synthetic demo independently exercises quiet, caution, urgent, and quiet states.
- A wheel installed without dependencies in a fresh Python 3.11 environment ran its packaged demo
  outside the source checkout. The core does not import MetaDrive.
- The universal dependency lock resolved and installed in Python 3.11 on Linux.
- The repository's six existing scaffold checks passed independently.

## Windows local run

November 7, 2026, on the user's Ryzen 7 2700X, Radeon RX 580 8 GB, and 32 GB RAM machine:

- The existing checkout was clean and matched `origin/main` at `8e2a0d5`.
- Used 64-bit Python 3.11.9 in `CoDriverSim/.venv`, independently of the repository's Python 3.13
  environment. Installed `requirements-lock.txt` and the editable package without changing pins.
  Installed MetaDrive provenance matches commit `fe54b03327b14bcfc17a9a04b60c8abbee4d7ab1`.
- All 18 tests, Ruff lint, Ruff formatting, and `pip check` passed.
- CLI help, version, and the packaged synthetic warning demo completed successfully.
- A 100-step physics-only run completed with seed 21 and traffic density 0.1, exporting
  `outputs/windows-smoke/result.json`: 10 simulated seconds in approximately 2.24 wall seconds.
  This is not a rendering benchmark.
- The normal `drive` command opened an onscreen MetaDrive 0.4.3 window at 960 x 540 with traffic
  and the simulator-truth warning overlay. The user confirmed that W/A/S/D driving worked and
  the overlay was readable. The assistant continues to send no vehicle-control commands.
- A stationary window capture showed 39.7 FPS. This single sample is not a sustained driving
  benchmark. The installed Radeon driver version was `31.0.21923.1000`.
- A built wheel installed without dependencies in a fresh Python 3.11 environment ran its
  packaged demo outside the source checkout, without importing MetaDrive.

Local reports, launch logs, and the window capture are under ignored `outputs/windows-*` folders.
At this stage, sustained driving performance, help/reset/exit controls, controlled hazardous
scenarios, and real-camera/model evaluation remained unverified. No camera-based AI was exercised.

## Windows terrain fix and city scene

November 8, 2026, on the same Windows/RX 580 machine and Python 3.11 environment:

- The user reported that the expected city was missing. The original `map=3` launcher did not
  create buildings. Separately, reproduction confirmed missing road textures/markings in the
  earlier view. Debug logging exposed the terrain GLSL link failure:
  `Error: Fail to build reflection info.` The normal simulator log suppressed that error.
- The new compatibility shader displayed asphalt, grass, markings, and crosswalks on that driver.
  Native terrain shading is retained when it links. The fallback has simplified terrain lighting.
- The visual surface mask is capped at 8,192 pixels per side (or a lower GPU limit). The original
  2 km terrain requested 45,056 pixels per side, above this GPU's reported 16,384 texture limit.
  The city profile uses a 1 km terrain region and low terrain relief.
- The new default `--scene city` uses `XTX` connected intersection blocks and decorative buildings;
  `--scene road` retains the previous procedural course. Onscreen checks generated 48 buildings
  for seed 21 and 40 for seed 42, with colored facades and windows visible in saved captures.
- The user confirmed that the updated city, textured roads, and buildings were visible and
  W/A/S/D manual driving still worked.
- Direct seed resets and the registered `R` shortcut callback each left exactly one building
  mesh, with no accumulating neighborhoods. The compatible terrain renderer survived resets.
  A bird's-eye capture also showed buildings outside the intersection streets.
- All 21 unit/CLI/scenery tests, Ruff lint/format checks, and `pip check` passed. City and original
  road profiles each completed a 100-step headless smoke run. Graphical captures are under
  `outputs/windows-city-check`; final launch logs are under `outputs/windows-city-live`.
- The built wheel contained both shaders and the upstream license. Its installed package ran
  the dependency-free demo outside the source checkout in a fresh environment.

The buildings are visual scenery without collision bodies. This does not add controlled hazards,
traffic-signal logic, model inference, or building occlusion to the truth adapter. Individual
stationary city captures showed 18.6 to 45.3 FPS as the traffic/view changed; these samples are not
a sustained driving benchmark. Manual-driving performance and the fallback on other GPUs remain
unmeasured.

## Separate upstream Comma ONNX experiment

November 8, 2026, on the same machine in Ubuntu 22.04 WSL 2: official openpilot v0.9.7,
unmodified commit `50dd024081496cc5f036a99c8f62d2887112dbb7`, Python 3.11.4, exact upstream
Poetry lock/submodules/assets, ONNX Runtime 1.18.0 with `CPUExecutionProvider`, and PoCL image
preprocessing. The optional online map/navigation services were excluded because device
credentials and a Mapbox token are unavailable. Driving health checks remained enabled.

- The native minimal build passed after installing the matching ICU 66 runtime alongside ICU 70.
- The unmodified upstream MetaDrive driving test passed its 60-second drive, including active
  controls, movement, and lane-keeping assertions (`1 passed, 1 skipped in 83.30s`; the skip is
  the abstract base class).
- A 30-second run with the stock Qt UI visible recorded 3,000 active-controls updates out of
  3,000 observed, speed 10.30–11.02 m/s, no blocking events, and no missing required processes.
  Textured roads, lane markings, green planned path, and engaged state were visible.
- Median model execution was 52.1 ms in that run, with 127.0 ms maximum and about 16.9 model
  updates per second. Median reported frame drop was 7.1%. A model-only zero-input benchmark
  was faster (29.3 ms median); neither sample establishes sustained performance.
- A clean relaunch using the Windows launcher engaged automatically after startup. Temporary
  initialization warnings cleared in the later samples. Dependency checks, Bash/PowerShell
  syntax checks, all 21 CoDriverSim tests, and Ruff lint/format checks passed.

See [commands, exact revisions, setup, and limitations](COMMA_SIM.md). Captures are in
`outputs/comma-onnx-check`. This virtual-car control experiment is separate from CoDriverSim's
observer-only assistant; no model adapter or real-car control was added to that product.
