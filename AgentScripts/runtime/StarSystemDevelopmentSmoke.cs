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
            var depthResult = RenderDepth(camera, world.groundRenderer, material);
            Check(depthResult.covered > 32 && depthResult.clear > 32, "DepthOnly did not render the ground silhouette");
            Debug.Log($"B612_TERRAIN_PLAYER: {material.shader.name}, DepthOnly pixels {depthResult.covered}/{depthResult.total}, " +
                $"seed {world.Terrain.GenerationPlan.seed}, mesh {mesh.vertexCount} vertices, {material.passCount} passes");
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
