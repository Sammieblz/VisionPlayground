# MeasureLab

**Status: runnable foundation · synthetic demonstration · version 0.1.0.dev0**

Measure planar rectangular objects using a known reference in the same plane. Part of [VisionPlayground](../README.md).

Intended users: Developers building measurement, inspection, and positioning tools.

This package implements deterministic post-processing of supplied observations. It includes no pretrained model, camera access, GPU requirement, or automatic model download. Model integration and an application UI are future milestones.

## Run locally

Requires Python 3.12 or 3.13. From this project folder in PowerShell:

```powershell
py -3.13 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-lock.txt
.\.venv\Scripts\python.exe -m pip install --no-build-isolation --no-deps -e .
.\.venv\Scripts\measurelab.exe demo --output-dir outputs/demo
```

On Linux, use `python3.12 -m venv .venv` or Python 3.13 and replace `.venv\Scripts\python.exe` with `.venv/bin/python`; the installed command is `.venv/bin/measurelab`. Each project owns its environment; EyePause's environment is separate.

## Python API

Import `measure_quad` from `measurelab`. Typed input/output records live in `measurelab.domain`; future model boundaries are protocols in `measurelab.adapters`.

**Input:** reference.image_corners in corresponding TL/TR/BR/BL order, positive width_mm/height_mm, and an object_corners quadrilateral in the same order.

**Output:** width_mm, height_mm, rectified_corners, and mm units. Width/height average the respective opposite rectified edge lengths.

**Current boundary:** Uses a normalized four-point planar homography. Rejects degenerate or unstable references and horizon crossings. The caller supplies a rectangular object on the reference plane; this is not 3D reconstruction or a physical accuracy claim.

See the [complete input example](src/measurelab/examples/demo.json) and [API tests](tests/test_api.py) for direct Python examples and independently specified expected values.

## CLI and manifests

```powershell
measurelab --help
measurelab --version
measurelab demo
measurelab demo --output-dir outputs/demo
measurelab run src/measurelab/examples/demo.json --output-dir outputs/imported
```

The displayed commands assume the project environment is activated; otherwise use its full command path above. `python -m measurelab` exposes the same CLI.

Every input manifest is a JSON object with `schema_version: 1`, optional boolean `synthetic`, and the project fields described above. Bundled fixtures have `synthetic: true`. Paths inside a manifest resolve relative to that manifest, not the working directory.

Reports identify `mode` (`synthetic_demo` or `imported_observations`), preserve the synthetic-input flag, and state `model_inference: false`. Results print as JSON. `--output-dir` additionally writes `result.json` and any visual artifacts; omitting it creates no output files. Existing generated artifacts in the chosen output directory can be replaced, but input manifests and source images are protected against output-path collisions. Invalid inputs produce a clear stderr message and nonzero exit status.

**Expected demo result:** The synthetic perspective fixture contains an independently specified 40 mm by 20 mm rectangle and a 100 mm square reference.

Pixel boxes use `[left, top, right, bottom]` with exclusive right/bottom boundaries. Timestamps are nonnegative seconds. Coordinate spaces, image dimensions, and model versions must stay explicit in future adapters.

## Development

```powershell
.\.venv\Scripts\python.exe -m pytest -q
.\.venv\Scripts\python.exe -m ruff check src tests
.\.venv\Scripts\python.exe -m ruff format --check src tests
```

The repository verifier also builds a wheel, installs it in a fresh isolated environment, and exercises the CLI outside this project's source directory. Runtime dependencies: numpy>=2,<3. Locks include development tools as well as runtime dependencies.

[Implementation plan](docs/PLAN.md) · [Evaluation guide](docs/EVALUATION.md) · [Repository roadmap](../ROADMAP.md)
