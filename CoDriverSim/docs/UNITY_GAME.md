# Unity driving application — development record

This is an implementation in progress, **not the qualified first release** in the
approved plan. The existing MetaDrive demonstrations and v1 JSON commands remain.
The new game is Unity 6000.3.10f1, URP 17.3.0, Input System 1.18.0, D3D11 on Windows.
Dependencies are pinned in `game/Packages/manifest.json` and `packages-lock.json`.

## Current realism rebuild

See [REALISM_REBUILD.md](REALISM_REBUILD.md) for the latest checkpoint measurements
and remaining gates. The older qualification results below refer to their named
builds; they do not qualify the new population, art, bridge or GLS adapter.

From the repository root, prepare the supplied local models without modifying originals:

```powershell
.\CoDriverSim\scripts\setup-world-art.ps1 -SuppliedAssets 'C:\Users\Samuel\Desktop\3D Assets'
.\CoDriverSim\scripts\build-game.ps1
.\CoDriverSim\.venv\Scripts\python.exe .\CoDriverSim\scripts\package-game.py --destination 'C:\CoDriverSim-local'
```

The preparation script uses pinned Blender 4.5.15 LTS. Pass `-Blender` with the path
to an existing matching portable executable to reuse it. Generated models stay in
the ignored `game/Assets/External` tree. Catalogs, converters, source hashes and
notices remain in the repository. Each package gets a new directory and includes
its build identity, asset fingerprint and independent Python runtime.

World-art preparation also builds the clothed MakeHuman civilians. It downloads
only the selected hash-pinned CC0 core files and uses MPFB at revision
`6ff1eb358f6c1959ced77e79e6dd2406d9a71f49` as an offline tool. Source caches and
inspection Blender files stay outside Unity; the game receives FBX meshes,
textures, two detail levels and retargeted animation. To rebuild just those actors:

```powershell
.\CoDriverSim\scripts\setup-civilians.ps1 -Blender 'C:\path\to\blender-4.5.15-windows-x64\blender.exe'
```

Close the Unity editor and finish any automated Unity run before preparing assets.
The build then generates the final URP materials and humanoid prefabs. Rebuilding
does not modify the original files in the supplied desktop asset folder.

To produce a package with local-only assets excluded:

```powershell
.\CoDriverSim\scripts\build-game.ps1 -PublicAssets
.\CoDriverSim\.venv\Scripts\python.exe .\CoDriverSim\scripts\package-game.py --public --destination 'C:\CoDriverSim-public'
```

This uses the Honda comparison vehicle when the local Mercedes prefab is absent.
The packaging command rejects a Unity build marked `local-prototype`. Both build
types remain development builds until the combined acceptance checks pass.

## Current menus and driving controls

The home menu offers **Manual driving** and **Self-driving** for free drive. On
**Scenario tests**, select either driving mode before choosing an exercise. The
independent co-driver observes the forward camera and presents warnings in both
modes, including the Comma display. There is no playable muted mode; older
recordings and comparison labels remain readable.

Self-driving prepares the scene with the vehicle held still while the camera and
observer continue running. It requires fresh model and control messages from
after placement, then accepted Comma engagement before releasing the scene and
starting scenario timing, scoring and capture. Preparation does not count as
manual driving. A timeout or cancelled start offers **Retry self-driving** and
**Drive manually**. Selecting self-driving is not evidence that Comma has taken
control; the HUD distinguishes preparation, request and accepted driving.

Xbox triggers accelerate/brake, the left stick steers, Y changes view, X requests
or cancels Comma, View opens the map and Menu pauses. Keyboard equivalents are
W/A/S/D, C for view, X for Comma, Tab for map and Esc for pause. Deliberate driving
input cancels a pending self-driving start; during accepted driving it retains
the existing takeover behavior. Settings contains input remapping and dead zones;
Diagnostics contains service status and restart controls.

Settings also offers **Normal traffic** and **Rush hour**: 20 or 32 ordinary
vehicles, plus the dedicated bus, emergency vehicle and parking car. The selected
density persists across map changes and is shared by free drive and scenarios.
These menu and behavior descriptions are implementation notes, not additional
qualification results. Historical measurements below retain their original scope.

## Earlier input/bridge repair (November 10, 2026)

The build-16 Xbox Comma attempt failed: all nine X presses reached the app, but
upstream timing/radar health errors prevented engagement. A rejected request then
remained pending without sending a fresh engage event. Requests now require genuine
fresh readiness, cancel on health rejection, and expire after two seconds if not engaged.
Retry is explicit; health recovery cannot unexpectedly start driving. The 100 Hz bridge
no longer writes presentation files, and a stalled producer cannot return apparently
fresh active commands through a responsive socket. Upstream code and checks are unchanged.

A 60 mph Cedar bridge regression reproduced chassis strikes missed by the earlier
18 mph course. Correcting damper speed to the contact plane passed both directions at
both speeds. All 97 editor checks and seven populated-world tests now pass, including
ten ambulance cycles. All 835 currently enabled Davis car/bus turn paths passed a fresh
physical audit; prior conservative exclusions remain. The world run also recorded one
low-speed animal/parking contact, so it is not described as globally collision-free.

The new packaged game accepted three keyboard Comma requests and revoked authority on
all three brake takeovers. A forced bridge crash cancelled control, removed all 19 owned
processes in 2.44 s, and recovered healthy controls through the in-game restart button.
The full 30-minute workload was still pending at this checkpoint. The subsequent
realism candidate's measured run and remaining traffic failures are recorded in
[REALISM_REBUILD.md](REALISM_REBUILD.md).
See [the acceptance checklist](OVERHAUL_ACCEPTANCE.md) for scope and evidence.

## Physics and Davis overhaul (November 9, 2026)

The new source implementation uses a 100 Hz, finite-mass PhysX vehicle through
`IVehicleDynamics` and `VehicleProfile`. Core RVP suspension, tire and drivetrain
calculations have been adapted/reimplemented at the pinned MIT revision; this is
not the full upstream RVP component package. Human steering shaping is separate
from Comma's requested road-wheel angle. Pedestrian/animal navigation requests
bounded forces and follows physical positions. It cannot move the car with a
kinematic solid body. Rail guidance uses bounded forces and can lose the track
after an impact; it does not snap a collided train back onto its route.

Davis and Cedar Grove are selectable. Davis roads, lane routing, buildings, GPS,
signals, crosswalks and the minimap use one generated map, with the original OSM
IDs and source extract retained. Richards Boulevard is below the rail deck.
The map's elevations, building roofs/heights, junction curves and signal schedules
include explicitly documented estimates; this is not a surveyed digital twin.
SUMO 1.28.0 prepares the lane graph offline and does not control any vehicle.
Activity bays require a clear, vehicle-width approach outside the junction. Their
estimated paved aprons and curb cuts are generated with the map and appear on the
minimap. They are simulation additions, not claimed source-map roads.
The simulated Davis bus circuit connects mapped Central Park and Downtown stops.
The Amtrak branch currently has excluded bus turns and is not served by that
circuit. Passenger waiting points use the same served-stop list as the bus.

Cockpit is the default view. Y cycles hood/cockpit/chase/full-screen Comma; View
or Tab opens the full map. The CarConcept-derived cabin has animated controls,
limited-rate mirrors, a Comma display and an observer-driven robot. The Canvas UI
reads a separate `OpenpilotUiSnapshot`; only the control arbiter can grant actuator
authority. Geometry is drawn only against its matching captured frame and session.
The read-only geometry display accepts at most 500 ms of frame age; an older or
mismatched frame hides the overlay. This display limit does not change the
250 ms actuator-command expiry or any upstream Comma health check. Diagnostics
record accepted automation separately from upstream engagement, together with
frame age, cache matching and the actual generated overlay vertex count.

Current isolated-course measurements: 0–60 mph **10.60 s**, 30 mph braking
**10.76 m**, 60 mph braking **42.10 m** after the suspension correction. A 15-second person/dog/cat contact test
measured **0.0000 m** of handbraked-car displacement. These are game calibration
results, not validated Honda measurements or proof of all-world behavior.
Forward/reverse curved-bay tests stop about 0.34 m from their target. The Python
regression suite currently passes 75 tests and both Ruff checks.
The Cedar Grove bridge now uses 50 m eased approaches, tessellated every 2 m,
instead of sharp grade changes. A production-geometry PlayMode test traverses
both directions at approximately 8 m/s without a chassis contact
(`overhaul-bridge21.xml`). The earlier packaged Cedar run logged two road contacts
and remains recorded as a failed contact check; it is not discarded.
The populated PlayMode run `overhaul-playmode14.xml` completed all ten ambulance
dispatch/incident/return cycles (seeds 21–30, 247–293 simulated seconds each),
including temporary occupied station access. Parking also completed its return
within 0.31 m. The cycle log also contained an actor contact, so this was not a
collision-free world result. The bus travelled 1.7 km, visited two
stops and boarded a passenger. These checks include the pedestrian curb/crossing
changes; they are revision-specific and do not substitute for sustained driving.
The separate `overhaul-transit15.xml` check also completed alighting at a later
stop (three stop visits, 1,720 m travelled).
The later `overhaul-parking16.xml` check covers the driver's complete exit as well
as the vehicle's return. Boarding/alighting now moves the non-colliding rigid body,
so render interpolation cannot restore an old seated pose when the collider is
enabled. That run completed within 0.30 m, passed the physical exit/overlap checks,
and logged no actor contact.

The latest complete editor test run (`overhaul-regression29.xml`) passes 97 tests,
including finite-radius tires over a 14 cm driveway lip and person/dog/cat curb
steps using finite leg forces. The handbraked-car contact regressions still pass.
A three-minute populated Davis run (`4eeb485e8f434e67a07ae9c7ff5fc6dc`) measured
48.09 FPS, 29.96 ms p95 / 39.94 ms p99 frame time, 19.96 Hz camera delivery and
5.80 Hz general detection. Its 404 ms latency field measured observer updates,
not only warning-bearing frames; subsequent builds report those separately.
Four player contacts and a parking-route defect were observed in that run.
It is a throughput sample, not a passed driving or 30-minute acceptance test.
The later three-minute run `64b9600a46444daa839547c65a90648b` measured 43.99 FPS,
34.76 ms p95 / 44.52 ms p99 frame time, 19.96 Hz camera delivery and 5.99 Hz
detection. It recorded zero player contacts and zero sampled traffic/rail overlaps.
Warning-bearing frames had 368 ms p95 sensor-to-warning latency (69 samples);
all observer updates had 381 ms p95 latency. This still does not pass the 30-minute
duration gate. The later build fixes the cabin steering hierarchy and passes an
added wheel/column alignment check. Its actual cockpit capture was inspected
alongside 720p/1080p Comma UI fixtures.

The map audit checks 1,288 lane connections without continuity/access errors.
Conservative static turn-envelope checks flag 19 car and 40 bus connections.
The additional physical sweep audit traversed 993 car/bus turn combinations at
100 Hz, with complete body footprints and a 0.25 m pavement-edge tolerance.
Retesting the initially flagged cases corrected cropped-course endpoint artifacts;
one car turn and 157 bus combinations still required exclusion. These correspond
to one additional car internal lane and 207 additional bus internal lanes.
Automatic routing now excludes 20 car and 247 bus internal lanes using a separate
map-hash-bound `davis-turn-limits.json`; source traffic rules remain intact.
`game/physical-turn-exclusions.json` also binds the physical review to the vehicle
and driver source hashes. Regeneration retains it only when all inputs match.
The Central Park/Downtown bus round trip remains connected through wider turns.
These isolated low-speed sweeps do not certify real road rules or interactions
with moving traffic; the populated tests remain separate.

Qualification evidence is kept under `outputs/qualification/overhaul` and
`outputs/unity/overhaul-*`. The populated ambulance tests, turn-clearance audit,
UI fixtures, packaged runtime and sustained performance are separate delivery
gates. Earlier short runs missed 30 FPS and 20 Hz camera delivery. Passing a build
or an isolated course does not qualify this overhaul; the combined release checks
are still in progress. Synthetic UI fixtures are labeled and do not demonstrate
actual Comma engagement.
The seven UI states were captured at both 720p and 1080p. The capture harness
requires a real simulator camera image, checks that fresh model geometry produces
a mesh, and checks that stale/unavailable geometry produces none. Custom graphics
explicitly require a Canvas renderer so these checks work in the editor too.
The six-minute packaged hardware attempt on November 9 recorded keyboard only;
all four Windows XInput slots reported disconnected. Physical Xbox qualification
was still pending at that stage; later confirmed checks are recorded below.
After the user woke the Bluetooth controller, Windows and Unity detected it in
session `c8a4a28b4f944678b70e6c948423ffeb`. That six-minute window recorded no driving
input. This established detection only; subsequent user checks confirmed driving and reconnection.

The subsequent physical Xbox check confirmed acceleration, braking, camera views,
the map and pause controls, but reported failed reverse selection and a
disconnect/reconnect problem. The build-15 journal recorded trigger input and
Unity `Disabled`/`Enabled` device events, with no reverse or pause transitions.
The input handler previously only paused for `Disconnected`/`Removed`.
It now handles `Disabled`, polls availability as a fallback, clears vehicle
commands and automation immediately, and leaves the simulation paused on return.
Unity documents device disabling in its
[focus/background behavior](https://github.com/Unity-Technologies/InputSystem/blob/develop/Packages/com.unity.inputsystem/Documentation~/background-behavior.md).
Button actions now use button semantics with explicit dynamic-update polling;
gear requests have visible feedback and per-button counters in the diagnostic
journal. `overhaul-input26.xml` passes 94 tests, including stopped reverse
selection, physical backward travel, loss cancellation and explicit resume.
The user subsequently confirmed reverse now works and that controller loss pauses,
reconnect leaves the game paused, and Menu resumes. The runtime log records
stopped gear selections plus actual removal/disconnection/reconnection events.
The 16 MiB diagnostic journal cap was reached during the long idle interactive
window before those later checks; the bounded journal therefore cannot supply
their detailed timing. `hardware-check.json` retains that limitation and the
separate user confirmation. The final Xbox Comma request/brake takeover check
uses a fresh session with a complete journal.

The installed build's keyboard test (`4398501a68444caa80dadaa49d776a01`) recorded
38 samples with accepted stock Comma control (39 upstream active samples). A separate brake action
returned authority to the human. An earlier engagement cancelled on upstream
`controlsdLagging`; this is an observed health failure, not suppressed by the bridge.
The map's destination/route UI was exercised. A deliberate owned-bridge crash
removed its 19-process tree in 2.23 seconds. Camera and Comma services restarted
through the diagnostics controls, remained in human mode, and cleaned up on exit.
Camera encoding currently runs through the observer service: after killing it,
Comma also needed its restart control. `packaged-recovery.json` preserves these
conditions and distinguishes valid foreground game captures from cached PrintWindow images.

The first long overhaul attempt (`98afcd731f434714bca1adb5c102a136`) was stopped
after 911 seconds because a pedestrian/bus crossing deadlock accumulated queues.
It measured 47.93 FPS, 32.40/41.49 ms p95/p99 frame time, 19.97 Hz camera delivery,
6.16 Hz detection and 352 ms p95 warning latency. Zero player contacts or sampled
vehicle/rail overlaps do not make that a passed world test. The radial pedestrian
stop rule has been replaced with approach braking and slow clearing for a vehicle
already occupying the crossing. Physical forward and curved-path clearance remain
authoritative. `overhaul-crossing17.xml` reproduces a pedestrian beside a bus with
a truck following: both vehicles and the pedestrian clear without penetration in
16.45 simulated seconds. The populated endurance rerun is still required.
With the physical routing exclusions, `overhaul-world18.xml` completed all ten
ambulance cycles (seeds 21–30, 249–317 simulated seconds), the parking/driver exit,
and the crossing regression. Its transit case failed because initial placement
skipped nearer timetable stops. The corrected `overhaul-transit19.xml` run passed:
three stops, 2,511 m travelled, boarding and completed alighting, with no logged
actor contact. The bus now begins near the first scheduled stop and chooses the
next stop after its initial collision-free placement.

The Cedar bridge packaged retest (`82a6d091950d46aea5d730b7ef632d8b`) travelled
511 m in 120 seconds with zero player contacts, at 53.22 FPS. Its bus then exposed
a separate cornering issue: a straight-ahead clearance cast saw street furniture
beyond a turn and held the bus indefinitely. Cedar now checks that furniture
against its curved full-body route while retaining actor clearance and physical
collisions. Red/stop-line braking no longer disappears just past its stopping
threshold. `overhaul-cedar23.xml` completed two stops and 299.7 m without a bus
contact. Standalone world previews also tolerate the absence of a player camera.

The later Davis sample (`f49f35f640a749afa080d8421eac8567`) was stopped after
617 seconds to incorporate that Cedar fix into the final candidate. It measured
50.53 FPS, 28.65/34.39 ms p95/p99 frame time, 19.97 Hz camera delivery, 6.07 Hz
detection and 349 ms p95 warning latency. It completed ambulance and parking
cycles with zero player contacts or sampled vehicle/rail overlaps. This remains
a short run, not the required 30-minute qualification. Actual frame-matched model
geometry was rendered in 1,780 of 2,352 diagnostic samples; a verified foreground
capture shows its path on the cockpit display. This is display availability,
not a perception-accuracy metric.
The final editor regression run (`overhaul-regression25.xml`) passes all 90
checks, including static obstacles on curved routes and continuous bridge grades.
Its source fingerprints are retained in `regression25-inputs.json`.


Build from the repository with `CoDriverSim/scripts/build-game.ps1`, then run
`CoDriverSim/.venv/Scripts/python.exe -m codriversim game`. The local executable is
`CoDriverSim/outputs/game/CoDriverSim.exe`. `setup-game.ps1` installs the Windows
observer dependencies, fetches external textures/models, and builds. Comma needs
the existing WSL v0.9.7 installation documented in `COMMA_SIM.md`.

## Boundaries

- PhysX rigid bodies, RVP-derived wheel forces and the simulation-only control
  arbiter own the virtual car.
  Manual input wins. Braking, deliberate steering, pause, lost controller, stale or
  cross-session commands revoke Comma authority. Human is the startup default.
- `contracts.py` strictly admits camera images, calibration, ego sensors and labelled
  navigation-map instructions. It rejects additional fields, including hidden truth.
- `perception.py`, `signs.py`, and `road.py` are portable observation code; they do
  not import Unity, MetaDrive, controllers or the simulation bridge.
- `scripts/unity-comma.py` is a separate simulation adapter. It starts an isolated
  stock v0.9.7 manager, feeds its original simulated sensors/CAN/VisionIPC camera,
  and returns stock actuators. Upstream source and engagement health checks remain
  unchanged. A separate read-only file contains only model outputs for the observer.
- Scoring reads world truth in the game. That data never enters the observer packet.

Camera wire v2 uses length-prefixed UTF-8 metadata plus bounded RGB24/JPEG bytes.
Each connection has a session capability, frame IDs and monotonic capture times.
Latest-frame slots discard obsolete work. The local service encodes RGB with OpenCV
before forwarding a JPEG to the separate Comma connection. There is no unbounded
inference queue. Source camera is independent of hood/cockpit/chase views.

## Assets and storage

`game/assets-manifest.json` records URLs, licenses and content hashes. Textures and
models live in ignored `game/Assets/External`, `models`, and `outputs/assets`.
Poly Haven materials are CC0. OpenCV Zoo models are Apache-2.0; notices are in the
package's licenses directory. Quaternius archives retain their included license.
Generated Unity Library, builds, recordings and diagnostic images remain ignored.

### Modeled scenery pass (November 9, 2026)

The presentation layer now loads prepared, licensed models for three ordinary car shapes,
pickups, an ambulance and coach bus; three houses, shops, apartments and offices; metro-style
train/streetcar bodies; signal housings and visors; oak/pine trees; truss bridges, cones,
barrels, barriers, mailboxes and a construction crane. Accessible procedural buildings,
the cockpit, animated excavator, people and animals still have simpler geometry. This is
not a claim that every asset is photorealistic or that visual changes improve measured AI accuracy.

Open Robotics/Gazebo files are pinned to revision
`8163eb4b5e7e21985c6591d1c0bfb56468c0093f` in `game/realism-manifest.json`.
Their CC BY 3.0 notice and individual authors are retained. The CC0 Innerscene railcar,
Poly Haven surface maps and HDR sky are pinned in `game/surface-rail-manifest.json`.
`scripts/fetch-game-assets.py` verifies the original and both new manifests;
`scripts/package-game.py` includes these manifests and licenses in the bundle.

`RealismImporter` normalizes axes and metres, repairs material texture paths, prepares URP
normal/occlusion/smoothness maps, and merges stationary rail detail by material. Railcars
use a compact 13.5 m variant within the existing station/crossing envelope. This is an adapted
game vehicle, not the source railcar's certified dimensions. The original 330 rendered mesh
parts become 32 parts per car, retaining separate doors and wheel geometry. `MeshPanels`
cuts textured bus and parking-car door panels at triangle boundaries; the moving panel leaves
a genuine opening. Rail boarding uses the selected modeled door pair. Ground-truth transport,
observer contracts and Comma authority remain separate and unchanged by the asset layer.

Most decorative façades use imported models and mesh collision. They do not advertise activity
doors; visits use accessible buildings with actual openings. Additional street-facing lots
avoid the river/bridge ramps and reserved construction/emergency access. The bridge import
finds its deck plane and aligns it just below the existing road and markings. The independent
AI camera sees the same scenery/sky while retaining its own fixed mount and calibration.

For reproducible inspection, Unity editor entry points `CoDriver.Editor.RealismImporter.Preview`
and `.PreviewTown` write PNGs to `outputs/qualification/realism-previews`. Run them in batch
mode with `-force-d3d11`, without `-nographics`. Preparation is also part of the normal build.
The asset pass passed 69 Unity EditMode and 66 Python tests; the added tests check metre-scale
vehicle envelopes, both cut door openings and their closed panels, bound PBR/sky textures,
the player wheel pivots/radius and an unobstructed independent forward-camera view. The player
uses a separately prepared lower shell/hood and upper cabin, with suspension-driven textured
wheels. The driver camera can hide the upper cabin without hiding the hood or changing the AI
camera. Rendering uses 2x MSAA and a 90 m shadow distance.
Previous performance and perception measurements below predate this scenery pass unless
explicitly labelled otherwise.

Two initial 150-second scripted driving checks of the upgraded scenery averaged 55.15 and
56.36 FPS. Both had zero player collisions and zero GPU readback drops. The second run had
19.89 Hz camera receipt but only 4.49 Hz detection, so it did not meet the detection target.
The observer was subsequently tuned to reject unsuccessful speed-sign headers before two
additional OCR passes, retaining the same required words/numbers and temporal confirmation.
The worker's minimum period changed from 180 to 150 ms to leave headroom for slower OCR cycles;
its single latest-frame slot still bounds work, with no catch-up burst. Recognition thresholds
and the stock Comma models/controllers are unchanged. The before/after OCR smoke comparison
on a town preview and captured frame returned identical observations; it is not an accuracy study.

The subsequent 100-second run, session `4d876c0fa1234cb8a2adb0633158c136`, measured **53.85 FPS**,
p95/p99 frame times **23.43/29.50 ms**, **19.97 Hz** camera receipt and **5.40 Hz** detection.
Capture-to-observation p95 was **436 ms**, with zero GPU readback drops and zero player
collisions. The scripted driver moved for 60.8 seconds and waited at traffic controls for the
remainder. This is a short default-scene smoke check, not the required 30-minute qualification
or validation of perception accuracy on the new assets. The Windows input backend reported
keyboard only during these runs; earlier physical Xbox checks remain historical evidence.

### Real-world maps considered

The running map is still synthetic. A real district is not implemented by placing satellite
imagery under the current grid: the lane graph, road heights, junction conflicts, crossings,
transit stops and scenario spawn locations all need to match its geometry.

- **OpenStreetMap plus SUMO** is the recommended open-data route for a compact real district.
  [SUMO imports OSM roads](https://sumo.dlr.de/docs/Networks/Import/OpenStreetMap.html), including
  configurable lane/junction/traffic-light interpretation. Its
  [OpenDRIVE export](https://sumo.dlr.de/docs/Networks/Export.html) has documented limitations,
  including signal/object information; those require explicit adaptation and validation here.
  Building footprints need heights, façade assets and collision preparation. Retain
  [OSM attribution and ODbL obligations](https://www.openstreetmap.org/copyright) with any extract
  and derived database. No OSM-derived database is bundled in this build.
- **Cesium for Unity** supports georeferenced terrain, buildings and
  [photorealistic 3D tiles](https://cesium.com/learn/unity/). It is an option for visual context;
  tile imagery does not define the simulation's usable lanes, animated doors or traffic logic.
  Dataset terms, access and streaming costs must be checked for the selected source, and the
  extra GPU/streaming workload benchmarked on the RX 580 before adopting it.

Use a separate map adapter with explicit origin/projection, source attribution, lane connectivity
and scenario anchors. First qualify one district's intersections, elevations and collision surfaces;
retain this town for regression comparisons. No account, subscription or real-map service has
been provisioned by the asset pass.

### Selected district: downtown Davis, California

The selected candidate covers **1.02 km²** around Central Park and the train depot:
38.5408–38.5498 N, 121.7475–121.7358 W. The source includes residential/commercial streets,
parking, rail, crossings and transit stops in a compact area. The
[city's downtown plan](https://www.cityofdavis.org/city-hall/community-development-and-sustainability/planning-and-zoning/downtown-davis-plan)
provides a municipal reference for later street and access review. No streetcar line is asserted
to exist here; fictional scenario additions will need explicit labels.

`game/maps/davis-downtown.json` defines the extent, local origin and attribution. The offline
`codriversim.maps` preparation module resolves source geometry into metres east/north and reports
missing attributes. It imports no simulator or perception code. The downloaded source, manifest,
derived layout and PNG preview are in ignored `outputs/maps/davis-downtown`. This initial survey
feeds the subsequent SUMO/Unity compilation described at the top of this document. Davis is now
playable; Cedar Grove remains selectable for regression and streetcar activities.

Reproduce from the CoDriverSim directory, using a new output folder for a new snapshot:

```powershell
.\.venv\Scripts\python.exe scripts\fetch-district.py --source osm-api --output-dir outputs\maps\davis-downtown
.\.venv\Scripts\python.exe scripts\prepare-district.py outputs\maps\davis-downtown --output-dir outputs\maps\davis-downtown\survey-final --preview
```

The fetcher normally offers Overpass; the recorded survey used one bounded read-only OSM API
request after the public Overpass instances failed. Existing caches require matching source hashes
and district definitions and are reused without network access; preparation refuses to overwrite
an output directory. `--preview` uses the existing optional Matplotlib installation. The map module
and JSON preparation otherwise use only the standard library. The raw snapshot hash is
`ecc6602959ec6b02c28de92544385489e8bdc90ce73c0d534b372e4ea9019e58`.

The extract contains 301 road records, 909 building records, 14 rail ways, 23 signal nodes,
103 crossing nodes and 18 transit-stop records. These are source records, not unique physical
objects, and complete source ways can extend beyond the box. Only 29 road records specify lane
counts and 134 specify speed limits; none specify building heights and seven specify levels.
Two incomplete relations are reported and omitted. Missing geometry is never bridged with an
invented segment; levels are not converted to claimed measured heights. Source member roles
survive. The initial survey leaves fragmented rings separate and junction candidates undirected;
the later playable-map compiler assembles polygon rings and constructs reviewed lane connections.

The playable-map checks cover lane connections/access, pedestrian crossings, vehicle envelopes,
rail clearance/elevation and shared collision/map surfaces. Generated dimensions and schedules
remain labelled estimates. Map information belongs in navigation or
simulation/evaluation, never as hidden truth in camera perception. Retain © OpenStreetMap
contributors and the ODbL notice with the raw and derived data. Both are bundled under DistrictData.

The survey change passed 71 Python tests and Ruff checks. Five hand-authored map tests cover
projection, units, unknown attributes, node connectivity, incomplete geometry and member roles.
Direct integrity probes rejected modified source bytes and preserved existing generated output.
A fresh wheel outside the checkout passed 68 tests, with the optional Shapely scenery module
skipped; its original demo and `pip check` also passed. The root `check_scaffolds.py` currently
accepts only the six original catalog projects, so equivalent isolated wheel checks were run
directly for CoDriverSim. No Unity runtime code changed for this source survey.

Event recording keeps a bounded pre-trigger ring and durable event directories,
including explicit timestamps, calibration and synchronized observation IDs.
Automatic recordings have a 2 GiB disk budget. When it fills, recording reports an error
and stops automatic writes; it does not delete clips. Explicit saves remain available.
Remove unwanted clips and restart the camera service to reclaim the automatic budget.
Sensor replay uses the same observer. Uncalibrated real video has no range claims.
`replay VIDEO --calibration CALIBRATION.json` and `evaluate CRITERIA.json` are separate
from the original `run` manifest command. Synthetic correctness is not real-road accuracy.

## Historical checks before the physics/Davis overhaul (November 9, 2026)

The following records describe earlier builds. Current overhaul measurements and
remaining qualification gates are listed at the beginning of this document.

Target: Windows, Ryzen 7 2700X, Radeon RX 580 8 GB, 32 GB RAM, 1280 × 720 D3D11.

- 66 Python tests pass, including the retained CLI/scenery checks and new camera contracts,
  bounded recording, scoring, tracking and labelled-image evaluation checks. Ruff passes.
  A fresh wheel outside the checkout passed 63 tests with the optional MetaDrive scenery module
  skipped because Shapely was unavailable; `pip check` passed. The portable v2 scenario-result contract keeps
  driver criteria, perception metrics and explicitly unit-labelled timing separate.
- 61 Unity EditMode checks pass: authority arbitration, virtual Xbox inputs, local railway
  gates, road continuity, scoring rules, and animated asset dimensions/controller presence.
  The latest checks cover retained intersection reservations, bridge ramps versus obstacles,
  stopping a fast emergency vehicle behind a bus, and the clothed civilian/worker humanoid rigs.
  An isolated preview-scene physics course checks acceleration/braking, reversing/handbrake,
  turning stability, a 6.5-degree slope, and solid-barrier collision. Braking from 30 mph took
  8.55 m on the flat course. These broad game-behavior checks are not real-car calibration.
  The course uses fixed 0.02-second physics steps; see Unity's
  [PhysicsScene.Simulate documentation](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PhysicsScene.Simulate.html).
- The user confirmed physical Xbox controls, camera changes and pause work. After localizing
  gate activation to the approaching train at each crossing, the user confirmed the stopping
  problem at the intersection/railway was resolved.
- Session `3097381e05d64a9594fe08e013291270`: 180 seconds, 44.19 mean FPS,
  p95 33.38 ms, p99 39.32 ms; 3,498 submitted camera frames (19.43 Hz), zero GPU readback drops.
- Session `0ecfafa34af842c0921ea368ee912762`: 420 seconds, 41.21 mean FPS,
  p95 36.58 ms, p99 43.05 ms; observer camera receipt 19.93 Hz, detection 5.46 Hz,
  capture-to-observation p95 319 ms. About 188 seconds were paused after controller disconnect;
  the camera was stationary during the world tour. This is **not** sustained-driving acceptance.
- Session `e6be4e0064d649419df09f4095bd9a10`: stock Comma engaged in the Unity car,
  with 56 consecutive diagnostic samples showing active control (about 14 seconds), reaching
  9.32 m/s. A brake input returned authority to the human and stopped the vehicle. Controls
  remained human afterward. These observations preceded force-based actuator calibration.
- Installed session `fac931230726473e838539a7803f667b` used the force-based actuator mapping:
  102 active/healthy samples over 26.2 seconds reached 9.04 m/s, then a brake input returned
  authority to the human and stopped the car. The isolated package started both services from
  outside the checkout. Killing its observer process exposed disconnection in Settings;
  restarting the observer and Comma restored readiness. Closing the game removed both readiness
  files and ended its owned services. Direct GUI-to-WSL launch failed on this machine; the
  packaged Windows Python helper now owns that launch and signals shutdown if its game exits.
- Installed session `1d10c5d2e9aa4473b2f76de28d8ba466` was stopped at 1,405 seconds after
  traffic deadlocked. It averaged 56.72 FPS (p95 22.28 ms, p99 27.89 ms), camera 19.85 Hz,
  detection 5.37 Hz, and capture-to-observation p95 333 ms. There were no player contacts
  or sampled NPC overlaps, but the player moved for only 291 seconds. This is a failed
  endurance test. Junction reservations now require the front vehicle near the stop line;
  the regression check rejects a rear vehicle that would lock out its own queue leader.
- Session `736cc13227594aed8692acda5de9834e` exposed a second queue deadlock between a long bus
  and a shorter emergency vehicle. Queue priority now compares front bumpers, not vehicle
  centres. A geometric regression checks that the leading bus can clear its turn beside the
  stopped emergency vehicle. This run remains failed endurance evidence.
- Session `e2d6cfd0b4d94f738729f1e90c24cca1` completed 1,800 seconds with both models:
  57.41 mean FPS, p95 21.68 ms, p99 28.50 ms, camera receipt 19.66 Hz, general detection
  5.26 Hz, and capture-to-observation p95 372 ms. Comma was healthy in 99.0% of samples.
  This meets the measured throughput targets but **fails world acceptance**: the scripted
  test driver contacted a car, and queues eventually deadlocked despite zero sampled NPC
  overlaps. New regressions cover an emergency vehicle still alongside a short truck before
  turning, and an old reservation whose owner has turned again elsewhere. Queue leaders now
  clear before an overlapping passing vehicle turns; reservations release outside their exit
  boundary regardless of the old owner's later heading. Sustained world validation must be rerun.
- Session `6d28cdff586d4772a9d2180810bdbf6f` completed 1,800 seconds: 57.73 mean FPS,
  p95 20.89 ms, p99 28.34 ms, camera receipt 19.99 Hz, detection 5.40 Hz and capture-to-observation
  p95 324 ms. The test vehicle travelled 4.96 km but contacted an oncoming truck while turning
  at 1,060 seconds; queues then stalled. This remains failed world/endurance evidence despite
  meeting throughput targets. Its straight-ahead test-driver probe missed a curved conflict.
  The qualification harness now uses curved clearance and shared junction reservations; NPC
  queue priority also accounts for a player vehicle ahead. Streetcars now use the same junction
  reservation and curved clearance system with their own braking limit. All new regressions pass;
  live qualification of these changes remains separate. This older build recorded no rail-overlap
  diagnostics, so its rail check is unknown. The run observed 35 bus, 14 train and 15 streetcar
  stops, boarding on all three, parking departure/return and all emergency phases. All 19 owned
  WSL processes and readiness files were gone after normal game exit.
- Updated installed session `873202f8cb4f4012824b89d75a997395`: the first 509 seconds contained
  505 world samples with no recorded traffic or rail overlaps. The streetcar completed four
  stops and exercised boarding, departure, signals, intersection yielding and road-conflict
  stops; boarding was observed for bus, train and streetcar. The player was stationary and the
  bus/emergency queue waited behind it. This is a focused activity smoke check, not a replacement
  for the required driving endurance run. Physical controller checks were pending at that stage.
- Vertical GPU readback orientation is corrected explicitly as `rgb24-bottom-up`. The observer
  converts to upright pixels and encodes JPEG asynchronously from the driving loop. Earlier
  inverted-camera runs are invalid for recognition evaluation.
- World logs observed bus boarding/alighting, building entry/exit, worker sequences, and bird
  takeoff/flight/landing. Visual inspection caught raw-FBX/prepared-prefab name collisions;
  prepared resources now have unique paths and animated scale checks.
- Session `6fbb4eede3c34468be20765c554fc289` observed the complete parking-driver cycle:
  walk to car, open/enter, drive out, return/park, exit, visit a shop, return and depart again.
  Bus, train and streetcar passengers boarded and alighted. This 600-second world probe averaged
  45.60 FPS, camera receipt 19.97 Hz and detection 5.29 Hz, with a stationary player and an editor
  test run overlapping startup. It also exposed traffic overlaps; it is not world acceptance.
- Session `e8618295335c4e88a24e6c5ba0075c7d` traversed the bridges and upper course without body
  contacts, but an NPC hit the test vehicle while their curved lanes converged. Three subsequent
  curb contacts ended the drive. This failed course run lasted 540 seconds (273 moving), averaged
  57.12 FPS, and does not satisfy sustained-driving acceptance.
- Installed session `b100814be1d640d2b1a06aabb6653317` ran 900 seconds at 59.28 FPS with no
  player contacts, but Comma was unavailable and a bus/emergency overlap occurred. This is not
  complete-workload acceptance.
- Installed session `13dd6024ccd94433ba358afe06b8e3a9` ran both models for 819.7 seconds:
  57.59 mean FPS, p95 20.86 ms, p99 27.76 ms, camera 19.81 Hz, detection 5.33 Hz,
  capture-to-observation p95 341 ms, and no player contacts. It failed world acceptance: NPCs
  stopped at bridge ramps and a truck/car turning conflict overlapped. Traffic probes now exclude
  physical road support surfaces, and junction reservations persist through a turning vehicle's exit.
- The real observer service passed dropped-frame, hidden-field rejection, duplicate-frame,
  wrong-capability, oversized-packet, unavailable-storage and fresh-session probes. It continued
  inference after a recording-directory failure and exited cleanly when each client disconnected.
  Reproduce with `scripts/qualify-service.py --image CAMERA.jpg`; results are transport/recovery
  evidence, not model accuracy. In-game crash/restart checks remain separate.
- Installed session `b28b7d206a8447b7883920a059f68d4a`: a deliberate SIGKILL of the active
  Comma bridge at approximately 6.9 m/s cancelled virtual automated control. The first cancelled
  diagnostic sample followed the last applied-control sample by 249 ms, with zero throttle/steer;
  this sampled interval is not a direct crash-to-actuator latency measurement. Manual braking
  stopped the car. The outer subreaper removed all 19 owned Linux processes and readiness in
  2.44 seconds. Restart restored health; restarting a live instance displayed its waiting state
  while the interface continued rendering. A subsequent forced game exit removed its new
  19-process tree in 2.22 seconds and the observer readiness disappeared. A separate supervisor
  fixture confirmed that an unrelated sibling process survived both bridge and owner failures.
  The tested build retained stale health fields in diagnostics after loss; source now clears them.
- A licensed Visaitech real-camera clip was processed through the same observer: 100 frames,
  sampled at 5 Hz over 20.33 seconds of media time, median processing 128 ms. Calibration and
  motion measurements were unavailable, so all monocular ranges remained unknown. Detections
  were **not** validated as accuracy. Visual review confirmed a train false positive on a close
  white car at media time 10.884 seconds; `visual-review.json` records the limitation.

The original 75/120-second probes failed camera delivery (8.4/12.6 Hz). Moving JPEG encoding
out of Unity and bounding the transport improved delivery to the measurements above. None of
these throughput measurements qualifies unfinished world behavior or scenario correctness.

## Scenarios, replay, and evaluation

Scenario selection resets activity actors before the drive begins and records the seed,
initial vehicle position, comparison mode, actions, criterion events and scorecard in a separate
scenario directory. Scripted braking, cut-in, wind, crossings and activity dispatch are isolated
from perception. Scoring uses stop-line transitions, measured stop duration, clearance, speed,
crossing state and collision evidence. An unencountered or unfinished exercise cannot pass.
These first scenario implementations still need end-to-end qualification and timing refinement.
Scorecards now include sampled human/Comma control durations and authority changes. A requested
Comma run with manual horizontal motion, or a human run using Comma, is marked invalid for mode comparison
without penalizing a needed takeover. Scenario completion requires reaching the actual course
corridor; leaving the corridor is a separate driving criterion. The current source excludes vertical
suspension settling from manual travel; earlier scorecards can include that startup artifact and
are retained as recorded.

Each non-free-drive scenario requests a separate continuous sensor recording, linked by
`sensor_capture_id` in its scorecard. Action rows also contain the camera's realtime clock for
synchronization across pauses. Scenario captures stop on scenario close, have a ten-minute hard
limit, and share the 2 GiB automatic disk budget. Hitting either limit leaves a partial recording
with an error; recordings are never silently deleted. Event clips still retain their independent
10-second pre/post windows. A real-service start/stop probe replayed all twelve captured frames
with their original session, calibration, timestamps and JPEG bytes.

The first in-game continuous-capture trial (`0e55f42692ac44fdad3bec3285f60a4e`) exposed a
recording bottleneck: repeatedly rewriting its growing 12.8 MB manifest delayed camera delivery,
and Comma's stale-command guard cancelled control. Its 84.6-second capture contained 1,076
frames and replayed in the game, but the scenario was incomplete with a critical collision and
an invalid Comma comparison. These are failed-drive results, not successful autonomous driving.
New recordings append metadata to `frames.jsonl` beside a small atomic v2 header. A bounded
32 MiB/80-request worker keeps disk writes outside camera receipt and inference. Queue overflow
is reported and affected clips remain partial. Python and game replay retain support for older
embedded-frame v2 recordings. The ring reserves two extra seconds for delayed inference triggers;
clips still select the requested ten-second pre-event interval.

The follow-up installed capture (`ba53a77b670b4aeabe3904a3ceae7f4d`) saved 1,914 frames over
95.58 seconds at 20.02 Hz, with zero recording-queue drops; maximum camera gap was 79 ms.
Attached observations contained 5.17 distinct detection updates per second after initial capture
warm-up. The 414-byte header and frame journal replayed in the game. Comma remained active in
355 diagnostic samples with no stale-command cancellation, but hit a signal pole at the
intersection. The scenario remains incomplete with a critical failure and an invalid comparison-mode
flag. Its manual brake takeover and the earlier vertical-settling accounting are recorded limitations.
This validates the recording fix, not driving competence.
A separate 20 Hz storage probe saved and replayed all 1,201 submitted frames with no queue drops;
frame submission took at most 1.88 ms despite a concurrent Unity build. Its 70.25-second total
includes 60 seconds of submission plus final disk replay.

`evaluate-ai LABELS.json REPLAY/result.json` evaluates complete held-out frame annotations.
The v2 labels document has `annotations_complete: true`, a `source`, a `split`, and `frames`.
Each frame has `session_id`, `frame_id`, `timestamp`, and `objects` containing `object_id`,
`label`, and exclusive pixel `box: [left, top, right, bottom]`. Optional `signal_state` and
`speed_limit_mph` supply reading labels. Matching is class-specific, one-to-one greedy IoU ≥ 0.5.
Missing inference counts as missed objects; unknown lights are not credited as correct colors.
Tracking identity switches are counted over matches no more than one second apart.

Warning scoring requires `hazards_complete: true` and a `hazards` array with `hazard_id`,
`session_id`, `kind`, `observable_from`, `deadline`, and `end`. Optional `acceptable_kinds` names
equivalent warning categories; optional `object_id` requires the warning's evidence to reference
a track matched to that actual labelled object. Every prediction frame must
be covered. Warnings are currently counted per annotated sample, not merged episodes; the
report states this. Without complete hazard labels, false-warning and missed-hazard results
remain unavailable. No model-quality results are inferred from unit-test success.

The optional `-qualification-driver` command-line flag enables a labelled simulation test
driver for physics/performance measurements. It is never a Comma fallback and cannot produce
a human/Comma comparison scorecard. Any deliberate manual input cancels it. The driving menu and
Scenario Tests do not enable it.

Diagnostics contains **World activity checks**. With the vehicle stopped, select traffic,
pedestrians, animals, buildings, parking, construction, bus, streetcar, railway or emergency.
The view follows that activity while the driver vehicle stays parked. C / Y returns to driving;
the observer continues to receive only the independent forward camera. Activity views close any
active scorecard and are labelled separately from comparison drives.

## Camera evaluation evidence

`scripts/qualify-perception.py` selects 43 COCO val2017 images before inference (seed 1049,
at least five image occurrences for each of the eleven supported COCO classes, no crowd labels,
CC BY 2.0 image-license metadata). With confidence 0.38 and IoU matching 0.5, it produced
64 true positives, 84 misses and 15 false positives: precision 81.0%, recall 43.2%. This small
class-stratified still-image set is not the COCO AP benchmark or a road/tracking validation.
Images, historical source photo links, hashes, labels and full outputs remain in the ignored cache.

The predeclared development sign views exposed oversized/back-facing sign text and a pale-sign
proposal mask that merged signs into blue sky. The renderer was corrected before valid evaluation.
The initial valid baseline found 48/120 objects and read no speed limits. Subsequent reader fixes
retain unknown for ambiguous OCR, trim word margins, reject saturated sky, and expire confirmation
across camera gaps. Baseline results are retained separately from development reruns.

A separate held-out set changes camera angle/offset, distances (8/16/28 m), daylight intensity,
and speed values (20/35/50 mph). Twenty stationary views, six samples each at 5 Hz, yielded
54 matches and 66 misses (45% recall, 100% precision on this very small rendered set).
Known signal-color accuracy including misses was 55.6%; speed reading accuracy including misses
and confirmation warm-up was 8.3%. Three speed-track identity switches occurred because OCR runs
less often than general detection. These are substantial limitations, not acceptable real-road
accuracy. Unknown/off lights are included but excluded from known-color accuracy. There are no
complete hazard labels here, so missed-hazard/false-warning/lead-time metrics remain unavailable.

The subsequent tracker keeps identity memory for 0.8 seconds across skipped detector cycles,
while returning only objects actually detected in the current frame. It never reports remembered
boxes as current observations. Model provenance distinguishes this version; the metrics above
describe the earlier tracker. A post-review rerun retained the same recognition/readout results
and reduced the three identity switches to zero. It reuses the reviewed images and is not a new
independent holdout. The original tracker outputs are preserved as `*-tracker-v1.json`.

Generate the held-out images with Unity's `CoDriver.Editor.PerceptionFixtures.CaptureHoldout`
batch entry point using D3D11 (not `-nographics`), then run
`python scripts/qualify-rendered.py --holdout`. The development fixture entry point is `Capture`.
`scripts/summarize-game-run.py --root INSTALLATION --session SESSION` reports throughput, contacts,
sampled overlaps, stationary intervals, activity states and service health separately. It never
labels a build as release-qualified.

Four predeclared warning probes (lead, pedestrian, dog and quiet road; 104 frames at 5 Hz)
produced 9 matches, 69 misses and 6 false detections. The general detector alone missed both the
lead-car and dog hazards; the pedestrian warning preceded its script checkpoint by 1.1 seconds.
One warning sample lacked supporting hazard evidence. The checkpoints are not real-world safe
reaction-time labels. Visual review confirmed the low-detail car and dog, and the stylized
clothed pedestrian; these views expose a substantial rendering/model domain gap. No Comma road
output was supplied in this probe. Results remain in `outputs/qualification/rendered-hazards`;
reproduce using Unity `CoDriver.Editor.HazardFixtures.Capture`, then `scripts/qualify-hazards.py`.
They do not establish reliable perception or real-road accuracy.

## Remaining release gates

The current realism implementation record is `docs/REALISM_REBUILD.md`; it records
newer results separately from the historical checkpoints below. The camera v2
contract now optionally carries `specific_force_device_mps2` and
`angular_velocity_device_rps` in forward/right/down device axes. These are measured
IMU values, not map or actor state. Stationary upright specific force is
`[0, 0, -9.81]`; a right turn has positive device-Z angular velocity. Unity samples
at the physics step, and the simulation-only Comma bridge maps these vectors to
the stock v0.9.7 raw sensor axes. Old recorded clips may omit them; the live Comma
adapter requires them and rejects invalid measurements. Neither vector grants
the observer any actuator access.

Cockpit settings allow a bounded seat-height/fore-aft adjustment and a 60–85 degree
driver-view field of view. Those settings move only the presentation camera;
the calibrated forward AI camera and its recorded pose stay fixed.

Lane-graph routes and lane changes, parking occupants, rail/streetcar boarding, bus merge/indicator
logic, and cockpit instruments are implemented. Verify them together without clipping or deadlocks,
including shared streetcar junction priority and curved-lane clearance. Finish animation/visual
consistency and end-to-end scenario resets, timing, criteria and replay coverage.

The isolated physics course, brake takeover, real service crash/restart and hard-exit cleanup,
recording-storage failure and continuous capture have the measured evidence above. Physical Xbox
reverse and disconnect/reconnect/pause/resume were explicitly confirmed by the user in build 16.
The later Xbox Comma request failed: nine button presses reached the game, but upstream health
errors prevented engagement. This failure is retained in `comma-controller-failure.json`;
it must not be described as a passed takeover check.
Labelled image, signal and hazard probes expose substantial missed detections and sign-reading
errors; improve and independently reevaluate these before claiming reliable recognition.

The complete 30-minute workload must pass world behavior as well as throughput. Installed
development packages contain an isolated Windows Python/runtime, observer models and notices;
`scripts/package-game.py` creates a new package without overwriting an earlier candidate.
Refresh the package manifest when rebuilding and rerun checks affected by the change.
The separate WSL Comma installation remains a prerequisite. This is not the approved first release yet.
