"""Audit the saved Blender asset and refresh measured per-dummy bounds.

Run with Blender --background --python verify_source.py after the build finishes.
This script does not change the model or the FBX files.
"""
import json
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
bpy.ops.wm.open_mainfile(filepath=str(ROOT / "TrainingCourtyard.blend"))
bpy.context.view_layer.update()
report = json.loads((ROOT / "geometry_report.json").read_text(encoding="utf-8"))


def measure(objects):
    objects = [obj for obj in objects if obj.type == "MESH"]
    triangles = 0
    for obj in objects:
        obj.data.calc_loop_triangles()
        triangles += len(obj.data.loop_triangles)
    points = [obj.matrix_world @ Vector(corner) for obj in objects for corner in obj.bound_box]
    low = [min(p[i] for p in points) for i in range(3)]
    high = [max(p[i] for p in points) for i in range(3)]
    materials = sorted({mat.name for obj in objects for mat in obj.data.materials if mat})
    return {
        "mesh_objects": len(objects),
        "vertices": sum(len(obj.data.vertices) for obj in objects),
        "triangles": triangles,
        "materials": materials,
        "material_count": len(materials),
        "unity_bounds_m": {"min": [-high[0], low[2], -high[1]], "max": [-low[0], high[2], -low[1]]},
        "unity_dimensions_m": [high[0] - low[0], high[2] - low[2], high[1] - low[1]],
    }


dummy_results = []
for index, expected in enumerate([(-4, 0, 4), (0, 0, 5), (4, 0, 4)], 1):
    name = f"TrainingDummy_{index:02d}"
    obj = bpy.data.objects[name]
    translation = obj.matrix_world.translation
    position = (-translation.x, translation.z, -translation.y)
    assert all(abs(a-b) < .0001 for a, b in zip(position, expected)), (name, position, expected)
    report["dummies"][name] = measure([obj])
    dummy_results.append({"name": name, "unity_pivot_m": position, "passed": True})

materials = []
for material in bpy.data.materials:
    if material.name == "Material_0" or not material.use_nodes:
        continue
    outputs = [node for node in material.node_tree.nodes if node.type == "OUTPUT_MATERIAL" and node.is_active_output]
    assert len(outputs) == 1, (material.name, "Expected exactly one active material output")
    assert outputs[0].inputs["Surface"].is_linked, (material.name, "Disconnected material output")
    shader = outputs[0].inputs["Surface"].links[0].from_node
    assert shader.type == "BSDF_PRINCIPLED", (material.name, shader.type)
    materials.append({"name": material.name, "base_color_linear": list(shader.inputs["Base Color"].default_value), "passed": True})

environment = [obj for name in ("VIS_Architecture", "VIS_Paving", "VIS_Details", "VIS_Vegetation", "MESHY_Decor_AI_RuneObelisk")
               for obj in bpy.data.collections[name].objects]
visual = environment + list(bpy.data.collections["Props_TrainingDummies"].objects)
report["visual"] = measure(visual)
report["environment_only"] = measure(environment)
report["source_validation"] = "Passed verify_source.py: saved scene dummy pivots and active material graphs checked."
(ROOT / "geometry_report.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
(ROOT / "source_validation_report.json").write_text(json.dumps({
    "passed": True,
    "blender_version": bpy.app.version_string,
    "file": "TrainingCourtyard.blend",
    "dummy_positions": dummy_results,
    "material_graphs": materials,
    "visual": report["visual"],
    "note": "Source checks only; Unity importing is validated separately."
}, ensure_ascii=False, indent=2), encoding="utf-8")
print("COURTYARD_SOURCE_VALIDATION_PASSED")
