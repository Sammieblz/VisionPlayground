# Evaluation

## Commands

From CoDriverSim, in its own environment:

```text
python -m pip install -e ".[dev]"
python -m pytest -q
python -m ruff check src tests
python -m ruff format --check src tests
python -m pip wheel --no-deps --wheel-dir dist .
python -m codriversim demo --output-dir outputs/demo
python -m codriversim drive --headless --steps 100 --output-dir outputs/smoke
```

The last command requires the `sim` extra. Install the built wheel in a fresh environment and
run its demo outside the source checkout to verify packaged examples. The repository's
`scripts/check_scaffolds.py` currently verifies the six older foundations, not CoDriverSim.

## Interpretation

- Unit tests check warning semantics, malformed inputs, timestamp order, provenance, and input preservation.
- The headless smoke test checks simulator API integration without camera rendering.
- Neither establishes real-model accuracy, detection range, collision prevention, or frame rate on Windows.
- Live warnings use ground truth, cover only the current lane segment, and ignore visibility/occlusion.
- The `quiet` state means no illustrative following rule triggered; it is not a safety assessment.
- Record perception and alert latency separately when camera inference is implemented.

Local execution evidence is recorded in [VALIDATION.md](VALIDATION.md).
