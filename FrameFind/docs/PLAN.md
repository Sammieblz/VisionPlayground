# FrameFind implementation plan

## Problem and audience

Rank supplied image/video embeddings against a compatible query vector. Developers building local media search, visual matching, and searchable archives.

## Current foundation

The `search_embeddings()` API, typed records, CLI, packaged synthetic examples, and correctness tests form milestone 1. In-memory cosine search with dimension and norm validation. k larger than the index returns all items; an empty index returns no matches. Query and items must share the same future encoder/version.

Input contract: A finite nonzero query vector, items containing unique item_id and compatible embedding vectors, optional URI/timestamp, and positive integer k.

Output contract: Matches ranked by descending cosine similarity with item_id tie ordering. URIs and optional timestamp seconds are returned without opening media.

Example acceptance: The synthetic query ranks red-car first and blue-car second. Labels illustrate a known vector arrangement; no text or image encoding occurs.

## Milestones

1. Runnable foundation: independent package, deterministic observations, Python API/CLI, source preservation, correctness tests, and installed-wheel verification.
2. Integrate a real compatible image/text encoder with explicit model-version metadata.
3. Label relevance judgments and evaluate retrieval plus useful frame-sampling strategies.
4. Add persistent indexing, deduplication, timestamps, and a search application.

Model and dataset choices will be made at the adapter milestone against its runtime, quality, and redistribution requirements. No model performance is claimed by this foundation.

## Evaluation and completion gates

Precision@k, recall@k, ranking quality, query latency, and index size on independently judged real media. Synthetic vector agreement is not semantic retrieval accuracy.

Before calling model integration complete: record data provenance, hold out evaluation samples, compare a simple baseline, inspect failure cases, record model/runtime versions, and provide reproducible commands. Before calling the application complete: validate its intended user workflow and export paths on its supported platforms.

## Reuse and integration

DocLens text and SceneFlow events can become additional searchable metadata. Keep text filters separate from vector similarity and preserve media provenance.

This package has no imports from another VisionPlayground project. Shared runtime code will be extracted only after multiple real consumers establish compatible requirements. [Repository integration roadmap](../../ROADMAP.md).
