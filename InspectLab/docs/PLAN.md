# InspectLab implementation plan

## Problem and audience

Turn supplied anomaly-score maps into review decisions and localized regions. Developers exploring product inspection and visual quality control.

## Current foundation

The `inspect_map()` API, typed records, CLI, packaged synthetic examples, and correctness tests form milestone 1. Threshold comparison is inclusive (score >= threshold). Four-connectivity keeps diagonal-only neighbors separate. Region coordinates refer to score-map pixels, which a future adapter must map to source-image pixels.

Input contract: A cases array of case_id, nonempty rectangular finite scores, and a finite threshold. Raw scores may be outside 0..1; they are not probabilities.

Output contract: Per-case pass/review decision, anomalous pixel count, and four-connected regions with exclusive xyxy boxes, area, maximum, and mean score.

Example acceptance: The normal demo map passes. The defective map requires review with three anomalous pixels grouped into two regions.

## Milestones

1. Runnable foundation: independent package, deterministic observations, Python API/CLI, source preservation, correctness tests, and installed-wheel verification.
2. Fit a baseline on normal images of one controlled product type and document score-map scaling.
3. Use separate labeled validation/test sets to calibrate thresholds and compare baselines.
4. Add image inspection, reviewed outcomes, drift checks, and an inspection application.

Model and dataset choices will be made at the adapter milestone against its runtime, quality, and redistribution requirements. No model performance is claimed by this foundation.

## Evaluation and completion gates

Defect recall, false alarms per normal item, image-level precision/recall, and localization overlap. Report thresholds and splits; do not tune on the test set.

Before calling model integration complete: record data provenance, hold out evaluation samples, compare a simple baseline, inspect failure cases, record model/runtime versions, and provide reproducible commands. Before calling the application complete: validate its intended user workflow and export paths on its supported platforms.

## Reuse and integration

MeasureLab geometry can later normalize inspection viewpoints. Image-to-score-map transforms must be explicit; score-map boxes cannot be passed directly as image boxes at a different resolution.

This package has no imports from another VisionPlayground project. Shared runtime code will be extracted only after multiple real consumers establish compatible requirements. [Repository integration roadmap](../../ROADMAP.md).
