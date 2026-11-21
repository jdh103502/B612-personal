using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    /// <summary>
    /// v12 light erosion: shallow dry gullies along the drainage network of the uneroded height field.
    /// Every flow cell with at least erosionMinCells upstream cells is a channel cell; channels are traced from
    /// their heads down the receivers to a junction or basin, smoothed, and carved with a depth that grows with
    /// the log of the upstream count (0 at the head, erosionDepthMeters at 6 x erosionMinCells). The cross
    /// section is (1 - t^2)^2 over a half width of at least 6.5 x depth, so the carve adds at most ~13 degrees
    /// of slope and has zero slope at the centre and the edge. Overlapping gullies take the deepest carve.
    /// Gullies are absent within pondMaxRadius + 1.5 m of every basin floor and deepen over the next 3 m, as if
    /// sediment filled them where the water slows down; basins keep their rims, so ponds can still form there.
    /// </summary>
    internal sealed class NaturalTerrainErosion
    {
        readonly Vector3[] point;
        readonly float[] depth, halfWidth;
        readonly Dictionary<Vector3Int, List<int>> bins = new Dictionary<Vector3Int, List<int>>();
        readonly float radius, binSize;
        internal readonly int gullies;
        internal readonly float totalLength, deepest, meanDepth;

        /// <summary>Upstream cells, as a multiple of erosionMinCells, at which a gully reaches full depth.</summary>
        const float FullDepthFactor = 6f;

        NaturalTerrainErosion(ProceduralPlanetTerrain.Plan plan, List<Vector3> points, List<float> depths, List<float> widths,
            int gullies, float totalLength)
        {
            radius = plan.radius;
            point = points.ToArray();
            depth = depths.ToArray();
            halfWidth = widths.ToArray();
            this.gullies = gullies;
            this.totalLength = totalLength;
            float widest = .01f, sum = 0f;
            foreach (float w in halfWidth) widest = Mathf.Max(widest, w);
            foreach (float d in depth) { deepest = Mathf.Max(deepest, d); sum += d; }
            meanDepth = depth.Length > 0 ? sum / depth.Length : 0f;
            // Bins twice the widest half width: any gully point within reach of a query lies in the 2 x 2 x 2
            // block of bins around it, so a lookup touches 8 bins instead of 27.
            binSize = 2f * widest;
            for (int i = 0; i < point.Length; i++)
            {
                var key = Vector3Int.FloorToInt(point[i] * (radius / binSize));
                if (!bins.TryGetValue(key, out var list)) bins.Add(key, list = new List<int>());
                list.Add(i);
            }
        }

        /// <summary>Thread-safe depth removed at a unit direction (0 away from every gully).</summary>
        internal float Carve(Vector3 direction)
        {
            var bin = Vector3Int.FloorToInt(direction * (radius / binSize) - new Vector3(.5f, .5f, .5f));
            float best = 0f, r2 = radius * radius;
            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                if (!bins.TryGetValue(bin + new Vector3Int(x, y, z), out var list)) continue;
                foreach (int i in list)
                {
                    float w = halfWidth[i];
                    float squared = (point[i] - direction).sqrMagnitude * r2;
                    if (squared >= w * w) continue;
                    float k = 1f - squared / (w * w);
                    float carve = depth[i] * k * k;
                    if (carve > best) best = carve;
                }
            }
            return best;
        }

        internal static NaturalTerrainErosion Build(ProceduralPlanetTerrain.Plan plan, NaturalTerrainFlow.Field field)
        {
            var settings = plan.natural;
            int count = field.direction.Length;
            float minCells = settings.erosionMinCells;
            float low = Mathf.Log(minCells), high = Mathf.Log(minCells * FullDepthFactor);
            var upstream = new float[count];
            var channel = new bool[count];
            for (int i = 0; i < count; i++)
            {
                upstream[i] = field.accumulation[i] / field.area[i];
                channel[i] = upstream[i] >= minCells && field.receiver[i] >= 0;
            }

            float q = Mathf.Min(1f, plan.radius / 12f);
            float step = .15f * q;
            var basins = new List<Vector3>();
            for (int i = 0; i < count; i++) if (field.receiver[i] < 0) basins.Add(field.direction[i]);
            float clear = settings.pondMaxRadiusMeters + 1.5f * q, fade = 3f * q;
            var points = new List<Vector3>();
            var depths = new List<float>();
            var widths = new List<float>();
            var visited = new bool[count];
            int gullies = 0;
            float totalLength = 0f;
            for (int head = 0; head < count; head++)
            {
                if (!channel[head] || visited[head] || HasChannelDonor(field, channel, head)) continue;
                // Trace down the receivers until the line joins an earlier gully or reaches a basin.
                var cells = new List<int> { head };
                visited[head] = true;
                int current = head;
                while (field.receiver[current] >= 0)
                {
                    int next = field.receiver[current];
                    cells.Add(next);
                    if (visited[next] || !channel[next]) break;
                    visited[next] = true;
                    current = next;
                }
                if (cells.Count < 3) continue;
                gullies++;

                // Densify with the wetness carried along, then round the cell-graph corners (ends fixed).
                var line = new List<Vector3>();
                var strength = new List<float>();
                for (int k = 1; k < cells.Count; k++)
                {
                    Vector3 a = field.direction[cells[k - 1]], b = field.direction[cells[k]];
                    float fa = Mathf.Clamp01((Mathf.Log(Mathf.Max(1f, upstream[cells[k - 1]])) - low) / (high - low));
                    float fb = Mathf.Clamp01((Mathf.Log(Mathf.Max(1f, upstream[cells[k]])) - low) / (high - low));
                    float arc = Mathf.Acos(Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f)) * plan.radius;
                    totalLength += arc;
                    int steps = Mathf.Max(1, Mathf.CeilToInt(arc / step));
                    for (int s = k == 1 ? 0 : 1; s <= steps; s++)
                    {
                        float t = s / (float)steps;
                        line.Add(Vector3.Slerp(a, b, t).normalized);
                        strength.Add(Mathf.Lerp(fa, fb, t));
                    }
                }
                int passes = Mathf.CeilToInt(Mathf.Pow(.8f * q / step, 2f));
                for (int pass = 0; pass < passes; pass++)
                {
                    var previous = line[0];
                    for (int i = 1; i < line.Count - 1; i++)
                    {
                        var currentPoint = line[i];
                        line[i] = (currentPoint * .5f + (previous + line[i + 1]) * .25f).normalized;
                        previous = currentPoint;
                    }
                }
                var carve = new float[line.Count];
                for (int i = 0; i < line.Count; i++)
                {
                    float nearest = float.PositiveInfinity;
                    foreach (var basin in basins)
                        nearest = Mathf.Min(nearest, Mathf.Acos(Mathf.Clamp(Vector3.Dot(basin, line[i]), -1f, 1f)) * plan.radius);
                    float settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((nearest - clear) / fade));
                    carve[i] = settings.erosionDepthMeters * strength[i] * settle;
                }
                // The bed must never rise downstream, or the shallowing near a basin would trap water in a new
                // pit. Walking up from the mouth, a bed lower than the bed below it is raised (the carve shrinks).
                float below = float.NegativeInfinity;
                for (int i = line.Count - 1; i >= 0; i--)
                {
                    float ground = ProceduralPlanetTerrain.FreeHeight(plan, line[i]);
                    float bed = Mathf.Max(ground - carve[i], below);
                    carve[i] = Mathf.Max(0f, ground - bed);
                    below = Mathf.Min(bed, ground);
                }
                for (int i = 0; i < line.Count; i++)
                {
                    float d = carve[i];
                    if (d <= .002f) continue;
                    points.Add(line[i]);
                    depths.Add(d);
                    widths.Add(Mathf.Max(6.5f * d, settings.erosionWidthMeters * (.6f + .4f * strength[i])));
                }
            }
            return new NaturalTerrainErosion(plan, points, depths, widths, gullies, totalLength);
        }

        static bool HasChannelDonor(NaturalTerrainFlow.Field field, bool[] channel, int cell)
        {
            var donors = field.donors[cell];
            if (donors == null) return false;
            foreach (int d in donors) if (channel[d]) return true;
            return false;
        }
    }
}
