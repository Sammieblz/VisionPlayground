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
python -m codriversim drive --scene city --steps 300 --output-dir outputs/city
python -m codriversim drive --scene road --steps 300 --output-dir outputs/road
```

The driving commands require the pinned simulator dependencies. During the graphical runs, verify
textured roads, markings, buildings in city mode, and the warning overlay. Check that `R` rebuilds
one neighborhood without accumulating old meshes. Record the report's terrain renderer and FPS
under actual driving conditions. Scenery is decorative and does not add collision physics.

Install the built wheel in a fresh environment and
run its demo outside the source checkout to verify packaged examples. The repository's
`scripts/check_scaffolds.py` currently verifies the six older foundations, not CoDriverSim.

## Interpretation

- Unit tests check warning semantics, malformed inputs, timestamp order, provenance, and input preservation.
- With simulator dependencies installed, building-placement tests check street clearance,
  non-overlapping footprints, and seed reuse. They skip when Shapely is unavailable.
- The headless smoke test checks simulator API integration without camera rendering.
- Neither establishes real-model accuracy, detection range, collision prevention, or frame rate on Windows.
- The MetaDrive demonstrations' live warnings use ground truth, cover only the current lane
  segment, and ignore visibility/occlusion. The separate Unity application's observer receives
  camera images and permitted telemetry, with game truth reserved for scoring.
- The `quiet` state means no illustrative following rule triggered; it is not a safety assessment.
- Record perception and alert latency separately from rendering throughput.

Local execution evidence is recorded in [VALIDATION.md](VALIDATION.md).

## Unity game and camera observer

The [Unity development record](UNITY_GAME.md) contains current commands, measured performance,
model evaluation, process-recovery evidence and outstanding release gates. It is not a qualified
release. Run `scripts/qualify-service.py --image CAMERA.jpg` for transport/storage/capture checks,
`scripts/qualify-perception.py` for the declared licensed still-image subset, and
`scripts/summarize-game-run.py --root INSTALLATION --session SESSION` after a sustained game run.
The report keeps performance, traffic behavior and service health separate.

Use `replay RECORDING/recording.json` to reprocess recorded camera frames through the portable
observer, and `evaluate-ai LABELS.json REPLAY/result.json` for separately annotated camera metrics.
Scenario scorecards link continuous captures with `sensor_capture_id`; actions include the
matching realtime clock. Neither seeded physics nor saved driver actions guarantees identical
sensor images. Human, assisted and Comma comparisons must check `comparison_mode_valid` and
complete scenario exposure before using their scores.

The portable `evaluation.ScenarioResult` v2 contract stores driving criteria independently of
`perception_metrics` and `timing`. The `evaluate` command preserves these optional maps from a
criteria manifest. Timing keys declare units, such as `duration_seconds` and `latency_p95_ms`.
Unknown perception metrics remain null or absent; signed warning lead times may be negative.
Perception quality never changes the driver's score. Results remain evaluation-only and do not
assert deterministic physics replay.

New v2 recording headers reference an append-only `frames_file` journal and report `frame_count`
and recording-queue drops. Older v2 manifests with an embedded `frames` array remain readable.
Replay readers must keep image and journal references inside the recording directory and may
recover complete journal lines before an interrupted final append. A clip marked partial must
not be treated as complete event coverage.
