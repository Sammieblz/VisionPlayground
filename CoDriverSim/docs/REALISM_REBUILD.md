# Realism rebuild — implementation record

This continues the accepted Davis/Mercedes/living-town plan. The previous development
candidate is preserved. Passing its tests did not establish visual acceptance.

## Delivery gates

- [ ] Prepared assets: original sources, hashes, licenses, axes, materials, moving-part anchors and LODs.
- [ ] Continuous Davis surfaces, readable paint/signs, architectural detail, railway and bridge extension.
- [ ] Mercedes cabin/player, SUV dynamics, stock-Comma simulation configuration and Honda baseline.
- [ ] Forward-moving traffic, bounded contacts, complete nearby activity and animation cycles.
- [ ] Animated HUD companion, readable instruments, scalable Comma presentation and menus.
- [ ] Python/Unity regressions, physical courses, controller checks and replay/isolation checks.
- [ ] Complete-population 30-minute performance run, comparison captures and packaged verification.
- [ ] Public distribution: eligible asset manifest, notices, source and reproducible packaging.

## Preservation and distribution

Source before this pass is backed up under the ignored `.cache/realism-pass/` folder.
Downloaded tools, original assets and generated exports remain outside Git.
The supplied Mercedes is usable in the local prototype; its source page currently
lists personal use, so it is excluded from public asset distributions until redistribution
permission is established. CC0/CC-BY assets retain their individual provenance.

## Current work

Prepared art, the local Mercedes cockpit, HUD companion, bridge extension and
revised Davis streets are integrated. The populated contact/traffic regressions
and renewed physical turn review pass with the documented route exclusions.
An earlier pooled-camera candidate met the sustained rendering/inference targets,
but its long run exposed traffic and railway failures. The subsequent run had no
recorded player contacts, but exposed a pedestrian crossing blockage and camera
delivery below target. Current work is correcting those failures, qualifying the
resulting application and preparing the eligible public package. Visual acceptance
and the final controller/Comma check remain open. Measurements below
identify their exact candidates; an earlier candidate's results are not a blanket
pass for later changes.

## Measurements from the first packaged checkpoint

The preserved package `Realism-20261110-025150` has code identity
`36c698499fa3e3a3ec5e80255e17ca18c787b5ed27b826ede824bed55900515d`.
It predates the bridge extension and the subsequent facade performance corrections.

- Python: 79 tests passed; Ruff checks passed.
- Unity editor: 98 tests passed. GLS dry-course 0–60 mph **5.40 s** and 60 mph
  braking **41.79 m**. Honda and finite-contact regressions passed.
- Stock v0.9.7 read back GLS mass 2660 kg, wheelbase 3.134 m and steering ratio 18.7.
  In the packaged Cedar Grove session, an initial request failed on
  `controlsdLagging`; a later keyboard request engaged, drove approximately 6 m,
  and braking returned authority to the human. This is not a lane-keeping pass.
- Two **150-second, mostly stationary** 720p RX 580 checks: Cedar Grove averaged
  39.23 FPS, 19.73 Hz camera and 5.53 Hz detection; Davis averaged **11.82 FPS**,
  13.39 Hz camera and 6.49 Hz detection. These are not the required sustained run.
- Davis submitted about 209 million triangles and 8,574 draw calls per frame.
  Investigation found excessive distant facade detail. The next build removes
  unnecessary flat-wall tessellation, limits facade detail range, and uses the
  existing building shells for shadow casting. Results remain to be measured.

The fictional bridge loop is now being integrated with the same map definition
used by navigation, road meshes and the minimap. It remains subject to geometry,
physical clearance, driving and performance qualification. Supplied animated cat,
walking character and vegetation have been audited; they are not yet imported as
finished replacements. Public-build exclusion exists but still needs a packaging test.

## Optimized Davis checkpoint

Package `Realism-20261110-031808`, code identity
`7fcad1423e68428c53015980a08e480f3e698d3d48ff16b21bc84f9b350dfa17`,
ran a 150-second, mostly stationary Davis check with the observer and stock Comma
services enabled. Mean **30.72 FPS**, p95 **45.26 ms**, p99 **55.25 ms**;
camera **19.80 Hz**, detector **6.43 Hz**, sensor-to-observer p95 **346.9 ms**.
No warning was triggered, so warning latency was not measured. A late frame
submitted 12.06 million triangles and 2,349 draw calls, down from 209 million
and 8,574 in the failed checkpoint. This does not qualify sustained driving.

The bridge's physical approach tests passed at 8 and 26.82 m/s. Later visual
inspection found the truss was too narrow: its clear span is now derived from
the upright geometry, rather than the wide abutment bounding box. That visual
correction and the latest cockpit/storefront/civilian work postdate this package.

New storefront openings passed physical clearance checks. The supplied civilian
passed a PlayMode check for moving feet, forward orientation and stationary
animation root. Three populated Davis tests initially failed because their
playerless fixtures exposed a null player reference; a map-spawn fallback fixes
the error and their longer activity checks are being rerun. Failed reports are
retained in `outputs/unity/realism-playmode.xml`.

## Population regression follow-up

The next full PlayMode run (`realism-playmode-final-pass.xml`, despite its filename)
**failed** Davis transit, parking and ambulance seed 22. The focused parking and
seed-22 passes did not establish that the populated town was reliable. The failures
are retained; none is waived by the editor/Python passes (104 and 79 respectively).

Current fixes check the complete crosswalk before pedestrians enter, allow vehicles
holding a junction reservation to clear before yielding to an ambulance, and reserve
shop entrances for one visitor at a time. Storefront threshold geometry now joins an
actual sidewalk triangle rather than a nearby vertex. A separate PlayMode check
requires complete navigation paths from the sidewalk into every activity shop.

The earlier populated rerun exposed transit/alighting and parking failures.
Those failures led to the fixes and measured rerun below. The source and latest
visual work have not yet been packaged or subjected to the 30-minute performance
gate.

## Populated activity rerun and subsequent art work

`outputs/unity/realism-population-qualified.xml` records **10/10 passing PlayMode
tests in 850.24 seconds**, including ten occupied ambulance dispatch/return seeds,
a complete parking/occupant cycle, a bus passenger trip, storefront navigation,
bridge approaches and finite pedestrian impacts. The pedestrian recovery from an
off-sidewalk displacement is force-driven; it does not warp the actor onto the
navigation mesh. This is an activity regression result, not release qualification.

Subsequent changes need their own verification: the prepared cat with original
walk/run and two standing idle clips; a two-detail-level CarConcept traffic coupe;
varied facade windows/plaster; continuous rail profiles and ballast; water shading;
cockpit seat/FOV controls; and interruptible skeletal pedestrian impact/recovery.
Captures and failed predecessors remain under `outputs/qualification/realism/`.
The cat FBX round-trip reproduced 12 sampled bone poses with maximum error
0.000000923 m. That validates export transforms, not its in-game foot placement.

The stock Comma bridge audit found a sensor-axis error: yaw was published into the
raw event's Z component, and acceleration omitted the gravity response. Unity now
samples physical velocity at the 100 Hz physics step and sends optional explicit
device-axis IMU vectors. The adapter maps them through the unchanged v0.9.7 sensor
decoder. Device axes are forward/right/down; upright stationary specific force is
(0, 0, -9.81) m/s². Missing/malformed IMU fails closed. This is not yet evidence of
successful lane keeping; all stock engagement checks remain enabled.

The Python regression after this change passes **85 tests**, with Ruff checks
passing. `realism-imu-editor-rerun.xml` records **108/108** editor passes, including
the dry vehicle courses, IMU axes, prepared coupe and skeletal recovery checks.
`realism-imu-populated.xml` records **11/11** PlayMode passes in **815.83 seconds**,
including the cat ground/forward/walk check and ten populated ambulance seeds.
The latter's per-seed evidence is copied to
`outputs/qualification/realism/ambulance-imu-art-qualified.json`.

The candidate is now being rebuilt. Runtime Comma behavior, complete-workload
performance, public package and final visual/controller checks remain pending.
The package build writes a Unity packed-asset audit, and public packaging rejects
local-only assets. `scripts/verify-game-package.py` checks hashes and exercises the
bundled CLI from a temporary working directory outside the checkout. Activity
video capture is explicitly separate from performance qualification.

## IMU-corrected package and roadblock investigation

`Realism-20261110-062214` is a development candidate, not the default shortcut.
Its code identity is
`5693b7533557147d6d5e8eaf56b8bb31a4f111b68c0a6908797d2cb71c75f3f3`.
The packed-asset audit and isolated bundled-Python/CLI verification passed.

- A 240-second Cedar Grove run used genuine stock openpilot v0.9.7. Three separate
  engagements were observed: 1.3 seconds/4.3 metres (ended on stale connection),
  23.2 seconds/168.3 metres, and 15.6 seconds/80.9 metres. The latter two ended on
  human braking. These are short straight-road checks, not sustained autonomy.
  Mean rendering was 43.41 FPS; p95/p99 frame time 35.02/41.80 ms; camera 19.73 Hz;
  detector 6.22 Hz; warning latency p95 430.32 ms (15 samples). No player contacts
  were recorded. An activity video and ready/active screenshots are retained.
- A 300-second Davis run used the explicitly labelled qualification driver, not
  Comma. It travelled 1,404.47 metres without player collision; no sampled traffic
  or rail overlap was detected. Mean rendering was 30.22 FPS; p95/p99 frame time
  49.39/58.16 ms; camera 19.83 Hz; detector 6.33 Hz; warning latency p95 392.42 ms
  (69 samples). Three brief unrequested reverse-speed samples remain in the report.
  Neither run is the required 30-minute qualification or an Xbox test.

Those runs exposed long ordinary-traffic queues. Davis stop signs had been assigned
to nearby through roads even when their source OSM way was an omitted alley. The
compiler now records source parent roads and direction and assigns controls only
to matching playable lanes. It retains 101 assigned and 13 unassigned source stop
facts. Independent stop-sign dwell memories prevent alternating nearby signs from
resetting each other. No road geometry was changed by that recompilation.

In Cedar Grove, boundary speed signs stood at crossing roads, and some entrance
paths extended into avenue lanes. Their physical placement is corrected. Vehicles
already occupying a junction now complete the turn before pulling aside for an
ambulance. Subsequent diagnostics found a rail collider catching the curb, a
streetcar stopping at red after committing to a crossing, and a 30 cm unsupported
gap between road and sidewalk. Those are corrected. Both full-population
six-minute traffic checks pass, as does physical dog/cat crossing in both
directions. The earlier failed reports remain available.

The scene-rebuild regression exposed a native out-of-memory crash: generated
facade meshes/materials and navigation data outlived destroyed towns. Explicit
ownership now releases these resources without destroying imported shared assets.
The focused repeated-Davis cleanup check passes. After that fix the complete
PlayMode run completed without the crash: **14/15 passed**. The remaining failure
was a bus/ambulance deadlock in seed 22; its corrective retest is pending. The
editor run passed **111/111**; Python regression remains **89/89**.

A licensed low-rise model is attached to one compatible Davis footprint, retaining
the mapped collision shell. This is an initial architecture variation, not visual
acceptance for the district. Two clothed MakeHuman civilian variants are prepared
with public CC0 assets, pinned inputs and detail levels; Unity import/retargeting
qualification is pending. Blender previews do not establish in-game appearance.

Public packaging, complete-workload performance, final visual acceptance and
hardware checks remain open. The latest packaged candidate predates these fixes.

### Subsequent road and civilian checks

The bus sweep now reserves extra lateral room only while bending. Its previous
full-turn allowance extended down a straight exit and blocked an opposing vehicle.
The corrected seed-22 ambulance cycle passes; a later seed exposed a separate
raised footway crossing a road, trapping an ordinary car. The compiler now clips
raised footways against the road surface. The independent serialized-mesh audit
finds **0 m² raised pedestrian/road overlap**, with all **1,288** connection checks
still free of continuity/access errors. Road/lane/rail geometry is unchanged; map
landmarks and the sidewalk mesh differ from the earlier physical-review map.

Previously rejected physical turns remain excluded after regeneration even when
the old review hash is stale. That conservative retention is explicitly labelled;
it does not make the new physical review pass. The complete populated regression
is being rerun with these corrections and the restored restrictions.

The two MakeHuman characters are integrated with the existing finite-mass walkers.
Both have valid humanoid bindings, cloth/skin/hair textures, two detail levels and
no added collision shapes. **114/114 editor checks** and the focused two-character
walk/recovery check pass. Ground checks bake the actual mesh with FBX unit scaling,
rather than treating padded renderer bounds as shoe geometry. The texture adapter
also now distinguishes a linked diffuse texture from an unused Blender socket color.
New in-world 720p/1080p captures are retained under `visual/citizen_a-*` and
`visual/citizen_b-*`. These are presentation captures, not a performance benchmark.

### Populated regression and physical map review, 10 November

The final populated PlayMode run passed **17/17** checks, including all ten Davis
ambulance seeds (21–30), six-minute ordinary traffic runs, finite-mass contacts,
animal crossings, transit/parking activity, scene cleanup and character animation.
The earlier failing reports remain available. A pedestrian/car circular wait was
resolved with a bounded, navigation-constrained walking detour around stationary
vehicles. The pedestrian retains its destination, yields if the vehicle moves,
and neither teleports nor pushes the car aside. A focused physical test verifies
completion with less than 15 cm of handbraked-car displacement.

The fresh Unity physical audit drove **847** enabled car/bus turn paths. All reached
their exits; two bus paths exceeded the pavement footprint tolerance. Their three
internal segments are now excluded for buses, retaining all earlier restrictions.
The remaining **845** paths passed. A separate runtime routing comparison confirms
that the scheduled bus route is unchanged. The reviewed map SHA-256 is
`15b53ebd8c65d7af42454c3c524fed0af4688a956111b375aa6d6f165cd6412a`.
The geometric compiler audit still reports no continuity/access errors across
1,288 connections and no raised sidewalk/road surface overlap. These checks do
not certify the source map's real-world rules or eliminate all dynamic conflicts.

The final editor run passed **114/114** after those restrictions and a rendering
change: small cockpit mirrors now cull detailed facade attachments beyond 45 m,
while their building shells remain visible to 120 m. Main and AI cameras retain
normal facade detail. Any performance gain remains unmeasured until the new
packaged application is tested. A scene inventory is an estimate of visible work,
not a GPU benchmark.

Evidence is retained in `outputs/unity/realism-detour-populated.xml`,
`outputs/unity/realism-build-editor.xml`, and under
`outputs/qualification/realism/` in `map-physical-current.json`,
`bus-route-restriction-review.json` and `scene-render-inventory.json`.
Python regression remains **89/89**. The complete-workload 30-minute run,
public-package verification, visual acceptance and final Xbox Comma/takeover
check remain delivery gates; passing these automated regressions does not finish
the requested overhaul.

### Packaged hotspot measurement

The isolated local package `Realism-20261110-091604` passed file integrity and
bundled CLI verification outside the checkout (2,604 files). Build code identity:
`58c674e486eef3a842a221d37e1930e5f5cd5fb0c1bd33e031b410af51353ef5`.
Its 180-second stationary Davis cockpit check, session
`3f33b565de3e4a119a94941f0ec291db`, **failed the rendering target**: 25.15 FPS,
p95/p99 frame times 54.65/63.86 ms. Camera delivery was 19.72 Hz and general
detection 6.42 Hz. Observer latency p95 was 357.54 ms; there were no warning
samples, so warning latency is unavailable. This was not a driving qualification.

The starting area contained 34–35 nearby people. Nearby moving vehicles peaked
at nine but averaged 3.54 in the sampled interval. Some queues were legitimate
signal waits; one car waited behind the stationary player in its lane. This
measurement does not establish the six-moving-vehicles activity target. No sampled
traffic or rail overlap and no unrequested reverse-motion samples were recorded.
Actual cockpit captures show working mirror images, the mounted Comma screen,
dashboard and HUD companion. Visual acceptance remains separate.

Rendering samples indicate CPU cost above GPU cost. A subsequent candidate is
testing URP's GPU Resident Drawer while preserving population, camera frequency
and physics. Its shader variants are retained explicitly and static batching is
disabled as required by that rendering path. The unsupported built-in-renderer
`layerCullSpherical` setting was removed from the mirrors. Performance improvements
must be measured in the resulting application before acceptance.

The GPU Resident Drawer experiment was **rejected** after its equivalent packaged
180-second check: 20.69 FPS, p95/p99 62.21/74.79 ms, camera 19.09 Hz and detector
6.24 Hz. Session `00d8621737084f9f9834aaf679ed8d1c` belongs to preserved package
`Realism-20261110-094136` (code `f83eeea497ea11ca813a3e6d453310274eefc29697947d3d27a30d7137d92eb3`).
It rendered correctly but cost more CPU time on this scene. The default rendering
path and its prior batching settings are restored. The next candidate uses two
shadow cascades across the unchanged 90 m shadow range and skips off-screen
mirrors while retaining the cadence of visible mirrors. Auxiliary-camera CPU
submission timings are recorded separately from GPU timing for diagnosis.

The run reviewer now requires 1,800 seconds of advancing recorded simulation
time. A long initial loading frame or a pause cannot satisfy the duration gate.
Aggregate frame statistics still include startup; short-run figures above must
not be described as steady-state-only measurements. Fresh physical-course evidence
in `outputs/qualification/realism/physics-current.json` records **19/19** passing
cases from the final editor test: GLS 0–60 mph **5.40 s**, 60 mph braking **41.79 m**,
with input/test-result hashes. These remain game calibration results.

The two-cascade/visible-mirror package `Realism-20261110-095402` measured **26.81 FPS**
including startup over 180 seconds, p95/p99 52.50/66.67 ms, camera 19.61 Hz and
detector 6.48 Hz (session `7c8928f9dcd94b18876c2987501a7369`). This improves the
short aggregate result but is not a sustained performance pass.

The next camera transport uses pooled, explicitly owned pixel buffers, replacing
a fresh 1,746,768-byte allocation per RGB frame. The latest-frame queue releases
superseded buffers; completed writes, malformed responses, shutdown and late
readbacks release their buffers as well. The wire sends only the valid image
length. **117/117 editor checks pass**, including three new buffer-ownership/socket
failure checks. Minimap sampling now uses filtered mipmaps to reduce jagged edges
during rotation and zoom; map geometry and navigation are unchanged. Sustained
performance was subsequently measured below; visual acceptance remains pending.

### Sustained workload and reproduced failures

Package `Realism-20261110-100923`, code
`ad91c2d2aae002d03ebaa72268e54cc572cf72bac164b94f593f4243b35c22e5`,
completed session `f675d024366a4a429cd407bf21ebdcd5` at 1280×720 with the default
population, observer and official Comma services enabled. Its explicitly labelled
qualification driver was driving; Comma automation was not engaged.

| Measurement | Result |
|---|---:|
| Advancing recorded simulation | 1,817.58 s |
| Mean rendering rate | 40.55 FPS |
| Frame time p95 / p99 | 40.91 / 55.41 ms |
| Camera delivery | 19.74 Hz |
| General detection | 6.26 Hz |
| Warning latency p95, 867 samples | 387.43 ms |
| Comma healthy samples | 99.06% |
| Recorded driving distance | 10,086.59 m |

This is a **performance pass, not a gameplay/release pass**. The run recorded
four railway-gate contacts, one permanently stopped ordinary car and a 317.65 s
bus wait. No traffic/rail penetrations were found in the once-per-second samples;
that sampling does not prove absence of every transient contact. Emergency and
parking activity completed. The actual cockpit capture shows the filtered map,
mirrors, instruments, mounted model visualization and HUD companion.

The 5th Street car stop was reproduced at its recorded pose: an inner lane's
signal pole intruded into the outer lane, capping the planner at 0.06794 m/s.
Disabling that exact pole cleared the query. A shared roadside mast with separate
overhead lane heads now clears the same pose without removing signal phases.
Before/after evidence: `outputs/qualification/realism/traffic-stop-audit-before.json`
and `traffic-stop-audit.json`.

The bus was waiting for a turning truck; the truck's straight nose query pointed
into the stopped bus while its curved route was clear. A checked, walking-speed
steering-arc continuation is under physical regression. The gate contacts exposed
a missing advance rail-warning stop in the Davis test/NPC path. Both now consider
the train warning and the arm's reopening clearance. Static obstacles are also
checked along the curved braking horizon. These changes require fresh regression
and sustained measurements; the preceding run is retained as failed gameplay
evidence rather than relabelled after a source change.

Source is published in draft PR [#2](https://github.com/Sammieblz/VisionPlayground/pull/2).
The CoDriverSim wheel/CLI/Python checks passed on Windows and Linux with Python
3.11 and 3.12 in GitHub Actions. The unrelated pre-existing EyePause workflow is
outside this change. A public binary release and final visual/controller checks
are still pending.

### Traffic corrections and physical follow-up

The mapped signal change passed **120/120 editor checks**, including bus-sized
clearance at every incoming signal lane and the exact 5th Street stopped-car pose.
World diagnostics now record the planner's actual clearance blocker per vehicle.
The subsequent populated suite passed **18 of 19 CoDriverSim PlayMode cases**,
including all ten ambulance seeds, both towns' ordinary traffic, Davis boarding/
alighting and parking, animal/civilian animation and finite-contact regressions.
Its one failure was the newly added railway stop fixture: a requested two-metre
margin left only 0.41 m of physical clearance after progressive braking settled.
The upstream Input System also ran two passing integration cases and skipped two
of its own mouse tests; those are not counted as project verification.

After increasing the rail approach allowance, a focused **3/3 PlayMode** follow-up
passed: Honda and GLS stop/reopen/resume, plus the recorded truck/bus conflict with
a waiting-bus displacement limit of 0.15 m. At near-stop (0.149 m/s), Honda front
clearance was **2.39 m** and GLS **1.87 m**, both with zero contacts. Both remained
clear while the arm rose and then resumed. The truck physically travelled 15.07 m
through its turn without penetration or pushing the bus aside. The earlier failed
suite is preserved; it is not rewritten as a full passing run of the later source.

Evidence: `outputs/unity/realism-traffic-editor.xml`,
`realism-traffic-populated.xml`, `realism-rail-margin.xml`, and
`outputs/qualification/realism/ambulance-traffic-qualified.json`.
Local/public packages and a new sustained run are the next gates.

Original project code now carries an MIT license. The Python wheel declares the
MIT/Apache combination because its adapted MetaDrive shader retains Apache-2.0;
the wheel and Windows package include the corresponding texts and upstream
notices. Existing pinned dependency versions are unchanged.

Both traffic-corrected profiles built with code identity
`6ba37c4fb9c1331fc324306eec842e7fb56d796fd3e634926c22be4aabeab9d6`.
The isolated local package is `Realism-local-20261110-112822`; the public package
is `Realism-public-20261110-113200`, with 2,305 packed source assets and zero
local-only assets. Both passed integrity and isolated CLI verification.

The public 180-second launch check, session `225a9f3d95664512b4e704e4333bf435`,
recorded no player contacts or sampled traffic/rail overlaps. Its startup-inclusive
mean was 28.64 FPS, camera delivery 19.11 Hz and detection 6.33 Hz. It is not a
sustained performance pass. The cockpit capture exposed the fallback's legacy
floating readout; it is being replaced with the same live Canvas instrument
cluster used by the local cabin, in a dashboard housing with neutral matte
upholstery. The qualification driver also now uses the selected profile's
wheelbase, replacing its Honda-only constant. Neither change modifies Comma's
upstream models or control logic. Both profiles are being rebuilt before delivery.

### Packaged cockpit review and crossing blockage

The next packages, `Realism-local-20261110-114821` and
`Realism-public-20261110-115250`, share code identity
`b706b03d88ebaf1828d796a38ac1896f6405f68a021b33496b07a70557ca852d`.
Both passed package integrity and isolated CLI checks. Public launch session
`6264820ded1a421998b68481ecb96bef` completed 180 seconds with no recorded player
contacts or sampled vehicle/rail overlaps. Startup-inclusive rendering was
26.69 FPS, camera delivery 19.01 Hz and detection 6.30 Hz. Its cockpit capture
exposed overlap between the new instrument housing and the Comma device. Their
public-car anchors are being adjusted; the Mercedes uses independent anchors.

The attempted sustained GLS run, `c2c58f51f5834d61a09b5ae7a2c1c44b`, was ended
early after a long crossing blockage appeared. It recorded 879.78 seconds of
advancing simulation, 30.17 FPS including startup, p95/p99 frame times
59.91/83.70 ms, camera delivery **18.92 Hz**, detection 6.25 Hz and warning
latency p95 464.07 ms across 415 warning samples. It had zero player contacts
and no sampled vehicle/rail overlap, but **failed qualification**: car 9 waited
201.55 seconds, car 21 behind it 197.40 seconds, and the bus behind that queue
129.20 seconds. Comma services were enabled; the labelled qualification driver
was driving. This is neither a 30-minute pass nor Comma driving evidence.

Person 49 stopped in the crosswalk at (-79.7173, 0, -95.3962), in front of car 9.
The exact map has a complete navigation path to the far sidewalk, but the direct
collision sweep hits that car's nose. An isolated physical reproduction cleared
in 15.62 seconds with the previous code, so the static pose alone does not prove
the cause of the long-run stall. A follow-up adds same-side detours, crossing
progress recovery, completion based on physical arrival, and stopped-crossing
navigation diagnostics. Focused fault-injection and populated runs must establish
whether those changes resolve the actual failure.

`scripts/archive-game.py` now creates a public ZIP and SHA-256 sidecar only from
a redistributable package's verified manifest. It excludes runtime recordings,
logs and unlisted files, rejects changed bytes and local-only art, and never
overwrites an existing download. Eight focused archive tests passed, followed by
the complete Python/CLI matrix on Windows/Linux and Python 3.11/3.12. This does
not waive the outstanding game qualification. `scripts/visual-review.py --run
PACKAGE SESSION LABEL` adds original packaged screenshots, measured results and
build identities to the local visual review page.

The crossing follow-up passed **4/4 focused PlayMode checks**, including a lost-
navigation fault at the recorded Davis car nose. The pedestrian physically reached
the far sidewalk in 9.34 seconds without penetrating the car, snapping position,
or moving the held vehicle more than 0.15 m. The other cases exercise a stopped
car detour, an occupied bus crossing, and impact interruption. Evidence:
`outputs/unity/realism-crossing-recovery.xml`. A fresh populated run and packaged
long run remain necessary; this fixture alone does not prove the long-run cause.

The pine's pale edges were traced to white RGB in transparent texture pixels.
Foliage import now pads visible colors into the transparent area and preserves
alpha coverage in mipmaps, including already imported cached textures. Actual
24 m and 65 m Unity captures are retained before and after the change under
`outputs/qualification/realism/visual-before-material-pass/` and `visual/`.
No source texture was repainted or screenshot enhanced. Public cockpit anchors
and the activity-tour label also changed and still require packaged review.

The subsequent populated follow-up passed **6/6 PlayMode cases** in 1,135 seconds:
animal crossings in both directions, ordinary traffic in both towns, Davis bus
passenger travel, a complete parking occupant/vehicle cycle, and all ten ambulance
dispatch/return seeds (21–30) with temporary blocked access. The bus covered
2,511 m, visited three stops and completed boarding/alighting. Parking physically
departed, searched and returned. Evidence: `outputs/unity/realism-crossing-populated.xml`
and `outputs/qualification/realism/ambulance-crossing-qualified.json`. These are
accelerated behavior tests, not the real-time rendering/inference qualification.
New local and public packages are being built from this follow-up.

Those packages are `Realism-local-20261110-125442` and
`Realism-public-20261110-125938`, sharing code identity
`9d944517f28121c2a84e4ff9275c13a51927cc53925a0eab8e3c95c5aeb97d05`.
Both passed integrity and isolated CLI checks; the public packed-asset audit
contains 2,340 source assets and no local-only assets. The Python/wheel/CLI matrix
also passed all four Windows/Linux, Python 3.11/3.12 jobs for source `634f445`.

Public session `23d8740d6a694a94b761fdbf36548fea` completed its 200-second
presentation check with zero recorded player contacts, no sampled traffic/rail
overlaps, and no two-minute vehicle wait. Startup-inclusive performance was
**26.50 FPS**, p95/p99 **60.56/79.95 ms**, camera **19.13 Hz**, detector **6.33 Hz**,
and warning latency p95 **455.85 ms** over 41 warning samples. It is not a
performance pass. Its screenshot showed the public wheel/decorative display
obscuring the cluster, despite resolving the separate Comma-device overlap.

The following presentation change moves the cluster above that obstruction and
disables the replaced concept-car display. The cockpit Comma UI now renders as
a world-space Canvas on the device, removing a redundant auxiliary camera pass
while keeping the same read-only layout/model-overlay component. A focused
PlayMode camera-isolation check passes. That test first exposed a null-reference
error in the existing service-free UI fixture path; null-owner initialization is
now supported again, with the companion shown disconnected. The failed test is
retained as `realism-cockpit-isolation-before.xml`; the passing follow-up is
`realism-cockpit-isolation.xml`. No dynamics or traffic changes are in this pass;
actual presentation and performance still need renewed packaged measurements.

The updated service-free captures completed for all seven Comma layout states at
720p and 1080p. The public instrument inspection now shows speed, gear, mph and
RPM above the dashboard without the decorative panel covering them. These are
labelled inspection/layout fixtures; the next packaged captures must verify the
new direct Canvas in the running application before delivery.

The direct-Canvas candidates are `Realism-local-20261110-131707` and
`Realism-public-20261110-132121`, code
`b97a59b8a533fee47ee930cd83d021512d29d020d2a1710aef24490523f65b77`.
Public session `9b0bf0d0c6a44f36b284fda5823ee876` recorded **29.42 FPS**,
p95/p99 **55.84/69.69 ms**, camera **19.66 Hz**, detector **6.22 Hz**, and warning
latency p95 **387.80 ms** across 42 warning samples, including startup. It had no
recorded player contacts, sampled traffic/rail overlaps or two-minute vehicle
waits. This is a short comparison, not a 30-minute qualification. The Python CI
matrix passed for `04da31e`.

Its packaged capture verified the readable instrument cluster and direct device
Canvas, but exposed model road-edge lines drawing beyond the device. The camera
viewport now clips its children. Two focused PlayMode cases pass: observer-camera
exclusion and a rendered pixel comparison requiring an oversized model path to
draw inside the screen without changing pixels outside it. The test also exposed
a missing explicit CanvasRenderer dependency on the companion in the editor;
that dependency is now declared. Evidence: `realism-cockpit-clipping.xml`, with
the earlier renderer-dependency failure retained in `realism-cockpit-clipping-before.xml`.
The combined long run is still pending this final presentation rebuild.

The clipped-display packages are `Realism-local-20261110-133433` and
`Realism-public-20261110-133910`, code
`770bc026817d7a38fcd4ad92d828b75b6379238966f65719db248b49c5dd0345`.
Both passed integrity and isolated CLI verification. The four-job Python
matrix passed for `1a64af7`. Public session `7c9f4d1093414123b7460e9c68b61c56`
completed 200 seconds with zero player contacts, no sampled traffic/rail overlap,
and no two-minute vehicle wait. Its actual cockpit capture shows readable speed,
gear and RPM, the mounted Comma display and the animated HUD companion.

That short run measured **27.54 FPS**, p95/p99 **60.35/95.52 ms**, camera
**18.98 Hz**, detection **6.26 Hz**, and warning latency p95 **352.50 ms** across
41 samples. It does not pass the performance target. The local sustained run is
in progress; neither the rendered-pixel clipping test nor a short screenshot
check substitutes for that result. Public packaging also now includes the
previously omitted MakeHuman core-asset provenance manifest and a controls guide;
these packaging-only changes do not alter the measured game binaries.

### Interrupted sustained run and stale service measurements

The local session `120e9204f91c4377818089da7d9cd9ea` is **not qualified**.
The model stream stopped updating near application time 1,074 seconds, before
the later manual controller testing and map changes. The observer process exited;
Comma subsequently stopped when camera delivery ceased. The original observer
launcher discarded stdout, so the precise exit cause is unavailable. The prior
`f675d024366a4a429cd407bf21ebdcd5` and `c2c58f51f5834d61a09b5ae7a2c1c44b`
recordings retain fresh model output at their ends; this failure does not establish
that their services stopped in the same way.

This exposed a reporting defect: diagnostics retained the last command's healthy
flag, and the performance report reused the observer's last reported rates.
The actual control arbiter still rejects commands older than 250 ms. The new
reviewer checks recorded model/command freshness and map continuity; missing
whole-session throughput evidence cannot pass its rate gates. The original report
is preserved as `run-review-120e9204f91c4377818089da7d9cd9ea-before-freshness-fix.json`
under `outputs/qualification/realism/`. Its corrected review measures fresh model
output in only **54.7%** of samples. The aggregate 41.88 FPS includes the period
after inference stopped and later manual driving; it is not the required combined
workload result. Manual testing visited both maps, and the advancing world-time
interval was 1,775.04 seconds.

The bus also waited **149.85 seconds** at the E Street turn, across signal waits
and occupied turn clearance. It physically resumed two seconds after the recorded
stationary interval; it was not permanently stuck. The original two-minute review
threshold remains failed. The queue history is retained in `bus-long-wait-review.json`.
No ordinary vehicle reached that threshold, and no traffic/rail overlap was found
in the sampled world rows. These observations do not waive the failed gates.

The source follow-up measures received/detected frames across the full session,
including service downtime and restarts; expires diagnostic readiness by command
age; records observer stdout/stderr, transport errors and an exit-reason JSON;
and discards obsolete GPU readback completions before the strict ordered camera
connection. Out-of-order completion is a defended failure case, not the proven
cause of this shutdown. **100 Python tests pass**, including persistent transport
exit evidence and stale-health reporting. The focused Unity measurement, buffer
and control suite passes **26/26** (`realism-service-measurements.xml`). The user
has finished testing the untimed game; rebuilt local/public applications and
renewed sustained/recovery checks are next. No root-cause or release pass is claimed.

The follow-up packages are `Realism-local-20261110-142937` and
`Realism-public-20261110-143601`, code
`6e6510600df26eb9fb14c0b2b442c33b578cc68eb5eba32fd3fff481e9dc6400`.
Both passed integrity/isolated CLI checks, and all four Python CI jobs passed for
`c65bc83`. In public session `be5f611926ab4f45918ea363bb0374d3`, the fault probe
terminated only the verified game-owned observer. Recorded observer/command health
became false; the Diagnostics restart buttons restored observer generation 2 and
fresh model output. The recovered stream remained live through application time
165 seconds. This intentional-outage check is not a performance qualification.

Its visual review exposed a separate capture defect: Windows `PrintWindow`/Pillow
`window=` could return an old D3D surface after the game's state changed. Those
screenshots and the older window-method video remain preserved but are excluded
from current visual/motion acceptance. Internal Unity screenshots are unaffected.
Capture now verifies the foreground owner and reads the visible client rectangle,
stops on focus loss, and records distinct frame counts and completion/error state.
The Diagnostics page also now hides stale rate/latency values and labels valid
rates as session averages. The brief queued local attempt was stopped to rebuild
these presentation corrections before another long run.


### Sustained service recovery follow-up

Packages `Realism-local-20261110-144828` and `Realism-public-20261110-145311`
share code `8edc9a95f94fd53c6d1bca16218e49b2027d757160d92deabfc64ae7c637be97`.
Both passed integrity and isolated CLI checks. All four CoDriverSim Python CI jobs
passed for `83b60a9`. Public session `2e78836886e04b0db50ef02e37781873` passed
the owned-observer fault/restart check; verified foreground captures show stale
rates becoming unavailable, restored service status, and the return to driving.
Its deliberate outage is excluded from performance qualification.

Local session `765cfb71a0d648c7b3dd5c3d70b7e887` completed **1,876.53 seconds**
of advancing Davis simulation and **10,039.63 metres**, with no controller input,
pauses, service restart or recorded player contact. It measured **32.37 FPS**,
p95/p99 frame time **56.84/77.49 ms**, whole-session camera delivery **19.17 Hz**,
and detection **6.33 Hz**. Warning latency p95 was **422.56 ms** across 978 samples.
Fresh observer/model/healthy-command coverage was **99.91% / 99.07% / 99.14%**.
The earlier observer shutdown did not recur, but its original cause remains unknown.

No sampled traffic/rail overlap or two-minute vehicle wait was found. The longest
bus wait was **115.32 seconds** and cleared; the longest ordinary wait was **79.06
seconds**. Five complete parking visits, six bus stops, 49 train stops and complete
emergency phases were observed. Pedestrians at the busy shop crossing sometimes
congested and later cleared; this run does not establish polished animation or the
required nearby population at every test hotspot. The actual midpoint screenshot
shows readable local instruments, mirrors and the clipped mounted Comma display;
facade repetition remains visible. Stock Comma supplied live model/control data,
while the explicitly labelled qualification harness drove the car. This is not a
Comma autonomous-driving result.

**The camera-rate gate failed** (19.5 Hz is the recorded near-20 tolerance), so
this is not complete performance qualification. There were 36,831 captured/submitted
frames versus 36,041 received, and no GPU queue-capacity drops. The follow-up
replaces the single pending transport slot with at most two waiting frames,
discarding frames queued longer than 100 ms and waking the worker on submission.
This is intended to absorb GPU completion bursts without obsolete-frame buildup.
It adds separate transport-drop measurements and burst/expiry/ownership tests;
its measured benefit remains to be verified in new packages. No threshold is relaxed.


The delivery follow-up passes **29/29 focused Unity checks** in
`outputs/unity/realism-camera-delivery.xml`, including a real socket response held
while two further frames arrive, bounded overload, age expiry and buffer cleanup.
Its local/public packages are being rebuilt; the rate improvement is unmeasured.

Two complete 220-second activity-view recordings are retained as
`activity-davis-status.mp4` and `activity-cedar-status.mp4`, with identity/timestamp
sidecars and H.264 browser copies. The game camera changes activity targets; the AI
still receives the vehicle camera. Foreground desktop capture sampled only
**3.41 / 3.39 Hz**, despite 15 FPS encoding, so repeated frames preserve wall time
and limit animation assessment. The optional pinned MSS 10.2.0 backend is being
compared; its first short probe competed with the Cedar recorder and is not a
clean speed comparison. The review page now explicitly labels actual sampling.
Frames show the remaining primitive construction machine and repeated facades;
these are unresolved visual defects, not first-release acceptance.


### Articulated construction follow-up

The primitive excavator is replaced by the CC0 Innerscene compact tracked model,
pinned in `game/construction-manifest.json`. Its 928 mesh objects are prepared as
five articulated sections with two detail levels (106,568 / 29,835 triangles),
transparent cab glazing and runtime telescoping hydraulics. The seated MakeHuman
operator uses the existing Quaternius driving animation with hands fitted to the
measured controls. Source/pin geometry and modifications remain in the preparation
report; the 3,600 kg body mass and timings are simulation estimates.

Actual Unity captures (`visual/construction-00-720.png` through the cycle) show
the prepared machine. Three focused checks pass in `realism-construction-physical.xml`: public
provenance/detail levels, the full 16-second cycle without rod detachment or
ground penetration, and two cycles of physical settling followed by a finite
impact response. No activity code resets the machine's physical root. The Python
regression remains 100/100. Both packaged profiles are being rebuilt; these checks
do not establish whole-town visual or performance acceptance.

The prior queue-only public recovery probe (`b1f71beccb574c19883e69cb4f8df6e4`)
restored generation 2 and kept the stream live, but its pre-injection checkpoint
was already stale. It is retained as recovery evidence, not a clean healthy-to-failed
transition proof. The probe now requires consecutive fresh samples after opening
Diagnostics and immediately before injection. A clean 67.22-second post-recovery
window received 19.97 camera frames/s and 6.11 detections/s; that stationary short
window does not replace sustained qualification of the bounded queue.


### Physical sidewalk journeys

Review of the sustained session found several extra citizens travelling only
10?50 metres in 30 minutes. Wandering used world-X offsets even on angled Davis
streets, and nearest-surface spawning could choose disconnected corner patches.
A new physical test reproduced the inability to obtain a walking route. Connected
spawn selection and complete sidewalk routes restored movement; subsequent tests
exposed endpoint oscillation and a path brushing building `way/45208365`.

Davis navigation now rasterizes the same building wall mesh used by PhysX, including
its door openings. Final-segment locomotion brakes using physical distance, retaining
bounded force, vehicle checks and pedestrian clearance. Visits select reachable,
available entrances and use a cooldown instead of crowding three shared queue points.
All eight previously stranded test actors now physically complete 9?25 metre journeys
in the populated world; all mapped shop access paths remain complete. Evidence:
`realism-sidewalk-walls.xml` (2/2), with earlier failed route/contact checks retained.
The wider crossing/traffic/transit/parking/ten-seed emergency suite is being rerun.

The earlier moving qualification route had at most 11 people within 200 m, while
a static central cluster contained approximately 35?36. The harness starts away
from the normal player spawn. Those counts do not validate the two-minute downtown
hotspot activity target; the new mobile population needs fresh packaged observation.

Construction packages `Realism-local-20261110-161424` and
`Realism-public-20261110-161806` share code
`83a76d19de1e097089520a348f8d59d7ecc9fc364739c657c47b935a196c0d55`;
both passed integrity and isolated CLI checks. All four Python CI jobs passed for
`fe3d506`. These packages predate the sidewalk changes above.

### Street furniture and sustained contact recovery

The wider walking rerun (`realism-walking-world.xml`) passed 10/12 cases, including
all ten ambulance dispatch/return cycles, transit, parking and crossing fixtures.
Both ordinary-traffic checks failed. Detailed traces showed a pedestrian held in
continuing contact with a Cedar truck and a Davis cat physically blocked by a
signal pole while vehicles correctly yielded. The initial suspicion about signal
reservations did not explain these recorded failures; reservation logic is unchanged.

Continuing vehicle contact now removes locomotion directed into the body without
repeatedly freezing steps away from it. Small static street furniture is included
in the navigation bake using conservative physical bounds. A held-truck fixture
uses explicit handbraking rather than the NPC route controller during the contact
check. The original failing fixtures and traces are retained.

`realism-furniture-contact.xml` passes 3/3: the recorded truck contact clears without
moving the handbraked vehicle more than 0.15 m, the cat completes its recorded
crossing around the pole, and eight sidewalk journeys still physically complete.
The populated traffic, shop access and activity checks require a fresh rerun.
Python CI for `ab39485` passed all four Windows/Linux, Python 3.11/3.12 jobs.

### Architectural finishes and street-facing shop fronts

The next visual pass adds three hash-pinned CC0 material sets (12 source maps at
2K), a stable six-finish facade palette, painted modeled joinery, street-facing
retail glazing with frames and awnings, and parapets/bitumen for flat roofs.
Pitched roofs retain their tile finish. Near and distant wall materials use the
same stable map identity. The licensed Poly Haven architectural profiles remain;
these are adapted appearances, not claims about the surveyed Davis buildings.

Matched 720p/1080p Unity captures are under `visual/`; the previous captures are
preserved under `visual-before-architecture-pass/`, and the first finish attempt
under `visual-architecture-first-pass/`. The lighter stucco follow-up improves
wall/trim separation, and the commercial ground floors now differ from upstairs
windows. Repetition, empty-looking glazing and sparse landscaping still limit
realism. These captures establish the actual rendered change, not whole-game
visual acceptance. Whole-population behavior and sustained rendering/inference
qualification remain necessary after this pass.

### Storefront landing qualification

The initial architectural editor rerun passed 128/129 checks. Its storefront
fixture still invoked the old building signature. After correcting that call,
a stronger floor check first exposed the fixture's missing mapped sidewalk, then
an actual unsupported approach at `way/45208404`. The original single diagonal
threshold could miss the straight-ahead door waiting point.

Landings now follow the door axis before joining the mapped sidewalk, with matching
world-aligned concrete UVs. The isolated physical check verifies closed-door
collision, open-door clearance and floor support at every mapped activity entrance;
`realism-storefront-landing.xml` passes. Earlier failures are retained. The complete
129-case editor suite and all 23 populated-world cases are being rerun before the
new local/public packages are produced. All four Python 3.11/3.12 Windows/Linux CI
jobs passed for `1613b56`; those do not qualify Unity behavior.
