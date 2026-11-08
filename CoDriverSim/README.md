# CoDriverSim

An observer-only co-driver experiment inside a 3D driving world. Status: **runnable foundation**.

This first version uses **exact simulator positions**, not camera perception or Comma models.
It provides manual driving in MetaDrive, seeded traffic, a live following-warning text overlay,
and a separate simulator-independent warning engine. It sends no driving commands from the assistant.

## Windows quick start

Prerequisites: Git and 64-bit Python 3.11. The initial target machine is Windows with a Ryzen 7
2700X, RX 580 8 GB, and 32 GB RAM. No frame-rate claim has been established for that machine.

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
- `cli.py`: input/output and lazy simulator startup.

MetaDrive is an external dependency pinned to a source commit in `pyproject.toml`; its source and
downloaded assets are not copied into VisionPlayground. An experimental Comma fork can be tested
separately later. Selected Comma integration belongs behind an observation adapter; any vendored
material should be isolated under `third_party/comma` with provenance and licenses.

See [plan](docs/PLAN.md), [evaluation](docs/EVALUATION.md), and
[third-party notices](THIRD_PARTY_NOTICES.md).
