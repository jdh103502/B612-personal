# Prints what is inside an FBX: objects, mesh sizes, armature bones and actions.
#   blender --background --factory-startup --python inspect_fbx.py -- <file.fbx>
import bpy
import sys

path = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)
print("== FILE", path)
for o in bpy.data.objects:
    line = "OBJ %s type=%s parent=%s dims=%s loc=%s rot=%s scale=%s" % (
        o.name, o.type, o.parent.name if o.parent else "-", tuple(round(v, 3) for v in o.dimensions),
        tuple(round(v, 3) for v in o.location), tuple(round(v, 3) for v in o.rotation_euler), tuple(round(v, 3) for v in o.scale))
    if o.type == "MESH":
        me = o.data
        line += " verts=%d faces=%d tris=%d uvs=%d mats=%s vgroups=%d" % (
            len(me.vertices), len(me.polygons), sum(len(p.vertices) - 2 for p in me.polygons), len(me.uv_layers),
            [m.name for m in me.materials if m], len(o.vertex_groups))
    print(line)
    if o.type == "ARMATURE":
        for b in o.data.bones:
            print("  BONE %s parent=%s head=%s" % (b.name, b.parent.name if b.parent else "-", tuple(round(v, 3) for v in b.head_local)))
for a in bpy.data.actions:
    print("ACTION %s frames=%s fcurves=%d" % (a.name, tuple(a.frame_range), len(a.fcurves) if hasattr(a, "fcurves") else -1))
print("FPS", bpy.context.scene.render.fps)
for img in bpy.data.images:
    print("IMAGE %s size=%s" % (img.name, tuple(img.size)))
