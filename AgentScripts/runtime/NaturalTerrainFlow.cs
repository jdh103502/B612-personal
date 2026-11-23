using System;
using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    /// <summary>
    /// v11 river: route water over a sphere cell graph (steepest descent), accumulate catchment area,
    /// fill a shallow pond in a basin and follow that basin's largest inflow upstream as the river.
    /// The plan only reads the height field; the final ground and water are built from the result.
    /// </summary>
    internal static class NaturalTerrainFlow
    {
        /// <summary>Flow routing over the sphere cell graph, shared by the river planner and the moisture colours.</summary>
        internal sealed class Field
        {
            public List<PlanetSurfaceGrid.SurfaceCell> cells;
            public Vector3[] direction;
            public float[] height, area, accumulation;
            public int[] receiver;
            public List<int>[] donors;
            public float spacing;
        }

        internal static Field Build(ProceduralPlanetTerrain.Plan plan)
        {
            var settings = plan.natural;
            float radius = plan.radius;
            // Triangle-cell centres are about 0.694 R / f apart.
            int frequency = Mathf.Clamp(Mathf.CeilToInt(.694f * radius / settings.flowSpacingMeters), 4, 64);
            var cells = PlanetSurfaceGrid.CreateTopology(frequency);
            int count = cells.Count;
            var direction = new Vector3[count];
            var height = new float[count];
            var area = new float[count];
            for (int i = 0; i < count; i++)
            {
                var cell = cells[i];
                direction[i] = (cell.a + cell.b + cell.c).normalized;
                height[i] = ProceduralPlanetTerrain.FreeHeight(plan, direction[i]);
                area[i] = cell.solidAngle * radius * radius;
            }
            float spacing = .694f * radius / frequency;

            // Steepest descent over the 3 edge neighbours and their neighbours (up to 9 directions),
            // so routing is not limited to the three triangle-edge headings.
            var receiver = new int[count];
            var near = new List<int>(12);
            for (int i = 0; i < count; i++)
            {
                Near(cells, i, near);
                int best = -1;
                float bestSlope = 0f;
                foreach (int j in near)
                {
                    float slope = (height[i] - height[j]) / Arc(direction[i], direction[j], radius);
                    if (slope > bestSlope) { bestSlope = slope; best = j; }
                }
                receiver[i] = best;
            }

            // Catchment area: pass each cell's area to its receiver from the highest cell down.
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int c = height[b].CompareTo(height[a]);
                return c == 0 ? a.CompareTo(b) : c;
            });
            var accumulation = (float[])area.Clone();
            var donors = new List<int>[count];
            foreach (int i in order)
            {
                int r = receiver[i];
                if (r < 0) continue;
                accumulation[r] += accumulation[i];
                (donors[r] ??= new List<int>()).Add(i);
            }
            return new Field
            {
                cells = cells, direction = direction, height = height, area = area, accumulation = accumulation,
                receiver = receiver, donors = donors, spacing = spacing
            };
        }

        internal static bool Plan(ProceduralPlanetTerrain.Plan plan, Field field = null)
        {
            var settings = plan.natural;
            if (!settings.generateRiver)
            {
                plan.riverStatus = "River disabled by settings.";
                return false;
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            float radius = plan.radius;
            field ??= Build(plan);
            var cells = field.cells;
            int count = cells.Count;
            var direction = field.direction;
            var height = field.height;
            var receiver = field.receiver;
            var accumulation = field.accumulation;
            var donors = field.donors;
            float spacing = field.spacing;

            var pits = new List<int>();
            for (int i = 0; i < count; i++) if (receiver[i] < 0) pits.Add(i);
            pits.Sort((a, b) =>
            {
                int c = accumulation[b].CompareTo(accumulation[a]);
                return c == 0 ? a.CompareTo(b) : c;
            });

            // v19: the river of the shaped valley comes first; the usual candidates are the fallback.
            string valleyReason = null;
            if (plan.valley != null)
            {
                var valley = plan.valley;
                int pit = 0;
                float best = float.NegativeInfinity;
                for (int i = 0; i < count; i++)
                {
                    float dot = Vector3.Dot(direction[i], valley.lakeCentre);
                    if (dot > best) { best = dot; pit = i; }
                }
                while (receiver[pit] >= 0) pit = receiver[pit];
                var pond = FillPond(cells, direction, height, pit, radius, settings, out float pondLevel, out float pondBowl);
                float rim = pond == null ? 0f : Rim(plan, direction, pond, pit, pondLevel, spacing);
                if (pond == null) valleyReason = "empty lake";
                else if (rim <= 0f) valleyReason = "open lake rim";
                else
                {
                    var designed = new Candidate
                    {
                        path = new List<Vector3>(valley.route), pondDirection = direction[pit], pondLevel = pondLevel,
                        pondRadius = rim, pondBowl = pondBowl, minLength = valley.minRiverLength, valley = true,
                        lakeCentre = valley.lakeCentre, lakeRadius = valley.lakeRadius
                    };
                    valleyReason = Fit(plan, designed, out var valleyRiver);
                    if (valleyReason == "deep cut") valleyReason += $" at {designed.cutAt:F1} of {valley.length:F1}m";
                    if (valleyReason == null)
                    {
                        plan.river = valleyRiver;
                        plan.riverPlanMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
                        float widest = valleyRiver.width;
                        foreach (float w in valleyRiver.widths) widest = Mathf.Max(widest, w);
                        plan.riverStatus = $"Valley river into lake: {valleyRiver.lengths[valleyRiver.lengths.Count - 1]:F1}m " +
                            $"({valleyRiver.lengths[valleyRiver.lengths.Count - 1] / (2f * Mathf.PI * radius):F2} of the circumference), " +
                            $"width {valleyRiver.width:F1}-{widest:F1}m, pond radius {valleyRiver.pondRadius:F1}m ({count} cells).";
                        return true;
                    }
                }
            }

            var random = new System.Random(plan.riverSeed);
            var candidates = new List<Candidate>();
            int shallowPonds = 0, shortStems = 0, openRims = 0, flooded = 0;
            var inLake = new bool[count];
            foreach (int pit in pits)
            {
                if (settings.floodedRouting && flooded >= FloodedCandidates) break;
                var lake = FillPond(cells, direction, height, pit, radius, settings, out float level, out float bowl);
                if (lake == null) { shallowPonds++; continue; }
                foreach (int c in lake) inLake[c] = true;
                try
                {
                    float rim = Rim(plan, direction, lake, pit, level, spacing);
                    if (rim <= 0f) { openRims++; continue; }

                    // v14: route the whole planet into this pond as if every other basin were filled to its spill
                    // point, so the river can run on through shallow basins and become much longer. Basins deeper
                    // than most of the allowed cut are not crossed.
                    var routeReceiver = receiver;
                    var routeAccumulation = accumulation;
                    var routeDonors = donors;
                    float[] filled = null;
                    if (settings.floodedRouting)
                    {
                        flooded++;
                        Flood(field, lake, out routeReceiver, out routeAccumulation, out routeDonors, out filled);
                    }
                    float crossable = .8f * settings.riverMaxCutMeters;
                    // Water cannot reach the pond from ground lower than the pond surface, and deep basins would
                    // need a gorge deeper than allowed.
                    bool Blocked(int cell) => inLake[cell] ||
                        filled != null && (filled[cell] - height[cell] > crossable || height[cell] < level);

                    // Largest inflow that is not itself part of the pond.
                    int entry = -1;
                    foreach (int c in lake)
                        if (routeDonors[c] != null)
                            foreach (int d in routeDonors[c])
                                if (!Blocked(d) && (entry < 0 || routeAccumulation[d] > routeAccumulation[entry] ||
                                    routeAccumulation[d] == routeAccumulation[entry] && d < entry)) entry = d;
                    if (entry < 0) { shortStems++; continue; }

                    // Follow the main stem upstream, up to the longest allowed river.
                    var stem = new List<int> { entry };
                    float length = Arc(direction[entry], direction[routeReceiver[entry]], radius);
                    int current = entry;
                    while (routeDonors[current] != null)
                    {
                        int next = -1;
                        foreach (int d in routeDonors[current])
                            if (!Blocked(d) && (next < 0 || routeAccumulation[d] > routeAccumulation[next] ||
                                routeAccumulation[d] == routeAccumulation[next] && d < next)) next = d;
                        if (next < 0) break;
                        float step = Arc(direction[current], direction[next], radius);
                        if (length + step > settings.riverLengthMeters.y) break;
                        length += step;
                        stem.Add(next);
                        current = next;
                    }
                    bool longEnough = length >= settings.riverLengthMeters.x;
                    // v14: a flooded stem a little short of the minimum is still a candidate (ranked last, half the
                    // minimum length), and the pond's own basin river below is tried as well.
                    bool keepShort = settings.floodedRouting && length >= .5f * settings.riverLengthMeters.x;
                    if (!longEnough) shortStems++;
                    if (!longEnough && !keepShort && !settings.floodedRouting) continue;

                    var path = new List<Vector3>(stem.Count + 1);
                    for (int k = stem.Count - 1; k >= 0; k--) path.Add(direction[stem[k]]);
                    path.Add(direction[routeReceiver[entry]]);
                    if (longEnough || keepShort)
                        candidates.Add(new Candidate
                        {
                            path = path, pondDirection = direction[pit], pondLevel = level, pondRadius = rim, pondBowl = bowl,
                            minLength = longEnough ? 0f : .5f * settings.riverLengthMeters.x,
                            score = longEnough
                                ? (settings.floodedRouting ? length : 1f) * routeAccumulation[entry] * (.8f + .4f * (float)random.NextDouble())
                                : -.5f / (1 + candidates.Count)
                        });
                    if (settings.floodedRouting)
                    {
                        // Fallback for this pond: the river of its own basin only (v13 routing), at least half the
                        // minimum length. Ranked after every flooded candidate.
                        var local = BasinStem(lake, inLake, receiver, accumulation, donors, direction, radius, settings);
                        if (local != null)
                            candidates.Add(new Candidate
                            {
                                path = local, pondDirection = direction[pit], pondLevel = level, pondRadius = rim, pondBowl = bowl,
                                minLength = .5f * settings.riverLengthMeters.x, score = -1f / (1 + candidates.Count)
                            });
                    }
                }
                finally { foreach (int c in lake) inLake[c] = false; }
            }

            candidates.Sort((a, b) => b.score.CompareTo(a.score));
            int rejected = 0;
            var reasons = new Dictionary<string, int>();
            foreach (var candidate in candidates)
            {
                string reason = Fit(plan, candidate, out var river);
                // v14: a gorge too deep part way down: drop the river above it and fit the rest (up to 3 times).
                for (int retry = 0; settings.floodedRouting && reason == "deep cut" && retry < 3; retry++)
                {
                    var trimmed = Trim(candidate.path, candidate.cutAt + .5f, radius);
                    if (trimmed == null) break;
                    candidate.path = trimmed;
                    candidate.minLength = .5f * settings.riverLengthMeters.x;
                    reason = Fit(plan, candidate, out river);
                }
                if (reason != null)
                {
                    rejected++;
                    reasons[reason] = reasons.TryGetValue(reason, out int n) ? n + 1 : 1;
                    continue;
                }
                plan.river = river;
                plan.riverPlanMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
                float widest = river.width;
                foreach (float w in river.widths) widest = Mathf.Max(widest, w);
                plan.riverStatus = $"Flow river into pond: {river.lengths[river.lengths.Count - 1]:F1}m, width {river.width:F1}-{widest:F1}m, " +
                    $"pond radius {river.pondRadius:F1}m ({count} cells, {pits.Count} basins, {candidates.Count} candidates, {rejected} unfit)." +
                    (valleyReason != null ? $" Valley river unfit: {valleyReason}." : "");
                return true;
            }
            var unfit = new List<string>();
            foreach (var pair in reasons) unfit.Add(pair.Key + " " + pair.Value);
            plan.riverStatus = $"No flow river: {count} cells, {pits.Count} basins, {shallowPonds} empty ponds, " +
                $"{openRims} open rims, {shortStems} short stems, {rejected} unfit channels ({string.Join(", ", unfit)})." +
                (valleyReason != null ? $" Valley river unfit: {valleyReason}." : "");
            return false;
        }

        /// <summary>
        /// Rendering-only wetness from the flow field, 0 (dry) to 1 (wet), mixed from three cues:
        /// 40% water supply (log of the upstream cell count, MoistureCells.x to .y; drainage lines and basins),
        /// 30% topographic position (hollows below the mean height within about 2 x blur radius are wetter,
        /// crests drier) and 30% elevation (low ground is wetter than high ground across the planet).
        /// The cues are blurred once on the cells (about the blur radius); vertices then interpolate the
        /// nearest cells, which keeps the cost per vertex small.
        /// </summary>
        internal sealed class Moisture
        {
            readonly Vector3[] direction;
            readonly float[] wetness;
            readonly Dictionary<Vector3Int, List<int>> near;
            readonly float radius, nearReach;

            internal Moisture(ProceduralPlanetTerrain.Plan plan, Field field)
            {
                radius = plan.radius;
                direction = field.direction;
                int count = direction.Length;
                float q = Mathf.Min(1f, plan.radius / 12f);
                float reach = 2f * plan.natural.moistureBlurMeters;
                var wide = Binned(direction, radius, reach);

                float low = Mathf.Log(MoistureCells.x), high = Mathf.Log(MoistureCells.y);
                var raw = new float[count];
                ProceduralPlanetTerrain.ForEachRange(count, (from, to) =>
                {
                for (int i = from; i < to; i++)
                {
                    // Topographic position: mean height within `reach` minus own height (> 0 in hollows).
                    float sum = 0f;
                    int n = 0;
                    var d = direction[i];
                    var bin = Vector3Int.FloorToInt(d * (radius / reach));
                    float limit = reach * reach / (radius * radius);
                    for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                    {
                        if (!wide.TryGetValue(bin + new Vector3Int(x, y, z), out var list)) continue;
                        foreach (int j in list)
                            if (j != i && (direction[j] - d).sqrMagnitude <= limit) { sum += field.height[j]; n++; }
                    }
                    float position = n > 0 ? sum / n - field.height[i] : 0f;
                    float valley = Mathf.Clamp01(.5f + .5f * position / (PositionMeters * q));
                    float upstream = field.accumulation[i] / field.area[i];
                    float supply = Mathf.Clamp01((Mathf.Log(Mathf.Max(1f, upstream)) - low) / (high - low));
                    float lowland = Mathf.Clamp01(.5f - .5f * field.height[i] / Mathf.Max(.001f, plan.amplitude));
                    raw[i] = .4f * supply + .3f * valley + .3f * lowland;
                }
                });

                // Blur once on the cells with the smooth compact kernel (1 - r^2/reach^2)^2.
                var smoothed = new float[count];
                ProceduralPlanetTerrain.ForEachRange(count, (from, to) =>
                {
                    for (int i = from; i < to; i++) smoothed[i] = Average(raw, wide, reach, direction[i]);
                });
                wetness = smoothed;

                nearReach = 1.5f * field.spacing;
                near = Binned(direction, radius, nearReach);
            }

            static Dictionary<Vector3Int, List<int>> Binned(Vector3[] direction, float radius, float size)
            {
                var result = new Dictionary<Vector3Int, List<int>>();
                for (int i = 0; i < direction.Length; i++)
                {
                    var key = Vector3Int.FloorToInt(direction[i] * (radius / size));
                    if (!result.TryGetValue(key, out var list)) result.Add(key, list = new List<int>());
                    list.Add(i);
                }
                return result;
            }

            float Average(float[] values, Dictionary<Vector3Int, List<int>> bins, float reach, Vector3 d)
            {
                var bin = Vector3Int.FloorToInt(d * (radius / reach));
                float sum = 0f, weight = 0f, inverse = radius * radius / (reach * reach);
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    if (!bins.TryGetValue(bin + new Vector3Int(x, y, z), out var list)) continue;
                    foreach (int i in list)
                    {
                        float k = 1f - (direction[i] - d).sqrMagnitude * inverse;
                        if (k <= 0f) continue;
                        k *= k;
                        sum += k * values[i];
                        weight += k;
                    }
                }
                return weight > 0f ? sum / weight : 0f;
            }

            /// <summary>Thread-safe read of the smoothed wetness at a unit direction.</summary>
            internal float Sample(Vector3 d) => Average(wetness, near, nearReach, d);
        }

        /// <summary>Upstream cell counts mapped to supply wetness 0 and 1 (log scale).</summary>
        static readonly Vector2 MoistureCells = new Vector2(3f, 60f);

        /// <summary>Height below the surrounding mean that counts as a full hollow (scaled by q).</summary>
        const float PositionMeters = .15f;

        /// <summary>Drops the part of a cell path above `metres` from its source; null if too little is left.</summary>
        static List<Vector3> Trim(List<Vector3> path, float metres, float radius)
        {
            if (metres <= 0f) return null;
            float run = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                run += Arc(path[i - 1], path[i], radius);
                if (run >= metres) return path.Count - i >= 3 ? path.GetRange(i, path.Count - i) : null;
            }
            return null;
        }

        /// <summary>The v13 main stem of a pond's own basin, or null when it is shorter than half the minimum.</summary>
        static List<Vector3> BasinStem(List<int> lake, bool[] inLake, int[] receiver, float[] accumulation, List<int>[] donors,
            Vector3[] direction, float radius, NaturalTerrainSettings settings)
        {
            int entry = -1;
            foreach (int c in lake)
                if (donors[c] != null)
                    foreach (int d in donors[c])
                        if (!inLake[d] && (entry < 0 || accumulation[d] > accumulation[entry] ||
                            accumulation[d] == accumulation[entry] && d < entry)) entry = d;
            if (entry < 0) return null;
            var stem = new List<int> { entry };
            float length = Arc(direction[entry], direction[receiver[entry]], radius);
            int current = entry;
            while (donors[current] != null)
            {
                int next = -1;
                foreach (int d in donors[current])
                    if (!inLake[d] && (next < 0 || accumulation[d] > accumulation[next] ||
                        accumulation[d] == accumulation[next] && d < next)) next = d;
                if (next < 0) break;
                float step = Arc(direction[current], direction[next], radius);
                if (length + step > settings.riverLengthMeters.y) break;
                length += step;
                stem.Add(next);
                current = next;
            }
            if (length < .5f * settings.riverLengthMeters.x) return null;
            var path = new List<Vector3>(stem.Count + 1);
            for (int k = stem.Count - 1; k >= 0; k--) path.Add(direction[stem[k]]);
            path.Add(direction[receiver[entry]]);
            return path;
        }

        /// <summary>Candidate ponds that get a planet-wide flooded routing (the most-fed basins first).</summary>
        const int FloodedCandidates = 8;

        /// <summary>
        /// Priority-flood from the pond's cells over the edge neighbours: every other cell is reached at
        /// max(its height, the height it was reached from + epsilon), so depressions are filled to their spill
        /// point and all water drains into the pond along the lowest passes. Returns the routing tree
        /// (receiver = the cell it was reached from), its catchment areas, donors and the filled heights.
        /// </summary>
        static void Flood(Field field, List<int> seeds, out int[] receiver, out float[] accumulation,
            out List<int>[] donors, out float[] filled)
        {
            int count = field.direction.Length;
            receiver = new int[count];
            filled = new float[count];
            var visited = new bool[count];
            var queue = new SortedSet<(float, int)>();
            foreach (int s in seeds)
            {
                visited[s] = true;
                receiver[s] = -1;
                filled[s] = field.height[s];
                queue.Add((filled[s], s));
            }
            var order = new List<int>(count);
            while (queue.Count > 0)
            {
                var (h, c) = queue.Min;
                queue.Remove(queue.Min);
                order.Add(c);
                foreach (int n in field.cells[c].neighbors)
                {
                    if (visited[n]) continue;
                    visited[n] = true;
                    receiver[n] = c;
                    filled[n] = Mathf.Max(field.height[n], h + 1e-5f);
                    queue.Add((filled[n], n));
                }
            }
            accumulation = (float[])field.area.Clone();
            donors = new List<int>[count];
            for (int k = order.Count - 1; k >= 0; k--)
            {
                int c = order[k], r = receiver[c];
                if (r < 0) continue;
                accumulation[r] += accumulation[c];
                (donors[r] ??= new List<int>()).Add(c);
            }
        }

        sealed class Candidate
        {
            public List<Vector3> path;
            public Vector3 pondDirection;
            public float pondLevel, pondRadius, pondBowl, score;
            // v14: shortest acceptable fitted river, and where a failed fit hit a gorge too deep (metres from the source).
            public float minLength;
            public float cutAt = -1f;
            // v19: the shaped valley's river. Where it enters the lake obliquely one bank reaches the lake bowl first;
            // a bank inside the lake (or below its surface) is lake, not a low bank the river must stay under.
            public bool valley;
            public Vector3 lakeCentre;
            public float lakeRadius;
        }

        static void Near(List<PlanetSurfaceGrid.SurfaceCell> cells, int index, List<int> result)
        {
            result.Clear();
            foreach (int a in cells[index].neighbors)
            {
                if (!result.Contains(a)) result.Add(a);
                foreach (int b in cells[a].neighbors)
                    if (b != index && !result.Contains(b)) result.Add(b);
            }
        }

        /// <summary>
        /// Floods the basin from its lowest cell in height order. Stops at the spill point (a cell with a
        /// lower, unreached neighbour), at the maximum depth or at the maximum radius. Returns null when
        /// the pond would be too shallow or too small to read.
        /// </summary>
        static List<int> FillPond(List<PlanetSurfaceGrid.SurfaceCell> cells, Vector3[] direction, float[] height, int pit,
            float radius, NaturalTerrainSettings settings, out float level, out float bowl)
        {
            bowl = 0f;
            float cap = height[pit] + settings.pondMaxDepthMeters;
            var queue = new SortedSet<(float, int)> { (height[pit], pit) };
            var seen = new HashSet<int> { pit };
            var accepted = new List<int>();
            level = cap;
            while (queue.Count > 0)
            {
                var (h, c) = queue.Min;
                queue.Remove(queue.Min);
                if (h >= cap) break;
                if (Arc(direction[c], direction[pit], radius) > settings.pondMaxRadiusMeters) { level = h; break; }
                bool spill = false;
                foreach (int n in cells[c].neighbors)
                    if (!seen.Contains(n) && height[n] < h) { spill = true; break; }
                if (spill) { level = h; break; }
                accepted.Add(c);
                foreach (int n in cells[c].neighbors)
                    if (seen.Add(n)) queue.Add((height[n], n));
            }
            level = Mathf.Min(level, cap) - .01f * settings.pondMaxDepthMeters / .25f;
            float surface = level;
            var lake = accepted.FindAll(c => height[c] < surface);
            // A basin that spills almost at once still holds a pond: its centre is dug out to the
            // target depth with a smooth bowl (TerrainWater.PondCarve).
            bowl = Mathf.Max(0f, PondTargetDepth * settings.pondMaxDepthMeters - (level - height[pit]));
            return lake.Count == 0 ? null : lake;
        }

        /// <summary>Pond depth at its centre as a share of the maximum depth.</summary>
        const float PondTargetDepth = .6f;

        /// <summary>
        /// Smallest disc radius around the pit that contains the pond and whose rim stays above the water
        /// everywhere, so the flat water disc never shows past the shore. Returns 0 when there is none.
        /// </summary>
        static float Rim(ProceduralPlanetTerrain.Plan plan, Vector3[] direction, List<int> lake, int pit, float level, float spacing)
        {
            float extent = 0f;
            foreach (int c in lake) extent = Mathf.Max(extent, Arc(direction[c], direction[pit], plan.radius));
            var centre = direction[pit];
            var tangent = Vector3.Cross(centre, Mathf.Abs(centre.y) < .8f ? Vector3.up : Vector3.right).normalized;
            var bitangent = Vector3.Cross(centre, tangent);
            for (float rim = extent + .6f * spacing; rim <= extent + 2f * spacing; rim += .35f * spacing)
            {
                bool closed = true;
                float angle = rim / plan.radius;
                for (int s = 0; s < 48 && closed; s++)
                {
                    float theta = s * Mathf.PI * 2f / 48f;
                    var heading = tangent * Mathf.Cos(theta) + bitangent * Mathf.Sin(theta);
                    var probe = (centre * Mathf.Cos(angle) + heading * Mathf.Sin(angle)).normalized;
                    closed = ProceduralPlanetTerrain.FreeHeight(plan, probe) > level + .02f;
                }
                if (closed) return rim;
            }
            return 0f;
        }

        /// <summary>
        /// Smooths the cell path, resamples it and fits a non-increasing water level that stays under both
        /// banks, ending where it reaches the pond. Mirrors the v10 channel fit with pond-aware ends.
        /// </summary>
        static string Fit(ProceduralPlanetTerrain.Plan plan, Candidate candidate, out TerrainWater.River river)
        {
            var settings = plan.natural;
            river = null;
            var path = candidate.path;
            // Corner cutting removes the cell graph's 30-60 degree turns; Laplacian passes on the resampled
            // path then round the remaining bends over about 1.5 m. Both ends stay fixed.
            for (int iteration = 0; iteration < 2; iteration++) path = Chaikin(path);
            float q = Mathf.Min(1f, plan.radius / 12f);
            float step = Mathf.Min(.30f * q, settings.riverWidthMeters * .36f);
            path = Resample(path, step, plan.radius);
            if (path.Count < 3) return "short";
            int passes = Mathf.CeilToInt(Mathf.Pow(1.5f * q / step, 2f));
            for (int pass = 0; pass < passes; pass++)
            {
                var previous = path[0];
                for (int i = 1; i < path.Count - 1; i++)
                {
                    var current = path[i];
                    path[i] = (current * .5f + (previous + path[i + 1]) * .25f).normalized;
                    previous = current;
                }
            }
            path = Resample(path, step, plan.radius);
            if (path.Count < 3) return "short";

            // v13: the channel widens downstream, from the spring width to the mouth width (capped by the pond).
            float total = 0f;
            for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i], path[i - 1]) * plan.radius;
            float mouth = Mathf.Min(settings.riverMouthWidthMeters, .8f * candidate.pondRadius);
            bool widen = mouth > settings.riverWidthMeters;
            float travelled = 0f;
            float WidthAlong(float metres) => widen
                ? Mathf.Lerp(settings.riverWidthMeters, mouth, Mathf.Pow(Mathf.Clamp01(metres / Mathf.Max(.01f, total)), .7f))
                : settings.riverWidthMeters;

            var result = new TerrainWater.River
            {
                width = settings.riverWidthMeters, bankWidth = settings.bankTransitionMeters, flowFit = true, pond = true,
                pondDirection = candidate.pondDirection, pondLevel = candidate.pondLevel, pondRadius = candidate.pondRadius,
                pondBowl = candidate.pondBowl
            };
            // Ground as built: the height field minus the pond bowl (the channel itself is fitted here).
            float Ground(Vector3 d)
            {
                float free = ProceduralPlanetTerrain.FreeHeight(plan, d);
                return free - TerrainWater.PondCarve(candidate.pondDirection, candidate.pondRadius, candidate.pondBowl,
                    candidate.pondLevel, plan.radius, d, free);
            }
            // v14 keeps the v13 surface margin (3 cm at full size) although the channel is deeper.
            float margin = settings.floodedRouting ? .03f * q : settings.riverDepthMeters.x * .25f;
            // v14 checks the banks at the channel edge; v13 looked further out, which on a wide river crossing a
            // slope pulled the level down to the lower side and forced needless gorges downstream.
            float bankReach = settings.floodedRouting ? .3f : .65f;
            float level = float.PositiveInfinity;
            var originals = new List<float>();
            var depths = new List<float>();
            for (int i = 0; i < path.Count; i++)
            {
                var direction = path[i];
                var along = path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(0, i - 1)];
                var side = Vector3.Cross(direction, along).normalized;
                if (i > 0) travelled += Vector3.Distance(direction, path[i - 1]) * plan.radius;
                float width = WidthAlong(travelled);
                float original = Ground(direction);
                float bankHeight = Mathf.Min(Bank(-1), Bank(1));
                level = Mathf.Min(level, Mathf.Min(original, bankHeight) - margin);
                // v14 lets a long river cut a shallow gorge through a low pass (riverMaxCutMeters).
                if (original - level > Mathf.Max(settings.riverDepthMeters.y * .85f, settings.riverMaxCutMeters))
                {
                    candidate.cutAt = i == 0 ? 0f : result.lengths[i - 1] + Vector3.Distance(direction, path[i - 1]) * plan.radius;
                    return "deep cut";
                }
                float slope = Mathf.Clamp01((original - level) / settings.riverDepthMeters.y);
                originals.Add(original);
                float depth = Mathf.Lerp(settings.riverDepthMeters.x, settings.riverDepthMeters.y, slope);
                // v14: a narrow channel stays shallow so its banks stay walkable; the river deepens as it widens.
                if (settings.riverDepthPerWidth > 0f) depth = Mathf.Min(depth, settings.riverDepthPerWidth * width);
                depths.Add(depth);
                result.directions.Add(direction);
                result.points.Add(direction * plan.radius);
                result.levels.Add(level);
                result.lengths.Add(i == 0 ? 0f : result.lengths[i - 1] + Vector3.Distance(direction, path[i - 1]) * plan.radius);
                if (widen) result.widths.Add(width);
                // The channel ends where it reaches the shore band of the pond (v14: of this pond, not of any
                // lower ground it passes on the way).
                if (original < candidate.pondLevel + margin && (!settings.floodedRouting ||
                    Arc(direction, candidate.pondDirection, plan.radius) <= candidate.pondRadius + 1f)) break;

                float Bank(float sign)
                {
                    var bank = (direction * plan.radius + side * (sign * (width * .55f +
                        settings.bankTransitionMeters * bankReach))).normalized;
                    if (!candidate.valley) return Ground(bank);
                    float ground = Ground(bank);
                    return ground < candidate.pondLevel + 2f * margin ||
                        Arc(bank, candidate.lakeCentre, plan.radius) < candidate.lakeRadius ? float.PositiveInfinity : ground;
                }
            }
            int last = result.directions.Count - 1;
            if (originals[last] >= candidate.pondLevel + margin) return "no pond";
            if (result.lengths[last] < (candidate.minLength > 0f ? candidate.minLength : settings.riverLengthMeters.x) * .995f) return "short";

            // Water cannot sit below the pond it flows into. Raising the level must not lift it above dry ground.
            Vector3 mean = Vector3.zero;
            for (int i = 0; i <= last; i++)
            {
                float raised = Mathf.Max(result.levels[i], candidate.pondLevel);
                if (raised > originals[i] - .5f * margin && originals[i] >= candidate.pondLevel + margin) return "above ground";
                result.levels[i] = raised;
                result.beds.Add(raised - depths[i]);
                mean += result.directions[i];
            }
            if (widen)
            {
                // A wide mouth would end in a straight cut beside the pond. Carry the channel on into the pond
                // for one mouth width, just under the pond surface, so the pond water hides the end of the ribbon.
                float mouthWidth = result.widths[last], extended = 0f;
                for (int i = last + 1; i < path.Count && extended < mouthWidth; i++)
                {
                    float segment = Vector3.Distance(path[i], path[i - 1]) * plan.radius;
                    extended += segment;
                    result.directions.Add(path[i]);
                    result.points.Add(path[i] * plan.radius);
                    result.levels.Add(candidate.pondLevel - UnderPond);
                    result.beds.Add(candidate.pondLevel - UnderPond - depths[last]);
                    result.lengths.Add(result.lengths[result.lengths.Count - 1] + segment);
                    result.widths.Add(mouthWidth);
                    mean += path[i];
                }
            }
            result.centre = mean * (plan.radius / result.directions.Count);
            foreach (var point in result.points)
                result.bound = Mathf.Max(result.bound, Vector3.Distance(point, result.centre));
            if (settings.floodedRouting)
            {
                // v14: where the river cuts a gorge, its banks would be steep over the usual transition. The
                // section's peak slope is 2 x (ground - bed) / (half width + bank), so size the bank of the whole
                // river from its worst point to keep that at or under about 27 degrees (at most 2.5 m of bank), and
                // never below 3 x the deepest cut.
                float deepestCut = 0f, needed = 0f;
                for (int i = 0; i <= last; i++)
                {
                    deepestCut = Mathf.Max(deepestCut, originals[i] - result.levels[i]);
                    needed = Mathf.Max(needed, 3.9f * (originals[i] - result.beds[i]) - .5f * TerrainWater.WidthAt(result, i));
                }
                result.bankWidth = Mathf.Max(result.bankWidth, Mathf.Max(3f * deepestCut, Mathf.Min(2.5f, needed)));
                result.smoothBank = true;
                result.sourceFade = Mathf.Min(.25f * result.lengths[last], 3.9f * Mathf.Max(0f, originals[0] - result.beds[0]));
            }
            if (settings.canyonDepthMeters > 0f && settings.floodedRouting) Gorge(result, settings);
            float widest = result.width;
            foreach (float w in result.widths) widest = Mathf.Max(widest, w);
            result.bound += widest * .5f + result.bankWidth;
            TerrainWater.BuildIndex(result);
            river = result;
            return null;
        }

        /// <summary>
        /// v17: the river runs through a gorge on the stretch where it is 2.4-2.9 m wide (the gap between the gorge
        /// rims is then about a dash: width + 1.8 m), ending at least 4 m before the pond mouth; at least 6 m long or
        /// no gorge. Rivers that never get that wide take the last 8 m before the mouth margin; never the first 3 m.
        /// </summary>
        static void Gorge(TerrainWater.River river, NaturalTerrainSettings settings)
        {
            int last = river.directions.Count - 1;
            float total = river.lengths[last], end = total - 4f;
            float start = -1f, stop = end;
            for (int i = 0; i <= last; i++)
            {
                float w = TerrainWater.WidthAt(river, i), at = river.lengths[i];
                if (start < 0f && w >= 2.4f) start = at;
                if (start >= 0f && w > 2.9f) { stop = Mathf.Min(stop, at); break; }
            }
            if (start < 0f) start = end - 8f;
            start = Mathf.Max(start, 3f);
            if (stop - start < 6f) return;
            river.gorgeStart = start;
            river.gorgeEnd = stop;
            river.gorgeHeight = settings.canyonDepthMeters;
            river.gorgeFirst = 0; river.gorgeLast = last;
            for (int i = 0; i <= last; i++)
            {
                if (river.lengths[i] <= start) river.gorgeFirst = i;
                if (river.lengths[i] >= stop) { river.gorgeLast = i; break; }
            }
            var centre = Vector3.zero;
            for (int i = river.gorgeFirst; i <= river.gorgeLast; i++) centre += river.points[i];
            river.gorgeCentre = centre / (river.gorgeLast - river.gorgeFirst + 1);
            float reach = 0f;
            for (int i = river.gorgeFirst; i <= river.gorgeLast; i++)
                reach = Mathf.Max(reach, Vector3.Distance(river.points[i], river.gorgeCentre) + .5f * TerrainWater.WidthAt(river, i));
            river.gorgeBound = reach + TerrainWater.GorgeReach;
        }

        /// <summary>How far the underwater end of a widened channel sits below the pond surface (m).</summary>
        const float UnderPond = .004f;

        static List<Vector3> Chaikin(List<Vector3> path)
        {
            var result = new List<Vector3>(path.Count * 2) { path[0] };
            for (int i = 0; i < path.Count - 1; i++)
            {
                result.Add((path[i] * .75f + path[i + 1] * .25f).normalized);
                result.Add((path[i] * .25f + path[i + 1] * .75f).normalized);
            }
            result.Add(path[path.Count - 1]);
            return result;
        }

        static List<Vector3> Resample(List<Vector3> path, float step, float radius)
        {
            var result = new List<Vector3> { path[0] };
            float carried = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                float segment = Arc(path[i - 1], path[i], radius);
                float position = step - carried;
                while (position <= segment)
                {
                    result.Add(Vector3.Slerp(path[i - 1], path[i], position / segment).normalized);
                    position += step;
                }
                carried = segment - (position - step);
            }
            if (Arc(result[result.Count - 1], path[path.Count - 1], radius) > .25f * step) result.Add(path[path.Count - 1]);
            return result;
        }

        static float Arc(Vector3 a, Vector3 b, float radius) => Mathf.Acos(Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f)) * radius;
    }
}
