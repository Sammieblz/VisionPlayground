# Scaffolding verification

The six project foundations use synthetic demonstrations and useful deterministic post-processing. No detector, OCR engine, anomaly model, semantic encoder, camera, or GPU is required by these checks.

## Local results — November 3, 2026

All six packages were built into wheels, installed independently in fresh environments, and tested from outside their source directories on **Windows with CPython 3.13.3 and CPython 3.12.16**. Both runs exercised console/module entry points, packaged fixtures, manifest-relative resources, malformed inputs, exports, and independently specified expected demo results. Ruff lint and format checks passed.

| Project | Tests passed on Python 3.13 | Tests passed on Python 3.12 | Wheel and external-directory demo |
| --- | --- | --- | --- |
| SceneFlow | 27 | 27 | Passed on both |
| DocLens | 22 | 22 | Passed on both |
| InspectLab | 23 | 23 | Passed on both |
| MeasureLab | 23 | 23 | Passed on both |
| FrameFind | 25 | 25 | Passed on both |
| RedactLab | 22 | 22 | Passed on both, including PNG exports |
| **Total scaffold tests** | **142** | **142** | **Six independent installations per version** |

EyePause's existing **73 regression tests passed on Python 3.13**. Its source, dependency configuration, launch paths, and Windows workflow were preserved. The extra Python 3.12 runtime used for verification was installed only in the ignored repository cache, without Windows registry registration or global command shims.

The scaffold CI matrix targets Windows/Linux and Python 3.12/3.13. **Linux and remote GitHub Actions runs were not executed locally**; check their actual results after the workflow is pushed and run.

## What the demonstrations establish

- SceneFlow: two independently specified crossings for one track; touching, missing observations, and off-segment movement do not add false crossings.
- DocLens: source-attributed merchant, date, and total extraction from explicitly labeled OCR lines; omitted currency remains missing.
- InspectLab: the normal map passes; three anomalous pixels in the defective map form two four-connected regions.
- MeasureLab: the supplied perspective fixture reconstructs its independently specified 40 mm by 20 mm rectangle within numerical test tolerance.
- FrameFind: supplied synthetic vectors produce the expected red-car/blue-car ranking without semantic encoding or opening media.
- RedactLab: supplied regions produce three masks with 2,565 union pixels and exported PNGs, while preserving source images.

These are synthetic processing checks, not evidence of real detector/OCR accuracy, semantic search quality, physical measurement accuracy, or complete privacy coverage.

Reproduce the checks from the repository root:

```powershell
py -3.13 scripts/check_scaffolds.py
```

Generated verification logs, wheel files, JUnit results, JSON demonstrations, redaction PNGs, and a summary report are saved under `build/scaffolds`. The latest local artifacts cover Python 3.12; the preceding Python 3.13 artifacts were preserved under `build/scaffolds-py313`. These are ignored build artifacts rather than claims of real-model performance. Subsequent verifier runs replace their selected project logs and update the summary status so a failed check does not retain a previous success report.
