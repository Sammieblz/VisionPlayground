"""Generate clothed CC0 civilian variants with pinned MPFB and Blender, offline.

Run through Blender --background --factory-startup --disable-autoexec --python.
The add-on is used only during asset preparation; no MPFB code enters the game.
"""

import argparse
import hashlib
import json
import re
import subprocess
import sys
from pathlib import Path

import addon_utils
import bpy

REVISION = "6ff1eb358f6c1959ced77e79e6dd2406d9a71f49"


def dress_color(meshes, suit, color):
    """Use a woven shirt while retaining the authored trousers and their UVs."""
    path = (
        Path(__file__).resolve().parents[1]
        / "game/Assets/External/Resources/WorldArt/Fabric/Fabric034_2K-JPG_Color.jpg"
    )
    if not path.is_file():
        raise FileNotFoundError("Prepare the pinned ambientCG Fabric034 surface first")
    material = bpy.data.materials.new("Woven_shirt")
    material.use_nodes = True
    shader = material.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = color
    shader.inputs["Roughness"].default_value = 0.85
    texture = material.node_tree.nodes.new("ShaderNodeTexImage")
    texture.name = "DiffuseTexture"
    texture.image = bpy.data.images.load(str(path))
    tint = material.node_tree.nodes.new("ShaderNodeMixRGB")
    tint.blend_type = "MULTIPLY"
    tint.inputs[0].default_value = 1
    tint.inputs[2].default_value = color
    material.node_tree.links.new(texture.outputs["Color"], tint.inputs[1])
    material.node_tree.links.new(tint.outputs[0], shader.inputs["Base Color"])
    assigned = 0
    for obj in meshes:
        if suit not in obj.name:
            continue
        parent = list(range(len(obj.data.vertices)))

        def find(i, parent=parent):
            while parent[i] != i:
                parent[i] = parent[parent[i]]
                i = parent[i]
            return i

        for edge in obj.data.edges:
            a, b = edge.vertices
            parent[find(a)] = find(b)
        height = {}
        for vertex in obj.data.vertices:
            group = find(vertex.index)
            z = (obj.matrix_world @ vertex.co).z
            low, high = height.get(group, (z, z))
            height[group] = (min(low, z), max(high, z))
        index = len(obj.data.materials)
        obj.data.materials.append(material)
        for polygon in obj.data.polygons:
            low, high = height[find(polygon.vertices[0])]
            if low > 0.75 and high > 1.2:
                polygon.material_index = index
                for loop in polygon.loop_indices:
                    obj.data.uv_layers.active.data[loop].uv *= 8
                assigned += 1
    if not assigned:
        raise ValueError("Could not identify the shirt's connected geometry")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--tool", type=Path, required=True)
    parser.add_argument("--user-data", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1 :])
    revision = subprocess.check_output(
        ["git", "-C", str(args.tool), "rev-parse", "HEAD"], text=True
    ).strip()
    if revision != REVISION or bpy.app.version_string != "4.5.15 LTS":
        raise ValueError("Use the pinned MPFB revision and Blender 4.5.15 LTS")
    data = args.user_data.resolve() / "data"
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    for item in manifest["files"]:
        path = (data / item["path"]).resolve()
        if (
            not path.is_relative_to(data)
            or hashlib.sha256(path.read_bytes()).hexdigest() != item["sha256"]
        ):
            raise ValueError("Asset does not match the pinned manifest: " + item["path"])
    sys.path.insert(0, str(args.tool.resolve() / "src"))
    # A temporary, process-local user-data root keeps preparation out of the
    # user's interactive Blender settings and library. Preferences are not saved.
    original = bpy.utils.extension_path_user
    bpy.utils.extension_path_user = lambda package, **kw: (
        str(args.user_data.resolve()) if package == "mpfb" else original(package, **kw)
    )
    addon_utils.enable("mpfb", default_set=True, persistent=False)
    from mpfb.services.humanservice import HumanService
    from mpfb.services.rigservice import RigService
    from mpfb.services.targetservice import TargetService

    recipes = [
        dict(
            name="Citizen_A",
            gender=1.0,
            height=1.78,
            age=0.5,
            race={"african": 0.85, "asian": 0.05, "caucasian": 0.10},
            skin="young_african_male",
            suit="male_casualsuit04",
            hair="short02",
            shoes="shoes01",
        ),
        dict(
            name="Citizen_B",
            gender=0.0,
            height=1.65,
            age=0.5,
            race={"african": 0.10, "asian": 0.15, "caucasian": 0.75},
            skin="young_caucasian_female",
            suit="female_casualsuit02",
            hair="bob01",
            shoes="shoes03",
        ),
    ]
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    reports = []
    for recipe in recipes:
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.object.delete(use_global=False)
        macro = TargetService.get_default_macro_info_dict()
        macro.update(gender=recipe["gender"], age=recipe["age"], race=recipe["race"])
        human = HumanService.create_human(macro_detail_dict=macro)
        rig = HumanService.add_builtin_rig(human, "mixamo_unity")
        HumanService.set_character_skin(
            str(data / "skins" / recipe["skin"] / (recipe["skin"] + ".mhmat")),
            human,
            skin_type="GAMEENGINE",
            material_instances=False,
        )
        for category, name, kind in (
            ("clothes", recipe["suit"], "Clothes"),
            ("clothes", recipe["shoes"], "Clothes"),
            ("hair", recipe["hair"], "Hair"),
            ("eyes", "low-poly", "Eyes"),
            ("eyebrows", "eyebrow001", "Eyebrows"),
        ):
            HumanService.add_mhclo_asset(
                str(data / category / name / (name + ".mhclo")),
                human,
                asset_type=kind,
                subdiv_levels=0,
                material_type="GAMEENGINE",
            )
        bpy.context.view_layer.objects.active = rig
        rig.select_set(True)
        bpy.ops.object.mode_set(mode="POSE")
        pose = json.loads(
            (args.tool / "src/mpfb/data/poses/mixamo_unity_fk/t-pose.json").read_text()
        )
        RigService.set_pose_from_dict(rig, pose)
        bpy.ops.object.mode_set(mode="OBJECT")
        RigService.apply_pose_as_rest_pose(rig)
        meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        for obj in meshes:
            bpy.ops.object.select_all(action="DESELECT")
            obj.select_set(True)
            bpy.context.view_layer.objects.active = obj
            for modifier in list(obj.modifiers):
                if modifier.type == "MASK":
                    bpy.ops.object.modifier_apply(modifier=modifier.name)
            # Keep the authored skin weights. Only remove redundant planar faces.
            modifier = obj.modifiers.new("Dissolve planar faces", "DECIMATE")
            modifier.decimate_type = "DISSOLVE"
            modifier.angle_limit = 0.0087
            modifier.delimit = {"MATERIAL", "UV"}
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        bpy.context.view_layer.update()
        dress_color(
            meshes,
            recipe["suit"],
            (0.24, 0.35, 0.24, 1) if recipe["gender"] else (0.58, 0.20, 0.17, 1),
        )
        feet = min((obj.matrix_world @ v.co).z for obj in meshes for v in obj.data.vertices)
        top = max((obj.matrix_world @ v.co).z for obj in meshes for v in obj.data.vertices)
        scale = recipe["height"] / (top - feet)
        rig.location.z -= feet
        definitions = []
        used_materials = sorted(
            {m for o in meshes for m in o.data.materials if m}, key=lambda m: m.name
        )
        for index, material in enumerate(used_materials):
            name = recipe["name"] + "_" + re.sub(r"[^a-zA-Z0-9_]", "_", material.name)
            material.name = name
            tree = material.node_tree
            shader = next(n for n in tree.nodes if n.type == "BSDF_PRINCIPLED")
            base = shader.inputs["Base Color"]
            # A linked diffuse texture replaces the socket's default, rather
            # than multiplying by Blender's unused default grey value.
            tint = (
                (1, 1, 1, 1)
                if base.is_linked and base.links[0].from_node.type == "TEX_IMAGE"
                else tuple(base.default_value)
            )
            item = dict(
                name=name,
                color=list(tint),
                smoothness=0.25,
                albedo="",
                normal="",
                alpha_clip=False,
            )
            for node in tree.nodes:
                if node.type != "TEX_IMAGE" or not node.image:
                    continue
                role = (
                    "normal"
                    if "normal" in node.name.lower()
                    else "albedo"
                    if "diffuse" in node.name.lower()
                    else None
                )
                if not role:
                    continue
                image = node.image.copy()
                factor = min(1, 2048 / max(image.size))
                if factor < 1:
                    image.scale(round(image.size[0] * factor), round(image.size[1] * factor))
                filename = f"{recipe['name']}_{index}_{role}.png"
                image.filepath_raw = str(output / filename)
                image.file_format = "PNG"
                image.save()
                node.image = image
                item[role] = filename
            item["alpha_clip"] = any(k in name.lower() for k in ("short02", "bob01", "eyebrow"))
            definitions.append(item)
        bpy.ops.object.select_all(action="DESELECT")
        distant = []
        for obj in meshes:
            obj.name = obj.name + "_LOD0"
            duplicate = obj.copy()
            duplicate.data = obj.data.copy()
            duplicate.name = obj.name.replace("_LOD0", "_LOD1")
            bpy.context.collection.objects.link(duplicate)
            duplicate.select_set(True)
            bpy.context.view_layer.objects.active = duplicate
            modifier = duplicate.modifiers.new("Distant character detail", "DECIMATE")
            modifier.ratio = 0.45
            bpy.ops.object.modifier_apply(modifier=modifier.name)
            duplicate.select_set(False)
            distant.append(duplicate)
        meshes += distant
        for obj in meshes + [rig]:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = rig
        rig.name = recipe["name"] + "_Rig"
        bpy.ops.export_scene.fbx(
            filepath=str(output / (recipe["name"] + ".fbx")),
            use_selection=True,
            object_types={"ARMATURE", "MESH"},
            global_scale=scale,
            axis_forward="-Z",
            axis_up="Y",
            add_leaf_bones=False,
            bake_anim=False,
            path_mode="COPY",
            embed_textures=True,
            use_mesh_modifiers=True,
        )
        (output / (recipe["name"] + "-materials.json")).write_text(
            json.dumps({"materials": definitions}, indent=2), encoding="utf-8"
        )
        bpy.ops.wm.save_as_mainfile(filepath=str(output / (recipe["name"] + ".blend")))
        reports.append(
            dict(
                recipe=recipe,
                scale=scale,
                triangles=sum(len(p.vertices) - 2 for o in meshes for p in o.data.polygons),
                bones=[bone.name for bone in rig.data.bones],
                materials=definitions,
            )
        )
        print("CIVILIAN_PREPARED", recipe["name"], reports[-1]["triangles"], flush=True)
    (output / "preparation.json").write_text(
        json.dumps(
            {
                "mpfb_revision": REVISION,
                "license": "CC0-1.0",
                "variants": reports,
                "source_manifest_sha256": hashlib.sha256(args.manifest.read_bytes()).hexdigest(),
            },
            indent=2,
        ),
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
