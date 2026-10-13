# Prints the Hips world position over an FBX animation, to see whether it walks in place.
#   blender --background --factory-startup --python inspect_motion.py -- <file.fbx>
import bpy
import sys

path = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
act = arm.animation_data.action if arm.animation_data else None
f0, f1 = (int(act.frame_range[0]), int(act.frame_range[1])) if act else (1, 1)
scene = bpy.context.scene
for f in range(f0, f1 + 1, max(1, (f1 - f0) // 8)):
    scene.frame_set(f)
    hips = arm.pose.bones["Hips"]
    lf = arm.pose.bones["LeftFoot"]
    rf = arm.pose.bones["RightFoot"]
    w = arm.matrix_world
    print("FRAME %d hips=%s lfoot=%s rfoot=%s" % (f, tuple(round(v, 3) for v in (w @ hips.head)), tuple(round(v, 3) for v in (w @ lf.head)), tuple(round(v, 3) for v in (w @ rf.head))))
