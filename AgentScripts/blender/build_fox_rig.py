# Builds a smooth, rigged fox (one skinned mesh + armature) to stand beside the painted prince, and exports FoxRig.fbx.
#   blender --background --factory-startup --python build_fox_rig.py -- <output_dir>
# Blender axes: Z up, the fox faces -Y. Sizes are in metres (about 0.5 m to the ear tips).
import bpy
import bmesh
import math
import os
import sys
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else os.getcwd()
bpy.ops.wm.read_factory_settings(use_empty=True)

# ---------------------------------------------------------------- palette (material names carry the colour for Unity)
ORANGE, ORANGE_D, CREAM, DARK, EYE, WHITE = "#E07A33", "#C9652A", "#F6E7CF", "#4A3328", "#1E1612", "#FFFFFF"
MATS = {}


def mat(hexs):
    key = hexs.lstrip("#").upper()
    if key not in MATS:
        m = bpy.data.materials.new("M_" + key)
        c = [int(key[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
        m.diffuse_color = (c[0], c[1], c[2], 1.0)
        MATS[key] = m
    return MATS[key]


# ---------------------------------------------------------------- geometry helpers
def frame(t):
    hint = Vector((0, 0, 1)) if abs(t.z) < 0.92 else Vector((1, 0, 0))
    side = t.cross(hint).normalized()
    up = side.cross(t).normalized()
    return side, up


def loft(points, radii, seg=14, cap0=True, cap1=True, up_hint=None):
    """Tube through `points` with elliptical sections (rx across, rz 'up'); returns (bmesh, ring parameter per vert)."""
    bm = bmesh.new()
    n = len(points)
    rings = []
    params = {}
    total = sum((points[i + 1] - points[i]).length for i in range(n - 1)) or 1.0
    acc = 0.0
    for i, p in enumerate(points):
        if i > 0:
            acc += (points[i] - points[i - 1]).length
        t = (points[min(i + 1, n - 1)] - points[max(i - 1, 0)]).normalized()
        side, up = frame(t) if up_hint is None else (t.cross(up_hint).normalized(), t.cross(up_hint).normalized().cross(t).normalized())
        rx, rz = radii[i]
        ring = []
        for k in range(seg):
            a = 2 * math.pi * k / seg
            v = bm.verts.new(p + side * (math.cos(a) * rx) + up * (math.sin(a) * rz))
            params[v] = acc / total
            ring.append(v)
        rings.append(ring)
    for i in range(n - 1):
        for k in range(seg):
            a, b = rings[i][k], rings[i][(k + 1) % seg]
            c, d = rings[i + 1][(k + 1) % seg], rings[i + 1][k]
            bm.faces.new((a, b, c, d))
    if cap0:
        c0 = bm.verts.new(points[0] - (points[1] - points[0]).normalized() * 0.002)
        params[c0] = 0.0
        for k in range(seg):
            bm.faces.new((c0, rings[0][(k + 1) % seg], rings[0][k]))
    if cap1:
        c1 = bm.verts.new(points[-1] + (points[-1] - points[-2]).normalized() * 0.002)
        params[c1] = 1.0
        for k in range(seg):
            bm.faces.new((c1, rings[-1][k], rings[-1][(k + 1) % seg]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm, params


def ellipsoid(center, rx, ry, rz, sub=3):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=sub, radius=1.0)
    for v in bm.verts:
        v.co = Vector((center[0] + v.co.x * rx, center[1] + v.co.y * ry, center[2] + v.co.z * rz))
    return bm


PARTS = []   # (object, part kind, extra)


def path_param(co, pts):
    """0..1 position of the closest point on the polyline `pts` to `co`, by arc length."""
    total = sum((pts[i + 1] - pts[i]).length for i in range(len(pts) - 1)) or 1.0
    best, best_d, acc = 0.0, 1e9, 0.0
    for i in range(len(pts) - 1):
        a, b = pts[i], pts[i + 1]
        ab = b - a
        L = ab.length
        t = max(0.0, min(1.0, (co - a).dot(ab) / (L * L))) if L > 1e-9 else 0.0
        d = (a + ab * t - co).length
        if d < best_d:
            best_d, best = d, (acc + L * t) / total
        acc += L
    return best


def add_part(bm, kind, colour_rule, subsurf=1, path=None, extra=None):
    """Turn a bmesh into a smooth object. colour_rule(face_center, face_normal, param) -> hex, where param is the
    0..1 position along `path` (measured after subdivision by projecting each vertex onto the path)."""
    me = bpy.data.meshes.new(kind)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(kind, me)
    bpy.context.scene.collection.objects.link(o)
    if subsurf:
        m = o.modifiers.new("sub", "SUBSURF")
        m.levels = subsurf
        dg = bpy.context.evaluated_depsgraph_get()
        me2 = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
        o.modifiers.clear()
        old = o.data
        o.data = me2
        bpy.data.meshes.remove(old)
    me = o.data
    params = [path_param(v.co, path) for v in me.vertices] if path else [0.0] * len(me.vertices)
    for poly in me.polygons:
        p = sum(params[vi] for vi in poly.vertices) / len(poly.vertices)
        m = mat(colour_rule(poly.center, poly.normal, p))
        names = [x.name for x in me.materials]
        if m.name not in names:
            me.materials.append(m)
            names.append(m.name)
        poly.material_index = names.index(m.name)
        poly.use_smooth = True
    PARTS.append((o, kind, extra, params))
    return o

def V(x, y, z):
    return Vector((x, y, z))


# ---------------------------------------------------------------- body
# a compact, storybook fox: big head and ears, short sturdy body, short legs, a large fluffy tail
torso_pts = [V(0, 0.13, 0.215), V(0, 0.10, 0.22), V(0, 0.04, 0.215), V(0, -0.03, 0.215), V(0, -0.09, 0.23), V(0, -0.13, 0.25), V(0, -0.155, 0.27)]
torso_r = [(0.045, 0.048), (0.075, 0.075), (0.08, 0.075), (0.074, 0.07), (0.08, 0.08), (0.074, 0.082), (0.05, 0.06)]
bm, prm = loft(torso_pts, torso_r, seg=16)


def torso_colour(c, n, p):
    if n.z < -0.35 and c.y > -0.1:
        return CREAM                          # belly
    return ORANGE


add_part(bm, "torso", torso_colour, path=torso_pts)
bm = ellipsoid(V(0, -0.15, 0.235), 0.062, 0.05, 0.072, sub=3)
add_part(bm, "bib", lambda c, n, p: CREAM)

neck_pts = [V(0, -0.13, 0.26), V(0, -0.16, 0.31), V(0, -0.18, 0.35)]
neck_r = [(0.055, 0.06), (0.05, 0.052), (0.046, 0.047)]
bm, prm = loft(neck_pts, neck_r, seg=14)
add_part(bm, "neck", lambda c, n, p: CREAM if (n.y < -0.3 and n.z < 0.3) else ORANGE, path=neck_pts)

HEAD_C = V(0, -0.195, 0.38)
bm = ellipsoid(HEAD_C, 0.085, 0.078, 0.072, sub=3)
add_part(bm, "skull", lambda c, n, p: CREAM if (n.z < -0.4 or (n.y < -0.55 and c.z < HEAD_C.z - 0.015)) else ORANGE)
snout_pts = [V(0, -0.235, 0.37), V(0, -0.27, 0.362), V(0, -0.295, 0.356), V(0, -0.31, 0.354)]
snout_r = [(0.05, 0.042), (0.035, 0.03), (0.02, 0.019), (0.008, 0.008)]
bm, prm = loft(snout_pts, snout_r, seg=12)
add_part(bm, "snout", lambda c, n, p: CREAM if n.z < 0.1 else ORANGE, path=snout_pts)
bm = ellipsoid(V(0, -0.315, 0.358), 0.015, 0.013, 0.012, sub=2)
add_part(bm, "nose", lambda c, n, p: DARK, subsurf=0)
for sx in (1, -1):
    bm = ellipsoid(V(sx * 0.04, -0.255, 0.397), 0.014, 0.01, 0.016, sub=2)
    add_part(bm, "eye", lambda c, n, p: EYE, subsurf=0)
    bm = ellipsoid(V(sx * 0.043, -0.264, 0.405), 0.004, 0.003, 0.004, sub=1)
    add_part(bm, "glint", lambda c, n, p: WHITE, subsurf=0)
    ruff_pts = [V(sx * 0.06, -0.2, 0.352), V(sx * 0.085, -0.19, 0.334), V(sx * 0.1, -0.178, 0.318)]
    bm, prm = loft(ruff_pts, [(0.028, 0.036), (0.017, 0.021), (0.002, 0.003)], seg=10)
    add_part(bm, "ruff", lambda c, n, p: CREAM, path=ruff_pts)
    ear_pts = [V(sx * 0.045, -0.18, 0.43), V(sx * 0.06, -0.178, 0.49), V(sx * 0.072, -0.176, 0.54), V(sx * 0.08, -0.175, 0.57)]
    bm, prm = loft(ear_pts, [(0.042, 0.016), (0.033, 0.012), (0.018, 0.008), (0.002, 0.002)], seg=10, up_hint=V(0, -1, 0))
    add_part(bm, "ear", lambda c, n, p: DARK if p > 0.7 else (CREAM if n.y < -0.4 else ORANGE), path=ear_pts, extra=sx)

tail_pts = [V(0, 0.13, 0.22), V(0, 0.18, 0.235), V(0, 0.24, 0.26), V(0, 0.29, 0.3), V(0, 0.32, 0.35), V(0, 0.335, 0.4), V(0, 0.335, 0.44)]
tail_r = [(0.03, 0.03), (0.055, 0.055), (0.075, 0.074), (0.08, 0.078), (0.072, 0.07), (0.05, 0.048), (0.008, 0.008)]
bm, prm = loft(tail_pts, tail_r, seg=14)
add_part(bm, "tail", lambda c, n, p: CREAM if p > 0.76 else (ORANGE_D if n.z < -0.6 and p < 0.5 else ORANGE), path=tail_pts)

LEGS = {
    "FL": (0.045, -0.105, True), "FR": (-0.045, -0.105, True),
    "BL": (0.048, 0.095, False), "BR": (-0.048, 0.095, False),
}
KNEE = {True: 0.11, False: 0.10}
for key, (x, y, front) in LEGS.items():
    if front:
        pts = [V(x, y, 0.23), V(x, y - 0.002, 0.16), V(x, y - 0.002, 0.09), V(x, y - 0.004, 0.035), V(x, y - 0.012, 0.01)]
        rad = [(0.032, 0.035), (0.026, 0.028), (0.02, 0.021), (0.018, 0.019), (0.022, 0.016)]
    else:
        pts = [V(x, y, 0.23), V(x, y + 0.02, 0.16), V(x, y + 0.025, 0.095), V(x, y + 0.01, 0.04), V(x, y + 0.005, 0.01)]
        rad = [(0.042, 0.05), (0.032, 0.036), (0.021, 0.022), (0.018, 0.019), (0.022, 0.016)]
    bm, prm = loft(pts, rad, seg=12)
    add_part(bm, "leg", lambda c, n, p: DARK if c.z < 0.085 else ORANGE, path=pts, extra=key)

# ---------------------------------------------------------------- armature
arm_data = bpy.data.armatures.new("FoxArmature")
arm = bpy.data.objects.new("Fox", arm_data)
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb = arm_data.edit_bones


def bone(name, head, tail, parent=None, connect=False):
    b = eb.new(name)
    b.head, b.tail = head, tail
    if parent:
        b.parent = eb[parent]
        b.use_connect = connect
    return b


bone("Root", V(0, 0, 0), V(0, 0, 0.08))
bone("Hips", V(0, 0.12, 0.22), V(0, 0.03, 0.215), "Root")
bone("Spine", V(0, 0.03, 0.215), V(0, -0.07, 0.22), "Hips", True)
bone("Chest", V(0, -0.07, 0.22), V(0, -0.13, 0.26), "Spine", True)
bone("Neck", V(0, -0.13, 0.26), V(0, -0.18, 0.35), "Chest", True)
bone("Head", V(0, -0.18, 0.35), V(0, -0.26, 0.40), "Neck", True)
for sx, s in ((1, "L"), (-1, "R")):
    bone("Ear." + s, V(sx * 0.045, -0.18, 0.43), V(sx * 0.08, -0.175, 0.57), "Head")
prev = "Hips"
tail_joints = [V(0, 0.13, 0.22), V(0, 0.21, 0.245), V(0, 0.28, 0.29), V(0, 0.325, 0.36), V(0, 0.335, 0.44)]
for i in range(4):
    bone("Tail.%d" % (i + 1), tail_joints[i], tail_joints[i + 1], prev, i > 0)
    prev = "Tail.%d" % (i + 1)
for key, (x, y, front) in LEGS.items():
    parent = "Chest" if front else "Hips"
    if front:
        top, knee, foot = V(x, y, 0.23), V(x, y - 0.002, KNEE[True]), V(x, y - 0.008, 0.015)
    else:
        top, knee, foot = V(x, y, 0.23), V(x, y + 0.025, KNEE[False]), V(x, y + 0.006, 0.015)
    bone("Leg.%s.1" % key, top, knee, parent)
    bone("Leg.%s.2" % key, knee, foot, "Leg.%s.1" % key, True)
bpy.ops.object.mode_set(mode="OBJECT")
# front marker for orientation in Unity
front_marker = bpy.data.objects.new("_Front", None)
front_marker.location = V(0, -0.6, 0.25)
front_marker.parent = arm
bpy.context.scene.collection.objects.link(front_marker)

# ---------------------------------------------------------------- weights
def smooth(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def chain_weights(param, names):
    """Spread weight along a chain of bones by a 0..1 parameter."""
    n = len(names)
    x = param * (n - 1)
    i = min(int(x), n - 2) if n > 1 else 0
    t = x - i if n > 1 else 0.0
    if n == 1:
        return {names[0]: 1.0}
    t = t * t * (3 - 2 * t)
    return {names[i]: 1.0 - t, names[i + 1]: t}


def weights_for(kind, co, param, extra):
    if kind == "torso":
        # rump -> hips, middle -> spine, front -> chest (by y)
        y = co.y
        w = {"Hips": smooth(-0.02, 0.09, y), "Chest": smooth(-0.04, -0.11, y)}
        w["Spine"] = max(0.0, 1.0 - w["Hips"] - w["Chest"])
        return w
    if kind == "neck":
        return chain_weights(param, ["Chest", "Neck", "Head"])
    if kind == "bib":
        return {"Chest": 0.8, "Neck": 0.2}
    if kind in ("skull", "snout", "nose", "eye", "glint", "ruff"):
        return {"Head": 1.0}
    if kind == "ear":
        e = "Ear.L" if extra == 1 else "Ear.R"
        k = smooth(0.0, 0.35, param)
        return {"Head": 1.0 - k, e: k}
    if kind == "tail":
        w = chain_weights(param, ["Tail.1", "Tail.2", "Tail.3", "Tail.4"])
        if param < 0.08:
            k = smooth(0.0, 0.08, param)
            w = {b: v * k for b, v in w.items()}
            w["Hips"] = 1.0 - k
        return w
    if kind == "leg":
        upper, lower = "Leg.%s.1" % extra, "Leg.%s.2" % extra
        body = "Chest" if extra in ("FL", "FR") else "Hips"
        z = co.z
        knee = KNEE[extra in ("FL", "FR")]
        kl = smooth(knee + 0.03, knee - 0.02, z)          # lower leg below the knee
        kb = smooth(0.19, 0.235, z)                        # top of the leg melts into the body
        return {lower: kl, upper: (1.0 - kl) * (1.0 - kb), body: (1.0 - kl) * kb}
    return {"Hips": 1.0}


bone_names = [b.name for b in arm_data.bones]
for o, kind, extra, params in PARTS:
    me = o.data
    groups = {n: o.vertex_groups.new(name=n) for n in bone_names}
    for v in me.vertices:
        p = params[v.index]
        w = weights_for(kind, v.co, p, extra)
        tot = sum(w.values()) or 1.0
        for name, val in w.items():
            if val > 1e-4:
                groups[name].add([v.index], val / tot, "REPLACE")

# join everything into one skinned mesh
bpy.ops.object.select_all(action="DESELECT")
for o, _, _, _ in PARTS:
    o.select_set(True)
bpy.context.view_layer.objects.active = PARTS[0][0]
bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
body.name = "FoxBody"
for poly in body.data.polygons:
    poly.use_smooth = True
body.parent = arm
mod = body.modifiers.new("Armature", "ARMATURE")
mod.object = arm

print("B612_FOX faces=%d verts=%d bones=%d mats=%s" % (len(body.data.polygons), len(body.data.vertices), len(arm_data.bones), [m.name for m in body.data.materials]))

path = os.path.join(OUT, "FoxRig.fbx")
bpy.ops.export_scene.fbx(
    filepath=path, use_selection=False, object_types={"ARMATURE", "MESH", "EMPTY"},
    apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", bake_space_transform=False,
    mesh_smooth_type="OFF", add_leaf_bones=False, bake_anim=False, primary_bone_axis="Y", secondary_bone_axis="X",
    armature_nodetype="NULL")
print("B612_FOX exported", path)
