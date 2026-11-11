"""Audit local art and prepare a parked truck without modifying game assets.

Run Blender 4.5.15 --background --factory-startup --disable-autoexec. Archives
have no bundled license: every derivative is local-only and publication-excluded.
"""

import argparse
import hashlib
import json
import math
import sys
import zipfile
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

SOURCE_HASHES = {
    "64-truck.zip": "b6f76339d418d73f5761d90db19337adbd72163ed072ddd751197c7f6cdb0fcc",
    "68-transportation-001.zip": "8e6237c25035107a47eb70dde83ad122d2550449d7081c76f2ae400fb46f5963",
    "hqzdkgvom9kw-Red_Brick_Building_Iridesium.zip": (
        "1d206e1db4c349a406cc4cd5ed16dc0543dad8d12c988d43c34501130d126851"
    ),
}


def bounds(objects):
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    return [[float(fn(p[i] for p in points)) for i in range(3)] for fn in (min, max)]


def clear():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)
    for material in list(bpy.data.materials):
        if material.users == 0:
            bpy.data.materials.remove(material)


def inspect_meshes():
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    return {
        "bounds_blender": bounds(meshes),
        "objects": [
            {
                "name": obj.name,
                "bounds": bounds([obj]),
                "triangles": sum(len(p.vertices) - 2 for p in obj.data.polygons),
                "materials": [m.name if m else None for m in obj.data.materials],
            }
            for obj in meshes
        ],
    }


def preview(path, angle=(1, -1, 0.6)):
    meshes = [
        obj for obj in bpy.context.scene.objects if obj.type == "MESH" and not obj.hide_render
    ]
    lo, hi = map(Vector, bounds(meshes))
    centre = (lo + hi) / 2
    extent = max(hi - lo)
    bpy.ops.object.camera_add(location=centre + Vector(angle) * extent)
    camera = bpy.context.object
    camera.rotation_euler = (centre - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = extent * 1.65
    camera.data.clip_start = max(0.01, extent / 10000)
    camera.data.clip_end = max(1000, extent * 10)
    scene = bpy.context.scene
    scene.camera = camera
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.display.shading.background_type = "WORLD"
    scene.world.color = (0.12, 0.14, 0.17)
    scene.render.resolution_x = 512
    scene.render.resolution_y = 384
    scene.render.resolution_percentage = 100
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(camera, do_unlink=True)


def extract(archive, output):
    output.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(archive) as source:
        for entry in source.infolist():
            if entry.filename.startswith("__MACOSX/"):
                continue
            destination = (output / entry.filename).resolve()
            if not destination.is_relative_to(output.resolve()):
                raise ValueError("Archive path escapes local preparation folder")
            if not entry.is_dir():
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(source.read(entry))
        return [{"name": e.filename, "size": e.file_size} for e in source.infolist()]


def prepare_truck(output):
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    lo, hi = map(Vector, bounds(meshes))
    scale = 9.5 / (hi.y - lo.y)
    centre = (lo + hi) / 2
    centre.z = lo.z
    head = next(obj for obj in meshes if obj.name.startswith("Truck_head"))
    hlo, hhi = map(Vector, bounds([head]))
    flip = ((hlo.y + hhi.y) / 2) > centre.y
    rotation = Matrix.Rotation(math.pi if flip else 0, 4, "Z")
    for obj in meshes:
        obj.data.transform(
            rotation @ Matrix.Scale(scale, 4) @ Matrix.Translation(-centre) @ obj.matrix_world
        )
        obj.matrix_world = Matrix.Identity(4)
    # The archived MTL assigns identical gray to all 17 slots. These are explicitly
    # authored semantic replacements, not recovered original texture/material art.
    palette = {
        "bodycolour": ((0.12, 0.29, 0.31, 1), 0.25, 0.42),
        "head_paint": ((0.82, 0.80, 0.72, 1), 0.3, 0.35),
        "tyres": ((0.019, 0.022, 0.025, 1), 0, 0.92),
        "wheels": ((0.19, 0.21, 0.23, 1), 0.65, 0.36),
        "wheel_hub": ((0.48, 0.5, 0.52, 1), 0.8, 0.3),
        "door_knob": ((0.07, 0.08, 0.09, 1), 0.4, 0.4),
        "flat_black": ((0.03, 0.035, 0.04, 1), 0, 0.85),
        "diffuse_black": ((0.045, 0.05, 0.055, 1), 0, 0.9),
        "deep_black": ((0.015, 0.018, 0.021, 1), 0, 0.85),
        "gloss_black": ((0.035, 0.04, 0.045, 1), 0.25, 0.3),
        "glass": ((0.08, 0.14, 0.19, 1), 0.3, 0.16),
        "full_glass": ((0.13, 0.20, 0.25, 1), 0.35, 0.12),
        "windglass": ((0.09, 0.17, 0.22, 1), 0.35, 0.14),
        "full_glossy": ((0.43, 0.47, 0.5, 1), 0.8, 0.2),
        "light_emit": ((0.96, 0.91, 0.7, 1), 0.1, 0.18),
        "signal_light": ((0.95, 0.29, 0.035, 1), 0, 0.3),
        "Material": ((0.65, 0.66, 0.63, 1), 0.2, 0.65),
    }
    for obj in meshes:
        for material in obj.data.materials:
            if material.name not in palette:
                raise ValueError("Unreviewed truck material: " + material.name)
            color, metallic, roughness = palette[material.name]
            material.use_nodes = True
            material.diffuse_color = color
            shader = material.node_tree.nodes.get("Principled BSDF")
            shader.inputs["Base Color"].default_value = color
            shader.inputs["Metallic"].default_value = metallic
            shader.inputs["Roughness"].default_value = roughness
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    near = bpy.context.object
    near.name = "ParkedLogisticsTruck_LOD0"
    lods = [near]
    for level, ratio in ((1, 0.35), (2, 0.12)):
        lod = near.copy()
        lod.data = near.data.copy()
        lod.name = f"ParkedLogisticsTruck_LOD{level}"
        bpy.context.collection.objects.link(lod)
        bpy.context.view_layer.objects.active = lod
        modifier = lod.modifiers.new("Static distant detail", "DECIMATE")
        modifier.ratio = ratio
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        lods.append(lod)
    report = inspect_meshes()
    dimensions = [b - a for a, b in zip(*bounds([near]), strict=True)]
    if not (2 <= dimensions[0] <= 2.8 and 2.4 <= dimensions[2] <= 4):
        raise ValueError("Proportional truck dimensions need review")
    output.mkdir(parents=True, exist_ok=True)
    prepared = output / "ParkedLogisticsTruck.glb"
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(
        filepath=str(prepared),
        export_format="GLB",
        export_apply=True,
        export_animations=False,
        export_cameras=False,
        export_lights=False,
        export_copyright="Supplied archive, license unknown. Local only.",
    )
    for lod in lods[1:]:
        lod.hide_render = True
    preview(output / "truck-front.png")
    preview(output / "truck-rear.png", (-1, 1, 0.6))
    report.update(
        {
            "dimensions_m_width_length_height": dimensions,
            "forward_blender": "-Y",
            "forward_exported_gltf": "+Z",
            "source_axis_flip_z": flip,
            "scale_uniform": scale,
            "material_replacements": palette,
            "prepared_sha256": hashlib.sha256(prepared.read_bytes()).hexdigest(),
            "license": "unknown",
            "public_redistribution": False,
            "intended_use": "static parked logistics prop; no control or traffic integration",
            "target_if_accepted": "WorldArt/Local only",
            "lod_selection": "Consumer must enable one LOD at a time; no Unity importer run",
        }
    )
    (output / "preparation.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    return report


def main():
    if bpy.app.version != (4, 5, 15):
        raise ValueError("Preparation is pinned to Blender 4.5.15 LTS")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path)
    parser.add_argument("--pickup", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1 :])
    args.output = args.output.resolve()
    args.output.mkdir(parents=True, exist_ok=True)
    if args.pickup:
        clear()
        bpy.ops.wm.collada_import(filepath=str(args.pickup.resolve()))
        report = inspect_meshes()
        preview(args.output / "pickup-positive-x-negative-y.png", (1, -1, 0.6))
        preview(args.output / "pickup-negative-x-positive-y.png", (-1, 1, 0.6))
        report["camera_directions"] = [[1, -1, 0.6], [-1, 1, 0.6]]
        report["source_sha256"] = hashlib.sha256(args.pickup.read_bytes()).hexdigest()
        (args.output / "pickup-orientation.json").write_text(json.dumps(report, indent=2))
        return
    result = []
    for name in (
        "64-truck.zip",
        "68-transportation-001.zip",
        "hqzdkgvom9kw-Red_Brick_Building_Iridesium.zip",
    ):
        archive = args.assets / name
        digest = hashlib.sha256(archive.read_bytes()).hexdigest()
        if digest != SOURCE_HASHES[name]:
            raise ValueError("Unreviewed supplied archive version: " + name)
        destination = args.output / "supplied-source" / archive.stem
        entries = extract(archive, destination)
        obj = next(destination.rglob("*.obj"))
        material_repairs = []
        source_text = obj.read_text(encoding="utf-8", errors="replace")
        for line in source_text.splitlines():
            if line.startswith("mtllib ") and not (obj.parent / line[7:].strip()).is_file():
                material_file = obj.with_suffix(".mtl")
                if material_file.is_file():
                    replacement = "mtllib " + material_file.name
                    source_text = source_text.replace(line, replacement)
                    material_repairs.append(
                        {"from": line, "to": replacement, "scope": "extracted working copy only"}
                    )
        if material_repairs:
            obj.write_text(source_text, encoding="utf-8")
        clear()
        bpy.ops.wm.obj_import(filepath=str(obj))
        report = inspect_meshes()
        report.update(
            {
                "archive": name,
                "source_sha256": digest,
                "entries": entries,
                "license": "no license included; unknown",
                "public_redistribution": False,
                "material_reference_repairs": material_repairs,
            }
        )
        if name == "64-truck.zip":
            report["prepared"] = prepare_truck(args.output / "truck-prepared")
        else:
            preview(args.output / (archive.stem + ".png"))
            report["decision"] = (
                "Do not integrate; legacy materials require separate reconstruction"
            )
        if hashlib.sha256(archive.read_bytes()).hexdigest() != digest:
            raise ValueError("Original archive changed")
        result.append(report)
    (args.output / "supplied-art-audit.json").write_text(
        json.dumps(result, indent=2), encoding="utf-8"
    )


if __name__ == "__main__":
    main()
