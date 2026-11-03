# FrameFind

**Status: runnable foundation · synthetic demonstration · version 0.1.0.dev0**

Rank supplied image/video embeddings against a compatible query vector. Part of [VisionPlayground](../README.md).

Intended users: Developers building local media search, visual matching, and searchable archives.

This package implements deterministic post-processing of supplied observations. It includes no pretrained model, camera access, GPU requirement, or automatic model download. Model integration and an application UI are future milestones.

## Run locally

Requires Python 3.12 or 3.13. From this project folder in PowerShell:

```powershell
py -3.13 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-lock.txt
.\.venv\Scripts\python.exe -m pip install --no-build-isolation --no-deps -e .
.\.venv\Scripts\framefind.exe demo --output-dir outputs/demo
```

On Linux, use `python3.12 -m venv .venv` or Python 3.13 and replace `.venv\Scripts\python.exe` with `.venv/bin/python`; the installed command is `.venv/bin/framefind`. Each project owns its environment; EyePause's environment is separate.

## Python API

Import `search_embeddings` from `framefind`. Typed input/output records live in `framefind.domain`; future model boundaries are protocols in `framefind.adapters`.

**Input:** A finite nonzero query vector, items containing unique item_id and compatible embedding vectors, optional URI/timestamp, and positive integer k.

**Output:** Matches ranked by descending cosine similarity with item_id tie ordering. URIs and optional timestamp seconds are returned without opening media.

**Current boundary:** In-memory cosine search with dimension and norm validation. k larger than the index returns all items; an empty index returns no matches. Query and items must share the same future encoder/version.

See the [complete input example](src/framefind/examples/demo.json) and [API tests](tests/test_api.py) for direct Python examples and independently specified expected values.

## CLI and manifests

```powershell
framefind --help
framefind --version
framefind demo
framefind demo --output-dir outputs/demo
framefind run src/framefind/examples/demo.json --output-dir outputs/imported
```

The displayed commands assume the project environment is activated; otherwise use its full command path above. `python -m framefind` exposes the same CLI.

Every input manifest is a JSON object with `schema_version: 1`, optional boolean `synthetic`, and the project fields described above. Bundled fixtures have `synthetic: true`. Paths inside a manifest resolve relative to that manifest, not the working directory.

Reports identify `mode` (`synthetic_demo` or `imported_observations`), preserve the synthetic-input flag, and state `model_inference: false`. Results print as JSON. `--output-dir` additionally writes `result.json` and any visual artifacts; omitting it creates no output files. Existing generated artifacts in the chosen output directory can be replaced, but input manifests and source images are protected against output-path collisions. Invalid inputs produce a clear stderr message and nonzero exit status.

**Expected demo result:** The synthetic query ranks red-car first and blue-car second. Labels illustrate a known vector arrangement; no text or image encoding occurs.

Pixel boxes use `[left, top, right, bottom]` with exclusive right/bottom boundaries. Timestamps are nonnegative seconds. Coordinate spaces, image dimensions, and model versions must stay explicit in future adapters.

## Development

```powershell
.\.venv\Scripts\python.exe -m pytest -q
.\.venv\Scripts\python.exe -m ruff check src tests
.\.venv\Scripts\python.exe -m ruff format --check src tests
```

The repository verifier also builds a wheel, installs it in a fresh isolated environment, and exercises the CLI outside this project's source directory. Runtime dependencies: Python standard library only. Locks include development tools as well as runtime dependencies.

[Implementation plan](docs/PLAN.md) · [Evaluation guide](docs/EVALUATION.md) · [Repository roadmap](../ROADMAP.md)
