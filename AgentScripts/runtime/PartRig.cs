using UnityEngine;

namespace B612
{
    /// <summary>Rest pose of one model part, so procedural animation can rotate it around axes of the model root.</summary>
    public class PartRig
    {
        public readonly Transform t;
        readonly Quaternion restLocal;
        readonly Vector3 restPos;
        readonly Quaternion parentInRoot;

        PartRig(Transform part, Transform root)
        {
            t = part;
            restLocal = part.localRotation;
            restPos = part.localPosition;
            parentInRoot = Quaternion.Inverse(root.rotation) * part.parent.rotation;
        }

        public static PartRig Find(Transform root, string name)
        {
            var p = FindDeep(root, name);
            return p ? new PartRig(p, root) : null;
        }

        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), n);
                if (r) return r;
            }
            return null;
        }

        /// <summary>Rotate from rest by angles (degrees) around the root's X (pitch), Y (yaw) and Z (roll) axes.</summary>
        public void Pose(float pitch, float yaw = 0f, float roll = 0f)
        {
            var inv = Quaternion.Inverse(parentInRoot);
            var q = Quaternion.AngleAxis(yaw, inv * Vector3.up) * Quaternion.AngleAxis(pitch, inv * Vector3.right) * Quaternion.AngleAxis(roll, inv * Vector3.forward);
            t.localRotation = q * restLocal;
        }

        public void Offset(Vector3 rootSpaceOffset)
        {
            t.localPosition = restPos + Quaternion.Inverse(parentInRoot) * rootSpaceOffset;
        }
    }
}
