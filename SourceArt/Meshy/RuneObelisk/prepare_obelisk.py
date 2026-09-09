"""Normalize the downloaded Meshy GLB to meters; keep textures packed in Blender.

Run: E:/Blender/blender.exe --background --python prepare_obelisk.py
No API calls or credentials are used by this script.
"""
import bpy
import json
import math
from pathlib import Path
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parent
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(ROOT / 'RuneObelisk_Textured.glb'))
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if not meshes:
    raise RuntimeError('Meshy GLB contains no mesh')

# Bake imported hierarchy transforms, preserving geometry in world space.
for obj in meshes:
    world = obj.matrix_world.copy()
    obj.parent = None
    obj.matrix_world = world
corners = [o.matrix_world @ Vector(p) for o in meshes for p in o.bound_box]
lo = Vector(tuple(min(p[i] for p in corners) for i in range(3)))
hi = Vector(tuple(max(p[i] for p in corners) for i in range(3)))
factor = 3.0 / (hi.z - lo.z)
offset = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
for index, obj in enumerate(meshes):
    obj.name = 'SM_RuneObelisk' if len(meshes) == 1 else f'SM_RuneObelisk_{index:02}'
    world = obj.matrix_world.copy()
    for vert in obj.data.vertices:
        vert.co = (world @ vert.co - offset) * factor
    obj.matrix_world = Matrix.Identity(4)
    obj.data.update()
for obj in list(bpy.context.scene.objects):
    if obj.type != 'MESH':
        bpy.data.objects.remove(obj, do_unlink=True)

scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
bpy.ops.object.select_all(action='DESELECT')
for obj in meshes:
    obj.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
out = ROOT / 'Exports'
out.mkdir(exist_ok=True)
tex_dir = out / 'Textures'
tex_dir.mkdir(exist_ok=True)
for idx, img in enumerate(bpy.data.images):
    if img.type == 'IMAGE' and img.size[0] > 0:
        img.filepath_raw = str(tex_dir / f'Obelisk_{idx:02}.png')
        img.file_format = 'PNG'
        img.save()
        img.pack()
bpy.ops.export_scene.fbx(filepath=str(out / 'RuneObelisk_3m.fbx'), use_selection=True,
    object_types={'MESH'}, axis_forward='-Z', axis_up='Y', apply_unit_scale=True,
    apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True, bake_anim=False, add_leaf_bones=False,
    path_mode='COPY', embed_textures=True)
bpy.ops.export_scene.gltf(filepath=str(out / 'RuneObelisk_3m.glb'),
    export_format='GLB', use_selection=True)
triangles = 0
for obj in meshes:
    obj.data.calc_loop_triangles()
    triangles += len(obj.data.loop_triangles)
report = {'source': 'Meshy text-to-3d, meshy-6', 'height_m': 3,
    'bounds_m_blender_xyz': [round(float((hi-lo)[i] * factor), 5) for i in range(3)],
    'triangles': triangles, 'mesh_objects': len(meshes),
    'materials': sorted({m.name for o in meshes for m in o.data.materials if m}),
    'origin': 'bottom center', 'unity_collider': 'not created',
    'character_rig_or_animation': False}
(ROOT / 'geometry_report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')

def track(obj, target):
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()

# Studio camera and lights are preview-only, added after model exports.
bpy.ops.object.camera_add(location=(5.3, -7.5, 4.6))
camera = bpy.context.object
camera.name = 'PREVIEW_Camera'
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 4.3
track(camera, (0, 0, 1.45))
scene.camera = camera
for name, loc, energy, color, size in [
    ('Key', (3,-4,7), 800, (1, .86, .69), 5),
    ('Fill', (-4,-1,4), 500, (.63,.81,1), 4),
    ('Rim', (1,4,5), 900, (.76,.88,1), 3)]:
    bpy.ops.object.light_add(type='AREA', location=loc)
    light = bpy.context.object
    light.name = 'PREVIEW_' + name
    light.data.energy, light.data.color, light.data.shape, light.data.size = energy, color, 'DISK', size
    track(light, (0,0,1.4))
scene.world.color = (.13,.16,.20)
scene.render.engine = 'CYCLES'
scene.cycles.samples = 40
scene.cycles.use_denoising = True
scene.render.resolution_x = 1000
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.view_settings.view_transform = 'AgX'
scene.render.image_settings.file_format = 'PNG'
scene.render.film_transparent = False
scene.render.filepath = str(ROOT / 'obelisk_preview.png')
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / 'RuneObelisk_3m.blend'))
bpy.ops.render.render(write_still=True)
print('OBELISK_REPORT ' + json.dumps(report))
