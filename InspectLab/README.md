# InspectLab

**Status: runnable foundation · synthetic demonstration · version 0.1.0.dev0**

Turn supplied anomaly-score maps into review decisions and localized regions. Part of [VisionPlayground](../README.md).

Intended users: Developers exploring product inspection and visual quality control.

This package implements deterministic post-processing of supplied observations. It includes no pretrained model, camera access, GPU requirement, or automatic model download. Model integration and an application UI are future milestones.

## Run locally

Requires Python 3.12 or 3.13. From this project folder in PowerShell:

```powershell
py -3.13 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-lock.txt
.\.venv\Scripts\python.exe -m pip install --no-build-isolation --no-deps -e .
.\.venv\Scripts\inspectlab.exe demo --output-dir outputs/demo
```

On Linux, use `python3.12 -m venv .venv` or Python 3.13 and replace `.venv\Scripts\python.exe` with `.venv/bin/python`; the installed command is `.venv/bin/inspectlab`. Each project owns its environment; EyePause's environment is separate.

## Python API

Import `inspect_map` from `inspectlab`. Typed input/output records live in `inspectlab.domain`; future model boundaries are protocols in `inspectlab.adapters`.

**Input:** A cases array of case_id, nonempty rectangular finite scores, and a finite threshold. Raw scores may be outside 0..1; they are not probabilities.

**Output:** Per-case pass/review decision, anomalous pixel count, and four-connected regions with exclusive xyxy boxes, area, maximum, and mean score.

**Current boundary:** Threshold comparison is inclusive (score >= threshold). Four-connectivity keeps diagonal-only neighbors separate. Region coordinates refer to score-map pixels, which a future adapter must map to source-image pixels.

See the [complete input example](src/inspectlab/examples/demo.json) and [API tests](tests/test_api.py) for direct Python examples and independently specified expected values.

## CLI and manifests

```powershell
inspectlab --help
inspectlab --version
inspectlab demo
inspectlab demo --output-dir outputs/demo
inspectlab run src/inspectlab/examples/demo.json --output-dir outputs/imported
```

The displayed commands assume the project environment is activated; otherwise use its full command path above. `python -m inspectlab` exposes the same CLI.

Every input manifest is a JSON object with `schema_version: 1`, optional boolean `synthetic`, and the project fields described above. Bundled fixtures have `synthetic: true`. Paths inside a manifest resolve relative to that manifest, not the working directory.

Reports identify `mode` (`synthetic_demo` or `imported_observations`), preserve the synthetic-input flag, and state `model_inference: false`. Results print as JSON. `--output-dir` additionally writes `result.json` and any visual artifacts; omitting it creates no output files. Existing generated artifacts in the chosen output directory can be replaced, but input manifests and source images are protected against output-path collisions. Invalid inputs produce a clear stderr message and nonzero exit status.

**Expected demo result:** The normal demo map passes. The defective map requires review with three anomalous pixels grouped into two regions.

Pixel boxes use `[left, top, right, bottom]` with exclusive right/bottom boundaries. Timestamps are nonnegative seconds. Coordinate spaces, image dimensions, and model versions must stay explicit in future adapters.

## Development

```powershell
.\.venv\Scripts\python.exe -m pytest -q
.\.venv\Scripts\python.exe -m ruff check src tests
.\.venv\Scripts\python.exe -m ruff format --check src tests
```

The repository verifier also builds a wheel, installs it in a fresh isolated environment, and exercises the CLI outside this project's source directory. Runtime dependencies: Python standard library only. Locks include development tools as well as runtime dependencies.

[Implementation plan](docs/PLAN.md) · [Evaluation guide](docs/EVALUATION.md) · [Repository roadmap](../ROADMAP.md)
