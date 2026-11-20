using System;
using System.Diagnostics;
using System.Reflection;
using B612;
using UnityEngine;

// Breaks the natural-terrain preparation cost into stages. Play mode; one planet per call:
//   unity command run_script --file AgentScripts/TerrainPrepProfile.cs --entry TerrainPrepProfile.Run --args "[612101,2,\"system_02_B\",2]"
public static class TerrainPrepProfile
{
    public static object Run(int journey, int chapter, string planetId, float size)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Use Play mode");
#if UNITY_EDITOR
        var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>("Assets/B612/Settings/Chapters/ChapterJourneyGeneration.asset");
#else
        PlanetGenerationSettings settings = null;
#endif
        var flow = UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();
        var capsule = flow.player.GetComponent<CapsuleCollider>();
        var data = StarSystemFactory.Create(StarSystemFactory.DeriveSeed(journey, 50, chapter), settings, chapter).planets.Find(p => p.spec.planetId == planetId);
        if (size > 0f)
        {
            data.spec.terrain.sizeScale = size;
            data.spec.terrain.radius = settings.nominalRadius * size;
            data.spec.sizeClass = PlanetGenerationSettings.Classify(size);
        }
        var root = new GameObject("Terrain prep profile");
        root.transform.position = new Vector3(2000, 0, 0);
        var host = root.AddComponent<PlanetWorldHost>();
        host.settings = settings;
        try
        {
            var total = Stopwatch.StartNew();
            var world = host.Prepare(data, capsule);
            total.Stop();
            var plan = world.Terrain.GenerationPlan;
            var placement = plan.placement;
            var watch = new Stopwatch();

            // Height-field evaluation only, over the same vertex directions.
            var mesh = world.Terrain.OwnedMesh;
            var vertices = mesh.vertices;
            watch.Restart();
            float sink = 0f;
            foreach (var v in vertices)
            {
                var n = v.normalized;
                float e = ProceduralPlanetTerrain.FreeHeight(plan, n);
                sink += TerrainWater.Height(plan, n, e);
                TerrainWater.Sample(plan, n, out _, out _, out _);
            }
            double heightMs = watch.Elapsed.TotalMilliseconds;

            var build = typeof(ProceduralPlanetTerrain).GetMethod("BuildMesh", BindingFlags.NonPublic | BindingFlags.Static);
            watch.Restart();
            var rebuilt = (Mesh)build.Invoke(null, new object[] { plan });
            double buildMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            rebuilt.RecalculateNormals();
            double normalsMs = watch.Elapsed.TotalMilliseconds;

            var collider = (MeshCollider)world.ground;
            var defaults = collider.cookingOptions;
            watch.Restart();
            Physics.BakeMesh(rebuilt.GetInstanceID(), false, defaults);
            double bakeDefaultMs = watch.Elapsed.TotalMilliseconds;
            var lean = defaults & ~(MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.EnableMeshCleaning);
            var copy = UnityEngine.Object.Instantiate(rebuilt);
            watch.Restart();
            Physics.BakeMesh(copy.GetInstanceID(), false, lean);
            double bakeLeanMs = watch.Elapsed.TotalMilliseconds;
            var probe = new GameObject("bake probe").AddComponent<MeshCollider>();
            probe.transform.SetParent(root.transform, false);
            probe.cookingOptions = defaults;
            watch.Restart();
            probe.sharedMesh = rebuilt; // pre-baked with the same options
            double assignBakedMs = watch.Elapsed.TotalMilliseconds;
            probe.sharedMesh = null;
            var unbaked = UnityEngine.Object.Instantiate(rebuilt);
            watch.Restart();
            probe.sharedMesh = unbaked; // cooks on assignment
            double assignCookMs = watch.Elapsed.TotalMilliseconds;

            // Placement primitives on the real collider.
            int samples = placement.nodes;
            var surface = world.surface;
            watch.Restart();
            int hits = 0;
            for (int i = 0; i < samples; i++)
            {
                float y = 1f - 2f * (i + .5f) / samples, ring = Mathf.Sqrt(1f - y * y), a = i * 2.39996323f;
                if (surface.TryProject(new Vector3(ring * Mathf.Cos(a), y, ring * Mathf.Sin(a)), out _, out _)) hits++;
            }
            double raycastMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            int overlaps = 0;
            var ground = surface.ground;
            for (int i = 0; i < samples; i++)
            {
                float y = 1f - 2f * (i + .5f) / samples, ring = Mathf.Sqrt(1f - y * y), a = i * 2.39996323f;
                var d = new Vector3(ring * Mathf.Cos(a), y, ring * Mathf.Sin(a));
                surface.TryProject(d, out var p, out var nrm);
                if (Physics.ComputePenetration(capsule, p + nrm * settings.spawnClearance, Quaternion.FromToRotation(Vector3.up, nrm),
                    ground, ground.transform.position, ground.transform.rotation, out _, out _)) overlaps++;
            }
            double penetrationMs = watch.Elapsed.TotalMilliseconds - raycastMs;
            watch.Restart();
            for (int i = 0; i < samples; i++)
            {
                float y = 1f - 2f * (i + .5f) / samples, ring = Mathf.Sqrt(1f - y * y), a = i * 2.39996323f;
                sink += ProceduralPlanetTerrain.Height(plan, new Vector3(ring * Mathf.Cos(a), y, ring * Mathf.Sin(a)));
            }
            double analyticMs = watch.Elapsed.TotalMilliseconds;

            UnityEngine.Object.DestroyImmediate(rebuilt);
            UnityEngine.Object.DestroyImmediate(copy);
            UnityEngine.Object.DestroyImmediate(unbaked);
            return new
            {
                planetId, plan.radius, vertices = vertices.Length, triangles = mesh.GetIndexCount(0) / 3,
                totalMs = total.Elapsed.TotalMilliseconds, plan.riverPlanMilliseconds, plan.meshMilliseconds,
                placement.analysisMilliseconds, placement.candidateMilliseconds, placement.nodes, placement.linkedEdges,
                heightEvalMs = heightMs, buildMeshMs = buildMs, recalcNormalsMs = normalsMs,
                bakeDefaultMs, bakeLeanMs, assignBakedMs, assignCookMs, cooking = defaults.ToString(),
                raycastPerNodeSetMs = raycastMs, penetrationPerNodeSetMs = penetrationMs, analyticHeightPerNodeSetMs = analyticMs,
                hits, overlaps, sink
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
}
