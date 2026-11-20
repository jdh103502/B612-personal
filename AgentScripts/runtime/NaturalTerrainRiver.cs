using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    internal static class NaturalTerrainRiver
    {
        internal static void Plan(ProceduralPlanetTerrain.Plan plan)
        {
            var settings = plan.natural;
            if (!settings.generateRiver)
            {
                plan.riverStatus = "River disabled by settings.";
                return;
            }

            var random = new System.Random(plan.riverSeed);
            var rotation = Quaternion.Euler((float)random.NextDouble() * 360f,
                (float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f);
            const int sourceCount = 768;
            var sources = new List<(Vector3 direction, float score, int id)>(sourceCount);
            for (int i = 0; i < sourceCount; i++)
            {
                float y = 1f - 2f * (i + .5f) / sourceCount;
                float ring = Mathf.Sqrt(1f - y * y);
                float angle = i * 2.39996323f;
                var direction = rotation * new Vector3(ring * Mathf.Cos(angle), y, ring * Mathf.Sin(angle));
                float height = ProceduralPlanetTerrain.FreeHeight(plan, direction);
                // Independent tie-breaking also spreads candidates among high areas.
                sources.Add((direction, height + (float)random.NextDouble() * plan.amplitude * .12f, i));
            }
            sources.Sort((a, b) =>
            {
                int score = b.score.CompareTo(a.score);
                return score == 0 ? a.id.CompareTo(b.id) : score;
            });

            float q = Mathf.Min(1f, plan.radius / 12f);
            float step = Mathf.Min(.30f * q, settings.riverWidthMeters * .36f);
            var attempted = new List<Vector3>();
            for (int i = 0; i < sources.Count && attempted.Count < settings.riverSourceAttempts; i++)
            {
                Vector3 source = sources[i].direction;
                if (attempted.Exists(prior => Vector3.Distance(prior, source) * plan.radius < 1.4f * q)) continue;
                attempted.Add(source);
                plan.riverSourceAttempts = attempted.Count;
                var path = Trace(plan, source, step);
                if (path == null)
                {
                    plan.riverShortTraces++;
                    continue;
                }

                SmoothDownhill(plan, path);
                if (!TryFit(plan, path, out var river))
                {
                    plan.riverRejectedBanks++;
                    continue;
                }
                plan.river = river;
                plan.riverStatus = "Downhill channel accepted.";
                return;
            }
            plan.riverStatus = $"No downhill channel after {plan.riverSourceAttempts} sources " +
                $"({plan.riverShortTraces} short traces, {plan.riverRejectedBanks} unfit banks).";
        }

        static List<Vector3> Trace(ProceduralPlanetTerrain.Plan plan, Vector3 source, float step)
        {
            var path = new List<Vector3> { source };
            var settings = plan.natural;
            float angularStep = step / plan.radius;
            float cosine = Mathf.Cos(angularStep), sine = Mathf.Sin(angularStep);
            float length = 0f;
            float height = ProceduralPlanetTerrain.FreeHeight(plan, source);
            for (int i = 1; i <= settings.riverTraceSteps && length + step <= settings.riverLengthMeters.y; i++)
            {
                var current = path[path.Count - 1];
                var tangent = Tangent(current);
                var side = Vector3.Cross(current, tangent);
                var onward = path.Count > 1 ? Vector3.ProjectOnPlane(current - path[path.Count - 2], current).normalized : Vector3.zero;
                Vector3 next = Vector3.zero;
                float best = float.PositiveInfinity, nextHeight = height;
                for (int ray = 0; ray < 16; ray++)
                {
                    float angle = ray * (Mathf.PI * 2f / 16f);
                    var heading = tangent * Mathf.Cos(angle) + side * Mathf.Sin(angle);
                    var sample = (current * cosine + heading * sine).normalized;
                    float sampleHeight = ProceduralPlanetTerrain.FreeHeight(plan, sample);
                    float score = sampleHeight - .012f * step * Vector3.Dot(heading, onward);
                    if (sampleHeight >= height - .0015f * step || score >= best) continue;
                    // Keep the ribbon away from previous nonlocal portions of the same stream.
                    int nearbySteps = Mathf.CeilToInt(settings.riverWidthMeters / step) + 2;
                    float separation = .8f * settings.riverWidthMeters / plan.radius;
                    bool visited = false;
                    for (int j = 0; j < path.Count - nearbySteps; j++)
                        if ((sample - path[j]).sqrMagnitude < separation * separation) { visited = true; break; }
                    if (visited) continue;
                    next = sample;
                    best = score;
                    nextHeight = sampleHeight;
                }
                if (next == Vector3.zero) break;
                height = nextHeight;
                path.Add(next);
                length += step;
            }

            return length >= settings.riverLengthMeters.x ? path : null;
        }

        static void SmoothDownhill(ProceduralPlanetTerrain.Plan plan, List<Vector3> path)
        {
            for (int i = 1; i < path.Count - 1; i++)
            {
                var smooth = (path[i] * .5f + (path[i - 1] + path[i + 1]) * .25f).normalized;
                float height = ProceduralPlanetTerrain.FreeHeight(plan, smooth);
                if (height <= ProceduralPlanetTerrain.FreeHeight(plan, path[i - 1]) &&
                    height >= ProceduralPlanetTerrain.FreeHeight(plan, path[i + 1]))
                    path[i] = smooth;
            }
        }

        static bool TryFit(ProceduralPlanetTerrain.Plan plan, List<Vector3> path, out TerrainWater.River river)
        {
            var settings = plan.natural;
            river = new TerrainWater.River { width = settings.riverWidthMeters, bankWidth = settings.bankTransitionMeters };
            float level = float.PositiveInfinity;
            float priorHeight = float.PositiveInfinity;
            Vector3 mean = Vector3.zero;
            for (int i = 0; i < path.Count; i++)
            {
                var direction = path[i];
                var along = path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(0, i - 1)];
                var side = Vector3.Cross(direction, along).normalized;
                float original = ProceduralPlanetTerrain.FreeHeight(plan, direction);
                float margin = settings.riverDepthMeters.x * .25f;
                float bankHeight = Mathf.Min(Bank(-1), Bank(1));
                level = Mathf.Min(level, Mathf.Min(original, bankHeight) - margin);
                if (original > priorHeight || original - level > settings.riverDepthMeters.y * .85f) return false;
                priorHeight = original;
                float slope = Mathf.Clamp01((original - level) / settings.riverDepthMeters.y);
                float depth = Mathf.Lerp(settings.riverDepthMeters.x, settings.riverDepthMeters.y, slope);
                river.directions.Add(direction);
                river.points.Add(direction * plan.radius);
                river.levels.Add(level);
                river.beds.Add(level - depth);
                river.lengths.Add(i == 0 ? 0f : river.lengths[i - 1] +
                    Vector3.Distance(direction, path[i - 1]) * plan.radius);
                mean += direction;

                float Bank(float sign)
                {
                    var bank = (direction * plan.radius + side * (sign * (settings.riverWidthMeters * .55f +
                        settings.bankTransitionMeters * .65f))).normalized;
                    return ProceduralPlanetTerrain.FreeHeight(plan, bank);
                }
            }
            if (river.lengths[river.lengths.Count - 1] < settings.riverLengthMeters.x * .995f) return false;
            river.centre = mean * (plan.radius / path.Count);
            foreach (var point in river.points)
                river.bound = Mathf.Max(river.bound, Vector3.Distance(point, river.centre));
            river.bound += river.width * .5f + river.bankWidth;
            return true;
        }

        static Vector3 Tangent(Vector3 direction)
            => Vector3.Cross(direction, Mathf.Abs(direction.y) < .8f ? Vector3.up : Vector3.right).normalized;
    }
}
