# Verification and compatibility

This document separates automated evidence from manual compatibility. A compatibility target is not a claim that its playback path has been exercised.

## Automated checks

The unit/integration suite covers deterministic dwell timing across frame rates; bilateral closure and winks; hysteresis; unknown tracking; calibration rejection; corrupt/local settings; stale and duplicate samples; pause ownership; command failure; external transport changes; content changes; session loss; native-backend fakes; camera failure/release; bounded frame exchange; demo isolation; and UI control availability.

Run `python -m pytest -q`, `python -m ruff check src scripts tests packaging/entrypoint.py`, and `python -m ruff format --check src scripts tests packaging/entrypoint.py`.

Build verification uses `python -m eyepause --self-test`, followed by the portable executable's `--self-test` and `--screenshot` paths. The self-test loads the real model and Windows projection; it does not open a camera or send media commands.

The CI workflow is provided in the repository; a remote workflow run is not implied by local success.

## Local results — November 2, 2026

Environment: Windows 11 x64 (build 26200), Python 3.13.3, Qt/PySide6 Essentials 6.12.0, MediaPipe 1.2.0, and OpenCV contrib 5.0.0.93. Dependency versions are pinned in the project lock file.

| Check | Observed result |
| --- | --- |
| Automated suite | **73 passed** |
| Ruff lint and formatting | Passed |
| Source model smoke test | Model checksum verified; real inference completed on a blank frame; Qt and Windows projection loaded |
| Windows discovery | One existing session discovered; identity stable across repeated queries; no playback commands sent |
| Annotated-video evaluator and legacy comparison | Both paths processed a ten-frame blank MJPEG clip successfully; no pause actions; this checks integration, not face accuracy |
| Synthetic trace | 161 samples over 16 seconds; one qualifying closure, one pause, zero false pauses, and one resume |
| Simulated dwell timing | Pause at 7.0 s for closure beginning at 5.0 s; resume at 9.0 s for reopening at 8.5 s |
| Source screenshots and walkthrough | Generated from the actual Qt interface using the explicitly simulated demo |
| Portable application | Final ZIP extracted into a fresh directory; bundled model inference and demo screenshot both passed with Python/virtual-environment paths removed |

Raw synthetic replay output: [trace-results.json](trace-results.json). Its throughput and perfect synthetic-label agreement describe that scripted replay only; they do not establish real-camera accuracy or hardware inference FPS.

The portable verifier extracts the ZIP into a fresh directory, removes Python/virtual-environment paths from the child process, runs the real model self-test, and renders the demo. Reproduce it with `python scripts/verify_portable.py dist/EyePause-0.1.0-windows-x64.zip`. Its report is written to `build/portable-verification.json` and identifies the tested archive by SHA-256. This remains a same-host check rather than a claim of clean-machine or network-disabled validation.

Verified local bundle SHA-256: `8a69f636c6fd111908ac59111da6726ff533cb93dbdef97ed5e3db01a5e5456a`.

## Manual media compatibility matrix

| Target | Windowed/background | Fullscreen | Version | Status |
| --- | --- | --- | --- | --- |
| YouTube / Chrome | Not run | Not run | Not recorded | Requires an interactive video session |
| YouTube / Edge | Not run | Not run | Not recorded | Requires an interactive video session |
| Netflix / Chrome | Not run | Not run | Not recorded | Requires an authenticated viewing session |
| Netflix / Edge | Not run | Not run | Not recorded | Requires an authenticated viewing session |
| Windows Media Player / local file | Not run | Not run | Not recorded | Requires an interactive player and local media |

For each combination, select the player, start playback yourself, enable monitoring, verify blinks do not pause, hold both eyes closed for two seconds, reopen for half a second, and repeat in background/fullscreen. Verify a manually paused video stays paused. Start a second player and confirm EyePause retains its original target. Change content or close the selected player and confirm it does not resume another item/session.

## Camera and lifecycle acceptance

- Physical webcam, glasses, low light, head turns, calibration, and unplugging: **not yet manually exercised**. Camera failure paths use injected captures in automated tests.
- Tray Show/Stop/Exit and close-to-tray behavior: automated UI/control coverage plus a pending interactive Windows shell check.
- Windows without Python installed and with networking disabled: **pending clean-machine validation**. A successful local frozen-build smoke test does not substitute for that environment.
- Streaming services are not accessed or authenticated by the application itself. Unsupported players remain unsupported rather than receiving a fallback keypress.

