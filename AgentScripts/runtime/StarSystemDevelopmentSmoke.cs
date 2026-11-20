#if DEVELOPMENT_BUILD && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace B612
{
    // Opt-in local build verification. No listener, network API or release-build code.
    public sealed class StarSystemDevelopmentSmoke : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Launch()
        {
            var arguments = Environment.GetCommandLineArgs();
            if (!arguments.Contains("-b612-map-smoke") && !arguments.Contains("-b612-terrain-smoke")) return;
            var root=new GameObject("Development map smoke test");DontDestroyOnLoad(root);root.AddComponent<StarSystemDevelopmentSmoke>();
        }
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        IEnumerator Start()
        {
            Application.runInBackground=true;
            bool terrain = Environment.GetCommandLineArgs().Contains("-b612-terrain-smoke");
            string marker = terrain ? "B612_TERRAIN_SMOKE" : "B612_MAP_SMOKE";
            var routine = terrain ? VerifyTerrain() : Verify();
            while(true)
            {
                object current;
                try{if(!routine.MoveNext())break;current=routine.Current;}
                catch(Exception error){Debug.LogError(marker + "_FAILED: " + error);Application.Quit(1);yield break;}
                yield return current;
            }
            Debug.Log(marker + "_PASSED");
            Application.Quit(0);
        }
        IEnumerator VerifyTerrain()
        {
            yield return null;
            var flow = FindFirstObjectByType<StarSystemFlowController>();
            Check(flow && flow.WorldReady && flow.CampaignEnabled && flow.gameObject.scene.buildIndex == 0, "Campaign start");
            var world = flow.worldHost.ActiveWorld;
            var mesh = world.Terrain.OwnedMesh;
            var material = world.groundRenderer.sharedMaterial;
            bool natural = flow.worldHost.settings.profileId == PlanetGenerationSettings.NaturalProfileId;
            if (natural) VerifyNaturalWorld(world, requireRiver: true);
            Check(material && material.shader.name == "B612/Planet Ground" && material.shader.isSupported &&
                material.GetTexture("_Paper"), "Missing Player shader or paper texture");
            Check(material.FindPass("Forward") >= 0 && material.FindPass("ShadowCaster") >= 0 &&
                material.FindPass("DepthOnly") >= 0 && material.FindPass("DepthNormals") >= 0, "Missing shader pass");
            Check(mesh.subMeshCount == 1 && mesh == ((MeshCollider)world.ground).sharedMesh, "Player mesh differs");
            yield return new WaitForEndOfFrame();
            string folder = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Artifacts", "TerrainAppearanceV6");
            System.IO.Directory.CreateDirectory(folder);
            var camera = Camera.main;
            Check(camera, "Missing Player camera");
            CaptureFrame(camera, System.IO.Path.Combine(folder, "player-start.png"));
            if (natural) CaptureNaturalRiver(camera, world, System.IO.Path.Combine(folder, "player-river.png"));
            var depthResult = RenderDepth(camera, world.groundRenderer, material);
            Check(depthResult.covered > 32 && depthResult.clear > 32, "DepthOnly did not render the ground silhouette");
            Debug.Log($"B612_TERRAIN_PLAYER: {material.shader.name}, DepthOnly pixels {depthResult.covered}/{depthResult.total}, " +
                $"seed {world.Terrain.GenerationPlan.seed}, mesh {mesh.vertexCount} vertices, {material.passCount} passes");

            var template = flow.worldHost.settings.proceduralGroundMaterial;
            var maps = VerifyGroundTextures(material, template);
            var memory = maps.Select(texture => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture));
            Debug.Log($"B612_GROUND_TEXTURES: {string.Join(", ", maps.Select(t => $"{t.name}: {t.graphicsFormat}"))}; bytes {string.Join(", ", memory)}");

            var timing = CompareGroundGpuTime(flow, material);
            while (timing.MoveNext()) yield return timing.Current;

            var walker = flow.player.GetComponent<GravityWalker>();
            var unlockDirection = (flow.Sites.Positions[0] - world.transform.position).normalized;
            Check(world.surface.TryProject(unlockDirection, out var point, out var normal), "Unlock site ground");
            walker.Teleport(point + normal * .04f, normal);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!flow.Sites.CanInteract(0) && Time.realtimeSinceStartup < deadline) yield return null;
            Check(flow.RequestSiteInteraction(flow.Sites, 0), "Unlock interaction");
            yield return null;
            yield return null;
            Check(flow.HasCapabilities(TraversalCapability.Dash), "Dash did not unlock");

            var debug = DeveloperDebugController.Active;
            debug.Open();
            Check(debug.RequestBaobabSkip(), "Player debug skip rejected");
            yield return null;
            yield return null;
            Check(flow.surge.IsComplete, "Player debug skip incomplete");
            debug.Close();
            yield return null;
            flow.RequestMap();
            yield return null;
            yield return null;
            Check(flow.IsMapOpen, "Player map did not open after debug skip");
            var destination = flow.SystemData.planets.Where(p => flow.CanTravel(p.spec.planetId))
                .OrderBy(p => p.spec.terrain.groundColor == world.Data.spec.terrain.groundColor).FirstOrDefault();
            Check(destination != null && flow.RequestTravel(destination.spec.planetId), "Player route request");
            deadline = Time.realtimeSinceStartup + 15f;
            while (flow.CompletedTransitions == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            var next = flow.worldHost.ActiveWorld;
            Check(flow.Current == destination && next != world, "Player route commit");
            if (natural) VerifyNaturalWorld(next, requireRiver: false);
            var nextMaterial = next.groundRenderer.sharedMaterial;
            Check(VerifyGroundTextures(nextMaterial, template).SequenceEqual(maps) &&
                nextMaterial.GetColor("_BaseColor") != template.GetColor("_BaseColor"), "Next planet maps or palette");
            flow.player.Respawn();
            Check(next.groundRenderer.sharedMaterial == nextMaterial, "Respawn ground changed");
            yield return new WaitForEndOfFrame();
            CaptureFrame(camera, System.IO.Path.Combine(folder, "player-next.png"));
            Debug.Log($"B612_GROUND_TRAVEL: {destination.spec.planetId}, color {destination.spec.terrain.groundColor}, " +
                $"scale {destination.spec.terrain.sizeScale:F3}, seed {next.Terrain.GenerationPlan.seed}");
        }

        static void VerifyNaturalWorld(PlanetWorldInstance world, bool requireRiver)
        {
            var plan = world.Terrain.GenerationPlan;
            Check(plan.natural != null && plan.placement != null && plan.features.Count == 0 &&
                plan.connectors.Count == 0 && plan.landforms.Count == 0 && plan.placement.sites.Length == 3 &&
                plan.placement.connectedAreaRatio >= plan.natural.minConnectedAreaRatio &&
                world.GetComponentsInChildren<LineRenderer>(true).All(line =>
                    line.name == "Lamb placement range" || line.name == "Intended sprout"), "Natural player terrain and placement");
            if (requireRiver) Check(plan.river != null && world.Terrain.WaterMesh, "Missing start river in Player");
            if (plan.river != null)
            {
                int mid = plan.river.directions.Count / 2;
                var direction = plan.river.directions[mid];
                Check(world.Terrain.WaterMesh && world.surface.TryProject(direction, out var ground, out _) &&
                    TerrainWater.MovementFactor(plan, direction, Vector3.Distance(ground, world.transform.position) - plan.radius) == .65f,
                    "Natural Player river or movement factor");
            }
            Debug.Log($"B612_NATURAL_PLAYER: {world.Data.spec.planetId}, {plan.profileId} v{plan.version}, " +
                $"seed {plan.seed}, R {plan.radius:F2}, area {plan.placement.connectedAreaRatio:P1}, " +
                $"river vertices {(world.Terrain.WaterMesh ? world.Terrain.WaterMesh.vertexCount : 0)}");
        }

        static void CaptureNaturalRiver(Camera camera, PlanetWorldInstance world, string path)
        {
            var plan = world.Terrain.GenerationPlan;
            var river = plan.river;
            int mid = river.directions.Count / 2;
            var up = world.transform.TransformDirection(river.directions[mid]);
            var along = world.transform.TransformDirection((river.directions[mid + 1] - river.directions[mid - 1]).normalized);
            var side = Vector3.Cross(up, along).normalized;
            var channel = world.transform.position + up * (plan.radius + river.levels[mid]);
            var prior = (camera.transform.position, camera.transform.rotation);
            try
            {
                var position = channel + up * 5.5f + side * 3.5f - along * 3f;
                camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(channel + along - position, up));
                CaptureFrame(camera, path);
            }
            finally { camera.transform.SetPositionAndRotation(prior.position, prior.rotation); }
        }

        static Texture2D[] VerifyGroundTextures(Material material, Material template)
        {
            Check(material && material != template && material.shader.isSupported &&
                material.GetFloat("_GroundTexturesEnabled") == 1 &&
                template.GetFloat("_GroundTexturesEnabled") == 1 &&
                Mathf.Approximately(material.GetFloat("_DirtTileMeters"), template.GetFloat("_DirtTileMeters")) &&
                Mathf.Approximately(material.GetFloat("_GrassTileMeters"), template.GetFloat("_GrassTileMeters")),
                "Generated ground material settings");
            return new[] { "_DirtAlbedo", "_DirtNormal", "_GrassAlbedo", "_GrassNormal" }.Select(property =>
            {
                var texture = material.GetTexture(property) as Texture2D;
                Check(texture && texture == template.GetTexture(property) && texture.mipmapCount > 1 &&
                    !texture.isReadable && texture.wrapMode == TextureWrapMode.Repeat, "Missing ground map: " + property);
                return texture;
            }).ToArray();
        }

        static IEnumerator CompareGroundGpuTime(StarSystemFlowController flow, Material material)
        {
            var timing = new FrameTiming[1];
            var samples = new[] { new List<double>(), new List<double>() };
            var rig = flow.player.cameraRig;
            var timeScale = Time.timeScale;
            var vsync = QualitySettings.vSyncCount;
            var targetRate = Application.targetFrameRate;
            try
            {
                Time.timeScale = 0;
                rig.enabled = false;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 120;
                foreach (int enabled in new[] { 0, 1, 1, 0 })
                {
                    material.SetFloat("_GroundTexturesEnabled", enabled);
                    for (int i = 0; i < 100; i++)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        yield return null;
                        if (i >= 30 && FrameTimingManager.GetLatestTimings(1, timing) == 1 && timing[0].gpuFrameTime > 0)
                            samples[enabled].Add(timing[0].gpuFrameTime);
                    }
                }
            }
            finally
            {
                material.SetFloat("_GroundTexturesEnabled", 1);
                rig.enabled = true;
                Time.timeScale = timeScale;
                QualitySettings.vSyncCount = vsync;
                Application.targetFrameRate = targetRate;
            }
            var summary = samples.Select(values =>
            {
                values.Sort();
                return values.Count == 0 ? "unavailable" : $"{values[values.Count / 2]:F3} ms ({values.Count} samples)";
            });
            Debug.Log($"B612_GROUND_GPU: {Screen.width}x{Screen.height}, {SystemInfo.graphicsDeviceType}, " +
                $"{SystemInfo.graphicsDeviceName}, timing enabled {FrameTimingManager.IsFeatureEnabled()}, " +
                $"timer {FrameTimingManager.GetGpuTimerFrequency()} Hz, off/on {string.Join(" / ", summary)}");
        }

        static void CaptureFrame(Camera camera, string path)
        {
            var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                var request = new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest
                    { destination = target };
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                System.IO.File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Destroy(image);
                Destroy(target);
            }
        }

        static (int covered, int clear, int total) RenderDepth(Camera camera, Renderer ground, Material material)
        {
            // Exercise the shipped pass directly, independent of the current URP
            // quality level's camera depth-copy policy and RenderGraph texture lifetime.
            const int size = 128;
            var target = new RenderTexture(size, size, 24, RenderTextureFormat.RFloat);
            var image = new Texture2D(size, size, TextureFormat.RFloat, false, true);
            var previous = RenderTexture.active;
            using (var commands = new UnityEngine.Rendering.CommandBuffer())
            {
                try
                {
                    var clearColor = new Color(.5f, 0, 0, 0);
                    commands.SetRenderTarget(target);
                    commands.ClearRenderTarget(true, true, clearColor);
                    commands.SetViewProjectionMatrices(camera.worldToCameraMatrix,
                        GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
                    commands.DrawRenderer(ground, material, 0, material.FindPass("DepthOnly"));
                    Graphics.ExecuteCommandBuffer(commands);
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    int clear = 0;
                    foreach (float value in image.GetRawTextureData<float>())
                        if (Mathf.Abs(value - .5f) < .0001f) clear++;
                    return (size * size - clear, clear, size * size);
                }
                finally
                {
                    RenderTexture.active = previous;
                    Destroy(image);
                    Destroy(target);
                }
            }
        }
        IEnumerator Verify()
        {
            yield return null;
            var flow=FindFirstObjectByType<StarSystemFlowController>();Check(flow&&flow.WorldReady,"World initialization");
            var view=flow.view;var font=view.font;
            Check(font&&font.atlasPopulationMode==AtlasPopulationMode.Static&&font.atlasTextures.All(t=>t&&t.width>1),"Static font atlas missing in Player");
            foreach(var label in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(t=>t.font==font))
                Check(font.HasCharacters(label.text,out List<char> missing),"Player missing glyphs: "+new string(missing.ToArray()));
            Check(font.HasCharacters("행성 지도 활성화 · M 키로 다음 행성 선택 행성을 준비하지 못했습니다. 다시 선택해 주세요. 테스트 항성계 완료 행성으로 이동 중"),"Notice glyphs");
            Check(DeveloperDebugController.Available,"Development F12 gate");
            flow.surge.durationSeconds=1;flow.surge.Advance(1);yield return null;
            flow.RequestMap();yield return null;yield return null;
            Check(flow.IsMapOpen&&Time.timeScale==0,"Map did not pause");
            var target=flow.SystemData.Find("system_01_B");view.Hover(target.spec.planetId,new Vector2(Screen.width*.5f,Screen.height*.5f));
            Check(view.tooltipLabel.text=="행성 크기: "+PlanetMapPreview.From(target).sizeLabel,"Player tooltip");
            Check(flow.RequestTravel(target.spec.planetId),"Player route request");yield return null;yield return null;
            float deadline=Time.realtimeSinceStartup+10;
            while(flow.IsTravelling){Check(Time.realtimeSinceStartup<deadline,"Transition timeout");yield return null;}
            Check(flow.Progress.currentPlanetId==target.spec.planetId&&flow.Progress.chosenPath.Count==2,"Player route commit");
            Check(flow.hand.InHandCount==4&&flow.pause.OwnerCount==0&&Time.timeScale==1,"Player inventory/pause");
            Check(Mathf.Abs(flow.worldHost.ActiveWorld.ground.transform.localScale.x-target.spec.terrain.sizeScale)<.0001f,"Player planet scale");
        }
    }
}
#endif
