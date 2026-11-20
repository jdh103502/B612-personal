using System;
using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    public static class TerrainLandforms
    {
        public const int MaxAttempts = 64;

        [Serializable]
        public sealed class Landform
        {
            public Vector3 centre;
            public float radius;
            public float height;
            public ProceduralPlanetTerrain.RouteConnector approach;
            public bool reduced;

            public float Weight(Vector3 direction, float planetRadius)
            {
                float dot = Vector3.Dot(direction, centre);
                if (dot <= Mathf.Cos(radius / planetRadius)) return 0f;
                float t = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * planetRadius / radius;
                float rounded = 1f - t * t;
                return rounded * rounded;
            }
        }

        internal static void Build(ProceduralPlanetTerrain.Plan plan)
        {
            var settings = plan.appearance;
            float scale = Mathf.Clamp(plan.radius / 12f, .3f, 1f);
            var random = new System.Random(plan.landformSeed);
            var route = ProceduralPlanetTerrain.MainRoute(plan);
            int hills = settings.broadHills;
            int basins = settings.broadBasins;
            if (plan.radius < 8f || plan.requiredKind >= 0)
            {
                hills = Mathf.Min(hills, 1);
                basins = hills == 0 ? basins : 0;
                if (hills != settings.broadHills || basins != settings.broadBasins)
                    plan.repairs.Add("Reduced landform budget for reserved or small planet.");
            }
            Place(false, hills);
            Place(true, basins);

            void Place(bool basin, int count)
            {
                if (count == 0) return;
                var candidates = new List<(Vector3 point, float score)>();
                foreach (var patch in plan.patches)
                {
                    Add(patch.centre);
                    // Use a nearby pair of regional centres to fill space between patch cores.
                    float nearest = -1;
                    Vector3 adjacent = patch.centre;
                    foreach (var other in plan.patches)
                    {
                        float dot = Vector3.Dot(patch.centre, other.centre);
                        if (patch != other && dot > nearest) { nearest = dot; adjacent = other.centre; }
                    }
                    Add(Vector3.Slerp(patch.centre, adjacent, .38f).normalized);
                }
                candidates.Sort((a, b) => b.score.CompareTo(a.score));
                Vector2 heights = basin ? settings.basinDepths : settings.hillHeights;
                Vector2 radii = basin ? settings.basinRadii : settings.hillRadii;
                int placed = 0;
                foreach (var candidate in candidates)
                {
                    if (placed == count || plan.landformAttempts == MaxAttempts) break;
                    plan.landformAttempts++;
                    var centre = candidate.point;
                    float radius = Mathf.Lerp(radii.x, radii.y, .55f + .45f * (float)random.NextDouble()) * scale;
                    float height = Mathf.Lerp(heights.x, heights.y, .60f + .40f * (float)random.NextDouble()) * scale;
                    if (basin) height = -height;
                    float clearance = Mathf.Min(ProceduralPlanetTerrain.MainRouteDistance(plan, centre),
                        ProceduralPlanetTerrain.ConnectorDistance(plan, centre));
                    // Full influence stays off the protected shoulders. The shallow edge can
                    // meet the blend into the existing path, keeping the centre well away.
                    if (clearance < radius * .65f + ProceduralPlanetTerrain.ProtectedHalfWidth + 1.2f ||
                        Vector3.Angle(plan.spawn, centre) * Mathf.Deg2Rad * plan.radius < radius * .75f + 2.2f ||
                        IsNearCourse(plan, centre, radius * .48f)) continue;
                    bool overlaps = false;
                    foreach (var other in plan.landforms)
                        if (Vector3.Angle(other.centre, centre) * Mathf.Deg2Rad * plan.radius < (other.radius + radius) * .56f)
                            overlaps = true;
                    if (overlaps || !FindApproach(plan, route, centre, out var approach)) continue;

                    plan.landforms.Add(new Landform { centre = centre, radius = radius, height = height, approach = approach });
                    placed++;
                }
                if (placed < count)
                    plan.repairs.Add($"Landforms: placed {placed}/{count} {(basin ? "basins" : "hills")} within approach and reservation bounds.");

                void Add(Vector3 direction)
                {
                    Vector2 region = TerrainRegions.BlendWeights(plan, direction);
                    float affinity = basin ? region.y : region.x;
                    candidates.Add((direction, affinity * .25f + (float)random.NextDouble()));
                }
            }
        }

        static bool IsNearCourse(ProceduralPlanetTerrain.Plan plan, Vector3 direction, float margin)
        {
            foreach (var feature in plan.features)
            {
                if (Vector3.Dot(feature.centre, direction) <= 0f) continue;
                var local = feature.Coordinates(direction, plan.radius);
                if (local.magnitude <= feature.reservedRadius + margin &&
                    ProceduralPlanetTerrain.CourseCoreDistance(feature.kind, local) <= margin + 1.35f)
                    return true;
            }
            return false;
        }

        static bool FindApproach(ProceduralPlanetTerrain.Plan plan, List<Vector3> route, Vector3 centre,
            out ProceduralPlanetTerrain.RouteConnector approach)
        {
            approach = null;
            float best = Mathf.PI * .9f;
            foreach (Vector3 anchor in route)
            {
                float angle = Vector3.Angle(anchor, centre) * Mathf.Deg2Rad;
                if (angle >= best) continue;
                bool clear = true;
                int steps = Mathf.CeilToInt(angle * plan.radius / .9f);
                for (int i = 0; i < steps; i++)
                {
                    if (!IsNearCourse(plan, Vector3.Slerp(anchor, centre, i / (float)steps), .65f)) continue;
                    clear = false;
                    break;
                }
                if (!clear) continue;
                best = angle;
                approach = new ProceduralPlanetTerrain.RouteConnector
                {
                    anchor = anchor,
                    pad = centre,
                    planeNormal = Vector3.Cross(anchor, centre).normalized
                };
            }
            return approach != null;
        }

        internal static float Height(ProceduralPlanetTerrain.Plan plan, Vector3 direction, out float basinWeight)
        {
            float hills = 0f, basins = 0f, hillWeight = 0f, lowWeight = 0f, outsideHill = 1f, outsideBasin = 1f;
            foreach (var form in plan.landforms)
            {
                float weight = form.Weight(direction, plan.radius);
                if (weight == 0f) continue;
                if (form.height > 0f)
                {
                    hills += weight * form.height;
                    hillWeight += weight;
                    outsideHill *= 1f - weight;
                }
                else
                {
                    basins += weight * form.height;
                    lowWeight += weight;
                    outsideBasin *= 1f - weight;
                }
            }
            basinWeight = 1f - outsideBasin;
            return (hillWeight > 0f ? hills / hillWeight * (1f - outsideHill) : 0f) +
                (lowWeight > 0f ? basins / lowWeight * basinWeight : 0f);
        }
    }
}
