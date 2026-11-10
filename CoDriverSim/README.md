# CoDriverSim

An AI co-driver experiment with a Windows Unity driving application and retained MetaDrive demos.
Status: **development build; first-release qualification is incomplete**.

The Unity application uses an independent forward camera, NanoDet ONNX detection, conservative
image-based sign/light reading, tracking, and read-only openpilot model outputs. The portable
dashcam observes, warns and records. A separate, simulation-only adapter can give stock Comma
control of the virtual vehicle after an explicit request. Human driving is the default.

The original `drive` MetaDrive demo and v1 `demo`/`run` commands still use **simulator truth or
imported observations**. They do not run camera perception. See [the Unity development record](docs/UNITY_GAME.md)
for measured checks, current limitations, architecture and the release checklist.

## Unity Windows application

Use the existing Unity **6000.3.10f1** installation, Python **3.11**, and the existing WSL Ubuntu
22.04 openpilot v0.9.7 environment. Packages and external model/texture hashes are pinned.
From the repository root:

```powershell
# Install the observer dependencies and licensed assets, then build:
.\CoDriverSim\scripts\setup-game.ps1

# Rebuild after C# changes, or launch the existing executable:
.\CoDriverSim\scripts\build-game.ps1
.\CoDriverSim\.venv\Scripts\python.exe -m codriversim game
```

The local executable is `CoDriverSim/outputs/game/CoDriverSim.exe`. This development build uses the
checkout and separately installed WSL runtime. `scripts/package-game.py` prepares a development
folder with its own Python interpreter, observer, models and notices. Installed-folder launch,
Comma engagement, service restart and clean exit have been exercised; full release qualification
remains incomplete. WSL Comma is a separate installation, configurable in `codriversim-app.json`.
Play starts human driving. Xbox triggers accelerate/brake, left stick steers, Y changes view,
X requests/cancels Comma, and Menu pauses. Keyboard fallback is W/A/S/D, C for view, X for Comma,
and Esc for pause. Settings includes remapping, dead zones and service restart controls.

The town now uses licensed modeled sedans, hatchbacks, SUVs, pickups, a coach, an ambulance,
houses/shops, railcars, signal housings, trees and truss bridges. Roads and sidewalks use
Poly Haven surface maps, with an HDR sky and environment reflections. Bus, parking-car and
rail doors remain animated. Cedar Grove uses a synthetic layout; Davis uses the mapped layout
described below. Neither is a claim of photorealism. Asset provenance and map estimates are recorded in
[the development record](docs/UNITY_GAME.md); external downloads remain outside Git.

Downtown Davis, California, is playable and selectable alongside Cedar Grove in Settings.
Its OSM-derived roads, buildings, lane graph, GPS and minimap share one map definition;
Richards Boulevard passes underneath the railway. Building heights, grades and traffic
schedules include documented estimates. It is not a surveyed digital twin.
See [the development and qualification record](docs/UNITY_GAME.md).

Saved event clips and scenario captures are timestamped JPEG sequences with synchronized
observations under `outputs/recordings`; the Recordings page plays them. Scenario scorecards
link their capture IDs. The bounded recorder reports storage failures and preserves saved clips.
CLI recording/video replay uses the same observer:

```powershell
.\CoDriverSim\.venv\Scripts\python.exe -m codriversim replay camera.mp4 --max-frames 100 --sample-hz 5 --output-dir replay-output
.\CoDriverSim\.venv\Scripts\python.exe -m codriversim evaluate-ai labels.json replay-output/result.json --output-dir evaluation-output
```

Uncalibrated footage retains unknown range estimates. Camera replay currently reruns NanoDet
and sign/light processing; it does not rerun the separate WSL openpilot model.

For Comma's own driving stack and interface, see the separate
[upstream Comma simulator experiment](docs/COMMA_SIM.md). Official openpilot v0.9.7 runs with
ONNX CPU inference in WSL on the target machine and passed its 60-second simulator driving test.
Its stock UI displays textured roads and an engaged planned path. The Windows launcher defaults
to this separately installed profile; the newer, slower CPU:LLVM profile is also preserved.

## Retained MetaDrive Windows quick start

Prerequisites: Git and 64-bit Python 3.11. The initial target machine is Windows with a Ryzen 7
2700X, RX 580 8 GB, and 32 GB RAM. Sustained driving qualification remains incomplete.

From the root of your VisionPlayground checkout:

```powershell
Set-Location CoDriverSim
py -3.11 -m venv .venv
.\.venv\Scripts\python.exe -m pip install --upgrade pip
.\.venv\Scripts\python.exe -m pip install -r requirements-lock.txt
.\.venv\Scripts\python.exe -m pip install --no-deps --no-build-isolation -e .
.\.venv\Scripts\python.exe -m codriversim drive
```

Alternatively, inspect and run `scripts/setup.ps1` to perform the environment setup and core demo.
The explicit Python path avoids virtual-environment activation and PowerShell execution-policy changes.
The lock file pins simulator and development dependencies; the `sim` extra is available for
developers who intentionally want to resolve fresh dependencies. Installation may take several
minutes. MetaDrive assets may download on first use.
Do not put a separate folder named `metadrive` in this project's working directory.

Click the driving window. Use **W/A/S/D** to drive, **H** for MetaDrive help,
**R** to reset, and **Esc** to exit. The assistant text is an informational overlay;
it never applies steering, braking, or acceleration. A headless test uses a separate
MetaDrive driving policy strictly as a test-harness driver.

```powershell
# The default city scene has connected intersections and decorative buildings:
.\.venv\Scripts\python.exe -m codriversim drive --scene city

# The original procedural road course, without buildings:
.\.venv\Scripts\python.exe -m codriversim drive --scene road

# Fewer vehicles if performance is poor:
.\.venv\Scripts\python.exe -m codriversim drive --traffic-density 0.05

# A different road/traffic seed:
.\.venv\Scripts\python.exe -m codriversim drive --seed 42

# Bounded run and JSON result, including up to the last 1,000 warning transitions:
.\.venv\Scripts\python.exe -m codriversim drive --steps 500 --output-dir outputs\drive

# Physics-only test; this does not benchmark rendering or camera inference:
.\.venv\Scripts\python.exe -m codriversim drive --headless --steps 100 --output-dir outputs\smoke
```

For bounded runs, reports are exported on normal completion. Esc/interrupt is an immediate exit,
not a guaranteed export. No video recording, speech, animated character, dedicated hazard director,
GPS service, driver monitoring, or neural-network inference is implemented yet.

## Scene and graphics

The default city scene uses MetaDrive's `XTX` road sequence: two four-way intersections and
one T-junction. Seeded, low-detail buildings sit outside the road and sidewalk clearance area.
They are decorative scenery without collision bodies; this is a small procedural neighborhood,
not a complete city traffic model. Resetting also rebuilds the scenery for that scenario seed.
The warning adapter still ignores occlusion, including buildings.

On the tested RX 580 driver, MetaDrive's native terrain shader fails during linking, which
previously left the ground gray and hid road markings. CoDriverSim detects that failure and uses
a simpler RGB terrain shader with asphalt, lane markings, crosswalks, and grass. This fallback
omits terrain lighting/shadows. A visual terrain mask is capped at 8,192 pixels per side or the
GPU limit, avoiding the pinned simulator's oversized default mask. Reports identify the scene,
renderer, mask resolution, and decorative building count. Headless runs omit visual scenery.

## Warning logic without graphics

The core package has no runtime dependencies. Install with `python -m pip install -e .`
if you only want to exercise supplied observations.

```powershell
.\.venv\Scripts\python.exe -m codriversim --help
.\.venv\Scripts\python.exe -m codriversim --version
.\.venv\Scripts\python.exe -m codriversim demo --output-dir outputs\demo
.\.venv\Scripts\python.exe -m codriversim run src\codriversim\examples\demo.json
```

`demo` runs four intentionally synthetic observations. `run` accepts a manifest:

```json
{
  "schema_version": 1,
  "synthetic": true,
  "frames": [
    {"timestamp": 0, "speed_mps": 20, "gap_m": 12, "closing_mps": 8}
  ]
}
```

Time is finite, nonnegative seconds and must strictly increase within a manifest.
Speed is nonnegative metres per second. Gap is nonnegative bumper-to-bumper metres.
Positive closing speed means the gap shrinks; negative means it grows.
`gap_m` and `closing_mps` must either both be present as numbers or both be absent/null.
Missing lead observations produce `unknown`, not a claim that driving is safe.

Illustrative rules: time to collision below 2 seconds produces `urgent`; otherwise headway
below 1.5 seconds at speeds of at least 2 m/s produces `caution`. These thresholds are not calibrated
for a real vehicle. The adapter covers only the current lane segment, ignores occlusion, and does
not predict cut-ins. It must not be described as vision-based collision avoidance.

## Architecture and next steps

- `domain.py`: typed `WorldState`, validation, pure warning decisions.
- `adapters.py`: observation-source protocol and MetaDrive truth adapter.
- `simulation.py`: driving world, read-only co-driver overlay, bounded-run reports.
- `rendering.py`: pinned MetaDrive graphics compatibility and scene reset/close lifecycle.
- `scenery.py`: seeded building placement and batched decorative geometry.
- `cli.py`: input/output and lazy simulator startup.

MetaDrive is an external dependency pinned to a source commit in `pyproject.toml`; downloaded
assets remain external. The small adapted terrain vertex shader and its license are included
with provenance in the third-party notices. The upstream Comma experiment runs separately in WSL;
see its setup and limitations above. Selected Comma integration belongs behind an observation adapter; any vendored
material should be isolated under `third_party/comma` with provenance and licenses.

See [plan](docs/PLAN.md), [evaluation](docs/EVALUATION.md), and
[third-party notices](THIRD_PARTY_NOTICES.md).
