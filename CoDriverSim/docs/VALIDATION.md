# Local validation evidence

Initial implementation: November 7, 2026. These results describe a Linux development environment,
not the user's Windows/RX 580 machine.

- Python 3.11.17 and 3.12.14: 18 unit/CLI tests passed on each; Ruff lint and format checks passed.
- Pinned MetaDrive 0.4.3: a 500-step physics-only run with seed 21 and traffic density 0.1
  completed (50 simulated seconds). It observed lead vehicles through the truth adapter and
  exported the report. No camera inference or graphical frame-rate result is implied.
- The packaged synthetic demo independently exercises quiet, caution, urgent, and quiet states.
- A wheel installed without dependencies in a fresh Python 3.11 environment ran its packaged demo
  outside the source checkout. The core does not import MetaDrive.
- The universal dependency lock resolved and installed in Python 3.11 on Linux.
- The repository's six existing scaffold checks passed independently.

Pending local user validation: Windows installation, interactive rendering, keyboard input,
overlay layout, and frame rate on the RX 580. Controlled hazardous scenarios and real-camera/model
evaluation are future milestones. A successful headless run does not validate the graphics path.
