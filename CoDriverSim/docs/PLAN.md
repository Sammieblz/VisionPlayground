# Implementation plan

## Active Unity application work

The current direction is the approved Unity town and portable camera observer described in
[UNITY_GAME.md](UNITY_GAME.md). Unity 6000.3.10f1, URP 17.3, Input System 1.18 and the Windows
application are now implemented locally, alongside the retained MetaDrive package. The game
launches the Python camera observer and an isolated WSL stock openpilot v0.9.7 adapter. Comma
engagement and brake takeover have been observed with the Unity vehicle; this does not establish
performance at junctions or real-road model accuracy.

Downtown Davis, California, is now playable using the verified OSM source, generated physical
roads and lane graph. Routing, scenarios, GPS and map presentation use that shared definition.
Cedar Grove remains selectable for regression and its streetcar system. See
[district evidence and qualification](UNITY_GAME.md).

Current work is activity-cycle validation, scenario-specific rules, broader perception evaluation,
physics qualification and recovery. Completing every requested system, held-out recognition checks,
the 30-minute driving benchmark, and standalone packaging remains necessary before a first release.
The sections below retain the earlier MetaDrive milestones and their original scope.

## Current foundation

- Independent package, typed observation boundary, JSON demo/import commands.
- Native MetaDrive launcher with keyboard driving, traffic seed and density controls.
- Following-gap and closing-time warning overlay using exact simulated observations.
- Optional bounded-run JSON reporting, no video capture or real-model inference.
- A small city street layout with decorative buildings, plus the original road-course option.
- A simpler terrain shader for drivers that reject MetaDrive's native shader, and a bounded
  terrain surface-mask allocation. Buildings currently have no collision bodies.

## Next: measure sustained Windows performance

Windows setup, onscreen rendering, manual keyboard driving, and overlay readability have been
verified on the user's machine; see [validation evidence](VALIDATION.md). Record sustained driving
frame rate, traffic settings, seed, graphics driver, and errors, and check help/reset/exit controls.
Neither the physics-only smoke tests nor a single stationary FPS sample is a driving benchmark.

## Scenario milestone

Add a controlled lead-braking event, followed by cut-ins and lane drift. Separate the event
director and test driver from the observer-only assistant. Save settings, seeds, simulator
revision, fixed time step, and outcomes; verify replay empirically rather than assuming a seed
guarantees identical execution across platforms.

## Camera/model milestone

Add a virtual forward camera and a versioned inference adapter. Keep exact simulator truth in
the evaluator, not the camera model's inputs. Measure preprocessing, inference, and transfer costs.
Evaluate Comma's existing MetaDrive bridge in a separate checkout, including model/runtime compatibility
with Windows/WSL and RX 580. Do not promise an AMD backend before testing it. Do not import vehicle
actuation components into the product.

An unmodified upstream Comma experiment is now installed separately in WSL; see
[the experiment record](COMMA_SIM.md). The newer CPU:LLVM revision was too slow to engage.
Official v0.9.7 with ONNX CPU inference passed its upstream 60-second driving test, and engagement
was also observed with the textured road view and Qt UI running. Sustained performance, traffic,
controlled hazards, and an observer-only model adapter remain future evaluations.

## Product milestone

Navigation-aware guidance, event video buffers, a restrained animated companion, and driver
monitoring require their own implementations and evaluations. No dashcam hardware deployment
or real-road safety claim is included in this foundation.
