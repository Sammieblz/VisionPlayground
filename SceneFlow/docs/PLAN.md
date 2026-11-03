# SceneFlow implementation plan

## Problem and audience

Turn supplied object tracks into timestamped boundary-crossing events. Developers building occupancy, inventory movement, and video analytics tools.

## Current foundation

The `analyze_tracks()` API, typed records, CLI, packaged synthetic examples, and correctness tests form milestone 1. Uses supplied track centers and a finite directed line. A point touching the line retains its prior side. Missing a complete frame breaks continuity; an omitted frame cannot be inferred from timestamps alone. Event timestamps are observed frame times.

Input contract: A directed line with start/end pixel points and complete frames containing timestamp seconds and track_id/box observations. An empty frame is meaningful: it breaks continuity.

Output contract: negative_to_positive and positive_to_negative counts, plus track ID, observed timestamp, and direction for each crossing.

Example acceptance: The demo yields two crossings for track alpha: positive_to_negative at 2 seconds and negative_to_positive at 3 seconds. The outside-segment, missing, and stationary tracks contribute zero.

## Milestones

1. Runnable foundation: independent package, deterministic observations, Python API/CLI, source preservation, correctness tests, and installed-wheel verification.
2. Integrate one object detector and tracker with recorded video.
3. Label clips and evaluate missed crossings, double counts, and identity switches.
4. Add camera/file inputs, zones, dwell-time rules, CSV exports, and an application UI.

Model and dataset choices will be made at the adapter milestone against its runtime, quality, and redistribution requirements. No model performance is claimed by this foundation.

## Evaluation and completion gates

Crossing precision/recall, absolute count error, double-count rate, and identity-switch counts, separated by occlusion and camera motion.

Before calling model integration complete: record data provenance, hold out evaluation samples, compare a simple baseline, inspect failure cases, record model/runtime versions, and provide reproducible commands. Before calling the application complete: validate its intended user workflow and export paths on its supported platforms.

## Reuse and integration

A future RedactLab adapter can translate frame tracks into per-frame pixel regions; an adapter must establish the category and preserve timestamps. No runtime dependency is created now.

This package has no imports from another VisionPlayground project. Shared runtime code will be extracted only after multiple real consumers establish compatible requirements. [Repository integration roadmap](../../ROADMAP.md).
