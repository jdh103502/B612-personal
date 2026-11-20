using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using B612;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class TerrainV6Comparison
{
    const string Folder = "Artifacts/TerrainAppearanceV6";
    static Camera camera;
    static RenderTexture target;
    static Texture2D pixels;

    public static object CaptureStart()
    {
        var flow = Object.FindFirstObjectByType<StarSystemFlowController>();
        if (!flow || !flow.WorldReady || flow.Progress.chapter != 1 || flow.surge.IsGameOver ||
            flow.Current != flow.SystemData.planets[0])
            throw new InvalidOperationException("Enter a fresh Chapter Journey before capturing the actual play view.");

        camera = Camera.main;
        target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
        pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
        var ground = flow.worldHost.ActiveWorld.groundRenderer.sharedMaterial;
        float legacy = ground.GetFloat("_DiagnosticLegacy");
        try
        {
            ground.SetFloat("_DiagnosticLegacy", 1f);
            SavePlay("shape-actual-play");
            ground.SetFloat("_DiagnosticLegacy", 0f);
            SavePlay("final-actual-play");
        }
        finally
        {
            ground.SetFloat("_DiagnosticLegacy", legacy);
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(target);
        }
        return new { flow.Progress.journeySeed, chapter = flow.Progress.chapter, captured = 2 };
    }

    public static object Capture(string phase, int journeySeed)
    {
        var flow = Object.FindFirstObjectByType<StarSystemFlowController>();
        if (!flow || !flow.WorldReady) throw new InvalidOperationException("Enter Chapter Journey in Play mode.");
        camera = Camera.main;
        var pose = (camera.transform.position, camera.transform.rotation, camera.farClipPlane);
        var settings = flow.worldHost.settings;
        var fixture = new GameObject("Terrain comparison");
        var host = fixture.AddComponent<PlanetWorldHost>();
        host.settings = settings;
        var rows = new List<object>();
        target = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32);
        pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
        var flat = new Material(settings.proceduralWaterMaterial);
        flat.color = new Color(.7f, .7f, .7f);
        flat.SetFloat("_Smoothness", .1f);
        var originalRenderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
            .Where(r => r != flow.player.GetComponent<Renderer>() &&
                (r.transform.IsChildOf(flow.worldHost.ActiveWorld.transform) ||
                 r.transform.IsChildOf(flow.player.transform) || r is LineRenderer)).ToArray();
        var visibility = originalRenderers.Select(r => r.enabled).ToArray();
        Directory.CreateDirectory(Folder);
        try
        {
            camera.farClipPlane = 200;
            foreach (var renderer in originalRenderers) renderer.enabled = false;
            for (int chapter = 1; chapter <= 3; chapter++)
            {
                int seed = chapter == 1 ? journeySeed : StarSystemFactory.DeriveSeed(journeySeed, 50, chapter);
                var system = StarSystemFactory.Create(seed, settings, chapter);
                foreach (int index in new[] { 0, 1 })
                {
                    var data = system.planets[index];
                    if (index == 1)
                    {
                        data.spec.terrain.sizeScale = 1f;
                        data.spec.terrain.radius = settings.nominalRadius;
                        data.spec.sizeClass = PlanetGenerationSettings.Classify(1f);
                    }
                    var world = host.Prepare(data, flow.player.GetComponent<CapsuleCollider>());
                    try
                    {
                        var plan = world.Terrain.GenerationPlan;
                        string name = $"{phase}-{journeySeed}-c{chapter}-{(index == 0 ? "start" : "normal")}";
                        var main = world.groundRenderer.sharedMaterial;
                        if (phase == "shape" && main.HasProperty("_DiagnosticLegacy")) main.SetFloat("_DiagnosticLegacy", 1f);
                        var origin = world.transform.position;
                        var pad = (plan.spawn + plan.routeTangent * .38f).normalized;
                        var heading = Vector3.ProjectOnPlane(plan.routeTangent, pad).normalized;
                        // Fixed world axes derived from the original spawn/route, independent of repaired course placements.
                        camera.transform.SetPositionAndRotation(origin + pad * (plan.radius + 4.6f) - heading * 4.9f,
                            Quaternion.LookRotation(heading * 5.8f - pad * 4.3f, pad));
                        double milliseconds = Save(name + "-near");
                        var water = world.Terrain.GetComponentsInChildren<MeshRenderer>()
                            .FirstOrDefault(r => r.gameObject.name == "Shallow connected river");
                        if (water && phase != "shape" && journeySeed == 612101 && chapter == 1)
                        {
                            water.enabled = false;
                            Save(name + "-near-no-water");
                            water.enabled = true;
                        }
                        foreach (var view in new[] { ("front", plan.routeNormal), ("side", plan.routeTangent), ("back", -plan.routeNormal) })
                        {
                            var direction = (view.Item2 + plan.spawn * .29f).normalized;
                            camera.transform.SetPositionAndRotation(origin + direction * plan.radius * 3.45f,
                                Quaternion.LookRotation(-direction, plan.spawn));
                            Save(name + "-" + view.Item1);
                            if (water && journeySeed == 612101 && chapter == 1 && view.Item1 == "back" && phase != "shape")
                            {
                                water.enabled = false;
                                Save(name + "-back-no-water");
                                water.enabled = true;
                            }
                            world.groundRenderer.sharedMaterial = flat;
                            Save(name + "-" + view.Item1 + "-flat");
                            world.groundRenderer.sharedMaterial = main;
                            if (main.HasProperty("_DiagnosticUnlit") && view.Item1 == "front")
                            {
                                main.SetFloat("_DiagnosticUnlit", 1f);
                                Save(name + "-" + view.Item1 + "-color");
                                main.SetFloat("_DiagnosticUnlit", 0f);
                            }
                        }
                        rows.Add(Measure(world, journeySeed, chapter, index, milliseconds));
                    }
                    finally { Object.DestroyImmediate(world.gameObject); }
                }
            }
            if (phase == "v5" && journeySeed == flow.Progress.journeySeed && flow.Progress.chapter == 1 &&
                flow.Current == flow.SystemData.planets[0] && !flow.surge.IsGameOver)
            {
                for (int i = 0; i < originalRenderers.Length; i++) originalRenderers[i].enabled = visibility[i];
                camera.transform.SetPositionAndRotation(pose.Item1, pose.Item2);
                SavePlay(phase + "-actual-play");
            }
            File.WriteAllText(Path.Combine(Folder, $"{phase}-{journeySeed}.json"),
                Newtonsoft.Json.JsonConvert.SerializeObject(rows, Newtonsoft.Json.Formatting.Indented));
            return new { phase, journeySeed, cases = rows.Count };
        }
        finally
        {
            camera.transform.SetPositionAndRotation(pose.Item1, pose.Item2);
            camera.farClipPlane = pose.Item3;
            for (int i = 0; i < originalRenderers.Length; i++) originalRenderers[i].enabled = visibility[i];
            Object.DestroyImmediate(fixture);
            Object.DestroyImmediate(flat);
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(target);
        }
    }

    static void SavePlay(string name)
    {
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay)
            .Select(c => (canvas: c, prior: c.worldCamera, distance: c.planeDistance)).ToArray();
        try
        {
            foreach (var entry in canvases)
            {
                entry.canvas.renderMode = RenderMode.ScreenSpaceCamera;
                entry.canvas.worldCamera = camera;
                entry.canvas.planeDistance = 1;
            }
            Canvas.ForceUpdateCanvases();
            Save(name);
        }
        finally
        {
            foreach (var entry in canvases)
            {
                entry.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                entry.canvas.worldCamera = entry.prior;
                entry.canvas.planeDistance = entry.distance;
            }
        }
    }

    static double Save(string name)
    {
        var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
        var previous = RenderTexture.active;
        try
        {
            RenderPipeline.SubmitRenderRequest(camera, request);
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 3; i++)
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            }
            watch.Stop();
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), pixels.EncodeToPNG());
            return watch.Elapsed.TotalMilliseconds / 3;
        }
        finally { RenderTexture.active = previous; }
    }

    static int Zone(ProceduralPlanetTerrain.Plan plan, Vector3 direction)
    {
        foreach (var feature in plan.features)
            if (Vector3.Dot(direction, feature.centre) > 0 &&
                ProceduralPlanetTerrain.CourseCoreDistance(feature.kind, feature.Coordinates(direction, plan.radius)) == 0)
                return 0;
        if (Mathf.Min(ProceduralPlanetTerrain.MainRouteDistance(plan, direction),
                ProceduralPlanetTerrain.ConnectorDistance(plan, direction)) <= ProceduralPlanetTerrain.ProtectedHalfWidth ||
            Vector3.Angle(plan.spawn, direction) * Mathf.Deg2Rad * plan.radius <= 1.6f) return 1;
        return 2;
    }

    static object Measure(PlanetWorldInstance world, int run, int chapter, int index, double captureMs)
    {
        var plan = world.Terrain.GenerationPlan;
        var heights = new[] { new List<float>(), new List<float>(), new List<float>() };
        const int samples = 16384;
        int misses = 0;
        for (int i = 0; i < samples; i++)
        {
            float y = 1f - 2f * (i + .5f) / samples;
            float r = Mathf.Sqrt(1f - y * y), angle = 2.39996323f * i;
            var direction = new Vector3(r * Mathf.Cos(angle), y, r * Mathf.Sin(angle));
            if (!world.surface.TryProject(direction, out var point, out _)) { misses++; continue; }
            heights[Zone(plan, direction)].Add(Vector3.Distance(point, world.transform.position) - plan.radius);
        }
        var mesh = world.Terrain.OwnedMesh;
        var points = mesh.vertices;
        var triangles = mesh.triangles;
        var areas = new[] { new double[3], new double[3], new double[3] };
        var surfaceColors = mesh.colors;
        var dominant = new double[3];
        var material = world.groundRenderer.sharedMaterial;
        float heightRange = material.GetFloat("_HeightColorRange");
        var limits = plan.version >= 6 ? material.GetVector("_VegetationRemap") : Vector4.zero;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var a = points[triangles[i]];
            var b = points[triangles[i + 1]];
            var c = points[triangles[i + 2]];
            var direction = (a + b + c).normalized;
            var normal = Vector3.Cross(b - a, c - a);
            float angle = Vector3.Angle(normal, direction);
            int zone = Zone(plan, direction);
            double area = .5 * normal.magnitude;
            areas[zone][angle <= 15 ? 0 : angle <= 30 ? 1 : 2] += area;
            if (zone == 2 && surfaceColors.Length == points.Length && plan.version >= 6)
            {
                Color mask = (surfaceColors[triangles[i]] + surfaceColors[triangles[i + 1]] +
                    surfaceColors[triangles[i + 2]]) / 3;
                float rock = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.951057f, .694658f, Vector3.Dot(normal.normalized, direction)));
                float elevation = (a + b + c).magnitude / 3 - plan.radius;
                float high = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-heightRange, heightRange, elevation));
                float fertility = mask.a * .82f + mask.r * .23f - mask.g * .35f + high * .14f;
                float meadow = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(limits.x, limits.y, fertility)) *
                    (1 - rock) * (1 - mask.b * .9f);
                float earth = (1 - meadow) * (1 - rock);
                float grass = meadow * (1 - rock);
                dominant[rock > earth && rock > grass ? 2 : grass > earth ? 1 : 0] += area;
            }
        }
        var regions = heights.Select((values, i) =>
        {
            values.Sort();
            float Q(float q) => values[Mathf.RoundToInt((values.Count - 1) * q)];
            double area = areas[i].Sum();
            return new { zone = new[] { "course", "protected", "free" }[i], count = values.Count, area,
                range = values.Count == 0 ? null : new { min = Q(0), p10 = Q(.1f), p50 = Q(.5f), p90 = Q(.9f), max = Q(1) },
                slopes = areas[i].Select(a => area > 0 ? a / area : 0).ToArray() };
        }).ToArray();
        return new { run, chapter, index, plan.seed, plan.version, plan.radius, plan.fixedPlacement, plan.safeFallback,
            plan.repairs, plan.buildAttempts, plan.validationMilliseconds, plan.traversalCosts, captureMs, misses,
            mesh.vertexCount, triangles = triangles.Length / 3, mesh.subMeshCount, regions,
            river = plan.river != null, slots = world.surface.SlotCount,
            dominantEarthGrassRock = dominant.Sum() > 0 ? dominant.Select(a => a / dominant.Sum()).ToArray() : null,
            plan = JsonUtility.ToJson(plan) };
    }
}
