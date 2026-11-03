"""
Builds Project Deadfall's low-poly models and exports them as FBX into Assets/_Project/Resources/Models.

Run headless (no Blender UI needed):
    blender -b --factory-startup --python Tools/Blender/build_models.py -- [--preview out_folder] [--only name,name]

Conventions (see Tools/Blender/README.md):
- Every coordinate in this file is in UNITY space: +X right, +Y up, +Z forward, metres. Helpers convert to Blender.
  Models face +Z (Blender -Y), so they face forward in Unity with the FBX axis settings used below.
- Material names carry the colour, so the game never depends on imported material settings (URP builds turn
  unknown shaders pink): "C_RRGGBB" is a fixed colour, and Skin / SkinDark / Accent / AccentDim are tinted by
  the game (enemy definition colour, facility or class colour). See ModelLibrary.cs.
- "Fit" models are normalised to a 1x1x1 box centred on the origin: the game parents them under the placeholder
  cube it already builds, so they take that cube's size (crates, columns, doors...). Other models keep real size
  with the origin at the feet / floor.
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "_Project", "Resources", "Models")

# Preview colours for the tinted slots (the game replaces them).
PREVIEW = {
    "Skin": (0.45, 0.52, 0.40),
    "SkinDark": (0.27, 0.31, 0.24),
    "Accent": (0.95, 0.65, 0.20),
    "AccentDim": (0.57, 0.39, 0.12),
}


# ----------------------------------------------------------------------------------------------- helpers

def to_b(p):
    """Unity point -> Blender point."""
    return Vector((-p[0], -p[2], p[1]))


def rot_b(r):
    """Unity euler (degrees, applied Z, X, Y) -> Blender rotation matrix."""
    rx, ry, rz = (math.radians(a) for a in r)
    # Unity X -> Blender X (same sense), Unity Y -> Blender Z (opposite sense), Unity Z -> Blender Y (same sense).
    return (Matrix.Rotation(-ry, 4, 'Z') @ Matrix.Rotation(rx, 4, 'X') @ Matrix.Rotation(rz, 4, 'Y'))


def material(name):
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    m = bpy.data.materials.new(name)
    if name.startswith(("C_", "G_")):
        h = name[2:8]
        rgb = tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
    else:
        rgb = PREVIEW.get(name, (0.8, 0.8, 0.8))
    m.diffuse_color = (*rgb, 1.0)
    m.roughness = 0.8
    return m


class Model:
    def __init__(self, name, fit=False):
        self.name = name
        self.fit = fit
        self.parts = []

    def part(self, kind, pos, size, mat, rot=(0, 0, 0), bevel=0.0, smooth=False, segments=10):
        """kind: box | sph | cyl | cone. size: full extents in Unity axes (cyl/cone run along Unity Y)."""
        if kind == "box":
            bpy.ops.mesh.primitive_cube_add(size=1.0)
        elif kind == "sph":
            bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=max(4, segments // 2 + 1), radius=0.5)
        elif kind == "cyl":
            bpy.ops.mesh.primitive_cylinder_add(vertices=segments, radius=0.5, depth=1.0)
        elif kind == "cone":
            bpy.ops.mesh.primitive_cone_add(vertices=segments, radius1=0.5, radius2=0.0, depth=1.0)
        else:
            raise ValueError(kind)
        o = bpy.context.active_object
        o.scale = (size[0], size[2], size[1])  # Unity (x, y, z) extents -> Blender (x, y, z) = (x, z, y)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        if bevel > 0 and kind == "box":
            mod = o.modifiers.new("bevel", 'BEVEL')
            mod.width = bevel
            mod.segments = 2
            bpy.ops.object.modifier_apply(modifier=mod.name)
        o.matrix_world = Matrix.Translation(to_b(pos)) @ rot_b(rot)
        o.data.materials.append(material(mat))
        for poly in o.data.polygons:
            poly.use_smooth = smooth
        self.parts.append(o)
        return o

    # Short forms.
    def box(self, pos, size, mat, rot=(0, 0, 0), bevel=0.02):
        return self.part("box", pos, size, mat, rot, bevel)

    def sph(self, pos, size, mat, rot=(0, 0, 0), smooth=True, segments=10):
        return self.part("sph", pos, size, mat, rot, smooth=smooth, segments=segments)

    def cyl(self, pos, size, mat, rot=(0, 0, 0), segments=10, smooth=False):
        return self.part("cyl", pos, size, mat, rot, segments=segments, smooth=smooth)

    def cone(self, pos, size, mat, rot=(0, 0, 0), segments=8):
        return self.part("cone", pos, size, mat, rot, segments=segments)

    def limb(self, a, b, thick, mat, kind="box"):
        """A box (or cylinder) from Unity point a to b."""
        a, b = Vector(a), Vector(b)
        d = b - a
        length = d.length
        mid = (a + b) / 2
        # Yaw (around Y) then pitch (around X), Unity convention: forward +Z.
        yaw = math.degrees(math.atan2(d.x, d.z))
        pitch = -math.degrees(math.atan2(d.y, math.hypot(d.x, d.z)))
        if kind == "cyl":
            # Cylinders run along Y: tip them forward first.
            return self.part("cyl", mid, (thick[0], length, thick[1]), mat, rot=(pitch + 90, yaw, 0), segments=8, smooth=True)
        return self.part("box", mid, (thick[0], thick[1], length), mat, rot=(pitch, yaw, 0), bevel=min(thick) * 0.25)

    def finish(self):
        bpy.ops.object.select_all(action='DESELECT')
        for o in self.parts:
            o.select_set(True)
        bpy.context.view_layer.objects.active = self.parts[0]
        if len(self.parts) > 1:
            bpy.ops.object.join()
        o = bpy.context.active_object
        o.name = self.name
        o.data.name = self.name
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        if self.fit:
            xs = [v.co.x for v in o.data.vertices]
            ys = [v.co.y for v in o.data.vertices]
            zs = [v.co.z for v in o.data.vertices]
            c = Vector(((max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2, (max(zs) + min(zs)) / 2))
            s = Vector((max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs)))
            for v in o.data.vertices:
                v.co = Vector(((v.co.x - c.x) / s.x, (v.co.y - c.y) / s.y, (v.co.z - c.z) / s.z))
        return o


def export(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    path = os.path.join(OUT, o.name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        mesh_smooth_type='FACE', use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False,
        path_mode='STRIP', embed_textures=False)
    return path


# ----------------------------------------------------------------------------------------------- palette

METAL = "C_3A3D44"
METAL_LIGHT = "C_6B7079"
DARK = "C_1C1D21"
RUBBER = "C_26272B"
CLOTH = "C_4A4038"
CLOTH_DARK = "C_2F2A26"
BONE = "C_CFC4A8"
EYE = "G_F2E6A0"
WOOD = "C_7A5A36"
WOOD_DARK = "C_573F25"
HAZARD = "C_D9A521"
CONCRETE = "C_5A5C60"
CONCRETE_DARK = "C_45474B"
SCREEN = "C_1E3A4A"
GLASS = "C_2A4F5E"
OLIVE = "C_3B4A35"
PANTS = "C_3B3F45"
ARMOR = "C_2E3238"


# ----------------------------------------------------------------------------------------------- enemies
# Each matches the placeholder's proportions at the definition's nominal height h and radius r, and keeps every
# glowing weak point where its hurtbox is (Enemy.BuildVisual), so players aim at what they see.

def eyes(m, head_pos, head_size, spread=0.07):
    x, y, z = head_pos
    for s in (-1, 1):
        m.box((x + s * spread, y + head_size * 0.05, z + head_size * 0.42), (0.05, 0.03, 0.03), EYE, bevel=0.0)


def enemy_standard():
    m = Model("enemy_standard")
    h = 1.8
    # Legs, slightly apart and bent.
    for s in (-1, 1):
        m.limb((s * 0.12, 0.9, 0.0), (s * 0.13, 0.48, 0.05), (0.15, 0.16), CLOTH_DARK)
        m.limb((s * 0.13, 0.5, 0.05), (s * 0.14, 0.08, -0.02), (0.13, 0.14), CLOTH_DARK)
        m.box((s * 0.14, 0.04, 0.04), (0.14, 0.08, 0.26), RUBBER)
    m.box((0, 0.93, 0), (0.42, 0.2, 0.26), CLOTH_DARK, bevel=0.04)
    # Torso leaning forward, torn shirt.
    m.box((0, 1.2, 0.03), (0.46, 0.5, 0.28), "Skin", rot=(10, 0, 0), bevel=0.07)
    m.box((0, 1.1, 0.02), (0.5, 0.32, 0.31), CLOTH, rot=(10, 0, 0), bevel=0.04)
    m.box((0.12, 1.3, 0.12), (0.18, 0.2, 0.1), CLOTH, rot=(10, 0, 15), bevel=0.02)
    m.box((0, 1.42, 0.06), (0.52, 0.12, 0.26), "Skin", rot=(10, 0, 0), bevel=0.05)  # shoulders
    m.cyl((0, 1.5, 0.07), (0.11, 0.12, 0.11), "Skin")
    head = (0, h * 0.89, 0.05)
    m.sph(head, (0.25, 0.29, 0.27), "SkinDark")
    m.box((0, head[1] - 0.1, head[2] + 0.07), (0.16, 0.07, 0.12), "SkinDark", bevel=0.02)  # jaw
    eyes(m, head, 0.27)
    # Arms reaching forward (the shamble), at the placeholder arm height.
    ay = h * 0.68
    for s in (-1, 1):
        m.limb((s * 0.27, 1.4, 0.05), (s * 0.31, ay, 0.3), (0.1, 0.1), "Skin")
        m.limb((s * 0.31, ay, 0.3), (s * 0.3, ay - 0.04, 0.6), (0.085, 0.085), "Skin")
        m.box((s * 0.3, ay - 0.05, 0.68), (0.1, 0.06, 0.14), "SkinDark", bevel=0.015)
    return m


def enemy_lean():
    m = Model("enemy_lean")
    h = 1.65
    for s in (-1, 1):
        m.limb((s * 0.1, 0.82, -0.02), (s * 0.12, 0.45, 0.12), (0.1, 0.11), CLOTH_DARK)
        m.limb((s * 0.12, 0.45, 0.12), (s * 0.12, 0.06, -0.06), (0.09, 0.09), "Skin")
        m.box((s * 0.12, 0.03, 0.0), (0.1, 0.06, 0.2), "SkinDark")
    m.box((0, 0.84, -0.02), (0.32, 0.16, 0.2), CLOTH_DARK, bevel=0.03)
    # Hunched sprinter: long thin torso pitched forward with a visible spine.
    m.box((0, 1.08, 0.08), (0.34, 0.52, 0.2), "Skin", rot=(25, 0, 0), bevel=0.06)
    for i in range(4):
        m.box((0, 0.95 + i * 0.11, -0.06 + i * 0.05), (0.06, 0.05, 0.06), BONE, rot=(25, 0, 0), bevel=0.0)
    m.box((0, 1.0, 0.07), (0.36, 0.2, 0.22), CLOTH, rot=(25, 0, 0), bevel=0.03)
    head = (0, h * 0.86, h * 0.12)
    m.sph(head, (0.24, 0.27, 0.27), "SkinDark")
    m.box((0, head[1] - 0.09, head[2] + 0.09), (0.13, 0.06, 0.12), "SkinDark", bevel=0.01)
    eyes(m, head, 0.27, 0.06)
    ay = h * 0.55
    for s in (-1, 1):
        m.limb((s * 0.2, 1.25, 0.14), (s * 0.34, ay + 0.05, 0.25), (0.07, 0.07), "Skin")
        m.limb((s * 0.34, ay + 0.05, 0.25), (s * 0.36, ay - 0.06, 0.58), (0.06, 0.06), "Skin")
        for c in (-1, 0, 1):  # claws
            m.cone((s * 0.36 + c * 0.025, ay - 0.08, 0.66), (0.025, 0.14, 0.025), BONE, rot=(100, 0, 0), segments=5)
    return m


def enemy_plated():
    m = Model("enemy_plated")
    h, r = 2.3, 0.6
    for s in (-1, 1):
        m.limb((s * 0.3, 1.0, 0.0), (s * 0.34, 0.5, 0.06), (0.3, 0.32), CLOTH_DARK)
        m.limb((s * 0.34, 0.52, 0.06), (s * 0.34, 0.1, 0.0), (0.27, 0.28), "SkinDark")
        m.box((s * 0.34, 0.06, 0.06), (0.3, 0.12, 0.42), RUBBER, bevel=0.03)
    m.box((0, 1.05, 0), (0.8, 0.3, 0.6), CLOTH_DARK, bevel=0.06)
    m.box((0, 1.45, -0.02), (1.1, 0.7, 0.78), "Skin", bevel=0.14)  # huge chest
    m.box((0, 1.82, -0.04), (1.25, 0.3, 0.7), "Skin", bevel=0.12)  # shoulders
    head = (0, h * 0.88, 0.08)
    m.sph(head, (0.3, 0.3, 0.32), "SkinDark")
    eyes(m, head, 0.3, 0.07)
    # Front plate (armour hurtbox): bolted riot-shield slabs grown into the chest.
    m.box((0, h * 0.5, r * 0.95), (r * 2.2, h * 0.53, 0.1), METAL, bevel=0.03)
    for y in (0.85, 1.15, 1.45):
        m.box((0, y, r * 0.95 + 0.06), (r * 2.25, 0.05, 0.04), METAL_LIGHT, bevel=0.0)
    for sx in (-1, 1):
        for y in (0.8, 1.5):
            m.cyl((sx * 0.55, y, r * 0.95 + 0.06), (0.06, 0.03, 0.06), METAL_LIGHT, rot=(90, 0, 0), segments=6)
    # Back growth (weak point): glowing cluster.
    gy = h * 0.6
    m.box((0, gy, -r * 0.92), (r * 1.0, h * 0.22, 0.2), "G_F28C26", bevel=0.05)
    for (x, y, d) in ((-0.18, gy + 0.12, 0.2), (0.16, gy - 0.1, 0.22), (0.0, gy + 0.02, 0.26)):
        m.sph((x, y, -r * 1.02), (d, d, d), "G_F28C26", segments=8)
    # Thick arms with fists, held low in front.
    for s in (-1, 1):
        m.limb((s * 0.68, 1.8, -0.02), (s * 0.74, 1.3, 0.12), (0.24, 0.24), "Skin")
        m.limb((s * 0.74, 1.3, 0.12), (s * 0.74, 1.12, 0.55), (0.22, 0.22), "Skin")
        m.box((s * 0.74, 1.1, 0.68), (0.26, 0.24, 0.26), "SkinDark", bevel=0.05)
    return m


def enemy_crawler():
    m = Model("enemy_crawler")
    h = 0.7
    # Low body along Z, spine bumps, four splayed limbs.
    m.sph((0, 0.32, -0.02), (0.46, 0.3, 0.92), "Skin")
    for i in range(5):
        m.box((0, 0.48, -0.32 + i * 0.15), (0.07, 0.06, 0.07), BONE, bevel=0.0)
    m.sph((0, h * 0.55, 0.48), (0.22, 0.2, 0.26), "SkinDark")
    m.box((0, h * 0.55 - 0.07, 0.56), (0.13, 0.05, 0.12), "SkinDark", bevel=0.01)
    eyes(m, (0, h * 0.55, 0.48), 0.26, 0.05)
    for s in (-1, 1):
        # Front arms (placeholder arms at x = +-r, z = 0.35).
        m.limb((s * 0.18, 0.36, 0.3), (s * 0.34, 0.3, 0.42), (0.07, 0.07), "Skin")
        m.limb((s * 0.34, 0.3, 0.42), (s * 0.3, 0.02, 0.5), (0.06, 0.06), "Skin")
        # Back legs.
        m.limb((s * 0.16, 0.3, -0.34), (s * 0.34, 0.28, -0.42), (0.08, 0.08), "Skin")
        m.limb((s * 0.34, 0.28, -0.42), (s * 0.32, 0.02, -0.5), (0.07, 0.07), "Skin")
    m.box((0, 0.28, -0.12), (0.4, 0.16, 0.4), CLOTH_DARK, bevel=0.03)
    return m


def enemy_bloated():
    m = Model("enemy_bloated")
    h, r = 1.75, 0.42
    for s in (-1, 1):
        m.limb((s * 0.18, 0.5, 0.0), (s * 0.2, 0.06, 0.02), (0.18, 0.18), "SkinDark")
        m.box((s * 0.2, 0.04, 0.05), (0.18, 0.08, 0.28), RUBBER)
    m.sph((0, h * 0.47, 0), (r * 2.35, h * 0.52, r * 2.15), "Skin", segments=14)
    m.box((0, h * 0.3, 0.02), (r * 2.0, 0.22, r * 1.9), CLOTH, bevel=0.06)  # strained trousers
    head = (0, h * 0.88, 0.05)
    m.sph(head, (0.25, 0.26, 0.26), "SkinDark")
    eyes(m, head, 0.26, 0.06)
    # Sac (weak point) and pustules.
    m.sph((0, h * 0.42, r * 0.95), (0.38, 0.38, 0.32), "G_FF8C1A", segments=12)
    for (x, y, z, d) in ((-0.32, 1.05, 0.22, 0.13), (0.3, 0.95, 0.26, 0.11), (0.35, 0.62, -0.2, 0.12), (-0.28, 0.7, -0.3, 0.1)):
        m.sph((x, y, z), (d, d, d), "G_FF8C1A", segments=8)
    for s in (-1, 1):
        m.limb((s * 0.42, 1.2, 0.0), (s * 0.55, 0.85, 0.08), (0.12, 0.12), "Skin")
        m.limb((s * 0.55, 0.85, 0.08), (s * 0.52, 0.62, 0.2), (0.11, 0.11), "Skin")
    return m


def enemy_spitter():
    m = Model("enemy_spitter")
    h = 1.7
    for s in (-1, 1):
        m.limb((s * 0.13, 0.85, 0.0), (s * 0.15, 0.46, 0.08), (0.13, 0.14), CLOTH_DARK)
        m.limb((s * 0.15, 0.48, 0.08), (s * 0.15, 0.07, -0.02), (0.11, 0.12), "SkinDark")
        m.box((s * 0.15, 0.04, 0.03), (0.13, 0.08, 0.24), RUBBER)
    m.box((0, 0.88, 0), (0.4, 0.18, 0.26), CLOTH_DARK, bevel=0.03)
    m.box((0, 1.15, 0.04), (0.46, 0.5, 0.28), "Skin", rot=(16, 0, 0), bevel=0.07)
    m.box((0, 1.05, 0.03), (0.48, 0.24, 0.3), CLOTH, rot=(16, 0, 0), bevel=0.03)
    head = (0, h * 0.84, 0.14)
    m.sph(head, (0.25, 0.27, 0.3), "SkinDark")
    # Unhinged jaw.
    m.box((0, head[1] - 0.13, head[2] + 0.1), (0.16, 0.05, 0.16), "SkinDark", rot=(25, 0, 0), bevel=0.01)
    eyes(m, head, 0.3, 0.06)
    # Throat sac (weak point).
    m.sph((0, h * 0.7, 0.28), (0.34, 0.3, 0.3), "G_B3D933", segments=12)
    for s in (-1, 1):
        m.limb((s * 0.26, 1.36, 0.08), (s * 0.3, 1.0, 0.1), (0.08, 0.08), "Skin")
        m.limb((s * 0.3, 1.0, 0.1), (s * 0.28, 0.75, 0.2), (0.07, 0.07), "Skin")
    return m


def enemy_alpha():
    m = Model("enemy_alpha")
    h, r = 2.15, 0.42
    for s in (-1, 1):
        m.limb((s * 0.16, 1.0, 0.0), (s * 0.18, 0.55, 0.08), (0.17, 0.18), CLOTH_DARK)
        m.limb((s * 0.18, 0.57, 0.08), (s * 0.18, 0.08, -0.02), (0.15, 0.15), "SkinDark")
        m.box((s * 0.18, 0.05, 0.04), (0.16, 0.1, 0.3), RUBBER)
    m.box((0, 1.03, 0), (0.5, 0.2, 0.32), CLOTH_DARK, bevel=0.04)
    m.box((0, 1.38, 0.02), (0.62, 0.62, 0.38), "Skin", bevel=0.1)
    m.box((0, 1.72, 0.0), (0.92, 0.22, 0.42), "Skin", bevel=0.09)  # wide shoulders
    head = (0, h * 0.89, 0.06)
    m.sph(head, (0.29, 0.31, 0.31), "SkinDark")
    eyes(m, head, 0.31, 0.07)
    # Crystal growth on the upper back (weak point).
    gy = h * 0.78
    m.box((0, gy, -r * 0.62), (r * 1.5, 0.24, 0.3), "G_59D9FF", bevel=0.04)
    for (x, tilt) in ((-0.22, -20), (-0.08, -8), (0.07, 6), (0.21, 18)):
        m.cone((x, gy + 0.2, -r * 0.7), (0.1, 0.38, 0.1), "G_59D9FF", rot=(-25, 0, tilt), segments=5)
    ay = h * 0.62
    for s in (-1, 1):
        m.limb((s * 0.48, 1.74, 0.0), (s * 0.54, ay + 0.05, 0.2), (0.14, 0.14), "Skin")
        m.limb((s * 0.54, ay + 0.05, 0.2), (s * 0.52, ay - 0.05, 0.6), (0.12, 0.12), "Skin")
        for c in (-1, 0, 1):
            m.cone((s * 0.52 + c * 0.035, ay - 0.07, 0.7), (0.035, 0.18, 0.035), BONE, rot=(100, 0, 0), segments=5)
    return m


def enemy_specimen():
    """Body, head and arms only: the phase plates, growths and core stay game-built so they can appear by phase."""
    m = Model("enemy_specimen")
    h, r = 3.4, 1.0
    for s in (-1, 1):
        m.limb((s * 0.5, 1.5, 0.0), (s * 0.58, 0.75, 0.15), (0.48, 0.5), "SkinDark")
        m.limb((s * 0.58, 0.78, 0.15), (s * 0.58, 0.12, 0.0), (0.42, 0.44), "SkinDark")
        m.box((s * 0.58, 0.08, 0.12), (0.46, 0.16, 0.66), "SkinDark", bevel=0.06)
    m.box((0, 1.55, 0), (1.3, 0.5, 1.0), "Skin", bevel=0.18)
    m.box((0, 2.2, 0.05), (1.8, 1.0, 1.35), "Skin", rot=(14, 0, 0), bevel=0.25)  # hunched chest
    m.box((0, 2.75, -0.1), (2.1, 0.45, 1.2), "Skin", rot=(14, 0, 0), bevel=0.2)  # shoulder mass
    for i, x in enumerate((-0.45, -0.15, 0.15, 0.45)):  # back spikes
        m.cone((x, 2.95, -0.55), (0.16, 0.6 if i in (1, 2) else 0.45, 0.16), BONE, rot=(-35, 0, x * 25), segments=6)
    head = (0, h * 0.9, 0.2)
    m.sph(head, (0.5, 0.48, 0.55), "SkinDark")
    m.box((0, head[1] - 0.2, head[2] + 0.18), (0.36, 0.14, 0.3), "SkinDark", bevel=0.04)
    eyes(m, head, 0.55, 0.12)
    for s in (-1, 1):
        m.limb((s * 1.0, 2.75, -0.05), (s * 1.22, 2.0, 0.25), (0.42, 0.42), "Skin")
        m.limb((s * 1.22, 2.0, 0.25), (s * 1.25, 1.6, 0.95), (0.38, 0.38), "Skin")
        m.box((s * 1.25, 1.52, 1.2), (0.5, 0.45, 0.5), "SkinDark", bevel=0.08)
    return m


# ----------------------------------------------------------------------------------------------- players

def player_body():
    """Feet origin. Legs, torso, vest and pack; the head, arms and rifle are player_head (they pitch with aim)."""
    m = Model("player_body")
    for s in (-1, 1):
        m.limb((s * 0.12, 0.95, 0.0), (s * 0.13, 0.5, 0.03), (0.16, 0.18), PANTS)
        m.limb((s * 0.13, 0.52, 0.03), (s * 0.13, 0.1, 0.0), (0.15, 0.16), PANTS)
        m.box((s * 0.13, 0.52, 0.12), (0.15, 0.14, 0.06), ARMOR, bevel=0.02)  # knee pads
        m.box((s * 0.13, 0.06, 0.04), (0.16, 0.12, 0.3), RUBBER, bevel=0.02)  # boots
    m.box((0, 0.98, 0), (0.42, 0.18, 0.26), PANTS, bevel=0.04)
    m.box((0, 1.02, 0.0), (0.46, 0.07, 0.29), DARK, bevel=0.01)  # belt
    for x in (-0.12, 0.0, 0.12):
        m.box((x, 1.04, 0.16), (0.09, 0.11, 0.05), OLIVE, bevel=0.01)  # pouches
    m.box((0, 1.27, 0.0), (0.46, 0.46, 0.27), OLIVE, bevel=0.06)  # torso
    m.box((0, 1.27, 0.02), (0.5, 0.4, 0.31), ARMOR, bevel=0.04)  # plate carrier
    m.box((0, 1.33, 0.18), (0.3, 0.16, 0.03), "Accent", bevel=0.01)  # class colour chest plate
    for s in (-1, 1):
        m.box((s * 0.3, 1.46, 0.0), (0.16, 0.1, 0.22), "Accent", bevel=0.03)  # shoulder pads
    m.box((0, 1.25, -0.22), (0.38, 0.44, 0.18), CLOTH_DARK, bevel=0.04)  # pack
    m.box((0, 1.48, -0.22), (0.3, 0.06, 0.16), RUBBER, bevel=0.01)
    m.cyl((0, 1.53, 0.0), (0.13, 0.12, 0.13), OLIVE)  # neck
    return m


def player_head():
    """Eye-height origin, pitches with the aim: helmet, visor, forearms and a rifle where the placeholder gun was."""
    m = Model("player_head")
    m.sph((0, 0.02, -0.03), (0.3, 0.28, 0.32), "C_C9A88A")  # face/head under the helmet
    m.sph((0, 0.08, -0.04), (0.34, 0.24, 0.36), ARMOR, smooth=True)  # helmet dome
    m.box((0, 0.0, -0.04), (0.36, 0.06, 0.36), ARMOR, bevel=0.02)  # helmet rim
    m.box((0, 0.17, -0.04), (0.06, 0.05, 0.3), "Accent", bevel=0.0)  # stripe
    m.box((0, 0.0, 0.14), (0.26, 0.08, 0.06), GLASS, bevel=0.015)  # visor
    m.box((0.17, 0.06, 0.0), (0.04, 0.08, 0.08), DARK, bevel=0.01)  # headset
    # Rifle (placeholder gun at (0.25, -0.25, 0.35), 0.6 long).
    g = (0.25, -0.25, 0.35)
    m.box(g, (0.07, 0.1, 0.46), DARK, bevel=0.01)
    m.box((g[0], g[1] - 0.02, g[2] + 0.33), (0.035, 0.035, 0.22), METAL, bevel=0.0)  # barrel
    m.box((g[0], g[1] + 0.08, g[2] + 0.02), (0.04, 0.05, 0.16), METAL, bevel=0.005)  # sight
    m.box((g[0], g[1] - 0.12, g[2] + 0.06), (0.05, 0.14, 0.07), DARK, rot=(15, 0, 0), bevel=0.005)  # magazine
    m.box((g[0], g[1] - 0.05, g[2] - 0.28), (0.06, 0.12, 0.16), RUBBER, bevel=0.01)  # stock
    # Forearms and gloves holding it.
    m.limb((0.24, -0.38, -0.05), (0.25, -0.3, 0.22), (0.09, 0.09), OLIVE)
    m.box((0.25, -0.31, 0.24), (0.08, 0.08, 0.1), RUBBER, bevel=0.01)
    m.limb((-0.12, -0.36, 0.1), (0.2, -0.32, 0.46), (0.085, 0.085), OLIVE)
    m.box((0.22, -0.32, 0.5), (0.08, 0.07, 0.1), RUBBER, bevel=0.01)
    return m


# ----------------------------------------------------------------------------------------------- FOB Halcyon

def fob_booth(wide):
    """Origin on the floor at the console; the back panel is 2.2 m behind (FobBuilder.Booth layout)."""
    m = Model("fob_booth_wide" if wide else "fob_booth")
    w = 9.0 if wide else 6.0
    # Back wall panel with frame, ribs and a header light strip in the facility colour.
    m.box((0, 2.0, -2.2), (w, 4.0, 0.3), "C_383A3F", bevel=0.03)
    for x in [(-w / 2 + 0.25) + i * ((w - 0.5) / 4) for i in range(5)]:
        m.box((x, 2.0, -2.03), (0.12, 3.9, 0.06), METAL, bevel=0.01)
    m.box((0, 4.2, -2.1), (w, 0.5, 0.4), "C_2A2C30", bevel=0.03)
    m.box((0, 4.2, -1.89), (w - 0.3, 0.16, 0.02), "Accent", bevel=0.0)
    # Big screen with bezel and a facility-coloured frame.
    m.box((0, 2.2, -2.02), (w * 0.5 + 0.2, 1.6, 0.06), DARK, bevel=0.02)
    m.box((0, 2.2, -1.98), (w * 0.5, 1.4, 0.03), "AccentDim", bevel=0.0)
    m.box((0, 1.35, -1.98), (w * 0.5, 0.05, 0.03), "Accent", bevel=0.0)
    # Side partitions with a glass top.
    for s in (-1, 1):
        m.box((s * w / 2, 0.9, -1.0), (0.2, 1.8, 2.6), "C_42454B", bevel=0.03)
        m.box((s * w / 2, 2.15, -1.0), (0.08, 0.7, 2.5), GLASS, bevel=0.01)
        m.box((s * w / 2, 2.52, -1.0), (0.22, 0.06, 2.6), METAL, bevel=0.01)
    # Console desk: base, angled top, keyboard, small monitors.
    dw = 3.2 if wide else 2.0
    m.box((0, 0.45, 0.05), (dw, 0.9, 0.8), "C_4B4F56", bevel=0.04)
    m.box((0, 0.48, 0.47), (dw - 0.1, 0.8, 0.04), DARK, bevel=0.0)  # kick panel
    m.box((0, 1.0, 0.0), (dw + 0.1, 0.12, 0.95), "C_33363B", rot=(-8, 0, 0), bevel=0.03)
    m.box((0, 1.08, 0.12), (0.9, 0.03, 0.3), DARK, rot=(-8, 0, 0), bevel=0.005)  # keyboard
    m.box((0, 1.1, 0.12), (0.84, 0.02, 0.24), "Accent", rot=(-8, 0, 0), bevel=0.0)  # glow (FobBuilder "Glow")
    for s in ((-1, 1) if not wide else (-1.5, -0.5, 0.5, 1.5)):
        x = s * (0.6 if not wide else 0.7)
        m.box((x, 1.36, -0.25), (0.5, 0.34, 0.04), DARK, rot=(-12, 0, 0), bevel=0.01)
        m.box((x, 1.36, -0.225), (0.44, 0.28, 0.01), SCREEN, rot=(-12, 0, 0), bevel=0.0)
        m.box((x, 1.12, -0.25), (0.05, 0.16, 0.05), METAL, bevel=0.0)
    return m


def fob_transport():
    """Fit model for FobBuilder's "Transport" box (3.2 x 2.2 x 6): an armoured personnel carrier."""
    m = Model("fob_transport", fit=True)
    m.box((0, 0.9, 0), (3.0, 1.1, 5.6), OLIVE, bevel=0.12)
    m.box((0, 1.65, -0.4), (2.6, 0.6, 3.8), OLIVE, bevel=0.1)
    m.box((0, 1.6, 1.85), (2.5, 0.5, 1.2), OLIVE, rot=(-20, 0, 0), bevel=0.08)  # sloped nose
    m.box((0, 1.65, 1.35), (1.8, 0.22, 0.04), GLASS, rot=(-20, 0, 0), bevel=0.0)  # windscreen
    for z in (-2.0, -0.7, 0.6, 1.9):
        for s in (-1, 1):
            m.cyl((s * 1.42, 0.5, z), (1.0, 0.36, 1.0), RUBBER, rot=(0, 0, 90), segments=12)
            m.cyl((s * 1.62, 0.5, z), (0.45, 0.04, 0.45), METAL, rot=(0, 0, 90), segments=8)
    m.box((0, 0.95, -2.82), (1.6, 1.2, 0.06), "C_334030", bevel=0.02)  # rear hatch
    m.box((0, 2.05, -0.8), (0.9, 0.3, 0.9), OLIVE, bevel=0.05)  # roof hatch
    m.box((0, 2.2, -0.4), (0.12, 0.12, 1.4), METAL, bevel=0.0)  # roof gun
    for s in (-1, 1):
        m.box((s * 1.0, 1.2, 2.82), (0.35, 0.18, 0.05), "G_F2E6A0", bevel=0.0)  # headlights
        m.box((s * 1.51, 1.3, 0), (0.04, 0.16, 5.0), HAZARD, bevel=0.0)  # side stripe
    return m


def crate_stack():
    """Fit model for FobBuilder's crate boxes: two military crates and a duffel."""
    m = Model("fob_crates", fit=True)
    m.box((-0.3, 0.3, 0), (1.2, 0.6, 1.0), OLIVE, bevel=0.03)
    m.box((-0.3, 0.6, 0), (1.25, 0.05, 1.05), "C_334030", bevel=0.0)
    m.box((0.55, 0.25, 0.05), (0.7, 0.5, 0.9), OLIVE, bevel=0.03)
    for x in (-0.75, 0.15):
        m.box((x, 0.3, 0.51), (0.08, 0.5, 0.02), HAZARD, bevel=0.0)
    m.cyl((0.0, 0.85, 0.05), (0.35, 0.9, 0.35), CLOTH, rot=(0, 0, 90), segments=10, smooth=True)
    return m


# ----------------------------------------------------------------------------------------------- generated maps (fit models)

def prop_crate():
    m = Model("prop_crate", fit=True)
    m.box((0, 0.5, 0), (1.0, 1.0, 1.0), WOOD, bevel=0.02)
    for s in (-1, 1):  # planks and battens on the faces
        m.box((0, 0.5, s * 0.505), (0.9, 0.06, 0.02), WOOD_DARK, bevel=0.0)
        m.box((s * 0.505, 0.5, 0), (0.02, 0.06, 0.9), WOOD_DARK, bevel=0.0)
    for sx in (-1, 1):
        for sz in (-1, 1):
            m.box((sx * 0.47, 0.5, sz * 0.47), (0.1, 1.02, 0.1), METAL, bevel=0.01)  # corner irons
    m.box((0, 1.0, 0), (1.02, 0.04, 1.02), WOOD_DARK, bevel=0.01)
    return m


def prop_locker():
    m = Model("prop_locker", fit=True)
    m.box((0, 0.9, 0), (1.2, 1.8, 1.2), "C_4C5560", bevel=0.02)
    for x in (-0.3, 0.3):
        m.box((x, 0.92, 0.605), (0.56, 1.7, 0.02), "C_56606C", bevel=0.005)
        for i in range(4):
            m.box((x, 1.55 - i * 0.06, 0.62), (0.36, 0.02, 0.01), DARK, bevel=0.0)  # vents
        m.box((x + (0.2 if x < 0 else -0.2), 1.0, 0.625), (0.03, 0.18, 0.03), METAL_LIGHT, bevel=0.0)  # handles
    m.box((0, 0.04, 0), (1.22, 0.08, 1.22), DARK, bevel=0.0)
    return m


def prop_column():
    m = Model("prop_column", fit=True)
    m.box((0, 2.25, 0), (0.8, 4.5, 0.8), CONCRETE, bevel=0.03)
    m.box((0, 0.15, 0), (0.95, 0.3, 0.95), CONCRETE_DARK, bevel=0.03)
    m.box((0, 4.35, 0), (0.95, 0.3, 0.95), CONCRETE_DARK, bevel=0.03)
    for i in range(4):  # hazard wrap at waist height
        m.box((0, 0.6 + i * 0.16, 0), (0.82, 0.08, 0.82), HAZARD if i % 2 == 0 else DARK, bevel=0.0)
    m.box((0, 2.8, 0.405), (0.3, 0.2, 0.02), "C_C8C8C0", bevel=0.0)  # sector plate
    return m


def prop_partition():
    """Fit model, long along X (the game turns it for partitions running along Z)."""
    m = Model("prop_partition", fit=True)
    m.box((0, 0.3, 0), (3.6, 0.6, 0.6), CONCRETE, bevel=0.04)
    m.box((0, 0.85, 0), (3.6, 0.6, 0.3), CONCRETE, bevel=0.04)
    for x in (-1.2, 0.0, 1.2):
        m.box((x, 1.0, 0), (0.5, 0.12, 0.32), HAZARD, bevel=0.0)
    return m


def prop_door():
    """Fit model for SectorDoor's slab (width along X): a blast door with the lock colour as its stripe."""
    m = Model("prop_door", fit=True)
    m.box((0, 1.3, 0), (2.5, 2.6, 0.25), METAL, bevel=0.03)
    for s in (-1, 1):
        m.box((s * 0.62, 1.3, 0), (1.15, 2.4, 0.29), "C_454952", bevel=0.02)  # two leaves
        for i in range(5):
            m.box((s * 0.62, 0.45 + i * 0.42, 0), (1.0, 0.05, 0.31), METAL, bevel=0.0)
    m.box((0, 1.3, 0), (0.06, 2.4, 0.32), DARK, bevel=0.0)  # seam
    m.box((0, 1.55, 0), (2.3, 0.22, 0.33), "Accent", bevel=0.0)  # lock colour band
    for i in range(6):
        m.box((-1.0 + i * 0.4, 0.15, 0), (0.2, 0.18, 0.33), HAZARD if i % 2 == 0 else DARK, bevel=0.0)
    return m


def prop_relay():
    """Fit model for the power relay cabinet body (DoorSwitch, 1.3 x 1.6 x 0.7)."""
    m = Model("prop_relay", fit=True)
    m.box((0, 0.8, 0), (1.3, 1.6, 0.7), "Accent", bevel=0.03)
    m.box((0, 0.8, 0.355), (1.2, 1.5, 0.02), "AccentDim", bevel=0.0)
    for i in range(6):
        m.box((0.35, 0.4 + i * 0.08, 0.37), (0.4, 0.03, 0.01), DARK, bevel=0.0)
    m.box((-0.3, 0.5, 0.37), (0.3, 0.3, 0.02), HAZARD, bevel=0.0)  # warning plate
    for x in (-0.4, 0.0, 0.4):
        m.cyl((x, 1.66, 0), (0.12, 0.14, 0.12), RUBBER, segments=6)  # cable glands
    return m


def prop_switch():
    """Fit model for the security switch panel body (DoorSwitch, 0.7 x 0.9 x 0.2)."""
    m = Model("prop_switch", fit=True)
    m.box((0, 0.45, 0), (0.7, 0.9, 0.2), "Accent", bevel=0.02)
    m.box((0, 0.45, 0.1), (0.62, 0.82, 0.02), "AccentDim", bevel=0.0)
    m.box((0, 0.75, 0.11), (0.5, 0.08, 0.02), HAZARD, bevel=0.0)
    for s in (-1, 1):
        m.box((s * 0.3, 0.45, 0.0), (0.04, 0.92, 0.22), DARK, bevel=0.0)
    return m


def supply_cache():
    """Supply Cache at real size (origin on the floor, front +Z): footlocker with gold trim and a green status light."""
    m = Model("supply_cache")
    m.box((0, 0.42, 0), (1.6, 0.84, 0.8), "C_385C42", bevel=0.04)
    m.box((0, 0.88, 0), (1.66, 0.1, 0.86), "C_BF9E33", bevel=0.02)  # lid trim
    m.box((0, 0.8, 0), (1.62, 0.04, 0.82), DARK, bevel=0.0)
    for s in (-1, 1):
        m.box((s * 0.82, 0.5, 0), (0.06, 0.12, 0.3), METAL_LIGHT, bevel=0.01)  # handles
        m.box((s * 0.55, 0.42, 0.41), (0.12, 0.8, 0.03), "C_BF9E33", bevel=0.0)
    m.box((0, 0.55, -0.41), (0.5, 0.18, 0.03), "G_66E680", bevel=0.0)  # status light (the placeholder's)
    m.box((0, 0.55, 0.41), (0.5, 0.18, 0.03), "G_66E680", bevel=0.0)
    m.box((0, 0.04, 0), (1.64, 0.08, 0.84), RUBBER, bevel=0.0)
    return m


BUILDERS = [
    enemy_standard, enemy_lean, enemy_plated, enemy_crawler, enemy_bloated, enemy_spitter, enemy_alpha, enemy_specimen,
    player_body, player_head,
    lambda: fob_booth(False), lambda: fob_booth(True), fob_transport, crate_stack,
    prop_crate, prop_locker, prop_column, prop_partition, prop_door, prop_relay, prop_switch, supply_cache,
]


# ----------------------------------------------------------------------------------------------- preview

# Display size for fit models (they are unit cubes) in the preview: their usual in-game box.
FIT_SIZES = {
    "prop_crate": (1.8, 1.0, 1.1), "prop_locker": (1.2, 1.8, 1.2), "prop_column": (0.8, 4.5, 0.8),
    "prop_partition": (3.6, 1.2, 0.4), "prop_door": (2.5, 2.6, 0.25), "prop_relay": (1.3, 1.6, 0.7),
    "prop_switch": (0.7, 0.9, 0.2), "fob_transport": (3.2, 2.2, 6.0), "fob_crates": (2.4, 1.2, 1.2),
}

SHEETS = {
    "characters": ["enemy_standard", "enemy_lean", "enemy_plated", "enemy_crawler", "enemy_bloated",
                   "enemy_spitter", "enemy_alpha", "enemy_specimen", "player"],
    "fob": ["fob_booth", "fob_booth_wide", "fob_transport", "fob_crates"],
    "map": ["prop_crate", "prop_locker", "prop_column", "prop_partition", "prop_door", "prop_relay", "prop_switch", "supply_cache"],
}


def preview(objects, out_dir):
    """One image per sheet, Workbench renderer (needs EGL: libegl1 + libgl1-mesa-dri on a headless box)."""
    by_name = {o.name: o for o in objects}
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_cavity = True
    scene.display.shading.show_shadows = True
    scene.display.shading.show_object_outline = True
    scene.render.film_transparent = False
    for o in objects:
        o.hide_render = True
    paths = []
    for sheet, names in SHEETS.items():
        temp = []

        def place(name, x, y=0.0):
            src = by_name.get(name)
            if src is None:
                return 0.0
            c = src.copy()
            c.data = src.data
            c.hide_render = False
            scene.collection.objects.link(c)
            if name in FIT_SIZES:
                sx, sy, sz = FIT_SIZES[name]
                c.scale = (sx, sz, sy)
                c.location = (x, 0, sy / 2 + y)
                width = sx
            else:
                c.location = (x, 0, y)
                width = src.dimensions.x
            temp.append(c)
            return width

        x = 0.0
        tallest = 0.0
        for name in names:
            if name == "player":
                w = place("player_body", x)
                place("player_head", x, 1.68)
                tallest = max(tallest, 2.0)
            else:
                w = place(name, x)
                src = by_name.get(name)
                if src is not None:
                    tallest = max(tallest, FIT_SIZES.get(name, (0, src.dimensions.z, 0))[1])
            x -= max(w, 0.8) + 0.9  # Unity +X is Blender -X: lay out left to right as seen from the front
        width = -x
        cx = x / 2 + 0.45
        bpy.ops.object.camera_add()
        cam = bpy.context.active_object
        cam.data.type = 'ORTHO'
        cam.data.ortho_scale = max(width * 1.2, tallest * 2.4)
        target = Vector((cx, 0, tallest * 0.45))
        cam.location = target + Vector((-width * 0.25, -width * 1.2, width * 0.55))
        cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scene.camera = cam
        scene.render.resolution_x = 1800
        scene.render.resolution_y = int(max(500, 1800 * (tallest * 2.0) / max(width, 1.0)))
        path = os.path.join(out_dir, f"models_{sheet}.png")
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        paths.append(path)
        for c in temp:
            bpy.data.objects.remove(c, do_unlink=True)
        bpy.data.objects.remove(cam, do_unlink=True)
    return paths


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    preview_path = None
    only = None
    if "--preview" in argv:
        preview_path = os.path.abspath(argv[argv.index("--preview") + 1])  # a folder
    if "--only" in argv:
        only = set(argv[argv.index("--only") + 1].split(","))

    bpy.ops.wm.read_factory_settings(use_empty=True)
    os.makedirs(OUT, exist_ok=True)
    built = []
    for b in BUILDERS:
        model = b()
        if only and model.name not in only:
            for p in model.parts:
                bpy.data.objects.remove(p, do_unlink=True)
            continue
        o = model.finish()
        print("exported", export(o), len(o.data.polygons), "faces")
        built.append(o)
    if preview_path:
        os.makedirs(preview_path, exist_ok=True)
        for p in preview(built, preview_path):
            print("preview", p)


main()
