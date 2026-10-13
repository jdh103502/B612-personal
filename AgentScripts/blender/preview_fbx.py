# Renders a quick solid-colour preview of an FBX from a few angles (Workbench, material colours).
#   blender --background --factory-startup --python preview_fbx.py -- <file.fbx> <out_prefix>
import bpy
import math
import sys
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
path, out = args[0], args[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
for m in bpy.data.materials:
    k = m.name.find("M_")
    if k >= 0 and len(m.name) >= k + 8:
        h = m.name[k + 2:k + 8]
        try:
            m.diffuse_color = (int(h[0:2], 16) / 255, int(h[2:4], 16) / 255, int(h[4:6], 16) / 255, 1)
        except ValueError:
            pass
mins = Vector((1e9, 1e9, 1e9)); maxs = Vector((-1e9, -1e9, -1e9))
for o in meshes:
    for c in o.bound_box:
        w = o.matrix_world @ Vector(c)
        mins = Vector(map(min, mins, w)); maxs = Vector(map(max, maxs, w))
center = (mins + maxs) / 2
size = max(maxs - mins)
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "MATERIAL"
scene.display.shading.show_shadows = True
scene.display.shading.show_cavity = True
scene.render.resolution_x, scene.render.resolution_y = 900, 700
world = bpy.data.worlds.new("w"); scene.world = world
world.color = (0.93, 0.9, 0.84)
cam_data = bpy.data.cameras.new("cam"); cam_data.lens = 50
cam = bpy.data.objects.new("cam", cam_data); scene.collection.objects.link(cam); scene.camera = cam
for i, yaw in enumerate((-35, -90, 145)):
    a = math.radians(yaw)
    d = size * 2.3
    cam.location = center + Vector((math.sin(a) * d, -math.cos(a) * d, size * 0.55))
    direction = center - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = "%s_%d.png" % (out, i)
    bpy.ops.render.render(write_still=True)
print("PREVIEW size=%.3f center=%s" % (size, tuple(round(v, 3) for v in center)))
