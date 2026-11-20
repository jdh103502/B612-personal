using System;
using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    /// <summary>Conservative walking connectivity for placement on the finished collider.</summary>
    public sealed class NaturalTerrainPlacement
    {
        [Serializable]
        public struct Location
        {
            public Vector3 direction, localPoint, localNormal;
            public int node;
        }

        [Serializable]
        public sealed class Report
        {
            public int nodes, standable, linkedEdges, candidates, emptyCells;
            public float connectedAreaRatio, slopeLimit, analysisMilliseconds, candidateMilliseconds;
            public Location spawn;
            public Location[] sites = Array.Empty<Location>();
        }

        struct Node
        {
            public Vector3 direction, point, normal;
            public float area;
            public bool safe;
        }

        readonly ProceduralPlanetTerrain.Plan plan;
        readonly PlanetSurfaceGrid surface;
        readonly CapsuleCollider capsule;
        readonly float clearance, cosSlope, maxGrade, binSize;
        readonly Node[] nodes;
        readonly int[] parents;
        readonly Dictionary<Vector3Int, List<int>> bins = new Dictionary<Vector3Int, List<int>>();
        int selected;

        internal NaturalTerrainPlacement(ProceduralPlanetTerrain.Plan plan, PlanetSurfaceGrid surface,
            CapsuleCollider capsule, float clearance, Vector3 preferred, bool campaign)
        {
            this.plan = plan;
            this.surface = surface;
            this.capsule = capsule;
            this.clearance = clearance;
            var report = plan.placement = new Report();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            float motorLimit = capsule && capsule.TryGetComponent<GravityWalker>(out var motor) ? motor.maxGroundAngle : 60f;
            report.slopeLimit = Mathf.Min(35f, motorLimit - 5f);
            if (report.slopeLimit <= 0f) throw Failure("The configured motor leaves no safe placement slope.");
            cosSlope = Mathf.Cos(report.slopeLimit * Mathf.Deg2Rad);
            maxGrade = Mathf.Tan(report.slopeLimit * Mathf.Deg2Rad);
            int topologyLimit = Mathf.FloorToInt(Mathf.Sqrt(plan.natural.maxAnalysisNodes / 20f));
            int frequency = Mathf.Clamp(Mathf.CeilToInt(.82f * plan.radius / plan.natural.analysisSpacingMeters), 2, topologyLimit);
            var cells = PlanetSurfaceGrid.CreateTopology(frequency);
            binSize = 1.23f * plan.radius / frequency;
            nodes = new Node[cells.Count];
            parents = new int[cells.Count];
            report.nodes = cells.Count;

            float totalArea = 0f;
            for (int i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];
                var direction = (cell.a + cell.b + cell.c).normalized;
                bool projected = surface.TryProject(direction, out var point, out var normal);
                float radius = projected ? (point - surface.transform.position).magnitude : plan.radius;
                float up = projected ? Vector3.Dot(normal, surface.transform.TransformDirection(direction)) : 1f;
                float area = cell.solidAngle * radius * radius / Mathf.Max(.25f, up);
                bool safe = projected && up >= cosSlope && Clear(point, normal);
                nodes[i] = new Node { direction = direction, point = point, normal = normal, area = area, safe = safe };
                parents[i] = i;
                totalArea += area;
                if (safe) report.standable++;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                if (!nodes[i].safe) continue;
                foreach (int neighbor in cells[i].neighbors)
                {
                    if (neighbor <= i || !nodes[neighbor].safe ||
                        !CanConnect(nodes[i].direction, nodes[i].point, nodes[neighbor].direction, nodes[neighbor].point))
                        continue;
                    int a = Root(i), b = Root(neighbor);
                    parents[Mathf.Max(a, b)] = Mathf.Min(a, b);
                    report.linkedEdges++;
                }
            }

            var areas = new float[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
                if (nodes[i].safe) areas[Root(i)] += nodes[i].area;
            for (int i = 1; i < areas.Length; i++)
                if (areas[i] > areas[selected]) selected = i;
            report.connectedAreaRatio = areas[selected] / totalArea;
            if (report.connectedAreaRatio < plan.natural.minConnectedAreaRatio)
                throw Failure($"Largest walking component is {report.connectedAreaRatio:P1} of the surface, " +
                    $"below {plan.natural.minConnectedAreaRatio:P1} at seed {plan.seed}.");

            var dry = new List<int>();
            for (int i = 0; i < nodes.Length; i++)
            {
                if (!nodes[i].safe || Root(i) != selected) continue;
                var key = Bin(nodes[i].direction);
                if (!bins.TryGetValue(key, out var list)) bins.Add(key, list = new List<int>());
                list.Add(i);
                if (!TerrainWater.IsWet(plan, nodes[i].direction, .22f, out _)) dry.Add(i);
            }

            report.spawn = FindLocation(preferred, dry, new List<Location>());
            if (campaign) report.sites = PlaceSites(dry, report.spawn);
            watch.Stop();
            report.analysisMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
        }

        Location[] PlaceSites(List<int> dry, Location spawn)
        {
            var sites = new Location[3];
            var used = new List<Location> { spawn };
            var random = new System.Random(plan.placementSeed);
            var tangent = Vector3.Cross(spawn.direction,
                Mathf.Abs(spawn.direction.y) < .8f ? Vector3.up : Vector3.right).normalized;
            float bearing = (float)random.NextDouble() * 360f;
            for (int i = 0; i < sites.Length; i++)
            {
                var heading = Quaternion.AngleAxis(bearing + i * 120f, spawn.direction) * tangent;
                float metres = Mathf.Clamp((.31f + i * .11f) * plan.radius, 3.2f, 9f);
                float radians = metres / plan.radius;
                sites[i] = FindLocation((spawn.direction * Mathf.Cos(radians) + heading * Mathf.Sin(radians)).normalized, dry, used);
                used.Add(sites[i]);
            }
            return sites;
        }

        Location FindLocation(Vector3 preferred, List<int> dry, List<Location> used)
        {
            dry.Sort((a, b) =>
            {
                int score = Vector3.Dot(nodes[b].direction, preferred).CompareTo(Vector3.Dot(nodes[a].direction, preferred));
                return score == 0 ? a.CompareTo(b) : score;
            });
            int attempts = 0;
            foreach (int index in dry)
            {
                var node = nodes[index];
                bool separated = true;
                foreach (var prior in used)
                    if (Vector3.Distance(prior.direction, node.direction) * plan.radius < plan.natural.siteSpacingMeters)
                    { separated = false; break; }
                if (!separated) continue;
                if (++attempts > plan.natural.siteCandidateLimit) break;
                var tangent = Vector3.Cross(node.direction,
                    Mathf.Abs(node.direction.y) < .8f ? Vector3.up : Vector3.right).normalized;
                float footprint = (used.Count == 0 ? .24f : .38f) / plan.radius;
                var across = Vector3.Cross(node.direction, tangent);
                if (!DryFootprint(node.direction, tangent * footprint) || !DryFootprint(node.direction, across * footprint))
                    continue;
                return new Location
                {
                    node = index, direction = node.direction,
                    localPoint = surface.transform.InverseTransformPoint(node.point),
                    localNormal = surface.transform.InverseTransformDirection(node.normal)
                };
            }
            throw Failure($"No dry, separated {(used.Count == 0 ? "spawn" : "interaction site")} near " +
                $"{preferred.ToString("F4")} for seed {plan.seed} after {Mathf.Min(attempts, plan.natural.siteCandidateLimit)} candidates.");
        }

        internal bool AllowsBaobab(Vector3 point, Vector3 normal)
        {
            var direction = surface.transform.InverseTransformPoint(point).normalized;
            if (Vector3.Dot(normal, surface.transform.TransformDirection(direction)) < cosSlope ||
                TerrainWater.IsWet(plan, direction, .24f, out _) || !Clear(point, normal)) return false;
            if (Vector3.Distance(plan.placement.spawn.direction, direction) * plan.radius < 1.1f) return false;
            foreach (var site in plan.placement.sites)
                if (Vector3.Distance(site.direction, direction) * plan.radius < .8f) return false;

            var bin = Bin(direction);
            float best = binSize * binSize * 1.4f / (plan.radius * plan.radius);
            int closest = -1;
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                if (!bins.TryGetValue(bin + new Vector3Int(x, y, z), out var list)) continue;
                foreach (int index in list)
                {
                    float squared = (nodes[index].direction - direction).sqrMagnitude;
                    if (squared >= best) continue;
                    best = squared;
                    closest = index;
                }
            }
            return closest >= 0 && CanConnect(direction, point, nodes[closest].direction, nodes[closest].point);
        }

        internal void CheckCapacity(int count, float spacing)
        {
            var separated = new List<Vector3>(count);
            foreach (var cell in surface.Cells)
            foreach (var slot in cell.slots)
            {
                var point = surface.WorldPoint(slot);
                if (separated.Exists(existing => LambCardHand.SurfaceDistance(existing, point, surface.transform.position) < spacing))
                    continue;
                separated.Add(point);
                if (separated.Count == count) return;
            }
            throw Failure($"Only {separated.Count} mutually separated baobab positions for {count} active sprouts.");
        }

        bool CanConnect(Vector3 a, Vector3 pointA, Vector3 b, Vector3 pointB)
        {
            float distance = Vector3.Distance(a, b) * plan.radius;
            if (distance < .001f) return true;
            float step = plan.river == null ? .30f : Mathf.Min(.30f, plan.river.width * .38f);
            int intervals = Mathf.Max(2, Mathf.CeilToInt(distance / step));
            var previousDirection = a;
            var previousPoint = pointA;
            for (int i = 1; i < intervals; i++)
            {
                var direction = Vector3.Slerp(a, b, i / (float)intervals);
                if (!TryStand(direction, out var point) || !WithinGrade(previousDirection, previousPoint, direction, point))
                    return false;
                previousDirection = direction;
                previousPoint = point;
            }
            return WithinGrade(previousDirection, previousPoint, b, pointB);
        }

        bool WithinGrade(Vector3 a, Vector3 pointA, Vector3 b, Vector3 pointB)
        {
            var origin = surface.transform.position;
            float rise = Mathf.Abs((pointA - origin).magnitude - (pointB - origin).magnitude);
            return rise <= maxGrade * Vector3.Distance(a, b) * plan.radius + .025f;
        }

        bool TryStand(Vector3 direction, out Vector3 point)
        {
            return surface.TryProject(direction, out point, out var normal) &&
                Vector3.Dot(normal, surface.transform.TransformDirection(direction)) >= cosSlope && Clear(point, normal);
        }

        bool DryFootprint(Vector3 centre, Vector3 offset)
        {
            var a = (centre + offset).normalized;
            var b = (centre - offset).normalized;
            return !TerrainWater.IsWet(plan, a, 0f, out _) && !TerrainWater.IsWet(plan, b, 0f, out _) &&
                TryStand(a, out _) && TryStand(b, out _);
        }

        bool Clear(Vector3 point, Vector3 normal)
        {
            if (!capsule) return true;
            var ground = surface.ground;
            var pose = Quaternion.FromToRotation(Vector3.up, normal);
            return !Physics.ComputePenetration(capsule, point + normal * clearance, pose,
                ground, ground.transform.position, ground.transform.rotation, out _, out float depth) || depth <= .01f;
        }

        Vector3Int Bin(Vector3 direction) => Vector3Int.FloorToInt(direction * (plan.radius / binSize));

        int Root(int index)
        {
            while (parents[index] != index)
            {
                parents[index] = parents[parents[index]];
                index = parents[index];
            }
            return index;
        }

        static InvalidOperationException Failure(string reason) => new InvalidOperationException("Natural terrain placement: " + reason);
    }
}
