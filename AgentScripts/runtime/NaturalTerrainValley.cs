using System;
using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    /// <summary>
    /// Generator v19: the one long river runs in a valley shaped before the flow is routed. The route follows the
    /// lowest passes of the relief from a high source to a lake in the lowest ground (a priority flood over a coarse
    /// cell graph), at least the given share of the circumference long. Along it the ground is blended to a floor
    /// that only falls downstream (it cuts through passes and never rises above the ground on the route), with a flat
    /// cross-section as wide as the river plus a bank, and walls that blend back to the relief. The lake is a smooth
    /// bowl with one lowest point. The flow planner then fits the river to this route first.
    /// </summary>
    internal sealed class NaturalTerrainValley
    {
        /// <summary>Route from the source to the lake centre (unit directions).</summary>
        public readonly List<Vector3> route = new List<Vector3>();
        public Vector3 lakeCentre;
        public float length, lakeRadius, minRiverLength, maxCut, meanCut;
        public string status;

        float radius, cell, lakeSide, lakeRim, lakeBottom, lakeCurve, entryAlong, averageReach, lakeEdge;
        Vector3[] points;
        float[] floor, half, side, alongs;
        // Route points in consecutive chunks, each with a bounding sphere, so a ground point only visits the chunks
        // within reach (exactly: no cell edges where the set of visible points could change).
        Vector3[] chunkCentre;
        float[] chunkRadius;
        int chunkSize;

        /// <summary>
        /// The ground lookup visits every second route point (about 1 m apart): the valley edge then deviates from a
        /// smooth line by under 4 cm of radius, and the averaging radius (at least 2 m) still spans several points.
        /// </summary>
        const int Stride = 2;

        /// <summary>Downstream fall of the floor so it never has flat stretches (1 in 330).</summary>
        const float FloorFall = .003f;

        internal static NaturalTerrainValley Build(ProceduralPlanetTerrain.Plan plan)
        {
            var n = plan.natural;
            float radius = plan.radius, q = Mathf.Min(1f, radius / 12f);
            float circumference = 2f * Mathf.PI * radius;
            float lakeRadius = n.riverLakeRadiusMeters;
            float minRiver = n.riverCircumferenceShare.x * circumference;
            float maxRiver = Mathf.Max(minRiver, n.riverCircumferenceShare.y * circumference);
            // The fitted river ends at the lake shore, about a lake radius before the route's end.
            float minRoute = minRiver + lakeRadius + 1f, maxRoute = maxRiver + lakeRadius + 1f;

            int frequency = Mathf.Clamp(Mathf.CeilToInt(.694f * radius / (.8f * q)), 8, 40);
            var cells = PlanetSurfaceGrid.CreateTopology(frequency);
            int count = cells.Count;
            var direction = new Vector3[count];
            var height = new float[count];
            ProceduralPlanetTerrain.ForEachRange(count, (from, to) =>
            {
                for (int i = from; i < to; i++)
                {
                    var c = cells[i];
                    direction[i] = (c.a + c.b + c.c).normalized;
                    height[i] = ProceduralPlanetTerrain.FreeHeight(plan, direction[i]);
                }
            });

            // The lake sits in the broadest low ground: the lowest mean height over a lake-sized disc (a single low
            // cell can be a narrow hollow, and a river dropping into it would end in a steep chute).
            var cellBins = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < count; i++)
            {
                var key = Vector3Int.FloorToInt(direction[i] * (radius / lakeRadius));
                if (!cellBins.TryGetValue(key, out var list)) cellBins[key] = list = new List<int>();
                list.Add(i);
            }
            var basin = new float[count];
            float chord = lakeRadius / radius;
            ProceduralPlanetTerrain.ForEachRange(count, (from, to) =>
            {
                for (int i = from; i < to; i++)
                {
                    var key = Vector3Int.FloorToInt(direction[i] * (radius / lakeRadius));
                    float sum = 0f;
                    int used = 0;
                    for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                    {
                        if (!cellBins.TryGetValue(key + new Vector3Int(x, y, z), out var list)) continue;
                        foreach (int j in list)
                            if ((direction[j] - direction[i]).sqrMagnitude <= chord * chord) { sum += height[j]; used++; }
                    }
                    basin[i] = sum / Mathf.Max(1, used);
                }
            });
            int mouth = 0;
            for (int i = 1; i < count; i++) if (basin[i] < basin[mouth]) mouth = i;

            // Routes keep off the mountains' upper parts (inside 75% of their radius): the flood below never enters them,
            // so the lowest passes lead around a peak. A river may start on a lower flank but never cuts through a peak.
            var peak = new bool[count];
            float peakArc = .75f * n.mountainRadiusMeters / radius;
            foreach (var m in plan.mountains)
            {
                var c = new Vector3(m.x, m.y, m.z);
                for (int i = 0; i < count; i++) peak[i] |= Vector3.Dot(direction[i], c) > Mathf.Cos(peakArc);
            }
            // Priority flood from the lake: each cell's route to it crosses the lowest passes (a river's path).
            var parent = new int[count];
            var distance = new float[count];
            var visited = new bool[count];
            var queue = new SortedSet<(float, int)>();
            parent[mouth] = -1;
            visited[mouth] = true;
            queue.Add((height[mouth], mouth));
            var order = new List<int>(count);
            while (queue.Count > 0)
            {
                var (h, c) = queue.Min;
                queue.Remove(queue.Min);
                order.Add(c);
                foreach (int x in cells[c].neighbors)
                {
                    if (visited[x]) continue;
                    visited[x] = true;
                    parent[x] = c;
                    if (peak[x]) { distance[x] = -1f; continue; }
                    distance[x] = distance[c] + Arc(direction[c], direction[x], radius);
                    queue.Add((Mathf.Max(height[x], h + 1e-5f), x));
                }
            }

            // Score the sources whose route has a usable length: shallow cuts first, then a higher source, but not a
            // steep start (a river would run down a mountain face).
            var random = new System.Random(plan.riverSeed ^ 0x5a11e);
            var scored = new List<(float score, int source)>();
            var chain = new List<int>(256);
            for (int s = 0; s < count; s++)
            {
                if (distance[s] < 1.2f * minRoute || distance[s] > 1.3f * maxRoute) continue;
                chain.Clear();
                bool crossesPeak = false;
                for (int c = s; c >= 0; c = parent[c]) { chain.Add(c); crossesPeak |= peak[c]; }
                if (crossesPeak) continue;
                float low = float.PositiveInfinity, worst = 0f, total = 0f, steep = 0f;
                for (int k = 0; k < chain.Count; k++)
                {
                    float h = height[chain[k]];
                    low = Mathf.Min(low, h);
                    float cut = h - low;
                    worst = Mathf.Max(worst, cut);
                    total += cut;
                    // Fall over the next ~3 m.
                    float along = distance[chain[k]];
                    for (int j = k + 1; j < chain.Count && along - distance[chain[j]] <= 3f * q; j++)
                        steep = Mathf.Max(steep, (h - height[chain[j]]) / Mathf.Max(.5f, along - distance[chain[j]]));
                }
                float mean = total / chain.Count;
                float score = -2f * worst - mean + .25f * Mathf.Min(height[s] - height[mouth], 4f * q)
                    - 4f * Mathf.Max(0f, steep - .3f) + .3f * (float)random.NextDouble();
                scored.Add((score, s));
            }
            scored.Sort((a, b) => b.score != a.score ? b.score.CompareTo(a.score) : a.source.CompareTo(b.source));

            string rejected = "";
            for (int pick = 0; pick < scored.Count && pick < 40; pick++)
            {
                chain.Clear();
                for (int c = scored[pick].source; c >= 0; c = parent[c]) chain.Add(c);
                var path = new List<Vector3>(chain.Count);
                foreach (int c in chain) path.Add(direction[c]);
                path = Smooth(path, radius, q);
                path = Meander(path, radius, q, random);
                // The river ends where the route first reaches the lake; from there it runs straight to the centre.
                var centre = direction[mouth];
                for (int i = 0; i < path.Count; i++)
                {
                    if (Arc(path[i], centre, radius) >= lakeRadius) continue;
                    path.RemoveRange(i + 1, path.Count - i - 1);
                    path.Add(centre);
                    path = Resample(path, .5f * q, radius);
                    break;
                }
                float total = 0f;
                for (int i = 1; i < path.Count; i++) total += Arc(path[i - 1], path[i], radius);
                if (total < minRoute) { rejected += "short "; continue; }
                var valley = new NaturalTerrainValley
                {
                    radius = radius, lakeCentre = direction[mouth], lakeRadius = lakeRadius, length = total,
                    minRiverLength = minRiver
                };
                valley.route.AddRange(path);
                string reason = valley.Shape(plan, q);
                if (reason != null) { rejected += reason + " "; continue; }
                valley.status = $"Valley route {total:F1}m ({total / circumference:F2} of the circumference), " +
                    $"cut {valley.meanCut:F2}-{valley.maxCut:F2}m, lake radius {lakeRadius:F1}m, choice {pick + 1} of {scored.Count}.";
                return valley;
            }
            var tally = new SortedDictionary<string, int>();
            foreach (var word in rejected.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                tally[word] = tally.TryGetValue(word, out int seen) ? seen + 1 : 1;
            var parts = new List<string>();
            foreach (var pair in tally) parts.Add(pair.Key + " " + pair.Value);
            plan.riverStatus = $"No valley route ({scored.Count} sources; {string.Join(", ", parts)}).";
            return null;
        }

        /// <summary>Corner cutting, resampling and Laplacian passes that round bends over about 2.5 m; ends stay.</summary>
        static List<Vector3> Smooth(List<Vector3> path, float radius, float q)
        {
            for (int iteration = 0; iteration < 2; iteration++)
            {
                var cut = new List<Vector3>(path.Count * 2) { path[0] };
                for (int i = 0; i < path.Count - 1; i++)
                {
                    cut.Add((path[i] * .75f + path[i + 1] * .25f).normalized);
                    cut.Add((path[i] * .25f + path[i + 1] * .75f).normalized);
                }
                cut.Add(path[path.Count - 1]);
                path = cut;
            }
            float step = .5f * q;
            path = Resample(path, step, radius);
            int passes = Mathf.CeilToInt(Mathf.Pow(2.5f * q / step, 2f));
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
            return Resample(path, step, radius);
        }

        /// <summary>
        /// Bends the smoothed route into meanders: a sideways offset of about 2 m with a wavelength of about 15 m
        /// (both varying along the river), growing in over the first 6 m from the source. The tightest bend keeps a
        /// radius of about 3 m, wider than half the mouth.
        /// </summary>
        static List<Vector3> Meander(List<Vector3> path, float radius, float q, System.Random random)
        {
            float wave = 15f * q, amplitude = 2f * q;
            float a = (float)random.NextDouble() * 6.283f, b = (float)random.NextDouble() * 6.283f, c = (float)random.NextDouble() * 6.283f;
            var result = new List<Vector3>(path.Count);
            float along = 0f, phase = c;
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0)
                {
                    float step = Arc(path[i - 1], path[i], radius);
                    along += step;
                    phase += 6.283f * step / (wave * (1f + .25f * Mathf.Sin(6.283f * along / (3.7f * wave) + a)));
                }
                var up = path[i];
                var forward = path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(0, i - 1)];
                var across = Vector3.Cross(up, forward).normalized;
                float grow = Mathf.Clamp01(along / (6f * q));
                float offset = amplitude * grow * grow * (3f - 2f * grow) * (.7f + .3f * Mathf.Sin(6.283f * along / (2.9f * wave) + b)) * Mathf.Sin(phase);
                result.Add((up + across * (offset / radius)).normalized);
            }
            return Resample(result, .5f * q, radius);
        }

        /// <summary>Floor, widths and wall widths along the route, the lake bowl and the lookup chunks.</summary>
        string Shape(ProceduralPlanetTerrain.Plan plan, float q)
        {
            var n = plan.natural;
            int count = route.Count;
            points = new Vector3[count];
            floor = new float[count];
            half = new float[count];
            side = new float[count];
            var ground = new float[count];
            var along = alongs = new float[count];
            for (int i = 0; i < count; i++)
            {
                points[i] = route[i] * radius;
                if (i > 0) along[i] = along[i - 1] + Arc(route[i - 1], route[i], radius);
                ground[i] = ProceduralPlanetTerrain.FreeHeight(plan, route[i]);
            }

            float entry = length - lakeRadius;

            // Floor: lowest ground so far (looking 6 m ahead), falling a little all the way, then box-averaged twice
            // over +/-3 m. An average of a falling series still falls, and the look-ahead keeps the averaged floor
            // under the ground on the route.
            float depth = .25f * q;
            int lookAhead = Mathf.CeilToInt(6f * q / (.5f * q)), window = Mathf.CeilToInt(3f * q / (.5f * q));
            var raw = new float[count];
            float low = float.PositiveInfinity;
            var suffix = new float[count];
            for (int i = 0; i < count; i++)
            {
                low = Mathf.Min(low, ground[Mathf.Min(count - 1, i + lookAhead)] - depth);
                if (i == 0) for (int j = 0; j <= Mathf.Min(count - 1, lookAhead); j++) low = Mathf.Min(low, ground[j] - depth);
                raw[i] = low - FloorFall * along[i];
            }
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < count; i++)
                {
                    float sum = 0f;
                    int used = 0;
                    for (int j = Mathf.Max(0, i - window); j <= Mathf.Min(count - 1, i + window); j++) { sum += raw[j]; used++; }
                    suffix[i] = sum / used;
                }
                Array.Copy(suffix, raw, count);
            }
            Array.Copy(raw, floor, count);
            int entryIndex = count - 1;
            for (int i = 0; i < count; i++) if (along[i] >= entry) { entryIndex = i; break; }

            // Flat floor: half the river width (widening downstream as the planner will widen it) plus a bank.
            float spring = n.riverWidthMeters;
            float mouth = Mathf.Max(spring, Mathf.Min(n.riverMouthWidthMeters, .8f * .9f * lakeRadius));
            float river = Mathf.Max(1f, entry);
            for (int i = 0; i < count; i++)
                half[i] = .5f * Mathf.Lerp(spring, mouth, Mathf.Pow(Mathf.Clamp01(along[i] / river), .7f)) + 1f * q;

            // Walls: wide enough that blending the floor back to the relief stays near 23 degrees.
            var raise = new float[count];
            maxCut = 0f;
            float cutSum = 0f;
            for (int i = 0; i < count; i++)
            {
                var up = route[i];
                var forward = route[Mathf.Min(i + 1, count - 1)] - route[Mathf.Max(0, i - 1)];
                var across = Vector3.Cross(up, forward).normalized;
                float offset = (half[i] + 1.5f * q) / radius;
                float left = ProceduralPlanetTerrain.FreeHeight(plan, (up + across * offset).normalized);
                float right = ProceduralPlanetTerrain.FreeHeight(plan, (up - across * offset).normalized);
                float cut = ground[i] - floor[i];
                if (along[i] < entry) { maxCut = Mathf.Max(maxCut, cut); cutSum += cut; }
                raise[i] = Mathf.Max(Mathf.Abs(cut), Mathf.Max(Mathf.Abs(left - floor[i]), Mathf.Abs(right - floor[i])));
            }
            meanCut = cutSum / count;
            int spread = Mathf.CeilToInt(3f * q / (.5f * q));
            for (int i = 0; i < count; i++)
            {
                float worst = 0f;
                for (int j = Mathf.Max(0, i - spread); j <= Mathf.Min(count - 1, i + spread); j++) worst = Mathf.Max(worst, raise[j]);
                side[i] = Mathf.Clamp(1.6f * worst / .42f, 2f * q, 8f * q);
            }

            // Lake: a bowl, its rim at the floor where the route enters, its centre deeper than the pond fills.
            averageReach = 2f * q;
            lakeEdge = 1.5f * q;
            lakeRim = floor[entryIndex];
            entryAlong = along[entryIndex];
            lakeBottom = lakeRim - (n.pondMaxDepthMeters + .12f * q);
            lakeCurve = .05f * q;
            float lakeRaise = 0f;
            var centre = lakeCentre;
            var t0 = Vector3.Cross(centre, Mathf.Abs(centre.y) < .8f ? Vector3.up : Vector3.right).normalized;
            var t1 = Vector3.Cross(centre, t0);
            for (int k = 0; k < 16; k++)
            {
                float theta = k * Mathf.PI * 2f / 16f, angle = (lakeRadius + 2f * q) / radius;
                var heading = t0 * Mathf.Cos(theta) + t1 * Mathf.Sin(theta);
                var d = (centre * Mathf.Cos(angle) + heading * Mathf.Sin(angle)).normalized;
                lakeRaise = Mathf.Max(lakeRaise, Mathf.Abs(ProceduralPlanetTerrain.FreeHeight(plan, d) - lakeRim));
            }
            lakeSide = Mathf.Clamp(1.6f * lakeRaise / .42f, 2f * q, 8f * q);

            cell = 0f;
            for (int i = 0; i < count; i++) cell = Mathf.Max(cell, half[i] + side[i]);
            var bins = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < count; i++)
            {
                var key = Vector3Int.FloorToInt(points[i] / cell);
                if (!bins.TryGetValue(key, out var list)) bins[key] = list = new List<int>();
                list.Add(i);
            }
            // Two stretches of the route far apart along it must not share ground, or their floors would mix.
            for (int i = 0; i < count; i++)
            {
                var key = Vector3Int.FloorToInt(points[i] / cell);
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    if (!bins.TryGetValue(key + new Vector3Int(x, y, z), out var list)) continue;
                    foreach (int j in list)
                    {
                        if (j <= i || along[j] - along[i] < 2.5f * cell) continue;
                        float apart = Mathf.Max(half[i] + side[i], half[j] + side[j]) + 1f * q;
                        if ((points[j] - points[i]).sqrMagnitude < apart * apart) return "loop";
                    }
                }
            }
            chunkSize = Mathf.Max(12, Mathf.CeilToInt(count / 128f) * 2); // even, so the stride lines up in every chunk
            int chunks = (count + chunkSize - 1) / chunkSize;
            chunkCentre = new Vector3[chunks];
            chunkRadius = new float[chunks];
            for (int c = 0; c < chunks; c++)
            {
                int first = c * chunkSize, last = Mathf.Min(count, first + chunkSize);
                var middle = Vector3.zero;
                for (int i = first; i < last; i++) middle += points[i];
                middle /= last - first;
                float reach = 0f;
                for (int i = first; i < last; i++) reach = Mathf.Max(reach, (points[i] - middle).magnitude);
                chunkCentre[c] = middle;
                // Any point that can matter lies within the widest reach plus the averaging margin.
                chunkRadius[c] = reach + cell + averageReach;
            }
            return null;
        }

        /// <summary>Ground with the valley and the lake blended in (h = the relief at this point, in metres).</summary>
        internal float Apply(Vector3 metres, float h)
        {
            ulong near = 0;
            for (int c = 0; c < chunkCentre.Length; c++)
                if ((metres - chunkCentre[c]).sqrMagnitude < chunkRadius[c] * chunkRadius[c]) near |= 1UL << c;
            float weight = 0f, kernel = 0f, target = 0f;
            if (near != 0)
            {
                // Pass 1: nearest route point (sets the averaging radius); pass 2: valley weight and averaged position.
                float nearest = float.PositiveInfinity;
                for (int c = 0; c < chunkCentre.Length; c++)
                {
                    if ((near & (1UL << c)) == 0) continue;
                    int last = Mathf.Min(points.Length, (c + 1) * chunkSize);
                    for (int i = c * chunkSize; i < last; i += Stride)
                        if (alongs[i] <= entryAlong) nearest = Mathf.Min(nearest, (metres - points[i]).sqrMagnitude);
                }
                if (nearest < cell * cell)
                {
                    // The averaging radius grows with the distance from the route, so it changes smoothly over the
                    // ground and stays short on the floor (shorter than half a meander, where a bend is nearly symmetric).
                    float kernelRadius = Mathf.Sqrt(nearest) + averageReach;
                    float kernelSq = kernelRadius * kernelRadius;
                    for (int c = 0; c < chunkCentre.Length; c++)
                    {
                        if ((near & (1UL << c)) == 0) continue;
                        int last = Mathf.Min(points.Length, (c + 1) * chunkSize);
                        for (int i = c * chunkSize; i < last; i += Stride)
                        {
                            // Inside the lake the bowl is the ground; the route there only leads to its centre.
                            if (alongs[i] > entryAlong) continue;
                            float sq = (metres - points[i]).sqrMagnitude;
                            float reach = half[i] + side[i];
                            if (sq < reach * reach)
                            {
                                float r = Mathf.Sqrt(sq);
                                weight = Mathf.Max(weight, r <= half[i] ? 1f : 1f - Smooth01((r - half[i]) / side[i]));
                            }
                            if (sq >= kernelSq) continue;
                            float k = 1f - sq / kernelSq;
                            k *= k; k *= k;
                            kernel += k;
                            target += k * alongs[i];
                        }
                    }
                }
            }
            // The kernel averages the position along the route, not the floor height: across a straight or evenly bent
            // stretch the average is the foot point's position, so the cross-section stays flat where the floor drops.
            if (weight > 0f && kernel > 0f) h = Mathf.Lerp(h, FloorAt(target / kernel), weight);

            float lake = (metres - lakeCentre * radius).magnitude;
            if (lake < lakeRadius + lakeSide)
            {
                float t = Mathf.Clamp01(lake / lakeRadius);
                float bowl = lake <= lakeRadius
                    ? lakeBottom + (lakeRim - lakeBottom) * Smooth01((t - .55f) / .45f) + lakeCurve * (t * t - 1f)
                    : lakeRim;
                // Outside the lake its walls blend back to the ground, but not over the valley floor (which reaches
                // the rim height at the lake on its own); otherwise the floor near the mouth would tilt towards the lake.
                // The valley's share fades in over the last 1.5 m inside the rim, so both sides of the rim agree where
                // a valley wall meets the lake.
                float w = lake <= lakeRadius ? 1f : 1f - Smooth01((lake - lakeRadius) / lakeSide);
                w *= 1f - weight * Smooth01((lake - (lakeRadius - lakeEdge)) / lakeEdge);
                h = Mathf.Lerp(h, bowl, w);
            }
            return h;
        }

        float FloorAt(float s)
        {
            int last = alongs.Length - 1;
            if (s <= 0f) return floor[0];
            if (s >= alongs[last]) return floor[last];
            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (alongs[mid] <= s) lo = mid; else hi = mid;
            }
            float t = (s - alongs[lo]) / Mathf.Max(1e-6f, alongs[hi] - alongs[lo]);
            return Mathf.Lerp(floor[lo], floor[hi], t);
        }

        static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        static List<Vector3> Resample(List<Vector3> path, float step, float radius)
        {
            var result = new List<Vector3> { path[0] };
            float carried = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                float segment = Arc(path[i - 1], path[i], radius);
                if (segment <= 0f) continue;
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
