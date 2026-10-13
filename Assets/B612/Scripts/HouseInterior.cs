using System;
using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    /// <summary>
    /// While the prince is inside the house, its outer shell (walls, roof, pipes) fades to see-through and the
    /// camera stops colliding with the house, so the room reads like a dollhouse. The inner lining faces inward,
    /// so only the far walls show and the near ones drop out by themselves.
    /// </summary>
    public class HouseInterior : MonoBehaviour
    {
        [Serializable]
        public class ShellPart
        {
            public Renderer renderer;
            public Material[] opaque;
            public Material[] faded;
        }

        public List<ShellPart> shell = new List<ShellPart>();
        [Range(0f, 1f)] public float insideAlpha = 0.12f;
        public float fadeSeconds = 0.35f;
        [Tooltip("Layer the house is on; the camera ignores it while the prince is inside.")]
        public int houseLayer = 1;

        public static bool PlayerInside { get; private set; }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        MaterialPropertyBlock block;
        float fade;
        bool inside, usingFaded;

        void Awake() { block = new MaterialPropertyBlock(); }

        void OnTriggerEnter(Collider other) { if (other.GetComponentInParent<PlayerController>()) inside = true; }
        void OnTriggerExit(Collider other) { if (other.GetComponentInParent<PlayerController>()) inside = false; }

        void OnDisable()
        {
            PlayerInside = false;
            OrbitCamera.IgnoreMask = 0;
        }

        void Update()
        {
            PlayerInside = inside;
            OrbitCamera.IgnoreMask = inside ? 1 << houseLayer : 0;
            fade = Mathf.MoveTowards(fade, inside ? 1f : 0f, Time.deltaTime / Mathf.Max(fadeSeconds, 0.01f));

            bool wantFaded = fade > 0.001f;
            if (wantFaded != usingFaded)
            {
                usingFaded = wantFaded;
                foreach (var p in shell)
                {
                    if (!p.renderer) continue;
                    p.renderer.sharedMaterials = usingFaded ? p.faded : p.opaque;
                    if (!usingFaded) p.renderer.SetPropertyBlock(null);
                }
            }
            if (!usingFaded) return;

            float a = Mathf.Lerp(1f, insideAlpha, fade * fade * (3f - 2f * fade));
            foreach (var p in shell)
            {
                if (!p.renderer) continue;
                for (int i = 0; i < p.faded.Length; i++)
                {
                    var c = p.faded[i].GetColor(BaseColorId);
                    c.a = a;
                    block.Clear();
                    block.SetColor(BaseColorId, c);
                    // glowing parts (the window) dim with the rest, or they would hide the room
                    if (p.faded[i].IsKeywordEnabled("_EMISSION")) block.SetColor(EmissionId, p.faded[i].GetColor(EmissionId) * a);
                    p.renderer.SetPropertyBlock(block, i);
                }
            }
        }
    }
}
