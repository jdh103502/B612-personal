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
                Check(a.connectedAreaRatio >= equal.natural.minConnectedAreaRatio && a.emptyCells == 0, "Placement coverage");
                var errorA = SurfaceError(equalWorld, equal);
                var errorB = SurfaceError(normalWorld, normal);
                Check(errorA.misses == 0 && errorB.misses == 0, "Collider has holes");
                Check(errorA.max < .03f, $"Equal-angle surface error {errorA.max:F4}m");
                return new
                {
                    planetId, radius = equal.radius, river = equal.river != null, equal.riverStatus, verticesEqual = vertices, verticesNormal = normalWorld.Terrain.OwnedMesh.vertexCount,
                    meshMsEqual = equal.meshMilliseconds, meshMsNormal = normal.meshMilliseconds,
                    connected = a.connectedAreaRatio, a.candidates, candidatesNormal = b.candidates, spawnShift, siteShifts,
                    meanErrorMmEqual = errorA.mean * 1000f, maxErrorMmEqual = errorA.max * 1000f,
                    meanErrorMmNormal = errorB.mean * 1000f, maxErrorMmNormal = errorB.max * 1000f
                };
            }
            finally { UnityEngine.Object.DestroyImmediate(normalWorld.transform.root.gameObject); }
        }
        finally { UnityEngine.Object.DestroyImmediate(equalWorld.transform.root.gameObject); }
    }

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
                bool ok = (bool)walk.Invoke(null, args);
                maxSlope = Mathf.Max(maxSlope, (float)args[7]);
                Check(ok, $"{planetId} {name}: basic walk failed");
                rows.Add(new { name, seconds = (float)args[8] });
            }
            List<Vector3> Path(Vector3 from, Vector3 to)
            {
                var list = new List<Vector3> { from };
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Acos(Mathf.Clamp(Vector3.Dot(from, to), -1, 1)) * radius / 1.5f));
                for (int s = 1; s <= steps; s++) list.Add(Vector3.Slerp(from, to, s / (float)steps).normalized);
                return list;
            }

            var spawn = plan.placement.spawn.direction;
            Vector3 high = spawn, low = spawn;
            float highH = float.NegativeInfinity, lowH = float.PositiveInfinity;
            foreach (var d in Directions(4096))
            {
                if (TerrainWater.IsWet(plan, d, .6f, out _)) continue;
                float h = ProceduralPlanetTerrain.Height(plan, d);
                if (h > highH) { highH = h; high = d; }
                if (h < lowH) { lowH = h; low = d; }
            }
            Run("hillUp", Path(spawn, high));
            Run("hillDown", Path(high, spawn));
            Run("basinIn", Path(spawn, low));
            Run("basinOut", Path(low, spawn));
            if (plan.river != null)
            {
                var river = plan.river;
                foreach (float fraction in new[] { .35f, .65f })
                {
                    int i = Mathf.Clamp(Mathf.RoundToInt(fraction * (river.directions.Count - 1)), 1, river.directions.Count - 2);
                    var n = river.directions[i];
                    var side = Vector3.Cross(n, river.directions[i + 1] - river.directions[i - 1]).normalized;
                    float offset = river.width * .5f + river.bankWidth + .6f;
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
                    var shore = (river.pondDirection * Mathf.Cos(angle) - inflow * Mathf.Sin(angle)).normalized;
                    Run("pondIn", Path(shore, river.pondDirection));
                    Run("pondOut", Path(river.pondDirection, shore));
                }
            }
            return new { planetId, radius, equalAngle = plan.natural.equalAngleMesh, highMeters = highH, lowMeters = lowH, maxSlope, routes = rows };
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

    static (float mean, float max, int misses) SurfaceError(PlanetWorldInstance world, ProceduralPlanetTerrain.Plan plan)
    {
        float sum = 0f, max = 0f;
        int misses = 0, count = 0;
        var centre = world.surface.transform.position;
        foreach (var d in Directions(4096))
        {
            if (!world.surface.TryProject(d, out var point, out _)) { misses++; continue; }
            float error = Mathf.Abs((point - centre).magnitude - (plan.radius + ProceduralPlanetTerrain.Height(plan, d)));
            sum += error; max = Mathf.Max(max, error); count++;
        }
        return (sum / Mathf.Max(1, count), max, misses);
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
