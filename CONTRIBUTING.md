# Project conventions

## Independent packages

Each project owns a src-layout package, versioned pyproject metadata, runtime/development lock file, tests, packaged examples, implementation plan, and evaluation guide. Install it in its own environment. No scaffold imports another VisionPlayground project at runtime.

Keep processing APIs independent of command parsing, presentation, inference adapters, and application state. Type model boundaries with Protocols. Imports must not activate cameras, download weights, or perform inference. The initial new-package version is 0.1.0.dev0 and its status is a runnable foundation.

## Observation contracts

- Time is finite, nonnegative seconds; document whether a stream requires strictly increasing timestamps.
- Image boxes are `[left, top, right, bottom]` in pixels with exclusive right/bottom boundaries. Fractional detector boxes are permitted; raster masks round outward and clip to image bounds.
- Keep image dimensions and coordinate transforms explicit. InspectLab map coordinates belong to the score map, not automatically to the source image.
- IDs are opaque identifiers with the uniqueness requirements documented by the receiving API.
- Model scores are not probabilities unless a model-specific calibration supports that interpretation.
- Vectors used together must share the same encoder, vector dimension, and model version.

Independent packages document compatible semantics without sharing a runtime library. Use explicit adapters for integration until real consumers justify extraction.

## CLI and resources

Every foundation supports `--help`, `--version`, `demo [--output-dir DIRECTORY]`, and `run INPUT.json [--output-dir DIRECTORY]`. `python -m <package>` exposes the same command interface.

Input manifests are JSON objects with `schema_version: 1` and an optional boolean `synthetic`. Project-specific payload fields are documented in each README and demonstrated in packaged examples. Resolve file references relative to the input manifest. Load installed examples with importlib.resources rather than repository-relative paths.

Reports distinguish synthetic demonstrations from imported observations and declare that the scaffold did not run inference. Print JSON to stdout; send errors to stderr with nonzero exit status. Export result.json plus applicable images only when requested. Protect source images and manifests against output collisions, including resolved paths.

## Data, models, and evidence

Keep private media, generated outputs, downloaded datasets, and model weights out of source control. Include only small, intentionally shareable fixtures with clear provenance. Record evaluation conditions, labels, model/adapter versions, splits, thresholds, and reproducible commands when real models are added. Credit upstream models and resolve applicable redistribution requirements before distribution.

Every plan must distinguish current capabilities from future model and application milestones. Do not publish synthetic correctness as real-model accuracy or an unexecuted CI matrix as measured platform coverage.

## Checks

From a project's environment, run `python -m pytest -q`, `python -m ruff check src tests`, and `python -m ruff format --check src tests`. Domain tests should specify expected behavior independently of the implementation and cover meaningful failure cases.

From the repository root run `python scripts/check_scaffolds.py`, optionally with `--project <CatalogFolder>`. This builds wheels and runs the checks in separate fresh environments outside each source directory, overriding pytest's development source-path setting. Root tools are linted with the root Ruff configuration. EyePause retains its existing Windows workflow and independent regression suite.

See the [portfolio roadmap](ROADMAP.md) and [project catalog](README.md).
