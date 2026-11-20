using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using B612;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class TerrainAppearanceCapture
{
    const string DirectoryPath = "Artifacts/TerrainAppearance";

    public static object Capture(string label)
    {
        var flow = Object.FindFirstObjectByType<StarSystemFlowController>();
        if (!Application.isPlaying || !flow || !flow.WorldReady || flow.Progress.chapter != 1)
            throw new InvalidOperationException("Open the first Chapter Journey world in Play mode.");
        var world = flow.worldHost.ActiveWorld;
        var plan = world.Terrain.GenerationPlan;
        var course = plan.features[0];
        var camera = Camera.main;
        // Keep the terrain and the river. Hide the separate site and route renderers only.
        var guides = flow.Sites.GetComponentsInChildren<Renderer>()
            .Where(r => r != world.groundRenderer && !r.gameObject.name.Contains("river")).ToArray();
        var guideStates = guides.Select(r => r.enabled).ToArray();
        var other = flow.player.GetComponentsInChildren<Renderer>().Concat(flow.hand.GetComponentsInChildren<LineRenderer>()).ToArray();
        var otherStates = other.Select(r => r.enabled).ToArray();
        var sprouts = flow.surge.Spawned.Where(r => r.IsActive).SelectMany(r => r.instance.GetComponentsInChildren<Renderer>()).ToArray();
        var sproutStates = sprouts.Select(r => r.enabled).ToArray();
        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .Where(c => c.renderMode == RenderMode.ScreenSpaceOverlay)
            .Select(c => (canvas: c, camera: c.worldCamera, distance: c.planeDistance)).ToArray();
        var originalMaterials = world.groundRenderer.sharedMaterials;
        var originalPosition = camera.transform.position;
        var originalRotation = camera.transform.rotation;
        float originalFar = camera.farClipPlane;
        var flat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        ColorUtility.TryParseHtmlString(world.Data.spec.terrain.groundColor, out var representative);
        flat.color = representative;
        flat.SetFloat("_Smoothness", .1f);
        Directory.CreateDirectory(DirectoryPath);
        double renderMs = 0;
        try
        {
            foreach (var guide in guides) guide.enabled = false;
            foreach (var renderer in other) renderer.enabled = false;
            foreach (var renderer in sprouts) renderer.enabled = false;
            var origin = world.transform.position;
            var pad = course.Direction(new Vector2(-3.6f, 0f), plan.radius);
            var heading = Vector3.ProjectOnPlane(course.right, pad).normalized;
            var cameraSide = Vector3.Cross(pad, heading).normalized;
            var near = origin + pad * (plan.radius + 4.25f) - heading * 4.6f + cameraSide * 1.5f;
            var nearFocus = origin + pad * (plan.radius + .35f) + heading * 1.25f;
            camera.farClipPlane = Mathf.Max(originalFar, 200f);
            camera.transform.SetPositionAndRotation(near, Quaternion.LookRotation(nearFocus - near, pad));
            renderMs = Render(camera, label + "-near");
            if (label == "before" || label == "after")
            {
                world.groundRenderer.sharedMaterials = Enumerable.Repeat(flat, originalMaterials.Length).ToArray();
                Render(camera, label + "-flat-near");
                world.groundRenderer.sharedMaterials = originalMaterials;
            }
            var overviewDirection = (course.centre + course.north * .48f - course.right * .55f).normalized;
            camera.transform.SetPositionAndRotation(origin + overviewDirection * (plan.radius * 3.45f),
                Quaternion.LookRotation(-overviewDirection, course.north));
            Render(camera, label + "-whole");
            if (label == "before" || label == "after")
            {
                world.groundRenderer.sharedMaterials = Enumerable.Repeat(flat, originalMaterials.Length).ToArray();
                Render(camera, label + "-flat-whole");
                world.groundRenderer.sharedMaterials = originalMaterials;
                RestoreRenderers();
                camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
                foreach (var entry in canvases)
                {
                    entry.canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    entry.canvas.worldCamera = camera;
                    entry.canvas.planeDistance = 1f;
                }
                Canvas.ForceUpdateCanvases();
                Render(camera, label + "-play");
            }
        }
        finally
        {
            RestoreRenderers();
            foreach (var entry in canvases)
            {
                entry.canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                entry.canvas.worldCamera = entry.camera;
                entry.canvas.planeDistance = entry.distance;
            }
            camera.transform.SetPositionAndRotation(originalPosition, originalRotation);
            camera.farClipPlane = originalFar;
            world.groundRenderer.sharedMaterials = originalMaterials;
            Object.DestroyImmediate(flat);
        }
        var mesh = world.Terrain.OwnedMesh;
        var metrics = new
        {
            label, seed = plan.seed, plan.radius, plan.version, plan.validationMilliseconds,
            mesh.vertexCount, triangles = mesh.triangles.Length / 3, mesh.subMeshCount,
            materialCount = originalMaterials.Length,
            bumpScales = originalMaterials.Select(m => m.HasProperty("_BumpScale") ? m.GetFloat("_BumpScale") : 0f).ToArray(),
            nearRenderAndReadbackMs = renderMs,
            slopes = SlopeAreas(mesh, plan),
            river = plan.river != null
        };
        File.WriteAllText(Path.Combine(DirectoryPath, label + ".json"), Newtonsoft.Json.JsonConvert.SerializeObject(metrics, Newtonsoft.Json.Formatting.Indented));
        return metrics;

        void RestoreRenderers()
        {
            for (int i = 0; i < guides.Length; i++) guides[i].enabled = guideStates[i];
            for (int i = 0; i < other.Length; i++) other[i].enabled = otherStates[i];
            for (int i = 0; i < sprouts.Length; i++) sprouts[i].enabled = sproutStates[i];
        }
    }

    static double Render(Camera camera, string name)
    {
        var texture = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = texture };
            RenderPipeline.SubmitRenderRequest(camera, request);
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 3; i++)
            {
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            }
            watch.Stop();
            File.WriteAllBytes(Path.Combine(DirectoryPath, name + ".png"), pixels.EncodeToPNG());
            return watch.Elapsed.TotalMilliseconds / 3;
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(texture);
        }
    }

    static object SlopeAreas(Mesh mesh, ProceduralPlanetTerrain.Plan plan)
    {
        var points = mesh.vertices;
        var indices = mesh.triangles;
        var area = new double[3];
        var course = plan.features[0];
        double fixedArea = 0, total = 0;
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = points[indices[i]], b = points[indices[i + 1]], c = points[indices[i + 2]];
            Vector3 face = Vector3.Cross(b - a, c - a);
            Vector3 direction = (a + b + c).normalized;
            double size = face.magnitude * .5;
            total += size;
            var local = course.Coordinates(direction, plan.radius);
            if (Vector3.Dot(direction, course.centre) > 0 && Mathf.Abs(local.x) <= 4.5f && Mathf.Abs(local.y) <= 6.5f)
            {
                fixedArea += size;
                continue;
            }
            float slope = Vector3.Angle(face, direction);
            area[slope <= 15f ? 0 : slope <= 30f ? 1 : 2] += size;
        }
        return new { bandsOutsideCourse = area.Select(a => 100 * a / (total - fixedArea)).ToArray(),
            referenceCoursePercent = 100 * fixedArea / total };
    }
}
