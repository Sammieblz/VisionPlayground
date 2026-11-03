# RedactLab

**Status: runnable foundation · synthetic demonstration · version 0.1.0.dev0**

Apply reviewed detection regions to image copies and export inspectable redaction masks. Part of [VisionPlayground](../README.md).

Intended users: Developers preparing recordings, documents, and datasets for sharing.

This package implements deterministic post-processing of supplied observations. It includes no pretrained model, camera access, GPU requirement, or automatic model download. Model integration and an application UI are future milestones.

## Run locally

Requires Python 3.12 or 3.13. From this project folder in PowerShell:

```powershell
py -3.13 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-lock.txt
.\.venv\Scripts\python.exe -m pip install --no-build-isolation --no-deps -e .
.\.venv\Scripts\redactlab.exe demo --output-dir outputs/demo
```

On Linux, use `python3.12 -m venv .venv` or Python 3.13 and replace `.venv\Scripts\python.exe` with `.venv/bin/python`; the installed command is `.venv/bin/redactlab`. Each project owns its environment; EyePause's environment is separate.

## Python API

Import `create_redactions` from `redactlab`. Typed input/output records live in `redactlab.domain`; future model boundaries are protocols in `redactlab.adapters`.

**Input:** An image path relative to the manifest, regions with region_id/category/pixel xyxy box, and optional categories. Supported selected categories: face, plate, sensitive_text.

**Output:** Image dimensions, selected clipped regions, union redacted-pixel count, an L-mode binary mask, and a blacked-out RGB image copy. PNG artifacts are exported only when --output-dir is supplied.

**Current boundary:** Rounds boxes outward, clips to image bounds, skips off-image and unselected regions, and preserves source pixels/files. It does not claim all sensitive regions have been found; detection and human review are future stages.

See the [complete input example](src/redactlab/examples/demo.json) and [API tests](tests/test_api.py) for direct Python examples and independently specified expected values.

## CLI and manifests

```powershell
redactlab --help
redactlab --version
redactlab demo
redactlab demo --output-dir outputs/demo
redactlab run src/redactlab/examples/demo.json --output-dir outputs/imported
```

The displayed commands assume the project environment is activated; otherwise use its full command path above. `python -m redactlab` exposes the same CLI.

Every input manifest is a JSON object with `schema_version: 1`, optional boolean `synthetic`, and the project fields described above. Bundled fixtures have `synthetic: true`. Paths inside a manifest resolve relative to that manifest, not the working directory.

Reports identify `mode` (`synthetic_demo` or `imported_observations`), preserve the synthetic-input flag, and state `model_inference: false`. Results print as JSON. `--output-dir` additionally writes `result.json` and any visual artifacts; omitting it creates no output files. Existing generated artifacts in the chosen output directory can be replaced, but input manifests and source images are protected against output-path collisions. Invalid inputs produce a clear stderr message and nonzero exit status.

**Expected demo result:** The demo image contains generated colored rectangles and supplied synthetic regions. It exports mask.png and redacted.png without detecting any real faces or text.

Pixel boxes use `[left, top, right, bottom]` with exclusive right/bottom boundaries. Timestamps are nonnegative seconds. Coordinate spaces, image dimensions, and model versions must stay explicit in future adapters.

## Development

```powershell
.\.venv\Scripts\python.exe -m pytest -q
.\.venv\Scripts\python.exe -m ruff check src tests
.\.venv\Scripts\python.exe -m ruff format --check src tests
```

The repository verifier also builds a wheel, installs it in a fresh isolated environment, and exercises the CLI outside this project's source directory. Runtime dependencies: pillow>=11,<14. Locks include development tools as well as runtime dependencies.

[Implementation plan](docs/PLAN.md) · [Evaluation guide](docs/EVALUATION.md) · [Repository roadmap](../ROADMAP.md)
