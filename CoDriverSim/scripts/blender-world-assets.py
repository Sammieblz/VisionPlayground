"""Run with pinned Blender in background to audit/prepare supplied source assets.

Original .blend files are read with script execution disabled and never overwritten.
"""

import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def prepare_mercedes(output):
    """Separate physical moving parts in the authored mesh; keep its cabin intact."""
    import bmesh

    obj = bpy.data.objects.get("Mercedes Benz GLS 580")
    if obj is None:
        raise ValueError("Expected supplied GLS source mesh")
    for other in list(bpy.data.objects):
        if other != obj:
            bpy.data.objects.remove(other, do_unlink=True)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.context.view_layer.update()
    low = [min(v.co[i] for v in obj.data.vertices) for i in range(3)]
    high = [max(v.co[i] for v in obj.data.vertices) for i in range(3)]
    scale = 5.207 / (high[1] - low[1])
    centre = Vector(((low[0] + high[0]) / 2, (low[1] + high[1]) / 2, low[2]))
    for v in obj.data.vertices:
        v.co = (v.co - centre) * scale
    obj.location = (0, 0, 0)
    obj.name = "GLS_Body"
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.0002)
    bm.verts.index_update()
    duplicate_faces = []
    face_keys = set()
    for face in bm.faces:
        key = tuple(sorted(v.index for v in face.verts))
        if key in face_keys:
            duplicate_faces.append(face)
        else:
            face_keys.add(key)
    bmesh.ops.delete(bm, geom=duplicate_faces, context="FACES_ONLY")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.verts.ensure_lookup_table()
    bm.verts.index_update()
    # Centre the wheelbase; +X is the left side of this -Y-forward Blender model.
    tires = [
        v.co
        for f in bm.faces
        if obj.data.materials[f.material_index].name == "Tyres"
        for v in f.verts
    ]
    axle_y = []
    for sign in (-1, 1):
        values = [v.y for v in tires if v.y * sign > 0]
        axle_y.append((min(values) + max(values)) / 2)
    axle_centre = sum(axle_y) / 2
    for v in bm.verts:
        v.co.y -= axle_centre
    wheel_centres = []
    for front in (True, False):
        for left in (True, False):
            values = [v for v in tires if (v.y < 0) == front and (v.x > 0) == left]
            wheel_centres.append(
                Vector(
                    [(min(v[i] for v in values) + max(v[i] for v in values)) / 2 for i in range(3)]
                )
            )
    seen = set()
    islands = []
    groups = {"GLS_Body": []}
    anchors = {}
    for start in bm.verts:
        if start.index in seen:
            continue
        stack = [start]
        seen.add(start.index)
        verts = []
        while stack:
            v = stack.pop()
            verts.append(v)
            for edge in v.link_edges:
                other = edge.other_vert(v)
                if other.index not in seen:
                    seen.add(other.index)
                    stack.append(other)
        lo = [min(v.co[i] for v in verts) for i in range(3)]
        hi = [max(v.co[i] for v in verts) for i in range(3)]
        faces = {f for v in verts for f in v.link_faces}
        mats = sorted({obj.data.materials[f.material_index].name for f in faces})
        islands.append({"vertices": len(verts), "bounds": [lo, hi], "materials": mats})
        group = "GLS_Body"
        for i, centre in enumerate(wheel_centres):
            if all(
                abs(v.co.x - centre.x) < 0.18
                and (Vector((0, v.co.y - centre.y, v.co.z - centre.z))).length < 0.41
                for v in verts
            ):
                group = "Wheel_" + ["FL", "FR", "RL", "RR"][i]
                anchors[group] = centre
                break
        if (
            group == "GLS_Body"
            and lo[0] > 0.18
            and hi[0] < 0.61
            and lo[1] > -0.57
            and hi[1] < -0.30
            and lo[2] > 0.91
            and hi[2] < 1.38
        ):
            group = "SteeringWheel"
            anchors[group] = Vector((0.387, -0.4375, 1.15))
        groups.setdefault(group, []).extend(faces)
    output.mkdir(parents=True, exist_ok=True)
    (output / "mercedes-components.json").write_text(json.dumps(islands, indent=2))
    # The missing plate image is replaced by a neutral plate. Dashboard displays
    # are supplied separately by Unity; do not mislabel the missing source image.
    for mat in obj.data.materials:
        if not mat or not mat.use_nodes:
            continue
        p = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if not p:
            continue
        if mat.name == "material":
            for link in list(mat.node_tree.links):
                if link.to_node == p:
                    mat.node_tree.links.remove(link)
            p.inputs["Base Color"].default_value = (0.015, 0.02, 0.025, 1)
        if mat.name in {"WindowsTint", "Lights_Glass"}:
            p.inputs["Alpha"].default_value = 0.045 if mat.name == "WindowsTint" else 0.3
            p.inputs["Roughness"].default_value = 0.12
            p.inputs["Metallic"].default_value = 0
            mat.surface_render_method = "DITHERED"
        elif mat.name == "Polar_White":
            p.inputs["Metallic"].default_value = 0.6
            p.inputs["Roughness"].default_value = 0.24
        elif mat.name in {"Color_M01", "Color_M02", "Color_M03", "Color_M04"}:
            p.inputs["Metallic"].default_value = 0.35
            p.inputs["Roughness"].default_value = 0.42
        elif mat.name in {"Interior", "Tyres", "UnderCarriage"}:
            p.inputs["Roughness"].default_value = 0.82
        if mat.name == "Interior":
            leather = Path(__file__).resolve().parents[1] / "outputs/asset-downloads/leather"
            normal_file = leather / "Leather026_2K-JPG_NormalGL.jpg"
            if normal_file.exists():
                texture = mat.node_tree.nodes.new("ShaderNodeTexImage")
                texture.image = bpy.data.images.load(str(normal_file))
                texture.image.colorspace_settings.name = "Non-Color"
                normal = mat.node_tree.nodes.new("ShaderNodeNormalMap")
                normal.inputs["Strength"].default_value = 0.3
                mat.node_tree.links.new(texture.outputs["Color"], normal.inputs["Color"])
                mat.node_tree.links.new(normal.outputs["Normal"], p.inputs["Normal"])
    # Export disconnected semantic groups using the original material assignments.
    materials = list(obj.data.materials)
    for name, faces in groups.items():
        faces = sorted(faces, key=lambda face: face.index)
        vertices = list({v for f in faces for v in f.verts})
        lookup = {v: i for i, v in enumerate(vertices)}
        origin = anchors.get(name, Vector((0, 0, 0)))
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(
            [v.co - origin for v in vertices], [], [[lookup[v] for v in f.verts] for f in faces]
        )
        for mat in materials:
            mesh.materials.append(mat)
        for poly, face in zip(mesh.polygons, faces, strict=True):
            poly.material_index = face.material_index
            poly.use_smooth = face.smooth
        # The supplied cabin is largely unwrapped solid-color geometry. Give its
        # fine leather grain a consistent 15 cm world scale without stretching it
        # across the whole dashboard or steering wheel.
        uv = mesh.uv_layers.new(name="Material scale")
        for poly in mesh.polygons:
            dominant = max(range(3), key=lambda i: abs(poly.normal[i]))
            axes = [i for i in range(3) if i != dominant]
            for loop_index in poly.loop_indices:
                point = mesh.vertices[mesh.loops[loop_index].vertex_index].co + origin
                uv.data[loop_index].uv = (point[axes[0]] / 0.15, point[axes[1]] / 0.15)
        part = bpy.data.objects.new(name, mesh)
        bpy.context.collection.objects.link(part)
        part.location = origin
        part.select_set(True)
    bpy.data.objects.remove(obj, do_unlink=True)
    bm.free()
    (output / "anchors.json").write_text(
        json.dumps({k: list(v) for k, v in anchors.items()}, indent=2)
    )
    bpy.ops.export_scene.gltf(
        filepath=str(output / "MercedesSource.glb"),
        export_format="GLB",
        use_selection=True,
        export_apply=True,
        export_yup=True,
        export_cameras=False,
        export_lights=False,
    )
    # A compact orthographic inspection render documents orientation/materials.
    bpy.ops.object.camera_add(location=(6, -8, 4))
    camera = bpy.context.object
    camera.rotation_euler = (
        (Vector((0, 0, 0.9)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    )
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 7
    bpy.context.scene.camera = camera
    bpy.context.scene.render.engine = "BLENDER_WORKBENCH"
    bpy.context.scene.display.shading.light = "STUDIO"
    bpy.context.scene.display.shading.color_type = "MATERIAL"
    bpy.context.scene.render.resolution_x = 960
    bpy.context.scene.render.resolution_y = 640
    bpy.context.scene.render.resolution_percentage = 100
    bpy.context.scene.render.filepath = str(output / "mercedes-inspection.png")
    bpy.ops.render.render(write_still=True)
    camera.data.type = "PERSP"
    camera.data.lens = 25
    for distance in (-0.33, 0, 0.25):
        camera.location = (0.387, -distance, 1.48)
        camera.rotation_euler = Vector((0, -1, -0.10)).to_track_quat("-Z", "Y").to_euler()
        bpy.context.scene.render.filepath = str(output / f"cabin-{distance}.png")
        bpy.ops.render.render(write_still=True)


def bounds(obj):
    corners = [obj.matrix_world @ Vector(p) for p in obj.bound_box]
    return [[round(fn(p[i] for p in corners), 5) for i in range(3)] for fn in (min, max)]


def prepare_signs(output):
    output.mkdir(parents=True, exist_ok=True)
    for obj in list(bpy.data.objects):
        if obj.type != "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)
            continue
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        low, high = bounds(obj)
        centre = Vector(((low[0] + high[0]) / 2, 0, low[2]))
        for vertex in obj.data.vertices:
            vertex.co = (vertex.co - centre) * 0.393
        obj.location = (0, 0, 0)
        modifier = obj.modifiers.new("Coplanar simplification", "DECIMATE")
        modifier.decimate_type = "DISSOLVE"
        modifier.angle_limit = math.radians(2)
        modifier.delimit = {"MATERIAL", "UV", "SHARP"}
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    for material in bpy.data.materials:
        image = (
            next(
                (
                    node.image
                    for node in material.node_tree.nodes
                    if node.type == "TEX_IMAGE" and node.image
                ),
                None,
            )
            if material.use_nodes
            else None
        )
        material.use_nodes = True
        nodes = material.node_tree.nodes
        nodes.clear()
        shader = nodes.new("ShaderNodeBsdfPrincipled")
        out = nodes.new("ShaderNodeOutputMaterial")
        material.node_tree.links.new(shader.outputs["BSDF"], out.inputs["Surface"])
        color = tuple(material.diffuse_color)
        if material.name == "Steel":
            color = (0.42, 0.44, 0.46, 1)
            shader.inputs["Metallic"].default_value = 0.75
        shader.inputs["Base Color"].default_value = color
        shader.inputs["Roughness"].default_value = 0.58
        if image:
            tex = nodes.new("ShaderNodeTexImage")
            tex.image = image
            material.node_tree.links.new(tex.outputs["Color"], shader.inputs["Base Color"])
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(
        filepath=str(output / "StreetSigns.glb"),
        export_format="GLB",
        export_apply=True,
        export_cameras=False,
        export_lights=False,
    )


def audit(path):
    if path.suffix.lower() == ".blend":
        bpy.ops.wm.open_mainfile(filepath=str(path), load_ui=False, use_scripts=False)
    else:
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.object.delete()
        bpy.ops.import_scene.gltf(filepath=str(path))
    objects = []
    for obj in bpy.data.objects:
        if obj.type not in {"MESH", "ARMATURE"}:
            continue
        row = {
            "name": obj.name,
            "type": obj.type,
            "bounds": bounds(obj),
            "hidden_render": obj.hide_render,
            "hidden_view": obj.hide_get(),
            "scale": list(obj.scale),
        }
        if obj.type == "MESH":
            row.update(
                vertices=len(obj.data.vertices),
                polygons=len(obj.data.polygons),
                materials=[m.name if m else None for m in obj.data.materials],
                modifiers=[(m.name, m.type) for m in obj.modifiers],
            )
        else:
            row["bones"] = [b.name for b in obj.data.bones]
        objects.append(row)
    materials = []
    for m in bpy.data.materials:
        materials.append(
            {
                "name": m.name,
                "color": list(m.diffuse_color),
                "nodes": [(n.type, n.name) for n in m.node_tree.nodes] if m.use_nodes else [],
            }
        )
    return {
        "source": str(path),
        "blender": bpy.app.version_string,
        "objects": objects,
        "materials": materials,
        "images": [
            {
                "name": i.name,
                "path": i.filepath,
                "packed": bool(i.packed_file),
                "size": list(i.size),
            }
            for i in bpy.data.images
        ],
        "actions": [a.name for a in bpy.data.actions],
    }


def prepare_facades(output):
    names = {
        "wall_window_centered_large_01",
        "window_centered_large_01",
        "wall_window_centered_large_02",
        "window_centered_large_02",
        "wall_window_centered_large_03",
        "window_centered_large_03",
        "wall_door_window_small_01",
        "door_window_small_01",
        "wall_door_centered_large_01",
        "door_centered_large_01",
        "cornice_standard_standard_01",
        "crown_standard_standard_01",
        "dado_standard_standard_01",
    }
    output.mkdir(parents=True, exist_ok=True)
    report = []
    for obj in list(bpy.data.objects):
        if obj.name not in names:
            bpy.data.objects.remove(obj, do_unlink=True)
            continue
        bpy.ops.object.select_all(action="DESELECT")
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        before = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        modifier = obj.modifiers.new("Remove planar tessellation", "DECIMATE")
        modifier.decimate_type = "DISSOLVE"
        modifier.angle_limit = math.radians(1)
        modifier.delimit = {"MATERIAL", "UV"}
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        after = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        report.append(dict(name=obj.name, triangles_before=before, triangles_after=after))
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.gltf(
        filepath=str(output / "FacadePrepared.glb"),
        export_format="GLB",
        export_apply=True,
        export_cameras=False,
        export_lights=False,
    )
    (output / "simplification.json").write_text(json.dumps(report, indent=2))
    print("FACADE_SIMPLIFICATION", report)


def prepare_person(output):
    """Modernize the supplied civilian's rest rig for the existing licensed UAL clips."""
    output.mkdir(parents=True, exist_ok=True)
    armature = bpy.data.objects["Armature"]
    character = bpy.data.objects["Low Poly Characte.001"]
    for obj in list(bpy.data.objects):
        if obj not in (armature, character):
            bpy.data.objects.remove(obj, do_unlink=True)
    armature.animation_data_clear()
    armature.data.pose_position = "REST"
    for bone in armature.pose.bones:
        for constraint in list(bone.constraints):
            bone.constraints.remove(constraint)
    bpy.ops.object.select_all(action="DESELECT")
    character.select_set(True)
    bpy.context.view_layer.objects.active = character
    subdivision = next(m for m in character.modifiers if m.type == "SUBSURF")
    subdivision.levels = 1
    bpy.ops.object.modifier_apply(modifier=subdivision.name)
    # The original shirt image is an unfinished UV checker. Replace it with the
    # pinned CC0 fabric while preserving the painted jeans and their UV assignment.
    fabric = (
        Path(__file__).resolve().parents[1]
        / "game/Assets/External/Resources/WorldArt/Fabric/Fabric034_2K-JPG_Color.jpg"
    )
    mappings = {"shirt": ("t shirt.004", "Main Texture"), "jeans": ("jeans", "main jeans")}
    mapped_uv = {}
    for polygon in character.data.polygons:
        name = character.data.materials[polygon.material_index].name
        source = character.data.uv_layers[mappings.get(name, ("", "Main Texture"))[1]]
        for index in polygon.loop_indices:
            mapped_uv[index] = source.data[index].uv.copy()
    while character.data.uv_layers:
        character.data.uv_layers.remove(character.data.uv_layers[0])
    uv = character.data.uv_layers.new(name="Portable UV")
    for index, value in mapped_uv.items():
        uv.data[index].uv = value
    for material in character.data.materials:
        color = tuple(material.diffuse_color)
        material.use_nodes = True
        material.node_tree.nodes.clear()
        shader = material.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
        shader.inputs["Base Color"].default_value = color
        shader.inputs["Roughness"].default_value = 0.8
        output_node = material.node_tree.nodes.new("ShaderNodeOutputMaterial")
        material.node_tree.links.new(shader.outputs["BSDF"], output_node.inputs["Surface"])
        if material.name in mappings:
            image = (
                bpy.data.images.load(str(fabric))
                if material.name == "shirt"
                else bpy.data.images[mappings[material.name][0]]
            )
            texture = material.node_tree.nodes.new("ShaderNodeTexImage")
            texture.image = image
            material.node_tree.links.new(texture.outputs["Color"], shader.inputs["Base Color"])
            image.filepath_raw = str(output / (material.name + ".png"))
            image.file_format = "PNG"
            image.save()
    bpy.context.view_layer.update()
    measured = bounds(character)
    scale = 1.78 / (measured[1][2] - measured[0][2])
    bpy.ops.object.select_all(action="SELECT")
    armature.data.pose_position = "POSE"
    for bone in armature.pose.bones:
        bone.matrix_basis.identity()
    bpy.ops.export_scene.fbx(
        filepath=str(output / "Civilian.fbx"),
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
    (output / "preparation.json").write_text(
        json.dumps(
            {
                "height_m": 1.78,
                "scale": scale,
                "source_rig": "brightonpiers walking man",
                "animation": "Retargeted separately from pinned UAL1",
            },
            indent=2,
        )
    )


def prepare_fleet(output):
    """Derive two NPC detail levels from the already pinned licensed CarConcept."""
    import bmesh
    import numpy as np
    from mathutils import Matrix

    output.mkdir(parents=True, exist_ok=True)

    def ancestry(obj):
        names = []
        while obj:
            names.append(obj.name)
            obj = obj.parent
        return names

    # Several tire meshes have numeric names. Preserve their parent wheel role
    # before flattening the glTF hierarchy; otherwise the tires end up welded to
    # the body while only the rims animate.
    meshes = [
        o
        for o in bpy.data.objects
        if o.type == "MESH" and not any(n.startswith("Interior") for n in ancestry(o))
    ]
    groups = {}
    wheel_names = ["WheelFrontL", "WheelFrontR", "WheelRearL", "WheelRearR"]
    for obj in meshes:
        role = next((k for k in wheel_names if any(n.startswith(k) for n in ancestry(obj))), "Body")
        groups.setdefault(role, []).append(obj)
    matrices = {o: o.matrix_world.copy() for o in meshes}
    for obj in list(bpy.data.objects):
        if obj not in meshes:
            bpy.data.objects.remove(obj, do_unlink=True)
    for obj in meshes:
        matrix = matrices[obj]
        obj.parent = None
        obj.data.transform(matrix)
        cleaned = bmesh.new()
        cleaned.from_mesh(obj.data)
        bmesh.ops.remove_doubles(cleaned, verts=list(cleaned.verts), dist=0.00005)
        bmesh.ops.recalc_face_normals(cleaned, faces=list(cleaned.faces))
        cleaned.to_mesh(obj.data)
        cleaned.free()
        obj.matrix_world = Matrix.Identity(4)
        obj.animation_data_clear()
    # Front wheels in the sample are steered. Recover the axle from each rim's
    # principal plane before centring the wheel group on the simulation anchors.
    centres = {
        key: Vector(
            np.array(
                [tuple(v.co) for o in objects if o.name.endswith("Rim") for v in o.data.vertices]
            ).mean(axis=0)
        )
        for key, objects in groups.items()
        if key != "Body"
    }
    front_y = sum(p.y for k, p in centres.items() if "Front" in k) / 2
    rear_y = sum(p.y for k, p in centres.items() if "Rear" in k) / 2
    length_scale = 2.7 / (rear_y - front_y)
    width_scale = 0.78 / (sum(abs(p.x) for p in centres.values()) / 4)
    centre_y = (front_y + rear_y) * 0.5
    height_scale = 0.87
    height_offset = 0.34 - sum(p.z for p in centres.values()) / 4 * height_scale
    for obj in groups["Body"]:
        for vertex in obj.data.vertices:
            p = vertex.co
            vertex.co = (
                p.x * width_scale,
                (p.y - centre_y) * length_scale,
                p.z * height_scale + height_offset,
            )
    for key, objects in groups.items():
        if key == "Body":
            continue
        rim = next(o for o in objects if o.name.endswith("Rim"))
        coordinates = np.array([tuple(v.co) for v in rim.data.vertices])
        centre = Vector(coordinates.mean(axis=0))
        values, axes = np.linalg.eigh(np.cov(coordinates.T))
        axis = Vector(axes[:, np.argmin(values)])
        if axis.x < 0:
            axis = -axis
        rotation = axis.rotation_difference(Vector((1, 0, 0)))
        radius = max(
            (rotation @ (v.co - centre)).yz.length for obj in objects for v in obj.data.vertices
        )
        anchor = Vector(
            (0.78 if key.endswith("L") else -0.78, -1.35 if "Front" in key else 1.35, 0.34)
        )
        for obj in objects:
            for vertex in obj.data.vertices:
                vertex.co = rotation @ (vertex.co - centre) * (0.34 / radius) + anchor
    report = []
    for lod, ratio in enumerate([0.6, 0.12]):
        root = bpy.data.objects.new("LOD" + str(lod), None)
        bpy.context.collection.objects.link(root)
        for key, objects in groups.items():
            copies = []
            for source in objects:
                copy = source.copy()
                copy.data = source.data.copy()
                bpy.context.collection.objects.link(copy)
                bpy.ops.object.select_all(action="DESELECT")
                copy.select_set(True)
                bpy.context.view_layer.objects.active = copy
                modifier = copy.modifiers.new("NPC detail budget", "DECIMATE")
                modifier.ratio = max(ratio, 0.65 if lod == 0 else 0.18) if key != "Body" else ratio
                bpy.ops.object.modifier_apply(modifier=modifier.name)
                copies.append(copy)
            bpy.ops.object.select_all(action="DESELECT")
            for copy in copies:
                copy.select_set(True)
            bpy.context.view_layer.objects.active = copies[0]
            bpy.ops.object.join()
            part = copies[0]
            part.name = key + "_LOD" + str(lod)
            if key != "Body":
                anchor = Vector(
                    (0.78 if key.endswith("L") else -0.78, -1.35 if "Front" in key else 1.35, 0.34)
                )
                for vertex in part.data.vertices:
                    vertex.co -= anchor
                part.location = anchor
            part.parent = root
            report.append(
                {
                    "name": part.name,
                    "triangles": sum(len(p.vertices) - 2 for p in part.data.polygons),
                }
            )
    for obj in meshes:
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.export_scene.gltf(
        filepath=str(output / "ConceptFleet.glb"),
        export_format="GLB",
        export_animations=False,
        export_cameras=False,
        export_lights=False,
        export_apply=True,
    )
    (output / "geometry-budget.json").write_text(json.dumps(report, indent=2))
    print("FLEET_GEOMETRY", report)


def prepare_cat(output):
    """Bake the supplied CC-BY rig; runtime never evaluates Blender constraints/hair."""
    from mathutils import Matrix, Quaternion

    output.mkdir(parents=True, exist_ok=True)
    armature = bpy.data.objects["Armature"]
    reference_pose = {b.name: b.matrix_basis.copy() for b in armature.pose.bones}
    kept = [bpy.data.objects[n] for n in ["Armature", "Cat", "Sphere", "Sphere.001"]]
    for obj in list(bpy.data.objects):
        if obj not in kept and obj.name != "Empty":
            bpy.data.objects.remove(obj, do_unlink=True)
    # Two legacy controller constraints refer to their own descendants. Their
    # circular dependencies are invalid in modern Blender; the authored limb IK
    # and action curves remain intact and are sampled into the exported bones.
    for bone_name, kind in [("Spine", "PIVOT"), ("Tail", "COPY_ROTATION")]:
        bone = armature.pose.bones[bone_name]
        for constraint in list(bone.constraints):
            if constraint.type == kind:
                bone.constraints.remove(constraint)
    for obj in kept:
        if obj.type != "MESH":
            continue
        for modifier in list(obj.modifiers):
            if modifier.type == "PARTICLE_SYSTEM":
                obj.modifiers.remove(modifier)
            elif modifier.type == "SUBSURF":
                modifier.levels = modifier.render_levels = 1
        for material in obj.data.materials:
            if not material or material.get("codriver_prepared"):
                continue
            source_image = next(
                (n.image for n in material.node_tree.nodes if n.type == "TEX_IMAGE" and n.image),
                None,
            )
            material.use_nodes = True
            material.node_tree.nodes.clear()
            shader = material.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
            shader.inputs["Roughness"].default_value = 0.78
            shader.inputs["Base Color"].default_value = (0.035, 0.032, 0.030, 1)
            terminal = material.node_tree.nodes.new("ShaderNodeOutputMaterial")
            material.node_tree.links.new(shader.outputs["BSDF"], terminal.inputs["Surface"])
            if source_image:
                texture = material.node_tree.nodes.new("ShaderNodeTexImage")
                texture.image = source_image
                material.node_tree.links.new(texture.outputs["Color"], shader.inputs["Base Color"])
                source_image.filepath_raw = str(output / (material.name + ".png"))
                source_image.file_format = "PNG"
                source_image.save()
            if material.name == "Cornea":
                shader.inputs["Base Color"].default_value = (0.02, 0.025, 0.018, 0.06)
                shader.inputs["Alpha"].default_value = 0.06
                shader.inputs["Roughness"].default_value = 0.08
            material["codriver_prepared"] = True
    for action in list(bpy.data.actions):
        if action.name not in {"Walk", "run"}:
            bpy.data.actions.remove(action)
    # Bake each evaluated rig explicitly before exporting. Letting FBX switch
    # between controller actions can evaluate legacy IK/NLA state incorrectly.
    # The exported actions contain only sampled bone transforms, no constraints.
    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    for track in armature.animation_data.nla_tracks:
        track.mute = True
    for variant, name in enumerate(["Idle_Breathe", "Idle_Watch"]):
        armature.animation_data.action = bpy.data.actions.new(name)
        for bone in armature.pose.bones:
            bone.matrix_basis = Matrix.Identity(4)
        for frame in [1, 25, 49, 73, 97]:
            phase = (frame - 1) / 96 * math.tau
            armature.pose.bones["Spine_controller_1"].location.z = 0.035 * math.sin(phase)
            armature.pose.bones["Tail_controller"].location.x = 0.3 * math.sin(phase)
            head = armature.pose.bones["Head_controller"]
            head.rotation_mode = "QUATERNION"
            head.rotation_quaternion = Quaternion(
                (0, 0, 1), math.sin(phase) * (0.16 if variant else 0.025)
            )
            for bone in armature.pose.bones:
                bone.keyframe_insert("location", frame=frame)
                bone.keyframe_insert(
                    "rotation_quaternion"
                    if bone.rotation_mode == "QUATERNION"
                    else "rotation_euler",
                    frame=frame,
                )
                bone.keyframe_insert("scale", frame=frame)
    baked = []
    names = ["Walk", "run", "Idle_Breathe", "Idle_Watch"]
    for name in names:
        armature.animation_data.action = None
        for bone in armature.pose.bones:
            bone.matrix_basis = reference_pose[bone.name]
        action = bpy.data.actions[name]
        armature.animation_data.action = action
        start, end = (int(v) for v in action.frame_range)
        bpy.context.scene.frame_set(start)
        bpy.ops.nla.bake(
            frame_start=start,
            frame_end=end,
            step=1,
            only_selected=False,
            visual_keying=True,
            clear_constraints=False,
            use_current_action=False,
            bake_types={"POSE"},
        )
        result = armature.animation_data.action
        result.name = name + "_baked"
        baked.append(result)
    for bone in armature.pose.bones:
        for constraint in list(bone.constraints):
            bone.constraints.remove(constraint)
    for action in list(bpy.data.actions):
        if action not in baked:
            bpy.data.actions.remove(action)
    for action, name in zip(baked, names, strict=True):
        action.name = name
    for obj in list(bpy.data.objects):
        if obj not in kept:
            bpy.data.objects.remove(obj, do_unlink=True)
    samples = []
    watched = ["Spine", "Foot_front_L", "Foot_Back_L", "Head_controller"]
    for action in baked:
        armature.animation_data.action = action
        armature.animation_data.action_slot = action.slots[0]
        for frame in [1, int(action.frame_range[1] / 2), int(action.frame_range[1])]:
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            samples.append(
                (
                    action.name,
                    frame,
                    {
                        name: (
                            armature.matrix_world @ armature.pose.bones[name].matrix
                        ).translation.copy()
                        * 0.08
                        for name in watched
                    },
                )
            )
    armature.animation_data.action = baked[2]
    armature.animation_data.action_slot = baked[2].slots[0]
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=str(output / "Cat.fbx"),
        use_selection=True,
        object_types={"ARMATURE", "MESH"},
        global_scale=0.08,
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_step=1,
        bake_anim_simplify_factor=0,
        path_mode="COPY",
        embed_textures=True,
        use_mesh_modifiers=True,
    )
    (output / "preparation.json").write_text(
        json.dumps(
            {
                "author": "JonasDichelle",
                "license": "CC-BY-3.0",
                "source_url": "https://blendswap.com/blend/18519",
                "changes": [
                    "Removed particle hair and two circular controller constraints",
                    "Baked original Walk/run bones and authored two standing idle variations",
                    "Converted materials to portable PBR",
                ],
                "scale": 0.08,
                "source_forward": "-Y",
                "unity_forward": "+Z",
            },
            indent=2,
        )
    )
    # Round-trip evaluated poses, not merely the presence of named FBX clips.
    # Blender 4.5 requires selecting the imported action slot explicitly.
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(output / "Cat.fbx"))
    imported = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    maximum_error = 0
    for name, frame, poses in samples:
        action = next(a for a in bpy.data.actions if a.name.endswith("|" + name))
        imported.animation_data.action = action
        imported.animation_data.action_slot = action.slots[0]
        bpy.context.scene.frame_set(frame)
        bpy.context.view_layer.update()
        for bone, expected in poses.items():
            actual = (imported.matrix_world @ imported.pose.bones[bone].matrix).translation
            maximum_error = max(maximum_error, (actual - expected).length)
    if maximum_error > 0.002:
        raise ValueError(
            f"Cat animation round-trip changed bone positions by {maximum_error:.4f} m"
        )
    (output / "animation-check.json").write_text(
        json.dumps(
            {
                "clips": names,
                "poses": len(samples),
                "maximum_bone_error_m": maximum_error,
                "tolerance_m": 0.002,
            },
            indent=2,
        )
    )


def prepare_buildings(output):
    """Reuse the supplied low-rise geometry with independent CC0 surface maps."""
    source = bpy.data.objects.get("building 7")
    if source is None or len(source.data.vertices) != 2736:
        raise ValueError("Expected audited Prop Buildings low-rise geometry")
    for obj in list(bpy.data.objects):
        if obj != source:
            bpy.data.objects.remove(obj, do_unlink=True)
    bpy.context.view_layer.objects.active = source
    source.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    lo = Vector([min(v.co[a] for v in source.data.vertices) for a in range(3)])
    hi = Vector([max(v.co[a] for v in source.data.vertices) for a in range(3)])
    centre = (lo + hi) * 0.5
    centre.z = lo.z
    scale = 12 / (hi.x - lo.x)
    for vertex in source.data.vertices:
        vertex.co = (vertex.co - centre) * scale
    source.location = (0, 0, 0)
    source.name = "Lowrise_LOD0"
    source.data.update()
    # The source's embedded TexturesCom images are deliberately not exported.
    # Retain recessed windows/cornices, replacing all material slots and UVs.
    source.data.materials.clear()
    materials = Path(__file__).resolve().parents[1] / "game/Assets/External/Resources/Materials"
    for name, color, roughness, texture in (
        ("Lowrise_Plaster", (0.86, 0.80, 0.69, 1), 0.85, "Plaster.jpg"),
        ("Lowrise_Window", (0.035, 0.075, 0.10, 1), 0.3, None),
        ("Lowrise_Trim", (0.65, 0.61, 0.53, 1), 0.7, None),
        ("Lowrise_Roof", (0.35, 0.34, 0.31, 1), 0.9, "Roof.jpg"),
    ):
        material = bpy.data.materials.new(name)
        material.use_nodes = True
        shader = material.node_tree.nodes.get("Principled BSDF")
        shader.inputs["Base Color"].default_value = color
        shader.inputs["Roughness"].default_value = roughness
        if texture:
            image = material.node_tree.nodes.new("ShaderNodeTexImage")
            image.image = bpy.data.images.load(str(materials / texture))
            material.node_tree.links.new(image.outputs["Color"], shader.inputs["Base Color"])
        source.data.materials.append(material)
    for layer in list(source.data.uv_layers):
        source.data.uv_layers.remove(layer)
    uv = source.data.uv_layers.new(name="Metres")
    height = (hi.z - lo.z) * scale
    for face in source.data.polygons:
        axis = max(range(3), key=lambda a: abs(face.normal[a]))
        if axis == 2:
            face.material_index = 3 if face.center.z > height - 0.35 else 2
        else:
            wall = 5.83 if axis == 0 else 5.81
            depth = abs(face.center[axis])
            face.material_index = (
                1 if wall - 0.15 < depth < wall - 0.06 else 2 if depth > wall + 0.02 else 0
            )
        axes = [a for a in range(3) if a != axis]
        for loop in face.loop_indices:
            point = source.data.vertices[source.data.loops[loop].vertex_index].co
            uv.data[loop].uv = (point[axes[0]] / 2, point[axes[1]] / 2)
    # Preserve the architectural silhouette at both detail levels.
    lod = source.copy()
    lod.data = source.data.copy()
    lod.name = "Lowrise_LOD1"
    bpy.context.collection.objects.link(lod)
    bpy.context.view_layer.objects.active = lod
    modifier = lod.modifiers.new("Distant planar reduction", "DECIMATE")
    modifier.decimate_type = "DISSOLVE"
    modifier.angle_limit = math.radians(2)
    modifier.delimit = {"MATERIAL"}
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    output.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=str(output / "Lowrise.glb"),
        export_format="GLB",
        export_apply=True,
        export_cameras=False,
        export_lights=False,
    )
    (output / "preparation.json").write_text(
        json.dumps(
            {
                "source_object": "building 7",
                "width_m": 12,
                "height_m": height,
                "materials_replaced": True,
                "source_images_included": False,
                "triangles": {
                    o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in (source, lod)
                },
            },
            indent=2,
        ),
        encoding="utf-8",
    )


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument(
        "--prepare", choices=["mercedes", "signs", "facades", "person", "fleet", "cat", "buildings"]
    )
    parser.add_argument("--output", type=Path)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1 :])
    result = audit(args.source.resolve())
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print("ASSET_AUDIT", args.report)
    if args.prepare:
        if args.output is None:
            raise ValueError("--output is required for preparation")
        {
            "mercedes": prepare_mercedes,
            "signs": prepare_signs,
            "facades": prepare_facades,
            "person": prepare_person,
            "fleet": prepare_fleet,
            "cat": prepare_cat,
            "buildings": prepare_buildings,
        }[args.prepare](args.output.resolve())


if __name__ == "__main__":
    main()
