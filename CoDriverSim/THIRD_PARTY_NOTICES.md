# Third-party provenance

MetaDrive: https://github.com/metadriverse/metadrive

The optional simulator dependency is pinned to commit
`fe54b03327b14bcfc17a9a04b60c8abbee4d7ab1` (package version 0.4.3).
Its repository declares the Apache License 2.0. MetaDrive and its dependencies/assets are
installed separately; retain and review their individual notices for any future redistribution.

The launcher uses MetaDrive's public environment interface and keyboard-control pattern.
The terrain compatibility adapter also uses private terrain-resolution attributes specific to
the pinned revision; verify these when updating MetaDrive.

`src/codriversim/shaders/terrain_compat.vert.glsl` is adapted from MetaDrive's
`metadrive/shaders/terrain.vert.glsl` at the pinned commit above. Its terrain instancing and LOD
calculations are retained; unused lighting/projector declarations are removed for the simplified
RGB path. MetaDrive's Apache-2.0 license is included in
`src/codriversim/licenses/MetaDrive-Apache-2.0.txt`. The accompanying compatibility fragment shader
and procedural building geometry are CoDriverSim code. Texture and 3D-model assets continue to
load from the separately installed simulator.

The Unity application downloads external assets into ignored caches; binary models, datasets,
and asset archives are not added to source control. Provenance and hashes are recorded in
`game/assets-manifest.json` and `game/quaternius-manifest.json`.

The realism asset pass additionally records every downloaded file in
`game/realism-manifest.json` and `game/surface-rail-manifest.json`:

- Vehicles, buildings and street furniture from [Open Robotics Gazebo models](https://github.com/osrf/gazebo_models),
  revision `8163eb4b5e7e21985c6591d1c0bfb56468c0093f`, are **CC BY 3.0**.
  Copyright 2012 Nathan Koenig; individual model authors from each `model.config` are retained
  in the manifest. The complete license is `src/codriversim/licenses/gazebo-models-CC-BY-3.0.txt`.
  CoDriverSim adapts their orientation, dimensions, materials, collision and moving parts.
  Source files and authorship metadata are preserved in the ignored download cache.
- [Innerscene's original Metro car](https://www.innerscene.com/tools/library/3d-parts/metro-car-22a0c19c)
  is released under CC0. CoDriverSim adapts its dimensions, draw-call grouping and doors.
- Additional Poly Haven normal, roughness and occlusion maps, and the
  [Kloofendal 48d partly cloudy pure sky](https://polyhaven.com/a/kloofendal_48d_partly_cloudy_puresky)
  by Greg Zaal and Jarod Guest are CC0. SHA-256 hashes pin the exact downloaded files.

These notices and all four asset manifests are included in development bundles. Unused downloaded
models are acquisition candidates, not claims that every model has been integrated into gameplay.

- Poly Haven asphalt, concrete, grass, brick and roof textures are [CC0](https://polyhaven.com/license).
  Exact download URLs and SHA-256 hashes are verified by `scripts/fetch-game-assets.py`.
- Quaternius Universal Animation Library, Universal Base Characters, low-poly cars, textured trees,
  and animated animal packs are CC0. Their author pages and archive hashes are in the Quaternius
  manifest. Dog, Cat and Eagle come from the author's
  [Animated Animales Low Poly pack on OpenGameArt](https://opengameart.org/content/animated-animales-low-poly).
  Keep the included archive notices. Imported scale, materials and animation controllers are adapted
  for this application; generated prefabs stay outside source control.
- Quaternius [Ultimate Animated Character Pack](https://quaternius.com/packs/ultimatedanimatedcharacter.html)
  supplies clothed civilian and worker models under CC0. The author's public Drive files and exact
  hashes are in `game/assets-manifest.json`; the license reference is retained under
  `src/codriversim/licenses/Quaternius-Characters-CC0.txt`. The older foot hierarchy is adapted in
  the imported prefab for humanoid retargeting to the Universal Animation Library.
- OpenCV Zoo NanoDet-M-Plus 416 and CRNN English ONNX models use Apache-2.0 licenses, copied under
  `src/codriversim/licenses`. Model downloads are pinned to Hugging Face revisions and verified hashes.
  The reference preprocessing/model documentation comes from OpenCV Zoo revision
  `48b10fa505aeb51fbd127fc60a5c660040e823b7`.
- Unity and its packages are installed under their own terms; their versions are locked in the
  Unity project's package manifest and lock file. The application includes the built Unity player;
  the Unity Editor is not bundled.
- Unity glTFast 6.20.0 imports the CC0 railcar GLB. Copyright 2023 Unity Technologies and
  the Unity glTFast authors; Apache-2.0. Its complete notice and license are retained in
  `src/codriversim/licenses/glTFast-Apache-2.0.txt` and the bundle's `Notices/licenses/`.
- The optional development bundle copies the locally installed Windows CPython 3.11 runtime,
  retaining its `LICENSE.txt`, standard library and extension modules. Pinned NumPy, OpenCV and
  supporting Python wheels retain their installed license metadata. The bundle records the actual
  interpreter version and file hashes; it is not an assertion that this interpreter is the newest release.

The simulation adapter uses a separately installed official openpilot v0.9.7 checkout in WSL,
commit `50dd024081496cc5f036a99c8f62d2887112dbb7`. Its upstream model and controller remain unchanged.
The checkout, weights and upstream dependencies are not bundled in the Python wheel. See
`docs/COMMA_SIM.md` for installation history. The portable observer imports no vehicle actuators.

The optional real-footage replay fixture is Visaitech dataset v0.1, `videos/clip_140.mp4`,
[CC BY 4.0](https://huggingface.co/datasets/visaitech/vehicle-mixed-traffic-detection), revision
`de35185262bda34c104a626f866bab9d9aaf67ed`. Credit: Visaitech dataset contributors.
Its SHA-256 is `dcb4515c87e3e81e008ba95107b20e7ec9e334e0e23b4f1783dac5d0c14901f8`.
`scripts/fetch-real-clip.py` verifies the file and writes an attribution record beside it.
Retain that credit and the license when sharing derived images or recordings. The video is not
included in source control. No Mixkit or verification-gated Poly Pizza assets were downloaded.

The optional COCO smoke evaluation uses [COCO val2017](https://cocodataset.org/#download).
Annotations are CC BY 4.0; individual images keep their own Flickr licenses, as described in
[COCO's terms](https://cocodataset.org/#termsofuse). The subset selects only historical CC BY 2.0
image-license metadata. `outputs/qualification/coco-smoke/attribution.json` retains each source
photo URL, image ID, license URL and SHA-256. Some historical photo pages are no longer available;
the images and annotations are not bundled or committed. Retain their separate attribution when
sharing an evaluation artifact. These optional datasets are not included in the application package.

The optional downtown Davis map survey uses **© OpenStreetMap contributors**, under
[ODbL 1.0](https://www.openstreetmap.org/copyright). `scripts/fetch-district.py` retains the
request, retrieval time, SHA-256 and attribution beside the ignored raw extract;
`scripts/prepare-district.py` carries that provenance into the derived local-coordinate layout
and visibly credits OSM on its preview. The playable Davis map embeds the derived lane and building
geometry. The Windows bundle includes the source extract, retrieval record and derived database in
`DistrictData/`, under ODbL 1.0; the Python wheel does not bundle those databases. Retain the attribution and applicable ODbL terms when sharing
these files. This data is not CC0, surveyed lane geometry or licensed aerial/photogrammetric imagery.

Randomation Vehicle Physics by Justin Couch is MIT licensed, pinned to
`192ba860c3c92a16363ffe87fb943dc282e62398`. `game/dynamics-provenance.json` records the exact
reference source hashes and the changes in the independently stepped Unity 6 dynamics adapter.
The adapter reimplements/adapts the core calculations with SI forces and load-bounded tires;
it does not include the upstream demo, input, menus, stunt, burnout or rollover systems.
Keep `src/codriversim/licenses/RVP-MIT.txt` with distributions.

CarConcept cockpit geometry and textures: Eric Chadwick, © 2024 Darmstadt Graphics Group GmbH,
[CC BY 4.0](https://github.com/KhronosGroup/glTF-Sample-Assets/tree/7d4ba189827916452eeadc82d4b712dbc6280a6f/Models/CarConcept).
The original public-domain concept model was created by Unity Fan. The revision and binary hash
are locked in `game/overhaul-manifest.json`. Cabin components are selected, resized and repositioned;
emblem/logo meshes and the original instrument display are removed. Animated controls, instruments,
mirrors and the co-driver device are application additions. Khronos trademarks are not licensed by
CC BY; this adaptation omits the trademark-bearing meshes. Full attribution and license are under
`src/codriversim/licenses/CarConcept-*`.

The public fallback additionally uses converted URP materials with neutral,
matte upholstery and reduced normal-map strength, plus an authored dashboard
instrument housing and a live Canvas cluster. Licensed source geometry is retained.

The Comma-style Canvas layout and steering-wheel image are adapted from the installed official
openpilot v0.9.7 revision above, MIT licensed by comma.ai. The fonts are unmodified Inter Regular, Bold
and Semibold by Rasmus Andersson and contributors, under the SIL Open Font License 1.1.
Their exact upstream URLs/hashes are in the overhaul manifest; license texts are
`openpilot-MIT.txt` and `Inter-OFL.txt`. Observer warning labels and control-authority distinctions
are CoDriverSim additions. No affiliation or endorsement is implied.

Eclipse SUMO 1.28.0 `netconvert` is an offline map build tool, not a bundled driving controller.
Its official Windows archive checksum is locked in the overhaul manifest. Generated connections
and timings are simulation estimates with retained OSM source identities. SUMO's distribution
retains its own EPL-2.0/GPL-2.0-or-later notices in the external tool cache.
# Realism rebuild additions (2026-11-10)

- **Modular Urban Apartments Facade — Poly Haven**, CC0-1.0. Source:
  https://polyhaven.com/a/modular_urban_apartments_facade ; license:
  https://polyhaven.com/license . Exact 2K files and upstream checksums are pinned
  in `game/world-art-sources.json`. Converted to Unity URP materials; door/window,
  plinth, cornice and crown modules are assembled against OSM building footprints.
- **Street Signs — ScreamingOrange**, source page identifies CC0:
  https://blendswap.com/blend/31350 . Supplied source SHA-256:
  `e443f39632437418d559e743c248d00b26bc28e5022a1d50989eec41d398f0fa`.
  Adaptations: metre scale, grounded pivots, coplanar simplification and portable
  materials. Source sign images remain embedded in the prepared model.
- **Mercedes-Benz GLS 580 2020 — goldenflies**, supplied source:
  https://free3d.com/3d-model/mercedes-benz-gls-580-2020-83444.html . Source archive
  SHA-256 `8f694684cbb44c26b93d7872885870a0e6fe9f65da85aaf2888524d5eb3c81bf`.
  Listed as personal use; integrated in the **local prototype only**. Public asset
  builds exclude the source, derivatives and materials. Adaptations: duplicate
  vertex/face cleanup, normal repair, metre scale, wheel/steering separation and
  live instruments fitted to the dashboard. The missing source image was a plate
  texture, not an instrument display. This is not an official
  Mercedes product or an openpilot Mercedes vehicle port.
- Preparation uses Blender 4.5.15 LTS, obtained from the official Blender download
  archive and checksum-verified. Blender is a build tool and is not bundled.

- **Rigged Man With Walk Cycle — brightonpiers**, CC BY 3.0:
  https://blendswap.com/blend/20564 . Supplied archive SHA-256
  `b93b59c5bdff4eea175873fa53f37ed7cc1534ed4ea96e7f58d7d192d76f4aab`.
  Adaptations: metre scale, subdivision level 1, explicit humanoid bone mapping,
  portable clothing materials, retargeting to the existing licensed UAL clips.
  The unfinished UV-checker shirt is replaced with CC0 fabric; jeans retain the
  supplied painted texture. The existing CC BY 3.0 license text applies.
- **Fabric 034 — ambientCG**, CC0-1.0:
  https://ambientcg.com/view?id=Fabric034 ; https://docs.ambientcg.com/license/ .
  The 2K JPG archive is pinned in `game/fabric-source.json`. Applied as a tinted
  civilian clothing material.

- **Leather 026 — ambientCG / Lennart Demes**, CC0-1.0:
  https://ambientcg.com/view?id=Leather026 ; https://docs.ambientcg.com/license/ .
  The pinned 2K normal map supplies fine cabin leather grain. UV coordinates are
  generated at a consistent physical scale during the GLS conversion. Source
  archive SHA-256 is recorded in `game/leather-source.json`.
- **Rigged and animated Cat — JonasDichelle**, CC BY 3.0:
  https://blendswap.com/blend/18519 . Supplied Blender source SHA-256
  `fda4b80f1a06e67a1cc04396b858085faa5cd9742f28d9bde9837aaeab2727fa`.
  Adaptations: removed particle hair and circular legacy rig constraints, baked
  the original walk/run clips, authored two standing idle variations, converted
  painted materials and normalized physical scale, forward axis and foot height.
- **Gravel Floor — Jenelle van Heerden / Matterfield, Poly Haven**, CC0-1.0:
  https://polyhaven.com/a/gravel_floor ; https://polyhaven.com/license .
  Four 2K maps are pinned by SHA-256 in `game/surface-rail-manifest.json` and used
  on the continuous railway ballast bed.
- The pinned **CarConcept** license above also covers the new NPC coupe derivative:
  interior removal, wheel separation/alignment, two simplified detail levels,
  physical scale and portable URP materials, including exterior glazing and
  restored paint-normal texture scale. Original attribution is retained.
- **White Plaster 02 — Rob Tuytel, Poly Haven**, CC0-1.0:
  https://polyhaven.com/a/white_plaster_02 ; https://polyhaven.com/license .
  Four 2K maps are pinned in `game/surface-rail-manifest.json`; applied to the
  varied facade assemblies at a consistent texture scale.
- **Beige Wall 001 — Dimitrios Savva and Rico Cilliers, Poly Haven**, CC0-1.0:
  https://polyhaven.com/a/beige_wall_001 ; https://polyhaven.com/license .
  Four pinned 2K maps provide painted stucco for the adapted architectural palette;
  the photographed surface spans approximately three metres.
- **Bitumen — Rob Tuytel, Poly Haven**, CC0-1.0:
  https://polyhaven.com/a/bitumen ; https://polyhaven.com/license .
  Four pinned 2K maps provide the flat roof finish, with the source's twenty-metre
  surface scale. Both sets are listed in `game/surface-rail-manifest.json`.
- **White Stucco — Amal Kumar, Poly Haven**, CC0-1.0:
  https://polyhaven.com/a/white_stucco ; https://polyhaven.com/license .
  Four pinned 2K maps provide the lighter wall finishes, with approximately
  two metres per texture repeat. The source API metadata is retained locally.
- The licensed Quaternius UAL animation subset also supplies the pedestrian
  impact clips. `Ground_Recovery` is a reversed, non-looping adaptation of
  `Death01`; the simulation adds interruption, timing and locomotion recovery.

- **Prop Buildings — ShalmonAnandas**, geometry published as CC0:
  https://blendswap.com/blend/28283 . Supplied Blender source SHA-256
  `58ebf9a700fe2b89f72983f7ba9c96fe8a7ec7f85e279cacf3ec2529601f7efe`.
  The low-rise `building 7` is normalized to metres and prepared at two detail
  levels (5,182 and 2,813 triangles). All original material slots, UVs and embedded
  images are replaced; the original TexturesCom images are not distributed.
  Replacement wall/roof maps use the separately attributed CC0 surfaces above.
  Its Davis appearance is an authored adaptation attached to source footprint
  `way/45208383`, not a surveyed reconstruction of that real building.

- **MakeHuman / MPFB clothed civilians**, CC0-1.0 assets and generated output:
  https://github.com/makehumancommunity/mpfb2/blob/6ff1eb358f6c1959ced77e79e6dd2406d9a71f49/LICENSE.md .
  MPFB is pinned to `6ff1eb358f6c1959ced77e79e6dd2406d9a71f49` and used only as an
  offline Blender preparation tool. Its GPLv3 program code is not included in
  the application. The selected MakeHuman core clothes, skin, hair, shoes, eyes
  and eyebrow files are individually pinned in `game/makehuman-core-manifest.json`.
  Core asset credits include the MakeHuman team, Data Collection AB, Joel Palmius
  and Jonas Hauquier, as retained in those source files. Adaptations: two adult
  body recipes, covered-body masks, normalized scale, Mixamo-compatible rest rig,
  two mesh detail levels, URP materials and separately licensed Quaternius UAL
  animation retargeting. Shirt surfaces use the attributed ambientCG Fabric034
  texture with authored colors. No MPFB executable or source is required at runtime.

- **Python MSS 10.2.0 — Mickaël Schoentgen**, MIT:
  https://pypi.org/project/mss/10.2.0/ ;
  https://github.com/BoboTiG/python-mss . Optional Windows visual-review tooling,
  pinned separately in `requirements-capture-lock.txt`; not bundled into the game
  observer runtime. The installed wheel retains its upstream license text.

- **Compact tracked excavator ? Innerscene Corp**, CC0-1.0:
  https://www.innerscene.com/tools/library/3d-parts/compact-tracked-excavator-76c41cd5 .
  Source bytes are pinned in `game/construction-manifest.json`. Adaptations: forward-axis
  normalization, five articulated sections, two detail levels, transparent cab glazing,
  and animated telescoping hydraulics. Collision uses an estimated finite-mass body;
  the animation is a staged construction activity, not validated machinery dynamics.

- **DXcam 0.3.0 ? Rain**, MIT: https://github.com/ra1nty/DXcam/tree/v0.3.0 .
  Optional Windows desktop-duplication capture for visual review, with
  **comtypes 1.5.0**, MIT: https://github.com/enthought/comtypes .
  These tools are pinned in `requirements-capture-lock.txt`; their wheels retain
  license texts. Neither is bundled into the game observer runtime.

- **Rough Wood — Rob Tuytel / Poly Haven**, CC0-1.0:
  https://polyhaven.com/a/rough_wood . The 2K diffuse, OpenGL normal, roughness
  and occlusion maps are pinned in `game/surface-rail-manifest.json`. Railway
  sleepers use the weathered timber surface with an authored dark treatment;
  the source scan covers 0.5 metres per texture repeat. Original downloads and
  upstream file metadata are retained outside source control.
