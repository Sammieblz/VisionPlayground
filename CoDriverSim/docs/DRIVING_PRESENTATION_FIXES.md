# Driving and presentation fixes

Current pass, November 11, 2026. Final Build 8 is packaged as
`Presentation-local-20261111-175035`, with source code hash
`c32c54e624d683ac47a4398c5575f137f6d561d3f68cbe9bff8049abe0e51a18`.
Both 1,900-second workloads completed; the final run still fails FPS, camera-rate
and ordinary-traffic wait gates. Rendering/mirror regressions, replay smoke tests,
package verification and cleanup checks passed. **This package is not qualified.**
Exhaustive actual scenario driving, physical Xbox and warning-comparison acceptance
remain incomplete. Earlier measurements retain their original package and test scope.

- Manual driving and Self-driving are explicit menu choices. The independent
  co-driver remains visible in both. Self-driving holds the placed scene until
  fresh post-placement camera/model/control data and accepted engagement arrive;
  cancellation and timeout offer retry or manual driving. Placement resets IMU
  history before the paused-physics readiness check.
- Added late-braking, rapid-acceleration, wrong-way, animal-recognition and
  construction-recognition exercises. Recognition grading uses fresh observer
  output and evaluator-owned exposure recorded at the exact camera frame/time,
  including delayed detections. Evaluation truth never enters observer inputs.
- Revised minimap orientation/projection, menus, companion presentation, mirror
  surfaces and wheel bindings. The pickup importer now applies a 180-degree yaw
  correction to normalize its nose to vehicle +Z and separates paired axle meshes
  into individual tires. Static orientation and keyboard motion probes are recorded
  below; exhaustive vehicle acceptance remains pending.
- Normal/Rush hour settings select 20/32 ordinary vehicles plus the dedicated
  bus, emergency vehicle and parking car. Arrival/clearance handling and physical
  district perimeter walls were revised; populated-world behavior remains a gate.

The official openpilot v0.9.7 runtime is untouched. The simulation adapter copies
actual FCW events and left/right LDW HUD outputs into read-only structured warnings,
retaining upstream gates. See [official controlsd source](https://raw.githubusercontent.com/commaai/openpilot/v0.9.7/selfdrive/controls/controlsd.py).
Each warning records its source, producer monotonic time, associated model frame,
capture time and provenance. Event messages have no camera ID, so the exported frame
association is explicitly the model input referenced by controlsState. Missing,
unsupported, stale or unmatched data is **unknown**, not evidence of a clear road.
Transitions are retained in `outputs/services/comma-<token>.warnings.jsonl`.

Portable warnings use camera pixels and permitted ego measurements only. Comfort
and late-braking advice are illustrative heuristics with uncertain monocular range.
Wrong-direction advice requires repeated sign OCR and does not establish road
orientation. Workzone cues require repeated orange/white tapered shapes and are
labelled an unvalidated heuristic with no fabricated confidence score. NanoDet's
supported COCO animal labels are retained; cones, construction and deer are not
claimed as supported detector classes. No real-road accuracy is established.

| Acceptance gate | Current-pass status |
| --- | --- |
| Python regressions | **Pass after the shell-launcher fix:** 149 passed, 2 skipped; workspace temporary directory used. Evidence: `outputs/qualification/presentation-fixes/python-lf-20261111-214631.json`. |
| Ruff | **Pass after the shell-launcher fix:** `src`, `tests` and `scripts` checked; all 72 Python files passed the formatting check. Evidence is included in the Python checkpoint above. |
| Supplied-source audit | **Complete:** original archive hashes preserved, materials/axes/previews documented; see evidence below. |
| Unity compilation and EditMode regressions | **Pass:** Windows build succeeded. Latest broad run passed 202/203 cases; one restart test incorrectly compared against a pre-rounding position. Comparing the actual placed pose fixed the test, and both Civic/GLS cases passed. Coverage includes Submit/handbrake release, takeover/timeout, drivetrain/IMU reset, capture-time recognition, imported wheel axes, mirrors and map alignment. Initial map-test reflection error was also corrected and retested. The final accepted-engagement notice regression run passed **13/13** ([XML](../outputs/unity/presentation-fixes-engaged-notice-1.xml)). |
| Physical/PlayMode regressions | Full run: **42/43 passed**. Cedar/Davis queue regressions, 180-second Rush Hour checks, 30 mph boundaries, contact/transit/animation checks and all ten ambulance cycles passed. The one failure was a pedestrian overshooting a navigation corner. Bounded corner braking fixed it; **7/7 focused retests passed**, covering the original failure, recorded corner, another corner, vehicle avoidance, contact friction, animal crossings and bus boarding. Evidence: `outputs/unity/presentation-fixes-physical-world-1.xml`, `presentation-fixes-pedestrian-retest-1.xml` and `outputs/qualification/presentation-fixes/ambulance-world-36296.json`. |
| Menus and pending self-driving start | **80 actual-world placements passed again after the restart fixes:** 19 scenarios plus Free Drive, both maps, manual/deferred Comma. Physical road placement, required actors, deferred scoring and unchanged pose on Begin checked. Held Submit cannot engage until release; other takeover inputs and timeout remain active. Build 7's actual 1080p Normal driving exercise accepted self-driving and then keyboard brake takeover. The accepted HUD no longer shows the waiting line. Placement tests are not a stock Comma driving pass. |
| Stock Comma and controller | Installed startup succeeds after the CRLF launcher repair. Build 7 has an accepted Normal driving engagement and keyboard brake takeover; the earlier Build 6 audit also records accepted driving with a fresh independent observer. Physical Xbox, exhaustive actual scenario driving, stale-service rejection and restart qualification remain incomplete. The earlier audit includes a collision, as detailed below. |
| Warning presentation and recognition | Earlier packaged evidence records independent observer warnings while Comma is active and actual FCW/LDW provenance with inactive/unknown states. Active stock-warning comparisons, exhaustive new exercises and false-positive review remain pending. |
| Wheels, mirrors, minimap and HUD | 14 wheel/mirror tests passed after optical adjustment; final rendering/mirror regressions passed **19/19** ([XML](../outputs/unity/presentation-fixes-rendering-2.xml)). Matched static 720p/1080p `visual-3` captures show all three fitted mirrors, rearward framing and pickup orientation. Build 6's actual 720p keyboard probe records positive forward wheel RPM, negative reverse RPM, steering input and braking to rest (`motion-final-720/motion.json`). Build 7's 1080p scenario/menu/map captures show the corrected accepted-driving notice, map registration and player arrows; the mini arrow has low contrast against pale roads. Build 8's cockpit smoke capture is linked below. These probes do not complete exhaustive moving-vehicle acceptance. |
| Nearby world activity | Both sustained Davis runs record a complete emergency cycle and parking departure/return, with no logged traffic/rail overlaps. Final ordinary `Car 18` waited **123.072 seconds: fail**. Bus **68.235 seconds** and active emergency **66.144 seconds: pass**. Baseline waits remain recorded: three ordinary vehicles at 121.866, 122.879 and 131.408 seconds, emergency at 120.865 seconds. Complete Cedar/scenario coverage remains incomplete. |
| Sustained combined workload | **Not qualified:** both uninterrupted 1,900-second runs had zero player collisions and no logged traffic/rail overlaps. Final mean FPS and near-20 Hz camera gates failed; detection passed 5 Hz. These used the qualification driver, not sustained accepted Comma driving. Measurements and review links are below. |
| Replay and isolation | **20 frames passed in each smoke test:** recorded game camera and licensed real clip, with model inference enabled and vehicle control disabled. [Camera replay](../outputs/qualification/presentation-fixes/replay-final-recording.log), [real-clip replay](../outputs/qualification/presentation-fixes/replay-final-real.log). Broader replay/accuracy and final observer/control/evaluation boundary qualification remain pending. |
| Package and public exclusions | **Pass:** final Build 8 package verification and cleanup checks completed; no orphaned owned processes remained. Package/source identity is recorded above. This packaging result does not override failed driving/performance gates. |

Both workloads used 1280x720 on Radeon RX 580:

| Measurement | [Build 7 baseline](../outputs/qualification/presentation-fixes/sustained-baseline-review.json) | [Build 8 optimized](../outputs/qualification/presentation-fixes/sustained-optimized-review.json) |
| --- | ---: | ---: |
| Uninterrupted duration | 1,900.015 s | 1,900.005 s |
| Mean FPS | 26.7082 | 28.5536 |
| Frame p95 / p99 | 69.317 / 97.990 ms | 64.730 / 88.702 ms |
| Camera / detection | 18.6735 / 5.8856 Hz | 19.1073 / 5.9458 Hz |
| Sensor-to-warning p95 | 902.225 ms | 569.219 ms |

Build 8 corrects facade-detail distance for the quality setting's LOD bias and
uses a mirror renderer without SSAO or unnecessary color/depth texture copies.
The later measurements improved, but background load was uncontrolled: sampled
total CPU was 87-96%, with roughly 1 GB free memory and other applications active.
The [process audit](../outputs/qualification/presentation-fixes/sustained-optimized-process-audit.json)
found no orphaned prior services. This comparison does not establish causal gains
from the rendering changes, and its FPS, camera and queue failures remain open.

The 80 placements cover 19 scenarios plus Free Drive, two maps and manual/deferred
Comma modes. They establish setup, road placement and deferred scoring, not 80
accepted stock Comma drives or completed exercises. The broad editor run's one
failure and the full world's one failure remain recorded alongside their focused
passing retests; no fresh all-green combined run is implied.

The installed probe in `Presentation-local-20261111-163036`, session
`aec7f3e080e540ffb3e459f4da38bef8`, used ordinary keyboard input, not a physical Xbox
controller. `motion-720/motion.json` records contact with `Car 18` during the
reverse-throttle sample. This control/visual probe is not a collision-free drive
or a Comma driving pass. Its unavailable screen and clipped idle-map caption are
retained as earlier-package evidence.

The later [healthy Comma audit](../outputs/qualification/presentation-fixes/healthy-comma-session-5f04a325.json)
belongs to Build 6 package `Presentation-local-20261111-165107`, session
`5f04a3257aa04452adbe00c1a5f2f3b5`. It records accepted Free Drive, independent
observer warnings and brake takeover, but also a `Car 22` collision before takeover.
It establishes engagement and warning plumbing, not collision-free driving or a
completed scenario. The same session's 720p motion probe used ordinary keyboard
input, not a physical Xbox controller.

The launcher correction preserves LF in repository `.sh` files through
`.gitattributes` and normalizes shell scripts when packaging, including CRLF
source checkouts. The Python checkpoint verifies the repair, and later installed
Build 6/7 engagement probes demonstrate successful startup. Upstream openpilot
control and warning gates remain unchanged.

Selected presentation evidence:

- Fitted mirrors: [left](../outputs/qualification/presentation-fixes/visual-3/mirror--1-1080.png), [rear](../outputs/qualification/presentation-fixes/visual-3/mirror-0-1080.png), [right](../outputs/qualification/presentation-fixes/visual-3/mirror-1-1080.png).
- Build 6 at 720p: [motion telemetry](../outputs/qualification/presentation-fixes/motion-final-720/motion.json), [forward](../outputs/qualification/presentation-fixes/motion-final-720/forward.png), [reverse](../outputs/qualification/presentation-fixes/motion-final-720/reverse.png).
- Build 7 at 1080p: [accepted Normal driving](../outputs/qualification/presentation-fixes/scenario-self-driving-1080.png), [scenario menu](../outputs/qualification/presentation-fixes/scenarios-final-1080.png), [full map](../outputs/qualification/presentation-fixes/map-final-1080.png).
- Final Build 8 at 720p: [cockpit smoke capture](../outputs/qualification/presentation-fixes/optimized-cockpit-720.png).

The [supplied asset decisions](../outputs/qualification/presentation-fixes/asset-decisions.json)
and adjacent previews record three archives. The eight-wheel truck is prepared
**only in qualification output**, not integrated: proportional dimensions
2.082 m wide by 9.5 m long by 2.724 m high; LODs 34,904/12,216/4,187 triangles.
Its 17 gray source material slots received explicit semantic replacements.
Those dimensions are a preparation target, not manufacturer measurements.
The older Rover is deferred because its usable export lacks coherent materials;
the brick storefront needs procedural-material baking and scale review. All three
archives have unknown redistribution permission and remain excluded from public
assets. Any later truck integration belongs under `WorldArt/Local`.

Pickup source orientation is documented separately by two Blender views and
`pickup-orientation.json` in the same evidence directory. These source audits and
synthetic regression checks do not establish finished-game visual quality.
