# MeasureLab implementation plan

## Problem and audience

Measure planar rectangular objects using a known reference in the same plane. Developers building measurement, inspection, and positioning tools.

## Current foundation

The `measure_quad()` API, typed records, CLI, packaged synthetic examples, and correctness tests form milestone 1. Uses a normalized four-point planar homography. Rejects degenerate or unstable references and horizon crossings. The caller supplies a rectangular object on the reference plane; this is not 3D reconstruction or a physical accuracy claim.

Input contract: reference.image_corners in corresponding TL/TR/BR/BL order, positive width_mm/height_mm, and an object_corners quadrilateral in the same order.

Output contract: width_mm, height_mm, rectified_corners, and mm units. Width/height average the respective opposite rectified edge lengths.

Example acceptance: The synthetic perspective fixture contains an independently specified 40 mm by 20 mm rectangle and a 100 mm square reference.

## Milestones

1. Runnable foundation: independent package, deterministic observations, Python API/CLI, source preservation, correctness tests, and installed-wheel verification.
2. Integrate camera calibration, printed-reference detection, and object outlines.
3. Compare physical objects against ruler/caliper references under varied viewpoints and lighting.
4. Add measurement review, uncertainty reporting, and inspection/positioning integrations.

Model and dataset choices will be made at the adapter milestone against its runtime, quality, and redistribution requirements. No model performance is claimed by this foundation.

## Evaluation and completion gates

Absolute and relative dimensional error, repeatability, reference reprojection error, and rejection rates. Separate synthetic numerical tolerance from physical measurement accuracy.

Before calling model integration complete: record data provenance, hold out evaluation samples, compare a simple baseline, inspect failure cases, record model/runtime versions, and provide reproducible commands. Before calling the application complete: validate its intended user workflow and export paths on its supported platforms.

## Reuse and integration

The planar transform may later normalize InspectLab views or position overlays. Keep reference units, corner order, and plane assumptions attached to every transform.

This package has no imports from another VisionPlayground project. Shared runtime code will be extracted only after multiple real consumers establish compatible requirements. [Repository integration roadmap](../../ROADMAP.md).
