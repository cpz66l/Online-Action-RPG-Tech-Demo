"""Build an original procedural training courtyard plus optional AI decor in Blender 5.x.

Run: E:/Blender/blender.exe --background --python build_courtyard.py
Options after --: --skip-render, --samples 48
All design coordinates are Unity meters: X right, Y up, Z toward the arch.
Blender coordinates = (-Unity.x, -Unity.z, Unity.y), including the X handedness
compensation measured in Unity's FBX importer with this export configuration.
"""
from __future__ import annotations

import argparse
import json
import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
EXPORT = ROOT / "Exports"
PREVIEW = ROOT / "Previews"
for directory in (ROOT, EXPORT, PREVIEW):
    directory.mkdir(parents=True, exist_ok=True)
args = argparse.ArgumentParser()
args.add_argument("--skip-render", action="store_true")
args.add_argument("--samples", type=int, default=48)
opts = args.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
random.seed(404)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
for collection in list(bpy.data.collections):
    if collection.name != "Collection":
        bpy.data.collections.remove(collection)
default_collection = bpy.data.collections.get("Collection")
if default_collection:
    bpy.data.collections.remove(default_collection)


def collection(name):
    result = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(result)
    return result


ARCH = collection("VIS_Architecture")
FLOOR = collection("VIS_Paving")
DETAIL = collection("VIS_Details")
PLANTS = collection("VIS_Vegetation")
MESHY = collection("MESHY_Decor_AI_RuneObelisk")
DUMMIES = collection("Props_TrainingDummies")
ANCHORS = collection("Anchors_UnityMeters")
COLLISION = collection("COLLISION_Proxies_NotUnityColliders")
STUDIO = collection("PREVIEW_ONLY_CamerasLightingGround")
SCALE = collection("PREVIEW_ONLY_1p8mScaleFigure")


def u(point):
    x, y, z = point
    return (-x, -z, y)


def linear(c):
    return c / 12.92 if c < 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def material(name, hex_color, roughness=0.8, metallic=0.0):
    color = tuple(linear(int(hex_color[i:i + 2], 16) / 255) for i in (0, 2, 4))
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    # Blender can localize default node names. Clear the graph rather than
    # appending a second shader beside the still-active default output.
    mat.node_tree.nodes.clear()
    bsdf = mat.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
    output = mat.node_tree.nodes.new("ShaderNodeOutputMaterial")
    output.is_active_output = True
    mat.node_tree.links.new(bsdf.outputs["BSDF"],output.inputs["Surface"])
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    return mat


MAT = {
    "basalt": material("Stone_Basalt_BlueGrey", "394653"),
    "basalt_edge": material("Stone_Basalt_Edge", "526272"),
    "mortar": material("Stone_Mortar", "6C7778"),
    "pave0": material("Stone_Paving_Warm", "B9B8A5"),
    "pave1": material("Stone_Paving_Light", "C5C3AE"),
    "pave2": material("Stone_Paving_Cool", "A7B0A8"),
    "pave3": material("Stone_Paving_Grey", "9EA79F"),
    "sand": material("Stone_Sandstone", "CCB995"),
    "sand_light": material("Stone_Sandstone_Cap", "E5D3AA"),
    "sand_dark": material("Stone_Sandstone_Shade", "B5A487"),
    "teal": material("Cloth_PatinaTeal", "277A78", 0.95),
    "gold": material("Metal_AgedBrass", "BE9452", 0.4, 0.58),
    "iron": material("Metal_DarkIron", "3C444C", 0.53, 0.6),
    "wood": material("Wood_DarkOak", "795235"),
    "wood_light": material("Wood_Heartwood", "AF8350"),
    "rope": material("Rope_NaturalFlax", "C8B587"),
    "leaf": material("Plant_Sage", "618068"),
    "leaf_light": material("Plant_Fern", "809577"),
    "grass": material("Plant_DryGrass", "949965"),
    "preview_ground": material("Preview_Backdrop", "B6C4C5"),
    "preview_figure": material("Preview_ScaleFigure", "38676B", 0.65),
}


def move(obj, target):
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    target.objects.link(obj)
    return obj


def attach(obj, name, mat, target):
    obj.name = name
    move(obj, target)
    if mat:
        obj.data.materials.append(MAT[mat] if isinstance(mat, str) else mat)
    return obj


def box(name, center, size, mat, target=ARCH, bevel=0.04, segments=2):
    bpy.ops.mesh.primitive_cube_add(size=1, location=u(center))
    obj = attach(bpy.context.object, name, mat, target)
    obj.scale = (size[0], size[2], size[1])
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new("Crafted softened stone edges", "BEVEL")
        mod.width = bevel
        mod.segments = segments
        mod.affect = "EDGES"
    return obj


def mesh(name, vertices, faces, mat, target=DETAIL, bevel=0):
    data = bpy.data.meshes.new(name)
    data.from_pydata([u(v) for v in vertices], [], [tuple(reversed(face)) for face in faces])
    data.update()
    obj = bpy.data.objects.new(name, data)
    target.objects.link(obj)
    if mat:
        data.materials.append(MAT[mat] if isinstance(mat, str) else mat)
    if bevel:
        mod = obj.modifiers.new("Edge wear", "BEVEL")
        mod.width = bevel
        mod.segments = 2
    return obj


def cylinder(name, center, radius, depth, mat, target=DETAIL, vertices=12, bevel=0.025):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=u(center))
    obj = attach(bpy.context.object, name, mat, target)
    if bevel:
        mod = obj.modifiers.new("Rounded end grain", "BEVEL")
        mod.width = bevel
        mod.segments = 2
    return obj


def beam(name, a, b, radius, mat, target=DETAIL, vertices=10):
    av, bv = Vector(u(a)), Vector(u(b))
    obj = cylinder(name, (0, 0, 0), radius, (bv - av).length, mat, target, vertices, 0.012)
    obj.location = (av + bv) / 2
    obj.rotation_euler = (bv - av).to_track_quat("Z", "Y").to_euler()
    return obj


def torus(name, center, major, minor, mat, target=DETAIL):
    bpy.ops.mesh.primitive_torus_add(major_radius=major, minor_radius=minor, major_segments=24,
                                   minor_segments=6, location=u(center))
    return attach(bpy.context.object, name, mat, target)


def empty(name, point, size=0.65):
    obj = bpy.data.objects.new(name, None)
    ANCHORS.objects.link(obj)
    obj.location = u(point)
    obj.empty_display_type = "ARROWS"
    obj.empty_display_size = size
    obj["unity_position_m"] = [float(v) for v in point]
    return obj


def proxy(name, center, size):
    obj = box(name, center, size, None, COLLISION, 0)
    obj.display_type = "WIRE"
    obj.hide_render = True
    obj["purpose"] = "Geometry proxy only. Add Unity BoxCollider or MeshCollider during integration."
    return obj


# A clean, layered 28 m square foundation. Walkable top is Y = 0.
box("Foundation_LowerPlinth", (0, -1.08, 0), (27.65, .48, 27.65), "basalt", bevel=.17)
box("Foundation_ContinuousReveal", (0, -.79, 0), (27.78, .13, 27.78), "basalt_edge", bevel=.03)
box("Foundation_MainBed", (0, -.44, 0), (28, .58, 28), "basalt", bevel=.10)
box("Foundation_UpperLip", (0, -.18, 0), (27.92, .17, 27.92), "basalt_edge", bevel=.04)
box("Paving_MortarBed", (0, -.12, 0), (27.6, .18, 27.6), "mortar", FLOOR, .02)

# Core: running-bond stone courses, all tops precisely on the same plane.
for row in range(20):
    z = -9.5 + row
    x = -10.0
    lengths = ([1.] + [2.] * 9 + [1.]) if row % 2 else [2.] * 10
    for column, length in enumerate(lengths):
        color = random.choices(["pave0", "pave1", "pave2", "pave3"], [6, 3, 3, 1])[0]
        box(f"CorePaver_{row:02d}_{column:02d}", (x + length / 2, -.10, z),
            (length - .028, .20, .974), color, FLOOR, .022, 1)
        x += length

# Outer frame is readable in gameplay, but has no raised trip hazards.
for side in [-1, 1]:
    box(f"Border_Inlay_X{side}", (side * 10.11, -.075, 0), (.16, .15, 20.40), "basalt_edge", FLOOR, .012)
    box(f"Border_Inlay_Z{side}", (0, -.075, side * 10.11), (20.06, .15, .16), "basalt_edge", FLOOR, .012)
    for i in range(13):
        p = -12 + i * 2
        box(f"Peripheral_X{side}_{i}", (side * 12, -.105, p), (3.45, .21, 1.96),
            random.choice(["pave0", "pave1", "pave2"]), FLOOR, .027, 1)
    for i in range(10):
        p = -9 + i * 2
        box(f"Peripheral_Z{side}_{i}", (p, -.105, side * 12), (1.96, .21, 3.45),
            random.choice(["pave0", "pave1", "pave2"]), FLOOR, .027, 1)

# A restrained flush octagonal practice medallion. Lines rise only 4 mm.
def ring_mesh(name, center, inner, outer, height, mat, segments=64):
    cx, cy, cz = center
    vertices = []
    for radius in (inner, outer):
        for i in range(segments):
            a = math.tau * i / segments
            vertices.append((cx + radius * math.cos(a), cy + height, cz + radius * math.sin(a)))
    faces = [(i, (i + 1) % segments, (i + 1) % segments + segments, i + segments) for i in range(segments)]
    return mesh(name, vertices, faces, mat, FLOOR)


ring_mesh("Practice_Ring_Outer", (0, 0, -.7), 2.65, 2.70, .004, "basalt_edge", 64)
ring_mesh("Practice_Ring_Inner", (0, 0, -.7), 2.46, 2.48, .004, "sand_light", 64)
for i in range(8):
    a = math.tau * i / 8
    cx, cz = math.cos(a) * 2.61, -.7 + math.sin(a) * 2.61
    vertices = [(cx + math.cos(a) * .06, .004, cz + math.sin(a) * .06),
                (cx - math.sin(a) * .037, .004, cz + math.cos(a) * .037),
                (cx - math.cos(a) * .06, .004, cz - math.sin(a) * .06),
                (cx + math.sin(a) * .037, .004, cz - math.cos(a) * .037)]
    mesh(f"Practice_Ring_Tick_{i}", vertices, [(0, 1, 2, 3)], "sand_light", FLOOR)

# Foundation panels reinforce the human scale in the overview.
for side in [-1, 1]:
    for i in range(10):
        p = -12.1 + i * 2.69
        box(f"FoundationPanel_FrontBack_{side}_{i}", (p, -.52, side * 14.015), (2.56, .30, .06), "basalt_edge", bevel=.025)
        box(f"FoundationPanel_LeftRight_{side}_{i}", (side * 14.015, -.52, p), (.06, .30, 2.56), "basalt_edge", bevel=.025)


def wall_segment(label, center_x, center_z, length, height, axis="x"):
    sx, sz = (length, .80) if axis == "x" else (.80, length)
    box(label + "_Foot", (center_x, .10, center_z), (sx + .12, .20, sz + .12), "basalt_edge", bevel=.04)
    courses = max(1, round((height - .2) / .46))
    h = (height - .2) / courses
    blocks = max(1, round(length / 1.5))
    span = length / blocks
    for row in range(courses):
        for i in range(blocks):
            offset = -length / 2 + (i + .5) * span
            point = (center_x + (offset if axis == "x" else 0), .20 + h * (row + .5), center_z + (offset if axis == "z" else 0))
            size = (span - .025, h - .024, .79) if axis == "x" else (.79, h - .024, span - .025)
            box(f"{label}_Stone_{row}_{i}", point, size, random.choice(["sand", "sand", "sand_dark"]), bevel=.033)
    box(label + "_Coping", (center_x, height + .09, center_z), (sx + .17, .18, sz + .17), "sand_light", bevel=.045)
    proxy("COL_" + label, (center_x, (height + .18) / 2, center_z), (sx + .17, height + .18, sz + .17))


# Side walls retain open sightlines; front entrance is a wide unimpeded opening.
for side in [-1, 1]:
    wall_segment(f"SideWall_{side}_Front", side * 12.65, -6.25, 9.6, .92, "z")
    wall_segment(f"SideWall_{side}_Back", side * 12.65, 5.8, 8.3, 1.40, "z")
    wall_segment(f"FrontReturn_{side}", side * 10.35, -12.65, 3.85, .92)
    wall_segment(f"RearWall_{side}", side * 8.6, 12.15, 6.8, 2.35)
    wall_segment(f"RearStepWall_{side}", side * 4.65, 12.15, 1.02, 3.0)


def pillar(label, x, z, height=3.5, decorative=True):
    box(label + "_Foot", (x, .13, z), (1.52, .26, 1.52), "basalt_edge", bevel=.075)
    box(label + "_Plinth", (x, .36, z), (1.31, .22, 1.31), "sand_dark", bevel=.04)
    box(label + "_PlinthCap", (x, .52, z), (1.42, .13, 1.42), "sand_light", bevel=.035)
    shaft_h = height - .98
    for i in range(4):
        box(label + f"_Shaft_{i}", (x, .585 + shaft_h * (i + .5) / 4, z),
            (1.08, shaft_h / 4 - .018, 1.08), "sand", bevel=.033)
    if decorative:
        box(label + "_FaceInlay", (x, 1.72, z - .549), (.15, 1.45, .025), "basalt_edge", DETAIL, .014)
        box(label + "_InlayCap", (x, 2.52, z - .563), (.35, .12, .035), "gold", DETAIL, .018)
    box(label + "_CapitalLower", (x, height - .29, z), (1.24, .16, 1.24), "sand_dark", bevel=.04)
    box(label + "_Capital", (x, height - .13, z), (1.50, .19, 1.50), "sand_light", bevel=.045)
    proxy("COL_" + label, (x, height / 2, z), (1.52, height, 1.52))


for side in [-1, 1]:
    pillar(f"RearCornerPillar_{side}", side * 12.65, 12.15, 3.30)
    pillar(f"SidePillar_{side}", side * 12.65, -.45, 2.5)
    pillar(f"EntryPillar_{side}", side * 7.83, -12.65, 1.80, False)

# Main gate: individually built tapered voussoirs, inset dark reveal, projecting keystone.
arch_z, spring, inner, outer = 11.90, 3.05, 2.22, 2.98
for side in [-1, 1]:
    pillar(f"GatePier_{side}", side * 2.68, arch_z, 3.12, False)
    box(f"GatePier_InnerShaft_{side}", (side * 2.53, 1.58, arch_z), (.63, 2.60, 1.28), "sand", bevel=.035)
    box(f"GatePier_Impost_{side}", (side * 2.66, spring - .01, arch_z), (1.13, .24, 1.66), "sand_light", bevel=.045)
    proxy(f"COL_GatePier_{side}", (side * 2.68, 1.55, arch_z), (1.55, 3.1, 1.65))


def arch_wedge(name, a0, a1, r0, r1, depth, mat, center_z=arch_z, target=ARCH):
    steps = 3
    vertices = []
    for z in (center_z - depth / 2, center_z + depth / 2):
        for r in (r0, r1):
            for i in range(steps + 1):
                angle = a0 + (a1 - a0) * i / steps
                vertices.append((r * math.cos(angle), spring + r * math.sin(angle), z))
    n = steps + 1
    faces = []
    for i in range(steps):
        faces.extend([(i, i + 1, n + i + 1, n + i),
                      (2*n + i, 3*n + i, 3*n + i + 1, 2*n + i + 1),
                      (i, 2*n + i, 2*n + i + 1, i + 1),
                      (n + i, n + i + 1, 3*n + i + 1, 3*n + i)])
    faces.extend([(0, n, 3*n, 2*n), (n - 1, 3*n - 1, 4*n - 1, 2*n - 1)])
    return mesh(name, vertices, faces, mat, target, .025 if target == ARCH else 0)


for i in range(15):
    a0, a1 = math.pi * i / 15 + .006, math.pi * (i + 1) / 15 - .006
    arch_wedge(f"Gate_ArchVoussoir_{i:02d}", a0, a1, inner, outer + (.14 if i == 7 else 0),
               1.70 if i == 7 else 1.40, "sand_light" if i == 7 else ("sand" if i % 3 else "sand_dark"))
    arch_wedge(f"Gate_OuterMoulding_{i:02d}", a0, a1, outer + .025, outer + .15, .13,
               "sand_light", arch_z - .76)
# Conservative header proxy remains above character clearance; opening stays passable.
proxy("COL_GateHeader", (0, 5.18, arch_z), (5.9, 1.65, 1.6))

# Thin rune diamond on keystone, a deliberately small focal detail.
mesh("Gate_Keystone_BrassDiamond", [(-.14, 5.81, arch_z-.87), (0, 6.01, arch_z-.87),
                                   (.14, 5.81, arch_z-.87), (0, 5.61, arch_z-.87)], [(0, 1, 2, 3)], "gold")

# An interrupted fallen wall in the far side creates asymmetry, away from combat space.
for i in range(4):
    stone = box(f"Ruin_LooseStone_{i}", (-10.6 + i * .55, .18 + .04 * (i % 2), 10.50 - i * .32),
                (.85, .36, .52), "sand_dark", DETAIL, .065)
    stone.rotation_euler.z = .15 + i * .52


def banner(side):
    x, z = side * 5.05, 11.03
    box(f"Banner_{side}_Socket", (x, .17, z), (.70, .34, .70), "basalt", DETAIL, .08)
    cylinder(f"Banner_{side}_Pole", (x, 3.07, z), .055, 5.85, "iron")
    beam(f"Banner_{side}_Crossbar", (x-.70, 5.82, z), (x+.70, 5.82, z), .035, "gold")
    cylinder(f"Banner_{side}_FinialBase", (x, 6.04, z), .105, .17, "gold", vertices=8)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=.16, location=u((x, 6.21, z)))
    attach(bpy.context.object, f"Banner_{side}_Finial", "gold", DETAIL)
    nx, ny = 10, 18
    verts = []
    for j in range(ny+1):
        t = j / ny
        for i in range(nx+1):
            s = i / nx
            # Fixed top, increasing wind fold lower down; swallowtail hem.
            hx = x + (s - .5) * 1.20 + .08 * math.sin(t*3) * t
            hy = 5.74 - 2.80 * t + (max(0, 1-abs(s-.5)*2) * .35 if j == ny else 0)
            hz = z - .12 + .15 * math.sin(s*math.pi*2.3 + t*2.8) * (t*.7+.3)
            verts.append((hx, hy, hz))
    faces = []
    for j in range(ny):
        for i in range(nx):
            a = j*(nx+1)+i
            faces.append((a, a+1, a+nx+2, a+nx+1))
    cloth = mesh(f"Banner_{side}_Cloth", verts, faces, "teal")
    for face in cloth.data.polygons:
        face.use_smooth = True
    solid = cloth.modifiers.new("Fabric thickness", "SOLIDIFY")
    solid.thickness = .012
    # Geometric brass trim rides exactly on the cloth and exports without textures.
    for edge in (0, nx-1):
        ribbon = []
        for j in range(ny+1):
            a = verts[j*(nx+1)+edge]
            b = verts[j*(nx+1)+edge+1]
            if edge == 0:
                c = tuple(a[k] + (b[k]-a[k])*.27 for k in range(3))
                ribbon.extend([(a[0],a[1],a[2]-.012),(c[0],c[1],c[2]-.012)])
            else:
                c = tuple(b[k] + (a[k]-b[k])*.27 for k in range(3))
                ribbon.extend([(c[0],c[1],c[2]-.012),(b[0],b[1],b[2]-.012)])
        mesh(f"Banner_{side}_WovenEdge_{edge}", ribbon,
             [(j*2,j*2+1,j*2+3,j*2+2) for j in range(ny)], "gold")
    # Small off-white shield emblem: independent surface geometry.
    mid = verts[6*(nx+1)+5]
    ex, ey, ez = mid[0], mid[1], mid[2]-.04
    mesh(f"Banner_{side}_TrainingSigil", [(ex-.17,ey+.15,ez),(ex+.17,ey+.15,ez),
                                         (ex+.14,ey-.07,ez),(ex,ey-.28,ez),(ex-.14,ey-.07,ez)],
         [(0,1,2,3,4)], "sand_light")
    proxy(f"COL_BannerSocket_{side}", (x,.18,z), (.72,.36,.72))


banner(-1)
banner(1)

# Outdoor benches and training supply rack along the margins.
for side in [-1, 1]:
    x, z = side * 11.48, -4.1
    for dz in (-1.05, 1.05):
        box(f"Bench_{side}_Pedestal_{dz}", (x,.34,z+dz), (.65,.68,.44), "sand_dark", DETAIL, .055)
    for i in range(3):
        box(f"Bench_{side}_Slat_{i}", (x+(i-1)*.24,.75,z), (.225,.16,2.80), "wood", DETAIL, .03)
    proxy(f"COL_Bench_{side}", (x,.43,z), (.76,.86,2.85))
for x in (-11.75, -9.75):
    beam("WeaponRack_Upright", (x,.1,7.9), (x,1.80,7.9), .075, "wood")
beam("WeaponRack_Crossbeam", (-11.95,1.5,7.9), (-9.55,1.5,7.9), .065, "wood")
for i in range(3):
    x = -11.55 + .62*i
    beam(f"PracticeStaff_{i}", (x-.15,.15,7.55), (x+.10,2.1,7.95), .034, "wood_light")
    beam(f"PracticeStaff_{i}_Wrap", (x-.06,.75,7.67), (x-.01,1.12,7.74), .045, "rope")


def leaf(name, a, direction, length, width, mat):
    p = Vector(a)
    d = Vector(direction).normalized()
    side = d.cross(Vector((0, 0, 1)))
    if side.length < .05:
        side = d.cross(Vector((1, 0, 0)))
    side.normalize()
    tip = p + d*length
    middle = p + d*length*.45
    verts = [tuple(p), tuple(middle + side*width*.5), tuple(tip),
             tuple(middle - side*width*.5), tuple(middle + Vector((0,.035,0)))]
    return mesh(name, verts, [(0,1,4),(1,2,4),(2,3,4),(3,0,4)], mat, PLANTS)


def grass_patch(label, x, z, count=13):
    for i in range(count):
        px, pz = x+random.uniform(-.4,.4), z+random.uniform(-.4,.4)
        angle = random.uniform(0,math.tau)
        height = random.uniform(.13,.4)
        leaf(label+f"_Blade_{i}", (px,.008,pz), (math.cos(angle)*.45,1,math.sin(angle)*.45),
             height, .025, random.choice(["leaf","leaf_light","grass"]))


for i,(x,z) in enumerate([(-12,10.8),(11.9,10.85),(-12.05,-9.5),(12.08,-9.4),
                          (-10.9,11.38),(10.8,11.45),(-11.95,2.1),(12.1,2.5),
                          (-8.85,-12.1),(9.0,-12.1)]):
    grass_patch(f"EdgeGrass_{i}",x,z)

# Broad vine silhouettes read from the playable camera; avoid dense noise.
for side in [-1,1]:
    for strand in range(3):
        xbase = side*11.0 + strand*.20
        zbase = 11.67
        points = [(xbase+.15*math.sin(j*.8+strand),2.5-j*.29,zbase-.015*j) for j in range(8)]
        for j in range(len(points)-1):
            beam(f"Ivy_{side}_{strand}_Stem_{j}", points[j], points[j+1], .012, "leaf", PLANTS, 5)
        for j,p in enumerate(points):
            for flip in [-1,1]:
                leaf(f"Ivy_{side}_{strand}_Leaf_{j}_{flip}",p,(flip*.8,-.3,-.25),
                     random.uniform(.20,.35),.18,random.choice(["leaf","leaf_light"]))


def dummy(index, point):
    x,y,z = point
    before = set(DUMMIES.objects)
    cylinder(f"Dummy{index}_Foundation", (x,.09,z), .56,.18,"basalt_edge",DUMMIES,16,.04)
    cylinder(f"Dummy{index}_Post", (x,.90,z), .16,1.70,"wood",DUMMIES,12,.028)
    cylinder(f"Dummy{index}_Torso", (x,1.22,z), .30,.80,"wood_light",DUMMIES,12,.04)
    cylinder(f"Dummy{index}_Head", (x,1.89,z), .23,.41,"wood_light",DUMMIES,12,.035)
    beam(f"Dummy{index}_Arms", (x-.76,1.42,z), (x+.76,1.42,z), .11,"wood",DUMMIES)
    for h in (.96,1.10,1.36,1.49):
        torus(f"Dummy{index}_Rope_{h}",(x,h,z),.298,.022,"rope",DUMMIES)
    # Front-facing concentric target, represented by exportable wood/rope geometry.
    target = cylinder(f"Dummy{index}_Target",(x,1.25,z-.325),.215,.035,"wood",DUMMIES,20,.006)
    target.rotation_euler.x = math.pi/2
    ring = torus(f"Dummy{index}_TargetRing",(x,1.25,z-.35),.147,.017,"rope",DUMMIES)
    ring.rotation_euler.x = math.pi/2
    center = cylinder(f"Dummy{index}_TargetCenter",(x,1.25,z-.37),.05,.022,"rope",DUMMIES,12,.003)
    center.rotation_euler.x = math.pi/2
    anchor = empty(f"DummyAnchor_{index:02d}",point)
    anchor["suggested_hitbox"] = "Unity CapsuleCollider center (0, 1.05, 0), height 2.1, radius 0.35"
    for obj in set(DUMMIES.objects)-before:
        obj["dummy_index"] = index
    return anchor


dummy_anchors = [dummy(i+1,p) for i,p in enumerate([(-4,0,4),(0,0,5),(4,0,4)])]
empty("Spawn_Player_01",(-2,0,-7))
empty("Spawn_Player_02",(2,0,-7))
empty("Courtyard_Origin",(0,0,0))
empty("Gate_Center",(0,0,arch_z))

# Optional companion AI prop, supplied separately with its own generation provenance.
meshy_source = ROOT.parent / "Meshy" / "RuneObelisk" / "Exports" / "RuneObelisk_3m.glb"
if meshy_source.exists():
    previous = set(bpy.data.objects)
    previous_images = set(bpy.data.images)
    bpy.ops.import_scene.gltf(filepath=str(meshy_source))
    texture_dir = EXPORT / "Textures"
    texture_dir.mkdir(exist_ok=True)
    for i, image in enumerate(sorted(set(bpy.data.images)-previous_images,key=lambda image:image.name)):
        texture_path = texture_dir / f"Courtyard_Meshy_{i:02d}.png"
        if image.packed_file:
            texture_path.write_bytes(image.packed_file.data)
        else:
            image.file_format = "PNG"
            image.filepath_raw = str(texture_path)
            image.save()
        image.filepath = str(texture_path)
    imported = set(bpy.data.objects) - previous
    meshes = [obj for obj in imported if obj.type == "MESH"]
    for i,obj in enumerate(meshes):
        matrix = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = matrix
        move(obj,MESHY)
        duplicate = obj.copy()
        duplicate.data = obj.data
        MESHY.objects.link(duplicate)
        for side,item in [(-1,obj),(1,duplicate)]:
            item.name=f"Meshy_RuneObelisk_{side}_{i}"
            item.location += Vector(u((side*9,0,10)))
            item["source"]="Meshy AI companion prop; provenance stored in SourceArt/Meshy/RuneObelisk"
            proxy(f"COL_MeshyObelisk_{side}_{i}",(side*9,1.5,10),(1.04,3.0,1.04))
    for obj in imported:
        if obj.type != "MESH":
            bpy.data.objects.remove(obj,do_unlink=True)

# Only one continuous floor proxy, plus simple structural boxes. No brick colliders.
proxy("COL_Floor_28m",(0,-.20,0),(28,.40,28))

# A simple 1.8 m scale mannequin exists only in preview collections.
sx,sz=-2,-6.7
beam("Scale_LeftLeg",(sx-.12,.12,sz),(sx-.12,.85,sz),.085,"preview_figure",SCALE)
beam("Scale_RightLeg",(sx+.12,.12,sz+.06),(sx+.12,.85,sz),.085,"preview_figure",SCALE)
for side in [-1,1]:
    box(f"Scale_Foot_{side}",(sx+side*.12,.055,sz+.065),(.19,.11,.32),"preview_figure",SCALE,.035,2)
body=box("Scale_Torso",(sx,1.18,sz),(.43,.60,.25),"preview_figure",SCALE,.12,3)
for side in [-1,1]:
    beam(f"Scale_Arm_{side}",(sx+side*.26,1.4,sz),(sx+side*.34,.91,sz+.03),.065,"preview_figure",SCALE)
bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=8,radius=1,location=u((sx,1.66,sz)))
head=attach(bpy.context.object,"Scale_Head","preview_figure",SCALE)
head.scale=(.13,.12,.14)
bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)

# Bake bevels, correct mesh normals, and combine static meshes by logical category.
def bake(obj):
    if obj.type != "MESH":
        return
    bpy.context.view_layer.objects.active=obj
    for modifier in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    # Meshes built in Unity coordinates are reflected during axis conversion;
    # recalculate closed-solid normals before export.
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    obj.select_set(False)


for coll in (ARCH,FLOOR,DETAIL,PLANTS,DUMMIES,SCALE,COLLISION):
    for obj in list(coll.objects):
        bake(obj)


def join(objects, name, origin=(0,0,0)):
    objects = [obj for obj in objects if obj.type == "MESH"]
    if not objects:
        return None
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.object.join()
    result=bpy.context.object
    result.name=name
    bpy.context.scene.cursor.location=u(origin)
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    return result


# Substantial structure is grouped by function; banners and benches remain selectable.
join(list(FLOOR.objects),"Courtyard_Paving_Flat20mCore")
join(list(PLANTS.objects),"Courtyard_EdgeVegetation")
for prefix in ("Foundation", "SideWall", "FrontReturn", "RearWall", "RearStepWall", "RearCornerPillar",
               "SidePillar", "EntryPillar", "GatePier", "Gate_Arch", "Gate_Outer"):
    join([o for o in list(ARCH.objects) if o.name.startswith(prefix)],"Courtyard_"+prefix)
for side in [-1,1]:
    join([o for o in list(DETAIL.objects) if o.name.startswith(f"Banner_{side}_")],f"Banner_Teal_{side}")
    join([o for o in list(DETAIL.objects) if o.name.startswith(f"Bench_{side}_")],f"Bench_Oak_{side}")
join([o for o in list(DETAIL.objects) if o.name.startswith(("WeaponRack","PracticeStaff"))],"Prop_TrainingWeaponRack")
for i,anchor in enumerate(dummy_anchors,1):
    result=join([o for o in list(DUMMIES.objects) if o.get("dummy_index")==i],f"TrainingDummy_{i:02d}",tuple(anchor["unity_position_m"]))
    result["dummy_index"]=i
    result["pivot"]="Ground level at the center of the individual dummy"
bpy.context.scene.cursor.location=(0,0,0)

# Separate exports keep visual, collision and placement responsibilities explicit.
def select(objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.hide_set(False)
        obj.select_set(True)
    if objects:
        bpy.context.view_layer.objects.active=objects[0]


def export_fbx(path, objects):
    select(objects)
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={"MESH","EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", use_space_transform=True,
        bake_space_transform=True, axis_forward="-Z",axis_up="Y", global_scale=1.0,
        use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False,
        path_mode="COPY", embed_textures=True, use_custom_props=True)


visual_objects=[o for c in (ARCH,FLOOR,DETAIL,PLANTS,MESHY,DUMMIES) for o in c.objects]
environment_objects=[o for c in (ARCH,FLOOR,DETAIL,PLANTS,MESHY) for o in c.objects]
export_fbx(EXPORT/"TrainingCourtyard.fbx",visual_objects+list(ANCHORS.objects))
export_fbx(EXPORT/"TrainingCourtyard_EnvironmentOnly.fbx",environment_objects+list(ANCHORS.objects))
export_fbx(EXPORT/"TrainingCourtyard_CollisionProxies.fbx",list(COLLISION.objects))
select(visual_objects+list(ANCHORS.objects))
bpy.ops.export_scene.gltf(filepath=str(EXPORT/"TrainingCourtyard.glb"),export_format="GLB",use_selection=True,
                         export_yup=True,export_apply=True,export_extras=True)
for obj in list(DUMMIES.objects):
    original=obj.location.copy()
    obj.location=(0,0,0)
    export_fbx(EXPORT/(obj.name+".fbx"),[obj])
    obj.location=original
bpy.context.view_layer.update()

# A report derives counts from exported scene geometry instead of estimates.
def stats(objects):
    objects=[o for o in objects if o.type=="MESH"]
    vertices=sum(len(o.data.vertices) for o in objects)
    triangles=0
    for obj in objects:
        obj.data.calc_loop_triangles()
        triangles+=len(obj.data.loop_triangles)
    points=[o.matrix_world@Vector(v) for o in objects for v in o.bound_box]
    mins=[min(v[i] for v in points) for i in range(3)]
    maxs=[max(v[i] for v in points) for i in range(3)]
    bounds_unity={"min":[-maxs[0],mins[2],-maxs[1]],"max":[-mins[0],maxs[2],-mins[1]]}
    return {"mesh_objects":len(objects),"vertices":vertices,"triangles":triangles,
            "materials":sorted({m.name for o in objects for m in o.data.materials if m}),
            "material_count":len({m.name for o in objects for m in o.data.materials if m}),
            "unity_bounds_m":bounds_unity,
            "unity_dimensions_m":[maxs[0]-mins[0],maxs[2]-mins[2],maxs[1]-mins[1]]}


report={"title":"Original Stylized Training Courtyard / Scene 04 prototype", "seed":404,
        "blender_version":bpy.app.version_string,"units":"meters", "design_footprint_m":[28,28],
        "clear_combat_core_m":[20,20],"paving_top_unity_y":0.0,"inlay_top_unity_y":0.004,
        "coordinates":{"design":"Unity X right, Y up, Z toward arch", "blender_from_unity":"(-x, -z, y)",
                       "handedness_note":"X inversion compensates the measured Unity FBX importer handedness conversion.",
                       "fbx_export_forward":"-Z","fbx_export_up":"Y","gltf_y_up":True},
        "visual":stats(visual_objects),"environment_only":stats(environment_objects),
        "meshy_decoration":{"included":bool(list(MESHY.objects)),"source":str(meshy_source),
                            "instances":len(list(MESHY.objects)),"height_m":3},
        "collision_proxies":stats(list(COLLISION.objects)),
        "dummies":{obj.name:stats([obj]) for obj in DUMMIES.objects},
        "anchors_unity_m":{o.name:list(o["unity_position_m"]) for o in ANCHORS.objects},
        "notes":["Courtyard architecture, paving, banners, plants and dummies are original procedural geometry with flat PBR materials.",
                 "Two optional rune obelisk instances use the separately generated Meshy model and its AI-generated PBR textures.",
                 "Collision FBX contains proxy geometry only. Unity Collider components are not enabled by this export.",
                 "Preview scale figure, ground, cameras and lights are excluded from all model exports.",
                 "Three dummies have individual ground-centered pivots and individual FBX exports at origin.",
                 "Core is planar paving; combat props occupy the specified rear training positions.",
                 "Lighting and shadows shown in renders require separate Unity lighting setup.",
                 "No UV lightmap unwrap is authored; generate UV2 in Unity before lightmap baking."]}
(ROOT/"geometry_report.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")

# Presentation setup (excluded from exported models).
box("Preview_Backdrop",(0,-1.44,0),(200,.10,200),"preview_ground",STUDIO,0)
scene=bpy.context.scene
scene.unit_settings.system="METRIC"
scene.unit_settings.scale_length=1.0
scene.render.engine="CYCLES"
scene.cycles.samples=opts.samples
scene.cycles.use_denoising=True
scene.cycles.max_bounces=6
scene.world.use_nodes=True
scene.world.node_tree.nodes.clear()
world_background=scene.world.node_tree.nodes.new("ShaderNodeBackground")
world_output=scene.world.node_tree.nodes.new("ShaderNodeOutputWorld")
scene.world.node_tree.links.new(world_background.outputs["Background"],world_output.inputs["Surface"])
world_background.inputs["Color"].default_value=(.53,.68,.84,1)
world_background.inputs["Strength"].default_value=.42
scene.view_settings.view_transform="AgX"
scene.view_settings.look="AgX - Medium High Contrast"
scene.render.image_settings.file_format="PNG"
scene.render.film_transparent=False
scene.render.resolution_percentage=100


def point_at(obj,target):
    direction=Vector(u(target))-obj.location
    obj.rotation_euler=direction.to_track_quat("-Z","Y").to_euler()


def light(name,kind,point,energy,color,size=1,target=(0,0,0)):
    data=bpy.data.lights.new(name,kind)
    data.energy=energy
    data.color=color
    if kind=="AREA":
        data.shape="DISK"
        data.size=size
    if kind=="SUN":
        data.angle=math.radians(12)
    obj=bpy.data.objects.new(name,data)
    STUDIO.objects.link(obj)
    obj.location=u(point)
    point_at(obj,target)
    return obj


light("Warm_Afternoon_Sun","SUN",(-12,20,-10),3.1,(1,.86,.68))
light("Cool_Sky_Fill","AREA",(8,18,3),2100,(.67,.80,1),15)


def camera(name,point,target,lens=43,ortho=None):
    data=bpy.data.cameras.new(name)
    data.lens=lens
    data.clip_end=400
    obj=bpy.data.objects.new(name,data)
    STUDIO.objects.link(obj)
    obj.location=u(point)
    point_at(obj,target)
    if ortho:
        data.type="ORTHO"
        data.ortho_scale=ortho
    return obj


overview=camera("Camera_Overview",(28,26,-34),(0,1,0),ortho=42)
gameplay=camera("Camera_Gameplay",(0,2.10,-11.9),(0,2.18,9),lens=24)
detailcam=camera("Camera_GateDetail",(8.5,6.2,-.6),(0,3.4,11.9),lens=44)
scene.camera=overview
scene.render.resolution_x=1600
scene.render.resolution_y=1300
for obj in COLLISION.objects:
    obj.hide_set(True)
for obj in ANCHORS.objects:
    obj.hide_set(True)
select(environment_objects)
for area in bpy.context.screen.areas:
    if area.type=="VIEW_3D":
        area.spaces.active.region_3d.view_distance=42
        area.spaces.active.region_3d.view_location=(0,0,1)
        area.spaces.active.shading.type="MATERIAL"
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/"TrainingCourtyard.blend"))
if not opts.skip_render:
    for cam,filename,resolution in [(overview,"overview.png",(1600,1300)),
                                     (gameplay,"gameplay.png",(1600,1000)),
                                     (detailcam,"gate_detail.png",(1500,1100))]:
        scene.camera=cam
        scene.render.resolution_x,scene.render.resolution_y=resolution
        scene.render.filepath=str(PREVIEW/filename)
        bpy.ops.render.render(write_still=True)
    scene.camera=overview
    scene.render.resolution_x,scene.render.resolution_y=1600,1300
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/"TrainingCourtyard.blend"))
print("COURTYARD_BUILD_COMPLETE",json.dumps(report["visual"],ensure_ascii=False))
