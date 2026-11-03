# RedactLab implementation plan

## Problem and audience

Apply reviewed detection regions to image copies and export inspectable redaction masks. Developers preparing recordings, documents, and datasets for sharing.

## Current foundation

The `create_redactions()` API, typed records, CLI, packaged synthetic examples, and correctness tests form milestone 1. Rounds boxes outward, clips to image bounds, skips off-image and unselected regions, and preserves source pixels/files. It does not claim all sensitive regions have been found; detection and human review are future stages.

Input contract: An image path relative to the manifest, regions with region_id/category/pixel xyxy box, and optional categories. Supported selected categories: face, plate, sensitive_text.

Output contract: Image dimensions, selected clipped regions, union redacted-pixel count, an L-mode binary mask, and a blacked-out RGB image copy. PNG artifacts are exported only when --output-dir is supplied.

Example acceptance: The demo image contains generated colored rectangles and supplied synthetic regions. It exports mask.png and redacted.png without detecting any real faces or text.

## Milestones

1. Runnable foundation: independent package, deterministic observations, Python API/CLI, source preservation, correctness tests, and installed-wheel verification.
2. Integrate face/plate detectors and an OCR-region adapter with reviewable provenance.
3. Evaluate missed sensitive regions and tracking gaps on annotated image/video samples.
4. Add temporal tracking, human review, and image/video export workflows.

Model and dataset choices will be made at the adapter milestone against its runtime, quality, and redistribution requirements. No model performance is claimed by this foundation.

## Evaluation and completion gates

Sensitive-region recall, missed frames, unintended redaction area, mask coverage, and temporal consistency. Pixel masking correctness does not measure detector recall.

Before calling model integration complete: record data provenance, hold out evaluation samples, compare a simple baseline, inspect failure cases, record model/runtime versions, and provide reproducible commands. Before calling the application complete: validate its intended user workflow and export paths on its supported platforms.

## Reuse and integration

Future adapters can translate SceneFlow tracks and DocLens OCR regions into local DetectionRegion objects. Match image dimensions, exclusive boundaries, timestamps, and categories; independent packages remain installable.

This package has no imports from another VisionPlayground project. Shared runtime code will be extracted only after multiple real consumers establish compatible requirements. [Repository integration roadmap](../../ROADMAP.md).
