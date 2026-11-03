# DocLens implementation plan

## Problem and audience

Convert supplied OCR receipt lines into fields with inspectable source attribution. Developers building receipt ingestion, expense tools, and document automation.

## Current foundation

The `extract_fields()` API, typed records, CLI, packaged synthetic examples, and correctness tests form milestone 1. Recognizes Merchant:, Date:, Total:, and Currency: lines. Dates use valid ISO YYYY-MM-DD; totals use decimal points and optional USD/EUR/GBP codes. OCR and word-to-line grouping are adapter responsibilities. Conflicting candidates remain ambiguous.

Input contract: Document width/height and regions containing region_id, text, and pixel xyxy box. Each region must be one whole OCR line.

Output contract: merchant, date, total, and currency fields, each with value, source_ids, and found/missing/ambiguous status.

Example acceptance: The demo extracts Vision Cafe, 2026-11-03, and 4.40 from explicitly labeled lines. Currency remains unset because the demo deliberately omits it; Subtotal is not mistaken for Total.

## Milestones

1. Runnable foundation: independent package, deterministic observations, Python API/CLI, source preservation, correctness tests, and installed-wheel verification.
2. Integrate an OCR engine, word-to-line grouping, orientation, and document cleanup.
3. Create independently labeled receipts and evaluate fields, provenance, and ambiguous layouts.
4. Support broader templates, human correction, exports, and a document application.

Model and dataset choices will be made at the adapter milestone against its runtime, quality, and redistribution requirements. No model performance is claimed by this foundation.

## Evaluation and completion gates

Per-field exact-match precision/recall, missing/ambiguous rates, source-region attribution accuracy, and OCR error rates measured separately.

Before calling model integration complete: record data provenance, hold out evaluation samples, compare a simple baseline, inspect failure cases, record model/runtime versions, and provide reproducible commands. Before calling the application complete: validate its intended user workflow and export paths on its supported platforms.

## Reuse and integration

RedactLab can consume selected OCR region boxes as sensitive_text detections. Field-to-region references support review before export; no DocLens import is required in RedactLab today.

This package has no imports from another VisionPlayground project. Shared runtime code will be extracted only after multiple real consumers establish compatible requirements. [Repository integration roadmap](../../ROADMAP.md).
