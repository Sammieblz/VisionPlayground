# Historical prototypes

These files are preserved verbatim from the original repository as provenance. They are not imported by EyePause or its tests.

- `Eyedetection.py`: Haar eye detection, mean-brightness heuristic, frame counter, global keyboard commands.
- `Eyedetection_optimized.py`: downscaled/frame-skipped variant with a cached-region heuristic.
- `performance_test.py`: original capture-loop timing comparison. It does not establish eye-state accuracy or a fair inference-throughput comparison because the variants process different numbers of frames.
- `haarcascade_eye.xml`: original cascade, including its upstream license header.

Running the old detection scripts can issue global media keys immediately. Use EyePause's `--demo` or its benchmark tools for reproducible evaluation without those side effects.

