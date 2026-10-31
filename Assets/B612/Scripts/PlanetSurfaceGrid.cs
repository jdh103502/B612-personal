using System;
using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    /// <summary>Logical spherical cells and surface slots. Never changes the planet mesh.</summary>
    public sealed class PlanetSurfaceGrid : MonoBehaviour
    {
        public Collider ground;
        [Range(0, 2)] public int subdivisions = 1;
        [Range(4, 96)] public int candidatesPerCell = 32;
        [Range(0f, 89f)] public float maxSlope = 55f;
        public bool drawCellGizmos = true;

        public sealed class SurfaceCell
        {
            public int id;
            public Vector3 a, b, c;
            public float solidAngle;
            public int[] neighbors = new int[3];
            public readonly List<SpawnSlot> slots = new List<SpawnSlot>();
        }

        public sealed class SpawnSlot
        {
            public int cellId;
            public Vector3 localPoint, localNormal;
            public bool occupied;
        }

        readonly List<SurfaceCell> cells = new List<SurfaceCell>();
        public IReadOnlyList<SurfaceCell> Cells => cells;
        public int SlotCount { get; private set; }
        public int EmptyCellCount { get; private set; }

        public void Prepare(int seed)
        {
            if (!ground) throw new InvalidOperationException("Assign this planet's ground collider.");
            BuildCells();
            var random = new System.Random(seed);
            SlotCount = EmptyCellCount = 0;
            foreach (var cell in cells)
            {
                // Area-uniform samples within each spherical triangle: reject uniform sphere samples
                // inside a small circumscribed cap. Unlike normalized planar barycentrics, this has
                // no vertex/centre density bias. Attempts are bounded even on unsuitable ground.
                var centre = (cell.a + cell.b + cell.c).normalized;
                var right = Vector3.Cross(centre, Mathf.Abs(centre.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                var forward = Vector3.Cross(centre, right);
                float cosCap = Mathf.Min(Vector3.Dot(centre, cell.a), Vector3.Dot(centre, cell.b), Vector3.Dot(centre, cell.c));
                for (int attempt = 0; attempt < candidatesPerCell * 16 && cell.slots.Count < candidatesPerCell; attempt++)
                {
                    float z = Mathf.Lerp(cosCap, 1f, (float)random.NextDouble());
                    float phi = (float)random.NextDouble() * Mathf.PI * 2f;
                    float ring = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
                    Vector3 direction = centre * z + ring * (right * Mathf.Cos(phi) + forward * Mathf.Sin(phi));
                    if (!Contains(cell, direction) || !TryProject(direction, out var point, out var normal)) continue;
                    var radial = (point - transform.position).normalized;
                    if (Vector3.Angle(normal, radial) > maxSlope) continue;
                    cell.slots.Add(new SpawnSlot
                    {
                        cellId = cell.id,
                        localPoint = transform.InverseTransformPoint(point),
                        localNormal = transform.localToWorldMatrix.transpose.MultiplyVector(normal).normalized
                    });
                }
                SlotCount += cell.slots.Count;
                if (cell.slots.Count == 0) EmptyCellCount++;
            }
        }

        public Vector3 WorldPoint(SpawnSlot slot) => transform.TransformPoint(slot.localPoint);
        public Vector3 WorldNormal(SpawnSlot slot) => transform.worldToLocalMatrix.transpose.MultiplyVector(slot.localNormal).normalized;

        public bool TryProject(Vector3 localDirection, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            if (!ground) return false;
            Vector3 direction = transform.TransformVector(localDirection).normalized;
            // Bounds, not GravitySource.surfaceRadius: the latter is only a gravity-selection hint.
            float outer = ground.bounds.extents.magnitude + Vector3.Distance(ground.bounds.center, transform.position) + 1f;
            if (!ground.Raycast(new Ray(transform.position + direction * outer, -direction), out var hit, outer * 2f)) return false;
            point = hit.point;
            normal = hit.normal;
            return true;
        }

        public static bool Contains(SurfaceCell cell, Vector3 direction)
        {
            return Vector3.Dot(Vector3.Cross(cell.a, cell.b), direction) >= -0.00001f
                && Vector3.Dot(Vector3.Cross(cell.b, cell.c), direction) >= -0.00001f
                && Vector3.Dot(Vector3.Cross(cell.c, cell.a), direction) >= -0.00001f;
        }

        void BuildCells()
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var vertices = new List<Vector3>
            {
                new Vector3(-1,t,0), new Vector3(1,t,0), new Vector3(-1,-t,0), new Vector3(1,-t,0),
                new Vector3(0,-1,t), new Vector3(0,1,t), new Vector3(0,-1,-t), new Vector3(0,1,-t),
                new Vector3(t,0,-1), new Vector3(t,0,1), new Vector3(-t,0,-1), new Vector3(-t,0,1)
            };
            for (int i = 0; i < vertices.Count; i++) vertices[i] = vertices[i].normalized;
            var faces = new List<int[]>
            {
                new[]{0,11,5}, new[]{0,5,1}, new[]{0,1,7}, new[]{0,7,10}, new[]{0,10,11},
                new[]{1,5,9}, new[]{5,11,4}, new[]{11,10,2}, new[]{10,7,6}, new[]{7,1,8},
                new[]{3,9,4}, new[]{3,4,2}, new[]{3,2,6}, new[]{3,6,8}, new[]{3,8,9},
                new[]{4,9,5}, new[]{2,4,11}, new[]{6,2,10}, new[]{8,6,7}, new[]{9,8,1}
            };
            var midpoints = new Dictionary<long, int>();
            for (int level = 0; level < Mathf.Clamp(subdivisions, 0, 2); level++)
            {
                var next = new List<int[]>(faces.Count * 4);
                foreach (var face in faces)
                {
                    int a = Midpoint(face[0], face[1], vertices, midpoints);
                    int b = Midpoint(face[1], face[2], vertices, midpoints);
                    int c = Midpoint(face[2], face[0], vertices, midpoints);
                    next.Add(new[] { face[0], a, c }); next.Add(new[] { face[1], b, a });
                    next.Add(new[] { face[2], c, b }); next.Add(new[] { a, b, c });
                }
                faces = next;
            }
            cells.Clear();
            var edges = new Dictionary<long, KeyValuePair<int, int>>();
            foreach (var face in faces)
            {
                var a = vertices[face[0]]; var b = vertices[face[1]]; var c = vertices[face[2]];
                var cell = new SurfaceCell { id = cells.Count, a = a, b = b, c = c,
                    solidAngle = 2f * Mathf.Atan2(Mathf.Abs(Vector3.Dot(a, Vector3.Cross(b,c))), 1f + Vector3.Dot(a,b) + Vector3.Dot(b,c) + Vector3.Dot(c,a)) };
                cells.Add(cell);
                for (int side = 0; side < 3; side++)
                {
                    long key = EdgeKey(face[side], face[(side + 1) % 3]);
                    if (edges.TryGetValue(key, out var other))
                    {
                        cell.neighbors[side] = other.Key;
                        cells[other.Key].neighbors[other.Value] = cell.id;
                    }
                    else edges.Add(key, new KeyValuePair<int, int>(cell.id, side));
                }
            }
        }

        static long EdgeKey(int a, int b) => ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);

        static int Midpoint(int a, int b, List<Vector3> vertices, Dictionary<long, int> cache)
        {
            long key = EdgeKey(a, b);
            if (cache.TryGetValue(key, out int index)) return index;
            index = vertices.Count;
            vertices.Add((vertices[a] + vertices[b]).normalized);
            cache.Add(key, index);
            return index;
        }

        void OnDrawGizmosSelected()
        {
            if (!drawCellGizmos || !ground) return;
            if (cells.Count != 20 * (1 << (2 * subdivisions))) BuildCells();
            foreach (var cell in cells)
            {
                Gizmos.color = new Color(0.5f, 0.95f, 0.85f, 0.65f);
                DrawEdge(cell.a, cell.b); DrawEdge(cell.b, cell.c); DrawEdge(cell.c, cell.a);
                foreach (var slot in cell.slots)
                    if (slot.occupied) { Gizmos.color = Color.yellow; Gizmos.DrawSphere(WorldPoint(slot), 0.055f); }
            }
        }

        void DrawEdge(Vector3 a, Vector3 b)
        {
            Vector3 previous = Vector3.zero;
            bool hasPrevious = false;
            for (int i = 0; i <= 6; i++)
            {
                if (!TryProject(Vector3.Slerp(a, b, i / 6f), out var p, out var n)) { hasPrevious = false; continue; }
                p += n * 0.02f;
                if (hasPrevious) Gizmos.DrawLine(previous, p);
                previous = p; hasPrevious = true;
            }
        }
    }
}
