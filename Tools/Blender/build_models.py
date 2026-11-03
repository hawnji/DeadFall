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

import bmesh
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
    def __init__(self, name, fit=False, triangulate=False):
        self.name = name
        self.fit = fit
        self.triangulate = triangulate  # concave outlines (prof) must be triangulated here, not by the importer
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

    def prof(self, pts, x0, x1, mat, chamfer=0.003):
        """
        A side outline extruded across the gun: pts are (z, y) points in Unity space (any order around the outline,
        concave allowed), x0..x1 the thickness. A one-segment chamfer gives the faceted low-poly look.
        """
        me = bpy.data.meshes.new("prof")
        bm = bmesh.new()
        a = [bm.verts.new(to_b((x0, y, z))) for z, y in pts]
        b = [bm.verts.new(to_b((x1, y, z))) for z, y in pts]
        bm.faces.new(a)
        bm.faces.new(list(reversed(b)))
        for i in range(len(pts)):
            j = (i + 1) % len(pts)
            bm.faces.new([a[i], a[j], b[j], b[i]])
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        bm.to_mesh(me)
        bm.free()
        o = bpy.data.objects.new("prof", me)
        bpy.context.scene.collection.objects.link(o)
        bpy.ops.object.select_all(action='DESELECT')
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        if chamfer > 0:
            mod = o.modifiers.new("chamfer", 'BEVEL')
            mod.width = chamfer
            mod.segments = 1
            mod.limit_method = 'ANGLE'
            mod.angle_limit = math.radians(35)
            mod.use_clamp_overlap = True
            bpy.ops.object.modifier_apply(modifier=mod.name)
        o.data.materials.append(material(mat))
        for poly in o.data.polygons:
            poly.use_smooth = False
        self.parts.append(o)
        return o

    def sym(self, pts, half, mat, chamfer=0.003, x=0.0):
        """prof() centred on x with half-thickness half."""
        return self.prof(pts, x - half, x + half, mat, chamfer)

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
        if self.triangulate:
            mod = o.modifiers.new("tri", 'TRIANGULATE')
            mod.quad_method = 'BEAUTY'
            mod.ngon_method = 'BEAUTY'
            bpy.ops.object.modifier_apply(modifier=mod.name)
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
    write_meta(path)
    return path


def write_meta(fbx_path):
    """New models get a full ModelImporter .meta (Unity rejects a bare guid-only one); existing metas keep their guid."""
    meta = fbx_path + ".meta"
    if os.path.exists(meta):
        return
    import uuid
    with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "fbx_meta_template.txt")) as f:
        text = f.read().replace("{guid}", uuid.uuid4().hex)
    with open(meta, "w") as f:
        f.write(text)


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


def enemy_brood():
    """Broodmother (M12): swollen egg-laden abdomen behind a raised front body, six jointed legs, a fanged head.
    The carapace plate, egg sacs and core stay game-built so they can appear by phase."""
    m = Model("enemy_brood")
    h, r = 2.6, 1.3
    m.sph((0, h * 0.38, -r * 0.35), (r * 2.0, h * 0.6, r * 2.3), "Skin")  # abdomen
    for i, (x, z) in enumerate(((-0.55, -0.9), (0.5, -1.1), (0.0, -1.5), (-0.3, -0.3), (0.45, -0.4))):
        m.sph((x, h * 0.42 + 0.25 * (i % 2), z), (0.55, 0.42, 0.55), "SkinDark")  # lumps and ridges
    m.limb((0, h * 0.42, r * 0.1), (0, h * 0.62, r * 0.75), (1.2, 1.1), "Skin")  # raised front body
    m.box((0, h * 0.68, r * 0.75), (1.1, 0.5, 0.9), "SkinDark", rot=(-20, 0, 0), bevel=0.15)  # chest plate
    head = (0, h * 0.78, r * 0.98)
    m.sph(head, (0.62, 0.52, 0.66), "SkinDark")
    for s in (-1, 1):  # mandibles
        m.cone((s * 0.17, head[1] - 0.22, head[2] + 0.3), (0.1, 0.38, 0.1), BONE, rot=(110, 0, s * 18), segments=6)
    eyes(m, head, 0.62, 0.11)
    for s in (-1, 1):
        for l in range(3):
            z = r * (0.6 - l * 0.6)
            hip = (s * r * 0.7, h * 0.36, z)
            knee = (s * r * 1.35, h * 0.62, z + (0.25 if l == 0 else -0.15 if l == 2 else 0.0))
            foot = (s * r * 1.75, 0.04, z + (0.45 if l == 0 else -0.35 if l == 2 else 0.0))
            m.limb(hip, knee, (0.2, 0.22), "SkinDark")
            m.limb(knee, foot, (0.15, 0.17), "SkinDark")
            m.cone(foot, (0.12, 0.2, 0.12), BONE, rot=(180, 0, 0), segments=6)
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
    """Eye-height origin, pitches with the aim: helmet, visor and forearms. The game adds the weapon model the
    teammate is holding at (0.25, -0.27, 0.18) (RemotePlayerProxy.WeaponAnchor)."""
    m = Model("player_head")
    m.sph((0, 0.02, -0.03), (0.3, 0.28, 0.32), "C_C9A88A")  # face/head under the helmet
    m.sph((0, 0.08, -0.04), (0.34, 0.24, 0.36), ARMOR, smooth=True)  # helmet dome
    m.box((0, 0.0, -0.04), (0.36, 0.06, 0.36), ARMOR, bevel=0.02)  # helmet rim
    m.box((0, 0.17, -0.04), (0.06, 0.05, 0.3), "Accent", bevel=0.0)  # stripe
    m.box((0, 0.0, 0.14), (0.26, 0.08, 0.06), GLASS, bevel=0.015)  # visor
    m.box((0.17, 0.06, 0.0), (0.04, 0.08, 0.08), DARK, bevel=0.01)  # headset
    # Forearms and gloves holding the weapon.
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



# ----------------------------------------------------------------------------------------------- environment dressing
# Theme props for generated rooms (MapBuilder.Dressing). Fit models are dressed onto placeholder boxes that keep the
# collider; real-size ones (wall and floor clutter) have no collider. Glow materials (G_) render emissive.

LAB_WHITE = "C_C9CCC8"
LAB_GREY = "C_8D9294"
RUST = "C_6E4A2E"
PIPE_GREEN = "C_3E5C47"
FLUID = "G_4CE08A"
LED_G = "G_5BF08C"
LED_A = "G_F0B54A"
LED_R = "G_F05A4A"
SCREEN_GLOW = "G_3F8FB8"
LAMP = "G_F4F1E6"
DEAD_WOOD = "C_4A3A2A"
SOIL = "C_2E261F"


def prop_light_fixture():
    """Fit model, long along X (1.4 x 0.14 x 0.5): ceiling strip light, glowing face down."""
    m = Model("prop_light_fixture", fit=True)
    m.box((0, 0.1, 0), (1.4, 0.08, 0.5), METAL, bevel=0.01)
    m.box((0, 0.04, 0), (1.3, 0.04, 0.38), LAMP, bevel=0.0)
    for s in (-1, 1):
        m.box((s * 0.68, 0.06, 0), (0.06, 0.1, 0.52), DARK, bevel=0.0)
    for x in (-0.33, 0.33):
        m.box((x, 0.04, 0), (0.03, 0.05, 0.4), METAL_LIGHT, bevel=0.0)  # cage bars
    return m


def prop_lab_bench():
    """Fit model, long along X (2.4 x 0.95 x 0.9): lab bench with cabinets, a microscope and glowing flasks."""
    m = Model("prop_lab_bench", fit=True)
    m.box((0, 0.42, 0), (2.3, 0.84, 0.82), LAB_WHITE, bevel=0.02)
    m.box((0, 0.87, 0), (2.4, 0.06, 0.9), "C_2B2E31", bevel=0.01)  # resin top
    for i in range(4):
        x = -0.86 + i * 0.575
        m.box((x, 0.45, 0.415), (0.52, 0.7, 0.02), LAB_GREY, bevel=0.005)
        m.box((x, 0.68, 0.43), (0.22, 0.03, 0.02), METAL_LIGHT, bevel=0.0)
    m.box((0, 0.05, 0), (2.32, 0.1, 0.84), DARK, bevel=0.0)  # kick plate
    m.box((-0.75, 0.93, -0.05), (0.18, 0.06, 0.22), LAB_GREY, bevel=0.005)  # microscope
    m.limb((-0.75, 0.95, -0.12), (-0.75, 1.2, -0.05), (0.05, 0.05), LAB_GREY)
    m.cyl((-0.75, 1.17, 0.02), (0.05, 0.14, 0.05), DARK, rot=(30, 0, 0), segments=8)
    for i, x in enumerate((0.25, 0.45, 0.62)):
        h = 0.18 + (i % 2) * 0.08
        m.cyl((x, 0.9 + h / 2, 0.1), (0.1, h, 0.1), GLASS, segments=8)
        m.cyl((x, 0.9 + h * 0.3, 0.1), (0.085, h * 0.5, 0.085), FLUID, segments=8)
    m.box((0.85, 0.96, -0.2), (0.42, 0.12, 0.3), "C_2F3236", bevel=0.01)  # analyser
    m.box((0.85, 0.99, -0.045), (0.3, 0.06, 0.01), SCREEN_GLOW, bevel=0.0)
    return m


def prop_tank():
    """Fit model (1.2 x 2.4 x 1.2): specimen tank with glowing fluid."""
    m = Model("prop_tank", fit=True)
    m.cyl((0, 0.2, 0), (1.2, 0.4, 1.2), METAL, segments=12)
    m.cyl((0, 0.42, 0), (1.05, 0.06, 1.05), HAZARD, segments=12)
    m.cyl((0, 1.25, 0), (0.94, 1.6, 0.94), FLUID, segments=12)  # glass reads as the glowing fluid (opaque shader)
    m.cyl((0, 1.9, 0), (0.96, 0.22, 0.96), GLASS, segments=12)  # air gap above the fluid line
    m.cyl((0, 2.2, 0), (1.2, 0.4, 1.2), METAL, segments=12)
    m.cyl((0, 2.42, 0), (0.5, 0.06, 0.5), DARK, segments=10)
    for a in range(4):  # frame posts
        ang = math.radians(45 + a * 90)
        m.box((math.sin(ang) * 0.52, 1.25, math.cos(ang) * 0.52), (0.07, 1.62, 0.07), METAL_LIGHT, bevel=0.0)
    m.box((0, 0.24, 0.6), (0.36, 0.16, 0.04), SCREEN_GLOW, bevel=0.0)
    return m


def prop_server_rack():
    """Fit model (0.8 x 2.2 x 1.1), front +Z: server cabinet with blinking status lights."""
    m = Model("prop_server_rack", fit=True)
    m.box((0, 1.1, 0), (0.8, 2.2, 1.1), "C_1E2024", bevel=0.02)
    m.box((0, 1.1, 0.555), (0.7, 2.0, 0.02), "C_2A2D33", bevel=0.0)
    for i in range(9):
        y = 0.3 + i * 0.21
        m.box((0, y, 0.57), (0.64, 0.16, 0.02), "C_34383F", bevel=0.0)
        for j in range(3):
            led = (LED_G, LED_G, LED_A, LED_R)[(i * 3 + j) % 4 if (i + j) % 3 == 0 else 0]
            m.box((0.2 + j * 0.05, y, 0.585), (0.025, 0.025, 0.01), led, bevel=0.0)
        m.box((-0.15, y, 0.585), (0.28, 0.02, 0.01), DARK, bevel=0.0)
    m.box((0, 0.04, 0), (0.82, 0.08, 1.12), DARK, bevel=0.0)
    m.box((0, 2.22, 0), (0.6, 0.06, 0.9), "C_2A2D33", bevel=0.0)
    return m


def prop_shelf():
    """Fit model, long along X (2.4 x 2.2 x 0.7): steel shelving with boxes and canisters."""
    m = Model("prop_shelf", fit=True)
    for sx in (-1, 1):
        for sz in (-1, 1):
            m.box((sx * 1.16, 1.1, sz * 0.31), (0.06, 2.2, 0.06), "C_355C8A", bevel=0.0)
    for i, y in enumerate((0.12, 0.8, 1.48, 2.14)):
        m.box((0, y, 0), (2.36, 0.04, 0.68), METAL_LIGHT, bevel=0.0)
        m.box((0, y, 0.33), (2.36, 0.08, 0.03), HAZARD if i == 0 else "C_355C8A", bevel=0.0)
    items = [(-0.8, 0.14, 0.5, 0.36, WOOD), (-0.2, 0.14, 0.4, 0.3, "C_8A7A5A"), (0.5, 0.14, 0.7, 0.4, OLIVE),
             (-0.6, 0.82, 0.6, 0.32, "C_8A7A5A"), (0.3, 0.82, 0.35, 0.45, WOOD), (0.85, 0.82, 0.3, 0.25, OLIVE),
             (-0.9, 1.5, 0.35, 0.28, OLIVE), (0.0, 1.5, 0.5, 0.36, "C_8A7A5A")]
    for x, y, w, h, mat in items:
        m.box((x, y + h / 2, 0), (w, h, 0.5), mat, bevel=0.015)
    for x in (0.55, 0.75, 0.95):
        m.cyl((x, 1.5 + 0.2, 0.05), (0.16, 0.4, 0.16), "C_B03A2E", segments=8)
    return m


def prop_barrel():
    """Real size (0.6 x 0.9), origin on the floor: steel drum."""
    m = Model("prop_barrel")
    m.cyl((0, 0.45, 0), (0.6, 0.9, 0.6), "C_3F5A7A", segments=12)
    for y in (0.3, 0.6):
        m.cyl((0, y, 0), (0.62, 0.04, 0.62), "C_33495F", segments=12)
    m.cyl((0, 0.9, 0), (0.56, 0.02, 0.56), DARK, segments=12)
    m.cyl((0.15, 0.91, 0.1), (0.08, 0.03, 0.08), METAL_LIGHT, segments=6)
    m.box((0, 0.45, 0.3), (0.26, 0.2, 0.02), HAZARD, bevel=0.0)
    return m


def prop_table():
    """Fit model, long along X (2.2 x 0.8 x 1.6): canteen table with fixed benches, a tray and a cup."""
    m = Model("prop_table", fit=True)
    m.box((0, 0.74, 0), (2.2, 0.06, 0.8), "C_8E8A80", bevel=0.01)
    for sz in (-1, 1):
        m.box((0, 0.44, sz * 0.66), (2.1, 0.05, 0.28), "C_7A3D2E", bevel=0.01)  # benches
        m.box((0, 0.22, sz * 0.4), (2.0, 0.04, 0.05), METAL, bevel=0.0)
    for sx in (-0.85, 0.85):
        m.box((sx, 0.37, 0), (0.08, 0.72, 0.08), METAL, bevel=0.0)
        m.box((sx, 0.22, 0), (0.06, 0.06, 1.5), METAL, bevel=0.0)
        for sz in (-1, 1):
            m.box((sx, 0.21, sz * 0.66), (0.05, 0.42, 0.05), METAL, bevel=0.0)
    m.box((-0.4, 0.78, 0.1), (0.42, 0.02, 0.3), "C_4F6A7A", bevel=0.0)
    m.cyl((0.5, 0.82, -0.15), (0.08, 0.1, 0.08), LAB_WHITE, segments=8)
    return m


def prop_planter():
    """Fit model (1.4 x 1.8 x 1.4): concrete planter with a dead tree."""
    m = Model("prop_planter", fit=True)
    m.box((0, 0.3, 0), (1.4, 0.6, 1.4), CONCRETE, bevel=0.04)
    m.box((0, 0.58, 0), (1.2, 0.04, 1.2), SOIL, bevel=0.0)
    m.limb((0, 0.6, 0), (0.05, 1.25, 0.02), (0.1, 0.1), DEAD_WOOD)
    for a, b in [((0.05, 1.2, 0.02), (0.4, 1.7, 0.15)), ((0.05, 1.2, 0.02), (-0.35, 1.6, -0.1)),
                 ((0.03, 1.0, 0.01), (-0.1, 1.45, 0.4)), ((0.04, 1.1, 0.02), (0.2, 1.5, -0.4))]:
        m.limb(a, b, (0.05, 0.05), DEAD_WOOD)
    return m


def prop_desk():
    """Fit model, long along X (2.0 x 1.2 x 0.9), front +Z: terminal desk with a glowing monitor."""
    m = Model("prop_desk", fit=True)
    m.box((0, 0.74, 0), (2.0, 0.05, 0.9), "C_4A4F57", bevel=0.01)
    for sx in (-1, 1):
        m.box((sx * 0.94, 0.37, 0), (0.08, 0.72, 0.84), "C_33373D", bevel=0.0)
    m.box((0.6, 0.38, 0.05), (0.5, 0.66, 0.74), "C_3A3F46", bevel=0.01)  # drawers
    for i in range(3):
        m.box((0.6, 0.18 + i * 0.22, 0.43), (0.18, 0.02, 0.02), METAL_LIGHT, bevel=0.0)
    m.box((-0.2, 1.0, -0.2), (0.7, 0.42, 0.05), DARK, bevel=0.01)
    m.box((-0.2, 1.0, -0.17), (0.64, 0.36, 0.01), SCREEN_GLOW, bevel=0.0)
    m.box((-0.2, 0.8, -0.24), (0.06, 0.12, 0.06), DARK, bevel=0.0)
    m.box((-0.2, 0.775, 0.15), (0.5, 0.02, 0.16), DARK, bevel=0.0)  # keyboard
    m.box((0.4, 0.82, -0.25), (0.25, 0.12, 0.3), "C_C4BFA8", bevel=0.0)  # file box
    return m


def prop_generator():
    """Fit model (1.8 x 1.8 x 1.4): reactor-side generator with cooling fins, pipes and a status light."""
    m = Model("prop_generator", fit=True)
    m.box((0, 0.1, 0), (1.8, 0.2, 1.4), DARK, bevel=0.01)
    m.box((0, 0.8, 0), (1.5, 1.2, 1.1), PIPE_GREEN, bevel=0.03)
    for i in range(7):
        m.box((-0.55 + i * 0.18, 0.85, 0.57), (0.06, 0.9, 0.06), "C_2F4636", bevel=0.0)
    m.cyl((0, 1.55, 0), (0.7, 0.3, 0.7), METAL, segments=12)
    m.limb((0.5, 1.4, -0.3), (0.5, 1.8, -0.3), (0.16, 0.16), RUST, kind="cyl")
    m.limb((-0.5, 1.4, -0.3), (-0.5, 1.8, -0.3), (0.16, 0.16), RUST, kind="cyl")
    m.box((0.6, 1.1, 0.56), (0.2, 0.14, 0.02), LED_A, bevel=0.0)
    for i in range(4):
        m.box((-0.45 + i * 0.3, 0.3, 0.56), (0.15, 0.2, 0.02), HAZARD if i % 2 == 0 else DARK, bevel=0.0)
    return m


def prop_fusebox():
    """Real size (0.5 x 0.7 x 0.18), origin at the back centre, front +Z: wall junction box."""
    m = Model("prop_fusebox")
    m.box((0, 0, 0.08), (0.5, 0.7, 0.16), "C_5A6068", bevel=0.01)
    m.box((0, 0, 0.165), (0.44, 0.64, 0.02), "C_656C75", bevel=0.0)
    m.box((0, 0.2, 0.18), (0.2, 0.12, 0.01), HAZARD, bevel=0.0)
    m.box((0.14, -0.2, 0.18), (0.04, 0.04, 0.01), LED_G, bevel=0.0)
    m.cyl((0, -0.42, 0.08), (0.08, 0.16, 0.08), RUBBER, segments=6)
    m.box((0, -0.65, 0.04), (0.06, 0.38, 0.06), RUBBER, bevel=0.0)  # conduit down
    return m


def prop_wall_vent():
    """Real size (0.9 x 0.5 x 0.08), origin at the back centre, front +Z: louvred wall vent."""
    m = Model("prop_wall_vent")
    m.box((0, 0, 0.03), (0.9, 0.5, 0.06), METAL, bevel=0.005)
    m.box((0, 0, 0.065), (0.8, 0.4, 0.01), DARK, bevel=0.0)
    for i in range(5):
        m.box((0, -0.16 + i * 0.08, 0.07), (0.78, 0.035, 0.02), METAL_LIGHT, rot=(25, 0, 0), bevel=0.0)
    return m


def prop_extinguisher():
    """Real size, origin at the back centre (wall), front +Z: extinguisher on a bracket with its sign."""
    m = Model("prop_extinguisher")
    m.cyl((0, -0.2, 0.12), (0.16, 0.5, 0.16), "C_B03A2E", segments=10, smooth=True)
    m.cyl((0, 0.08, 0.12), (0.06, 0.08, 0.06), DARK, segments=6)
    m.box((0.06, 0.1, 0.14), (0.12, 0.03, 0.03), DARK, bevel=0.0)
    m.box((0, -0.15, 0.02), (0.2, 0.06, 0.04), METAL, bevel=0.0)
    m.box((0, 0.35, 0.01), (0.24, 0.24, 0.02), "C_B03A2E", bevel=0.0)  # sign
    m.box((0, 0.35, 0.022), (0.14, 0.14, 0.01), LAB_WHITE, bevel=0.0)
    return m


def prop_rubble():
    """Real size (about 1.3 x 0.3 x 1.0), origin on the floor: low debris pile (no collider)."""
    m = Model("prop_rubble")
    chunks = [((0, 0.08, 0), (0.6, 0.16, 0.5), CONCRETE, (0, 15, 0)), ((0.35, 0.06, 0.2), (0.4, 0.12, 0.35), CONCRETE_DARK, (8, 40, 5)),
              ((-0.4, 0.05, -0.15), (0.35, 0.1, 0.3), CONCRETE, (0, -25, 10)), ((0.1, 0.18, -0.1), (0.3, 0.12, 0.25), CONCRETE_DARK, (12, 60, -8)),
              ((-0.15, 0.03, 0.35), (0.5, 0.04, 0.12), RUST, (0, 70, 0)), ((0.45, 0.02, -0.3), (0.6, 0.03, 0.06), METAL, (0, -35, 0)),
              ((-0.5, 0.015, 0.3), (0.25, 0.01, 0.3), LAB_WHITE, (0, 20, 0)), ((0.2, 0.015, 0.45), (0.22, 0.01, 0.28), "C_C4BFA8", (0, -50, 0))]
    for pos, size, mat, rot in chunks:
        m.box(pos, size, mat, rot=rot, bevel=0.01)
    return m


# ----------------------------------------------------------------------------------------------- weapons
# First-person and pickup models, built from extruded side outlines (prof) with faceted chamfers.
# Origin = the viewmodel's model origin (rear of the receiver, at the grip line); the barrel runs along +Z and the
# muzzle tip sits exactly where WeaponViewmodel puts the muzzle (y 0.02; z 0.65 rifle/heavy, 0.8 shotgun,
# 0.24 pistol, 0.42 SMG, 0.9 LMG, 0.8 melee tip), so muzzle flashes and tracers line up.
# Weapons have fixed materials (wood, steel, polymer) rather than the tint slots: they are the game's centrepiece.

WOOD_W = "C_8C4826"        # warm stock wood
WOOD_W2 = "C_6E3519"       # darker wood (grain bands, end grain)
STEEL_D = "C_25272B"       # blued receiver steel
STEEL_M = "C_3A3D43"
STEEL_L = "C_5C6168"       # worn edges, bolts
STEEL_B = "C_8E949C"       # bright steel (blades)
EDGE = "C_D6DBE0"          # sharpened edge
POLY = "C_1D1E21"          # polymer
POLY_L = "C_2C2E33"
TAN = "C_8A7A5A"           # cord wrap / tan polymer
BRASS = "C_B8933A"
SHELL_RED = "C_A8322A"
GLASS_G = "G_49D99A"
DOT_R = "G_E8452E"


def tube(m, z0, z1, d, mat, y=0.02, x=0.0, segments=8):
    """A faceted cylinder along +Z from z0 to z1 with diameter d."""
    return m.cyl((x, y, (z0 + z1) / 2), (d, z1 - z0, d), mat, rot=(90, 0, 0), segments=segments)


def rivet(m, x, y, z, d=0.008, mat=STEEL_L):
    side = 1 if x >= 0 else -1
    return m.cyl((x + side * 0.001, y, z), (d, 0.004, d), mat, rot=(0, 0, 90), segments=6)


def curved_mag(m, top_rear, top_front, drop, sweep, flare, half, mat, steps=7):
    """Curved box magazine outline: from the mag well (z rear/front at y) down `drop`, sweeping forward."""
    (zr, y0), zf = top_rear, top_front
    front, rear = [], []
    for i in range(steps + 1):
        t = i / steps
        dz = sweep * t * t
        front.append((zf + dz + flare * t, y0 - drop * t))
        rear.append((zr + dz, y0 - (drop - 0.008) * t))
    return m.sym(front + list(reversed(rear)), half, mat, chamfer=0.003)


def weapon_halberd_ar():
    """Assault rifle: stamped receiver, wood furniture, curved magazine, gas tube, slant brake."""
    m = Model("weapon_halberd_ar", triangulate=True)
    # Receiver with a rounded dust cover and a rear sight block.
    m.sym([(0.0, -0.045), (0.30, -0.045), (0.31, -0.03), (0.31, 0.022), (0.29, 0.035), (0.02, 0.035), (0.0, 0.026)], 0.024, STEEL_D)
    m.sym([(0.01, 0.034), (0.27, 0.034), (0.275, 0.046), (0.25, 0.052), (0.03, 0.052), (0.01, 0.044)], 0.021, STEEL_M)  # dust cover
    for z in (0.05, 0.09, 0.13, 0.17, 0.21):
        m.box((0, 0.053, z), (0.034, 0.003, 0.006), STEEL_D, bevel=0.0)  # cover ribs
    m.sym([(0.285, 0.035), (0.325, 0.035), (0.325, 0.058), (0.31, 0.064), (0.29, 0.058)], 0.016, STEEL_M)  # rear sight block
    m.box((0, 0.066, 0.31), (0.02, 0.004, 0.03), STEEL_L, bevel=0.0)  # sight leaf
    for s in (-1, 1):
        m.box((s * 0.0245, -0.012, 0.15), (0.002, 0.03, 0.2), STEEL_M, bevel=0.0)  # side panel stamping
        rivet(m, s * 0.025, -0.032, 0.04)
        rivet(m, s * 0.025, -0.032, 0.27)
        rivet(m, s * 0.025, 0.012, 0.035)
        rivet(m, s * 0.025, 0.012, 0.28)
    m.box((0.026, 0.012, 0.17), (0.003, 0.022, 0.1), POLY, bevel=0.0)  # ejection port
    m.box((0.027, 0.018, 0.23), (0.006, 0.01, 0.03), STEEL_L, bevel=0.002)  # charging handle
    m.box((0.026, 0.002, 0.11), (0.003, 0.008, 0.11), STEEL_L, rot=(-6, 0, 0), bevel=0.0)  # selector lever
    # Trigger group.
    m.sym([(0.07, -0.045), (0.07, -0.072), (0.085, -0.082), (0.14, -0.082), (0.15, -0.07), (0.15, -0.045),
           (0.142, -0.045), (0.142, -0.068), (0.136, -0.074), (0.088, -0.074), (0.078, -0.066), (0.078, -0.045)], 0.004, STEEL_M, chamfer=0.0)
    m.sym([(0.1, -0.045), (0.112, -0.045), (0.108, -0.06), (0.098, -0.068), (0.094, -0.062)], 0.003, STEEL_L, chamfer=0.0)
    # Wood pistol grip, swept back.
    m.sym([(0.075, -0.035), (0.03, -0.035), (0.0, -0.14), (0.004, -0.158), (0.035, -0.162), (0.05, -0.15), (0.078, -0.06)], 0.017, WOOD_W, chamfer=0.006)
    m.sym([(0.004, -0.152), (0.036, -0.157), (0.035, -0.163), (0.003, -0.158)], 0.0175, WOOD_W2, chamfer=0.0)
    # Wood stock: drops from the receiver to a tall butt with a steel plate.
    m.sym([(0.0, 0.03), (-0.05, 0.026), (-0.29, 0.006), (-0.30, -0.004), (-0.30, -0.112), (-0.285, -0.122),
           (-0.12, -0.076), (-0.03, -0.052), (0.0, -0.042)], 0.022, WOOD_W, chamfer=0.008)
    m.sym([(-0.30, 0.006), (-0.312, 0.004), (-0.312, -0.118), (-0.30, -0.122)], 0.024, STEEL_M, chamfer=0.002)  # butt plate
    m.sym([(0.0, 0.034), (0.012, 0.034), (0.012, -0.046), (0.0, -0.046)], 0.026, STEEL_M, chamfer=0.002)  # receiver end cap
    # Curved magazine with a floor plate.
    curved_mag(m, (0.13, -0.045), 0.195, 0.2, 0.1, 0.022, 0.016, STEEL_M)
    m.sym([(0.226, -0.236), (0.322, -0.226), (0.326, -0.24), (0.228, -0.252)], 0.018, STEEL_D, chamfer=0.002)
    m.sym([(0.123, -0.04), (0.205, -0.04), (0.205, -0.055), (0.123, -0.055)], 0.0255, STEEL_D, chamfer=0.0)  # mag well
    # Lower handguard (wood, finger grooves) with steel retainers.
    m.sym([(0.315, -0.012), (0.33, -0.032), (0.46, -0.032), (0.478, -0.018), (0.478, 0.034), (0.315, 0.034)], 0.026, WOOD_W, chamfer=0.008)
    for z in (0.36, 0.4, 0.44):
        m.box((0, -0.03, z), (0.053, 0.004, 0.012), WOOD_W2, bevel=0.0)
    m.sym([(0.305, -0.034), (0.318, -0.034), (0.318, 0.036), (0.305, 0.036)], 0.028, STEEL_M, chamfer=0.002)
    m.sym([(0.476, -0.022), (0.49, -0.022), (0.49, 0.036), (0.476, 0.036)], 0.027, STEEL_M, chamfer=0.002)
    # Upper handguard over the gas tube.
    m.sym([(0.33, 0.036), (0.44, 0.036), (0.445, 0.05), (0.43, 0.064), (0.345, 0.064), (0.33, 0.05)], 0.019, WOOD_W, chamfer=0.006)
    tube(m, 0.44, 0.53, 0.017, STEEL_M, y=0.05)  # gas tube
    m.sym([(0.505, 0.008), (0.535, 0.008), (0.535, 0.062), (0.505, 0.062)], 0.012, STEEL_D, chamfer=0.002)  # gas block
    # Barrel, front sight, cleaning rod, slant brake.
    tube(m, 0.49, 0.62, 0.019, STEEL_D)
    m.sym([(0.55, 0.026), (0.578, 0.026), (0.574, 0.05), (0.57, 0.08), (0.558, 0.08), (0.554, 0.05)], 0.011, STEEL_D, chamfer=0.0)
    m.box((0, 0.068, 0.564), (0.003, 0.02, 0.003), STEEL_L, bevel=0.0)  # sight post
    tube(m, 0.49, 0.6, 0.006, STEEL_L, y=-0.004)
    tube(m, 0.615, 0.65, 0.025, STEEL_M)
    m.sym([(0.628, 0.032), (0.65, 0.026), (0.65, 0.033), (0.628, 0.034)], 0.0126, STEEL_D, chamfer=0.0)  # brake slant
    m.box((0, 0.02, 0.651), (0.012, 0.012, 0.002), POLY, bevel=0.0)  # bore
    return m


def weapon_breacher_12():
    """Pump shotgun: steel receiver with shell port, wood pump and stock, vent rib, side saddle."""
    m = Model("weapon_breacher_12", triangulate=True)
    m.sym([(0.0, -0.045), (0.26, -0.045), (0.27, -0.03), (0.27, 0.035), (0.25, 0.046), (0.02, 0.046), (0.0, 0.034)], 0.026, STEEL_D)
    m.box((0.027, 0.018, 0.15), (0.003, 0.03, 0.1), POLY, bevel=0.0)  # ejection port
    m.box((0, -0.046, 0.16), (0.03, 0.003, 0.12), POLY, bevel=0.0)  # loading port
    for s in (-1, 1):
        rivet(m, s * 0.027, -0.03, 0.05)
        rivet(m, s * 0.027, -0.03, 0.22)
    # Barrel with a vent rib and bead; magazine tube with cap.
    tube(m, 0.26, 0.8, 0.03, STEEL_D, y=0.02)
    m.box((0, 0.039, 0.53), (0.012, 0.004, 0.53), STEEL_M, bevel=0.0)
    for z in [0.3 + i * 0.06 for i in range(8)]:
        m.box((0, 0.035, z), (0.006, 0.008, 0.006), STEEL_M, bevel=0.0)
    m.sph((0, 0.044, 0.79), (0.007, 0.007, 0.007), BRASS, smooth=False, segments=6)
    tube(m, 0.26, 0.69, 0.027, STEEL_M, y=-0.018)
    tube(m, 0.69, 0.71, 0.03, STEEL_L, y=-0.018)
    m.sym([(0.66, -0.035), (0.68, -0.035), (0.68, 0.035), (0.66, 0.035)], 0.018, STEEL_M, chamfer=0.002)  # barrel clamp
    # Ribbed wood pump.
    m.sym([(0.4, -0.03), (0.41, -0.043), (0.6, -0.043), (0.61, -0.03), (0.61, 0.008), (0.6, 0.012), (0.41, 0.012), (0.4, 0.008)], 0.025, WOOD_W, chamfer=0.007)
    for z in [0.43 + i * 0.03 for i in range(6)]:
        m.box((0, -0.018, z), (0.052, 0.05, 0.008), WOOD_W2, bevel=0.0)
    # Trigger guard and trigger.
    m.sym([(0.07, -0.045), (0.07, -0.074), (0.085, -0.084), (0.14, -0.084), (0.15, -0.072), (0.15, -0.045),
           (0.142, -0.045), (0.142, -0.07), (0.136, -0.076), (0.088, -0.076), (0.078, -0.068), (0.078, -0.045)], 0.005, STEEL_M, chamfer=0.0)
    m.sym([(0.1, -0.045), (0.112, -0.045), (0.108, -0.062), (0.098, -0.07), (0.094, -0.064)], 0.003, STEEL_L, chamfer=0.0)
    # Classic stock with a wrist, comb and rubber pad.
    m.sym([(0.0, 0.036), (-0.06, 0.03), (-0.33, 0.018), (-0.34, 0.004), (-0.34, -0.112), (-0.325, -0.124),
           (-0.12, -0.082), (-0.055, -0.088), (-0.03, -0.075), (0.0, -0.044)], 0.024, WOOD_W, chamfer=0.008)
    m.sym([(-0.34, 0.006), (-0.358, 0.006), (-0.358, -0.118), (-0.34, -0.124)], 0.026, POLY, chamfer=0.003)
    # Side saddle with red shells.
    m.sym([(0.04, -0.03), (0.2, -0.03), (0.2, 0.028), (0.04, 0.028)], 0.004, STEEL_M, chamfer=0.0, x=-0.03)
    for i in range(5):
        z = 0.06 + i * 0.03
        m.cyl((-0.042, 0.0, z), (0.02, 0.05, 0.02), SHELL_RED, segments=8)
        m.cyl((-0.042, -0.028, z), (0.021, 0.008, 0.021), BRASS, segments=8)
    return m


def weapon_ranger_9():
    """Sidearm: serrated slide with ejection port, polymer frame with rail, textured grip."""
    m = Model("weapon_ranger_9", triangulate=True)
    m.sym([(-0.02, 0.004), (0.24, 0.004), (0.24, 0.034), (0.232, 0.044), (-0.012, 0.044), (-0.02, 0.036)], 0.0145, STEEL_D, chamfer=0.003)
    for z in [-0.012 + i * 0.008 for i in range(5)]:
        for s in (-1, 1):
            m.box((s * 0.0145, 0.026, z), (0.003, 0.026, 0.003), POLY, bevel=0.0)
    for z in [0.19 + i * 0.008 for i in range(4)]:
        for s in (-1, 1):
            m.box((s * 0.0145, 0.026, z), (0.003, 0.022, 0.003), POLY, bevel=0.0)
    m.box((0.004, 0.044, 0.07), (0.022, 0.003, 0.05), POLY, bevel=0.0)  # ejection port
    m.box((0.004, 0.0445, 0.07), (0.018, 0.002, 0.044), BRASS, bevel=0.0)
    m.sym([(0.0, -0.022), (0.2, -0.022), (0.205, -0.012), (0.205, 0.004), (0.0, 0.004)], 0.0135, POLY_L, chamfer=0.002)  # frame
    for z in (0.15, 0.165, 0.18):
        m.box((0, -0.023, z), (0.028, 0.003, 0.006), POLY, bevel=0.0)  # rail slots
    m.sym([(0.06, -0.022), (0.06, -0.042), (0.075, -0.05), (0.115, -0.05), (0.12, -0.035), (0.12, -0.022),
           (0.112, -0.022), (0.112, -0.032), (0.108, -0.043), (0.08, -0.043), (0.068, -0.036), (0.068, -0.022)], 0.006, POLY_L, chamfer=0.0)
    m.sym([(0.085, -0.022), (0.094, -0.022), (0.09, -0.036), (0.082, -0.04)], 0.0025, STEEL_L, chamfer=0.0)
    m.sym([(0.062, -0.012), (0.016, -0.012), (-0.008, -0.002), (-0.028, -0.012), (-0.02, -0.03), (-0.012, -0.112),
           (0.03, -0.12), (0.044, -0.11), (0.05, -0.05)], 0.0145, POLY_L, chamfer=0.003)  # grip with beavertail
    m.sym([(-0.004, -0.04), (0.034, -0.04), (0.03, -0.1), (-0.008, -0.1)], 0.0152, POLY, chamfer=0.0)  # texture panel
    m.sym([(-0.014, -0.112), (0.032, -0.12), (0.034, -0.128), (-0.014, -0.122)], 0.015, STEEL_M, chamfer=0.0)  # base plate
    m.box((0, 0.049, 0.225), (0.005, 0.009, 0.008), STEEL_L, bevel=0.0)  # front sight
    m.sym([(-0.012, 0.044), (0.008, 0.044), (0.008, 0.054), (-0.008, 0.054)], 0.011, STEEL_M, chamfer=0.0)  # rear sight
    m.sym([(-0.024, 0.02), (-0.016, 0.02), (-0.018, 0.034), (-0.03, 0.036)], 0.005, STEEL_L, chamfer=0.0)  # hammer
    tube(m, 0.237, 0.2415, 0.011, POLY)  # bore
    return m


def weapon_saber_7():
    """Machete: curved belly blade with a bright edge and dark spine, finger-grooved handle, riveted scales."""
    m = Model("weapon_saber_7", triangulate=True)
    # Handle with finger grooves (outline) and rivets.
    m.sym([(0.0, 0.018), (0.215, 0.018), (0.215, -0.02), (0.19, -0.024), (0.165, -0.016), (0.14, -0.026),
           (0.115, -0.017), (0.09, -0.027), (0.065, -0.018), (0.03, -0.024), (0.0, -0.02)], 0.014, POLY_L, chamfer=0.003)
    for z in (0.04, 0.12, 0.19):
        rivet(m, 0.0145, -0.002, z, d=0.009, mat=BRASS)
        rivet(m, -0.0145, -0.002, z, d=0.009, mat=BRASS)
    m.sym([(-0.012, 0.016), (0.002, 0.02), (0.002, -0.022), (-0.012, -0.018)], 0.012, STEEL_M, chamfer=0.002)  # pommel
    m.sym([(0.215, 0.03), (0.232, 0.03), (0.232, -0.05), (0.215, -0.05)], 0.018, STEEL_M, chamfer=0.003)  # guard
    # Blade.
    blade = [(0.232, 0.026), (0.6, 0.032), (0.74, 0.024), (0.8, 0.004), (0.77, -0.03), (0.66, -0.055),
             (0.48, -0.058), (0.33, -0.045), (0.232, -0.036)]
    m.sym(blade, 0.0035, STEEL_B, chamfer=0.0015)
    edge = [(0.25, -0.03), (0.33, -0.039), (0.48, -0.052), (0.66, -0.049), (0.765, -0.026), (0.795, 0.002),
            (0.8, 0.004), (0.77, -0.03), (0.66, -0.055), (0.48, -0.058), (0.33, -0.045), (0.25, -0.036)]
    m.sym(edge, 0.0038, EDGE, chamfer=0.0)
    m.sym([(0.232, 0.026), (0.6, 0.032), (0.6, 0.024), (0.232, 0.018)], 0.0039, STEEL_D, chamfer=0.0)  # spine
    m.sym([(0.26, 0.005), (0.34, 0.006), (0.34, -0.012), (0.26, -0.014)], 0.0039, STEEL_M, chamfer=0.0)  # maker's mark
    return m


def weapon_wasp_smg():
    """Compact SMG: angular polymer body, ribbed stick mag, folding wire stock, suppressor, holo sight."""
    m = Model("weapon_wasp_smg", triangulate=True)
    m.sym([(0.0, -0.04), (0.2, -0.04), (0.24, -0.028), (0.3, -0.028), (0.3, 0.03), (0.28, 0.04), (0.015, 0.04), (0.0, 0.03)], 0.023, POLY_L)
    m.sym([(0.02, 0.04), (0.27, 0.04), (0.27, 0.048), (0.02, 0.048)], 0.012, STEEL_M, chamfer=0.0)  # rail
    for z in [0.03 + i * 0.02 for i in range(12)]:
        m.box((0, 0.05, z), (0.026, 0.004, 0.008), STEEL_D, bevel=0.0)
    for s in (-1, 1):
        m.sym([(0.21, -0.02), (0.29, -0.02), (0.29, 0.02), (0.21, 0.02)], 0.002, POLY, chamfer=0.0, x=s * 0.023)  # side vents
        rivet(m, s * 0.023, -0.025, 0.03)
        rivet(m, s * 0.023, 0.02, 0.18)
    m.box((0.024, 0.012, 0.14), (0.003, 0.022, 0.07), POLY, bevel=0.0)  # ejection port
    m.box((-0.026, 0.024, 0.2), (0.008, 0.01, 0.025), STEEL_L, bevel=0.002)  # charging handle
    # Grip, guard, trigger.
    m.sym([(0.075, -0.03), (0.03, -0.03), (0.004, -0.13), (0.01, -0.146), (0.04, -0.148), (0.052, -0.138), (0.078, -0.055)], 0.016, POLY, chamfer=0.003)
    m.sym([(0.068, -0.04), (0.068, -0.066), (0.08, -0.074), (0.125, -0.074), (0.132, -0.064), (0.132, -0.04),
           (0.125, -0.04), (0.125, -0.06), (0.12, -0.066), (0.084, -0.066), (0.076, -0.06), (0.076, -0.04)], 0.004, POLY_L, chamfer=0.0)
    m.sym([(0.094, -0.04), (0.104, -0.04), (0.1, -0.056), (0.092, -0.062)], 0.003, STEEL_L, chamfer=0.0)
    # Long stick magazine ahead of the trigger guard.
    m.sym([(0.14, -0.04), (0.18, -0.04), (0.185, -0.22), (0.142, -0.22)], 0.014, STEEL_M, chamfer=0.003)
    for y in [-0.07 - i * 0.03 for i in range(5)]:
        m.box((0, y, 0.163), (0.03, 0.006, 0.046), STEEL_D, bevel=0.0)
    m.sym([(0.138, -0.218), (0.188, -0.218), (0.19, -0.23), (0.136, -0.23)], 0.016, POLY, chamfer=0.002)
    m.sym([(0.13, -0.036), (0.195, -0.036), (0.195, -0.05), (0.13, -0.05)], 0.0235, POLY, chamfer=0.0)  # mag well
    # Angled foregrip.
    m.sym([(0.235, -0.018), (0.272, -0.018), (0.262, -0.1), (0.24, -0.104), (0.232, -0.09)], 0.013, POLY, chamfer=0.003)
    # Suppressor.
    tube(m, 0.29, 0.42, 0.044, POLY_L, segments=10)
    for z in (0.31, 0.4):
        tube(m, z, z + 0.008, 0.047, STEEL_M, segments=10)
    tube(m, 0.418, 0.4205, 0.014, POLY)
    # Folding wire stock.
    for s in (-1, 1):
        m.sym([(0.0, 0.016), (-0.2, 0.004), (-0.2, -0.004), (0.0, 0.008)], 0.004, STEEL_M, chamfer=0.0, x=s * 0.016)
        m.sym([(0.0, -0.022), (-0.2, -0.05), (-0.2, -0.058), (0.0, -0.03)], 0.004, STEEL_M, chamfer=0.0, x=s * 0.016)
    m.sym([(-0.2, 0.01), (-0.212, 0.01), (-0.212, -0.075), (-0.2, -0.075)], 0.024, POLY, chamfer=0.003)
    # Holo sight with a glowing reticle window.
    m.sym([(0.08, 0.048), (0.15, 0.048), (0.15, 0.06), (0.14, 0.062), (0.09, 0.062), (0.08, 0.06)], 0.016, POLY, chamfer=0.002)
    for z in (0.085, 0.145):
        m.sym([(z - 0.006, 0.06), (z + 0.006, 0.06), (z + 0.004, 0.098), (z - 0.004, 0.098)], 0.017, POLY, chamfer=0.0)
    m.sym([(0.088, 0.096), (0.142, 0.096), (0.142, 0.102), (0.088, 0.102)], 0.017, POLY, chamfer=0.0)
    m.box((0, 0.079, 0.14), (0.026, 0.03, 0.002), GLASS_G, bevel=0.0)
    return m


def weapon_anvil_lmg():
    """Light machine gun: big steel receiver, hinged feed cover, ammo box with belt, perforated shroud, bipod."""
    m = Model("weapon_anvil_lmg", triangulate=True)
    m.sym([(0.0, -0.06), (0.48, -0.06), (0.5, -0.04), (0.5, 0.045), (0.47, 0.058), (0.02, 0.058), (0.0, 0.045)], 0.044, STEEL_D, chamfer=0.004)
    m.sym([(0.08, 0.058), (0.36, 0.058), (0.37, 0.07), (0.35, 0.084), (0.1, 0.084), (0.08, 0.072)], 0.04, STEEL_M, chamfer=0.003)  # feed cover
    for z in (0.12, 0.18, 0.24, 0.3):
        m.box((0, 0.085, z), (0.07, 0.004, 0.012), STEEL_D, bevel=0.0)
    m.cyl((0, 0.07, 0.07), (0.016, 0.08, 0.016), STEEL_L, rot=(0, 0, 90), segments=6)  # hinge
    for s in (-1, 1):
        m.box((s * 0.045, -0.01, 0.25), (0.003, 0.07, 0.36), STEEL_M, bevel=0.0)
        for z in (0.04, 0.46):
            for y in (-0.04, 0.035):
                rivet(m, s * 0.045, y, z, d=0.01)
    m.box((0.046, 0.0, 0.2), (0.003, 0.03, 0.12), POLY, bevel=0.0)  # ejection port
    m.box((0.05, 0.02, 0.4), (0.012, 0.014, 0.04), STEEL_L, bevel=0.003)  # charging handle
    # Ammo box on the left with a brass belt feeding in.
    m.sym([(0.2, -0.06), (0.36, -0.06), (0.37, -0.075), (0.37, -0.2), (0.36, -0.21), (0.2, -0.21), (0.19, -0.2), (0.19, -0.075)], 0.05, "C_4A5236", chamfer=0.004, x=-0.03)
    m.box((-0.03, -0.14, 0.371), (0.06, 0.04, 0.004), "C_D9C27A", bevel=0.0)  # label
    m.box((-0.03, -0.205, 0.28), (0.08, 0.01, 0.12), "C_3A4129", bevel=0.0)
    for i in range(6):
        m.cyl((-0.072 + i * 0.008, 0.02, 0.27), (0.009, 0.05, 0.009), BRASS, rot=(90, 0, 0), segments=6)
    # Carry handle.
    m.sym([(0.24, 0.058), (0.26, 0.058), (0.27, 0.11), (0.39, 0.11), (0.4, 0.058), (0.42, 0.058), (0.41, 0.125), (0.25, 0.125)], 0.012, POLY, chamfer=0.003)
    # Perforated barrel shroud, barrel, flash hider.
    m.sym([(0.5, -0.03), (0.74, -0.03), (0.75, -0.015), (0.75, 0.055), (0.74, 0.07), (0.5, 0.07)], 0.034, STEEL_M, chamfer=0.004)
    for z in [0.53 + i * 0.04 for i in range(5)]:
        for y in (0.0, 0.04):
            for s in (-1, 1):
                m.cyl((s * 0.0345, y, z), (0.016, 0.003, 0.016), POLY, rot=(0, 0, 90), segments=6)
    tube(m, 0.74, 0.9, 0.034, STEEL_D)
    tube(m, 0.84, 0.9, 0.044, STEEL_M)
    for s in (-1, 1):
        m.box((s * 0.016, 0.02, 0.88), (0.006, 0.046, 0.03), POLY, bevel=0.0)  # flash hider slots
    m.sym([(0.83, 0.02), (0.85, 0.02), (0.848, 0.09), (0.832, 0.09)], 0.008, STEEL_D, chamfer=0.0)  # front sight
    # Folded bipod.
    m.box((0, -0.04, 0.755), (0.05, 0.02, 0.03), STEEL_M, bevel=0.003)
    for s in (-1, 1):
        m.box((s * 0.02, -0.045, 0.62), (0.012, 0.012, 0.26), STEEL_L, bevel=0.002)
        m.box((s * 0.02, -0.045, 0.49), (0.02, 0.016, 0.02), RUBBER, bevel=0.003)
    # Grip, guard and skeleton stock.
    m.sym([(0.085, -0.05), (0.04, -0.05), (0.012, -0.155), (0.018, -0.17), (0.05, -0.172), (0.062, -0.162), (0.088, -0.075)], 0.018, POLY, chamfer=0.003)
    m.sym([(0.08, -0.06), (0.08, -0.085), (0.092, -0.094), (0.14, -0.094), (0.148, -0.082), (0.148, -0.06),
           (0.14, -0.06), (0.14, -0.08), (0.135, -0.086), (0.096, -0.086), (0.088, -0.08), (0.088, -0.06)], 0.005, STEEL_M, chamfer=0.0)
    m.sym([(0.0, 0.04), (-0.27, 0.02), (-0.27, -0.0), (0.0, 0.012)], 0.026, POLY, chamfer=0.003)  # top strut
    m.sym([(0.0, -0.04), (-0.27, -0.09), (-0.27, -0.112), (0.0, -0.06)], 0.026, POLY, chamfer=0.003)  # bottom strut
    m.sym([(-0.27, 0.03), (-0.29, 0.03), (-0.29, -0.13), (-0.27, -0.13)], 0.03, RUBBER, chamfer=0.003)  # butt
    m.sym([(-0.06, 0.03), (-0.04, 0.03), (-0.04, -0.06), (-0.06, -0.065)], 0.024, POLY, chamfer=0.0)
    return m


def weapon_tether_cleaver():
    """Heavy cleaver on a corded haft, with a lanyard hole, bright edge and a trailing tether chain."""
    m = Model("weapon_tether_cleaver", triangulate=True)
    m.sym([(0.0, 0.02), (0.3, 0.02), (0.3, -0.02), (0.0, -0.02)], 0.017, WOOD_W, chamfer=0.006)
    for z in (0.03, 0.07, 0.11, 0.15, 0.19):
        m.sym([(z, 0.023), (z + 0.022, 0.023), (z + 0.022, -0.023), (z, -0.023)], 0.0195, TAN, chamfer=0.002)
    m.sym([(-0.015, 0.024), (0.0, 0.024), (0.0, -0.024), (-0.015, -0.024)], 0.02, STEEL_M, chamfer=0.003)
    m.sym([(0.28, 0.03), (0.32, 0.03), (0.32, -0.03), (0.28, -0.03)], 0.022, STEEL_M, chamfer=0.003)  # collar
    blade = [(0.3, 0.13), (0.78, 0.15), (0.8, 0.13), (0.8, -0.04), (0.77, -0.07), (0.33, -0.06), (0.3, -0.03)]
    m.sym(blade, 0.006, STEEL_B, chamfer=0.002)
    m.sym([(0.33, -0.045), (0.77, -0.055), (0.8, -0.03), (0.8, -0.04), (0.77, -0.07), (0.33, -0.06)], 0.0065, EDGE, chamfer=0.0)
    m.sym([(0.3, 0.13), (0.78, 0.15), (0.8, 0.13), (0.78, 0.135), (0.3, 0.115)], 0.0068, STEEL_D, chamfer=0.0)  # spine
    m.cyl((0, 0.09, 0.72), (0.04, 0.0142, 0.04), POLY, rot=(0, 0, 90), segments=10)  # lanyard hole
    m.cyl((0, 0.09, 0.72), (0.048, 0.0135, 0.048), STEEL_M, rot=(0, 0, 90), segments=10)
    for z in (0.4, 0.52, 0.64):
        rivet(m, 0.0065, 0.1, z, d=0.012)
        rivet(m, -0.0065, 0.1, z, d=0.012)
    m.cyl((0, 0.06, 0.335), (0.03, 0.012, 0.03), STEEL_L, rot=(0, 0, 90), segments=8)  # tether ring
    for i in range(5):  # chain links trailing back along the haft
        m.box((0.0, 0.045 - i * 0.02, 0.3 - i * 0.04), (0.006 if i % 2 else 0.016, 0.012, 0.032), STEEL_L,
              rot=(35, 0, 0), bevel=0.0)
    return m


def weapon_mule_launcher():
    """Revolving grenade launcher: fluted drum, ribbed barrel, polymer stock, foregrip, leaf sight."""
    m = Model("weapon_mule_launcher", triangulate=True)
    tube(m, 0.21, 0.65, 0.07, "C_4A5236", segments=12)
    for z in (0.3, 0.38, 0.46, 0.54):
        tube(m, z, z + 0.012, 0.076, STEEL_M, segments=12)
    tube(m, 0.62, 0.65, 0.08, STEEL_D, segments=12)
    tube(m, 0.6495, 0.651, 0.05, POLY, segments=12)
    # Frame around the drum, drum with chambers and flutes.
    m.sym([(0.0, -0.075), (0.24, -0.075), (0.24, -0.05), (0.0, -0.05)], 0.03, STEEL_D, chamfer=0.003)  # bottom strap
    m.sym([(-0.01, -0.075), (0.025, -0.075), (0.025, 0.105), (-0.01, 0.105)], 0.032, STEEL_D, chamfer=0.003)  # rear plate
    m.sym([(0.215, -0.075), (0.25, -0.075), (0.25, 0.105), (0.215, 0.105)], 0.032, STEEL_D, chamfer=0.003)  # front plate
    tube(m, 0.0, 0.25, 0.016, STEEL_L, y=0.015, segments=6)  # drum axle
    tube(m, 0.025, 0.215, 0.17, "C_3B4128", y=0.015, segments=12)
    for a in range(6):
        ang = math.radians(a * 60 + 30)
        tube(m, 0.022, 0.218, 0.036, POLY, y=0.015 + math.sin(ang) * 0.052, x=math.cos(ang) * 0.052, segments=6)
        tube(m, 0.05, 0.19, 0.012, STEEL_M, y=0.015 + math.sin(ang + math.radians(30)) * 0.082,
             x=math.cos(ang + math.radians(30)) * 0.082, segments=4)
    m.sym([(0.0, 0.09), (0.24, 0.09), (0.24, 0.105), (0.0, 0.105)], 0.03, STEEL_M, chamfer=0.002)  # top strap
    # Leaf sight with hazard marks.
    m.sym([(0.3, 0.035), (0.33, 0.035), (0.33, 0.1), (0.3, 0.1)], 0.022, HAZARD, chamfer=0.002)
    m.box((0, 0.09, 0.315), (0.03, 0.004, 0.032), DARK, bevel=0.0)
    m.box((0, 0.07, 0.315), (0.03, 0.004, 0.032), DARK, bevel=0.0)
    m.box((0.036, 0.02, 0.45), (0.004, 0.03, 0.12), HAZARD, bevel=0.0)
    # Grip, guard, foregrip, stock.
    m.sym([(0.075, -0.06), (0.03, -0.06), (0.004, -0.15), (0.01, -0.165), (0.042, -0.168), (0.054, -0.156), (0.078, -0.065)], 0.018, POLY, chamfer=0.003)
    m.sym([(0.07, -0.05), (0.07, -0.076), (0.082, -0.084), (0.13, -0.084), (0.138, -0.072), (0.138, -0.05),
           (0.13, -0.05), (0.13, -0.07), (0.125, -0.076), (0.086, -0.076), (0.078, -0.07), (0.078, -0.05)], 0.005, STEEL_M, chamfer=0.0)
    m.sym([(0.38, -0.008), (0.43, -0.008), (0.425, -0.13), (0.39, -0.135), (0.382, -0.12)], 0.017, POLY, chamfer=0.003)
    m.sym([(0.0, 0.05), (-0.06, 0.04), (-0.27, 0.02), (-0.28, 0.006), (-0.28, -0.1), (-0.265, -0.11),
           (-0.12, -0.075), (-0.03, -0.06), (0.0, -0.05)], 0.026, POLY_L, chamfer=0.005)
    m.sym([(-0.28, 0.01), (-0.296, 0.01), (-0.296, -0.104), (-0.28, -0.11)], 0.028, RUBBER, chamfer=0.003)
    return m


def weapon_vesper_dmr():
    """Marksman rifle: long tan polymer chassis, free-float handguard, 8x scope, box magazine, adjustable stock."""
    m = Model("weapon_vesper_dmr", triangulate=True)
    m.sym([(0.0, -0.042), (0.30, -0.042), (0.31, -0.03), (0.31, 0.03), (0.0, 0.03)], 0.023, TAN)  # lower receiver
    m.sym([(0.0, 0.03), (0.33, 0.03), (0.33, 0.046), (0.0, 0.046)], 0.02, STEEL_D, chamfer=0.002)  # upper
    for z in [0.02 + i * 0.022 for i in range(14)]:
        m.box((0, 0.05, z), (0.024, 0.004, 0.008), STEEL_D, bevel=0.0)  # top rail
    m.box((0.024, 0.02, 0.17), (0.003, 0.02, 0.07), POLY, bevel=0.0)  # ejection port
    m.box((0.026, 0.024, 0.1), (0.006, 0.008, 0.03), STEEL_L, bevel=0.002)  # bolt handle
    # Handguard with vent slots.
    m.sym([(0.31, -0.028), (0.56, -0.028), (0.565, -0.02), (0.565, 0.042), (0.31, 0.042)], 0.022, POLY_L, chamfer=0.004)
    for z in (0.35, 0.39, 0.43, 0.47, 0.51):
        for s in (-1, 1):
            m.box((s * 0.022, 0.008, z), (0.002, 0.03, 0.018), POLY, bevel=0.0)
    tube(m, 0.56, 0.63, 0.017, STEEL_D)
    tube(m, 0.625, 0.65, 0.026, STEEL_M, segments=10)  # muzzle brake
    for z in (0.632, 0.642):
        m.box((0, 0.02, z), (0.03, 0.006, 0.004), POLY, bevel=0.0)
    m.box((0, 0.02, 0.651), (0.012, 0.012, 0.002), POLY, bevel=0.0)
    # Grip, trigger guard, magazine.
    m.sym([(0.08, -0.03), (0.035, -0.03), (0.01, -0.14), (0.016, -0.156), (0.046, -0.158), (0.058, -0.146), (0.083, -0.05)], 0.016, POLY, chamfer=0.004)
    m.sym([(0.09, -0.042), (0.09, -0.066), (0.1, -0.074), (0.15, -0.074), (0.158, -0.064), (0.158, -0.042),
           (0.15, -0.042), (0.15, -0.06), (0.145, -0.066), (0.104, -0.066), (0.098, -0.06), (0.098, -0.042)], 0.004, POLY, chamfer=0.0)
    m.sym([(0.112, -0.042), (0.122, -0.042), (0.118, -0.058), (0.11, -0.064)], 0.003, STEEL_L, chamfer=0.0)
    m.sym([(0.17, -0.042), (0.24, -0.042), (0.245, -0.14), (0.172, -0.14)], 0.016, STEEL_M, chamfer=0.003)
    m.sym([(0.168, -0.138), (0.248, -0.138), (0.25, -0.15), (0.166, -0.15)], 0.018, POLY, chamfer=0.002)
    # Adjustable stock with cheek riser.
    m.sym([(0.0, 0.026), (-0.08, 0.02), (-0.08, -0.012), (0.0, -0.03)], 0.012, STEEL_M, chamfer=0.002)  # buffer tube
    m.sym([(-0.08, 0.05), (-0.27, 0.05), (-0.29, 0.03), (-0.29, -0.11), (-0.27, -0.12), (-0.2, -0.09), (-0.08, -0.03)], 0.02, TAN, chamfer=0.006)
    m.sym([(-0.12, 0.05), (-0.25, 0.05), (-0.25, 0.066), (-0.12, 0.066)], 0.018, POLY, chamfer=0.003)  # cheek riser
    m.sym([(-0.29, 0.034), (-0.302, 0.034), (-0.302, -0.114), (-0.29, -0.114)], 0.022, POLY, chamfer=0.002)  # butt pad
    # Scope: rings, body, bells, glowing lens.
    for z in (0.08, 0.22):
        m.sym([(z - 0.012, 0.05), (z + 0.012, 0.05), (z + 0.012, 0.086), (z - 0.012, 0.086)], 0.02, STEEL_D, chamfer=0.002)
    tube(m, 0.03, 0.27, 0.032, POLY, y=0.1, segments=12)
    tube(m, -0.02, 0.04, 0.042, POLY_L, y=0.1, segments=12)  # eyepiece
    tube(m, 0.26, 0.33, 0.05, POLY_L, y=0.1, segments=12)  # objective bell
    m.cyl((0, 0.135, 0.15), (0.03, 0.03, 0.03), STEEL_M, segments=8)  # turret
    m.cyl((0.03, 0.1, 0.15), (0.026, 0.03, 0.026), STEEL_M, rot=(0, 0, 90), segments=8)
    tube(m, 0.33, 0.332, 0.046, GLASS_G, y=0.1, segments=12)
    return m


def weapon_bulldog_44():
    """Heavy revolver: fluted six-shot cylinder, full-lug barrel, wood grip, big hammer."""
    m = Model("weapon_bulldog_44", triangulate=True)
    m.sym([(-0.02, -0.02), (0.07, -0.02), (0.07, 0.042), (0.0, 0.05), (-0.02, 0.04)], 0.016, STEEL_D, chamfer=0.003)  # frame
    m.sym([(0.07, -0.008), (0.24, -0.008), (0.24, 0.042), (0.07, 0.042)], 0.0115, STEEL_D, chamfer=0.003)  # barrel lug
    m.box((0, 0.045, 0.155), (0.008, 0.004, 0.17), STEEL_M, bevel=0.0)  # top rib
    m.box((0, 0.053, 0.23), (0.005, 0.012, 0.016), DOT_R, bevel=0.0)  # front sight insert
    tube(m, 0.2, 0.2415, 0.014, POLY, y=0.022)  # bore
    m.cyl((0, 0.016, 0.035), (0.05, 0.07, 0.05), STEEL_M, rot=(90, 0, 0), segments=6)  # cylinder (hex = fluted)
    for a in range(6):
        ang = a * math.pi / 3
        m.cyl((0.017 * math.cos(ang), 0.016 + 0.017 * math.sin(ang), 0.071), (0.01, 0.002, 0.01), BRASS, rot=(90, 0, 0), segments=6)
    m.sym([(0.0, -0.02), (0.06, -0.02), (0.062, -0.032), (0.05, -0.04), (0.012, -0.04), (0.0, -0.03)], 0.005, STEEL_M, chamfer=0.0)  # guard
    m.sym([(0.024, -0.02), (0.034, -0.02), (0.03, -0.034), (0.022, -0.038)], 0.0025, STEEL_L, chamfer=0.0)  # trigger
    m.sym([(-0.002, -0.012), (-0.022, -0.01), (-0.05, -0.1), (-0.044, -0.118), (-0.012, -0.124), (0.004, -0.11), (0.006, -0.04)], 0.0165, WOOD_W, chamfer=0.005)
    m.sym([(-0.044, -0.112), (-0.012, -0.118), (-0.012, -0.124), (-0.044, -0.118)], 0.017, STEEL_M, chamfer=0.0)
    m.sym([(-0.022, 0.03), (-0.012, 0.034), (-0.03, 0.06), (-0.044, 0.056), (-0.034, 0.036)], 0.006, STEEL_L, chamfer=0.0)  # hammer
    return m


def weapon_hailstorm():
    """Automatic shotgun: boxy receiver, 20-round drum, heat-shrouded barrel, carry handle, polymer stock."""
    m = Model("weapon_hailstorm", triangulate=True)
    m.sym([(0.0, -0.05), (0.36, -0.05), (0.37, -0.035), (0.37, 0.05), (0.02, 0.05), (0.0, 0.04)], 0.03, POLY_L)
    m.box((0.031, 0.015, 0.2), (0.003, 0.03, 0.1), POLY, bevel=0.0)  # ejection port
    m.box((0.034, 0.028, 0.28), (0.008, 0.01, 0.03), STEEL_L, bevel=0.002)
    for s in (-1, 1):
        rivet(m, s * 0.03, -0.035, 0.03)
        rivet(m, s * 0.03, -0.035, 0.34)
        rivet(m, s * 0.03, 0.035, 0.34)
    # Carry handle with a glowing sight dot.
    for z in (0.06, 0.3):
        m.sym([(z - 0.015, 0.05), (z + 0.015, 0.05), (z + 0.01, 0.09), (z - 0.01, 0.09)], 0.012, POLY, chamfer=0.0)
    m.sym([(0.045, 0.088), (0.315, 0.088), (0.315, 0.1), (0.045, 0.1)], 0.012, POLY, chamfer=0.002)
    m.box((0, 0.106, 0.3), (0.006, 0.01, 0.006), DOT_R, bevel=0.0)
    # Grip and guard.
    m.sym([(0.09, -0.04), (0.045, -0.04), (0.02, -0.15), (0.026, -0.166), (0.056, -0.168), (0.068, -0.156), (0.094, -0.06)], 0.018, POLY, chamfer=0.004)
    m.sym([(0.09, -0.05), (0.09, -0.076), (0.1, -0.084), (0.15, -0.084), (0.158, -0.074), (0.158, -0.05),
           (0.15, -0.05), (0.15, -0.07), (0.145, -0.076), (0.104, -0.076), (0.098, -0.07), (0.098, -0.05)], 0.004, POLY_L, chamfer=0.0)
    m.sym([(0.112, -0.05), (0.122, -0.05), (0.118, -0.066), (0.11, -0.072)], 0.003, STEEL_L, chamfer=0.0)
    # Drum magazine under the receiver, with red shell bases showing.
    m.cyl((0, -0.12, 0.25), (0.16, 0.07, 0.16), POLY, rot=(0, 0, 90), segments=14)
    m.cyl((0, -0.12, 0.25), (0.12, 0.072, 0.12), POLY_L, rot=(0, 0, 90), segments=14)
    m.cyl((0, -0.12, 0.25), (0.04, 0.074, 0.04), STEEL_M, rot=(0, 0, 90), segments=8)
    m.box((0, -0.06, 0.25), (0.05, 0.03, 0.06), POLY, bevel=0.0)  # drum neck
    for i in range(5):
        m.box((0.036, -0.16 + i * 0.012, 0.21 + i * 0.016), (0.003, 0.01, 0.012), SHELL_RED, bevel=0.0)
    # Barrel inside a vented shroud.
    tube(m, 0.37, 0.8, 0.024, STEEL_D)
    tube(m, 0.37, 0.66, 0.046, STEEL_M, segments=10)
    for z in [0.4 + i * 0.05 for i in range(5)]:
        for s in (-1, 1):
            m.box((s * 0.023, 0.02, z), (0.003, 0.016, 0.024), POLY, bevel=0.0)
    m.sym([(0.3, -0.05), (0.62, -0.05), (0.62, -0.012), (0.3, -0.012)], 0.026, POLY_L, chamfer=0.004)  # foregrip
    tube(m, 0.795, 0.802, 0.016, POLY)
    # Fixed polymer stock.
    m.sym([(0.0, 0.04), (-0.27, 0.03), (-0.29, 0.01), (-0.29, -0.12), (-0.27, -0.13), (-0.12, -0.08), (0.0, -0.05)], 0.024, POLY, chamfer=0.006)
    m.sym([(-0.29, 0.014), (-0.302, 0.014), (-0.302, -0.124), (-0.29, -0.124)], 0.026, POLY_L, chamfer=0.002)
    return m


def weapon_kestrel_pdw():
    """Personal defence weapon: sleek bullpup-style shell, top-loaded translucent magazine, thumbhole grip."""
    m = Model("weapon_kestrel_pdw", triangulate=True)
    shell = [(-0.18, -0.03), (-0.18, 0.02), (-0.15, 0.045), (0.3, 0.045), (0.36, 0.02), (0.36, -0.03), (0.28, -0.05),
             (0.2, -0.05), (0.18, -0.03), (0.1, -0.03), (0.06, -0.14), (0.02, -0.14), (0.0, -0.06), (-0.09, -0.07)]
    m.sym(shell, 0.024, POLY_L, chamfer=0.006)
    m.sym([(-0.07, -0.03), (0.0, -0.035), (-0.005, -0.06), (-0.075, -0.055)], 0.025, POLY, chamfer=0.0)  # thumbhole shadow
    m.sym([(-0.16, 0.045), (0.3, 0.045), (0.3, 0.054), (-0.16, 0.054)], 0.022, TAN, chamfer=0.002)  # top cover
    m.box((0, 0.058, 0.07), (0.03, 0.01, 0.34), GLASS_G, bevel=0.002)  # translucent magazine on top
    for z in [-0.08 + i * 0.03 for i in range(11)]:
        m.box((0, 0.064, z), (0.028, 0.002, 0.004), BRASS, bevel=0.0)  # rounds seen through the mag
    m.sym([(0.01, 0.054), (0.05, 0.054), (0.05, 0.09), (0.01, 0.09)], 0.004, POLY, chamfer=0.0)  # rear sight ears
    m.sym([(0.26, 0.054), (0.29, 0.054), (0.29, 0.086), (0.26, 0.086)], 0.004, POLY, chamfer=0.0)
    m.sym([(0.08, -0.03), (0.088, -0.03), (0.084, -0.046), (0.076, -0.05)], 0.003, STEEL_L, chamfer=0.0)  # trigger
    for s in (-1, 1):
        m.box((s * 0.025, 0.0, 0.24), (0.003, 0.03, 0.08), POLY, bevel=0.0)  # side grip panels
        rivet(m, s * 0.024, 0.02, -0.12)
    m.sym([(0.2, -0.05), (0.24, -0.05), (0.232, -0.11), (0.21, -0.112)], 0.012, POLY, chamfer=0.003)  # finger stop
    tube(m, 0.36, 0.42, 0.018, STEEL_D)
    tube(m, 0.395, 0.42, 0.026, STEEL_M, segments=8)  # flash hider
    tube(m, 0.418, 0.4205, 0.012, POLY)
    m.box((-0.025, 0.024, 0.0), (0.006, 0.01, 0.03), STEEL_L, bevel=0.002)  # ambi charging handle
    m.box((0.025, 0.024, 0.0), (0.006, 0.01, 0.03), STEEL_L, bevel=0.002)
    return m


def weapon_railspike_maul():
    """Two-handed maul: a railway tie-plate head with a spike on the back, on a long taped steel haft."""
    m = Model("weapon_railspike_maul", triangulate=True)
    tube(m, -0.04, 0.66, 0.034, STEEL_M, y=0.0, segments=8)  # haft
    for z in (0.0, 0.05, 0.1, 0.15, 0.2):
        tube(m, z, z + 0.035, 0.04, TAN, y=0.0, segments=8)  # grip tape
    tube(m, -0.06, -0.04, 0.048, STEEL_D, y=0.0, segments=8)  # pommel
    tube(m, 0.42, 0.45, 0.042, POLY, y=0.0, segments=8)  # second hand wrap
    # Head: chunky block across the haft (Y), striking face up, spike down.
    m.box((0, 0.03, 0.72), (0.09, 0.2, 0.16), STEEL_D, bevel=0.012)
    m.box((0, 0.14, 0.72), (0.1, 0.03, 0.17), STEEL_L, bevel=0.006)  # striking face
    m.box((0, 0.03, 0.795), (0.094, 0.18, 0.012), RUST, bevel=0.0)  # tie-plate edge
    for y in (-0.03, 0.05, 0.11):
        rivet(m, 0.046, y, 0.69, d=0.016)
        rivet(m, -0.046, y, 0.69, d=0.016)
    m.cone((0, -0.12, 0.72), (0.05, 0.14, 0.05), STEEL_B, rot=(180, 0, 0), segments=6)  # back spike
    m.box((0, -0.06, 0.72), (0.06, 0.04, 0.06), STEEL_M, bevel=0.004)
    m.box((0, 0.03, 0.62), (0.05, 0.06, 0.06), STEEL_M, bevel=0.004)  # collar
    return m


BUILDERS = [
    enemy_standard, enemy_lean, enemy_plated, enemy_crawler, enemy_bloated, enemy_spitter, enemy_alpha, enemy_specimen,
    player_body, player_head,
    lambda: fob_booth(False), lambda: fob_booth(True), fob_transport, crate_stack,
    prop_crate, prop_locker, prop_column, prop_partition, prop_door, prop_relay, prop_switch, supply_cache,
    prop_light_fixture, prop_lab_bench, prop_tank, prop_server_rack, prop_shelf, prop_barrel, prop_table, prop_planter,
    prop_desk, prop_generator, prop_fusebox, prop_wall_vent, prop_extinguisher, prop_rubble,
    weapon_halberd_ar, weapon_breacher_12, weapon_ranger_9, weapon_saber_7, weapon_wasp_smg, weapon_anvil_lmg,
    weapon_tether_cleaver, weapon_mule_launcher,
    enemy_brood, weapon_vesper_dmr, weapon_bulldog_44, weapon_hailstorm, weapon_kestrel_pdw, weapon_railspike_maul,
]


# ----------------------------------------------------------------------------------------------- preview

# Display size for fit models (they are unit cubes) in the preview: their usual in-game box.
FIT_SIZES = {
    "prop_crate": (1.8, 1.0, 1.1), "prop_locker": (1.2, 1.8, 1.2), "prop_column": (0.8, 4.5, 0.8),
    "prop_partition": (3.6, 1.2, 0.4), "prop_door": (2.5, 2.6, 0.25), "prop_relay": (1.3, 1.6, 0.7),
    "prop_switch": (0.7, 0.9, 0.2),
    "prop_light_fixture": (1.4, 0.14, 0.5), "prop_lab_bench": (2.4, 0.95, 0.9), "prop_tank": (1.2, 2.4, 1.2),
    "prop_server_rack": (0.8, 2.2, 1.1), "prop_shelf": (2.4, 2.2, 0.7), "prop_table": (2.2, 0.8, 1.6),
    "prop_planter": (1.4, 1.8, 1.4), "prop_desk": (2.0, 1.2, 0.9), "prop_generator": (1.8, 1.8, 1.4), "fob_transport": (3.2, 2.2, 6.0), "fob_crates": (2.4, 1.2, 1.2),
}

SHEETS = {
    "characters": ["enemy_standard", "enemy_lean", "enemy_plated", "enemy_crawler", "enemy_bloated",
                   "enemy_spitter", "enemy_alpha", "enemy_specimen", "enemy_brood", "player"],
    "fob": ["fob_booth", "fob_booth_wide", "fob_transport", "fob_crates"],
    "map": ["prop_crate", "prop_locker", "prop_column", "prop_partition", "prop_door", "prop_relay", "prop_switch", "supply_cache"],
    "rooms": ["prop_lab_bench", "prop_tank", "prop_server_rack", "prop_shelf", "prop_table", "prop_planter", "prop_desk",
              "prop_generator"],
    "clutter": ["prop_barrel", "prop_light_fixture", "prop_fusebox", "prop_wall_vent", "prop_extinguisher", "prop_rubble"],
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
                gun = by_name.get("weapon_halberd_ar")
                if gun is not None:  # what a teammate holds (RemotePlayerProxy.WeaponAnchor)
                    g = gun.copy()
                    g.data = gun.data
                    g.hide_render = False
                    scene.collection.objects.link(g)
                    g.location = (x - 0.25, -0.18, 1.68 - 0.27)
                    temp.append(g)
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
    paths.append(preview_weapons([o for o in objects if o.name.startswith("weapon_")], out_dir))
    return paths


def preview_weapons(weapons, out_dir):
    """Side view of every weapon, two columns, muzzle pointing right."""
    scene = bpy.context.scene
    temp = []
    for i, src in enumerate(weapons):
        c = src.copy()
        c.data = src.data
        c.hide_render = False
        scene.collection.objects.link(c)
        col, row = i % 2, i // 2
        # Seen from Blender -X (the weapon's left side), Blender -Y (muzzle) points right in the image.
        c.location = (0, -col * 1.3, -row * 0.45)
        temp.append(c)
    bpy.ops.object.camera_add()
    cam = bpy.context.active_object
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = 2.9
    target = Vector((0, -0.95, -0.7))
    cam.location = target + Vector((-4, -0.6, 0.9))
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    scene.render.resolution_x = 1800
    scene.render.resolution_y = 1200
    path = os.path.join(out_dir, "models_weapons.png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    for c in temp:
        bpy.data.objects.remove(c, do_unlink=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    # One close 3/4 shot per weapon (dark background, like a loadout screen).
    hero_dir = os.path.join(out_dir, "weapons")
    os.makedirs(hero_dir, exist_ok=True)
    old_bg = scene.display.shading.background_type
    scene.display.shading.background_type = 'VIEWPORT'
    scene.display.shading.background_color = (0.04, 0.04, 0.045)
    for src in weapons:
        c = src.copy()
        c.data = src.data
        c.hide_render = False
        scene.collection.objects.link(c)
        length = src.dimensions.y
        centre = Vector((0, -(min(v.co.y for v in src.data.vertices) + max(v.co.y for v in src.data.vertices)) / 2,
                         (min(v.co.z for v in src.data.vertices) + max(v.co.z for v in src.data.vertices)) / 2))
        centre.y = -centre.y
        bpy.ops.object.camera_add()
        cam = bpy.context.active_object
        cam.data.type = 'ORTHO'
        cam.data.ortho_scale = max(length, src.dimensions.z * 2.2) * 1.15
        cam.location = centre + Vector((-2.0, -0.55, 0.45))
        cam.rotation_euler = (centre - cam.location).to_track_quat('-Z', 'Y').to_euler()
        scene.camera = cam
        scene.render.resolution_x = 1400
        scene.render.resolution_y = 700
        scene.render.filepath = os.path.join(hero_dir, src.name + ".png")
        bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(c, do_unlink=True)
        bpy.data.objects.remove(cam, do_unlink=True)
    scene.display.shading.background_type = old_bg
    return path


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
