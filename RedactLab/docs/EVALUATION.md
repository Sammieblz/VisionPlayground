# RedactLab evaluation

## Current evidence

The packaged demo uses synthetic inputs with independently specified expected outputs. The demo image contains generated colored rectangles and supplied synthetic regions. It exports mask.png and redacted.png without detecting any real faces or text.

These checks establish processing correctness on those fixtures. They do not establish real-model accuracy, real-camera performance, or user-workflow reliability.

Run from the project environment:

```text
python -m pytest -q
redactlab demo --output-dir outputs/demo
```

The root verifier tests a built wheel in an isolated environment and saves results under the repository's ignored verification artifacts directory. A configured CI matrix is not a claim that every platform has already been run locally.

## Real-data evaluation milestone

Sensitive-region recall, missed frames, unintended redaction area, mask coverage, and temporal consistency. Pixel masking correctness does not measure detector recall.

Use independently labeled samples and document collection conditions, representative failure cases, input provenance, model/adapter versions, thresholds, runtime environment, and evaluation commands. Keep training, threshold selection, and test data separate where applicable. Publish measured results with dataset context; leave unavailable measurements explicitly unreported.

Store private media, downloaded models, and generated outputs outside version control; the project ignores data/, models/, and outputs/. Small shareable fixtures must be clearly labeled and packaged intentionally.

## Failure cases to include

Empty/invalid observations, non-finite values, coordinate boundary cases, incomplete inputs, and domain-specific failures belong in correctness tests. Real-data tests must additionally exercise the adapter's uncertainty, missing detections, changed input conditions, and unsupported cases described in the [implementation plan](PLAN.md).
