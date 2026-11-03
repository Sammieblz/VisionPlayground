# FrameFind evaluation

## Current evidence

The packaged demo uses synthetic inputs with independently specified expected outputs. The synthetic query ranks red-car first and blue-car second. Labels illustrate a known vector arrangement; no text or image encoding occurs.

These checks establish processing correctness on those fixtures. They do not establish real-model accuracy, real-camera performance, or user-workflow reliability.

Run from the project environment:

```text
python -m pytest -q
framefind demo --output-dir outputs/demo
```

The root verifier tests a built wheel in an isolated environment and saves results under the repository's ignored verification artifacts directory. A configured CI matrix is not a claim that every platform has already been run locally.

## Real-data evaluation milestone

Precision@k, recall@k, ranking quality, query latency, and index size on independently judged real media. Synthetic vector agreement is not semantic retrieval accuracy.

Use independently labeled samples and document collection conditions, representative failure cases, input provenance, model/adapter versions, thresholds, runtime environment, and evaluation commands. Keep training, threshold selection, and test data separate where applicable. Publish measured results with dataset context; leave unavailable measurements explicitly unreported.

Store private media, downloaded models, and generated outputs outside version control; the project ignores data/, models/, and outputs/. Small shareable fixtures must be clearly labeled and packaged intentionally.

## Failure cases to include

Empty/invalid observations, non-finite values, coordinate boundary cases, incomplete inputs, and domain-specific failures belong in correctness tests. Real-data tests must additionally exercise the adapter's uncertainty, missing detections, changed input conditions, and unsupported cases described in the [implementation plan](PLAN.md).
