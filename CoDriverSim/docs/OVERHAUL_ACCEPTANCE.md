# Physics, Davis and cockpit acceptance

This checklist covers the approved overhaul. Source implementation, isolated checks,
packaged driving and physical controller checks are separate evidence. The application
remains a development candidate until the combined qualification is complete.

The evidence table below records the earlier overhaul candidate. The active realism
rebuild changes assets, pedestrian presentation, map surfaces and traffic clearance;
its current results and open gates are tracked in [REALISM_REBUILD.md](REALISM_REBUILD.md).
Earlier seven-test and turn-audit passes do not qualify that changed build.

## Active rebuild status

| Gate | Latest evidence |
| --- | --- |
| Pedestrian arrival | Commit `353c0d1`: the simple arrival and recorded Davis endpoint fixtures fail before the fix; all six focused arrival/curb/pedestrian checks pass afterward. |
| Stationary vehicle physics | Commit `f52b437`: 24 vehicle-physics tests pass, including five ten-minute braked-hold cases and finite impact response. The original level truck drifted 6.41 m; the corrected cases report no drift at logged precision. The complete Editor suite passes 144/144. |
| Sustained rendering and inference | Package `Realism-local-20261110-223436`, before those two fixes: 1,878.04 advancing simulation seconds, 35.73 FPS, p95/p99 46.53/55.34 ms, camera 19.95 Hz, detection 6.20 Hz, warning p95 383.55 ms. No pauses, player contacts or transport drops. |
| Sustained world behavior | That same run fails: unfinished pedestrian crossings block a bus, and stationary truck drift blocks a junction/ambulance. The fixes pass all 32 PhysicalWorld cases, including ten ambulance seeds. A separate railway-pedestal regression exposed and corrected a late turning-lane obstruction; 145 Editor and five affected world checks pass afterward. A new packaged sustained run is still required. |
| Visuals and nearby activity | Fresh Davis/Cedar recordings and captures remain required. Earlier recordings missed the nearby-moving-vehicle count. Static captures and population presence alone do not establish complete animation cycles. |
| Comma and controller | Stock v0.9.7 remains intact. The final physical Xbox X-request/LT-takeover check remains open. Qualification-driver performance runs do not measure Comma autonomy. |
| Distribution | Source is on draft PR #2. Earlier eligible-package checks do not qualify the new runtime; no binary release is published. |

## Earlier overhaul evidence

| Gate | Evidence and current status |
|---|---|
| Finite contacts and locomotion | Person/dog/cat cannot continuously displace the handbraked car in the 15-second physical course. Impact reactions, curb stepping and no navigation pose snapping have focused checks. |
| Driving calibration | Latest 100 Hz course: 0–60 mph 10.60 s; braking from 30 mph 10.76 m; from 60 mph 42.10 m. These are game calibration measurements. |
| Bridge and suspension | The failed 60 mph approach was reproduced in `overhaul-bridge27.xml`. Contact-plane damper velocity fixes it: both directions at 8 and 26.82 m/s pass without chassis contact in `overhaul-bridge28.xml`. |
| Input and control authority | 97 editor checks pass in `overhaul-regression29.xml`, including request rejection/expiry, stale commands, takeover, reverse and device-loss handling. |
| Populated world | All seven PlayMode tests pass after the suspension change: bus trips, parking, crossings and all ten ambulance seeds. One low-speed animal/parking contact is reported separately. |
| Davis geometry and navigation | Shared road/lane/building/map projection; Richards underpass grade separation; 1,288 legal/continuity checks. All 835 currently enabled car/bus turn paths pass the fresh physical audit. Historical exclusions remain conservative simulation restrictions. |
| Cockpit and presentation | Licensed CarConcept-derived cabin, actual instrument/steering/pedal state, limited mirrors, observer-driven companion, frame-associated model overlay and Canvas menus. Previous 720p/1080p state captures are labelled separately from genuine runtime captures. |
| Stock Comma | User's build-16 X-button attempt failed with upstream timing/radar health errors. The repaired package accepted all three keyboard requests and revoked authority on all three brake takeovers. A forced bridge crash also cancelled automation; all 19 owned processes were removed in 2.44 s, and the in-game restart recovered healthy controls. |
| Observer isolation | Camera/ego/map contracts, evaluation truth, read-only model presentation and simulation actuators remain separate. No planner replaces Comma after rejection. |
| Physical Xbox | User confirmed acceleration, braking, reverse, views, map, pause, loss-pauses/reconnect-stays-paused/Menu-resumes. The final physical X/LT takeover check has not passed. |
| Sustained performance | A final-candidate 30-minute 720p run with the full default population and AI remains required. Earlier 617-second/911-second samples are not a pass. |
| Package/recovery | Isolated Windows runtime, model assets and notices are packaged outside the checkout. Owned-service crash/restart and exit were exercised; repeat affected checks for the final adapter. |

The retained evidence is in `outputs/qualification/overhaul/REPORT.html`, JSON reports
and `outputs/unity/` test results. Package identity and source hashes bind measurements
to a candidate. Earlier failures are retained, not replaced by later success.

Perception metrics, including substantial missed objects and sign-reading errors, are
reported in [UNITY_GAME.md](UNITY_GAME.md). There is no established real-road accuracy.
Stock Comma's urban junction failures remain visible test outcomes.
