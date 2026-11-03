# DocLens

**Status: runnable foundation · synthetic demonstration · version 0.1.0.dev0**

Convert supplied OCR receipt lines into fields with inspectable source attribution. Part of [VisionPlayground](../README.md).

Intended users: Developers building receipt ingestion, expense tools, and document automation.

This package implements deterministic post-processing of supplied observations. It includes no pretrained model, camera access, GPU requirement, or automatic model download. Model integration and an application UI are future milestones.

## Run locally

Requires Python 3.12 or 3.13. From this project folder in PowerShell:

```powershell
py -3.13 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-lock.txt
.\.venv\Scripts\python.exe -m pip install --no-build-isolation --no-deps -e .
.\.venv\Scripts\doclens.exe demo --output-dir outputs/demo
```

On Linux, use `python3.12 -m venv .venv` or Python 3.13 and replace `.venv\Scripts\python.exe` with `.venv/bin/python`; the installed command is `.venv/bin/doclens`. Each project owns its environment; EyePause's environment is separate.

## Python API

Import `extract_fields` from `doclens`. Typed input/output records live in `doclens.domain`; future model boundaries are protocols in `doclens.adapters`.

**Input:** Document width/height and regions containing region_id, text, and pixel xyxy box. Each region must be one whole OCR line.

**Output:** merchant, date, total, and currency fields, each with value, source_ids, and found/missing/ambiguous status.

**Current boundary:** Recognizes Merchant:, Date:, Total:, and Currency: lines. Dates use valid ISO YYYY-MM-DD; totals use decimal points and optional USD/EUR/GBP codes. OCR and word-to-line grouping are adapter responsibilities. Conflicting candidates remain ambiguous.

See the [complete input example](src/doclens/examples/demo.json) and [API tests](tests/test_api.py) for direct Python examples and independently specified expected values.

## CLI and manifests

```powershell
doclens --help
doclens --version
doclens demo
doclens demo --output-dir outputs/demo
doclens run src/doclens/examples/demo.json --output-dir outputs/imported
```

The displayed commands assume the project environment is activated; otherwise use its full command path above. `python -m doclens` exposes the same CLI.

Every input manifest is a JSON object with `schema_version: 1`, optional boolean `synthetic`, and the project fields described above. Bundled fixtures have `synthetic: true`. Paths inside a manifest resolve relative to that manifest, not the working directory.

Reports identify `mode` (`synthetic_demo` or `imported_observations`), preserve the synthetic-input flag, and state `model_inference: false`. Results print as JSON. `--output-dir` additionally writes `result.json` and any visual artifacts; omitting it creates no output files. Existing generated artifacts in the chosen output directory can be replaced, but input manifests and source images are protected against output-path collisions. Invalid inputs produce a clear stderr message and nonzero exit status.

**Expected demo result:** The demo extracts Vision Cafe, 2026-11-03, and 4.40 from explicitly labeled lines. Currency remains unset because the demo deliberately omits it; Subtotal is not mistaken for Total.

Pixel boxes use `[left, top, right, bottom]` with exclusive right/bottom boundaries. Timestamps are nonnegative seconds. Coordinate spaces, image dimensions, and model versions must stay explicit in future adapters.

## Development

```powershell
.\.venv\Scripts\python.exe -m pytest -q
.\.venv\Scripts\python.exe -m ruff check src tests
.\.venv\Scripts\python.exe -m ruff format --check src tests
```

The repository verifier also builds a wheel, installs it in a fresh isolated environment, and exercises the CLI outside this project's source directory. Runtime dependencies: Python standard library only. Locks include development tools as well as runtime dependencies.

[Implementation plan](docs/PLAN.md) · [Evaluation guide](docs/EVALUATION.md) · [Repository roadmap](../ROADMAP.md)
