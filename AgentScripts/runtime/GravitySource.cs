using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    /// <summary>Spherical gravity field. A walker falls toward the source whose surface is nearest.</summary>
    public class GravitySource : MonoBehaviour
    {
        public float surfaceRadius = 2f;
        public float influenceRadius = 6f;
        public float strength = 3.4f;

        static readonly List<GravitySource> active = new List<GravitySource>();

        void OnEnable() { active.Add(this); }
        void OnDisable() { active.Remove(this); }

        public static GravitySource Find(Vector3 p)
        {
            GravitySource best = null;
            float bestSurface = float.MaxValue;
            for (int i = 0; i < active.Count; i++)
            {
                var s = active[i];
                float d = Vector3.Distance(p, s.transform.position);
                if (d > s.influenceRadius) continue;
                float surface = d - s.surfaceRadius;
                if (surface < bestSurface) { bestSurface = surface; best = s; }
            }
            return best;
        }

        public Vector3 Pull(Vector3 p)
        {
            var d = transform.position - p;
            return d.sqrMagnitude < 1e-6f ? Vector3.zero : d.normalized * strength;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, influenceRadius);
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, surfaceRadius);
        }
    }
}
