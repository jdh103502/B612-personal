using System;
using System.Collections.Generic;
using System.Reflection;
using B612;
using UnityEngine;
using UnityEngine.SceneManagement;

// Generator v9 equal-angle ground mesh regression. Run in Play mode (any scene with a StarSystemFlowController),
// one planet per call to stay within the Pipeline main-thread budget:
//   unity command run_script --file AgentScripts/NaturalTerrainMeshValidation.cs --entry NaturalTerrainMeshValidation.Compare --args ...
// Compare: default (equal-angle) vs the v8 normalized mapping on the same seed. Walk: real GravityWalker routes on the default mesh.
public static class NaturalTerrainMeshValidation
{
    const string SettingsPath = "Assets/B612/Settings/Chapters/ChapterJourneyGeneration.asset";
    static void Check(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }

    public static object Compare(int journey, int chapter, string planetId, float size)
    {
        var settings = Settings();
        Check(settings.naturalTerrain.equalAngleMesh, "Equal-angle mesh is not the asset default");
        var equal = Prepare(settings, journey, chapter, planetId, size, true, out var equalWorld);
        try
        {
            var normal = Prepare(settings, journey, chapter, planetId, size, false, out var normalWorld);
            try
            {
                var a = equal.placement;
                var b = normal.placement;
                Check(equal.version == PlanetGenerationSettings.GeneratorVersion && equal.version >= 9 &&
                    equal.natural.equalAngleMesh && !normal.natural.equalAngleMesh, "Plan version/mapping snapshot");
                float spacing = equal.river != null ? Mathf.Min(.18f, equal.river.width * .3f) : .18f;
                int frequency = Mathf.CeilToInt(Mathf.PI * equal.radius / (2f * spacing));
                int vertices = equalWorld.Terrain.OwnedMesh.vertexCount;
                Check(vertices == 6 * frequency * frequency + 2, $"Unexpected vertex count {vertices}");
                Check(vertices < normalWorld.Terrain.OwnedMesh.vertexCount * .65f, "Equal-angle mesh did not reduce vertices");
                Check(((MeshCollider)equalWorld.ground).sharedMesh == equalWorld.Terrain.OwnedMesh, "Render/collision mesh mismatch");
                Check((equal.river != null) == (normal.river != null), "River presence changed");
                // Placement re-checks footprints on the final collider, so a borderline node may be
                // replaced by its next candidate. Report the shift instead of requiring identity.
                Check(a.sites.Length == b.sites.Length, "Site count changed");
                float Shift(Vector3 x, Vector3 y) => Mathf.Acos(Mathf.Clamp(Vector3.Dot(x, y), -1, 1)) * equal.radius;
                float spawnShift = Shift(a.spawn.direction, b.spawn.direction);
                var siteShifts = new float[a.sites.Length];
                for (int i = 0; i < a.sites.Length; i++) siteShifts[i] = Shift(a.sites[i].direction, b.sites[i].direction);
                // v15 cliffs leave some logical tile cells with no reachable candidate slot (sprouts never spawn on
                // ability-only plateaus); the game only reports them, so they are reported here too.
                Check(a.connectedAreaRatio >= equal.natural.minConnectedAreaRatio, "Placement coverage");
                var errorA = SurfaceError(equalWorld, equal);
                var errorB = SurfaceError(normalWorld, normal);
                // The game uses the equal-angle mesh; the v8 normalized mesh is only the comparison reference, so its rare
                // misses (steep v18 escarpments near its cube corners) are reported, not failed.
                Check(errorA.misses == 0, $"Collider has holes (equal-angle {errorA.misses} of 4096)");
                // Deeper v14 channels have steeper walls than the 0.18 m mesh resolves exactly; allow 15% of the
                // deepest channel (never less than the original 3 cm).
                float deepest = equal.river != null ? RiverDepths(equal.river)[2] : 0f;
                // v18 continuous relief has slopes up to about 45 degrees; a 0.18 m mesh then misses curved banks by a
                // few centimetres more, so those planets allow 9 cm outside the cliff zones.
                float allowed = Mathf.Max(.03f, .15f * deepest, equal.natural.reliefHeightMeters > 0f ? .09f : 0f);
                Check(errorA.max < allowed, $"Equal-angle surface error {errorA.max:F4}m" + worstSurface);
                return new
                {
                    planetId, radius = equal.radius, river = equal.river != null, equal.riverStatus,
                    riverDepths = RiverDepths(equal.river), verticesEqual = vertices, verticesNormal = normalWorld.Terrain.OwnedMesh.vertexCount,
                    meshMsEqual = equal.meshMilliseconds, meshMsNormal = normal.meshMilliseconds,
                    connected = a.connectedAreaRatio, a.candidates, candidatesNormal = b.candidates, spawnShift, siteShifts,
                    emptyCells = a.emptyCells, normalizedMisses = errorB.misses, cliffErrorMm = errorA.cliffMax * 1000f, cliffSamples = errorA.cliffSamples,
                    meanErrorMmEqual = errorA.mean * 1000f, maxErrorMmEqual = errorA.max * 1000f,
                    meanErrorMmNormal = errorB.mean * 1000f, maxErrorMmNormal = errorB.max * 1000f,
                    pad = Pad(equalWorld, equal)
                };
            }
            finally { UnityEngine.Object.DestroyImmediate(normalWorld.transform.root.gameObject); }
        }
        finally { UnityEngine.Object.DestroyImmediate(equalWorld.transform.root.gameObject); }
    }

    /// <summary>
    /// v21 spawn clearing on the final collider: largest height difference from the pad level inside the core (0.25 m in
    /// from its edge), steepest ground in the blend band, spawn offset from the centre, and wet ground inside the core.
    /// </summary>
    static object Pad(PlanetWorldInstance world, ProceduralPlanetTerrain.Plan plan)
    {
        var pad = plan.spawnPad;
        if (pad == null) return new { present = false, status = plan.spawnPadStatus };
        var ground = world.ground;
        var origin = world.transform.position;
        float R = plan.radius, flat = 0f, flatSlope = 0f, bandSlope = 0f; int wet = 0, samples = 0;
        var t0 = Vector3.Cross(pad.centre, Mathf.Abs(pad.centre.y) < .9f ? Vector3.up : Vector3.right).normalized;
        for (int k = 0; k <= 24; k++)
            for (int j = 0; j < 36; j++)
            {
                float metres = (pad.core * 1.15f + pad.blend) * k / 24f;
                var t = Quaternion.AngleAxis(10f * j, pad.centre) * t0;
                var d = (pad.centre * Mathf.Cos(metres / R) + t * Mathf.Sin(metres / R)).normalized;
                var w = world.transform.TransformDirection(d);
                if (!ground.Raycast(new Ray(origin + w * (R + 20f), -w), out var hit, R + 40f)) continue;
                float slope = Vector3.Angle(hit.normal, w);
                if (pad.InCore(d, -.25f))
                {
                    samples++;
                    flat = Mathf.Max(flat, Mathf.Abs(Vector3.Distance(hit.point, origin) - R - pad.level));
                    flatSlope = Mathf.Max(flatSlope, slope);
                    if (TerrainWater.IsWet(plan, d, 0f, out _)) wet++;
                }
                else if (!pad.InCore(d)) bandSlope = Mathf.Max(bandSlope, slope);
            }
        float spawnOffset = Mathf.Acos(Mathf.Clamp(Vector3.Dot(plan.placement.spawn.direction, pad.centre), -1f, 1f)) * R;
        // Analytic slope (0.15 m differences) in the band with and without the clearing: what the blend adds.
        float Slope(Vector3 d)
        {
            var a = Vector3.Cross(d, Mathf.Abs(d.y) < .9f ? Vector3.up : Vector3.right).normalized;
            var b = Vector3.Cross(d, a);
            float h = ProceduralPlanetTerrain.Height(plan, d), e = .15f / R;
            float gx = (ProceduralPlanetTerrain.Height(plan, (d + a * e).normalized) - h) / .15f;
            float gy = (ProceduralPlanetTerrain.Height(plan, (d + b * e).normalized) - h) / .15f;
            return Mathf.Atan(Mathf.Sqrt(gx * gx + gy * gy)) * Mathf.Rad2Deg;
        }
        float padMax = 0f, naturalMax = 0f; int padSteep = 0, naturalSteep = 0, bandSamples = 0;
        var keep = plan.spawnPad;
        for (int pass = 0; pass < 2; pass++)
        {
            plan.spawnPad = pass == 0 ? keep : null;
            for (int k = 0; k <= 30; k++)
                for (int j = 0; j < 48; j++)
                {
                    float metres = pad.core * .85f + (pad.core * .3f + pad.blend) * k / 30f;
                    var t = Quaternion.AngleAxis(7.5f * j, pad.centre) * t0;
                    var d = (pad.centre * Mathf.Cos(metres / R) + t * Mathf.Sin(metres / R)).normalized;
                    if (keep.InCore(d) || TerrainWater.IsWet(plan, d, .3f, out _)) continue;
                    float slope = Slope(d);
                    if (pass == 0) { bandSamples++; padMax = Mathf.Max(padMax, slope); if (slope > 35f) padSteep++; }
                    else { naturalMax = Mathf.Max(naturalMax, slope); if (slope > 35f) naturalSteep++; }
                }
        }
        plan.spawnPad = keep;
        return new { present = true, status = plan.spawnPadStatus, coreMeters = pad.core, blendMeters = pad.blend, pad.maxCut, pad.maxFill,
            pad.offsetMeters, buildMs = plan.spawnPadMilliseconds, flatMm = flat * 1000f, flatSlope, bandSlope, padMax, naturalMax, padSteep, naturalSteep, bandSamples, spawnOffset, wet, samples };
    }

    // Set to a route name to run the logged copy of the verifier loop on that route first.
    public static string DiagnoseRoute = null;
    public static object Walk(int journey, int chapter, string planetId, float size)
    {
        var settings = Settings();
        var plan = Prepare(settings, journey, chapter, planetId, size, settings.naturalTerrain.equalAngleMesh, out var world);
        var flow = UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();
        var player = flow.player.GetComponent<CapsuleCollider>();
        var scene = SceneManager.CreateScene("Natural mesh walk " + Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var probe = new GameObject("Natural mesh walk probe");
        SceneManager.MoveGameObjectToScene(probe, scene);
        probe.transform.position = new Vector3(10000, 10000, 10000);
        var rows = new List<object>();
        try
        {
            float radius = plan.radius;
            var gravity = probe.AddComponent<GravitySource>();
            gravity.surfaceRadius = radius; gravity.influenceRadius = radius + 10; gravity.terrainPlan = plan;
            gravity.strength = world.Data.spec.terrain.gravityStrength;
            var source = (MeshCollider)world.ground;
            var ground = new GameObject("Final terrain collider").AddComponent<MeshCollider>();
            ground.transform.SetParent(probe.transform, false);
            ground.cookingOptions = source.cookingOptions;
            ground.sharedMesh = source.sharedMesh;
            var actor = new GameObject("Walk probe actor");
            actor.transform.SetParent(probe.transform, false);
            var body = actor.AddComponent<Rigidbody>();
            var capsule = actor.AddComponent<CapsuleCollider>();
            capsule.radius = player.radius; capsule.height = player.height; capsule.center = player.center;
            capsule.direction = player.direction; capsule.sharedMaterial = player.sharedMaterial;
            actor.transform.localScale = player.transform.lossyScale;
            var motor = actor.AddComponent<GravityWalker>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(player.GetComponent<GravityWalker>()), motor);
            motor.enabled = false;
            var ability = actor.AddComponent<PlayerTraversalAbilities>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(player.GetComponent<PlayerTraversalAbilities>()), ability);
            ability.SetUnlocked(TraversalCapability.All);
            var walk = typeof(TerrainTraversalVerifier).GetMethod("Walk", BindingFlags.NonPublic | BindingFlags.Static);
            float maxSlope = 0f;

            void Run(string name, List<Vector3> route)
            {
                var args = new object[] { route, ground, probe.transform.position, motor, body, scene.GetPhysicsScene(), radius, 0f, 0f };
                if (name == DiagnoseRoute)
                {
                    string r = Replay(route); Check(r.Contains("PASS"), $"{planetId} {name} diagnose: {r}"); }
                bool ok = (bool)walk.Invoke(null, args);
                maxSlope = Mathf.Max(maxSlope, (float)args[7]);
                Check(ok, $"{planetId} {name}: basic walk failed. " + (ok ? "" : Replay(route)));
                rows.Add(new { name, seconds = (float)args[8] });
            }
            // Same loop as TerrainTraversalVerifier.Walk, logging the motor so a failure says where and why.
            string Replay(List<Vector3> route)
            {
                var tick = typeof(GravityWalker).GetMethod("TickTraversal", BindingFlags.NonPublic | BindingFlags.Instance);
                var origin = probe.transform.position;
                var physics = scene.GetPhysicsScene();
                var log = new System.Text.StringBuilder();
                motor.Traversal.SetAllowed(TraversalFeature.Dash, false); motor.Traversal.SetAllowed(TraversalFeature.AirJump, false); motor.Traversal.SetAllowed(TraversalFeature.Glide, false);
                log.Append($"pre v{body.linearVelocity.magnitude:F2} g{motor.Grounded} ");
                if (!ground.Raycast(new Ray(origin + route[0] * (radius + 20), -route[0]), out var hit, radius + 40)) return "no ground at start";
                body.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(route[1] - route[0], hit.normal), hit.normal);
                motor.Teleport(hit.point + hit.normal * .04f, hit.normal);
                motor.Traversal.SetInputEnabled(true);
                Physics.SyncTransforms();
                void Step() { tick.Invoke(motor, new object[] { .02f }); physics.Simulate(.02f); motor.transform.SetPositionAndRotation(body.position, body.rotation); }
                for (int i = 0; i < 12; i++) { Step(); log.Append(motor.Grounded ? "s" : "S"); }
                var startUp = (body.position - origin).normalized;
                log.Append($" settle grounded {motor.Grounded} slope {Vector3.Angle(hit.normal, route[0]):F0} wet {TerrainWater.IsWet(plan, route[0], 0f, out _)} ");
                int next = 1, air = 0;
                float length = 0; for (int i = 1; i < route.Count; i++) length += Mathf.Acos(Mathf.Clamp(Vector3.Dot(route[i - 1], route[i]), -1, 1)) * radius;
                int limit = Mathf.Min(9000, Mathf.CeilToInt((length / Mathf.Max(.1f, motor.moveSpeed) + 8) / .02f));
                log.Append($"length {length:F2} limit {limit} ");
                for (int t = 0; t < limit; t++)
                {
                    var radial = (body.position - origin).normalized;
                    float distance = Mathf.Acos(Mathf.Clamp(Vector3.Dot(radial, route[next]), -1, 1)) * radius;
                    if (distance < .22f && ++next == route.Count)
                    {
                        log.Append($"reached at {t * .02f:F2}s grounded {motor.Grounded} {(motor.Grounded ? "PASS" : "FAIL")}; next ticks:");
                        for (int k = 0; k < 6; k++) { Step(); log.Append(motor.Grounded ? " g" : " AIR"); }
                        var end = (body.position - origin).normalized;
                        return log.Append($" at {end.ToString("F4")}, slope {Vector3.Angle(motor.MovementUp, end):F0}").ToString();
                    }
                    motor.moveInput = Vector3.ProjectOnPlane(route[next] - radial, radial).normalized;
                    Step();
                    air = motor.Grounded ? 0 : air + 1;
                    float height = Vector3.Distance(body.position, origin) - radius - ProceduralPlanetTerrain.Height(plan, radial);
                    if (t % 10 == 0 || air > 4)
                        log.Append($"[{t * .02f:F2}s wp{next} d{distance:F2} g{(motor.Grounded ? 1 : 0)} air{air} up{Vector3.Angle(motor.MovementUp, radial):F0} h{height:F2} v{body.linearVelocity.magnitude:F2}] ");
                    if (air > 4) return log.Append("airborne too long").ToString();
                    if (Vector3.Distance(body.position, origin) < radius - 8) return log.Append("fell through").ToString();
                }
                return log.Append("timeout").ToString();
            }
            List<Vector3> Path(Vector3 from, Vector3 to)
            {
                var list = new List<Vector3> { from };
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Acos(Mathf.Clamp(Vector3.Dot(from, to), -1, 1)) * radius / 1.5f));
                for (int s = 1; s <= steps; s++) list.Add(Vector3.Slerp(from, to, s / (float)steps).normalized);
                return list;
            }

            // Hill and basin routes: v15 cliffs make the planet's highest point an ability-only plateau, so the
            // routes follow a walking graph (0.4 m cells, two-ring neighbours, no third of an edge steeper than the
            // placement's 35 degrees either way, dry cells clear of walls) from the spawn to the highest and the lowest cell it
            // reaches, sampled every third cell.
            int skipped = 0;
            var spawn = plan.placement.spawn.direction;
            int frequency = Mathf.Min(Mathf.CeilToInt(1.23f * radius / .4f), 44);
            var cells = PlanetSurfaceGrid.CreateTopology(frequency);
            int count = cells.Count;
            var dirs = new Vector3[count]; var heights = new float[count]; var dry = new bool[count];
            for (int i = 0; i < count; i++)
            {
                dirs[i] = (cells[i].a + cells[i].b + cells[i].c).normalized;
                heights[i] = ProceduralPlanetTerrain.Height(plan, dirs[i]);
                dry[i] = !TerrainWater.IsWet(plan, dirs[i], .6f, out _);
            }
            // Clearance: the ground changes by at most 0.35 m within 0.4 m (no cliff foot to rub, no lip to slip off;
            // 0.3 m plus room for v16 cliff-snapped mesh triangles, which reach up to one 0.18 m spacing past a lip).
            for (int i = 0; i < count; i++)
            {
                if (!dry[i]) continue;
                var t0 = Vector3.Cross(dirs[i], Mathf.Abs(dirs[i].y) < .9f ? Vector3.up : Vector3.right).normalized;
                for (int k = 0; k < 6 && dry[i]; k++)
                {
                    var t = Quaternion.AngleAxis(60f * k, dirs[i]) * t0;
                    var side = (dirs[i] * Mathf.Cos(.4f / radius) + t * Mathf.Sin(.4f / radius)).normalized;
                    dry[i] = Mathf.Abs(ProceduralPlanetTerrain.Height(plan, side) - heights[i]) <= .35f;
                }
            }
            float grade = Mathf.Tan(Mathf.Min(35f, plan.placement.slopeLimit) * Mathf.Deg2Rad);
            bool Walkable(int i, int j)
            {
                float run = Mathf.Acos(Mathf.Clamp(Vector3.Dot(dirs[i], dirs[j]), -1f, 1f)) * radius / 3f, previous = heights[i];
                for (int k = 1; k <= 3; k++)
                {
                    float h = k == 3 ? heights[j] : ProceduralPlanetTerrain.Height(plan, Vector3.Slerp(dirs[i], dirs[j], k / 3f).normalized);
                    if (Mathf.Abs(h - previous) > grade * run) return false;
                    previous = h;
                }
                return true;
            }
            int startCell = 0; float nearest = -2f;
            for (int i = 0; i < count; i++) { float v = Vector3.Dot(dirs[i], spawn); if (v > nearest && dry[i]) { nearest = v; startCell = i; } }
            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = -1;
            parent[startCell] = startCell;
            var queue = new Queue<int>(); queue.Enqueue(startCell);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                foreach (int a in cells[i].neighbors)
                {
                    if (a < 0) continue;
                    foreach (int j in new[] { a, cells[a].neighbors[0], cells[a].neighbors[1], cells[a].neighbors[2] })
                    {
                        if (j < 0 || parent[j] >= 0 || !dry[j] || !Walkable(i, j)) continue;
                        parent[j] = i; queue.Enqueue(j);
                    }
                }
            }
            int highCell = startCell, lowCell = startCell;
            for (int i = 0; i < count; i++)
            {
                if (parent[i] < 0) continue;
                if (heights[i] > heights[highCell]) highCell = i;
                if (heights[i] < heights[lowCell]) lowCell = i;
            }
            List<Vector3> GraphRoute(int target)
            {
                var chain = new List<Vector3>();
                for (int i = target; ; i = parent[i]) { chain.Add(dirs[i]); if (parent[i] == i) break; }
                chain.Reverse();
                var route = new List<Vector3> { spawn };
                for (int i = 3; i < chain.Count - 1; i += 3) route.Add(chain[i]);
                route.Add(chain[chain.Count - 1]);
                return route;
            }
            List<Vector3> Reversed(List<Vector3> route) { var copy = new List<Vector3>(route); copy.Reverse(); return copy; }
            Vector3 high = dirs[highCell], low = dirs[lowCell];
            float highH = heights[highCell], lowH = heights[lowCell];
            var up = GraphRoute(highCell);
            var down = GraphRoute(lowCell);
            Run("hillUp", up);
            Run("hillDown", Reversed(up));
            Run("basinIn", down);
            Run("basinOut", Reversed(down));
            if (plan.river != null)
            {
                var river = plan.river;
                // v17: straight crossings only where the straight line is walkable ground (canyons and the gorge are
                // for abilities, and in play a walker goes round them); the first two of these fractions.
                var fractions = new List<float>();
                foreach (float f in new[] { .35f, .65f, .2f, .8f, .5f, .45f, .55f, .3f, .7f })
                {
                    int k = Mathf.Clamp(Mathf.RoundToInt(f * (river.directions.Count - 1)), 1, river.directions.Count - 2);
                    var c = river.directions[k];
                    var across = Vector3.Cross(c, river.directions[k + 1] - river.directions[k - 1]).normalized;
                    float o = TerrainWater.WidthAt(river, k) * .5f + river.bankWidth + .6f;
                    if (!StraightWalkable(plan, (c * radius - across * o).normalized, (c * radius + across * o).normalized)) continue;
                    if (fractions.Count < 2) fractions.Add(f);
                }
                skipped += 2 - fractions.Count;
                foreach (float fraction in fractions)
                {
                    int i = Mathf.Clamp(Mathf.RoundToInt(fraction * (river.directions.Count - 1)), 1, river.directions.Count - 2);
                    var n = river.directions[i];
                    var side = Vector3.Cross(n, river.directions[i + 1] - river.directions[i - 1]).normalized;
                    float offset = TerrainWater.WidthAt(river, i) * .5f + river.bankWidth + .6f;
                    var a = (n * radius - side * offset).normalized;
                    var b = (n * radius + side * offset).normalized;
                    Run($"river{fraction:F2}AB", new List<Vector3> { a, n, b });
                    Run($"river{fraction:F2}BA", new List<Vector3> { b, n, a });
                }
                if (river.pond)
                {
                    // Wade from dry ground beyond the rim (away from the inflow) to the pond centre and back.
                    var inflow = Vector3.ProjectOnPlane(river.directions[river.directions.Count - 1] - river.pondDirection,
                        river.pondDirection).normalized;
                    float angle = (river.pondRadius + 1f) / radius;
                    // v17: away from the inflow first, then round the pond, the first shore with a walkable line in.
                    Vector3 shore = Vector3.zero; bool found = false;
                    for (int k = 0; k < 8 && !found; k++)
                    {
                        var heading = Quaternion.AngleAxis(45f * k, river.pondDirection) * -inflow;
                        shore = (river.pondDirection * Mathf.Cos(angle) + heading * Mathf.Sin(angle)).normalized;
                        found = StraightWalkable(plan, shore, river.pondDirection);
                    }
                    if (found)
                    {
                        Run("pondIn", Path(shore, river.pondDirection));
                        Run("pondOut", Path(river.pondDirection, shore));
                    }
                    else skipped += 2;
                }
            }
            return new { planetId, radius, equalAngle = plan.natural.equalAngleMesh, highMeters = highH, lowMeters = lowH, maxSlope, skipped, routes = rows };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probe);
            SceneManager.UnloadSceneAsync(scene);
            UnityEngine.Object.DestroyImmediate(world.transform.root.gameObject);
        }
    }

    /// <summary>P10-P90 spread and standard deviation of the two noise bases over the same random points.</summary>
    public static object NoiseSpread(int count)
    {
        var random = new System.Random(612);
        var value = new float[count];
        var simplex = new float[count];
        double valueGradient = 0, simplexGradient = 0;
        const float h = .001f;
        for (int i = 0; i < count; i++)
        {
            var p = new Vector3((float)random.NextDouble() * 200f - 100f, (float)random.NextDouble() * 200f - 100f,
                (float)random.NextDouble() * 200f - 100f);
            value[i] = 2f * ProceduralPlanetTerrain.Noise(p, 1234) - 1f;
            simplex[i] = ProceduralPlanetTerrain.Simplex(p, 1234);
            // Squared gradient magnitude by central differences on all three axes.
            foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                double dv = (2f * ProceduralPlanetTerrain.Noise(p + axis * h, 1234) - 2f * ProceduralPlanetTerrain.Noise(p - axis * h, 1234)) / (2f * h);
                double ds = (ProceduralPlanetTerrain.Simplex(p + axis * h, 1234) - ProceduralPlanetTerrain.Simplex(p - axis * h, 1234)) / (2f * h);
                valueGradient += dv * dv;
                simplexGradient += ds * ds;
            }
        }
        object Stats(float[] data)
        {
            var sorted = (float[])data.Clone();
            Array.Sort(sorted);
            double mean = 0, square = 0;
            foreach (float v in data) { mean += v; square += v * v; }
            mean /= data.Length;
            return new
            {
                p10 = sorted[data.Length / 10], p50 = sorted[data.Length / 2], p90 = sorted[data.Length * 9 / 10],
                min = sorted[0], max = sorted[data.Length - 1], std = Math.Sqrt(square / data.Length - mean * mean)
            };
        }
        return new
        {
            value = Stats(value), simplex = Stats(simplex),
            valueRmsGradient = Math.Sqrt(valueGradient / count), simplexRmsGradient = Math.Sqrt(simplexGradient / count)
        };
    }

    /// <summary>Height/slope statistics of the final collider for the v9 noise (legacy=true) or the current settings.</summary>
    public static object Relief(int journey, int chapter, string planetId, float size, bool legacy)
    {
        var settings = Settings();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var plan = Prepare(settings, journey, chapter, planetId, size, natural =>
        {
            if (!legacy) return;
            natural.noiseBasis = NaturalNoiseBasis.Value;
            natural.roughnessRange = Vector2.one;
        }, legacy ? -3000 : 3000, out var world);
        watch.Stop();
        try
        {
            float q = Mathf.Min(1f, plan.radius / 12f);
            var regions = PlanetSurfaceGrid.CreateTopology(1);
            var low = new float[regions.Count];
            var high = new float[regions.Count];
            for (int r = 0; r < regions.Count; r++) { low[r] = float.PositiveInfinity; high[r] = float.NegativeInfinity; }
            var heights = new List<float>();
            var centre = world.surface.transform.position;
            foreach (var d in Directions(4096))
            {
                if (!world.surface.TryProject(d, out var point, out _)) throw new InvalidOperationException("Collider hole");
                float h = (point - centre).magnitude - plan.radius;
                heights.Add(h);
                for (int r = 0; r < regions.Count; r++)
                {
                    var cell = regions[r];
                    if (Vector3.Dot(Vector3.Cross(cell.a, cell.b), d) < 0 || Vector3.Dot(Vector3.Cross(cell.b, cell.c), d) < 0 ||
                        Vector3.Dot(Vector3.Cross(cell.c, cell.a), d) < 0) continue;
                    low[r] = Mathf.Min(low[r], h); high[r] = Mathf.Max(high[r], h);
                    break;
                }
            }
            heights.Sort();
            float p10 = heights[heights.Count / 10], p50 = heights[heights.Count / 2], p90 = heights[heights.Count * 9 / 10];
            int regionsOk = 0;
            float minRegionRange = float.PositiveInfinity;
            for (int r = 0; r < regions.Count; r++)
            {
                float range = high[r] - low[r];
                minRegionRange = Mathf.Min(minRegionRange, range);
                if (range >= .35f * q) regionsOk++;
            }

            // Area-weighted slope bands of the final ground mesh, and mean slope in calm vs rough mask areas.
            var mesh = world.Terrain.OwnedMesh;
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            double total = 0, gentle = 0, moderate = 0, steep = 0;
            float maxSlope = 0f;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                var face = Vector3.Cross(b - a, c - a);
                double area = face.magnitude * .5;
                var mid = (a + b + c) / 3f;
                float slope = Vector3.Angle(face, mid);
                total += area;
                if (slope <= 15f) gentle += area; else if (slope <= 30f) moderate += area; else steep += area;
                maxSlope = Mathf.Max(maxSlope, slope);
            }

            // Local relief: |h - mean of six heights 2.5m away|, the small bumps the mask is meant to vary.
            // Split by the current settings' mask so v9 and v10 are compared on the same areas.
            var maskSettings = JsonUtility.FromJson<NaturalTerrainSettings>(JsonUtility.ToJson(settings.naturalTerrain));
            maskSettings.roughnessScaleMeters *= q;
            var maskPlan = new ProceduralPlanetTerrain.Plan
            {
                natural = maskSettings, roughnessSeed = plan.roughnessSeed, roughnessOffset = plan.roughnessOffset
            };
            double calmRelief = 0, roughRelief = 0;
            int calmCount = 0, roughCount = 0;
            foreach (var d in Directions(4096))
            {
                if (TerrainWater.IsWet(plan, d, 1f, out _)) continue;
                var tangent = Vector3.Cross(d, Mathf.Abs(d.y) < .8f ? Vector3.up : Vector3.right).normalized;
                float around = 0f;
                for (int k = 0; k < 6; k++)
                {
                    var heading = Quaternion.AngleAxis(60f * k, d) * tangent;
                    float angle = 2.5f / plan.radius;
                    around += ProceduralPlanetTerrain.Height(plan, d * Mathf.Cos(angle) + heading * Mathf.Sin(angle));
                }
                float relief = Mathf.Abs(ProceduralPlanetTerrain.Height(plan, d) - around / 6f);
                float rough = ProceduralPlanetTerrain.Roughness(maskPlan, d * plan.radius);
                var range = maskPlan.natural.roughnessRange;
                if (rough <= range.x + .1f) { calmRelief += relief; calmCount++; }
                else if (rough >= range.y - .1f) { roughRelief += relief; roughCount++; }
            }
            return new
            {
                planetId, radius = plan.radius, legacy, plan.version, prepareMs = watch.Elapsed.TotalMilliseconds,
                river = plan.river != null, plan.riverStatus, connected = plan.placement.connectedAreaRatio, plan.placement.candidates,
                p10, p50, p90, spreadOverQ = (p90 - p10) / q, min = heights[0], max = heights[heights.Count - 1],
                regionsOk, minRegionRange,
                slopePercent = new[] { 100 * gentle / total, 100 * moderate / total, 100 * steep / total }, maxSlope,
                calmShare = calmCount / 4096f, calmReliefMm = calmCount > 0 ? 1000 * calmRelief / calmCount : 0,
                roughShare = roughCount / 4096f, roughReliefMm = roughCount > 0 ? 1000 * roughRelief / roughCount : 0
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(world.transform.root.gameObject); }
    }

    /// <summary>Centre water depth (surface minus bed) along the river: min / mean / max in metres, above-water points excluded.</summary>
    static float[] RiverDepths(TerrainWater.River river)
    {
        if (river == null) return new float[3];
        float min = float.PositiveInfinity, max = 0f, sum = 0f;
        int n = 0;
        for (int i = 0; i < river.levels.Count; i++)
        {
            if (river.pond && river.levels[i] < river.pondLevel) continue; // underwater extension inside the pond
            float d = river.levels[i] - river.beds[i];
            min = Mathf.Min(min, d); max = Mathf.Max(max, d); sum += d; n++;
        }
        return n == 0 ? new float[3] : new[] { min, sum / n, max };
    }

    // ---------------------------------------------------------------------------------------------------------
    // Ground dash lift: dash from rest (forward held) toward river banks, up and over slopes and cliffs, and on open
    // ground, with the real GravityWalker. "lift" is how far the walker rises above the point where it left the ground
    // (falling off a ledge has lift 0); "clear" is the largest height above the analytic ground while airborne (walking
    // up a steep face counts in lift but stays near the ground); "vr" is the largest outward radial speed while airborne. Reference: a held
    // plain jump from rest.
    public static bool TraceDash = false;
    /// <summary>Dash with a per-tick trace of every run that lifts more than 0.4 m.</summary>
    public static object DashTrace(int journey, int chapter, string planetId, float size)
    {
        TraceDash = true;
        try { return Dash(journey, chapter, planetId, size); }
        finally { TraceDash = false; }
    }
    public static object Dash(int journey, int chapter, string planetId, float size)
    {
        var settings = Settings();
        var plan = Prepare(settings, journey, chapter, planetId, size, settings.naturalTerrain.equalAngleMesh, out var world);
        var flow = UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();
        var player = flow.player.GetComponent<CapsuleCollider>();
        var scene = SceneManager.CreateScene("Dash probe " + Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var probe = new GameObject("Dash probe");
        SceneManager.MoveGameObjectToScene(probe, scene);
        probe.transform.position = new Vector3(10000, 10000, 10000);
        try
        {
            float radius = plan.radius;
            var origin = probe.transform.position;
            var gravity = probe.AddComponent<GravitySource>();
            gravity.surfaceRadius = radius; gravity.influenceRadius = radius + 10; gravity.terrainPlan = plan;
            gravity.strength = world.Data.spec.terrain.gravityStrength;
            var ground = new GameObject("Final terrain collider").AddComponent<MeshCollider>();
            ground.transform.SetParent(probe.transform, false);
            ground.cookingOptions = ((MeshCollider)world.ground).cookingOptions;
            ground.sharedMesh = ((MeshCollider)world.ground).sharedMesh;
            var actor = new GameObject("Dash actor");
            actor.transform.SetParent(probe.transform, false);
            var body = actor.AddComponent<Rigidbody>();
            var capsule = actor.AddComponent<CapsuleCollider>();
            capsule.radius = player.radius; capsule.height = player.height; capsule.center = player.center;
            capsule.direction = player.direction; capsule.sharedMaterial = player.sharedMaterial;
            actor.transform.localScale = player.transform.lossyScale;
            var motor = actor.AddComponent<GravityWalker>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(player.GetComponent<GravityWalker>()), motor);
            motor.enabled = false;
            var ability = actor.AddComponent<PlayerTraversalAbilities>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(player.GetComponent<PlayerTraversalAbilities>()), ability);
            ability.SetUnlocked(TraversalCapability.All);
            var tick = typeof(GravityWalker).GetMethod("TickTraversal", BindingFlags.NonPublic | BindingFlags.Instance);
            var queue = typeof(PlayerTraversalAbilities).GetMethod("QueueSimulationInput", BindingFlags.NonPublic | BindingFlags.Instance);
            var physics = scene.GetPhysicsScene();
            float H(Vector3 d) => ProceduralPlanetTerrain.Height(plan, d);
            Vector3 Move(Vector3 d, Vector3 t, float metres) => (d * Mathf.Cos(metres / radius) + t * Mathf.Sin(metres / radius)).normalized;
            void Step() { tick.Invoke(motor, new object[] { .02f }); physics.Simulate(.02f); motor.transform.SetPositionAndRotation(body.position, body.rotation); }

            // One run: settle at rest facing t, press dash (or jump) and hold forward; lift above the take-off point.
            float Run(Vector3 start, Vector3 t, bool jump, out float vr, out float travel, out float clear, System.Text.StringBuilder log = null)
            {
                vr = 0f; travel = 0f; clear = 0f;
                t = Vector3.ProjectOnPlane(t, start).normalized;
                if (!ground.Raycast(new Ray(origin + start * (radius + 20), -start), out var hit, radius + 40)) return float.NaN;
                body.linearVelocity = Vector3.zero;
                body.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(t, hit.normal), hit.normal);
                motor.Teleport(hit.point + hit.normal * .04f, hit.normal);
                motor.Traversal.SetAllowed(TraversalFeature.Dash, true);
                motor.Traversal.SetAllowed(TraversalFeature.AirJump, false);
                motor.Traversal.SetAllowed(TraversalFeature.Glide, false);
                motor.Traversal.SetInputEnabled(true);
                Physics.SyncTransforms();
                motor.moveInput = Vector3.zero; motor.jumpHeld = false;
                for (int i = 0; i < 15; i++) Step();
                if (!motor.Grounded) return float.NaN;
                var from = (body.position - origin).normalized;
                queue.Invoke(motor.Traversal, new object[] { jump, !jump });
                motor.jumpHeld = jump;
                float takeoff = float.NaN, lift = 0f;
                for (int i = 0; i < 100; i++)
                {
                    var radial = (body.position - origin).normalized;
                    motor.moveInput = Vector3.ProjectOnPlane(Quaternion.FromToRotation(start, radial) * t, radial).normalized;
                    bool dashing = motor.Traversal.IsDashing, guard = motor.Traversal.DashRiseGuardActive;
                    Step();
                    float r = Vector3.Distance(body.position, origin);
                    if (log != null) { var rd = (body.position - origin).normalized; log.Append($"[{i} g{(motor.Grounded ? 1 : 0)} d{(dashing ? 1 : 0)}{(guard ? "G" : "")} h{r - radius - H(rd):F2} r{r - radius:F2} vr{Vector3.Dot(body.linearVelocity, rd):F1} sl{Vector3.Angle(motor.MovementUp, rd):F0}] "); }
                    if (!motor.Grounded)
                    {
                        if (float.IsNaN(takeoff)) takeoff = r;
                        lift = Mathf.Max(lift, r - takeoff);
                        clear = Mathf.Max(clear, r - radius - H((body.position - origin).normalized));
                        vr = Mathf.Max(vr, motor.RadialSpeed);
                    }
                    else if (!float.IsNaN(takeoff) && i > 3) break;
                }
                travel = Mathf.Acos(Mathf.Clamp(Vector3.Dot(from, (body.position - origin).normalized), -1f, 1f)) * radius;
                return lift;
            }

            var random = new System.Random(StarSystemFactory.DeriveSeed(journey, 4242, chapter));
            Vector3 RandomTangent(Vector3 d) => Quaternion.AngleAxis((float)random.NextDouble() * 360f, d) * Vector3.Cross(d, Mathf.Abs(d.y) < .9f ? Vector3.up : Vector3.right).normalized;
            var cases = new Dictionary<string, List<(Vector3 start, Vector3 t, float slope)>>
            {
                ["bankUp"] = new List<(Vector3, Vector3, float)>(), ["bankAcross"] = new List<(Vector3, Vector3, float)>(),
                ["slopeUp"] = new List<(Vector3, Vector3, float)>(), ["ledgeOff"] = new List<(Vector3, Vector3, float)>(),
                ["plain"] = new List<(Vector3, Vector3, float)>()
            };
            float SlopeAlong(Vector3 d, Vector3 t) => Mathf.Atan2(H(Move(d, t, .3f)) - H(Move(d, t, -.3f)), .6f) * Mathf.Rad2Deg;
            var river = plan.river;
            if (river != null)
            {
                for (int s = 0; s < 24; s++)
                {
                    int k = Mathf.Clamp(Mathf.RoundToInt((.05f + .9f * s / 23f) * (river.directions.Count - 1)), 1, river.directions.Count - 2);
                    var c = river.directions[k];
                    var across = Vector3.Cross(c, river.directions[k + 1] - river.directions[k - 1]).normalized;
                    float half = TerrainWater.WidthAt(river, k) * .5f;
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        // From the shallow edge of the channel, dash out up the bank.
                        var inside = Move(c, across * sign, Mathf.Max(0f, half - .5f));
                        cases["bankUp"].Add((inside, across * sign, SlopeAlong(Move(c, across * sign, half + river.bankWidth * .5f), across * sign)));
                        // From dry ground beyond the bank, dash toward the river.
                        var outside = Move(c, across * sign, half + river.bankWidth + 1f);
                        cases["bankAcross"].Add((outside, -across * sign, SlopeAlong(Move(c, across * sign, half + river.bankWidth * .5f), across * sign)));
                    }
                }
            }
            // Slopes and cliffs from random points: the steepest uphill of 8 headings.
            for (int s = 0; s < 4000 && (cases["slopeUp"].Count < 40 || cases["ledgeOff"].Count < 30 || cases["plain"].Count < 30); s++)
            {
                var d = new Vector3((float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1, (float)random.NextDouble() * 2 - 1);
                if (d.sqrMagnitude < .01f || d.sqrMagnitude > 1f) continue;
                d.Normalize();
                if (TerrainWater.IsWet(plan, d, .3f, out _)) continue;
                var t0 = RandomTangent(d);
                float best = -90f; Vector3 bestT = t0;
                for (int k = 0; k < 8; k++) { var t = Quaternion.AngleAxis(45f * k, d) * t0; float a = SlopeAlong(Move(d, t, 1f), t); if (a > best) { best = a; bestT = t; } }
                float here = SlopeAlong(d, bestT);
                if (best >= 20f && best <= 62f && here < 15f && cases["slopeUp"].Count < 40) cases["slopeUp"].Add((d, bestT, best));
                else if (best >= 25f && here < 15f && cases["ledgeOff"].Count < 30)
                {
                    // Stand on top of that slope and dash back down over its lip.
                    var top = Move(d, bestT, 2.2f);
                    if (SlopeAlong(top, bestT) < 12f) cases["ledgeOff"].Add((top, -bestT, best));
                }
                else if (best < 12f && cases["plain"].Count < 30) cases["plain"].Add((d, t0, best));
            }

            var rows = new List<object>();
            var worst = new List<string>();
            foreach (var pair in cases)
            {
                var lifts = new List<float>(); var clears = new List<float>(); float maxVr = 0f, travel = 0f; int n = 0;
                var examples = new List<(float lift, string text)>();
                foreach (var c in pair.Value)
                {
                    float lift = Run(c.start, c.t, false, out float vr, out float moved, out float clear);
                    if (float.IsNaN(lift)) continue;
                    n++; lifts.Add(lift); clears.Add(clear); maxVr = Mathf.Max(maxVr, vr); travel += moved;
                     string text = $"{pair.Key} lift {lift:F2} clear {clear:F2} vr {vr:F1} slope {c.slope:F0} at {c.start.ToString("F3")}";
                    if (TraceDash && lift > .4f) { var log = new System.Text.StringBuilder(" trace "); Run(c.start, c.t, false, out _, out _, out _, log); text += log.ToString(); }
                    examples.Add((lift, text));
                }
                lifts.Sort(); clears.Sort(); examples.Sort((a, b) => b.lift.CompareTo(a.lift));
                for (int i = 0; i < Mathf.Min(3, examples.Count); i++) worst.Add(examples[i].text);
                float Q(float q) => lifts.Count == 0 ? 0f : lifts[Mathf.Clamp(Mathf.RoundToInt(q * (lifts.Count - 1)), 0, lifts.Count - 1)];
                rows.Add(new { name = pair.Key, runs = n, liftMedian = Q(.5f), liftP90 = Q(.9f), liftMax = Q(1f), over30cm = lifts.FindAll(l => l > .3f).Count, clearMax = clears.Count == 0 ? 0f : clears[clears.Count - 1], clearOver30cm = clears.FindAll(l => l > .3f).Count, maxVr, meanTravel = n == 0 ? 0f : travel / n });
            }
            // Reference: a held plain jump from rest on open ground.
            var jumps = new List<float>();
            foreach (var c in cases["plain"]) { float l = Run(c.start, c.t, true, out _, out _, out _); if (!float.IsNaN(l)) jumps.Add(l); }
            jumps.Sort();
            return new { planetId, radius, jumpLiftMedian = jumps.Count == 0 ? 0f : jumps[jumps.Count / 2], cases = rows, worst };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probe);
            SceneManager.UnloadSceneAsync(scene);
            UnityEngine.Object.DestroyImmediate(world.transform.root.gameObject);
        }
    }

    static PlanetGenerationSettings Settings()
    {
        Check(Application.isPlaying, "Use Play mode");
#if UNITY_EDITOR
        var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>(SettingsPath);
#else
        PlanetGenerationSettings settings = null;
#endif
        Check(settings && settings.profileId == PlanetGenerationSettings.NaturalProfileId, "Natural chapter settings missing");
        return settings;
    }

    /// <summary>A straight line is walkable: every 0.15 m step climbs or drops at most 35 degrees.</summary>
    static bool StraightWalkable(ProceduralPlanetTerrain.Plan plan, Vector3 from, Vector3 to)
    {
        float length = Mathf.Acos(Mathf.Clamp(Vector3.Dot(from, to), -1f, 1f)) * plan.radius;
        int steps = Mathf.Max(2, Mathf.CeilToInt(length / .15f));
        float grade = Mathf.Tan(35f * Mathf.Deg2Rad) * length / steps;
        float previous = ProceduralPlanetTerrain.Height(plan, from);
        for (int i = 1; i <= steps; i++)
        {
            float h = ProceduralPlanetTerrain.Height(plan, Vector3.Slerp(from, to, i / (float)steps).normalized);
            if (Mathf.Abs(h - previous) > grade) return false;
            previous = h;
        }
        return true;
    }

    // ---------------------------------------------------------------------------------------------------------
    // v15 ability shortcuts: where do dash, double jump and glide save real time over walking on this planet?
    // Candidates come from the analytic height: a cliff base below a 1.9-2.9 m higher tread (double jump), a 3-5 m
    // gap over a dip at least 2 m deep between similar heights (dash) and a high edge 4-12 m from a landing 0.3-4 m
    // lower across a valley at least 2.2 m below the landing (glide): ground a walker would have to climb out of. Each candidate is flown with the real GravityWalker and PlayerTraversalAbilities twice, with only
    // that ability and with a plain jump, and compared with walking time on a 0.4 m surface graph (climbs <= 40 deg,
    // drops free, water at 65% speed). A shortcut needs the ability (the plain jump fails) and saves >= 1.5x the time, or
    // reaches ground walking cannot reach.
    public static bool TraceDoubleJump = false;
    public static object Shortcuts(int journey, int chapter, string planetId, float size, bool rugged)
    {
        var settings = Settings();
        var plan = Prepare(settings, journey, chapter, planetId, size, n => { if (!rugged) n.ruggedStrength = 0f; }, 1000, out var world);
        var flow = UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();
        var player = flow.player.GetComponent<CapsuleCollider>();
        var scene = SceneManager.CreateScene("Shortcut probe " + Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var probe = new GameObject("Shortcut probe");
        SceneManager.MoveGameObjectToScene(probe, scene);
        probe.transform.position = new Vector3(10000, 10000, 10000);
        try
        {
            float radius = plan.radius;
            var origin = probe.transform.position;
            var gravity = probe.AddComponent<GravitySource>();
            gravity.surfaceRadius = radius; gravity.influenceRadius = radius + 10; gravity.terrainPlan = plan;
            gravity.strength = world.Data.spec.terrain.gravityStrength;
            var ground = new GameObject("Final terrain collider").AddComponent<MeshCollider>();
            ground.transform.SetParent(probe.transform, false);
            ground.cookingOptions = ((MeshCollider)world.ground).cookingOptions;
            ground.sharedMesh = ((MeshCollider)world.ground).sharedMesh;
            var actor = new GameObject("Shortcut actor");
            actor.transform.SetParent(probe.transform, false);
            var body = actor.AddComponent<Rigidbody>();
            var capsule = actor.AddComponent<CapsuleCollider>();
            capsule.radius = player.radius; capsule.height = player.height; capsule.center = player.center;
            capsule.direction = player.direction; capsule.sharedMaterial = player.sharedMaterial;
            actor.transform.localScale = player.transform.lossyScale;
            var motor = actor.AddComponent<GravityWalker>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(player.GetComponent<GravityWalker>()), motor);
            motor.enabled = false;
            var ability = actor.AddComponent<PlayerTraversalAbilities>();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(player.GetComponent<PlayerTraversalAbilities>()), ability);
            ability.SetUnlocked(TraversalCapability.All);
            var tick = typeof(GravityWalker).GetMethod("TickTraversal", BindingFlags.NonPublic | BindingFlags.Instance);
            var queue = typeof(PlayerTraversalAbilities).GetMethod("QueueSimulationInput", BindingFlags.NonPublic | BindingFlags.Instance);
            var physics = scene.GetPhysicsScene();
            float H(Vector3 d) => ProceduralPlanetTerrain.Height(plan, d);
            float Arc(Vector3 a, Vector3 b) => Mathf.Acos(Mathf.Clamp(Vector3.Dot(a, b), -1f, 1f)) * radius;

            // Walking graph: grid cells with their two-ring neighbours. An edge is walkable when no third of it
            // climbs faster than 40 degrees; going down is free (a walker can step off any edge).
            int frequency = Mathf.Min(Mathf.CeilToInt(1.23f * radius / .4f), 44); // at most about 39k nodes
            var cells = PlanetSurfaceGrid.CreateTopology(frequency);
            int count = cells.Count;
            var dirs = new Vector3[count]; var heights = new float[count]; var wet = new bool[count];
            for (int i = 0; i < count; i++)
            {
                dirs[i] = (cells[i].a + cells[i].b + cells[i].c).normalized;
                heights[i] = H(dirs[i]);
                wet[i] = TerrainWater.IsWet(plan, dirs[i], 0f, out _);
            }
            var edges = new List<int>[count];
            float maxGrade = Mathf.Tan(40f * Mathf.Deg2Rad);
            for (int i = 0; i < count; i++)
            {
                var set = new HashSet<int>();
                foreach (int a in cells[i].neighbors)
                {
                    if (a < 0) continue;
                    set.Add(a);
                    foreach (int b in cells[a].neighbors) if (b >= 0 && b != i) set.Add(b);
                }
                edges[i] = new List<int>();
                foreach (int j in set)
                {
                    float run = Arc(dirs[i], dirs[j]) / 3f;
                    bool ok = true;
                    float previous = heights[i];
                    for (int k = 1; k <= 3 && ok; k++)
                    {
                        float h = k == 3 ? heights[j] : H(Vector3.Slerp(dirs[i], dirs[j], k / 3f).normalized);
                        ok = h - previous <= maxGrade * run;
                        previous = h;
                    }
                    if (ok) edges[i].Add(j);
                }
            }
            int Nearest(Vector3 d)
            {
                int best = 0; float dot = -2f;
                for (int i = 0; i < count; i++) { float v = Vector3.Dot(dirs[i], d); if (v > dot) { dot = v; best = i; } }
                return best;
            }
            float WalkSeconds(Vector3 from, Vector3 to)
            {
                int source = Nearest(from), target = Nearest(to);
                // A* toward the target (straight-line time is an admissible estimate).
                var cost = new float[count];
                for (int i = 0; i < count; i++) cost[i] = float.PositiveInfinity;
                var open = new SortedSet<(float, int)>();
                float Estimate(int i) => Arc(dirs[i], dirs[target]) / motor.moveSpeed;
                cost[source] = 0f; open.Add((Estimate(source), source));
                while (open.Count > 0)
                {
                    var (f, i) = open.Min;
                    open.Remove(open.Min);
                    if (i == target) return cost[i];
                    foreach (int j in edges[i])
                    {
                        float next = cost[i] + Arc(dirs[i], dirs[j]) / (motor.moveSpeed * (wet[i] || wet[j] ? .65f : 1f));
                        if (next >= cost[j]) continue;
                        if (!float.IsInfinity(cost[j])) open.Remove((cost[j] + Estimate(j), j));
                        cost[j] = next;
                        open.Add((next + Estimate(j), j));
                    }
                }
                return float.PositiveInfinity;
            }

            // Candidates. kind 0 = dash gap, 1 = double-jump ledge, 2 = glide.
            var candidates = new List<(int kind, Vector3 a, Vector3 b)>[3];
            for (int k = 0; k < 3; k++) candidates[k] = new List<(int kind, Vector3 a, Vector3 b)>();
            for (int i = 0; i < count; i += 2)
            {
                if (wet[i]) continue;
                var up = dirs[i];
                var t0 = Vector3.Cross(up, Mathf.Abs(up.y) < .9f ? Vector3.up : Vector3.right).normalized;
                float ha = heights[i];
                for (int s = 0; s < 8; s++)
                {
                    var t = Quaternion.AngleAxis(45f * s, up) * t0;
                    Vector3 At(float metres) => (up * Mathf.Cos(metres / radius) + t * Mathf.Sin(metres / radius)).normalized;
                    // The run-up behind the take-off point must be walkable ground at the same level.
                    if (Mathf.Abs(H(At(-1.2f)) - ha) > .35f || Mathf.Abs(H(At(-.6f)) - ha) > .25f) continue;
                    // Double jump: a cliff face starts 0.35-0.6 m ahead (the take-off point is just before it) and
                    // within 1.2 m more the ground has risen 1.9-2.9 m (a wall, not a step) to a tread that stays level for 0.8 m.
                    float face = -1f;
                    for (float d = .1f; d <= .6f; d += .05f) if (H(At(d)) - ha > .25f) { face = d; break; }
                    if (face >= .35f)
                        for (float d = face + .4f; d <= face + 1.2f; d += .2f)
                        {
                            var b = At(d);
                            float rise = H(b) - ha;
                            if (rise < 1.9f || rise > 2.9f || Mathf.Abs(H(At(d + .8f)) - H(b)) > .3f) continue;
                            candidates[1].Add((1, up, b));
                            break;
                        }
                    // Dash: 3-5 m away at a similar height with a dip of at least 2 m between.
                    for (float d = 3f; d <= 5f; d += .5f)
                    {
                        var b = At(d);
                        float hb = H(b);
                        if (Mathf.Abs(hb - ha) > .6f || H(At(d + .7f)) < hb - .3f) continue;
                        float low = float.PositiveInfinity;
                        for (float m = .5f; m < d - .4f; m += .3f) low = Mathf.Min(low, H(At(m)));
                        if (low > Mathf.Min(ha, hb) - 2f) continue;
                        candidates[0].Add((0, up, b));
                        break;
                    }
                    // Glide: 4-12 m away, 0.3-4 m lower, across a valley at least 2.2 m below the landing; the ground
                    // between stays below the glide line.
                    for (float d = 4f; d <= 12f; d += 1f)
                    {
                        var b = At(d);
                        float hb = H(b), drop = ha - hb;
                        if (drop < .3f || drop > 4f || Mathf.Abs(H(At(d + .7f)) - hb) > .3f || TerrainWater.IsWet(plan, b, .3f, out _)) continue;
                        bool clear = true;
                        float low = float.PositiveInfinity;
                        bool water = false;
                        for (float m = .6f; m < d && clear; m += .4f)
                        {
                            float h = H(At(m));
                            clear = h < ha - drop * (m / d) - .1f;
                            low = Mathf.Min(low, h);
                            water |= TerrainWater.IsWet(plan, At(m), 0f, out _);
                        }
                        if (!clear || low > hb - 2.2f) continue;
                        candidates[2].Add((2, up, b));
                        break;
                    }
                }
            }
            var found = new int[3];
            // Keep a spread: no two take-off points of the same kind within 2.5 m, at most 24 per kind.
            for (int k = 0; k < 3; k++)
            {
                found[k] = candidates[k].Count;
                var kept = new List<(int kind, Vector3 a, Vector3 b)>();
                var random = new System.Random(plan.seed + k);
                var shuffled = new List<(int kind, Vector3 a, Vector3 b)>(candidates[k]);
                for (int i = shuffled.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]); }
                foreach (var c in shuffled)
                {
                    bool far = true;
                    foreach (var o in kept) if (Arc(o.a, c.a) < 2.5f) { far = false; break; }
                    if (far) kept.Add(c);
                    if (kept.Count >= 24) break;
                }
                candidates[k] = kept;
            }

            // Fly one attempt: run up 1.2 m, jump at the take-off point, use the ability, land near the target.
            void Step() { tick.Invoke(motor, new object[] { .02f }); physics.Simulate(.02f); motor.transform.SetPositionAndRotation(body.position, body.rotation); }
            System.Text.StringBuilder trace = null;
            float Fly(int kind, Vector3 a, Vector3 b, bool useAbility, out bool landed)
            {
                landed = false;
                var t = Vector3.ProjectOnPlane(b - a, a).normalized;
                var start = (a * Mathf.Cos(1.2f / radius) - t * Mathf.Sin(1.2f / radius)).normalized;
                if (!ground.Raycast(new Ray(origin + start * (radius + 8), -start), out var hit, radius + 16)) return 0f;
                body.linearVelocity = Vector3.zero;
                body.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(t, hit.normal), hit.normal);
                motor.Teleport(hit.point + hit.normal * .04f, hit.normal);
                motor.Traversal.SetAllowed(TraversalFeature.Dash, useAbility && kind == 0);
                motor.Traversal.SetAllowed(TraversalFeature.AirJump, useAbility && kind == 1);
                motor.Traversal.SetAllowed(TraversalFeature.Glide, useAbility && kind == 2);
                motor.Traversal.SetInputEnabled(true);
                Physics.SyncTransforms();
                motor.moveInput = Vector3.zero;
                motor.jumpHeld = false;
                for (int i = 0; i < 12; i++) Step();
                bool jumped = false, used = false;
                float flight = 0f, hb = H(b);
                for (int i = 0; i < 400; i++)
                {
                    var radial = (body.position - origin).normalized;
                    motor.moveInput = Vector3.ProjectOnPlane(b - radial, radial).normalized;
                    motor.jumpHeld = jumped; // a held jump keeps its full height (glide is only allowed for kind 2)
                    if (!jumped && motor.Grounded && Arc(radial, start) >= 1.15f)
                    {
                        queue.Invoke(motor.Traversal, new object[] { true, false });
                        jumped = true;
                        motor.jumpHeld = true;
                    }
                    if (jumped)
                    {
                        flight += .02f;
                        if (useAbility && !used && kind == 0 && flight >= .5f) { queue.Invoke(motor.Traversal, new object[] { false, true }); used = true; }
                        if (useAbility && !used && kind == 1 && flight >= .65f) { queue.Invoke(motor.Traversal, new object[] { true, false }); used = true; }
                    }
                    Step();
                    if (trace != null && jumped)
                    {
                        var now = (body.position - origin).normalized;
                        trace.Append($"[{flight:F2} fwd{Arc(now, a) * Mathf.Sign(Vector3.Dot(now - a, t)):F2} h{Vector3.Distance(body.position, origin) - radius - H(a):F2} g{(motor.Grounded ? 1 : 0)} vr{motor.RadialSpeed:F1} air{motor.Traversal.AirJumpsRemaining}] ");
                    }
                    if (jumped && flight > .25f && motor.Grounded)
                    {
                        var at = (body.position - origin).normalized;
                        landed = Arc(at, b) < 1.8f && H(at) > hb - .35f;
                        return 1.2f / motor.moveSpeed + flight;
                    }
                }
                return 0f;
            }

            var names = new[] { "dash", "doubleJump", "glide" };
            var rows = new List<object>();
            var examples = new List<string>();
            for (int k = 0; k < 3; k++)
            {
                int abilityOk = 0, plainOk = 0, shortcuts = 0, unreachable = 0;
                var ratios = new List<float>();
                var plainRises = new List<string>(); var needRises = new List<string>();
                foreach (var c in candidates[k])
                {
                    float seconds = Fly(k, c.a, c.b, true, out bool ok);
                    if (false && !ok && k == 1 && TraceDoubleJump && trace == null)
                    {
                        trace = new System.Text.StringBuilder($"target fwd {Arc(c.a, c.b):F2} rise {H(c.b) - H(c.a):F2}; profile:");
                        var tt = Vector3.ProjectOnPlane(c.b - c.a, c.a).normalized;
                        for (float m = -.4f; m <= 2.01f; m += .1f) trace.Append($" {m:F1}:{H((c.a * Mathf.Cos(m / radius) + tt * Mathf.Sin(m / radius)).normalized) - H(c.a):F2}");
                        trace.Append(" || ");
                        Fly(k, c.a, c.b, true, out _);
                        examples.Add(trace.ToString());
                    }
                    if (!ok) continue;
                    abilityOk++;
                    Fly(k, c.a, c.b, false, out bool plain);
                    if (plain && k == 1 && TraceDoubleJump && trace == null && H(c.b) - H(c.a) > 2f)
                    {
                        trace = new System.Text.StringBuilder($"PLAIN target fwd {Arc(c.a, c.b):F2} rise {H(c.b) - H(c.a):F2}; profile:");
                        var tt = Vector3.ProjectOnPlane(c.b - c.a, c.a).normalized;
                        for (float m = -.2f; m <= 2.01f; m += .1f) trace.Append($" {m:F1}:{H((c.a * Mathf.Cos(m / radius) + tt * Mathf.Sin(m / radius)).normalized) - H(c.a):F2}");
                        trace.Append(" || ");
                        Fly(k, c.a, c.b, false, out _);
                        examples.Add(trace.ToString());
                    }
                    if (plain) { plainOk++; plainRises.Add((H(c.b) - H(c.a)).ToString("F1")); continue; }
                    needRises.Add((H(c.b) - H(c.a)).ToString("F1"));
                    var start = (c.a * Mathf.Cos(1.2f / radius) - Vector3.ProjectOnPlane(c.b - c.a, c.a).normalized * Mathf.Sin(1.2f / radius)).normalized;
                    float walk = WalkSeconds(start, c.b);
                    if (float.IsInfinity(walk))
                    {
                        unreachable++; shortcuts++;
                        if (examples.Count < 9) examples.Add($"{names[k]} only (no walking route) {seconds:F1}s at {c.a.ToString("F3")}");
                        continue;
                    }
                    float ratio = walk / Mathf.Max(.1f, seconds);
                    ratios.Add(ratio);
                    if (ratio >= 1.5f)
                    {
                        shortcuts++;
                        if (examples.Count < 9) examples.Add($"{names[k]} {seconds:F1}s vs walk {walk:F1}s at {c.a.ToString("F3")}");
                    }
                }
                ratios.Sort();
                rows.Add(new
                {
                    ability = names[k], found = found[k], tested = candidates[k].Count, abilityOk, plainJumpAlsoOk = plainOk,
                    shortcuts, walkUnreachable = unreachable, medianWalkRatio = ratios.Count == 0 ? 0f : ratios[ratios.Count / 2],
                    plainRises = string.Join(",", plainRises), needRises = string.Join(",", needRises)
                });
            }
            return new { planetId, radius, rugged, nodes = count, rows, examples };
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probe);
            SceneManager.UnloadSceneAsync(scene);
            UnityEngine.Object.DestroyImmediate(world.transform.root.gameObject);
        }
    }

    static ProceduralPlanetTerrain.Plan Prepare(PlanetGenerationSettings settings, int journey, int chapter, string planetId,
        float size, bool equalAngle, out PlanetWorldInstance world)
        => Prepare(settings, journey, chapter, planetId, size, natural => natural.equalAngleMesh = equalAngle,
            equalAngle ? 1000 : -1000, out world);

    // Prepares one planet off to the side; the settings are changed only for this call and restored.
    static ProceduralPlanetTerrain.Plan Prepare(PlanetGenerationSettings settings, int journey, int chapter, string planetId,
        float size, Action<NaturalTerrainSettings> configure, float offset, out PlanetWorldInstance world)
    {
        var flow = UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();
        var data = StarSystemFactory.Create(StarSystemFactory.DeriveSeed(journey, 50, chapter), settings, chapter)
            .planets.Find(p => p.spec.planetId == planetId);
        Check(data != null, "Unknown planet " + planetId);
        if (size > 0f)
        {
            data.spec.terrain.sizeScale = size;
            data.spec.terrain.radius = settings.nominalRadius * size;
            data.spec.sizeClass = PlanetGenerationSettings.Classify(size);
        }
        var root = new GameObject("Natural mesh validation " + offset);
        root.transform.position = new Vector3(offset, 0, 0);
        var host = root.AddComponent<PlanetWorldHost>();
        host.settings = settings;
        string original = JsonUtility.ToJson(settings.naturalTerrain);
        try
        {
            configure(settings.naturalTerrain);
            world = host.Prepare(data, flow.player.GetComponent<CapsuleCollider>());
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(root);
            throw;
        }
        finally { JsonUtility.FromJsonOverwrite(original, settings.naturalTerrain); }
        return world.Terrain.GenerationPlan;
    }

    // v15-v17: in the rugged highlands and along canyons and gorges a mesh of 0.18 m spacing cannot follow near-vertical faces and sharp edges,
    // so those samples are kept apart (cliffMax, reported) from the strict surface error, which covers the rest.
    static string worstSurface = "";
    static (float mean, float max, int misses, float cliffMax, int cliffSamples) SurfaceError(PlanetWorldInstance world, ProceduralPlanetTerrain.Plan plan)
    {
        float sum = 0f, max = 0f, cliffMax = 0f;
        worstSurface = "";
        int misses = 0, count = 0, cliff = 0;
        var centre = world.surface.transform.position;
        foreach (var d in Directions(4096))
        {
            if (!world.surface.TryProject(d, out var point, out _)) { misses++; continue; }
            float h = ProceduralPlanetTerrain.Height(plan, d);
            float error = Mathf.Abs((point - centre).magnitude - (plan.radius + h));
            if (ProceduralPlanetTerrain.InCliffZone(plan, d) || NearWall(plan, d, h)) { cliff++; cliffMax = Mathf.Max(cliffMax, error); continue; }
            if (error > max)
            {
                TerrainWater.SampleWidth(plan, d, out float riverDistance, out float riverWidth);
                worstSurface = $" at {d.ToString("F3")} h {h:F2} river distance {riverDistance:F2} (width {riverWidth:F2}) wet {TerrainWater.IsWet(plan, d, 0f, out _)}";
            }
            sum += error; max = Mathf.Max(max, error); count++;
        }
        return (sum / Mathf.Max(1, count), max, misses, cliffMax, cliff);
    }

    /// <summary>Any wall: the ground changes by more than 0.3 m within 0.45 m (the mesh spans such a step with one triangle).</summary>
    static bool NearWall(ProceduralPlanetTerrain.Plan plan, Vector3 d, float h)
    {
        if (plan.natural == null || plan.natural.ruggedStrength <= 0f && plan.natural.reliefHeightMeters <= 0f) return false;
        var t0 = Vector3.Cross(d, Mathf.Abs(d.y) < .9f ? Vector3.up : Vector3.right).normalized;
        for (int k = 0; k < 8; k++)
        {
            var t = Quaternion.AngleAxis(45f * k, d) * t0;
            var side = (d * Mathf.Cos(.45f / plan.radius) + t * Mathf.Sin(.45f / plan.radius)).normalized;
            if (Mathf.Abs(ProceduralPlanetTerrain.Height(plan, side) - h) > .3f) return true;
        }
        return false;
    }

    static IEnumerable<Vector3> Directions(int count)
    {
        for (int i = 0; i < count; i++)
        {
            float y = 1f - 2f * (i + .5f) / count;
            float ring = Mathf.Sqrt(1f - y * y);
            float angle = i * 2.39996323f;
            yield return new Vector3(ring * Mathf.Cos(angle), y, ring * Mathf.Sin(angle));
        }
    }
}
