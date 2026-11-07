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
        # Rig groups (M13 enemy animation): name -> (pivot in Unity space, parts). Each group is exported as a child
        # object whose origin is the pivot (hip, shoulder, neck), so the game can swing it like a limb.
        self.groups = {}
        self._group = None

    class _Group:
        def __init__(self, model, name):
            self.model, self.name = model, name

        def __enter__(self):
            self.model._group = self.name

        def __exit__(self, *exc):
            self.model._group = None

    def rig(self, name, pivot):
        """with m.rig("LegL", hip): parts made inside become the movable child "LegL" pivoting at hip."""
        self.groups[name] = (pivot, self.groups.get(name, (pivot, []))[1])
        return Model._Group(self, name)

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
        self._add(o)
        return o

    def _add(self, o):
        self.parts.append(o)
        if self._group is not None:
            self.groups[self._group][1].append(o)

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
        self._add(o)
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

    @staticmethod
    def _join(parts, name):
        bpy.ops.object.select_all(action='DESELECT')
        for o in parts:
            o.select_set(True)
        bpy.context.view_layer.objects.active = parts[0]
        if len(parts) > 1:
            bpy.ops.object.join()
        o = bpy.context.active_object
        o.name = name
        o.data.name = name
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        return o

    def finish(self):
        grouped = {id(p) for _, ps in self.groups.values() for p in ps}
        o = Model._join([p for p in self.parts if id(p) not in grouped], self.name)
        for gname, (pivot, ps) in self.groups.items():
            if not ps:
                continue
            c = Model._join(ps, gname)
            c.data.name = f"{self.name}_{gname}"
            pb = to_b(pivot)
            c.data.transform(Matrix.Translation(-pb))  # origin = pivot
            c.location = pb
            c.parent = o
        bpy.ops.object.select_all(action='DESELECT')
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
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
    for c in o.children:
        c.select_set(True)
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


def write_meta(fbx_path, template="fbx_meta_template.txt"):
    """New models get a full ModelImporter .meta (Unity rejects a bare guid-only one); existing metas keep their guid."""
    meta = fbx_path + ".meta"
    if os.path.exists(meta):
        return
    import uuid
    with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), template)) as f:
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
    # Legs, slightly apart and bent (rig: LegL/LegR swing at the hips).
    for s, side in ((-1, "L"), (1, "R")):
        with m.rig("Leg" + side, (s * 0.12, 0.9, 0.0)):
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
    with m.rig("Head", (0, 1.5, 0.07)):
        m.sph(head, (0.25, 0.29, 0.27), "SkinDark")
        m.box((0, head[1] - 0.1, head[2] + 0.07), (0.16, 0.07, 0.12), "SkinDark", bevel=0.02)  # jaw
        eyes(m, head, 0.27)
    # Arms reaching forward (the shamble), at the placeholder arm height.
    ay = h * 0.68
    for s, side in ((-1, "L"), (1, "R")):
        with m.rig("Arm" + side, (s * 0.27, 1.4, 0.05)):
            m.limb((s * 0.27, 1.4, 0.05), (s * 0.31, ay, 0.3), (0.1, 0.1), "Skin")
            m.limb((s * 0.31, ay, 0.3), (s * 0.3, ay - 0.04, 0.6), (0.085, 0.085), "Skin")
            m.box((s * 0.3, ay - 0.05, 0.68), (0.1, 0.06, 0.14), "SkinDark", bevel=0.015)
    return m


def enemy_lean():
    m = Model("enemy_lean")
    h = 1.65
    for s, side in ((-1, "L"), (1, "R")):
        with m.rig("Leg" + side, (s * 0.1, 0.82, -0.02)):
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
    with m.rig("Head", (0, 1.3, 0.14)):
        m.sph(head, (0.24, 0.27, 0.27), "SkinDark")
        m.box((0, head[1] - 0.09, head[2] + 0.09), (0.13, 0.06, 0.12), "SkinDark", bevel=0.01)
        eyes(m, head, 0.27, 0.06)
    ay = h * 0.55
    for s, side in ((-1, "L"), (1, "R")):
        with m.rig("Arm" + side, (s * 0.2, 1.25, 0.14)):
            m.limb((s * 0.2, 1.25, 0.14), (s * 0.34, ay + 0.05, 0.25), (0.07, 0.07), "Skin")
            m.limb((s * 0.34, ay + 0.05, 0.25), (s * 0.36, ay - 0.06, 0.58), (0.06, 0.06), "Skin")
            for c in (-1, 0, 1):  # claws
                m.cone((s * 0.36 + c * 0.025, ay - 0.08, 0.66), (0.025, 0.14, 0.025), BONE, rot=(100, 0, 0), segments=5)
    return m


def enemy_plated():
    m = Model("enemy_plated")
    h, r = 2.3, 0.6
    for s, side in ((-1, "L"), (1, "R")):
        with m.rig("Leg" + side, (s * 0.3, 1.0, 0.0)):
            m.limb((s * 0.3, 1.0, 0.0), (s * 0.34, 0.5, 0.06), (0.3, 0.32), CLOTH_DARK)
            m.limb((s * 0.34, 0.52, 0.06), (s * 0.34, 0.1, 0.0), (0.27, 0.28), "SkinDark")
            m.box((s * 0.34, 0.06, 0.06), (0.3, 0.12, 0.42), RUBBER, bevel=0.03)
    m.box((0, 1.05, 0), (0.8, 0.3, 0.6), CLOTH_DARK, bevel=0.06)
    m.box((0, 1.45, -0.02), (1.1, 0.7, 0.78), "Skin", bevel=0.14)  # huge chest
    m.box((0, 1.82, -0.04), (1.25, 0.3, 0.7), "Skin", bevel=0.12)  # shoulders
    head = (0, h * 0.88, 0.08)
    with m.rig("Head", (0, 1.9, 0.08)):
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
    for s, side in ((-1, "L"), (1, "R")):
        with m.rig("Arm" + side, (s * 0.68, 1.8, -0.02)):
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
    with m.rig("Head", (0, 0.38, 0.38)):
        m.sph((0, h * 0.55, 0.48), (0.22, 0.2, 0.26), "SkinDark")
        m.box((0, h * 0.55 - 0.07, 0.56), (0.13, 0.05, 0.12), "SkinDark", bevel=0.01)
        eyes(m, (0, h * 0.55, 0.48), 0.26, 0.05)
    for s, side in ((-1, "L"), (1, "R")):
        # Front arms (placeholder arms at x = +-r, z = 0.35).
        with m.rig("Arm" + side, (s * 0.18, 0.36, 0.3)):
            m.limb((s * 0.18, 0.36, 0.3), (s * 0.34, 0.3, 0.42), (0.07, 0.07), "Skin")
            m.limb((s * 0.34, 0.3, 0.42), (s * 0.3, 0.02, 0.5), (0.06, 0.06), "Skin")
        # Back legs.
        with m.rig("Leg" + side, (s * 0.16, 0.3, -0.34)):
            m.limb((s * 0.16, 0.3, -0.34), (s * 0.34, 0.28, -0.42), (0.08, 0.08), "Skin")
            m.limb((s * 0.34, 0.28, -0.42), (s * 0.32, 0.02, -0.5), (0.07, 0.07), "Skin")
    m.box((0, 0.28, -0.12), (0.4, 0.16, 0.4), CLOTH_DARK, bevel=0.03)
    return m


def enemy_bloated():
    m = Model("enemy_bloated")
    h, r = 1.75, 0.42
    for s, side in ((-1, "L"), (1, "R")):
        with m.rig("Leg" + side, (s * 0.18, 0.5, 0.0)):
            m.limb((s * 0.18, 0.5, 0.0), (s * 0.2, 0.06, 0.02), (0.18, 0.18), "SkinDark")
            m.box((s * 0.2, 0.04, 0.05), (0.18, 0.08, 0.28), RUBBER)
    m.sph((0, h * 0.47, 0), (r * 2.35, h * 0.52, r * 2.15), "Skin", segments=14)
    m.box((0, h * 0.3, 0.02), (r * 2.0, 0.22, r * 1.9), CLOTH, bevel=0.06)  # strained trousers
    head = (0, h * 0.88, 0.05)
    with m.rig("Head", (0, 1.42, 0.05)):
        m.sph(head, (0.25, 0.26, 0.26), "SkinDark")
        eyes(m, head, 0.26, 0.06)
    # Sac (weak point) and pustules.
    m.sph((0, h * 0.42, r * 0.95), (0.38, 0.38, 0.32), "G_FF8C1A", segments=12)
    for (x, y, z, d) in ((-0.32, 1.05, 0.22, 0.13), (0.3, 0.95, 0.26, 0.11), (0.35, 0.62, -0.2, 0.12), (-0.28, 0.7, -0.3, 0.1)):
        m.sph((x, y, z), (d, d, d), "G_FF8C1A", segments=8)
    for s, side in ((-1, "L"), (1, "R")):
        with m.rig("Arm" + side, (s * 0.42, 1.2, 0.0)):
            m.limb((s * 0.42, 1.2, 0.0), (s * 0.55, 0.85, 0.08), (0.12, 0.12), "Skin")
            m.limb((s * 0.55, 0.85, 0.08), (s * 0.52, 0.62, 0.2), (0.11, 0.11), "Skin")
    return m


def enemy_spitter():
    m = Model("enemy_spitter")
    h = 1.7
    for s, side in ((-1, "L"), (1, "R")):
        with m.rig("Leg" + side, (s * 0.13, 0.85, 0.0)):
            m.limb((s * 0.13, 0.85, 0.0), (s * 0.15, 0.46, 0.08), (0.13, 0.14), CLOTH_DARK)
            m.limb((s * 0.15, 0.48, 0.08), (s * 0.15, 0.07, -0.02), (0.11, 0.12), "SkinDark")
            m.box((s * 0.15, 0.04, 0.03), (0.13, 0.08, 0.24), RUBBER)
    m.box((0, 0.88, 0), (0.4, 0.18, 0.26), CLOTH_DARK, bevel=0.03)
    m.box((0, 1.15, 0.04), (0.46, 0.5, 0.28), "Skin", rot=(16, 0, 0), bevel=0.07)
    m.box((0, 1.05, 0.03), (0.48, 0.24, 0.3), CLOTH, rot=(16, 0, 0), bevel=0.03)
    head = (0, h * 0.84, 0.14)
    with m.rig("Head", (0, 1.32, 0.12)):
        m.sph(head, (0.25, 0.27, 0.3), "SkinDark")
        # Unhinged jaw.
        m.box((0, head[1] - 0.13, head[2] + 0.1), (0.16, 0.05, 0.16), "SkinDark", rot=(25, 0, 0), bevel=0.01)
        eyes(m, head, 0.3, 0.06)
    # Throat sac (weak point).
    m.sph((0, h * 0.7, 0.28), (0.34, 0.3, 0.3), "G_B3D933", segments=12)
    for s, side in ((-1, "L"), (1, "R")):
        with m.rig("Arm" + side, (s * 0.26, 1.36, 0.08)):
            m.limb((s * 0.26, 1.36, 0.08), (s * 0.3, 1.0, 0.1), (0.08, 0.08), "Skin")
            m.limb((s * 0.3, 1.0, 0.1), (s * 0.28, 0.75, 0.2), (0.07, 0.07), "Skin")
    return m


def enemy_alpha():
    m = Model("enemy_alpha")
    h, r = 2.15, 0.42
    for s, side in ((-1, "L"), (1, "R")):
        with m.rig("Leg" + side, (s * 0.16, 1.0, 0.0)):
            m.limb((s * 0.16, 1.0, 0.0), (s * 0.18, 0.55, 0.08), (0.17, 0.18), CLOTH_DARK)
            m.limb((s * 0.18, 0.57, 0.08), (s * 0.18, 0.08, -0.02), (0.15, 0.15), "SkinDark")
            m.box((s * 0.18, 0.05, 0.04), (0.16, 0.1, 0.3), RUBBER)
    m.box((0, 1.03, 0), (0.5, 0.2, 0.32), CLOTH_DARK, bevel=0.04)
    m.box((0, 1.38, 0.02), (0.62, 0.62, 0.38), "Skin", bevel=0.1)
    m.box((0, 1.72, 0.0), (0.92, 0.22, 0.42), "Skin", bevel=0.09)  # wide shoulders
    head = (0, h * 0.89, 0.06)
    with m.rig("Head", (0, 1.8, 0.06)):
        m.sph(head, (0.29, 0.31, 0.31), "SkinDark")
        eyes(m, head, 0.31, 0.07)
    # Crystal growth on the upper back (weak point).
    gy = h * 0.78
    m.box((0, gy, -r * 0.62), (r * 1.5, 0.24, 0.3), "G_59D9FF", bevel=0.04)
    for (x, tilt) in ((-0.22, -20), (-0.08, -8), (0.07, 6), (0.21, 18)):
        m.cone((x, gy + 0.2, -r * 0.7), (0.1, 0.38, 0.1), "G_59D9FF", rot=(-25, 0, tilt), segments=5)
    ay = h * 0.62
    for s, side in ((-1, "L"), (1, "R")):
        with m.rig("Arm" + side, (s * 0.48, 1.74, 0.0)):
            m.limb((s * 0.48, 1.74, 0.0), (s * 0.54, ay + 0.05, 0.2), (0.14, 0.14), "Skin")
            m.limb((s * 0.54, ay + 0.05, 0.2), (s * 0.52, ay - 0.05, 0.6), (0.12, 0.12), "Skin")
            for c in (-1, 0, 1):
                m.cone((s * 0.52 + c * 0.035, ay - 0.07, 0.7), (0.035, 0.18, 0.035), BONE, rot=(100, 0, 0), segments=5)
    return m


def enemy_specimen():
    """Body, head and arms only: the phase plates, growths and core stay game-built so they can appear by phase."""
    m = Model("enemy_specimen")
    h, r = 3.4, 1.0
    for s, side in ((-1, "L"), (1, "R")):
        with m.rig("Leg" + side, (s * 0.5, 1.5, 0.0)):
            m.limb((s * 0.5, 1.5, 0.0), (s * 0.58, 0.75, 0.15), (0.48, 0.5), "SkinDark")
            m.limb((s * 0.58, 0.78, 0.15), (s * 0.58, 0.12, 0.0), (0.42, 0.44), "SkinDark")
            m.box((s * 0.58, 0.08, 0.12), (0.46, 0.16, 0.66), "SkinDark", bevel=0.06)
    m.box((0, 1.55, 0), (1.3, 0.5, 1.0), "Skin", bevel=0.18)
    m.box((0, 2.2, 0.05), (1.8, 1.0, 1.35), "Skin", rot=(14, 0, 0), bevel=0.25)  # hunched chest
    m.box((0, 2.75, -0.1), (2.1, 0.45, 1.2), "Skin", rot=(14, 0, 0), bevel=0.2)  # shoulder mass
    for i, x in enumerate((-0.45, -0.15, 0.15, 0.45)):  # back spikes
        m.cone((x, 2.95, -0.55), (0.16, 0.6 if i in (1, 2) else 0.45, 0.16), BONE, rot=(-35, 0, x * 25), segments=6)
    head = (0, h * 0.9, 0.2)
    with m.rig("Head", (0, 2.85, 0.15)):
        m.sph(head, (0.5, 0.48, 0.55), "SkinDark")
        m.box((0, head[1] - 0.2, head[2] + 0.18), (0.36, 0.14, 0.3), "SkinDark", bevel=0.04)
        eyes(m, head, 0.55, 0.12)
    for s, side in ((-1, "L"), (1, "R")):
        with m.rig("Arm" + side, (s * 1.0, 2.75, -0.05)):
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
    with m.rig("Head", (0, h * 0.72, r * 0.8)):
        m.sph(head, (0.62, 0.52, 0.66), "SkinDark")
        for s in (-1, 1):  # mandibles
            m.cone((s * 0.17, head[1] - 0.22, head[2] + 0.3), (0.1, 0.38, 0.1), BONE, rot=(110, 0, s * 18), segments=6)
        eyes(m, head, 0.62, 0.11)
    for s, side in ((-1, "L"), (1, "R")):
        for l in range(3):
            z = r * (0.6 - l * 0.6)
            hip = (s * r * 0.7, h * 0.36, z)
            knee = (s * r * 1.35, h * 0.62, z + (0.25 if l == 0 else -0.15 if l == 2 else 0.0))
            foot = (s * r * 1.75, 0.04, z + (0.45 if l == 0 else -0.35 if l == 2 else 0.0))
            with m.rig(f"Leg{l}{side}", hip):  # six legs: Leg0L..Leg2R, front to back
                m.limb(hip, knee, (0.2, 0.22), "SkinDark")
                m.limb(knee, foot, (0.15, 0.17), "SkinDark")
                m.cone(foot, (0.12, 0.2, 0.12), BONE, rot=(180, 0, 0), segments=6)
    return m


# ----------------------------------------------------------------------------------------------- players

def player_body():
    """
    Feet origin. Legs, torso, vest and pack; the head, arms and rifle are player_head (they pitch with aim).
    Rig children for the walk/run/jump/crouch animation (RemotePlayerProxy): ThighL/R pivot at the hips, ShinL/R at
    the knees (the game parents each shin under its thigh) and Torso at the waist. The pelvis and belt stay on the root.
    """
    m = Model("player_body")
    for s, side in ((1, "R"), (-1, "L")):  # facing +Z, the character's right is +X
        with m.rig("Thigh" + side, (s * 0.12, 0.95, 0.0)):
            m.limb((s * 0.12, 0.95, 0.0), (s * 0.13, 0.5, 0.03), (0.16, 0.18), PANTS)
            m.box((s * 0.13, 0.52, 0.12), (0.15, 0.14, 0.06), ARMOR, bevel=0.02)  # knee pads
        with m.rig("Shin" + side, (s * 0.13, 0.52, 0.03)):
            m.limb((s * 0.13, 0.52, 0.03), (s * 0.13, 0.1, 0.0), (0.15, 0.16), PANTS)
            m.box((s * 0.13, 0.06, 0.04), (0.16, 0.12, 0.3), RUBBER, bevel=0.02)  # boots
    m.box((0, 0.98, 0), (0.42, 0.18, 0.26), PANTS, bevel=0.04)
    m.box((0, 1.02, 0.0), (0.46, 0.07, 0.29), DARK, bevel=0.01)  # belt
    for x in (-0.12, 0.0, 0.12):
        m.box((x, 1.04, 0.16), (0.09, 0.11, 0.05), OLIVE, bevel=0.01)  # pouches
    with m.rig("Torso", (0, 1.06, 0)):
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
    """Eye-height origin, pitches with the aim: helmet, visor, and the Arms child (pivot between the shoulders) with
    the forearms and gloves. The game puts the held weapon model under Arms at (0.25, -0.27, 0.18)
    (RemotePlayerProxy.WeaponAnchor) and moves Arms to shoulder the weapon when aiming or lower it when sprinting."""
    m = Model("player_head")
    m.sph((0, 0.02, -0.03), (0.3, 0.28, 0.32), "C_C9A88A")  # face/head under the helmet
    m.sph((0, 0.08, -0.04), (0.34, 0.24, 0.36), ARMOR, smooth=True)  # helmet dome
    m.box((0, 0.0, -0.04), (0.36, 0.06, 0.36), ARMOR, bevel=0.02)  # helmet rim
    m.box((0, 0.17, -0.04), (0.06, 0.05, 0.3), "Accent", bevel=0.0)  # stripe
    m.box((0, 0.0, 0.14), (0.26, 0.08, 0.06), GLASS, bevel=0.015)  # visor
    m.box((0.17, 0.06, 0.0), (0.04, 0.08, 0.08), DARK, bevel=0.01)  # headset
    # Forearms and gloves holding the weapon.
    with m.rig("Arms", (0.0, -0.28, 0.0)):
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
    """
    Supply Cache at real size (origin on the floor, front +Z, ~1.6 x 1.1 x 0.8): an open-topped supply crate with what
    it sells on show: olive ammo cans on the left, a white med kit with a red cross in the middle, a grenade tray right.
    """
    m = Model("supply_cache")
    crate = "C_385C42"
    trim = "C_BF9E33"
    m.box((0, 0.04, 0), (1.64, 0.08, 0.84), RUBBER, bevel=0.0)
    m.box((0, 0.34, 0), (1.6, 0.56, 0.8), crate, bevel=0.03)
    m.box((0, 0.63, 0), (1.66, 0.06, 0.86), trim, bevel=0.01)          # rim
    m.box((0, 0.6, 0), (1.5, 0.02, 0.7), DARK, bevel=0.0)              # inside floor seen from above
    for s_ in (-1, 1):
        m.box((s_ * 0.82, 0.42, 0), (0.06, 0.1, 0.3), METAL_LIGHT, bevel=0.01)  # handles
        m.box((s_ * 0.55, 0.34, 0.41), (0.1, 0.52, 0.03), trim, bevel=0.0)
        m.box((s_ * 0.55, 0.34, -0.41), (0.1, 0.52, 0.03), trim, bevel=0.0)
    m.box((0, 0.42, 0.41), (0.5, 0.14, 0.03), "G_66E680", bevel=0.0)  # status strip, both faces
    m.box((0, 0.42, -0.41), (0.5, 0.14, 0.03), "G_66E680", bevel=0.0)
    m.box((0, 0.22, 0.415), (0.9, 0.05, 0.02), HAZARD, bevel=0.0)
    # Ammo cans (left), two side by side and one on top.
    can = "C_4B5A36"
    for i, (x, y, z) in enumerate([(-0.56, 0.8, 0.13), (-0.56, 0.8, -0.15), (-0.56, 1.0, -0.01)]):
        m.box((x, y, z), (0.36, 0.2, 0.24), can, bevel=0.015)
        m.box((x, y + 0.105, z), (0.2, 0.02, 0.05), DARK, bevel=0.0)       # lid handle
        m.box((x, y, z + 0.125), (0.14, 0.05, 0.01), "C_D9C27A", bevel=0.0)  # stencil plate
    # Med kit (middle): white case, red cross on top and front.
    m.box((0.02, 0.8, 0.02), (0.42, 0.2, 0.34), "C_E6E6E2", bevel=0.02)
    m.box((0.02, 0.91, 0.02), (0.22, 0.012, 0.07), "G_E0403A", bevel=0.0)
    m.box((0.02, 0.91, 0.02), (0.07, 0.012, 0.22), "G_E0403A", bevel=0.0)
    m.box((0.02, 0.8, 0.195), (0.12, 0.04, 0.01), "C_E0403A", bevel=0.0)
    m.box((0.02, 0.8, 0.195), (0.04, 0.12, 0.01), "C_E0403A", bevel=0.0)
    m.box((0.02, 0.92, 0.02), (0.12, 0.03, 0.03), DARK, bevel=0.0)      # handle
    # Grenade tray (right): a low open box with six frag grenades.
    m.box((0.54, 0.7, 0), (0.44, 0.06, 0.5), "C_5E4A30", bevel=0.01)
    for s_ in (-1, 1):
        m.box((0.54 + s_ * 0.21, 0.76, 0), (0.02, 0.1, 0.5), "C_5E4A30", bevel=0.0)
        m.box((0.54, 0.76, s_ * 0.24), (0.44, 0.1, 0.02), "C_5E4A30", bevel=0.0)
    for ix in (-1, 0, 1):
        for iz in (-1, 1):
            x, z = 0.54 + ix * 0.13, iz * 0.11
            m.sph((x, 0.8, z), (0.1, 0.12, 0.1), "C_37442C", segments=8)
            m.box((x, 0.875, z), (0.03, 0.04, 0.03), METAL_LIGHT, bevel=0.0)  # fuse head
            m.box((x + 0.025, 0.86, z), (0.012, 0.06, 0.02), METAL_LIGHT, bevel=0.0)  # spoon
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
    """Assault rifle: stamped receiver, wood furniture, curved magazine, gas tube, bare barrel."""
    m = Model("weapon_halberd_ar", triangulate=True)
    # Receiver with a rounded dust cover (sights, optics and muzzle devices are attachments).
    m.sym([(0.0, -0.045), (0.30, -0.045), (0.31, -0.03), (0.31, 0.022), (0.29, 0.035), (0.02, 0.035), (0.0, 0.026)], 0.024, STEEL_D)
    m.sym([(0.01, 0.034), (0.27, 0.034), (0.275, 0.046), (0.25, 0.052), (0.03, 0.052), (0.01, 0.044)], 0.021, STEEL_M)  # dust cover
    for z in (0.05, 0.09, 0.13, 0.17, 0.21):
        m.box((0, 0.053, z), (0.034, 0.003, 0.006), STEEL_D, bevel=0.0)  # cover ribs
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
    # Bare barrel and cleaning rod.
    tube(m, 0.49, 0.65, 0.019, STEEL_D)
    tube(m, 0.49, 0.6, 0.006, STEEL_L, y=-0.004)
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
    # Barrel with a vent rib; magazine tube with cap.
    tube(m, 0.26, 0.8, 0.03, STEEL_D, y=0.02)
    m.box((0, 0.039, 0.53), (0.012, 0.004, 0.53), STEEL_M, bevel=0.0)
    for z in [0.3 + i * 0.06 for i in range(8)]:
        m.box((0, 0.035, z), (0.006, 0.008, 0.006), STEEL_M, bevel=0.0)
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
    """Compact SMG: angular polymer body, ribbed stick mag, folding wire stock, bare barrel."""
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
    # Barrel.
    tube(m, 0.29, 0.42, 0.022, STEEL_D, segments=10)
    tube(m, 0.418, 0.4205, 0.012, POLY)
    # Folding wire stock.
    for s in (-1, 1):
        m.sym([(0.0, 0.016), (-0.2, 0.004), (-0.2, -0.004), (0.0, 0.008)], 0.004, STEEL_M, chamfer=0.0, x=s * 0.016)
        m.sym([(0.0, -0.022), (-0.2, -0.05), (-0.2, -0.058), (0.0, -0.03)], 0.004, STEEL_M, chamfer=0.0, x=s * 0.016)
    m.sym([(-0.2, 0.01), (-0.212, 0.01), (-0.212, -0.075), (-0.2, -0.075)], 0.024, POLY, chamfer=0.003)
    return m


def weapon_anvil_lmg():
    """Light machine gun: big steel receiver, hinged feed cover, ammo box with belt, perforated shroud, bare barrel."""
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
    # Perforated barrel shroud and bare barrel.
    m.sym([(0.5, -0.03), (0.74, -0.03), (0.75, -0.015), (0.75, 0.055), (0.74, 0.07), (0.5, 0.07)], 0.034, STEEL_M, chamfer=0.004)
    for z in [0.53 + i * 0.04 for i in range(5)]:
        for y in (0.0, 0.04):
            for s in (-1, 1):
                m.cyl((s * 0.0345, y, z), (0.016, 0.003, 0.016), POLY, rot=(0, 0, 90), segments=6)
    tube(m, 0.74, 0.9, 0.034, STEEL_D)
    tube(m, 0.898, 0.9005, 0.014, POLY)  # bore
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
    """Revolving grenade launcher: fluted drum, ribbed barrel, polymer stock (no sights or foregrip)."""
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
    # Hazard marking on the barrel.
    m.box((0.036, 0.02, 0.45), (0.004, 0.03, 0.12), HAZARD, bevel=0.0)
    # Grip, guard, stock.
    m.sym([(0.075, -0.06), (0.03, -0.06), (0.004, -0.15), (0.01, -0.165), (0.042, -0.168), (0.054, -0.156), (0.078, -0.065)], 0.018, POLY, chamfer=0.003)
    m.sym([(0.07, -0.05), (0.07, -0.076), (0.082, -0.084), (0.13, -0.084), (0.138, -0.072), (0.138, -0.05),
           (0.13, -0.05), (0.13, -0.07), (0.125, -0.076), (0.086, -0.076), (0.078, -0.07), (0.078, -0.05)], 0.005, STEEL_M, chamfer=0.0)
    m.sym([(0.0, 0.05), (-0.06, 0.04), (-0.27, 0.02), (-0.28, 0.006), (-0.28, -0.1), (-0.265, -0.11),
           (-0.12, -0.075), (-0.03, -0.06), (0.0, -0.05)], 0.026, POLY_L, chamfer=0.005)
    m.sym([(-0.28, 0.01), (-0.296, 0.01), (-0.296, -0.104), (-0.28, -0.11)], 0.028, RUBBER, chamfer=0.003)
    return m


def weapon_vesper_dmr():
    """Marksman rifle: long tan polymer chassis, free-float handguard, top rail, box magazine, adjustable stock."""
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
    tube(m, 0.56, 0.65, 0.017, STEEL_D)
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
    return m


def weapon_bulldog_44():
    """Heavy revolver: fluted six-shot cylinder, full-lug barrel, wood grip, big hammer."""
    m = Model("weapon_bulldog_44", triangulate=True)
    m.sym([(-0.02, -0.02), (0.07, -0.02), (0.07, 0.042), (0.0, 0.05), (-0.02, 0.04)], 0.016, STEEL_D, chamfer=0.003)  # frame
    m.sym([(0.07, -0.008), (0.24, -0.008), (0.24, 0.042), (0.07, 0.042)], 0.0115, STEEL_D, chamfer=0.003)  # barrel lug
    m.box((0, 0.045, 0.155), (0.008, 0.004, 0.17), STEEL_M, bevel=0.0)  # top rib
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
    """Automatic shotgun: boxy receiver, 20-round drum, heat-shrouded barrel, polymer stock."""
    m = Model("weapon_hailstorm", triangulate=True)
    m.sym([(0.0, -0.05), (0.36, -0.05), (0.37, -0.035), (0.37, 0.05), (0.02, 0.05), (0.0, 0.04)], 0.03, POLY_L)
    m.box((0.031, 0.015, 0.2), (0.003, 0.03, 0.1), POLY, bevel=0.0)  # ejection port
    m.box((0.034, 0.028, 0.28), (0.008, 0.01, 0.03), STEEL_L, bevel=0.002)
    for s in (-1, 1):
        rivet(m, s * 0.03, -0.035, 0.03)
        rivet(m, s * 0.03, -0.035, 0.34)
        rivet(m, s * 0.03, 0.035, 0.34)
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
    m.sym([(0.08, -0.03), (0.088, -0.03), (0.084, -0.046), (0.076, -0.05)], 0.003, STEEL_L, chamfer=0.0)  # trigger
    for s in (-1, 1):
        m.box((s * 0.025, 0.0, 0.24), (0.003, 0.03, 0.08), POLY, bevel=0.0)  # side grip panels
        rivet(m, s * 0.024, 0.02, -0.12)
    m.sym([(0.2, -0.05), (0.24, -0.05), (0.232, -0.11), (0.21, -0.112)], 0.012, POLY, chamfer=0.003)  # finger stop
    tube(m, 0.36, 0.42, 0.018, STEEL_D)
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



# ----------------------------------------------------------------------------------------------- first-person arms
# One right arm; the left is the same model mirrored in game. Origin = the centre of the closed fist (where a grip
# passes through, along Unity Y); fingers wrap in front (+Z), the back of the hand faces +X, and the forearm runs back
# along -Z. WeaponViewmodel aims +Z from a shoulder point at the weapon's grip, so the arm always reaches the gun.

GLOVE = "C_6F6148"
GLOVE_L = "C_574B37"
SLEEVE = "C_49503B"
SLEEVE_D = "C_363C2C"
CUFF = "C_2A2B2F"


def fp_arm():
    m = Model("fp_arm")
    # Back of the hand and palm wrapping the grip.
    m.box((0.026, 0.0, -0.012), (0.028, 0.088, 0.075), GLOVE, rot=(0, -8, 0), bevel=0.008)
    m.box((0.036, 0.006, -0.01), (0.008, 0.07, 0.05), GLOVE_L, rot=(0, -8, 0), bevel=0.003)  # knuckle guard
    # Four curled fingers wrapping round the front of the grip, each a knuckle and a tip.
    for i, y in enumerate((0.03, 0.01, -0.01, -0.03)):
        w = 0.021 if i < 3 else 0.018
        m.box((0.012, y, 0.026), (0.026, w, 0.026), GLOVE, bevel=0.006)
        m.box((-0.014, y - 0.002, 0.02), (0.022, w * 0.95, 0.024), GLOVE, rot=(0, 25, 0), bevel=0.006)
    # Thumb over the top of the grip on the far side.
    m.limb((0.012, 0.03, -0.03), (-0.02, 0.04, 0.012), (0.022, 0.02), GLOVE)
    m.box((-0.026, 0.042, 0.018), (0.02, 0.019, 0.024), GLOVE, bevel=0.006)
    # Wrist and glove cuff.
    tube(m, -0.085, -0.04, 0.056, GLOVE, y=0.0, x=0.022, segments=10)
    tube(m, -0.13, -0.08, 0.064, CUFF, y=0.0, x=0.022, segments=10)
    # Forearm: sleeve widening towards the elbow, with a rolled band and a seam.
    m.cone((0.022, 0.0, -0.4), (0.088, 0.56, 0.088), SLEEVE, rot=(-90, 0, 0), segments=12)
    tube(m, -0.62, -0.12, 0.078, SLEEVE, y=0.0, x=0.022, segments=12)
    tube(m, -0.2, -0.14, 0.086, SLEEVE_D, y=0.0, x=0.022, segments=12)
    m.box((0.022, 0.041, -0.4), (0.008, 0.004, 0.42), SLEEVE_D, bevel=0.0)
    return m


# ----------------------------------------------------------------------------------------------- rigged zombies
# The zm_* models are one skinned mesh on a shared armature (Hips > Spine > Chest > Neck > Head, arms off the Chest,
# legs off the Hips) with real animation clips: Idle, Walk, Run, Attack, Hit, Spit, Convulse and Death. The body is an
# organic Skin-modifier hull over the bone graph (one subdivision), with primitives rigidly bound for the head, hands,
# shoes and each type's growths. ZombieAnimator.cs plays the clips; the older enemy_* models stay as the fallback.
# Crawler and Broodmother keep their limb-swing models (EnemyRig).

ZM_BONES = [  # name, parent, head joint, tail joint
    ("Hips", None, "hips", "spine"), ("Spine", "Hips", "spine", "chest"), ("Chest", "Spine", "chest", "uchest"),
    ("Neck", "Chest", "uchest", "head"), ("Head", "Neck", "head", "head_top"),
    ("UpperArm.L", "Chest", "shoulderL", "elbowL"), ("LowerArm.L", "UpperArm.L", "elbowL", "wristL"),
    ("Hand.L", "LowerArm.L", "wristL", "handL"),
    ("UpperArm.R", "Chest", "shoulderR", "elbowR"), ("LowerArm.R", "UpperArm.R", "elbowR", "wristR"),
    ("Hand.R", "LowerArm.R", "wristR", "handR"),
    ("UpperLeg.L", "Hips", "hipL", "kneeL"), ("LowerLeg.L", "UpperLeg.L", "kneeL", "ankleL"),
    ("Foot.L", "LowerLeg.L", "ankleL", "toeL"),
    ("UpperLeg.R", "Hips", "hipR", "kneeR"), ("LowerLeg.R", "UpperLeg.R", "kneeR", "ankleR"),
    ("Foot.R", "LowerLeg.R", "ankleR", "toeR"),
]
ZM_PARENT = {b: p for b, p, _, _ in ZM_BONES}
ZM_CHILDREN = {b: [c for c, p, _, _ in ZM_BONES if p == b] for b, _, _, _ in ZM_BONES}
# The hull: skin-modifier vertices (joint names) and the edges between them. "head" is the top of the neck.
ZM_HULL_EDGES = [("hips", "spine"), ("spine", "chest"), ("chest", "uchest"), ("uchest", "head"),
                 ("uchest", "shoulderL"), ("shoulderL", "elbowL"), ("elbowL", "wristL"),
                 ("uchest", "shoulderR"), ("shoulderR", "elbowR"), ("elbowR", "wristR"),
                 ("hips", "hipL"), ("hipL", "kneeL"), ("kneeL", "ankleL"),
                 ("hips", "hipR"), ("hipR", "kneeR"), ("kneeR", "ankleR")]
# Hull radii (side, depth) in metres at 1.8 m, before the type's girth / limb factors.
ZM_RADII = {"hips": (0.17, 0.12), "spine": (0.16, 0.11), "chest": (0.2, 0.125), "uchest": (0.19, 0.11),
            "head": (0.055, 0.055), "shoulder": (0.066, 0.066), "elbow": (0.048, 0.048), "wrist": (0.036, 0.036),
            "hip": (0.092, 0.092), "knee": (0.066, 0.066), "ankle": (0.05, 0.05)}
TORSO_JOINTS = ("hips", "spine", "chest", "uchest", "head")


class Zombie:
    """Skeleton, hull and rigid extras for one zm_* model. Every coordinate is Unity space (like Model)."""

    def __init__(self, name, h, w=1.0, hunch=0.0, arm_drop=0.0, stance=1.0, girth=1.0, limb=1.0, depth=1.0,
                 shirt=True, short_pants=False, sleeves=True):
        self.name, self.h, self.k = name, h, h / 1.8
        self.girth, self.limb, self.depth = girth, limb, depth
        self.shirt, self.short_pants, self.sleeves = shirt, short_pants, sleeves
        self.radius_override = {}
        self.extras = Model(name + "_extras")  # primitive factory; parts are bound to self.bone
        self.bound = {}
        self.bone = None
        k = self.k

        def P(x, y, z):
            return (x * w * k, y * k, z * k)

        J = {}
        J["hips"] = P(0, 0.95, 0)
        J["spine"] = P(0, 1.12, 0.02 + hunch * 0.3)
        J["chest"] = P(0, 1.29, 0.04 + hunch * 0.6)
        J["uchest"] = P(0, 1.42, 0.06 + hunch * 0.85)
        J["head"] = P(0, 1.54, 0.09 + hunch)
        J["head_top"] = P(0, 1.8, 0.09 + hunch)
        for side, s in (("L", -1), ("R", 1)):
            J["shoulder" + side] = P(s * 0.2, 1.42, 0.06 + hunch * 0.85)
            J["elbow" + side] = P(s * 0.26, 1.2 - arm_drop, 0.18 + hunch)
            J["wrist" + side] = P(s * 0.28, 1.15 - arm_drop * 1.5, 0.42 + hunch)
            J["hand" + side] = P(s * 0.28, 1.13 - arm_drop * 1.6, 0.56 + hunch)
            J["hip" + side] = (s * 0.11 * stance * w * k, 0.9 * k, 0.0)
            J["knee" + side] = (s * 0.12 * stance * w * k, 0.5 * k, 0.06 * k)
            J["ankle" + side] = (s * 0.13 * stance * w * k, 0.09 * k, -0.01 * k)
            J["toe" + side] = (s * 0.13 * stance * w * k, 0.02 * k, 0.15 * k)
        self.J = J

    def on(self, bone):
        z = self

        class _On:
            def __enter__(self):
                z.bone = bone

            def __exit__(self, *exc):
                z.bone = None
        return _On()

    def add(self, fn, *args, **kw):
        """z.add(z.extras.box, pos, size, mat, ...) inside `with z.on(bone)`: a primitive bound to that bone."""
        o = fn(*args, **kw)
        self.bound[o.name] = self.bone
        return o

    def radius(self, joint):
        if joint in self.radius_override:
            r = self.radius_override[joint]
        else:
            base = joint.rstrip("LR") if joint not in ZM_RADII else joint
            r = ZM_RADII[base]
        f = (self.girth if joint in TORSO_JOINTS else self.limb) * self.k
        return (r[0] * f, r[1] * f * (self.depth if joint in TORSO_JOINTS else 1.0))

    # ----------------------------------------------------------------------- standard parts

    def head(self, size=(0.24, 0.28, 0.26), jaw=(0.15, 0.06, 0.12), jaw_drop=0.0, eye_spread=0.065, eye=EYE):
        k = self.k
        a, b = Vector(self.J["head"]), Vector(self.J["head_top"])
        c = a + (b - a) * 0.45 + Vector((0, 0, 0.01 * k))
        sx, sy, sz = (v * k for v in size)
        with self.on("Head"):
            self.add(self.extras.sph, tuple(c), (sx, sy, sz), "SkinDark", segments=12)
            self.add(self.extras.box, (c.x, c.y - sy * 0.36 - jaw_drop * k, c.z + sz * 0.25),
                     (jaw[0] * k, jaw[1] * k, jaw[2] * k), "SkinDark", rot=(12 + jaw_drop * 200, 0, 0), bevel=0.012 * k)
            for s in (-1, 1):
                self.add(self.extras.box, (c.x + s * eye_spread * k, c.y + sy * 0.06, c.z + sz * 0.43),
                         (0.05 * k, 0.028 * k, 0.03 * k), eye, bevel=0.0)
        return c

    def hands(self, claws=False, fist=False, mat="SkinDark"):
        k = self.k
        for side in ("L", "R"):
            w, t = Vector(self.J["wrist" + side]), Vector(self.J["hand" + side])
            d = (t - w).normalized()
            mid = w + (t - w) * 0.5
            with self.on("Hand." + side):
                if fist:
                    self.add(self.extras.box, tuple(mid), (0.13 * k * self.limb, 0.12 * k * self.limb, 0.13 * k * self.limb),
                             mat, bevel=0.03 * k)
                else:
                    self.add(self.extras.limb, tuple(w + d * 0.01), tuple(mid + d * 0.04),
                             (0.085 * k * self.limb, 0.04 * k * self.limb), mat)
                    for f in (-1, 0, 1):  # fingers, curled down
                        base = mid + d * 0.03 + Vector((f * 0.024 * k * self.limb, 0, 0))
                        self.add(self.extras.limb, tuple(base), tuple(base + d * 0.06 * k + Vector((0, -0.03 * k, 0))),
                                 (0.018 * k, 0.018 * k), mat)
                if claws:
                    for f in (-1, 0, 1):
                        base = mid + d * 0.05 + Vector((f * 0.03 * k * self.limb, -0.02 * k, 0))
                        self.add(self.extras.cone, tuple(base + d * 0.05 * k), (0.026 * k, 0.15 * k, 0.026 * k), BONE,
                                 rot=(100, math.degrees(math.atan2(d.x, d.z)), 0), segments=5)

    def shoes(self, mat=RUBBER, bare=False):
        k = self.k
        for side in ("L", "R"):
            a, t = Vector(self.J["ankle" + side]), Vector(self.J["toe" + side])
            with self.on("Foot." + side):
                if bare:
                    self.add(self.extras.box, (a.x, 0.035 * k, (a.z + t.z) / 2), (0.1 * k * self.limb, 0.06 * k, 0.22 * k),
                             "SkinDark", bevel=0.015 * k)
                else:
                    self.add(self.extras.box, (a.x, 0.04 * k, (a.z + t.z) / 2 + 0.01 * k),
                             (0.13 * k * self.limb, 0.08 * k, 0.26 * k), mat, bevel=0.02 * k)

    def rags(self):
        """A belt, bound to the hips."""
        k = self.k
        hp = Vector(self.J["hips"])
        with self.on("Hips"):
            self.add(self.extras.box, (0, hp.y + 0.01 * k, hp.z), (0.35 * k * self.girth, 0.05 * k, 0.24 * k * self.girth * self.depth),
                     "C_2B2622", bevel=0.01 * k)

    # ----------------------------------------------------------------------- build

    def hull(self):
        """The organic body: a skin-modifier hull over the joints, one subdivision, materials by region."""
        names = sorted({n for e in ZM_HULL_EDGES for n in e})
        index = {n: i for i, n in enumerate(names)}
        me = bpy.data.meshes.new(self.name + "_hull")
        me.from_pydata([to_b(self.J[n]) for n in names], [(index[a], index[b]) for a, b in ZM_HULL_EDGES], [])
        o = bpy.data.objects.new(self.name + "_hull", me)
        bpy.context.scene.collection.objects.link(o)
        bpy.ops.object.select_all(action='DESELECT')
        o.select_set(True)
        bpy.context.view_layer.objects.active = o
        skin = o.modifiers.new("skin", 'SKIN')
        skin.branch_smoothing = 0.6
        skin.use_smooth_shade = True
        for n, i in index.items():
            rx, rz = self.radius(n)
            me.skin_vertices[0].data[i].radius = (rx, rz)
            me.skin_vertices[0].data[i].use_root = (n == "hips")
        sub = o.modifiers.new("sub", 'SUBSURF')
        sub.levels = 1
        sub.render_levels = 1
        bpy.ops.object.modifier_apply(modifier=skin.name)
        bpy.ops.object.modifier_apply(modifier=sub.name)
        for poly in me.polygons:
            poly.use_smooth = True
        # Materials by the nearest bone.
        slots = {}

        def slot(mat):
            if mat not in slots:
                slots[mat] = len(me.materials)
                me.materials.append(material(mat))
            return slots[mat]

        segs = self.segments()
        for poly in me.polygons:
            c = poly.center
            bone, t = nearest_bone(segs, c)
            u = to_u(c)
            mat = "Skin"
            if bone.startswith(("UpperLeg", "LowerLeg", "Foot")) or bone == "Hips":
                mat = CLOTH_DARK
                if self.short_pants and (bone.startswith(("LowerLeg", "Foot")) or (bone.startswith("UpperLeg") and t > 0.85)):
                    mat = "Skin"
                if bone == "Hips" and t > 0.75 and self.shirt:
                    mat = CLOTH
            elif bone in ("Spine", "Chest"):
                tear = math.sin(u[0] * 41.0 + 1.3) * math.sin(u[1] * 37.0 + u[2] * 23.0)
                mat = CLOTH if self.shirt and tear < 0.45 else "Skin"
                if not self.shirt and tear > 0.7:
                    mat = "SkinDark"
            elif bone.startswith("UpperArm") and self.sleeves and self.shirt and t < 0.35:
                mat = CLOTH
            elif bone.startswith(("UpperArm", "LowerArm")):
                bruise = math.sin(u[0] * 53.0) * math.sin(u[1] * 47.0 + u[2] * 31.0)
                mat = "SkinDark" if bruise > 0.75 else "Skin"
            poly.material_index = slot(mat)
        return o

    def segments(self):
        return [(b, to_b(self.J[a]), to_b(self.J[t])) for b, _, a, t in ZM_BONES]

    def build(self):
        """Hull + extras joined, weighted and parented to a new armature. Returns the armature object."""
        segs = self.segments()
        body = self.hull()
        weight_by_distance(body, segs)
        parts = [body]
        for o in self.extras.parts:
            vg = o.vertex_groups.new(name=self.bound[o.name])
            vg.add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')
            parts.append(o)
        mesh = Model._join(parts, self.name + "_body")
        mesh.name = "Body"
        mesh.data.name = self.name + "_body"

        ad = bpy.data.armatures.new(self.name + "_rig")
        arm = bpy.data.objects.new(self.name, ad)
        bpy.context.scene.collection.objects.link(arm)
        bpy.ops.object.select_all(action='DESELECT')
        arm.select_set(True)
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode='EDIT')
        for b, parent, a, t in ZM_BONES:
            eb = ad.edit_bones.new(b)
            eb.head = to_b(self.J[a])
            eb.tail = to_b(self.J[t])
            eb.roll = 0.0
            if parent:
                eb.parent = ad.edit_bones[parent]
                eb.use_connect = False
        bpy.ops.object.mode_set(mode='OBJECT')
        mesh.parent = arm
        mod = mesh.modifiers.new("rig", 'ARMATURE')
        mod.object = arm
        arm["zm_k"] = self.k
        return arm


def to_u(p):
    """Blender point -> Unity point (inverse of to_b)."""
    return Vector((-p[0], p[2], -p[1]))


def nearest_bone(segs, p):
    best, best_d, best_t = None, 1e9, 0.0
    for b, a, t in segs:
        ab = t - a
        u = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
        d = (a + ab * u - p).length
        if d < best_d:
            best, best_d, best_t = b, d, u
    return best, best_t


def weight_by_distance(o, segs):
    """Each vertex goes to its nearest bone, blended with that bone's nearest parent or child near the joints."""
    groups = {b: o.vertex_groups.new(name=b) for b, _, _ in segs}
    seg = {b: (a, t) for b, a, t in segs}

    def dist(b, p):
        a, t = seg[b]
        ab = t - a
        u = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
        return (a + ab * u - p).length

    for v in o.data.vertices:
        p = v.co
        b0, _ = nearest_bone(segs, p)
        d0 = max(dist(b0, p), 1e-4)
        neigh = ([ZM_PARENT[b0]] if ZM_PARENT[b0] else []) + ZM_CHILDREN[b0]
        if b0 in ("UpperArm.L", "UpperArm.R"):
            neigh = [n for n in neigh]  # Chest, LowerArm
        best = None
        for n in neigh:
            d = dist(n, p)
            if best is None or d < best[1]:
                best = (n, d)
        if best is None or best[1] > d0 * 2.2:
            groups[b0].add([v.index], 1.0, 'REPLACE')
            continue
        w0 = 1.0 / d0 ** 4
        w1 = 1.0 / max(best[1], 1e-4) ** 4
        # Fade the neighbour out as it gets twice as far as the nearest bone.
        fade = max(0.0, min(1.0, (2.2 - best[1] / d0) / 1.2))
        w1 *= fade
        s = w0 + w1
        groups[b0].add([v.index], w0 / s, 'REPLACE')
        if w1 > 0:
            groups[best[0]].add([v.index], w1 / s, 'REPLACE')


# ----------------------------------------------------------------------------------------------- zombie types
# Built at the prototype definition's height / radius for the body (the same as the enemy_* models), with the
# glowing weak points where the hurtboxes are (Enemy.BuildVisual).

def zm_standard():
    z = Zombie("zm_standard", 1.8)
    z.head()
    z.hands()
    z.shoes()
    z.rags()
    return z


def zm_lean():
    z = Zombie("zm_lean", 1.65, w=0.85, hunch=0.12, arm_drop=0.06, girth=0.78, limb=0.78, short_pants=True,
               sleeves=False)
    z.head(size=(0.23, 0.26, 0.26), eye_spread=0.06)
    z.hands(claws=True)
    z.shoes(bare=True)
    z.rags()
    k = z.k
    with z.on("Chest"):  # visible spine ridge
        for i in range(4):
            p = Vector(z.J["spine"]).lerp(Vector(z.J["uchest"]), i / 3.0)
            z.add(z.extras.box, (0, p.y, p.z - 0.085 * k), (0.05 * k, 0.045 * k, 0.05 * k), BONE, bevel=0.0)
    return z


def zm_plated():
    h, r = 2.3, 0.6
    z = Zombie("zm_plated", h, w=2.3, hunch=0.02, girth=2.2, limb=2.0, depth=1.25, shirt=False, stance=0.95)
    z.head(size=(0.24, 0.24, 0.26), jaw=(0.13, 0.05, 0.1), eye_spread=0.055)
    z.hands(fist=True)
    z.shoes()
    z.rags()
    with z.on("Spine"):  # front plate (armour hurtbox): riot-shield slabs grown into the chest
        z.add(z.extras.box, (0, h * 0.5, r * 0.95), (r * 2.2, h * 0.53, 0.1), METAL, bevel=0.03)
        for y in (0.85, 1.15, 1.45):
            z.add(z.extras.box, (0, y, r * 0.95 + 0.06), (r * 2.25, 0.05, 0.04), METAL_LIGHT, bevel=0.0)
        for sx in (-1, 1):
            for y in (0.8, 1.5):
                z.add(z.extras.cyl, (sx * 0.55, y, r * 0.95 + 0.06), (0.06, 0.03, 0.06), METAL_LIGHT, rot=(90, 0, 0), segments=6)
    gy = h * 0.6
    with z.on("Chest"):  # back growth (weak point)
        z.add(z.extras.box, (0, gy, -r * 0.92), (r * 1.0, h * 0.22, 0.2), "G_F28C26", bevel=0.05)
        for (x, y, d) in ((-0.18, gy + 0.12, 0.2), (0.16, gy - 0.1, 0.22), (0.0, gy + 0.02, 0.26)):
            z.add(z.extras.sph, (x, y, -r * 1.02), (d, d, d), "G_F28C26", segments=8)
    return z


def zm_bloated():
    h, r = 1.75, 0.42
    z = Zombie("zm_bloated", h, w=1.25, girth=1.5, limb=1.25, depth=1.3, shirt=False, stance=1.25)
    z.radius_override.update({"spine": (0.3, 0.27), "chest": (0.26, 0.2), "hips": (0.24, 0.2)})
    z.head(size=(0.24, 0.25, 0.25), eye_spread=0.06)
    z.hands()
    z.shoes()
    with z.on("Spine"):  # the gut, the sac (weak point) and pustules
        z.add(z.extras.sph, (0, h * 0.5, 0.04), (r * 2.1, h * 0.36, r * 1.9), "Skin", segments=14)
        z.add(z.extras.sph, (0, h * 0.42, r * 0.95), (0.38, 0.38, 0.32), "G_FF8C1A", segments=12)
        for (x, y, zz, d) in ((-0.32, 1.05, 0.22, 0.13), (0.3, 0.95, 0.26, 0.11), (0.35, 0.72, -0.2, 0.12), (-0.28, 0.76, -0.3, 0.1)):
            z.add(z.extras.sph, (x, y, zz), (d, d, d), "G_FF8C1A", segments=8)
    with z.on("Hips"):
        z.add(z.extras.sph, (0, h * 0.33, 0.02), (r * 1.75, 0.3, r * 1.6), CLOTH, segments=12)  # strained trousers
    return z


def zm_spitter():
    h = 1.7
    z = Zombie("zm_spitter", h, hunch=0.07, arm_drop=0.12)
    z.head(size=(0.24, 0.26, 0.29), jaw=(0.15, 0.05, 0.15), jaw_drop=0.08, eye_spread=0.06)
    z.hands()
    z.shoes()
    z.rags()
    with z.on("Neck"):  # throat sac (weak point)
        z.add(z.extras.sph, (0, h * 0.7, 0.26), (0.32, 0.28, 0.28), "G_B3D933", segments=12)
    return z


def zm_alpha():
    h, r = 2.15, 0.42
    z = Zombie("zm_alpha", h, w=1.45, hunch=0.03, girth=1.45, limb=1.4, depth=1.2, shirt=False)
    z.head(size=(0.25, 0.27, 0.27), eye_spread=0.06)
    z.hands(claws=True)
    z.shoes()
    z.rags()
    gy = h * 0.78
    with z.on("Chest"):  # crystal growth on the upper back (weak point)
        z.add(z.extras.box, (0, gy, -r * 0.62), (r * 1.5, 0.24, 0.3), "G_59D9FF", bevel=0.04)
        for (x, tilt) in ((-0.22, -20), (-0.08, -8), (0.07, 6), (0.21, 18)):
            z.add(z.extras.cone, (x, gy + 0.2, -r * 0.7), (0.1, 0.38, 0.1), "G_59D9FF", rot=(-25, 0, tilt), segments=5)
    return z


def zm_specimen():
    """Body, head and arms only: the phase plates, growths and core stay game-built so they can appear by phase."""
    h = 3.4
    z = Zombie("zm_specimen", h, w=2.6, hunch=0.12, arm_drop=0.12, girth=2.6, limb=2.3, depth=1.3, shirt=False,
               stance=1.1)
    z.radius_override.update({"uchest": (0.14, 0.1), "chest": (0.17, 0.12), "shoulderL": (0.075, 0.075),
                              "shoulderR": (0.075, 0.075)})
    z.head(size=(0.27, 0.26, 0.3), jaw=(0.2, 0.08, 0.17), eye_spread=0.07)
    z.hands(claws=True, mat="SkinDark")
    z.shoes(bare=True)
    k = z.k
    with z.on("Chest"):  # back spikes
        u = Vector(z.J["uchest"])
        for i, x in enumerate((-0.45, -0.15, 0.15, 0.45)):
            z.add(z.extras.cone, (x, u.y + 0.05, u.z - 0.4), (0.16, 0.6 if i in (1, 2) else 0.45, 0.16), BONE,
                  rot=(-35, 0, x * 25), segments=6)
    return z


ZOMBIES = [zm_standard, zm_lean, zm_plated, zm_bloated, zm_spitter, zm_alpha, zm_specimen]


# ----------------------------------------------------------------------------------------------- zombie clips
# A clip is a function of t (0..1) returning {bone: rotation} plus a Hips offset, all in Unity-flavoured body terms.
# Rotations are about the armature's axes at rest (basis = B^-1 R B), so "flex" means the same thing on every bone:
# positive swings a hanging limb forward / raises a forward-pointing arm, negative bends the spine forward.

def _rx(d):
    return Matrix.Rotation(math.radians(-d), 3, 'X')


def flex(d):
    return _rx(d)


def lean(d):
    """Spine / head bending forward by d degrees."""
    return _rx(-d)


def tilt(d):
    """Lean towards the zombie's right (positive) or left."""
    return Matrix.Rotation(math.radians(-d), 3, 'Y')


def turn(d):
    """Turn towards the zombie's right (positive)."""
    return Matrix.Rotation(math.radians(-d), 3, 'Z')


def spread(side, d):
    """Forward-pointing arm swinging outward."""
    return turn(d if side == "R" else -d)


def legout(side, d):
    return tilt(-d if side == "L" else d)


def smooth(a, b, t):
    t = max(0.0, min(1.0, (t - a) / (b - a))) if b > a else 1.0
    return t * t * (3 - 2 * t)


def keys(t, pts):
    """Smoothstepped value through (time, value) keys."""
    if t <= pts[0][0]:
        return pts[0][1]
    for (t0, v0), (t1, v1) in zip(pts, pts[1:]):
        if t <= t1:
            return v0 + (v1 - v0) * smooth(t0, t1, t)
    return pts[-1][1]


SIDES = (("L", 0.0), ("R", math.pi))


def base_pose(P):
    """The shamble stance every clip starts from: hunched, arms reaching, knees soft."""
    P["Spine"].append(lean(8))
    P["Neck"].append(lean(-6))
    P["Head"].append(lean(10))
    for side, _ in SIDES:
        P["UpperArm." + side].append(flex(-6))
        P["LowerArm." + side].append(flex(-4))
        P["Hand." + side].append(flex(-15))


def new_pose():
    return {b: [] for b, _, _, _ in ZM_BONES}


def clip_idle(t):
    P, hips = new_pose(), Vector((0, -0.02, 0))
    base_pose(P)
    ph = 2 * math.pi * t
    P["Spine"].append(lean(2 * math.sin(ph)))
    P["Chest"].append(tilt(2.5 * math.sin(ph)))
    P["Head"].append(tilt(7 * math.sin(ph + 0.6)))
    P["Head"].append(lean(4 * math.sin(2 * ph)))
    for side, off in SIDES:
        P["UpperArm." + side].append(flex(-18 + 4 * math.sin(ph + off)))
        P["LowerArm." + side].append(flex(-12 + 3 * math.sin(ph + off + 1)))
        P["UpperLeg." + side].append(flex(8))
        P["LowerLeg." + side].append(flex(-14))
        P["Foot." + side].append(flex(6))
    hips.y -= 0.01 * (1 + math.sin(ph))
    return P, hips


def clip_walk(t):
    """The shamble: a dragging right leg, lurching torso, arms reaching and bobbing."""
    P, hips = new_pose(), Vector((0, 0, 0))
    base_pose(P)
    ph = 2 * math.pi * t
    s, c = math.sin(ph), math.cos(ph)
    for side, off in SIDES:
        amp = 26 if side == "L" else 18
        sw = math.sin(ph + off)
        lift = max(0.0, math.cos(ph + off))
        P["UpperLeg." + side].append(flex(amp * sw + 4 + 10 * lift))
        P["LowerLeg." + side].append(flex(-(6 + (38 if side == "L" else 22) * lift)))
        P["Foot." + side].append(flex(-6 * sw + 8 * lift))
        if side == "R":
            P["UpperLeg.R"].append(legout("R", 6))
            P["Foot.R"].append(turn(14))
        reach = 24 if side == "L" else -6  # the left arm reaches, the right one hangs lower
        P["UpperArm." + side].append(flex(reach + 7 * math.sin(ph + off + math.pi)))
        P["UpperArm." + side].append(spread(side, 4))
        P["LowerArm." + side].append(flex(4 + 6 * math.sin(ph + off + 2)))
    hips.y -= 0.035 * abs(s)
    P["Hips"].append(tilt(4 * s))
    P["Hips"].append(turn(5 * s))
    P["Spine"].append(lean(4))
    P["Spine"].append(tilt(-4 * s))
    P["Chest"].append(turn(-6 * s))
    P["Head"].append(tilt(9 * math.sin(ph + 0.8)))
    P["Head"].append(lean(4 * abs(c)))
    return P, hips


def clip_run(t):
    P, hips = new_pose(), Vector((0, 0, 0))
    base_pose(P)
    ph = 2 * math.pi * t
    s = math.sin(ph)
    for side, off in SIDES:
        sw = math.sin(ph + off)
        lift = max(0.0, math.cos(ph + off))
        P["UpperLeg." + side].append(flex(44 * sw + 12 + 14 * lift))
        P["LowerLeg." + side].append(flex(-(18 + 75 * lift)))
        P["Foot." + side].append(flex(-10 * sw + 10 * lift))
        P["UpperArm." + side].append(flex(-14 + 36 * math.sin(ph + off + math.pi)))
        P["UpperArm." + side].append(spread(side, 10))
        P["LowerArm." + side].append(flex(-18 + 14 * math.sin(ph + off + 2.2)))
    hips.y -= 0.02 + 0.05 * abs(s)
    hips.z += 0.04
    P["Hips"].append(turn(9 * s))
    P["Spine"].append(lean(18))
    P["Chest"].append(turn(-12 * s))
    P["Neck"].append(lean(-14))
    P["Head"].append(tilt(6 * math.sin(ph * 2)))
    return P, hips


def clip_attack(t):
    """Rear back with both arms high (0..0.42), slam down (0.42..0.56), recover. Brain wind-up ends at 0.42."""
    P, hips = new_pose(), Vector((0, 0, 0))
    base_pose(P)
    up = keys(t, [(0, 0), (0.42, 1), (0.5, -0.2), (0.56, -1), (0.75, -0.8), (1.0, 0)])
    lift = max(up, 0.0)
    slam = max(-up, 0.0)
    P["Spine"].append(lean(-14 * lift + 26 * slam))
    P["Chest"].append(lean(-8 * lift + 12 * slam))
    P["Head"].append(lean(-14 * lift + 10 * slam))
    for side, _ in SIDES:
        P["UpperArm." + side].append(flex(70 * lift - 50 * slam))
        P["UpperArm." + side].append(spread(side, 18 * lift))
        P["LowerArm." + side].append(flex(25 * lift - 15 * slam))
        P["Hand." + side].append(flex(20 * lift - 20 * slam))
        P["UpperLeg." + side].append(flex(10 * slam + (12 if side == "L" else -4) * (lift + slam)))
        P["LowerLeg." + side].append(flex(-22 * slam - 6 * lift))
    hips.y -= 0.06 * slam
    hips.z += 0.05 * slam - 0.03 * lift
    return P, hips


def clip_hit(t):
    P, hips = new_pose(), Vector((0, 0, 0))
    base_pose(P)
    a = keys(t, [(0, 0), (0.18, 1), (1.0, 0)])
    P["Spine"].append(lean(-18 * a))
    P["Chest"].append(lean(-10 * a))
    P["Chest"].append(tilt(8 * a))
    P["Head"].append(lean(-22 * a))
    P["Head"].append(tilt(-12 * a))
    for side, _ in SIDES:
        P["UpperArm." + side].append(flex(25 * a))
        P["UpperArm." + side].append(spread(side, 22 * a))
        P["LowerArm." + side].append(flex(-20 * a))
        P["UpperLeg." + side].append(flex(-6 * a if side == "L" else 10 * a))
        P["LowerLeg." + side].append(flex(-14 * a))
    hips.z -= 0.08 * a
    hips.y -= 0.03 * a
    return P, hips


def clip_spit(t):
    """Head and chest rear back with the throat swelling (0..0.6), then lurch forward (0.6..0.72)."""
    P, hips = new_pose(), Vector((0, 0, 0))
    base_pose(P)
    v = keys(t, [(0, 0), (0.6, 1), (0.66, -0.3), (0.72, -1), (1.0, 0)])
    back, fwd = max(v, 0.0), max(-v, 0.0)
    P["Spine"].append(lean(-12 * back + 16 * fwd))
    P["Chest"].append(lean(-10 * back + 10 * fwd))
    P["Neck"].append(lean(-18 * back + 18 * fwd))
    P["Head"].append(lean(-24 * back + 22 * fwd))
    for side, _ in SIDES:
        P["UpperArm." + side].append(flex(-30 * back - 20 * fwd))
        P["UpperArm." + side].append(spread(side, 20 * back))
        P["LowerArm." + side].append(flex(-10 * back))
    hips.z += 0.05 * fwd - 0.03 * back
    return P, hips


def clip_convulse(t):
    """Shuddering loop for a bloated about to burst."""
    P, hips = new_pose(), Vector((0, 0, 0))
    base_pose(P)
    ph = 2 * math.pi * t
    P["Spine"].append(lean(6 * math.sin(ph * 2)))
    P["Spine"].append(tilt(7 * math.sin(ph * 3 + 1)))
    P["Chest"].append(turn(8 * math.sin(ph * 2 + 2)))
    P["Head"].append(tilt(18 * math.sin(ph * 3)))
    P["Head"].append(lean(-10 + 8 * math.sin(ph * 4)))
    for side, off in SIDES:
        P["UpperArm." + side].append(flex(10 + 22 * math.sin(ph * 3 + off)))
        P["UpperArm." + side].append(spread(side, 24))
        P["LowerArm." + side].append(flex(-20 + 15 * math.sin(ph * 4 + off)))
        P["UpperLeg." + side].append(flex(10))
        P["LowerLeg." + side].append(flex(-20))
    hips.y -= 0.04 + 0.015 * math.sin(ph * 4)
    return P, hips


def clip_death(t, k=1.0):
    """Knees buckle (0..0.35), then the body pitches forward onto the floor (0.35..0.75) and settles."""
    P, hips = new_pose(), Vector((0, 0, 0))
    base_pose(P)
    buckle = keys(t, [(0, 0), (0.35, 1), (0.6, 0.4), (1.0, 0.0)])
    fall = keys(t, [(0, 0), (0.3, 0.0), (0.72, 1.0), (0.8, 0.96), (1.0, 1.0)])
    P["Hips"].append(lean(84 * fall))
    P["Hips"].append(tilt(8 * fall))
    P["Spine"].append(lean(10 * buckle - 6 * fall))
    P["Chest"].append(tilt(-10 * fall))
    P["Head"].append(turn(55 * fall))
    P["Head"].append(lean(-25 * fall))
    for side, _ in SIDES:
        P["UpperLeg." + side].append(flex(55 * buckle + (8 if side == "L" else -6) * fall))
        P["LowerLeg." + side].append(flex(-95 * buckle - (25 if side == "L" else 5) * fall))
        P["Foot." + side].append(flex(30 * fall))
        arm = 1 if side == "L" else -1
        P["UpperArm." + side].append(flex(30 * buckle - 40 * fall))
        P["UpperArm." + side].append(spread(side, (35 + 20 * arm) * fall))
        P["LowerArm." + side].append(flex(-30 * buckle - 20 * fall))
    # Hips sink with the knees, then drop to the floor and slide forward as the body lays down.
    hips.y -= 0.38 * buckle * (1 - fall) + 0.8 * fall
    hips.z += 0.3 * fall
    return P, hips


# name, seconds, function, loops (the loop flag is informational: ZombieAnimator samples clips by time itself)
ZM_CLIPS = [("Idle", 2.0, clip_idle, True), ("Walk", 1.2, clip_walk, True), ("Run", 0.7, clip_run, True),
            ("Attack", 0.9, clip_attack, False), ("Hit", 0.5, clip_hit, False), ("Spit", 0.85, clip_spit, False),
            ("Convulse", 0.4, clip_convulse, True), ("Death", 1.4, clip_death, False)]
ZM_FPS = 30


def apply_pose(arm, P, hips):
    """Sets the armature's pose bones from a clip sample. Returns {bone: (quaternion, location)}."""
    k = arm.get("zm_k", 1.0)
    out = {}
    for pb in arm.pose.bones:
        R = Matrix.Identity(3)
        for m in P.get(pb.name, []):
            R = R @ m
        B = pb.bone.matrix_local.to_3x3()
        q = (B.inverted() @ R @ B).to_quaternion()
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = q
        loc = Vector((0, 0, 0))
        if pb.name == "Hips":
            loc = B.inverted() @ (to_b(hips) * k)
        pb.location = loc
        out[pb.name] = (q, loc)
    return out


def make_actions(arm):
    """One action per clip on the armature, keyed every frame (quaternions kept on one hemisphere)."""
    bpy.context.scene.render.fps = ZM_FPS
    arm.animation_data_create()
    actions = []
    for name, seconds, fn, _ in ZM_CLIPS:
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        arm.animation_data.action = act
        frames = max(2, int(round(seconds * ZM_FPS)))
        prev = {}
        for f in range(frames + 1):
            P, hips = fn(f / frames)
            pose = apply_pose(arm, P, hips)
            for pb in arm.pose.bones:
                q = pb.rotation_quaternion.copy()
                if pb.name in prev and prev[pb.name].dot(q) < 0:
                    q.negate()
                    pb.rotation_quaternion = q
                prev[pb.name] = q
                pb.keyframe_insert("rotation_quaternion", frame=f)
                if pb.name == "Hips":
                    pb.keyframe_insert("location", frame=f)
        for fc in act.fcurves:
            for kp in fc.keyframe_points:
                kp.interpolation = 'LINEAR'
        actions.append(act)
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
    return actions


def export_zombie(arm):
    """Skinned mesh + armature + every clip as its own take ("<armature>|Walk")."""
    bpy.ops.object.select_all(action='DESELECT')
    arm.select_set(True)
    for c in arm.children:
        c.select_set(True)
    bpy.context.view_layer.objects.active = arm
    path = os.path.join(OUT, arm.name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
        apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=False,
        mesh_smooth_type='FACE', use_mesh_modifiers=False, add_leaf_bones=False,
        primary_bone_axis='Y', secondary_bone_axis='X', use_armature_deform_only=True,
        bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=1.0,
        path_mode='STRIP', embed_textures=False)
    write_meta(path, "fbx_meta_template_animated.txt")
    return path


def preview_zombies(arms, out_dir, side=False):
    """One row per zombie: rest, idle, walk, run, attack wind-up, attack strike, spit, death (posed copies)."""
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_cavity = True
    scene.display.shading.show_shadows = True
    scene.display.shading.show_object_outline = True
    for o in list(scene.objects):
        o.hide_render = True
    poses = [("Walk", 0.1), ("Walk", 0.6), ("Run", 0.25), ("Attack", 0.42), ("Attack", 0.56), ("Hit", 0.18),
             ("Spit", 0.6), ("Death", 0.45), ("Death", 1.0)]
    fns = {n: fn for n, _, fn, _ in ZM_CLIPS}
    temp = []
    y_cursor = 0.0
    col_w = 1.7
    rows = []  # (bottom z, height) per row
    for row, arm in enumerate(arms):
        k = arm.get("zm_k", 1.0)
        scale_down = min(1.0, 2.0 / (1.8 * k))  # draw the big ones smaller so every row fits
        for col, (clip, t) in enumerate(poses):
            a = arm.copy()
            a.data = arm.data
            a.animation_data_clear()
            scene.collection.objects.link(a)
            body = arm.children[0]
            b = body.copy()
            b.data = body.data
            b.parent = a
            b.modifiers["rig"].object = a
            scene.collection.objects.link(b)
            a.hide_render = False
            b.hide_render = False
            a.location = (0, -col * col_w, -y_cursor) if side else (col * col_w, 0, -y_cursor)
            a.scale = (scale_down,) * 3
            apply_pose(a, *fns[clip](t))
            temp += [a, b]
        rows.append((-y_cursor, 1.8 * k * scale_down))
        y_cursor += 1.8 * k * scale_down + 0.5
    bpy.context.view_layer.update()
    bpy.ops.object.camera_add()
    cam = bpy.context.active_object
    cam.data.type = 'ORTHO'
    width = col_w * len(poses)
    cam.data.ortho_scale = max(width, y_cursor) * 1.05
    top, bottom = rows[0][0] + rows[0][1], rows[-1][0]
    mid = (len(poses) - 1) * col_w / 2
    target = Vector((0, -mid, (top + bottom) / 2)) if side else Vector((mid, 0, (top + bottom) / 2))
    cam.location = target + (Vector((-12.0, 0.0, 1.5)) if side else Vector((4.0, -12.0, 2.0)))
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    scene.render.resolution_x = 2000
    scene.render.resolution_y = int(2000 * y_cursor / width) + 40
    path = os.path.join(out_dir, "zombies_side.png" if side else "zombies_poses.png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    for o in temp:
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    return path



# ----------------------------------------------------------------------------------------------- objective props
# Real size (origin on the floor, front +Z), drawn around ObjectiveProp's placeholder parts. The status light stays the
# game's own primitive (its colour shows progress), so each model leaves a socket for it at the same spot.

OBJ_BODY = "C_4A5058"
OBJ_DARK = "C_23262B"
OBJ_OLIVE = "C_4F5640"
OBJ_STEEL = "C_7C838C"
OBJ_RED = "C_A3261C"
OBJ_GLASS = "C_8FB8C8"
OBJ_WHITE = "C_D3D6D2"
SCREEN_DIM = "G_1E4A66"


def obj_generator():
    """Generator (1.4 x 1.2 x 0.9 body): skid frame, ribbed engine block, exhaust stack, fuel tank, front control panel."""
    m = Model("obj_generator")
    for s in (-1, 1):
        m.box((s * 0.6, 0.05, 0), (0.12, 0.1, 1.0), OBJ_DARK, bevel=0.01)  # skids
    m.box((0, 0.14, 0), (1.4, 0.08, 0.92), OBJ_DARK, bevel=0.01)
    m.box((0, 0.66, 0), (1.36, 0.96, 0.86), OBJ_OLIVE, bevel=0.04)
    for i in range(7):
        m.box((-0.66 + i * 0.03 + 0.3, 0.7, -0.44), (0.015, 0.7, 0.02), OBJ_DARK, bevel=0.0)  # rear vents
    for s in (-1, 1):
        for i in range(5):
            m.box((s * 0.69, 0.4 + i * 0.12, 0), (0.02, 0.05, 0.7), OBJ_DARK, bevel=0.0)  # side ribs
    m.box((0, 1.16, 0), (1.3, 0.06, 0.8), OBJ_DARK, bevel=0.01)
    m.cyl((0, 1.35, -0.1), (0.2, 0.35, 0.2), OBJ_STEEL, segments=10)   # exhaust stack
    m.cyl((0, 1.54, -0.1), (0.24, 0.04, 0.24), OBJ_DARK, segments=10)
    m.cyl((0.45, 1.24, 0.1), (0.36, 0.14, 0.36), OBJ_RED, segments=12)  # fuel cap housing
    m.box((0, 0.9, 0.44), (0.62, 0.48, 0.04), OBJ_DARK, bevel=0.01)     # control panel (light sits in the middle)
    m.box((-0.22, 0.78, 0.465), (0.08, 0.08, 0.02), LED_A, bevel=0.0)
    m.box((0.22, 0.78, 0.465), (0.08, 0.08, 0.02), OBJ_STEEL, bevel=0.0)
    m.box((0, 0.42, 0.44), (1.0, 0.1, 0.03), HAZARD, bevel=0.0)
    m.cyl((-0.55, 0.3, 0.42), (0.1, 0.08, 0.1), RUBBER, rot=(90, 0, 0), segments=8)  # cable outlet
    return m


def _screen_housing(m, centre, size, depth, mat=OBJ_DARK):
    x, y, z = centre
    w, h = size
    m.box((x, y, z - depth * 0.5), (w + 0.08, h + 0.08, depth), mat, bevel=0.015)


def obj_terminal():
    """Data / lockdown terminal (0.8 x 1.1 x 0.5 pedestal, screen at y 1.2): slanted keyboard deck, screen hood, cables."""
    m = Model("obj_terminal")
    m.box((0, 0.04, 0), (0.9, 0.08, 0.6), OBJ_DARK, bevel=0.01)
    m.box((0, 0.52, -0.02), (0.76, 0.9, 0.46), OBJ_BODY, bevel=0.03)
    m.box((0, 0.98, 0.12), (0.8, 0.06, 0.34), OBJ_BODY, rot=(-18, 0, 0), bevel=0.01)  # keyboard deck
    for r in range(3):
        m.box((0, 1.0 + r * 0.01, 0.06 + r * 0.07), (0.62, 0.015, 0.04), OBJ_DARK, rot=(-18, 0, 0), bevel=0.0)
    _screen_housing(m, (0, 1.2, 0.075), (0.7, 0.45), 0.16)
    m.box((0, 1.47, 0.05), (0.84, 0.04, 0.22), OBJ_DARK, bevel=0.0)  # hood
    for s in (-1, 1):
        m.box((s * 0.39, 1.2, 0.0), (0.03, 0.5, 0.18), OBJ_DARK, bevel=0.0)
    m.box((0, 0.5, 0.215), (0.5, 0.5, 0.02), OBJ_DARK, bevel=0.0)  # service hatch
    m.box((0.18, 0.62, 0.23), (0.06, 0.06, 0.015), LED_G, bevel=0.0)
    m.cyl((-0.25, 0.05, -0.3), (0.06, 0.1, 0.06), RUBBER, rot=(90, 0, 0), segments=6)
    return m


def obj_console():
    """Containment console (1.1 x 1.0 x 0.7 body): heavy desk with a hazard band, big screen, emergency handle."""
    m = Model("obj_console")
    m.box((0, 0.06, 0), (1.2, 0.12, 0.8), OBJ_DARK, bevel=0.01)
    m.box((0, 0.52, 0), (1.1, 0.84, 0.7), "C_3A434C", bevel=0.03)
    m.box((0, 0.97, 0.08), (1.14, 0.08, 0.56), "C_3A434C", rot=(-12, 0, 0), bevel=0.01)
    m.box((0, 1.05, -0.15), (1.12, 0.12, 0.4), HAZARD, bevel=0.0)
    for i in range(6):
        m.box((-0.45 + i * 0.18, 1.05, 0.06), (0.06, 0.125, 0.01), OBJ_DARK, rot=(0, 0, 35), bevel=0.0)  # stripes
    _screen_housing(m, (0, 1.3, 0.025), (0.9, 0.4), 0.14)
    m.box((0, 1.54, 0.0), (1.0, 0.05, 0.2), OBJ_DARK, bevel=0.0)
    m.box((0.42, 0.98, 0.3), (0.12, 0.05, 0.12), OBJ_RED, bevel=0.01)  # emergency button
    m.box((-0.3, 1.0, 0.25), (0.3, 0.02, 0.14), SCREEN_DIM, rot=(-12, 0, 0), bevel=0.0)
    for s in (-1, 1):
        m.box((s * 0.56, 0.5, 0.0), (0.04, 0.7, 0.5), OBJ_DARK, bevel=0.0)
    return m


def obj_cryo():
    """Cryo container (1 x 1 base, 0.8 wide tank to y 1.8): glass cylinder with frost fluid, frame ribs, pipes."""
    m = Model("obj_cryo")
    m.cyl((0, 0.15, 0), (1.0, 0.3, 1.0), "C_3F454C", segments=16)
    m.cyl((0, 0.33, 0), (0.86, 0.06, 0.86), OBJ_DARK, segments=16)
    m.cyl((0, 1.05, 0), (0.74, 1.42, 0.74), "G_7FD8F0", segments=16, smooth=True)  # frosted tank (glows)
    for y in (0.6, 1.5):
        m.cyl((0, y, 0), (0.8, 0.05, 0.8), OBJ_STEEL, segments=16)  # bands
    for a in range(4):
        ang = math.radians(45 + a * 90)
        m.box((math.sin(ang) * 0.38, 1.05, math.cos(ang) * 0.38), (0.06, 1.5, 0.06), OBJ_STEEL, rot=(0, 45 + a * 90, 0), bevel=0.0)
    m.cyl((0, 1.86, 0), (0.92, 0.2, 0.92), "C_3F454C", segments=16)
    m.cyl((0, 2.0, 0), (0.4, 0.1, 0.4), OBJ_DARK, segments=12)
    for s in (-1, 1):
        m.cyl((s * 0.55, 1.2, -0.15), (0.08, 1.5, 0.08), OBJ_STEEL, segments=8)  # coolant lines
        m.box((s * 0.47, 1.9, -0.15), (0.2, 0.08, 0.08), OBJ_STEEL, bevel=0.0)
        m.box((s * 0.5, 0.35, -0.15), (0.14, 0.08, 0.08), OBJ_STEEL, bevel=0.0)
    m.box((0, 1.05, 0.39), (0.58, 0.98, 0.03), OBJ_DARK, bevel=0.0)  # status window frame
    m.box((0, 0.22, 0.5), (0.4, 0.1, 0.03), HAZARD, bevel=0.0)
    return m


def obj_valve():
    """Reactor coolant valve (pipe 0.35 wide to y 1.5, red wheel at y 1.25, z 0.25): flanged riser, valve body, beacon."""
    m = Model("obj_valve")
    m.cyl((0, 0.05, 0), (0.7, 0.1, 0.7), OBJ_DARK, segments=12)
    m.cyl((0, 0.75, 0), (0.34, 1.4, 0.34), OBJ_STEEL, segments=12)
    for y in (0.15, 0.7, 1.45):
        m.cyl((0, y, 0), (0.46, 0.06, 0.46), OBJ_BODY, segments=12)  # flanges
        for a in range(6):
            ang = math.radians(a * 60)
            m.cyl((math.sin(ang) * 0.2, y + 0.04, math.cos(ang) * 0.2), (0.04, 0.03, 0.04), OBJ_DARK, segments=6)
    m.box((0, 1.25, 0.12), (0.26, 0.26, 0.22), OBJ_BODY, bevel=0.02)  # valve body
    m.cyl((0, 1.25, 0.2), (0.05, 0.12, 0.05), OBJ_DARK, rot=(90, 0, 0), segments=6)  # stem
    n = 12
    for i in range(n):  # wheel rim
        ang = math.radians(i * 360 / n)
        m.box((math.sin(ang) * 0.36, 1.25 + math.cos(ang) * 0.36, 0.25), (0.2, 0.05, 0.05), OBJ_RED, rot=(0, 0, -i * 360 / n), bevel=0.0)
    for ang in (0, 60, 120):
        m.box((0, 1.25, 0.25), (0.72, 0.04, 0.04), OBJ_RED, rot=(0, 0, ang), bevel=0.0)  # spokes
    m.cyl((0, 1.25, 0.25), (0.1, 0.06, 0.1), OBJ_DARK, rot=(90, 0, 0), segments=8)
    m.cyl((0, 1.55, 0), (0.28, 0.08, 0.28), OBJ_DARK, segments=10)  # beacon base (light sits above)
    m.box((0, 0.55, 0.18), (0.22, 0.12, 0.01), HAZARD, bevel=0.0)
    return m


def obj_sample_case():
    """Sample case (0.5 x 0.4 x 0.35): white hard case, latches, front handle, status light socket on the lid."""
    m = Model("obj_sample_case")
    m.box((0, 0.19, 0), (0.5, 0.36, 0.35), OBJ_WHITE, bevel=0.03)
    m.box((0, 0.3, 0), (0.52, 0.03, 0.37), OBJ_DARK, bevel=0.0)  # lid seam
    m.box((0, 0.395, 0), (0.28, 0.02, 0.26), OBJ_DARK, bevel=0.0)  # light socket
    for s in (-1, 1):
        m.box((s * 0.17, 0.3, 0.18), (0.06, 0.08, 0.02), OBJ_STEEL, bevel=0.0)  # latches
        m.box((s * 0.26, 0.05, s * 0.0), (0.02, 0.06, 0.3), RUBBER, bevel=0.0)
    m.box((0, 0.2, 0.2), (0.2, 0.03, 0.04), OBJ_DARK, bevel=0.0)  # handle
    m.box((-0.1, 0.17, 0.195), (0.02, 0.06, 0.03), OBJ_DARK, bevel=0.0)
    m.box((0.1, 0.17, 0.195), (0.02, 0.06, 0.03), OBJ_DARK, bevel=0.0)
    m.box((0, 0.12, 0.178), (0.16, 0.06, 0.005), "C_2E8C5A", bevel=0.0)  # biohazard label
    return m


def obj_keycard():
    """Keycard on a pedestal (0.6 x 0.76 x 0.6): angled reader stand holding a yellow card at y 0.8."""
    m = Model("obj_keycard")
    m.box((0, 0.04, 0), (0.66, 0.08, 0.66), OBJ_DARK, bevel=0.01)
    m.box((0, 0.42, 0), (0.5, 0.68, 0.5), OBJ_BODY, bevel=0.03)
    m.box((0, 0.76, 0), (0.6, 0.04, 0.6), OBJ_DARK, bevel=0.01)
    m.box((0, 0.785, 0), (0.36, 0.02, 0.26), SCREEN_DIM, bevel=0.0)  # reader pad
    m.box((0, 0.8, 0), (0.3, 0.02, 0.2), "C_F0C21C", bevel=0.005)    # the card
    m.box((-0.08, 0.812, 0.02), (0.08, 0.005, 0.06), OBJ_DARK, bevel=0.0)  # chip
    m.box((0, 0.42, 0.255), (0.3, 0.08, 0.01), HAZARD, bevel=0.0)
    return m


def obj_locker():
    """Security locker (1.2 x 2 x 0.6): double armoured doors, reinforced frame, keypad housing at (0.45, 1.1)."""
    m = Model("obj_locker")
    m.box((0, 1.0, -0.02), (1.2, 2.0, 0.56), "C_434A55", bevel=0.03)
    for s in (-1, 1):
        m.box((s * 0.29, 1.0, 0.27), (0.56, 1.86, 0.04), "C_59616D", bevel=0.01)  # doors
        for y in (0.45, 1.0, 1.55):
            m.box((s * 0.29, y, 0.295), (0.5, 0.05, 0.015), OBJ_DARK, bevel=0.0)  # door ribs
    m.box((0, 1.0, 0.3), (0.03, 1.86, 0.03), OBJ_DARK, bevel=0.0)  # seam
    m.box((0.45, 1.1, 0.3), (0.2, 0.34, 0.03), OBJ_DARK, bevel=0.01)  # keypad housing (light inside)
    m.box((-0.1, 1.0, 0.31), (0.04, 0.3, 0.04), OBJ_STEEL, bevel=0.0)  # handles
    m.box((0.1, 1.0, 0.31), (0.04, 0.3, 0.04), OBJ_STEEL, bevel=0.0)
    m.box((0, 2.03, 0), (1.26, 0.06, 0.62), OBJ_DARK, bevel=0.01)
    m.box((0, 0.03, 0), (1.26, 0.06, 0.62), OBJ_DARK, bevel=0.01)
    m.box((0, 1.82, 0.3), (0.7, 0.1, 0.01), HAZARD, bevel=0.0)
    return m


# ----------------------------------------------------------------------------------------------- engineer structures
# Real size, origin on the floor (the drone and the sentry head: origin at their centre). "Accent" parts take the
# structure's health/tier tint in game (Structure.Dress gives each structure its own copy of that material).

ENG_DARK = "C_23262A"
ENG_STEEL = "C_4A4F57"
ENG_LIGHT = "C_7D838C"
ENG_GLOW = "G_59E68C"
ARC_GLOW = "G_80D9FF"


def eng_sentry_base():
    """Sentry tripod (0.75 wide, top flange at 0.88 m): three splayed legs, a column and the turret ring."""
    m = Model("eng_sentry_base")
    for i in range(3):
        a = math.radians(i * 120 + 30)
        foot = (math.sin(a) * 0.42, 0.02, math.cos(a) * 0.42)
        m.limb((0, 0.42, 0), foot, (0.07, 0.06), ENG_STEEL)
        m.box((foot[0], 0.025, foot[2]), (0.14, 0.05, 0.14), RUBBER, bevel=0.01)
    m.cyl((0, 0.34, 0), (0.26, 0.24, 0.26), ENG_DARK, segments=12)        # battery drum
    m.cyl((0, 0.34, 0), (0.28, 0.04, 0.28), HAZARD, segments=12)
    m.box((0, 0.36, 0.14), (0.1, 0.06, 0.02), ENG_GLOW, bevel=0.0)        # charge light
    m.cyl((0, 0.66, 0), (0.12, 0.42, 0.12), ENG_LIGHT, segments=10)       # column
    m.cyl((0, 0.86, 0), (0.36, 0.06, 0.36), ENG_DARK, segments=14)        # turret ring
    m.cyl((0, 0.82, 0), (0.3, 0.04, 0.3), "Accent", segments=14)
    return m


def eng_sentry_head():
    """Sentry gun head, origin at its pivot, aiming +Z: armoured housing, twin barrels to z 0.8, ammo box, sensor."""
    m = Model("eng_sentry_head")
    m.box((0, -0.16, 0), (0.14, 0.08, 0.14), ENG_DARK, bevel=0.01)       # yoke down to the ring
    m.box((0, 0, -0.02), (0.5, 0.3, 0.56), "Accent", bevel=0.04)          # housing
    m.box((0, 0.17, -0.04), (0.42, 0.05, 0.46), ENG_STEEL, bevel=0.015)   # top plate
    m.box((0, 0, 0.27), (0.44, 0.24, 0.04), ENG_DARK, bevel=0.01)         # mantlet
    for s_ in (-1, 1):
        m.cyl((s_ * 0.1, 0, 0.5), (0.065, 0.46, 0.065), ENG_DARK, rot=(90, 0, 0), segments=8)
        m.cyl((s_ * 0.1, 0, 0.42), (0.09, 0.14, 0.09), ENG_STEEL, rot=(90, 0, 0), segments=8)  # shroud
        m.cyl((s_ * 0.1, 0, 0.75), (0.085, 0.06, 0.085), ENG_LIGHT, rot=(90, 0, 0), segments=8)  # muzzle brake
        m.box((s_ * 0.27, -0.02, -0.08), (0.06, 0.2, 0.3), ENG_STEEL, bevel=0.01)  # side armour
    m.box((0.33, -0.04, -0.08), (0.1, 0.18, 0.24), "C_4B5A36", bevel=0.01)  # ammo box
    m.box((0, 0.23, 0.08), (0.16, 0.08, 0.12), ENG_DARK, bevel=0.01)     # sensor block
    m.box((0, 0.23, 0.145), (0.1, 0.035, 0.01), ENG_GLOW, bevel=0.0)
    m.box((0, 0.08, -0.31), (0.3, 0.08, 0.02), HAZARD, bevel=0.0)
    return m


def eng_drone():
    """Engineer drone (0.9 x 0.25 x 0.9), origin at its centre: body, four arms, rotors as children Rotor0..3."""
    m = Model("eng_drone")
    m.sph((0, 0, 0), (0.34, 0.16, 0.4), "Accent", segments=12)
    m.box((0, 0.07, -0.02), (0.22, 0.05, 0.26), ENG_DARK, bevel=0.01)     # top shell
    m.box((0, 0.1, 0.02), (0.08, 0.02, 0.06), ENG_GLOW, bevel=0.0)        # status LED
    m.sph((0, -0.08, 0.12), (0.1, 0.1, 0.1), ENG_DARK, segments=8)        # camera ball
    m.box((0, -0.08, 0.17), (0.04, 0.04, 0.02), "G_F05A4A", bevel=0.0)
    m.box((0, -0.1, -0.04), (0.06, 0.06, 0.16), ENG_STEEL, bevel=0.01)    # gun pod
    m.cyl((0, -0.1, 0.09), (0.03, 0.12, 0.03), ENG_DARK, rot=(90, 0, 0), segments=6)
    for i in range(4):
        a = math.radians(45 + i * 90)
        tip = (math.sin(a) * 0.34, 0.02, math.cos(a) * 0.34)
        m.limb((0, 0.0, 0), tip, (0.04, 0.03), ENG_STEEL)
        m.cyl((tip[0], 0.0, tip[2]), (0.07, 0.07, 0.07), ENG_DARK, segments=8)    # motor
        m.cyl((tip[0], 0.03, tip[2]), (0.28, 0.012, 0.28), "C_3A4048", segments=14)  # guard ring disk (thin)
        with m.rig(f"Rotor{i}", (tip[0], 0.06, tip[2])):
            m.box((tip[0], 0.06, tip[2]), (0.26, 0.008, 0.035), "C_15161A", bevel=0.0)
            m.box((tip[0], 0.06, tip[2]), (0.035, 0.008, 0.26), "C_15161A", bevel=0.0)
    return m


def eng_barricade():
    """Deployable barricade wall (3 x 1.4 x 0.45, back braces at -Z): steel frame, armour plates, hazard band."""
    m = Model("eng_barricade")
    w, h = 3.0, 1.4
    m.box((0, 0.05, 0), (w, 0.1, 0.5), ENG_DARK, bevel=0.01)              # foot rail
    m.box((0, h - 0.04, 0), (w, 0.08, 0.36), ENG_DARK, bevel=0.01)        # top rail
    for x in (-1.46, -0.5, 0.5, 1.46):
        m.box((x, h / 2, 0), (0.1, h, 0.4), ENG_STEEL, bevel=0.01)        # uprights
    for i, x in enumerate((-0.98, 0.0, 0.98)):
        m.box((x, 0.72, 0.12), (0.84, 1.1, 0.12), "Accent", bevel=0.03)   # armour plates (front)
        m.box((x, 0.72, 0.185), (0.6, 0.06, 0.02), ENG_DARK, bevel=0.0)   # weld seam
        m.box((x, 1.08, 0.185), (0.84, 0.1, 0.02), HAZARD, bevel=0.0)
        for sx in (-1, 1):
            for y in (0.28, 1.18):
                m.cyl((x + sx * 0.36, y, 0.19), (0.04, 0.02, 0.04), ENG_LIGHT, rot=(90, 0, 0), segments=6)  # bolts
        m.box((x, 0.72, -0.08), (0.84, 1.1, 0.06), ENG_STEEL, bevel=0.01)  # back skin
    m.box((-0.3, 1.2, 0.11), (0.3, 0.05, 0.14), ENG_DARK, bevel=0.0)      # firing slot
    for x in (-1.05, 1.05):
        m.limb((x, 1.25, -0.12), (x, 0.04, -0.65), (0.1, 0.08), ENG_STEEL)  # kickstand braces
        m.box((x, 0.03, -0.62), (0.22, 0.06, 0.2), RUBBER, bevel=0.01)
    m.box((1.25, 0.25, 0.19), (0.12, 0.06, 0.02), ENG_GLOW, bevel=0.0)    # power light
    return m


def eng_arc_post():
    """Arc fence pylon (0.3 x 1.6, emitter at 0.9 m facing +/-X): base plate, insulator stack, glowing coil."""
    m = Model("eng_arc_post")
    m.box((0, 0.04, 0), (0.42, 0.08, 0.42), ENG_DARK, bevel=0.01)
    m.box((0, 0.13, 0), (0.24, 0.1, 0.24), HAZARD, bevel=0.01)
    m.box((0, 0.75, 0), (0.12, 1.3, 0.12), ENG_STEEL, bevel=0.01)
    m.box((0, 0.3, 0.07), (0.1, 0.18, 0.03), "Accent", bevel=0.0)         # capacitor badge
    m.box((0, 0.36, 0.085), (0.05, 0.03, 0.01), ARC_GLOW, bevel=0.0)
    for y in (0.62, 0.7, 0.78, 1.02, 1.1, 1.18):
        m.cyl((0, y, 0), (0.2, 0.04, 0.2), "C_C9B48A", segments=10)        # ceramic insulators
    m.cyl((0, 0.9, 0), (0.22, 0.14, 0.22), "Accent", segments=12)          # emitter collar
    m.cyl((0, 0.9, 0), (0.14, 0.4, 0.14), ARC_GLOW, rot=(0, 0, 90), segments=8)  # coil across X
    m.sph((0, 1.48, 0), (0.14, 0.14, 0.14), ENG_LIGHT, segments=8)        # top ball
    m.box((0, 1.4, 0), (0.06, 0.1, 0.06), ENG_DARK, bevel=0.0)
    return m


def obj_nest():
    """Infected nest, fit model (the game scales it to 2.2 x 1.5 x 2.2): lumpy flesh mound, glowing pustules, spines."""
    m = Model("obj_nest", fit=True)
    flesh, dark, vein = "C_6E2A44", "C_3E1626", "C_8E3A54"
    pus = "G_D8F060"
    m.sph((0, 0.55, 0), (1.9, 1.1, 1.9), flesh, segments=14)
    lumps = [(0.55, 0.45, 0.45, 0.9), (-0.6, 0.4, 0.3, 0.85), (0.1, 0.4, -0.65, 0.9), (-0.3, 0.35, 0.65, 0.7),
             (0.7, 0.3, -0.35, 0.65), (-0.6, 0.3, -0.5, 0.7), (0.0, 0.95, 0.05, 0.9)]
    for x, y, z, d in lumps:
        m.sph((x, y, z), (d, d * 0.8, d), flesh if d > 0.75 else vein, segments=10)
    m.sph((0, 0.06, 0), (2.2, 0.16, 2.2), dark, segments=14)              # creeping base
    for i in range(9):
        a = math.radians(i * 40 + 10)
        r = 0.55 + (i % 3) * 0.12
        y = 0.55 + ((i * 37) % 5) * 0.1
        x, z = math.sin(a) * r, math.cos(a) * r
        m.sph((x * 1.15, y, z * 1.15), (0.2, 0.2, 0.2), pus, segments=8)  # pustules
    for i in range(7):
        a = math.radians(i * 51)
        base = (math.sin(a) * 0.6, 0.8, math.cos(a) * 0.6)
        tip = (math.sin(a) * 1.0, 1.45 - (i % 2) * 0.2, math.cos(a) * 1.0)
        m.limb(base, tip, (0.09, 0.09), "C_CFC4A8")                       # bone spines
    m.sph((0, 1.25, 0), (0.42, 0.3, 0.42), pus, segments=10)              # glowing crown
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
    fp_arm,
    obj_generator, obj_terminal, obj_console, obj_cryo, obj_valve, obj_sample_case, obj_keycard, obj_locker,
    eng_sentry_base, eng_sentry_head, eng_drone, eng_barricade, eng_arc_post, obj_nest,
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
    "objectives": ["obj_generator", "obj_terminal", "obj_console", "obj_cryo", "obj_valve", "obj_sample_case", "obj_keycard",
                   "obj_locker"],
    "clutter": ["prop_barrel", "prop_light_fixture", "prop_fusebox", "prop_wall_vent", "prop_extinguisher", "prop_rubble"],
    "engineer": ["eng_sentry_base", "eng_sentry_head", "eng_drone", "eng_barricade", "eng_arc_post", "obj_nest", "supply_cache"],
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
        for c in o.children:
            c.hide_render = True
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
            for child in src.children:  # rig parts (limbs, heads)
                k = child.copy()
                k.data = child.data
                k.parent = c
                k.hide_render = False
                scene.collection.objects.link(k)
                temp.append(k)
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


# ----------------------------------------------------------------------------------------------- attachments
# Review-only for now: built and rendered with --attachments, never exported to Resources/Models.
# Origins (Unity space, +Z forward):
#   optics       rail top centre, the window/tube faces +Z
#   muzzle       bore centre at the muzzle face, extends +Z
#   underbarrel  rail clamp top, hangs -Y
#   magazines    mag well bottom centre, hang -Y
#   stocks       receiver rear on the bore line, extend -Z

def att_mount(m, length, y=0.0, z=0.0, w=0.026):
    """A rail clamp: low base block with two cross bolts."""
    m.box((0, y + 0.004, z), (w, 0.008, length), STEEL_M, bevel=0.0015)
    for s in (-1, 1):
        m.cyl((s * (w / 2 + 0.001), y + 0.004, z + length * 0.25), (0.007, 0.004, 0.007), STEEL_L, rot=(0, 0, 90), segments=6)
    m.box((w / 2 + 0.003, y + 0.004, z - length * 0.2), (0.004, 0.006, 0.012), STEEL_L, bevel=0.0)  # throw lever


def att_red_dot():
    """Mini red dot: low body, open hood, green-tinted window centred 0.03 above the rail, red emitter."""
    m = Model("att_red_dot", triangulate=True)
    att_mount(m, 0.04, z=-0.004)
    m.box((0, 0.014, -0.006), (0.028, 0.014, 0.044), POLY, bevel=0.003)  # body
    for s in (-1, 1):
        m.box((s * 0.0155, 0.031, 0.008), (0.004, 0.026, 0.016), POLY, bevel=0.0015)  # hood sides
    m.sym([(0.0, 0.042), (0.016, 0.042), (0.016, 0.048), (0.004, 0.047)], 0.0175, POLY, chamfer=0.001)  # hood top
    m.box((0, 0.031, 0.012), (0.027, 0.024, 0.002), GLASS_G, bevel=0.0)  # window
    m.box((0, 0.024, -0.012), (0.004, 0.004, 0.003), DOT_R, bevel=0.0)  # emitter
    m.cyl((0.016, 0.014, -0.012), (0.012, 0.004, 0.012), STEEL_L, rot=(0, 0, 90), segments=8)  # brightness dial
    return m


def att_holo():
    """Holographic sight: battery tunnel base, tall square hood, window centred 0.036 above the rail."""
    m = Model("att_holo", triangulate=True)
    att_mount(m, 0.07, z=0.0)
    m.box((0, 0.013, 0.0), (0.032, 0.014, 0.08), POLY, bevel=0.003)  # body / battery tunnel
    m.cyl((0, 0.013, 0.041), (0.016, 0.004, 0.016), STEEL_L, rot=(90, 0, 0), segments=8)  # battery cap
    for s in (-1, 1):
        m.box((s * 0.018, 0.038, 0.012), (0.004, 0.036, 0.05), POLY, bevel=0.0015)  # hood sides
    m.box((0, 0.058, 0.012), (0.04, 0.006, 0.05), POLY, bevel=0.0015)  # hood top
    m.box((0, 0.037, -0.011), (0.032, 0.032, 0.002), GLASS_G, bevel=0.0)  # rear window
    m.box((0, 0.037, 0.035), (0.032, 0.032, 0.002), GLASS_G, bevel=0.0)  # front window
    m.box((0, 0.022, -0.03), (0.022, 0.006, 0.016), STEEL_L, bevel=0.0)  # up/down buttons
    m.box((0, 0.022, -0.03), (0.003, 0.0065, 0.004), DOT_R, bevel=0.0)
    return m


def att_sniper_scope():
    """Variable scope: 30 mm tube centred 0.048 above the rail, bells front and back, turrets, two rings."""
    m = Model("att_sniper_scope", triangulate=True)
    y = 0.048
    for z in (-0.045, 0.055):  # rings
        m.box((0, 0.004, z), (0.028, 0.008, 0.02), STEEL_M, bevel=0.0015)  # clamp
        m.box((0, 0.019, z), (0.016, 0.026, 0.014), STEEL_M, bevel=0.0015)  # riser
        tube(m, z - 0.008, z + 0.008, 0.038, STEEL_M, y=y, segments=12)
        m.cyl((0.017, 0.006, z), (0.006, 0.004, 0.006), STEEL_L, rot=(0, 0, 90), segments=6)
    tube(m, -0.09, 0.1, 0.03, POLY, y=y, segments=12)  # main tube
    m.cone((0, y, 0.115), (0.044, 0.03, 0.044), POLY, rot=(90, 0, 0), segments=12)  # objective taper (points back)
    tube(m, 0.12, 0.17, 0.046, POLY, y=y, segments=12)  # objective bell
    tube(m, 0.17, 0.172, 0.04, GLASS_G, y=y, segments=12)
    tube(m, -0.14, -0.09, 0.04, POLY, y=y, segments=12)  # eyepiece
    tube(m, -0.12, -0.112, 0.042, RUBBER, y=y, segments=12)  # zoom ring
    tube(m, -0.142, -0.14, 0.034, GLASS_G, y=y, segments=12)
    m.cyl((0, y + 0.022, 0.0), (0.022, 0.016, 0.022), STEEL_M, segments=10)  # elevation turret
    m.cyl((0.022, y, 0.0), (0.022, 0.016, 0.022), STEEL_M, rot=(0, 0, 90), segments=10)  # windage turret
    m.cyl((-0.019, y, 0.0), (0.016, 0.01, 0.016), STEEL_L, rot=(0, 0, 90), segments=8)  # parallax
    return m


def att_compensator():
    """Ported compensator for a ~0.02 bore barrel: clamp collar, three top ports, side slots, crown."""
    m = Model("att_compensator", triangulate=True)
    tube(m, 0.0, 0.012, 0.028, STEEL_M, y=0.0)  # collar
    tube(m, 0.012, 0.07, 0.026, STEEL_D, y=0.0, segments=10)
    for z in (0.026, 0.04, 0.054):
        m.box((0, 0.0125, z), (0.012, 0.003, 0.007), POLY, bevel=0.0)  # top ports
        for s in (-1, 1):
            m.box((s * 0.0125, 0.0, z), (0.003, 0.012, 0.007), POLY, bevel=0.0)
    tube(m, 0.07, 0.0705, 0.012, POLY, y=0.0)  # bore
    return m


def att_long_barrel():
    """Barrel extension: threaded collar, fluted extension, crowned muzzle."""
    m = Model("att_long_barrel", triangulate=True)
    tube(m, 0.0, 0.018, 0.026, STEEL_M, y=0.0)
    tube(m, 0.018, 0.16, 0.02, STEEL_D, y=0.0, segments=10)
    for a in range(4):
        ang = math.radians(a * 90 + 45)
        m.box((math.cos(ang) * 0.0095, math.sin(ang) * 0.0095, 0.09), (0.003, 0.003, 0.1), POLY, rot=(0, 0, a * 90 + 45), bevel=0.0)
    tube(m, 0.155, 0.165, 0.022, STEEL_L, y=0.0)  # crown
    tube(m, 0.165, 0.1655, 0.011, POLY, y=0.0)
    return m


def att_foregrip():
    """Vertical foregrip: rail clamp, raked grip with finger ribs, rubber cap."""
    m = Model("att_foregrip", triangulate=True)
    m.box((0, -0.006, 0.0), (0.028, 0.012, 0.045), STEEL_M, bevel=0.0015)
    m.box((0.016, -0.006, -0.01), (0.004, 0.008, 0.012), STEEL_L, bevel=0.0)
    m.limb((0, -0.012, 0.0), (0, -0.095, -0.012), (0.026, 0.026), POLY, kind="cyl")
    for t in (0.35, 0.55, 0.75):
        m.limb((0, -0.012 - 0.083 * t + 0.003, -0.012 * t), (0, -0.012 - 0.083 * t - 0.003, -0.012 * t), (0.029, 0.029), POLY_L, kind="cyl")
    m.limb((0, -0.092, -0.0115), (0, -0.102, -0.013), (0.03, 0.03), RUBBER, kind="cyl")
    return m


def att_laser():
    """Laser module: clamp, boxy housing, red laser lens and a green-tinted torch lens, rear pressure switch."""
    m = Model("att_laser", triangulate=True)
    m.box((0, -0.004, 0.0), (0.024, 0.008, 0.05), STEEL_M, bevel=0.0015)
    m.box((0, -0.022, 0.002), (0.028, 0.028, 0.066), POLY, bevel=0.003)
    tube(m, 0.035, 0.037, 0.012, DOT_R, y=-0.016, x=-0.006)
    tube(m, 0.035, 0.037, 0.012, GLASS_G, y=-0.026, x=0.005)
    m.box((0, -0.022, -0.033), (0.014, 0.012, 0.004), RUBBER, bevel=0.0)  # switch
    m.box((0.0145, -0.022, 0.0), (0.002, 0.014, 0.03), STEEL_L, bevel=0.0)  # adjuster panel
    return m


def att_ext_mag_curved():
    """Extended curved rifle mag (about 1.5x the stock one), steel with a polymer floor plate."""
    m = Model("att_ext_mag_curved", triangulate=True)
    curved_mag(m, (-0.034, 0.0), 0.031, 0.29, 0.15, 0.028, 0.016, STEEL_M, steps=9)
    m.sym([(0.112, -0.292), (0.214, -0.28), (0.218, -0.296), (0.114, -0.308)], 0.018, POLY, chamfer=0.002)
    for i in range(4):  # stiffening ribs
        t = (i + 1) / 5
        m.box((0, -0.29 * t, 0.15 * t * t + 0.012), (0.034, 0.004, 0.05), STEEL_D, rot=(-25 * t, 0, 0), bevel=0.0)
    return m


def att_ext_mag_stick():
    """Extended straight stick mag for SMGs, pistols and the DMR (scaled per gun)."""
    m = Model("att_ext_mag_stick", triangulate=True)
    m.sym([(-0.022, 0.0), (0.022, 0.0), (0.03, -0.27), (-0.016, -0.27)], 0.014, STEEL_M, chamfer=0.003)
    for i in range(7):
        y = -0.03 - i * 0.034
        m.box((0, y, 0.002 + i * 0.001), (0.03, 0.006, 0.042), STEEL_D, bevel=0.0)
    m.sym([(-0.02, -0.268), (0.034, -0.268), (0.036, -0.282), (-0.02, -0.282)], 0.016, POLY, chamfer=0.002)
    m.box((0, -0.25, 0.031), (0.028, 0.02, 0.004), TAN, bevel=0.0)  # pull tab
    return m


def att_ext_mag_top():
    """Kestrel's extended top mag: longer translucent case that overhangs the back of the receiver."""
    m = Model("att_ext_mag_top", triangulate=True)
    m.box((0, 0.008, -0.04), (0.032, 0.016, 0.44), GLASS_G, bevel=0.002)
    m.box((0, 0.008, -0.264), (0.034, 0.018, 0.012), POLY, bevel=0.002)  # end cap
    for i in range(14):
        m.box((0, 0.0165, -0.24 + i * 0.03), (0.028, 0.002, 0.004), BRASS, bevel=0.0)
    return m


def att_ext_drum():
    """Big drum for the Hailstorm and Anvil: wider and deeper than the Hailstorm's stock drum, with a wind key."""
    m = Model("att_ext_drum", triangulate=True)
    m.box((0, -0.02, 0.0), (0.05, 0.04, 0.06), POLY, bevel=0.002)  # neck
    m.cyl((0, -0.11, 0.0), (0.2, 0.09, 0.2), POLY, rot=(0, 0, 90), segments=16)
    m.cyl((0, -0.11, 0.0), (0.16, 0.092, 0.16), POLY_L, rot=(0, 0, 90), segments=16)
    m.cyl((0, -0.11, 0.0), (0.05, 0.094, 0.05), STEEL_M, rot=(0, 0, 90), segments=8)
    m.box((0.05, -0.11, 0.0), (0.006, 0.01, 0.05), STEEL_L, bevel=0.0)  # wind key
    for i in range(7):
        ang = math.radians(200 + i * 22)
        m.box((0.0465, -0.11 + math.sin(ang) * 0.065, math.cos(ang) * 0.065), (0.003, 0.012, 0.012), SHELL_RED, bevel=0.0)
    return m


def att_fast_mag():
    """Fast mag: two stick mags taped side by side, the spare offset forward (shown at rifle-mag size)."""
    m = Model("att_fast_mag", triangulate=True)
    for x, dz in ((0.0, 0.0), (-0.034, 0.012)):
        m.sym([(-0.03 + dz, 0.0 - (0.0 if x == 0 else 0.03)), (0.03 + dz, 0.0 - (0.0 if x == 0 else 0.03)),
               (0.05 + dz, -0.2), (-0.012 + dz, -0.2)], 0.0155, STEEL_M, chamfer=0.003, x=x)
        m.sym([(-0.014 + dz, -0.198), (0.054 + dz, -0.198), (0.056 + dz, -0.21), (-0.014 + dz, -0.21)], 0.017, POLY, chamfer=0.002, x=x)
    for y in (-0.07, -0.15):
        m.box((-0.017, y, 0.016 + (-y) * 0.1), (0.072, 0.022, 0.07), TAN, bevel=0.003)  # tape wraps
    return m


def att_speedloader():
    """Bulldog fast mag: six-round speedloader with a knurled knob (rides on the belt, shown on its own)."""
    m = Model("att_speedloader", triangulate=True)
    m.cyl((0, 0.0, 0.0), (0.054, 0.016, 0.054), POLY, rot=(90, 0, 0), segments=12)
    m.cyl((0, 0.0, -0.016), (0.02, 0.018, 0.02), STEEL_L, rot=(90, 0, 0), segments=8)
    for a in range(6):
        ang = a * math.pi / 3
        x, y = 0.017 * math.cos(ang), 0.017 * math.sin(ang)
        m.cyl((x, y, 0.018), (0.011, 0.03, 0.011), BRASS, rot=(90, 0, 0), segments=8)
        m.cyl((x, y, 0.035), (0.009, 0.006, 0.009), STEEL_D, rot=(90, 0, 0), segments=8)
    return m


def att_light_stock():
    """Skeleton stock: buffer tube, two thin struts, small rubber pad (light, quick to aim)."""
    m = Model("att_light_stock", triangulate=True)
    tube(m, -0.12, 0.0, 0.026, STEEL_M, y=0.0, segments=10)
    m.sym([(-0.02, 0.012), (-0.24, 0.006), (-0.24, -0.006), (-0.02, 0.0)], 0.012, POLY, chamfer=0.002)
    m.sym([(-0.04, -0.012), (-0.24, -0.09), (-0.24, -0.102), (-0.04, -0.024)], 0.012, POLY, chamfer=0.002)
    m.sym([(-0.24, 0.016), (-0.256, 0.016), (-0.256, -0.112), (-0.24, -0.112)], 0.022, RUBBER, chamfer=0.003)
    m.box((0, -0.03, -0.08), (0.02, 0.012, 0.02), STEEL_L, bevel=0.0)  # adjust latch
    return m


def att_heavy_stock():
    """Padded precision stock: solid body, raised cheek pad, thick recoil pad, sling cup (steadier, slower)."""
    m = Model("att_heavy_stock", triangulate=True)
    m.sym([(0.0, 0.03), (-0.06, 0.028), (-0.29, 0.024), (-0.3, 0.012), (-0.3, -0.125), (-0.285, -0.135),
           (-0.15, -0.095), (-0.05, -0.06), (0.0, -0.045)], 0.026, POLY_L, chamfer=0.006)
    m.sym([(-0.09, 0.028), (-0.26, 0.026), (-0.26, 0.05), (-0.24, 0.058), (-0.11, 0.058), (-0.09, 0.046)], 0.022, TAN, chamfer=0.004)  # cheek pad
    m.sym([(-0.3, 0.02), (-0.33, 0.02), (-0.33, -0.13), (-0.3, -0.138)], 0.03, RUBBER, chamfer=0.005)  # recoil pad
    for y in (-0.01, -0.05, -0.09):
        m.box((0, y, -0.331), (0.056, 0.004, 0.003), POLY, bevel=0.0)
    m.cyl((-0.027, -0.1, -0.22), (0.014, 0.006, 0.014), STEEL_L, rot=(0, 0, 90), segments=8)  # sling cup
    return m


ATTACHMENTS = [att_red_dot, att_holo, att_sniper_scope, att_compensator, att_long_barrel, att_foregrip, att_laser,
               att_ext_mag_curved, att_ext_mag_stick, att_ext_mag_top, att_ext_drum, att_fast_mag, att_speedloader,
               att_light_stock, att_heavy_stock]

# Mount points per gun (Unity space): rail (y, z) from WeaponSights; muzzle (y, z) and barrel diameter; underbarrel
# clamp (y, z). Used only to lay the attachments onto the guns for the review renders.
MOUNTS = {
    "weapon_halberd_ar":    dict(rail=(0.052, 0.15), muzzle=(0.02, 0.65), bore=0.019, under=(-0.032, 0.40)),
    "weapon_breacher_12":   dict(rail=(0.046, 0.14), muzzle=(0.02, 0.8), bore=0.03, under=(-0.035, 0.7)),
    "weapon_ranger_9":      dict(rail=(0.044, 0.09), muzzle=(0.02, 0.2415), bore=0.018, under=(-0.022, 0.165)),
    "weapon_wasp_smg":      dict(rail=(0.052, 0.115), muzzle=(0.02, 0.42), bore=0.022, under=(-0.028, 0.26)),
    "weapon_anvil_lmg":     dict(rail=(0.087, 0.2), muzzle=(0.02, 0.9), bore=0.034, under=(-0.03, 0.62)),
    "weapon_mule_launcher": dict(rail=(0.105, 0.12), muzzle=(0.02, 0.65), bore=0.08, under=(-0.015, 0.42)),
    "weapon_vesper_dmr":    dict(rail=(0.052, 0.15), muzzle=(0.02, 0.65), bore=0.017, under=(-0.028, 0.45)),
    "weapon_bulldog_44":    dict(rail=(0.047, 0.11), muzzle=(0.022, 0.2415), bore=0.02, under=(-0.008, 0.17)),
    "weapon_hailstorm":     dict(rail=(0.05, 0.18), muzzle=(0.02, 0.8), bore=0.024, under=(-0.05, 0.5)),
    "weapon_kestrel_pdw":   dict(rail=(0.065, 0.12), muzzle=(0.02, 0.42), bore=0.018, under=(-0.04, 0.32)),
}

# One example loadout per gun for the fitted renders (optic, muzzle, underbarrel).
FITTED = {
    "weapon_halberd_ar": ["att_holo", "att_compensator", "att_foregrip"],
    "weapon_breacher_12": ["att_red_dot", "att_long_barrel", "att_laser"],
    "weapon_ranger_9": ["att_red_dot", "att_compensator", "att_laser"],
    "weapon_wasp_smg": ["att_red_dot", "att_compensator", "att_foregrip"],
    "weapon_anvil_lmg": ["att_holo", "att_compensator", "att_foregrip"],
    "weapon_mule_launcher": ["att_red_dot", "att_laser"],
    "weapon_vesper_dmr": ["att_sniper_scope", "att_compensator", "att_foregrip"],
    "weapon_bulldog_44": ["att_red_dot", "att_long_barrel", "att_laser"],
    "weapon_hailstorm": ["att_holo", "att_long_barrel", "att_laser"],
    "weapon_kestrel_pdw": ["att_red_dot", "att_compensator", "att_foregrip"],
}
OPTICS = {"att_red_dot", "att_holo", "att_sniper_scope"}
MUZZLE = {"att_compensator", "att_long_barrel"}


def _show(src, loc=(0, 0, 0), scale=1.0):
    scene = bpy.context.scene
    c = src.copy()
    c.data = src.data
    c.hide_render = False
    c.location = loc
    c.scale = (scale, scale, scale)
    scene.collection.objects.link(c)
    return c


def _shot(objs, path, w, h, view=Vector((-2.0, -0.55, 0.45)), pad=1.15):
    """Ortho 3/4 shot framing objs (seen from the left, muzzle to the right)."""
    scene = bpy.context.scene
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    centre = (lo + hi) / 2
    bpy.ops.object.camera_add()
    cam = bpy.context.active_object
    cam.data.type = 'ORTHO'
    span = max(hi.y - lo.y, (hi.z - lo.z) * w / h, 0.05)
    cam.data.ortho_scale = span * pad
    cam.location = centre + view
    cam.rotation_euler = (centre - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    scene.render.resolution_x = w
    scene.render.resolution_y = h
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)


def preview_attachments(atts, guns, out_dir):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_cavity = True
    scene.display.shading.show_shadows = True
    scene.display.shading.show_object_outline = True
    scene.display.shading.background_type = 'VIEWPORT'
    scene.display.shading.background_color = (0.04, 0.04, 0.045)
    for o in list(atts.values()) + list(guns.values()):
        o.hide_render = True
    os.makedirs(out_dir, exist_ok=True)
    paths = []
    # Each attachment on its own, close up.
    for name, src in atts.items():
        c = _show(src)
        p = os.path.join(out_dir, name + ".png")
        _shot([c], p, 900, 600, view=Vector((-1.4, 1.3, 0.9)), pad=1.5)  # from behind-left, so windows show
        bpy.data.objects.remove(c, do_unlink=True)
        paths.append(p)
    # Contact sheet: three rows of five.
    scene.display.shading.background_type = 'VIEWPORT'
    temp = []
    for i, (name, src) in enumerate(atts.items()):
        col, row = i % 5, i // 5
        temp.append(_show(src, (0, -col * 0.45, -row * 0.42)))
    p = os.path.join(out_dir, "attachments_sheet.png")
    _shot(temp, p, 1800, 1100, pad=1.08)
    paths.append(p)
    for c in temp:
        bpy.data.objects.remove(c, do_unlink=True)
    # Each gun with its example loadout.
    for gname, names in FITTED.items():
        gun = guns.get(gname)
        if gun is None:
            continue
        mt = MOUNTS[gname]
        temp = [_show(gun)]
        for an in names:
            src = atts[an]
            if an in OPTICS:
                loc, s = (0, mt["rail"][0], mt["rail"][1] - (0.02 if an == "att_sniper_scope" else 0.0)), 1.0
            elif an in MUZZLE:
                loc, s = (0, mt["muzzle"][0], mt["muzzle"][1]), max(0.8, mt["bore"] / 0.019)
            else:
                loc, s = (0, mt["under"][0], mt["under"][1]), 1.0
            temp.append(_show(src, to_b(loc), s))
        p = os.path.join(out_dir, "fitted_" + gname.replace("weapon_", "") + ".png")
        _shot(temp, p, 1400, 700)
        paths.append(p)
        for c in temp:
            bpy.data.objects.remove(c, do_unlink=True)
    return paths


def build_attachments(preview_dir):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    atts = {}
    for b in ATTACHMENTS:
        o = b().finish()
        atts[o.name] = o
        print("built", o.name, len(o.data.polygons), "faces")
    guns = {}
    for b in BUILDERS:
        if b.__name__.startswith("weapon_"):
            m = b()
            if m.name in MOUNTS:
                guns[m.name] = m.finish()
            else:
                for p in m.parts:
                    bpy.data.objects.remove(p, do_unlink=True)
    for p in preview_attachments(atts, guns, preview_dir):
        print("preview", p)


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    preview_path = None
    only = None
    if "--preview" in argv:
        preview_path = os.path.abspath(argv[argv.index("--preview") + 1])  # a folder
    if "--only" in argv:
        only = set(argv[argv.index("--only") + 1].split(","))
    if "--attachments" in argv:  # review renders only: nothing is exported
        build_attachments(os.path.abspath(argv[argv.index("--attachments") + 1]))
        return

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
        print("exported", export(o), len(o.data.polygons) + sum(len(c.data.polygons) for c in o.children), "faces",
              f"({len(o.children)} rig parts)" if o.children else "")
        built.append(o)
    zombies = []
    for b in ZOMBIES:
        z = b()
        if only and z.name not in only:
            for p in z.extras.parts:
                bpy.data.objects.remove(p, do_unlink=True)
            continue
        arm = z.build()
        actions = make_actions(arm)
        print("exported", export_zombie(arm), len(arm.children[0].data.polygons), "faces,", len(actions), "clips")
        for a in actions:  # the exporter takes every action that fits the armature, so the next zombie must not see these
            bpy.data.actions.remove(a)
        zombies.append(arm)
    if preview_path:
        os.makedirs(preview_path, exist_ok=True)
        if built:
            for p in preview(built, preview_path):
                print("preview", p)
        if zombies:
            print("preview", preview_zombies(zombies, preview_path))
            print("preview", preview_zombies(zombies, preview_path, side=True))


main()
