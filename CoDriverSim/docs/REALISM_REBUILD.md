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
The pooled-camera candidate meets the sustained rendering/inference targets, but
its long run exposed traffic and railway failures. Current work is correcting
those failures, qualifying the resulting application and preparing the eligible
public package. Visual acceptance and the final controller/Comma check remain open. Measurements below
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
