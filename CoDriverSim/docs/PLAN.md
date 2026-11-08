# Implementation plan

## Current foundation

- Independent package, typed observation boundary, JSON demo/import commands.
- Native MetaDrive launcher with keyboard driving, traffic seed and density controls.
- Following-gap and closing-time warning overlay using exact simulated observations.
- Optional bounded-run JSON reporting, no video capture or real-model inference.

## Next: verify the user's Windows machine

Run the setup, drive manually, and record frame rate, traffic settings, seed, graphics driver,
and errors. The physics-only Linux smoke test is not a Windows rendering benchmark.

## Scenario milestone

Add a controlled lead-braking event, followed by cut-ins and lane drift. Separate the event
director and test driver from the observer-only assistant. Save settings, seeds, simulator
revision, fixed time step, and outcomes; verify replay empirically rather than assuming a seed
guarantees identical execution across platforms.

## Camera/model milestone

Add a virtual forward camera and a versioned inference adapter. Keep exact simulator truth in
the evaluator, not the camera model's inputs. Measure preprocessing, inference, and transfer costs.
Evaluate Comma's existing MetaDrive bridge in a separate fork, including model/runtime compatibility
with Windows/WSL and RX 580. Do not promise an AMD backend before testing it. Do not import vehicle
actuation components into the product.

## Product milestone

Navigation-aware guidance, event video buffers, a restrained animated companion, and driver
monitoring require their own implementations and evaluations. No dashcam hardware deployment
or real-road safety claim is included in this foundation.
