"""Prepare the pinned CC0 excavator with articulated parts and two detail levels.

Run with Blender 4.5.15, --factory-startup --disable-autoexec. Source is never changed.
The original hydraulic meshes are replaced at runtime by telescoping geometry
between the measured source pin anchors; they must not float when the arms move.
"""

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

SOURCE_SHA = "d7ab61533437b330b2107150092d10bf5f51fce66c1fb06ee554669a4eff4f17"
PIVOTS = {
    "Base": (0, 0, 0),
    "Upper": (0.1, 0, 0.68),
    "Boom": (-0.36, 0.29, 1.040024),
    "Dipper": (-1.1, 0.29, 2.150024),
    "Bucket": (-1.61, 0.29, 0.690024),
}
PARENTS = {"Base": None, "Upper": None, "Boom": "Upper", "Dipper": "Boom", "Bucket": "Dipper"}
ANCHORS = {
    "BoomCylinderBase": ("Upper", (-0.39, 0.29, 1.010024)),
    "BoomCylinderEnd": ("Boom", (-0.91, 0.29, 1.850024)),
    "DipperCylinderBase": ("Boom", (-0.61, 0.29, 1.450024)),
    "DipperCylinderEnd": ("Dipper", (-1.25, 0.29, 2.000024)),
    "BucketCylinderBase": ("Dipper", (-1.21, 0.29, 1.670024)),
    "BucketCylinderEnd": ("Bucket", (-1.56, 0.29, 0.890024)),
}


def part_for(obj):
    name = obj.name
    center = sum((obj.matrix_world @ Vector(p) for p in obj.bound_box), Vector()) / 8
    if name.startswith(("Boom lift", "Dipper actuator", "Bucket curl")):
        return None
    if name.startswith(("Excavator bucket", "Bucket quick coupler")):
        return "Bucket"
    if name.startswith("Shaped excavator dipper"):
        return "Dipper"
    if name.startswith(("Shaped excavator boom", "Boom high pressure")):
        return "Boom"
    if name.startswith(("Boom articulation pin", "Boom pin retaining")):
        return min(("Boom", "Dipper", "Bucket"), key=lambda k: (center - Vector(PIVOTS[k])).length)
    if name.startswith(("Blade", "Grading blade", "Slew bearing")) or center.z < 0.74:
        return "Base"
    return "Upper"


def empty(name, parent, position):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    obj.location = position
    return obj


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1 :])
    if hashlib.sha256(args.source.read_bytes()).hexdigest() != SOURCE_SHA:
        raise ValueError("Unexpected construction source; preserve and review the original")
    args.output.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.gltf(filepath=str(args.source))
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    groups = {key: [] for key in PIVOTS}
    # Source forward is -X. Bake +90 degrees around Blender Z before exporting:
    # the imported Unity asset then has +Z forward, +Y up and -X on its left.
    rotation = Matrix.Rotation(math.pi / 2, 4, "Z")
    removed = []
    for obj in meshes:
        group = part_for(obj)
        if group is None:
            removed.append(obj.name)
            bpy.data.objects.remove(obj, do_unlink=True)
            continue
        obj.data = obj.data.copy()
        obj.data.transform(rotation @ obj.matrix_world)
        obj.matrix_world = Matrix.Identity(4)
        groups[group].append(obj)
    for material in bpy.data.materials:
        if "safety glass" in material.name.lower() or "safety glazing" in material.name.lower():
            shader = next(n for n in material.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
            for link in list(shader.inputs["Base Color"].links):
                material.node_tree.links.remove(link)
            shader.inputs["Base Color"].default_value = (0.35, 0.5, 0.57, 1)
            shader.inputs["Alpha"].default_value = 0.24
            shader.inputs["Roughness"].default_value = 0.12
            shader.inputs["Transmission Weight"].default_value = 0
            material.diffuse_color = (0.35, 0.5, 0.57, 0.24)
    root = empty("PreparedExcavator", None, Vector())
    pivots = {key: rotation @ Vector(point) for key, point in PIVOTS.items()}
    rig = {}
    for key in PIVOTS:
        parent = PARENTS[key]
        rig[key] = empty(
            key,
            rig[parent] if parent else root,
            pivots[key] - (pivots[parent] if parent else Vector()),
        )
    counts = {}
    for key, objects in groups.items():
        if not objects:
            raise ValueError("Missing articulated geometry: " + key)
        bpy.ops.object.select_all(action="DESELECT")
        for obj in objects:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = objects[0]
        bpy.ops.object.join()
        near = bpy.context.object
        near.name = key + "_LOD0"
        near.data.transform(Matrix.Translation(-pivots[key]))
        near.parent = rig[key]
        near.matrix_parent_inverse = Matrix.Identity(4)
        near.matrix_basis = Matrix.Identity(4)
        near.data.calc_loop_triangles()
        high_triangles = len(near.data.loop_triangles)
        far = near.copy()
        far.data = near.data.copy()
        bpy.context.collection.objects.link(far)
        far.name = key + "_LOD1"
        bpy.context.view_layer.objects.active = far
        modifier = far.modifiers.new("Distant machinery detail", "DECIMATE")
        modifier.ratio = 0.28
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        far.data.calc_loop_triangles()
        counts[key] = {
            "near_triangles": high_triangles,
            "far_triangles": len(far.data.loop_triangles),
        }
    for name, (parent, point) in ANCHORS.items():
        empty(name, rig[parent], rotation @ Vector(point) - pivots[parent])
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(
        filepath=str(args.output / "Excavator.glb"),
        export_format="GLB",
        export_apply=True,
        export_animations=False,
        export_copyright="Innerscene Corp; CC0-1.0. CoDriverSim articulated/LOD adaptation.",
    )
    report = {
        "source_sha256": SOURCE_SHA,
        "prepared_sha256": hashlib.sha256((args.output / "Excavator.glb").read_bytes()).hexdigest(),
        "parts": counts,
        "removed_hydraulics": removed,
        "source_pivots_blender_m": PIVOTS,
        "source_hydraulic_anchors_blender_m": ANCHORS,
        "modifications": [
            "Forward-axis normalization, source meshes combined per moving part",
            "Distant LOD decimation, source pin anchors retained",
            "Cab glazing uses alpha transparency supported by the target URP material",
            "Static hydraulic meshes removed for runtime telescoping cylinders",
        ],
    }
    (args.output / "preparation.json").write_text(json.dumps(report, indent=2))
    print(json.dumps(counts))


if __name__ == "__main__":
    main()
