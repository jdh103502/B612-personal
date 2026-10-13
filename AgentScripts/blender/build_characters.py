# Builds the Prince and the Fox as faceted, papercraft-style low-poly models and exports
# each as an FBX made of named parts (pivots at the joints) for procedural animation in Unity.
#   blender --background --factory-startup --python build_characters.py -- <output_dir>
# Blender axes: Z up, characters face -Y.
import bpy
import bmesh
import math
import os
import random
import sys
from mathutils import Matrix, Vector

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else os.getcwd()
random.seed(612)

# Overall facet density: decimation ratios (and the hair face budget) are multiplied by this.
DETAIL = 2.2

bpy.ops.wm.read_factory_settings(use_empty=True)
SCENE = bpy.context.scene

# ---------------------------------------------------------------- materials
MATS = {}


def srgb_to_lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def mat(hexs):
    key = hexs.lstrip("#").upper()
    if key in MATS:
        return MATS[key]
    m = bpy.data.materials.new("M_" + key)
    rgb = [srgb_to_lin(int(key[i:i + 2], 16) / 255.0) for i in (0, 2, 4)]
    m.diffuse_color = (rgb[0], rgb[1], rgb[2], 1.0)
    try:
        m.use_nodes = True
    except Exception:
        pass
    nt = getattr(m, "node_tree", None)
    if nt:
        bsdf = nt.nodes.get("Principled BSDF")
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
            bsdf.inputs["Roughness"].default_value = 0.85
    MATS[key] = m
    return m


# ---------------------------------------------------------------- bmesh primitives
def jit(a):
    return (random.random() * 2 - 1) * a


def bm_ico(r, sub=3):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=r)
    return bm


def bm_uv(r, u=10, v=7):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r)
    return bm


def bm_cube(sx, sy, sz):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=(sx, sy, sz), verts=bm.verts)
    return bm


def bm_cone(r1, r2, depth, seg=6):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r1, radius2=r2, depth=depth)
    bmesh.ops.translate(bm, vec=(0, 0, depth / 2), verts=bm.verts)  # base at z=0, tip at +z
    return bm


def bm_loft(rings, seg, cap_bottom=True, cap_top=True, jitter=0.0, rot=0.0):
    """rings: (z, rx, ry[, ox, oy])"""
    bm = bmesh.new()
    first = prev = None
    for ring in rings:
        z, rx, ry = ring[0], ring[1], ring[2]
        ox = ring[3] if len(ring) > 3 else 0.0
        oy = ring[4] if len(ring) > 4 else 0.0
        cur = []
        for i in range(seg):
            a = rot + 2 * math.pi * i / seg
            cur.append(bm.verts.new((ox + math.cos(a) * rx + jit(jitter), oy + math.sin(a) * ry + jit(jitter), z + jit(jitter))))
        if prev:
            for i in range(seg):
                bm.faces.new((prev[i], prev[(i + 1) % seg], cur[(i + 1) % seg], cur[i]))
        else:
            first = cur
        prev = cur
    if cap_bottom:
        bm.faces.new(list(reversed(first)))
    if cap_top:
        bm.faces.new(prev)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def bm_torus(R, r, su=12, sv=6):
    bm = bmesh.new()
    grid = []
    for i in range(su):
        u = 2 * math.pi * i / su
        row = []
        for j in range(sv):
            v = 2 * math.pi * j / sv
            row.append(bm.verts.new(((R + r * math.cos(v)) * math.cos(u), (R + r * math.cos(v)) * math.sin(u), r * math.sin(v))))
        grid.append(row)
    for i in range(su):
        for j in range(sv):
            bm.faces.new((grid[i][j], grid[(i + 1) % su][j], grid[(i + 1) % su][(j + 1) % sv], grid[i][(j + 1) % sv]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def bm_ribbon(sections, width, fork=False):
    """sections: list of (point Vector, twist_deg). Ribbon spans along the polyline, width across local Z then twisted."""
    bm = bmesh.new()
    L, R = [], []
    n = len(sections)
    for k, (p, tw) in enumerate(sections):
        nxt = sections[min(k + 1, n - 1)][0]
        prv = sections[max(k - 1, 0)][0]
        t = (nxt - prv).normalized()
        side = Vector((0, 0, 1))
        side = (side - t * side.dot(t)).normalized()
        side.rotate(Matrix.Rotation(math.radians(tw), 3, t))
        L.append(bm.verts.new(p + side * width / 2))
        R.append(bm.verts.new(p - side * width / 2))
    for k in range(n - 1):
        bm.faces.new((L[k], L[k + 1], R[k + 1], R[k]))
    if fork:
        t = (sections[-1][0] - sections[-2][0]).normalized()
        mid = bm.verts.new(sections[-1][0] + t * 0.015)
        tl = bm.verts.new(L[-1].co + t * 0.07)
        tr = bm.verts.new(R[-1].co + t * 0.07)
        bm.faces.new((L[-1], tl, mid))
        bm.faces.new((mid, tr, R[-1]))
        bm.faces.new((L[-1], mid, R[-1]))
    return bm


def xform(bm, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    m = Matrix.Translation(loc) @ Matrix.Rotation(rot[2], 4, "Z") @ Matrix.Rotation(rot[1], 4, "Y") @ Matrix.Rotation(rot[0], 4, "X") @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return bm


def align_z_to(bm, direction, loc, roll=0.0):
    d = Vector(direction).normalized()
    q = Vector((0, 0, 1)).rotation_difference(d)
    m = Matrix.Translation(loc) @ q.to_matrix().to_4x4() @ Matrix.Rotation(roll, 4, "Z")
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)
    return bm


# ---------------------------------------------------------------- object helpers
def temp_obj(bm, name="tmp"):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    SCENE.collection.objects.link(o)
    return o


def shaped(bm, subsurf=0, decimate=None, remesh=None, solidify=None):
    """Run modifiers on a bmesh and return a baked temporary object."""
    o = temp_obj(bm)
    if remesh:
        m = o.modifiers.new("remesh", "REMESH")
        m.mode = "VOXEL"
        m.voxel_size = remesh
    if subsurf:
        m = o.modifiers.new("sub", "SUBSURF")
        m.levels = subsurf
        m.render_levels = subsurf
    if solidify:
        m = o.modifiers.new("sol", "SOLIDIFY")
        m.thickness = solidify
        m.offset = 0.0
    if decimate:
        m = o.modifiers.new("dec", "DECIMATE")
        m.ratio = min(1.0, decimate * DETAIL)
        m.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    old = o.data
    o.modifiers.clear()
    o.data = me
    bpy.data.meshes.remove(old)
    return o


class Part:
    """Accumulates geometry with per-source materials, then becomes one object."""

    def __init__(self):
        self.bm = bmesh.new()
        self.mats = []

    def slot(self, hexs):
        if hexs not in self.mats:
            self.mats.append(hexs)
        return self.mats.index(hexs)

    def add(self, src, hexes, random_tones=False):
        if isinstance(hexes, str):
            hexes = [hexes]
        slots = [self.slot(h) for h in hexes]
        if isinstance(src, bmesh.types.BMesh):
            o = temp_obj(src)
        else:
            o = src
        tmp = bmesh.new()
        tmp.from_mesh(o.data)
        for f in tmp.faces:
            f.material_index = slots[random.randrange(len(slots))] if random_tones else slots[min(f.material_index, len(slots) - 1)]
        me = bpy.data.meshes.new("merge")
        tmp.to_mesh(me)
        tmp.free()
        self.bm.from_mesh(me)
        bpy.data.meshes.remove(me)
        bpy.data.objects.remove(o)

    def make(self, name, parent, loc=(0, 0, 0), rot=(0, 0, 0)):
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        for h in self.mats:
            me.materials.append(mat(h))
        for p in me.polygons:
            p.use_smooth = False
        o = bpy.data.objects.new(name, me)
        SCENE.collection.objects.link(o)
        o.parent = parent
        o.location = loc
        o.rotation_euler = rot
        return o


def empty(name, parent, loc=(0, 0, 0)):
    o = bpy.data.objects.new(name, None)
    o.empty_display_size = 0.05
    SCENE.collection.objects.link(o)
    o.parent = parent
    o.location = loc
    return o


# ---------------------------------------------------------------- colours
GREEN, GREEN_D, SKIN, HAIR, HAIR_L, HAIR_D = "6F8F3A", "5F7E33", "F4D2B0", "E8802A", "F29A3C", "D46A20"
BELT, GOLD, BOOT, SCARF, SCARF_D, INK, BLUSH, WHITE = "E0662B", "D9A63A", "6B4128", "E8B93E", "D5A332", "2B1F1A", "EF9C8A", "FFFFFF"
FOX, FOX_D, CREAM, PAW = "E8782E", "D2632A", "F3E2C4", "4A2E22"


# ---------------------------------------------------------------- prince
def build_prince():
    root = empty("Prince", None)
    empty("_Front", root, (0, -0.5, 0.5))
    hips = empty("Hips", root, (0, 0, 0.44))

    # body: coat, belt, buttons, collar, knot  (pivot = hips)
    body = Part()
    hz = 0.44
    coat_rings = [(0.26, 0.19, 0.16), (0.34, 0.165, 0.14), (0.42, 0.142, 0.12), (0.5, 0.13, 0.107),
                  (0.6, 0.136, 0.112), (0.68, 0.122, 0.1), (0.73, 0.075, 0.068), (0.755, 0.045, 0.045)]
    coat = bm_loft([(z - hz, rx, ry) for z, rx, ry in coat_rings], 16, jitter=0.003)
    body.add(shaped(coat, subsurf=1, decimate=0.3), GREEN)
    belt = bm_loft([(0.462 - hz, 0.139, 0.116), (0.506 - hz, 0.137, 0.114)], 16)
    body.add(shaped(belt), BELT)
    body.add(xform(bm_cube(0.042, 0.014, 0.034), loc=(0, -0.117, 0.484 - hz)), GOLD)
    for z, y in ((0.665, -0.106), (0.605, -0.112), (0.545, -0.111), (0.43, -0.122)):
        body.add(xform(bm_ico(0.012, 1), loc=(0, y, z - hz)), GOLD)
    collar = xform(bm_torus(0.07, 0.034, 12, 6), loc=(0, 0.004, 0.735 - hz), scale=(1.12, 1.05, 1.0))
    body.add(shaped(collar, decimate=0.6), [SCARF, SCARF_D], random_tones=True)
    body.add(shaped(xform(bm_ico(0.038, 2), loc=(-0.04, 0.05, 0.72 - hz), scale=(1, 1, 0.8)), decimate=0.5), SCARF_D)
    body_obj = body.make("Body", hips)

    # legs (pivot = hip joints)
    for sx, name in ((1, "Leg.L"), (-1, "Leg.R")):
        leg = Part()
        trouser = bm_loft([(0.0, 0.072, 0.068), (-0.12, 0.075, 0.07), (-0.25, 0.08, 0.075), (-0.33, 0.09, 0.085), (-0.36, 0.088, 0.083)], 12, jitter=0.002)
        leg.add(shaped(trouser, subsurf=1, decimate=0.45), GREEN_D)
        boot = xform(bm_cube(1, 1, 1), scale=(0.1, 0.16, 0.075))
        leg.add(shaped(xform(boot, loc=(0, -0.02, -0.393)), subsurf=2, decimate=0.25), BOOT)
        leg.make(name, hips, loc=(sx * 0.068, 0, -0.01))

    # arms (pivot = shoulders)
    for sx, name in ((1, "Arm.L"), (-1, "Arm.R")):
        arm = Part()
        sleeve = bm_loft([(0.0, 0.05, 0.045), (-0.08, 0.052, 0.048), (-0.17, 0.06, 0.056), (-0.26, 0.088, 0.08), (-0.285, 0.082, 0.075)], 12, jitter=0.002)
        arm.add(shaped(sleeve, subsurf=1, decimate=0.4), GREEN)
        arm.add(shaped(xform(bm_ico(0.036, 2), loc=(0, 0, -0.3)), decimate=0.45), SKIN)
        arm.make(name, body_obj, loc=(sx * 0.125, 0, 0.69 - hz), rot=(0, -sx * math.radians(15), 0))

    # head (pivot = neck)
    head = Part()
    hc = Vector((0, 0, 0.11))
    head.add(shaped(bm_loft([(-0.05, 0.036, 0.036), (0.03, 0.034, 0.034)], 8)), SKIN)
    head.add(shaped(xform(bm_ico(0.132, 3), loc=hc, scale=(1.0, 0.96, 1.03)), decimate=0.14), SKIN)
    for sx in (1, -1):
        head.add(shaped(xform(bm_ico(0.03, 2), loc=(sx * 0.128, 0.0, 0.1)), decimate=0.5), SKIN)
        head.add(xform(bm_uv(0.025, 8, 6), loc=(sx * 0.048, -0.124, 0.106), scale=(0.75, 0.45, 1.25)), INK)
        head.add(xform(bm_uv(0.007, 6, 4), loc=(sx * 0.043, -0.134, 0.116)), WHITE)
        head.add(xform(bm_cube(0.038, 0.008, 0.008), loc=(sx * 0.05, -0.127, 0.15), rot=(0, sx * math.radians(12), 0)), HAIR_D)
        head.add(xform(bm_uv(0.027, 8, 6), loc=(sx * 0.08, -0.114, 0.068), scale=(1.1, 0.35, 0.55)), BLUSH)
    head.add(xform(bm_cube(0.026, 0.008, 0.007), loc=(0, -0.13, 0.048)), INK)

    # hair: locks + cap, fused with a voxel remesh, then decimated into facets
    hair = bmesh.new()
    tmp_me = bpy.data.meshes.new("hairtmp")

    def add_to_hair(bm_piece):
        bm_piece.to_mesh(tmp_me)
        bm_piece.free()
        hair.from_mesh(tmp_me)

    add_to_hair(xform(bm_ico(0.14, 2), loc=(0, 0.03, 0.172), scale=(1.03, 1.0, 0.8)))
    N = 42
    for i in range(N):
        z = 1 - (i + 0.5) / N * 2
        rr = math.sqrt(max(0.0, 1 - z * z))
        phi = i * 2.39996
        n = Vector((math.cos(phi) * rr, math.sin(phi) * rr, z))
        if n.z < -0.1:
            continue
        if n.y < -0.25 and n.z < 0.7:
            continue
        if n.z < 0.25 and n.y < 0.1:
            continue
        base = hc + Vector((n.x * 0.12, n.y * 0.12, n.z * 0.11))
        d = (n + Vector((0, 0.3, 0.45)) + Vector((jit(0.25), jit(0.25), jit(0.25)))).normalized()
        lock = bm_cone(0.05 + jit(0.008), 0.004, 0.13 + jit(0.03), 6)
        for v in lock.verts:
            t = v.co.z / 0.13
            v.co.x += 0.035 * t * t
        add_to_hair(align_z_to(lock, d, base, random.random() * 6.28))
    for k in range(6):
        t = -1 + k * 0.4
        base = Vector((t * 0.085, -0.095 + abs(t) * 0.03, 0.2 - abs(t) * 0.02))
        lock = bm_cone(0.04, 0.004, 0.06, 6)
        add_to_hair(align_z_to(lock, (t * 0.5, -0.9, -0.28), base, random.random() * 6.28))
    for sx in (1, -1):
        lock = bm_cone(0.04, 0.004, 0.085, 6)
        add_to_hair(align_z_to(lock, (sx * 0.4, 0.05, -1.0), Vector((sx * 0.122, -0.03, 0.115)), 0.3))
    bpy.data.meshes.remove(tmp_me)
    hair_obj = shaped(hair, remesh=0.011)
    faces = len(hair_obj.data.polygons)
    hair_obj = shaped_from_obj(hair_obj, decimate=min(1.0, 420.0 / max(1, faces)))  # scaled by DETAIL inside
    head.add(hair_obj, [HAIR, HAIR_L, HAIR_D], random_tones=True)
    head_obj = head.make("Head", body_obj, loc=(0, 0, 0.76 - hz))

    # scarf chain (pivots along the tail), streaming to the character's right and back
    seg_len = 0.2
    parent = body_obj
    loc = (-0.035, 0.07, 0.72 - hz)
    for k in range(3):
        part = Part()
        secs = [(Vector((0, 0, 0)), 0.0), (Vector((-seg_len * 0.5, 0.012, -0.006)), 22.0 if k % 2 == 0 else -18.0), (Vector((-seg_len, 0.02, -0.012)), -10.0 if k % 2 == 0 else 14.0)]
        rib = bm_ribbon(secs, 0.125 - k * 0.006, fork=(k == 2))
        part.add(shaped(rib, solidify=0.012), [SCARF, SCARF_D], random_tones=True)
        if k == 0:
            secs2 = [(Vector((0, 0.01, -0.03)), 10.0), (Vector((-0.09, 0.03, -0.06)), -20.0), (Vector((-0.17, 0.05, -0.08)), 15.0)]
            part.add(shaped(bm_ribbon(secs2, 0.1, fork=True), solidify=0.012), [SCARF_D, SCARF], random_tones=True)
        rot = (0, 0, math.radians(-28)) if k == 0 else (0, 0, math.radians(-6))
        parent = part.make("Scarf.%d" % (k + 1), parent, loc=loc, rot=rot)
        loc = (-seg_len, 0.02, -0.012)
    return root


def shaped_from_obj(o, decimate=None):
    if decimate:
        m = o.modifiers.new("dec", "DECIMATE")
        m.ratio = min(1.0, decimate * DETAIL)
        m.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    ev = o.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    old = o.data
    o.modifiers.clear()
    o.data = me
    bpy.data.meshes.remove(old)
    return o


# ---------------------------------------------------------------- fox
def build_fox():
    global DETAIL
    DETAIL = 3.6   # the fox is small and seen up close; give it more facets
    root = empty("Fox", None)
    empty("_Front", root, (0, -0.5, 0.5))
    bz = 0.2
    body = Part()
    torso = bmesh.new()
    tmp_me = bpy.data.meshes.new("foxtmp")
    for bmp in (xform(bm_ico(1.0, 3), loc=(0, 0.01, 0.02), scale=(0.095, 0.18, 0.09)),
                xform(bm_ico(1.0, 3), loc=(0, -0.11, 0.04), scale=(0.085, 0.09, 0.09))):
        bmp.to_mesh(tmp_me)
        bmp.free()
        torso.from_mesh(tmp_me)
    bpy.data.meshes.remove(tmp_me)
    body.add(shaped(torso, remesh=0.012, decimate=0.06), [FOX, FOX_D], random_tones=True)
    body.add(shaped(xform(bm_ico(1.0, 2), loc=(0, -0.12, -0.035), scale=(0.06, 0.09, 0.05)), decimate=0.4), CREAM)
    body_obj = body.make("Body", root, loc=(0, 0, bz))

    head = Part()
    head.add(shaped(xform(bm_ico(1.0, 3), loc=(0, -0.035, 0.03), scale=(0.08, 0.075, 0.07)), decimate=0.12), [FOX, FOX_D], random_tones=True)
    snout = xform(bm_cone(0.042, 0.008, 0.11, 6), rot=(math.radians(90), 0, 0), loc=(0, -0.085, 0.012))
    head.add(shaped(snout), FOX)
    head.add(shaped(xform(bm_ico(1.0, 2), loc=(0, -0.11, -0.012), scale=(0.03, 0.06, 0.018)), decimate=0.5), CREAM)
    head.add(xform(bm_uv(0.014, 8, 6), loc=(0, -0.196, 0.016)), INK)
    for sx in (1, -1):
        head.add(xform(bm_uv(0.012, 8, 6), loc=(sx * 0.038, -0.095, 0.05), scale=(0.8, 0.5, 1.1)), INK)
        ear = align_z_to(bm_cone(0.045, 0.004, 0.13, 4), (sx * 0.35, 0.1, 1.0), Vector((sx * 0.048, -0.015, 0.075)), math.radians(45))
        head.add(shaped(ear), FOX_D)
        inner = align_z_to(bm_cone(0.025, 0.003, 0.08, 4), (sx * 0.35, 0.1, 1.0), Vector((sx * 0.048, -0.03, 0.085)), math.radians(45))
        head.add(shaped(inner), PAW)
        head.add(shaped(xform(bm_ico(1.0, 1), loc=(sx * 0.058, -0.06, -0.022), scale=(0.035, 0.035, 0.028))), CREAM)
    head.make("Head", body_obj, loc=(0, -0.17, 0.09))

    tail = Part()
    tail.add(shaped(xform(bm_ico(1.0, 3), loc=(0, 0.14, 0.05), rot=(math.radians(25), 0, 0), scale=(0.065, 0.16, 0.07)), decimate=0.1), [FOX, FOX_D], random_tones=True)
    tail.add(shaped(xform(bm_ico(1.0, 2), loc=(0, 0.27, 0.11), rot=(math.radians(25), 0, 0), scale=(0.05, 0.07, 0.05)), decimate=0.3), CREAM)
    tail.make("Tail", body_obj, loc=(0, 0.17, 0.03))

    for name, x, y in (("Leg.FL", 0.05, -0.11), ("Leg.FR", -0.05, -0.11), ("Leg.BL", 0.055, 0.1), ("Leg.BR", -0.055, 0.1)):
        leg = Part()
        leg.add(shaped(bm_loft([(0.0, 0.026, 0.028), (-0.08, 0.021, 0.022), (-0.13, 0.017, 0.018)], 6)), FOX_D)
        leg.add(shaped(xform(bm_ico(1.0, 1), loc=(0, -0.012, -0.135), scale=(0.022, 0.03, 0.014))), PAW)
        leg.make(name, body_obj, loc=(x, y, -0.065))
    return root


# ---------------------------------------------------------------- export
def export(root, path):
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    stack = [root]
    while stack:
        o = stack.pop()
        o.select_set(True)
        stack.extend(o.children)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"EMPTY", "MESH"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=False, mesh_smooth_type="FACE", use_mesh_modifiers=True,
                             add_leaf_bones=False, bake_anim=False, use_custom_props=False)


def report_and_clear(root):
    faces = sum(len(c.data.polygons) for c in root.children_recursive if c.type == "MESH")
    print("B612_EXPORT", root.name, "faces=%d" % faces, "parts=" + ",".join(sorted(c.name for c in root.children_recursive)))
    for o in [root] + list(root.children_recursive):
        bpy.data.objects.remove(o, do_unlink=True)


os.makedirs(OUT, exist_ok=True)
prince = build_prince()
export(prince, os.path.join(OUT, "Prince.fbx"))
report_and_clear(prince)
fox = build_fox()
export(fox, os.path.join(OUT, "Fox.fbx"))
report_and_clear(fox)
