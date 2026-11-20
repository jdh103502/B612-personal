using System;
using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    /// <summary>Logical spherical cells and surface slots. Never changes the planet mesh.</summary>
    public sealed class PlanetSurfaceGrid : MonoBehaviour
    {
        public Collider ground;
        [Tooltip("Subdivision level of the 1x baseline. Area scaling uses ceil(2^level * sizeScale); up to 1,280 cells at level 2.")]
        [Range(0, 2)] public int subdivisions = 1;
        [Tooltip("PlanetSpec sizeScale, independent of this transform. Applied only on explicit Prepare; never by Gizmos.")]
        [SerializeField, Range(.7f, 2f)] float sizeScale = 1f;
        [Range(4, 96)] public int candidatesPerCell = 32;
        [Range(0f, 89f)] public float maxSlope = 55f;
        public bool drawCellGizmos = true;
        [NonSerialized] public Func<Vector3,Vector3,bool> CandidateFilter;

        public sealed class SurfaceCell
        {
            public int id;
            public Vector3 a, b, c;
            public float solidAngle;
            public int[] neighbors = { -1, -1, -1 };
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
        public float SizeScale => sizeScale;
        public int PreparedFrequency { get; private set; }
        public int PreparedVertexCount { get; private set; }
        public int PreparedEdgeCount { get; private set; }
        readonly List<SurfaceCell> previewCells = new List<SurfaceCell>();
        int previewFrequency;

        public static int CalculateFrequency(int baselineSubdivisions, float planetSizeScale)
        {
            if (baselineSubdivisions < 0 || baselineSubdivisions > 2)
                throw new ArgumentOutOfRangeException(nameof(baselineSubdivisions), "Supported baseline levels are 0 through 2.");
            PlanetGenerationSettings.Classify(planetSizeScale); // Shared finite/range validation: 0.7 through 2.0.
            // Exact float boundaries are intentional: the next representable scale above 1/1.5 advances a band.
            return Mathf.CeilToInt((1 << baselineSubdivisions) * planetSizeScale);
        }
        public void ConfigureResolution(int baselineSubdivisions, float planetSizeScale)
        {
            CalculateFrequency(baselineSubdivisions, planetSizeScale);
            subdivisions = baselineSubdivisions; sizeScale = planetSizeScale;
        }

        public void Prepare(int seed)
        {
            if (!ground) throw new InvalidOperationException("Assign this planet's ground collider.");
            int frequency = CalculateFrequency(subdivisions, sizeScale);
            BuildCells(frequency, cells, out int vertexCount, out int edgeCount);
            PreparedFrequency = frequency; PreparedVertexCount = vertexCount; PreparedEdgeCount = edgeCount;
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
                    if (CandidateFilter!=null && !CandidateFilter(point,normal)) continue;
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

        public static List<SurfaceCell> CreateTopology(int frequency)
        {var result=new List<SurfaceCell>();BuildCells(frequency,result,out _,out _);return result;}
        static void BuildCells(int frequency, List<SurfaceCell> result, out int vertexCount, out int edgeCount)
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
            if ((frequency & (frequency - 1)) != 0)
                faces = FrequencyFaces(frequency, vertices, faces);
            else
            {
                // Keep the original recursive order and float operations for powers of two, including 1x.
                var midpoints = new Dictionary<long, int>(MixedLongComparer.Instance);
                for (int resolution = 1; resolution < frequency; resolution *= 2)
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
            }
            result.Clear();
            var edges = new Dictionary<long, KeyValuePair<int, int>>(faces.Count * 3 / 2 + 1, MixedLongComparer.Instance);
            foreach (var face in faces)
            {
                var a = vertices[face[0]]; var b = vertices[face[1]]; var c = vertices[face[2]];
                var cell = new SurfaceCell { id = result.Count, a = a, b = b, c = c,
                    solidAngle = 2f * Mathf.Atan2(Mathf.Abs(Vector3.Dot(a, Vector3.Cross(b,c))), 1f + Vector3.Dot(a,b) + Vector3.Dot(b,c) + Vector3.Dot(c,a)) };
                result.Add(cell);
                for (int side = 0; side < 3; side++)
                {
                    long key = EdgeKey(face[side], face[(side + 1) % 3]);
                    if (edges.TryGetValue(key, out var other))
                    {
                        cell.neighbors[side] = other.Key;
                        if (result[other.Key].neighbors[other.Value] >= 0) throw new InvalidOperationException("Non-manifold planet cell edge.");
                        result[other.Key].neighbors[other.Value] = cell.id;
                    }
                    else edges.Add(key, new KeyValuePair<int, int>(cell.id, side));
                }
            }
            foreach (var cell in result)
                foreach (int neighbor in cell.neighbors)
                    if (neighbor < 0) throw new InvalidOperationException("Open planet cell edge.");
            vertexCount = vertices.Count; edgeCount = edges.Count;
        }

        static List<int[]> FrequencyFaces(int frequency, List<Vector3> vertices, List<int[]> originalFaces)
        {
            var faces = new List<int[]>(20 * frequency * frequency);
            var edgeVertices = new Dictionary<long, int>(MixedLongComparer.Instance);
            foreach (var face in originalFaces)
            {
                var grid = new int[frequency + 1, frequency + 1];
                for (int i = 0; i <= frequency; i++)
                    for (int j = 0; j <= frequency - i; j++)
                    {
                        int a = frequency - i - j, b = i, c = j;
                        int vertex;
                        if (a == frequency) vertex = face[0];
                        else if (b == frequency) vertex = face[1];
                        else if (c == frequency) vertex = face[2];
                        else if (c == 0) vertex = EdgeVertex(face[0], face[1], b, frequency, vertices, edgeVertices);
                        else if (b == 0) vertex = EdgeVertex(face[0], face[2], c, frequency, vertices, edgeVertices);
                        else if (a == 0) vertex = EdgeVertex(face[1], face[2], c, frequency, vertices, edgeVertices);
                        else
                        {
                            vertex = vertices.Count;
                            vertices.Add((a * vertices[face[0]] + b * vertices[face[1]] + c * vertices[face[2]]).normalized);
                        }
                        grid[i, j] = vertex;
                    }
                for (int i = 0; i < frequency; i++)
                    for (int j = 0; j < frequency - i; j++)
                    {
                        faces.Add(new[] { grid[i, j], grid[i + 1, j], grid[i, j + 1] });
                        if (i + j < frequency - 1) faces.Add(new[] { grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1] });
                    }
            }
            return faces;
        }

        static int EdgeVertex(int from, int to, int step, int frequency, List<Vector3> vertices, Dictionary<long, int> cache)
        {
            int low = Mathf.Min(from, to), high = Mathf.Max(from, to);
            int canonicalStep = from == low ? step : frequency - step;
            long key = ((long)(low * 12 + high) << 32) | (uint)canonicalStep; // Original icosahedron endpoint IDs and subdivision step.
            if (cache.TryGetValue(key, out int existing)) return existing;
            int index = vertices.Count;
            vertices.Add(((frequency - canonicalStep) * vertices[low] + canonicalStep * vertices[high]).normalized);
            cache.Add(key, index); return index;
        }

        static long EdgeKey(int a, int b) => ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);

        /// <summary>
        /// long.GetHashCode XORs the two halves, so packed (low, high) keys of nearby vertices
        /// collide heavily. Mixing the bits keeps lookups O(1) without changing key equality.
        /// </summary>
        sealed class MixedLongComparer : IEqualityComparer<long>
        {
            public static readonly MixedLongComparer Instance = new MixedLongComparer();
            public bool Equals(long x, long y) => x == y;
            public int GetHashCode(long value)
            {
                unchecked
                {
                    ulong x = (ulong)value;
                    x ^= x >> 33; x *= 0xff51afd7ed558ccdUL;
                    x ^= x >> 33; x *= 0xc4ceb9fe1a85ec53UL;
                    x ^= x >> 33;
                    return (int)x;
                }
            }
        }

        static int Midpoint(int a, int b, List<Vector3> vertices, Dictionary<long, int> cache)
        {
            long key = EdgeKey(a, b);
            if (cache.TryGetValue(key, out int index)) return index;
            index = vertices.Count;
            vertices.Add((vertices[a] + vertices[b]).normalized);
            cache.Add(key, index);
            return index;
        }

        IReadOnlyList<SurfaceCell> DrawingCells(bool playing)
        {
            // Drawing never changes live cells, candidate references, IDs or occupancy.
            if (playing) return cells;
            int frequency;
            try { frequency = CalculateFrequency(subdivisions, sizeScale); }
            catch (ArgumentOutOfRangeException) { return Array.Empty<SurfaceCell>(); }
            if (previewFrequency != frequency || previewCells.Count == 0)
            {
                BuildCells(frequency, previewCells, out _, out _); previewFrequency = frequency;
            }
            return previewCells;
        }

        void OnDrawGizmosSelected()
        {
            if (!drawCellGizmos || !ground) return;
            foreach (var cell in DrawingCells(Application.isPlaying))
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
