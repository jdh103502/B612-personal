// B612 diorama builder. Run through the Unity CLI:
//   unity command run_script --file AgentScripts/B612Builder.cs --entry B612Builder.Build
// Rebuilds Assets/B612/Generated (meshes, materials, prefabs, volume) and two scenes:
//   Assets/B612/Scenes/B612_Diorama.unity, Assets/B612/Scenes/B612_Turnaround.unity
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class B612Builder
{
    const string Root = "Assets/B612";
    const string Gen = "Assets/B612/Generated";
    const string MeshAsset = "Assets/B612/Generated/B612_Meshes.asset";

    // Main asteroid: base radius, the height where its top is cut into a walkable plateau,
    // and how far the plateau props are spread out compared to the original small asteroid.
    const float AstRadius = 5.3f;
    const float PlateauY = 3.1f;
    const float Spread = 1.3f;
    static float AstK { get { return AstRadius / 3.4f; } }

    // Clock planet: radius, and its horizontal distance from the asteroid centre (the stepping stones fill the gap).
    const float ClockR = 1.45f;
    const float ClockDistance = 14f;
    const float StoneSpacing = 2.2f;

    // Camera and layout knobs for the diorama shot. The clock planet sits where the viewport point below lands.
    static Vector3 CamTarget = new Vector3(-1.1f, 1.8f, -1.3f);
    static Vector3 CamDir = new Vector3(0.36f, 0.42f, 1f);
    static float CamDist = 24.5f;
    static float CamFov = 27f;
    static Vector2 ClockViewport = new Vector2(0.83f, 0.8f);
    static Vector3 ClockPlanetPos;

    static Dictionary<string, Material> mats;
    static System.Random rng;
    static int meshCount;
    static bool isNight;
    static float princeWalkSpeed = 0.5f, princeRunSpeed = 1.55f;

    // ------------------------------------------------------------------ entry points
    public static string Build()
    {
        rng = new System.Random(612);
        mats = new Dictionary<string, Material>();
        fades.Clear();
        meshCount = 0;

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        PrepareFolders();
        MakePaperTextures();
        ConfigurePipeline();
        var profile = MakeVolumeProfile();

        RuntimeAnimatorController princeController;
        AnimationClip princeIdle;
        var princePrefab = MakeMeshyPrince(out princeController, out princeIdle, out princeWalkSpeed, out princeRunSpeed);
        var foxPrefab = MakeVisualPrefab("FoxRig");

        isNight = false;
        var s1 = SceneManager.GetActiveScene();
        BuildDiorama(profile, princePrefab, foxPrefab);
        EditorSceneManager.SaveScene(s1, Root + "/Scenes/B612_Diorama.unity");

        // same layout (same random seed) under a night sky
        rng = new System.Random(612);
        isNight = true;
        var nightProfile = MakeNightVolumeProfile();
        var s3 = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildDiorama(nightProfile, princePrefab, foxPrefab);
        EditorSceneManager.SaveScene(s3, Root + "/Scenes/B612_Night.unity");
        isNight = false;

        var s2 = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildTurnaround(profile, princePrefab, foxPrefab);
        EditorSceneManager.SaveScene(s2, Root + "/Scenes/B612_Turnaround.unity");

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(Root + "/Scenes/B612_Night.unity", true),
            new EditorBuildSettingsScene(Root + "/Scenes/B612_Diorama.unity", true),
        };
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(Root + "/Scenes/B612_Night.unity", OpenSceneMode.Single);
        return "built: meshes=" + meshCount + " materials=" + mats.Count + "\n" + layoutReport;
    }

    // ------------------------------------------------------------------ project setup
    static void PrepareFolders()
    {
        if (AssetDatabase.IsValidFolder(Gen)) AssetDatabase.DeleteAsset(Gen);
        if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", "B612");
        if (!AssetDatabase.IsValidFolder(Root + "/Scenes")) AssetDatabase.CreateFolder(Root, "Scenes");
        AssetDatabase.CreateFolder(Root, "Generated");
        AssetDatabase.CreateFolder(Gen, "Materials");
        AssetDatabase.CreateFolder(Gen, "Prefabs");
        AssetDatabase.CreateFolder(Gen, "Textures");
        var holder = new Mesh { name = "_B612" };
        AssetDatabase.CreateAsset(holder, MeshAsset);
    }

    // Tileable paper: soft mottling, fibres and grain, saved as an albedo texture and a normal map.
    static Texture2D paperAlbedo, paperNormal;

    static void MakePaperTextures()
    {
        const int S = 512;
        var r = new System.Random(4612);
        var height = new float[S * S];
        var tone = new float[S * S];
        int P1 = 8, P2 = 32;
        var l1 = new float[P1 * P1 * P1]; var l2 = new float[P2 * P2 * P2];
        for (int i = 0; i < l1.Length; i++) l1[i] = (float)r.NextDouble();
        for (int i = 0; i < l2.Length; i++) l2[i] = (float)r.NextDouble();
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float m1 = Lattice(l1, x * (float)P1 / S, y * (float)P1 / S, 0f, P1);
                float m2 = Lattice(l2, x * (float)P2 / S, y * (float)P2 / S, 0f, P2);
                float grain = (float)r.NextDouble();
                height[x + y * S] = m1 * 0.35f + m2 * 0.35f + grain * 0.3f;
                tone[x + y * S] = (m1 - 0.5f) * 0.05f + (m2 - 0.5f) * 0.03f + (grain - 0.5f) * 0.025f;
            }
        // fibres: short, slightly curved strokes, wrapped so the tile repeats seamlessly
        for (int f = 0; f < 1100; f++)
        {
            float fx = (float)r.NextDouble() * S, fy = (float)r.NextDouble() * S, a = (float)r.NextDouble() * Mathf.PI * 2f;
            float len = 8f + (float)r.NextDouble() * 34f, bend = ((float)r.NextDouble() - 0.5f) * 0.08f, w = 0.6f + (float)r.NextDouble() * 0.7f;
            float strength = 0.25f + (float)r.NextDouble() * 0.5f;
            for (float s = 0f; s < len; s += 0.5f)
            {
                fx += Mathf.Cos(a) * 0.5f; fy += Mathf.Sin(a) * 0.5f; a += bend;
                for (int oy = -2; oy <= 2; oy++)
                    for (int ox = -2; ox <= 2; ox++)
                    {
                        int px = ((int)fx + ox) & (S - 1), py = ((int)fy + oy) & (S - 1);
                        float d2 = (ox * ox + oy * oy) / (w * w);
                        float k = Mathf.Exp(-d2) * strength * 0.12f;
                        height[px + py * S] += k;
                        tone[px + py * S] -= k * 0.35f;
                    }
            }
        }
        var alb = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var nrm = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var ac = new Color32[S * S]; var nc = new Color32[S * S];
        const float bump = 3.5f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                int i = x + y * S;
                byte g = (byte)Mathf.Clamp(Mathf.RoundToInt((0.965f + tone[i]) * 255f), 0, 255);
                ac[i] = new Color32(g, g, g, 255);
                float dx = height[((x + 1) & (S - 1)) + y * S] - height[((x - 1) & (S - 1)) + y * S];
                float dy = height[x + ((y + 1) & (S - 1)) * S] - height[x + ((y - 1) & (S - 1)) * S];
                var nv = new Vector3(-dx * bump, -dy * bump, 1f).normalized;
                nc[i] = new Color32((byte)((nv.x * 0.5f + 0.5f) * 255f), (byte)((nv.y * 0.5f + 0.5f) * 255f), (byte)((nv.z * 0.5f + 0.5f) * 255f), 255);
            }
        alb.SetPixels32(ac); alb.Apply(); nrm.SetPixels32(nc); nrm.Apply();
        paperAlbedo = SaveTexture(alb, Gen + "/Textures/Paper_Albedo.png", false);
        paperNormal = SaveTexture(nrm, Gen + "/Textures/Paper_Normal.png", true);
        UnityEngine.Object.DestroyImmediate(alb); UnityEngine.Object.DestroyImmediate(nrm);
    }

    static Texture2D SaveTexture(Texture2D tex, string path, bool normalMap)
    {
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = !normalMap;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.mipmapEnabled = true;
        ti.anisoLevel = 4;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void ConfigurePipeline()
    {
        var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (!rp) return;
        rp.shadowDistance = 50f;
        rp.shadowCascadeCount = 4;
        rp.msaaSampleCount = 4;
        var so = new SerializedObject(rp);
        SetInt(so, "m_MainLightShadowmapResolution", 4096);
        SetBool(so, "m_SoftShadowsSupported", true);
        SetInt(so, "m_SoftShadowQuality", 3);
        SetBool(so, "m_AdditionalLightShadowsSupported", true);
        SetInt(so, "m_AdditionalLightsShadowmapResolution", 2048);
        so.ApplyModifiedPropertiesWithoutUndo();
        var list = so.FindProperty("m_RendererDataList");
        for (int i = 0; list != null && i < list.arraySize; i++)
        {
            var rd = list.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
            if (!rd) continue;
            foreach (var f in rd.rendererFeatures)
            {
                if (!f || f.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                var fso = new SerializedObject(f);
                SetFloat(fso, "m_Settings.Intensity", 2.2f);
                SetFloat(fso, "m_Settings.Radius", 0.5f);
                SetFloat(fso, "m_Settings.DirectLightingStrength", 0.4f);
                fso.ApplyModifiedPropertiesWithoutUndo();
                f.SetActive(true);
                EditorUtility.SetDirty(f);
            }
        }
        EditorUtility.SetDirty(rp);
    }

    static void SetInt(SerializedObject so, string p, int v) { var x = so.FindProperty(p); if (x != null) { if (x.propertyType == SerializedPropertyType.Enum) x.enumValueIndex = Mathf.Min(v, x.enumNames.Length - 1); else x.intValue = v; } }
    static void SetBool(SerializedObject so, string p, bool v) { var x = so.FindProperty(p); if (x != null) x.boolValue = v; }
    static void SetFloat(SerializedObject so, string p, float v) { var x = so.FindProperty(p); if (x != null) x.floatValue = v; }

    static VolumeProfile MakeVolumeProfile()
    {
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(prof, Gen + "/B612_Volume.asset");
        var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
        var ca = prof.Add<ColorAdjustments>(true);
        ca.postExposure.Override(0.05f); ca.contrast.Override(16f); ca.saturation.Override(6f); ca.colorFilter.Override(Hex("#FFFBF4"));
        var bl = prof.Add<Bloom>(true);
        bl.threshold.Override(1.05f); bl.intensity.Override(0.55f); bl.scatter.Override(0.65f); bl.tint.Override(Hex("#FFE3B5"));
        var vg = prof.Add<Vignette>(true);
        vg.intensity.Override(0.13f); vg.smoothness.Override(0.55f); vg.color.Override(Hex("#6B5338"));
        foreach (var c in prof.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, prof); }
        EditorUtility.SetDirty(prof);
        return prof;
    }

    static VolumeProfile MakeNightVolumeProfile()
    {
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(prof, Gen + "/B612_NightVolume.asset");
        var tm = prof.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
        var ca = prof.Add<ColorAdjustments>(true);
        ca.postExposure.Override(0.15f); ca.contrast.Override(14f); ca.saturation.Override(10f); ca.colorFilter.Override(Hex("#EEF1FF"));
        var st = prof.Add<SplitToning>(true);
        st.shadows.Override(Hex("#2E3F92")); st.highlights.Override(Hex("#FFBE7A")); st.balance.Override(-15f);
        var bl = prof.Add<Bloom>(true);
        bl.threshold.Override(0.8f); bl.intensity.Override(1.15f); bl.scatter.Override(0.75f); bl.tint.Override(Hex("#DCE4FF"));
        var vg = prof.Add<Vignette>(true);
        vg.intensity.Override(0.24f); vg.smoothness.Override(0.6f); vg.color.Override(Hex("#05081A"));
        foreach (var c in prof.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, prof); }
        EditorUtility.SetDirty(prof);
        return prof;
    }

    // Tiling value noise (two independent channels) that the night sky shader samples instead of hashing per octave.
    static Texture3D MakeNoise3D()
    {
        const int S = 64, P = 16;
        var r = new System.Random(1612);
        var la = new float[P * P * P]; var lb = new float[P * P * P];
        for (int i = 0; i < la.Length; i++) { la[i] = (float)r.NextDouble(); lb[i] = (float)r.NextDouble(); }
        var px = new Color[S * S * S];
        float k = (float)P / S;
        for (int z = 0; z < S; z++)
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    px[x + S * (y + S * z)] = new Color(Lattice(la, x * k, y * k, z * k, P), Lattice(lb, x * k, y * k, z * k, P), 0f, 1f);
        var tex = new Texture3D(S, S, S, TextureFormat.RGBAHalf, false) { name = "B612_Noise3D", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        tex.SetPixels(px);
        tex.Apply();
        AssetDatabase.CreateAsset(tex, Gen + "/B612_Noise3D.asset");
        return tex;
    }

    static float Lattice(float[] L, float x, float y, float z, int P)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y), z0 = Mathf.FloorToInt(z);
        float tx = x - x0, ty = y - y0, tz = z - z0;
        tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty); tz = tz * tz * (3f - 2f * tz);
        Func<int, int, int, float> at = (a, b, c) => L[((a % P) + P) % P + P * ((((b % P) + P) % P) + P * (((c % P) + P) % P))];
        float a00 = Mathf.Lerp(at(x0, y0, z0), at(x0 + 1, y0, z0), tx), a10 = Mathf.Lerp(at(x0, y0 + 1, z0), at(x0 + 1, y0 + 1, z0), tx);
        float a01 = Mathf.Lerp(at(x0, y0, z0 + 1), at(x0 + 1, y0, z0 + 1), tx), a11 = Mathf.Lerp(at(x0, y0 + 1, z0 + 1), at(x0 + 1, y0 + 1, z0 + 1), tx);
        return Mathf.Lerp(Mathf.Lerp(a00, a10, ty), Mathf.Lerp(a01, a11, ty), tz);
    }

    // ------------------------------------------------------------------ scenes
    static Camera MakeCameraAndLights(VolumeProfile profile, string bg)
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Hex("#F1E6D2");
        RenderSettings.ambientEquatorColor = Hex("#D6C4A6");
        RenderSettings.ambientGroundColor = Hex("#76644F");
        RenderSettings.ambientIntensity = 0.75f;
        RenderSettings.fog = false;
        DynamicGI.UpdateEnvironment();

        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Hex(bg);
        cam.nearClipPlane = 0.3f; cam.farClipPlane = 200f;
        var acd = camGO.AddComponent<UniversalAdditionalCameraData>();
        acd.renderPostProcessing = true;
        acd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        acd.antialiasingQuality = AntialiasingQuality.High;
        camGO.AddComponent<AudioListener>();

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = Hex("#FFEFD4");
        sun.intensity = 1.6f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.78f;
        sun.transform.rotation = Quaternion.Euler(48f, 145f, 0f);
        sun.gameObject.AddComponent<UniversalAdditionalLightData>();

        var fill = new GameObject("Fill").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = Hex("#E9E2FF");
        fill.intensity = 0.32f;
        fill.shadows = LightShadows.None;
        fill.transform.rotation = Quaternion.Euler(28f, -35f, 0f);

        var vol = new GameObject("Global Volume").AddComponent<Volume>();
        vol.isGlobal = true;
        vol.sharedProfile = profile;
        return cam;
    }

    static void BuildDiorama(VolumeProfile profile, GameObject princePrefab, GameObject foxPrefab)
    {
        float s = Spread;
        var cam = MakeCameraAndLights(profile, "#F6EEDD");
        cam.fieldOfView = CamFov;
        cam.aspect = 1.5f;
        cam.transform.position = CamTarget + CamDir.normalized * CamDist;
        cam.transform.LookAt(CamTarget);
        ClockPlanetPos = AtHorizontalDistance(cam.ViewportPointToRay(new Vector3(ClockViewport.x, ClockViewport.y, 0f)), ClockDistance);
        var camT = cam.transform;
        var sunDir = (-camT.right * 0.85f + Vector3.up * 0.8f + camT.forward * 0.15f).normalized;
        GameObject.Find("Sun").transform.rotation = Quaternion.LookRotation(-sunDir);
        GameObject.Find("Fill").transform.rotation = Quaternion.LookRotation(-(camT.right * 0.8f + Vector3.up * 0.3f + camT.forward * 0.4f).normalized);

        var world = new GameObject("B612 World").transform;

        // main asteroid with a collider so props can be dropped onto its surface
        var ast = BuildAsteroid(world);
        var mc = ast.AddComponent<MeshCollider>();
        mc.sharedMesh = ast.GetComponent<MeshFilter>().sharedMesh;
        Physics.SyncTransforms();

        // house
        var house = BuildHouse(world);
        house.transform.position = OnTop(1.3f, -1.3f) - Vector3.up * 0.04f;
        house.transform.rotation = Quaternion.Euler(0f, 22f, 0f);
        house.transform.localScale = new Vector3(-HouseScale, HouseScale, HouseScale);

        // stone path from the door toward the camera
        var path = new FM(2);
        for (int i = 0; i < 5; i++)
        {
            var local = new Vector3(Rand(-0.04f, 0.04f) + i * 0.03f, 0f, 1.06f + i * 0.3f);
            var wp = house.transform.TransformPoint(local);
            wp = OnTop(wp.x, wp.z) + Vector3.up * 0.01f;
            path.Append(Slab(Rand(0.15f, 0.2f), 0.05f), Matrix4x4.TRS(wp, Quaternion.Euler(0f, Rand(0f, 60f), 0f), Vector3.one), i % 2);
        }
        Obj("Stone Path", path, world, Mat("#ECE6DA"), Mat("#DFD7C7"));

        // playable characters
        var clockXZ = new Vector3(ClockPlanetPos.x, 0f, ClockPlanetPos.z);
        var slippery = new PhysicsMaterial("B612 Slippery") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum, bounciness = 0f };

        var pp = OnTop(-0.45f * s, 0.55f * s);
        var spawn = new GameObject("Spawn Point").transform;
        spawn.SetParent(world, false);
        spawn.position = pp + Vector3.up * 0.05f;
        spawn.rotation = Quaternion.LookRotation(clockXZ - new Vector3(pp.x, 0f, pp.z));

        var playerGO = new GameObject("Player");
        playerGO.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
        playerGO.AddComponent<Rigidbody>().mass = 1f;
        var pcap = playerGO.AddComponent<CapsuleCollider>();
        pcap.radius = 0.13f; pcap.height = 0.78f; pcap.center = new Vector3(0f, 0.39f, 0f); pcap.sharedMaterial = slippery;
        var walker = playerGO.AddComponent<B612.GravityWalker>();
        var playerCtrl = playerGO.AddComponent<B612.PlayerController>();
        playerCtrl.spawnPoint = spawn;
        var princeVis = (GameObject)PrefabUtility.InstantiatePrefab(princePrefab, playerGO.transform);
        var pAnim = playerGO.AddComponent<B612.PrinceCharacterAnimator>();
        pAnim.walker = walker; pAnim.visual = princeVis.transform; pAnim.animator = princeVis.GetComponentInChildren<Animator>();
        pAnim.walkSpeed = princeWalkSpeed; pAnim.runSpeed = princeRunSpeed;

        var fp = OnTop(-0.95f * s, 0.8f * s);
        var foxGO = new GameObject("Fox");
        foxGO.transform.SetPositionAndRotation(fp + Vector3.up * 0.05f, Quaternion.LookRotation(clockXZ - new Vector3(fp.x, 0f, fp.z)) * Quaternion.Euler(0f, 35f, 0f));
        foxGO.AddComponent<Rigidbody>().mass = 0.5f;
        var fcap = foxGO.AddComponent<CapsuleCollider>();
        fcap.direction = 2; fcap.radius = 0.1f; fcap.height = 0.4f; fcap.center = new Vector3(0f, 0.14f, 0f); fcap.sharedMaterial = slippery;
        var foxWalker = foxGO.AddComponent<B612.GravityWalker>();
        foxWalker.moveSpeed = 2.0f; foxWalker.jumpSpeed = 3.0f; foxWalker.acceleration = 14f;
        var follower = foxGO.AddComponent<B612.FoxFollower>();
        follower.leader = walker;
        var foxVis = (GameObject)PrefabUtility.InstantiatePrefab(foxPrefab, foxGO.transform);
        foxVis.transform.localScale = Vector3.one * 0.8f;
        var fAnim = foxGO.AddComponent<B612.FoxRigAnimator>();
        fAnim.walker = foxWalker; fAnim.model = foxVis.transform; fAnim.watch = playerGO.transform;

        var orbit = cam.gameObject.AddComponent<B612.OrbitCamera>();
        orbit.target = walker;
        playerCtrl.cameraRig = orbit;
        new GameObject("HUD").AddComponent<B612.DemoHUD>().player = playerCtrl;

        // rose, the three volcanoes (two active, one extinct), baobab sprouts, pebbles
        var rose = BuildRose(world);
        rose.transform.position = OnTop(1.2f * s, 0.95f * s);
        rose.transform.localScale = Vector3.one * 0.85f;

        var vol = BuildVolcano(world, false);
        vol.transform.position = OnTop(-1.9f, -2.0f) - Vector3.up * 0.03f;
        var vol2 = BuildVolcano(world, false);
        vol2.name = "Volcano (small)";
        vol2.transform.position = OnTop(-2.3f, 1.9f) - Vector3.up * 0.03f;
        vol2.transform.localScale = Vector3.one * 0.7f;
        var dead = BuildVolcano(world, true);
        dead.name = "Extinct Volcano";
        dead.transform.position = OnTop(0.2f, -3.2f) - Vector3.up * 0.03f;
        dead.transform.localScale = Vector3.one * 0.8f;
        dead.transform.rotation = Quaternion.Euler(0f, 40f, 0f);

        foreach (var sp in new[] { new Vector2(2.9f, -0.4f), new Vector2(0.4f, 2.8f), new Vector2(-1.2f, -2.9f), new Vector2(-2.9f, 0.9f) })
        {
            var sprout = BuildSprout(world);
            sprout.transform.position = OnTop(sp.x, sp.y) - Vector3.up * 0.01f;
            sprout.transform.rotation = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
            sprout.transform.localScale = Vector3.one * Rand(0.85f, 1.25f);
        }

        var peb = new FM(2);
        for (int i = 0; i < 26; i++)
        {
            float a = Rand(0f, Mathf.PI * 2f), r = Rand(0.9f, 3.4f);
            var p = OnTop(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            float ps = Rand(0.06f, 0.14f);
            if (Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(house.transform.position.x, 0f, house.transform.position.z)) < 1.9f) continue;
            peb.Append(Rock(ps, 0), Matrix4x4.TRS(p, Quaternion.Euler(Rand(0, 360), Rand(0, 360), Rand(0, 360)), new Vector3(1f, 0.6f, 1f)), i % 2);
        }
        Obj("Pebbles", peb, world, Mat("#E9DFC8"), Mat("#D8CBB0"));

        // grass tufts on the plateau (clear of the house and volcanoes) and scattered down the sides
        var grass = new FM(2);
        var houseXZ = new Vector3(house.transform.position.x, 0f, house.transform.position.z);
        int placed = 0;
        for (int tries = 0; tries < 500 && placed < 70; tries++)
        {
            float a = Rand(0f, Mathf.PI * 2f), r = Rand(0.6f, 3.7f);
            var q = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            if (Vector3.Distance(q, houseXZ) < 2.2f) continue;
            bool clear = true;
            foreach (var vv in new[] { vol, vol2, dead })
                if (Vector3.Distance(q, new Vector3(vv.transform.position.x, 0f, vv.transform.position.z)) < 0.85f) clear = false;
            if (!clear) continue;
            grass.Append(Tuft(), Matrix4x4.TRS(OnTop(q.x, q.z) - Vector3.up * 0.01f, Quaternion.Euler(0f, Rand(0f, 360f), 0f), Vector3.one * Rand(0.8f, 1.35f)));
            placed++;
        }
        for (int i = 0; i < 70; i++)
        {
            var d = RandV();
            if (d.sqrMagnitude < 0.05f) continue;
            d.Normalize();
            if (d.y > 0.4f) continue;
            RaycastHit h;
            if (!Physics.Raycast(d * 20f, -d, out h, 20f, ~0, QueryTriggerInteraction.Ignore)) continue;
            var gu = (h.normal + d).normalized;
            grass.Append(Tuft(), Matrix4x4.TRS(h.point - gu * 0.01f, Quaternion.FromToRotation(Vector3.up, gu) * Quaternion.Euler(0f, Rand(0f, 360f), 0f), Vector3.one * Rand(0.8f, 1.35f)));
        }
        Obj("Grass", grass, world, Mat("#A8BC70", 0.1f, isNight ? 0.12f : 0f), Mat("#C2CE86", 0.1f, isNight ? 0.12f : 0f)).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        // clock planet
        var cp = BuildClockPlanet(world, cam.transform.position);
        cp.transform.position = ClockPlanetPos;

        // stepping stones: from the plateau rim toward the clock planet, about StoneSpacing apart.
        // Each stone has a small gravity field; neighbouring fields overlap so a jump is always pulled toward a stone.
        var toClock = (clockXZ - new Vector3(pp.x, 0f, pp.z)).normalized;
        var side = Vector3.Cross(Vector3.up, toClock);
        var rim = pp;
        for (float t = 0f; t < ClockDistance; t += 0.1f)
        {
            var q = new Vector3(pp.x, 0f, pp.z) + toClock * t;
            RaycastHit h;
            if (!Physics.Raycast(new Vector3(q.x, 30f, q.z), Vector3.down, out h, 60f, ~0, QueryTriggerInteraction.Ignore) || h.point.y < PlateauY - 0.3f) break;
            rim = h.point;
        }
        var landing = ClockPlanetPos + (rim - ClockPlanetPos).normalized * (ClockR + 0.2f);
        int count = Mathf.Clamp(Mathf.RoundToInt(Vector3.Distance(rim, landing) / StoneSpacing) - 1, 2, 6);
        var stonePositions = new List<Vector3>();
        for (int i = 0; i < count; i++)
        {
            float t = (i + 1f) / (count + 1f);
            float zig = i % 2 == 0 ? 1f : -1f;
            var p = Vector3.Lerp(rim, landing, t) + Vector3.up * (0.3f + Mathf.Sin(t * Mathf.PI) * 0.5f + zig * 0.12f) + side * (zig * 0.3f);
            var st = Ico(1, 1f, null, (c, n) => n.y > 0.35f ? 0 : 1, 2);
            var one = new FM(2);
            one.Append(st, Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(Rand(-5, 5), Rand(0, 360), Rand(-5, 5)), new Vector3(0.62f, 0.16f, 0.5f)));
            one = Warp(one, Lumpy(0.045f, 3.5f, i * 7.1f));
            var stoneGO = Obj("Stepping Stone " + (i + 1), one, world, Mat("#E2DCCF"), Mat("#C9C1B1"));
            stoneGO.transform.position = p;
            var smc = stoneGO.AddComponent<MeshCollider>(); smc.convex = true;
            var sg = stoneGO.AddComponent<B612.GravitySource>(); sg.surfaceRadius = 0.3f; sg.influenceRadius = 1.4f; sg.strength = 3.4f;
            stonePositions.Add(p);
        }
        layoutReport = "clock=" + ClockPlanetPos.ToString("F2") + " rim=" + rim.ToString("F2") + " stones=" + count;
        for (int i = 0; i < stonePositions.Count; i++)
            layoutReport += " | s" + (i + 1) + "=" + stonePositions[i].ToString("F2") + " gap=" + Vector3.Distance(i == 0 ? rim : stonePositions[i - 1], stonePositions[i]).ToString("F2");
        layoutReport += " | last->clockSurface=" + (Vector3.Distance(stonePositions[stonePositions.Count - 1], ClockPlanetPos) - ClockR).ToString("F2");

        // background stars and a small ringed planet, placed in screen space
        float bgDepth = CamDist + 15f;
        var stars = new FM(1);
        Vector2[] sv = { new Vector2(0.08f, 0.88f), new Vector2(0.06f, 0.09f), new Vector2(0.5f, 0.84f), new Vector2(0.81f, 0.5f), new Vector2(0.95f, 0.83f), new Vector2(0.72f, 0.3f), new Vector2(0.93f, 0.22f), new Vector2(0.36f, 0.94f), new Vector2(0.28f, 0.75f) };
        foreach (var v in sv)
        {
            var wp = cam.ViewportToWorldPoint(new Vector3(v.x, v.y, bgDepth));
            var rot = Quaternion.LookRotation(cam.transform.position - wp) * Quaternion.Euler(0f, 0f, Rand(-20f, 20f));
            stars.Append(StarPrism(0.36f, 0.16f, 0.1f), Matrix4x4.TRS(wp, rot, Vector3.one * Rand(0.75f, 1.1f) * bgDepth / 30f));
        }
        Obj("Background Stars", stars, world, Mat("#F2C23E", 0.3f, isNight ? 1.3f : 0.12f));

        var ringed = new FM(2);
        ringed.Append(Ico(2, 0.38f), Matrix4x4.identity, 0);
        ringed.Append(Torus(0.62f, 0.035f, 24, 5), Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1f, 1f, 0.35f)), 1);
        var rg = Obj("Ringed Planet", ringed, world, Mat("#F2C14E", 0.2f, 0.2f), Mat("#E3A93A", 0.2f, 0.2f));
        rg.transform.position = cam.ViewportToWorldPoint(new Vector3(0.62f, 0.9f, bgDepth));
        rg.transform.rotation = Quaternion.LookRotation(cam.transform.position - rg.transform.position) * Quaternion.Euler(-70f, 0f, 18f);
        rg.transform.localScale = Vector3.one * bgDepth / 30f;
        rg.AddComponent<B612.Spinner>().degreesPerSecond = 12f;

        // gravity and collision
        var g0 = ast.AddComponent<B612.GravitySource>(); g0.surfaceRadius = 2.6f * AstK; g0.influenceRadius = ClockDistance + 2f; g0.strength = 3.4f;
        foreach (var mf in house.GetComponentsInChildren<MeshFilter>())
            if (mf.name != "Smoke" && mf.name != "Window Glow" && mf.name != "Hanging Lamp" && mf.name != "Lamp Bulb" && mf.name != "Candle Flame" && mf.name != "Picture") mf.gameObject.AddComponent<MeshCollider>();
        foreach (var v in new[] { vol, vol2, dead }) v.AddComponent<MeshCollider>();
        foreach (var mf in cp.GetComponentsInChildren<MeshFilter>())
            if (mf.name != "Flag" && mf.name != "Lamp") mf.gameObject.AddComponent<MeshCollider>();
        var gc = cp.AddComponent<B612.GravitySource>(); gc.surfaceRadius = ClockR + 0.05f; gc.influenceRadius = ClockR + 2.3f; gc.strength = 3.4f;
        var smoke = house.transform.Find("Smoke");
        if (smoke) { var sp2 = smoke.gameObject.AddComponent<B612.Spinner>(); sp2.degreesPerSecond = 0f; sp2.bobHeight = 0.05f; sp2.bobSpeed = 1.3f; }
        Physics.SyncTransforms();

        // the way home: a launch star on the clock planet flies the prince back to where he started
        var homePad = new GameObject("Launch Landing").transform;
        homePad.SetParent(world, false);
        homePad.position = spawn.position + Vector3.up * 0.35f;
        homePad.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(house.transform.position - spawn.position, Vector3.up).normalized, Vector3.up);
        BuildLaunchStar(world, homePad);

        // the far side of the asteroid: a big baobab with lanterns and glowing mushrooms
        Vector3 groveStar, groveUp;
        BuildFarSide(world, out groveStar, out groveUp);
        Physics.SyncTransforms();

        // star pickups
        var pickups = new GameObject("Star Pickups").transform; pickups.SetParent(world, false);
        Pickup(pickups, groveStar, groveUp);
        Pickup(pickups, house.transform.TransformPoint(new Vector3(0.1f, 0.62f, -0.42f)), Vector3.up);          // on the bed
        Pickup(pickups, house.transform.position + Vector3.up * (2.3f * HouseScale + 0.4f), Vector3.up);
        var far = new Vector3(0.25f, -0.35f, -1f).normalized;
        RaycastHit farHit;
        if (Physics.Raycast(far * 20f, -far, out farHit, 20f)) Pickup(pickups, farHit.point + farHit.normal * 0.45f, farHit.normal);
        Pickup(pickups, stonePositions[stonePositions.Count / 2] + Vector3.up * 0.85f, Vector3.up);
        var cpDir = new Vector3(0.55f, 0.65f, -0.5f).normalized;
        Pickup(pickups, ClockPlanetPos + cpDir * (ClockR + 0.47f), cpDir);

        if (isNight) ApplyNight(cam, world, house, new[] { vol, vol2 }, cp);
    }

    static string layoutReport = "";

    // The far point along a ray whose horizontal distance from the world origin (the asteroid centre) is d.
    static Vector3 AtHorizontalDistance(Ray r, float d)
    {
        var o = new Vector2(r.origin.x, r.origin.z); var v = new Vector2(r.direction.x, r.direction.z);
        float a = Vector2.Dot(v, v), b = 2f * Vector2.Dot(o, v), c = Vector2.Dot(o, o) - d * d;
        float disc = b * b - 4f * a * c;
        if (a < 1e-6f || disc < 0f) return r.GetPoint(CamDist);
        return r.GetPoint((-b + Mathf.Sqrt(disc)) / (2f * a));
    }

    static void Pickup(Transform parent, Vector3 pos, Vector3 up)
    {
        var fm = new FM(1);
        fm.Append(StarPrism(0.17f, 0.075f, 0.07f), Matrix4x4.identity, 0);
        var go = Obj("Star", fm, parent, Mat("#FFD23F", 0.4f, isNight ? 1.5f : 0.9f));
        go.transform.position = pos;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
        var sc = go.AddComponent<SphereCollider>(); sc.isTrigger = true; sc.radius = 0.3f;
        go.AddComponent<B612.Collectible>();
    }

    // ------------------------------------------------------------------ rigged prince (Meshy model)
    const string MeshyDir = "Assets/B612/Models/Meshy";
    const float PrinceHeight = 0.82f;          // head-top height of the in-game prince, in metres
    static float meshyScale = 1f;

    // Imports the rigged prince (walk + run FBX), builds idle and air poses from those clips, an Animator Controller
    // blending idle / walk / run by speed, and a visual prefab facing +Z, scaled to PrinceHeight, standing in the idle pose.
    static GameObject MakeMeshyPrince(out RuntimeAnimatorController controller, out AnimationClip idleClip, out float walkSpeed, out float runSpeed)
    {
        string walkPath = MeshyDir + "/Prince_Walk.fbx", runPath = MeshyDir + "/Prince_Run.fbx";
        ConfigureMeshy(walkPath, "Walk", null);
        var avatar = AssetDatabase.LoadAllAssetsAtPath(walkPath).OfType<Avatar>().FirstOrDefault();
        ConfigureMeshy(runPath, "Run", avatar);
        var walk = AssetDatabase.LoadAllAssetsAtPath(walkPath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
        var run = AssetDatabase.LoadAllAssetsAtPath(runPath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));

        var ti = (TextureImporter)AssetImporter.GetAtPath(MeshyDir + "/Prince_BaseColor.png");
        if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; ti.anisoLevel = 2; ti.SaveAndReimport(); }
        foreach (var extra in new[] { "/Prince_Metallic.png", "/Prince_Roughness.png" })
        {
            var ei = (TextureImporter)AssetImporter.GetAtPath(MeshyDir + extra);
            if (ei && (ei.maxTextureSize != 1024 || ei.sRGBTexture)) { ei.maxTextureSize = 1024; ei.sRGBTexture = false; ei.SaveAndReimport(); }
        }

        // idle: halfway between two opposite moments of the walk cycle, so legs and arms meet in the middle
        idleClip = PoseClip(walk, 0f, walk.length * 0.5f, "Prince_Idle");
        var air = PoseClip(run, 12f / 30f, 12f / 30f, "Prince_Air");   // the running stride's flight phase
        AssetDatabase.CreateAsset(idleClip, Gen + "/Prince_Idle.anim");
        AssetDatabase.CreateAsset(air, Gen + "/Prince_Air.anim");

        // visual: unpack, give it the textured material, turn it to face +Z, scale it, pose it
        var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(walkPath));
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Prince Model";
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Prince_Meshy" };
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MeshyDir + "/Prince_BaseColor.png"));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0.22f);
        mat.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(mat, Gen + "/Materials/M_Prince_Meshy.mat");
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var ms = new Material[smr.sharedMaterials.Length];
            for (int i = 0; i < ms.Length; i++) ms[i] = mat;
            smr.sharedMaterials = ms;
            smr.updateWhenOffscreen = true;
            smr.quality = SkinQuality.Bone4;
        }
        Debug.Log("B612 " + RigScarf(model, mat));
        idleClip.SampleAnimation(model, 0f);

        // wrapper (squash and lean at runtime) > pivot (facing, size, feet on the ground) > model (the Animator)
        var wrapper = new GameObject("Prince Visual");
        var pivot = new GameObject("Pivot").transform;
        pivot.SetParent(wrapper.transform, false);
        model.transform.SetParent(pivot, false);
        var headT = FindDeep(model.transform, "Head");
        var front = FindDeep(model.transform, "headfront");
        var top = FindDeep(model.transform, "head_end");
        if (headT && front)
        {
            var fwd = Vector3.ProjectOnPlane(front.position - headT.position, Vector3.up);
            if (fwd.sqrMagnitude > 1e-8f) pivot.rotation = Quaternion.FromToRotation(fwd.normalized, Vector3.forward);
        }
        float h = top ? top.position.y - wrapper.transform.position.y : 1.4f;
        meshyScale = PrinceHeight / Mathf.Max(h, 0.01f);
        pivot.localScale = Vector3.one * meshyScale;
        // feet on the ground: lowest foot joint just above y = 0
        var toes = new[] { FindDeep(model.transform, "LeftToeBase"), FindDeep(model.transform, "RightToeBase"), FindDeep(model.transform, "LeftFoot"), FindDeep(model.transform, "RightFoot") };
        float low = float.MaxValue;
        foreach (var tt in toes) if (tt) low = Mathf.Min(low, tt.position.y);
        if (low < float.MaxValue) pivot.position -= Vector3.up * (low - 0.025f);

        // natural ground speed of each clip (measured on the source rig), scaled to the in-game size
        walkSpeed = 0.85f * h / 1.35f * meshyScale;
        runSpeed = 2.6f * h / 1.35f * meshyScale;

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(Gen + "/Prince.controller");
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        ctrl.parameters = ctrl.parameters.Select(p => { if (p.name == "Grounded") p.defaultBool = true; return p; }).ToArray();
        BlendTree tree;
        var loco = ctrl.CreateBlendTreeInController("Locomotion", out tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(idleClip, 0f);
        tree.AddChild(walk, walkSpeed);
        tree.AddChild(run, runSpeed);
        var sm = ctrl.layers[0].stateMachine;
        var airState = sm.AddState("Air");
        airState.motion = air;
        var toAir = loco.AddTransition(airState);
        toAir.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
        toAir.hasExitTime = false; toAir.duration = 0.12f;
        var toGround = airState.AddTransition(loco);
        toGround.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
        toGround.hasExitTime = false; toGround.duration = 0.1f;
        controller = ctrl;

        var anim = model.GetComponent<Animator>();
        if (!anim) anim = model.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        if (avatar) anim.avatar = avatar;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        Debug.Log("B612 rigged prince: source height=" + h.ToString("F2") + " scale=" + meshyScale.ToString("F3") + " walk=" + walkSpeed.ToString("F2") + " run=" + runSpeed.ToString("F2"));
        var prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, Gen + "/Prefabs/PrinceRiggedVisual.prefab");
        UnityEngine.Object.DestroyImmediate(wrapper);
        return prefab;
    }

    const int ScarfBones = 5;
    const float ScarfStretch = 1.75f;     // how much longer the loose end of the scarf gets
    const float ScarfRadialIn = 0.2f, ScarfRadialOut = 0.3f;

    // Lengthens the loose tail of the prince's scarf, gives it a chain of bones (Scarf.1..N plus a Scarf.End
    // marker) and re-skins it so it can swing, and repaints the scarf yellow in a copy of the base texture.
    // Scarf vertices are found by colour (red in the texture, above the red waist sash, not skinned to a limb);
    // the loose tail is the part standing out away from the neck.
    static string RigScarf(GameObject model, Material mat)
    {
        var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
        var src = smr.sharedMesh;
        var bones = smr.bones;
        var bind = src.bindposes;
        Func<string, int> idx = n => Array.FindIndex(bones, b => b && b.name == n);
        int iNeck = idx("neck"), iHips = idx("Hips"), iTop = idx("head_end");
        if (iNeck < 0 || iHips < 0 || iTop < 0) return "scarf: bones not found";
        Func<int, Vector3> at = i => bind[i].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector3 hips = at(iHips), neck = at(iNeck), top = at(iTop);
        var up = (top - hips).normalized;
        float H = Vector3.Dot(top - hips, up);

        var texPath = Application.dataPath + "/B612/Models/Meshy/Prince_BaseColor.png";
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.LoadImage(System.IO.File.ReadAllBytes(texPath));
        var verts = src.vertices; var uv = src.uv; var bw = src.boneWeights;
        var scarf = new HashSet<int>();
        var radial = new float[verts.Length];
        var blend = new float[verts.Length];
        var tail = new List<int>();
        // a vertex may belong to the scarf if it is not on a limb and not where the waist sash is (low, close to the body)
        var allowed = new bool[verts.Length];
        var sash = new HashSet<int>();
        Func<Color, bool> isRed = col => { float hu, sa, va; Color.RGBToHSV(col, out hu, out sa, out va); return (hu < 0.045f || hu > 0.955f) && sa > 0.45f && va > 0.15f; };
        for (int i = 0; i < verts.Length; i++)
        {
            var dn = verts[i] - neck;
            float hh = Vector3.Dot(verts[i] - hips, up), rr = (dn - up * Vector3.Dot(dn, up)).magnitude;
            bool lowClose = hh < 0.25f * H && rr < 0.24f * H;      // where the waist sash sits
            // texture bleeding at atlas seams can paint a few arm vertices red: skip anything mostly skinned to a limb
            int main = bw[i].weight0 >= bw[i].weight1 ? bw[i].boneIndex0 : bw[i].boneIndex1;
            string mainName = bones[main] ? bones[main].name : "";
            if (mainName.Contains("Arm") || mainName.Contains("Hand") || mainName.Contains("Leg") || mainName.Contains("Foot") || mainName.Contains("Toe")) continue;
            if (mainName.StartsWith("Head") || mainName.StartsWith("head")) continue;   // lips and cheeks are red too
            var c = tex.GetPixelBilinear(uv[i].x, uv[i].y);
            if (lowClose) { if (isRed(c)) sash.Add(i); continue; }   // the sash only gets recoloured, not rigged
            allowed[i] = true;
            if (isRed(c)) scarf.Add(i);
        }
        // grow into neighbouring vertices (shadowed folds, the frayed end) so no stray vertex is left behind
        var tri = src.triangles;
        for (int pass = 0; pass < 3; pass++)
        {
            var add = new List<int>();
            for (int k = 0; k < tri.Length; k += 3)
            {
                int a = tri[k], b = tri[k + 1], c = tri[k + 2];
                if (!(scarf.Contains(a) || scarf.Contains(b) || scarf.Contains(c))) continue;
                foreach (int q in new[] { a, b, c }) if (allowed[q] && !scarf.Contains(q)) add.Add(q);
            }
            foreach (int q in add) scarf.Add(q);
        }
        // vertices split along UV seams share a position: they must all move together
        float cell = H * 1e-4f;
        Func<Vector3, Vector3Int> key = v => new Vector3Int(Mathf.RoundToInt(v.x / cell), Mathf.RoundToInt(v.y / cell), Mathf.RoundToInt(v.z / cell));
        var scarfKeys = new HashSet<Vector3Int>(scarf.Select(i => key(verts[i])));
        for (int i = 0; i < verts.Length; i++) if (!scarf.Contains(i) && scarfKeys.Contains(key(verts[i]))) scarf.Add(i);
        foreach (int i in scarf)
        {
            // the loose tail goes out from the knot and then hangs down, so order it by distance from the neck
            float r = (verts[i] - neck).magnitude;
            float f = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ScarfRadialIn * H, ScarfRadialOut * H, r));
            if (f <= 0f) continue;
            radial[i] = r; blend[i] = f; tail.Add(i);
        }
        var paint = new HashSet<int>(scarf);
        paint.UnionWith(sash);
        string colour = PaintScarfYellow(tex, uv, src.triangles, paint, mat);
        UnityEngine.Object.DestroyImmediate(tex);
        if (tail.Count < 30) return "scarf: only " + tail.Count + " tail vertices";

        // the tail's centre line: centroids of slices ordered by distance from the neck
        int nj = ScarfBones + 1;
        float rMin = tail.Min(i => radial[i]), rMax = tail.Max(i => radial[i]);
        var joints = new Vector3[nj];
        for (int k = 0; k < nj; k++)
        {
            float lo = Mathf.Lerp(rMin, rMax, k / (float)nj), hi = Mathf.Lerp(rMin, rMax, (k + 1) / (float)nj);
            var slice = tail.Where(i => radial[i] >= lo && radial[i] <= hi).ToList();
            joints[k] = slice.Count > 0 ? slice.Aggregate(Vector3.zero, (s, i) => s + verts[i]) / slice.Count : (k > 0 ? joints[k - 1] : neck);
        }
        // stretched centre line: same bends, every segment longer
        var stretched = new Vector3[nj];
        stretched[0] = joints[0];
        for (int k = 1; k < nj; k++) stretched[k] = stretched[k - 1] + (joints[k] - joints[k - 1]) * ScarfStretch;

        // move each tail vertex to the same place relative to the longer centre line, and note where it sits on it
        var along = new float[verts.Length];
        foreach (int i in tail)
        {
            int seg = 0; float segT = 0f, best = float.MaxValue;
            for (int k = 0; k < nj - 1; k++)
            {
                var a = joints[k]; var ab = joints[k + 1] - a;
                float tt = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(verts[i] - a, ab) / ab.sqrMagnitude) : 0f;
                float dd = (a + ab * tt - verts[i]).sqrMagnitude;
                if (dd < best) { best = dd; seg = k; segT = tt; }
            }
            var onLine = Vector3.Lerp(joints[seg], joints[seg + 1], segT);
            var onLong = Vector3.Lerp(stretched[seg], stretched[seg + 1], segT);
            verts[i] = Vector3.Lerp(verts[i], onLong + (verts[i] - onLine), blend[i]);
            along[i] = seg + segT;
        }

        // bones along the stretched line, parented under the neck, plus an end marker for the simulation
        var newBones = new Transform[ScarfBones];
        var newBind = new Matrix4x4[ScarfBones];
        var parentMesh = bind[iNeck].inverse;
        var parentT = bones[iNeck];
        for (int k = 0; k < ScarfBones; k++)
        {
            var dir = stretched[k + 1] - stretched[k];
            var w = Matrix4x4.TRS(stretched[k], Quaternion.FromToRotation(Vector3.up, dir.sqrMagnitude > 1e-10f ? dir.normalized : up), Vector3.one);
            var local = parentMesh.inverse * w;
            var t = new GameObject("Scarf." + (k + 1)).transform;
            t.SetParent(parentT, false);
            t.localPosition = local.GetColumn(3);
            t.localRotation = local.rotation;
            t.localScale = local.lossyScale;
            newBones[k] = t; newBind[k] = w.inverse;
            parentMesh = w; parentT = t;
        }
        var end = new GameObject("Scarf.End").transform;
        end.SetParent(parentT, false);
        end.localPosition = parentMesh.inverse.MultiplyPoint3x4(stretched[nj - 1]);

        // re-skin: weight follows the bones along the tail; near the neck the old weights stay
        int baseCount = bones.Length;
        foreach (int i in tail)
        {
            var w = new Dictionary<int, float>();
            Action<int, float> add = (b, v) => { if (v <= 0f) return; float o; w.TryGetValue(b, out o); w[b] = o + v; };
            float f = blend[i];
            add(bw[i].boneIndex0, bw[i].weight0 * (1f - f)); add(bw[i].boneIndex1, bw[i].weight1 * (1f - f));
            add(bw[i].boneIndex2, bw[i].weight2 * (1f - f)); add(bw[i].boneIndex3, bw[i].weight3 * (1f - f));
            float hatSum = 0f;
            var hats = new float[ScarfBones];
            for (int b = 0; b < ScarfBones; b++) { hats[b] = Mathf.Max(0f, 1f - Mathf.Abs(along[i] - (b + 0.5f))); hatSum += hats[b]; }
            if (hatSum < 1e-5f) { hats[Mathf.Clamp((int)along[i], 0, ScarfBones - 1)] = 1f; hatSum = 1f; }
            for (int b = 0; b < ScarfBones; b++) add(baseCount + b, hats[b] / hatSum * f);
            var top4 = w.OrderByDescending(kv => kv.Value).Take(4).ToList();
            float sum = top4.Sum(kv => kv.Value);
            var nw = new BoneWeight();
            for (int q = 0; q < top4.Count; q++)
            {
                float v = top4[q].Value / sum;
                if (q == 0) { nw.boneIndex0 = top4[q].Key; nw.weight0 = v; }
                if (q == 1) { nw.boneIndex1 = top4[q].Key; nw.weight1 = v; }
                if (q == 2) { nw.boneIndex2 = top4[q].Key; nw.weight2 = v; }
                if (q == 3) { nw.boneIndex3 = top4[q].Key; nw.weight3 = v; }
            }
            bw[i] = nw;
        }

        var mesh = UnityEngine.Object.Instantiate(src);
        mesh.name = "Prince_ScarfRig";
        mesh.vertices = verts;
        mesh.boneWeights = bw;
        mesh.bindposes = bind.Concat(newBind).ToArray();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, Gen + "/Prince_ScarfRig.asset");
        smr.bones = bones.Concat(newBones).ToArray();
        smr.sharedMesh = mesh;
        return "scarf: " + tail.Count + " tail vertices on " + ScarfBones + " bones, tail " + (Vector3.Distance(joints[0], joints[nj - 1]) / H).ToString("F2")
               + " -> " + (Vector3.Distance(stretched[0], stretched[nj - 1]) / H).ToString("F2") + " body heights; " + colour;
    }

    // Repaints only the scarf's area of the texture (the UV triangles of scarf faces, grown by a couple of pixels)
    // from red to warm yellow, keeping the painted shading, and points the material at the copy.
    static string PaintScarfYellow(Texture2D tex, Vector2[] uv, int[] tris, HashSet<int> scarf, Material mat)
    {
        int W = tex.width, Hh = tex.height;
        var mask = new bool[W * Hh];
        int faces = 0;
        for (int i = 0; i < tris.Length; i += 3)
        {
            int a = tris[i], b = tris[i + 1], c = tris[i + 2];
            int inScarf = (scarf.Contains(a) ? 1 : 0) + (scarf.Contains(b) ? 1 : 0) + (scarf.Contains(c) ? 1 : 0);
            if (inScarf < 2) continue;
            faces++;
            Vector2 p0 = new Vector2(uv[a].x * W, uv[a].y * Hh), p1 = new Vector2(uv[b].x * W, uv[b].y * Hh), p2 = new Vector2(uv[c].x * W, uv[c].y * Hh);
            int x0 = Mathf.Max(0, (int)Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x)) - 1), x1 = Mathf.Min(W - 1, (int)Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x)) + 1);
            int y0 = Mathf.Max(0, (int)Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)) - 1), y1 = Mathf.Min(Hh - 1, (int)Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y)) + 1);
            float area = (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
            if (Mathf.Abs(area) < 1e-6f) continue;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float w0 = ((p1.x - px) * (p2.y - py) - (p2.x - px) * (p1.y - py)) / area;
                    float w1 = ((p2.x - px) * (p0.y - py) - (p0.x - px) * (p2.y - py)) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 >= -0.02f && w1 >= -0.02f && w2 >= -0.02f) mask[x + y * W] = true;
                }
        }
        // grow the mask by two pixels so filtering at island edges doesn't show red fringes
        for (int pass = 0; pass < 2; pass++)
        {
            var grown = (bool[])mask.Clone();
            for (int y = 1; y < Hh - 1; y++)
                for (int x = 1; x < W - 1; x++)
                    if (!mask[x + y * W] && (mask[x - 1 + y * W] || mask[x + 1 + y * W] || mask[x + (y - 1) * W] || mask[x + (y + 1) * W])) grown[x + y * W] = true;
            mask = grown;
        }
        var px32 = tex.GetPixels32();
        int painted = 0;
        for (int i = 0; i < px32.Length; i++)
        {
            if (!mask[i]) continue;
            float h, s, v;
            Color.RGBToHSV(px32[i], out h, out s, out v);
            if (!((h < 0.08f || h > 0.92f) && s > 0.25f)) continue;
            float dh = h > 0.5f ? h - 1f : h;                 // keep the small hue variations of the painting
            var col = Color.HSVToRGB(Mathf.Repeat(0.125f + dh * 0.4f, 1f), Mathf.Clamp01(s * 0.82f), Mathf.Clamp01(v * 1.1f + 0.06f));
            px32[i] = new Color32((byte)(col.r * 255f), (byte)(col.g * 255f), (byte)(col.b * 255f), px32[i].a);
            painted++;
        }
        tex.SetPixels32(px32);
        tex.Apply();
        string outPath = MeshyDir + "/Prince_BaseColor_YellowScarf.png";
        System.IO.File.WriteAllBytes(outPath, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(outPath);
        var ti = (TextureImporter)AssetImporter.GetAtPath(outPath);
        ti.maxTextureSize = 2048; ti.anisoLevel = 2;
        ti.SaveAndReimport();
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(outPath));
        return "yellow scarf: " + faces + " faces, " + painted + " texels repainted";
    }
    static void ConfigureMeshy(string path, string clipName, Avatar source)
    {
        var imp = (ModelImporter)AssetImporter.GetAtPath(path);
        imp.globalScale = 1f; imp.useFileScale = true;
        imp.materialImportMode = ModelImporterMaterialImportMode.None;
        imp.importCameras = false; imp.importLights = false; imp.importBlendShapes = false;
        imp.importAnimation = true;
        imp.animationType = ModelImporterAnimationType.Generic;
        imp.avatarSetup = source ? ModelImporterAvatarSetup.CopyFromOther : ModelImporterAvatarSetup.CreateFromThisModel;
        if (source) imp.sourceAvatar = source;
        var clips = imp.defaultClipAnimations;
        foreach (var c in clips) { c.name = clipName; c.loopTime = true; c.loopPose = true; }
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
    }

    // A pose clip: every bone's rotation and position averaged between two times of a clip
    // (pass the same time twice to freeze a single frame).
    static AnimationClip PoseClip(AnimationClip src, float ta, float tb, string name)
    {
        var dst = new AnimationClip { name = name, frameRate = 30f };
        var byPath = new Dictionary<string, List<EditorCurveBinding>>();
        foreach (var b in AnimationUtility.GetCurveBindings(src))
        {
            List<EditorCurveBinding> l;
            if (!byPath.TryGetValue(b.path, out l)) byPath[b.path] = l = new List<EditorCurveBinding>();
            l.Add(b);
        }
        foreach (var kv in byPath)
        {
            var curves = kv.Value.ToDictionary(b => b.propertyName, b => b);
            Func<string, float, float> at = (p, t) => AnimationUtility.GetEditorCurve(src, curves[p]).Evaluate(t);
            var done = new HashSet<string>();
            if (curves.ContainsKey("m_LocalRotation.x") && curves.ContainsKey("m_LocalRotation.w"))
            {
                var qa = new Quaternion(at("m_LocalRotation.x", ta), at("m_LocalRotation.y", ta), at("m_LocalRotation.z", ta), at("m_LocalRotation.w", ta));
                var qb = new Quaternion(at("m_LocalRotation.x", tb), at("m_LocalRotation.y", tb), at("m_LocalRotation.z", tb), at("m_LocalRotation.w", tb));
                var q = Quaternion.Slerp(qa.normalized, qb.normalized, 0.5f);
                float[] qv = { q.x, q.y, q.z, q.w };
                string[] qn = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
                for (int i = 0; i < 4; i++) { AnimationUtility.SetEditorCurve(dst, curves[qn[i]], AnimationCurve.Constant(0f, 1f, qv[i])); done.Add(qn[i]); }
            }
            foreach (var kv2 in curves)
            {
                if (done.Contains(kv2.Key)) continue;
                float v = (at(kv2.Key, ta) + at(kv2.Key, tb)) * 0.5f;
                AnimationUtility.SetEditorCurve(dst, kv2.Value, AnimationCurve.Constant(0f, 1f, v));
            }
        }
        var settings = AnimationUtility.GetAnimationClipSettings(dst);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(dst, settings);
        return dst;
    }

    // ------------------------------------------------------------------ night
    static void ApplyNight(Camera cam, Transform world, GameObject house, GameObject[] volcanoes, GameObject clockPlanet)
    {
        // hero shot: pulled back and lowered so more sky shows behind the asteroid
        var heroTarget = CamTarget + Vector3.up * 0.35f * AstK;
        cam.fieldOfView = 30f;
        cam.transform.position = heroTarget + new Vector3(0.36f, 0.3f, 1f).normalized * CamDist * 1.2f;
        cam.transform.LookAt(heroTarget);
        var camT = cam.transform;
        Func<float, float, Vector3> view = (x, y) => cam.ViewportPointToRay(new Vector3(x, y, 0f)).direction;

        // sky: the moon sits upper left, the Milky Way sweeps from lower left to upper right behind the clock planet
        var sky = new Material(Shader.Find("B612/Night Sky")) { name = "B612_NightSky" };
        sky.SetTexture("_Noise3D", MakeNoise3D());
        var moonDir = view(0.15f, 0.8f);
        var horizon = view(0.5f, -0.12f);
        var skyUp = Vector3.Cross(camT.right, horizon).normalized;
        if (Vector3.Dot(skyUp, camT.up) < 0f) skyUp = -skyUp;
        var core = view(0.74f, 0.74f);
        sky.SetVector("_SkyUp", skyUp);
        sky.SetVector("_MoonDir", moonDir);
        sky.SetVector("_MilkyNormal", Vector3.Cross(view(-0.1f, 0.12f), view(1.1f, 0.98f)).normalized);
        sky.SetVector("_MilkyCore", core);
        sky.SetVector("_PoleDir", core);
        sky.SetVector("_GlowDir", view(0.9f, 0.05f));
        AssetDatabase.CreateAsset(sky, Gen + "/Materials/B612_NightSky.mat");

        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Hex("#34428A");
        RenderSettings.ambientEquatorColor = Hex("#232A5C");
        RenderSettings.ambientGroundColor = Hex("#232650");
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        DynamicGI.UpdateEnvironment();
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.farClipPlane = 400f;
        cam.GetComponent<UniversalAdditionalCameraData>().dithering = true;

        // moonlight: a cool key from the upper left, and a rim light coming straight from the painted moon
        var key = GameObject.Find("Sun").GetComponent<Light>();
        key.name = "Moon Key";
        key.color = Hex("#93A8EC"); key.intensity = 0.66f; key.shadowStrength = 0.85f;
        key.transform.rotation = Quaternion.LookRotation(-(-camT.right * 0.8f + Vector3.up * 0.9f - camT.forward * 0.25f).normalized);
        var rim = GameObject.Find("Fill").GetComponent<Light>();
        rim.name = "Moon Rim";
        rim.color = Hex("#BFD0FF"); rim.intensity = 0.9f; rim.shadows = LightShadows.None;
        rim.transform.rotation = Quaternion.LookRotation(-moonDir);
        new GameObject("Night Sky").AddComponent<B612.NightSky>().moonLight = rim;

        // warm pools of light
        var winOut = house.transform.TransformPoint(new Vector3(-0.6f, 0.62f, 0.6f) + new Vector3(-0.707f, 0f, 0.707f) * 0.35f);
        PointLight("Window Light", winOut, world, "#FFB45C", 2.4f, 2.6f, false);

        var lamp = BuildLampPost(world);
        var lampXZ = house.transform.TransformPoint(new Vector3(-1.0f, 0f, 1.45f));
        lamp.transform.position = OnTop(lampXZ.x, lampXZ.z) - Vector3.up * 0.02f;
        PointLight("Lamp Light", lamp.transform.TransformPoint(new Vector3(0f, 0.99f, 0f)), world, "#FFC27A", 2.6f, 3.4f, true);

        var lookout = clockPlanet.transform.Find("Lookout");
        if (lookout) PointLight("Lookout Light", lookout.TransformPoint(new Vector3(0f, 0.42f, 0f)), world, "#FFC45A", 2f, 2.4f, false);

        foreach (var volcano in volcanoes)
        {
            var lava = Warp(Loft(new[] { new Ring(0.325f, 0.115f), new Ring(0.345f, 0.125f) }, 12, 0.3f), VolcanoWarp);
            Obj("Lava", lava, volcano.transform, Mat("#FF6A2B", 0.5f, 3.5f)).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            float vs = volcano.transform.localScale.x;
            PointLight("Volcano Glow", volcano.transform.TransformPoint(new Vector3(0f, 0.62f, 0f)), world, "#FF7A3A", 1.6f * vs, 1.8f * vs, false);
        }

        // fireflies over the plateau, stardust drifting around the whole asteroid
        var firefly = new Material(Shader.Find("B612/Glow Particle")) { name = "B612_Firefly" };
        firefly.SetFloat("_Intensity", 3.2f);
        AssetDatabase.CreateAsset(firefly, Gen + "/Materials/B612_Firefly.mat");
        var dust = new Material(firefly) { name = "B612_Stardust" };
        dust.SetFloat("_Intensity", 2.2f);
        AssetDatabase.CreateAsset(dust, Gen + "/Materials/B612_Stardust.mat");
        Motes("Fireflies", world, firefly, new Vector3(0f, PlateauY, 0f), ParticleSystemShapeType.Hemisphere, 3.6f, 1f, 90, 12f, new Vector2(5f, 8f), new Vector2(0.03f, 0.055f), Hex("#FFD27A"), Hex("#FFF0B8"), 0.18f);
        var grove = world.Find("Far Side Grove");
        if (grove)
        {
            var gf = Motes("Grove Fireflies", world, firefly, grove.position, ParticleSystemShapeType.Hemisphere, 2.6f, 1f, 45, 7f, new Vector2(5f, 8f), new Vector2(0.03f, 0.055f), Hex("#FFD27A"), Hex("#BFF4FF"), 0.18f);
            gf.transform.rotation = grove.rotation;
        }
        Motes("Stardust", world, dust, Vector3.zero, ParticleSystemShapeType.Sphere, 5.4f * AstK, 0.45f, 220, 28f, new Vector2(6f, 10f), new Vector2(0.025f, 0.06f), Hex("#9CC2FF"), Hex("#E9D8FF"), 0.1f);
    }

    static Light PointLight(string name, Vector3 pos, Transform parent, string hex, float intensity, float range, bool shadows)
    {
        var l = new GameObject(name).AddComponent<Light>();
        l.transform.SetParent(parent, false);
        l.transform.position = pos;
        l.type = LightType.Point;
        l.color = Hex(hex); l.intensity = intensity; l.range = range;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        if (shadows) { l.shadowStrength = 0.8f; l.shadowNearPlane = 0.1f; }
        l.gameObject.AddComponent<UniversalAdditionalLightData>();
        return l;
    }

    static GameObject BuildLampPost(Transform parent)
    {
        var root = new GameObject("Lamp Post").transform; root.SetParent(parent, false);
        var iron = new FM(1);
        iron.Append(Loft(new[] { new Ring(0f, 0.075f), new Ring(0.05f, 0.065f), new Ring(0.1f, 0.032f), new Ring(0.84f, 0.022f), new Ring(0.88f, 0.045f), new Ring(0.9f, 0.045f) }, 8, Mathf.PI / 8f), Matrix4x4.identity, 0);
        for (int k = 0; k < 4; k++)
            iron.Append(Box(new Vector3(0.012f, 0.17f, 0.012f)), Matrix4x4.Translate(Quaternion.Euler(0f, k * 90f + 45f, 0f) * new Vector3(0.062f, 0f, 0f) + Vector3.up * 0.985f), 0);
        Obj("Lamp Post Iron", iron, root, Mat("#3B3431", 0.35f, 0f, false, 0.4f));
        var cap = new FM(1);
        cap.Append(Loft(new[] { new Ring(1.07f, 0.1f), new Ring(1.1f, 0.1f), new Ring(1.18f, 0.035f), new Ring(1.22f, 0.012f) }, 4, Mathf.PI / 4f), Matrix4x4.identity, 0);
        cap.Append(Ico(0, 0.022f), Matrix4x4.Translate(new Vector3(0f, 1.24f, 0f)), 0);
        Obj("Lamp Post Cap", cap, root, Mat("#B86A3B", 0.35f, 0f, false, 0.3f));
        var glass = new FM(1);
        glass.Append(Loft(new[] { new Ring(0.9f, 0.05f), new Ring(1.07f, 0.068f) }, 4, Mathf.PI / 4f), Matrix4x4.identity, 0);
        Obj("Lamp Post Glass", glass, root, Mat("#FFC45A", 0.6f, 5f)).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        var col = root.gameObject.AddComponent<CapsuleCollider>();
        col.radius = 0.06f; col.height = 1.2f; col.center = new Vector3(0f, 0.6f, 0f);
        return root.gameObject;
    }

    static ParticleSystem Motes(string name, Transform parent, Material mat, Vector3 pos, ParticleSystemShapeType shape, float radius, float thickness,
                                int max, float rate, Vector2 life, Vector2 size, Color a, Color b, float wander)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 10f; main.loop = true; main.prewarm = true; main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.05f);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        var em = ps.emission; em.rateOverTime = rate;
        var sh = ps.shape; sh.shapeType = shape; sh.radius = radius; sh.radiusThickness = thickness;
        if (shape == ParticleSystemShapeType.Hemisphere) sh.rotation = new Vector3(-90f, 0f, 0f);
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.35f, 0.35f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0.4f, 0.78f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var nz = ps.noise; nz.enabled = true; nz.strength = wander; nz.frequency = 0.45f; nz.scrollSpeed = 0.15f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.maxParticleSize = 0.08f;
        return ps;
    }

    static GameObject MakeVisualPrefab(string name)
    {
        string path = "Assets/B612/Models/" + name + ".fbx";
        var imp = AssetImporter.GetAtPath(path) as ModelImporter;
        if (imp == null) throw new Exception("missing model " + path);
        imp.globalScale = 1f; imp.useFileScale = true; imp.bakeAxisConversion = true;
        imp.importNormals = ModelImporterNormals.Import;
        imp.importAnimation = false;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        imp.meshCompression = ModelImporterMeshCompression.Off;
        imp.importBlendShapes = false; imp.importCameras = false; imp.importLights = false;
        imp.SaveAndReimport();
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = name + " Model";
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            var ms = r.sharedMaterials;
            for (int i = 0; i < ms.Length; i++)
            {
                if (!ms[i]) continue;
                string n = ms[i].name; int k = n.IndexOf("M_");
                if (k >= 0 && n.Length >= k + 8) ms[i] = Mat("#" + n.Substring(k + 2, 6), 0.15f, 0f, false, 0f, false);
            }
            r.sharedMaterials = ms;
        }
        var wrapper = new GameObject(name + " Visual");
        model.transform.SetParent(wrapper.transform, false);
        var front = FindDeep(model.transform, "_Front");
        string orient = "none";
        if (front)
        {
            var lp = wrapper.transform.InverseTransformPoint(front.position);
            if (lp.y < 0f) model.transform.localRotation = Quaternion.Euler(180f, 0f, 0f) * model.transform.localRotation;
            lp = wrapper.transform.InverseTransformPoint(front.position);
            if (lp.z < 0f) model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
            orient = wrapper.transform.InverseTransformPoint(front.position).ToString("F2");
            UnityEngine.Object.DestroyImmediate(front.gameObject);
        }
        var b = new Bounds(wrapper.transform.position, Vector3.zero);
        foreach (var r in wrapper.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        Debug.Log("B612 visual " + name + " front=" + orient + " bounds=" + b.size.ToString("F3"));
        var prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, Gen + "/Prefabs/" + name + "Visual.prefab");
        UnityEngine.Object.DestroyImmediate(wrapper);
        return prefab;
    }

    static Transform FindDeep(Transform t, string n)
    {
        if (t.name == n) return t;
        for (int i = 0; i < t.childCount; i++) { var r = FindDeep(t.GetChild(i), n); if (r) return r; }
        return null;
    }

    static void BuildTurnaround(VolumeProfile profile, GameObject princePrefab, GameObject foxPrefab)
    {
        var cam = MakeCameraAndLights(profile, "#EFE6D6");
        cam.fieldOfView = 36f;
        cam.aspect = 1.5f;
        cam.transform.position = new Vector3(-0.65f, 2.7f, 6.2f);
        cam.transform.LookAt(new Vector3(-0.65f, 0.55f, 0f));
        var sun = GameObject.Find("Sun").transform;
        sun.rotation = Quaternion.Euler(52f, 160f, 0f);

        var ground = new FM(1);
        ground.Quad(new Vector3(-30, 0, -30), new Vector3(-30, 0, 30), new Vector3(30, 0, 30), new Vector3(30, 0, -30));
        Obj("Ground (shadow catcher)", ground, null, Mat("#F3E9D4", 0.05f));

        float[] yaw = { 0f, 90f, 180f };
        for (int i = 0; i < 3; i++)
        {
            var p = (GameObject)PrefabUtility.InstantiatePrefab(princePrefab);
            p.transform.position = new Vector3(1.3f - i * 1.3f, 0f, 0f);
            p.transform.rotation = Quaternion.Euler(0f, yaw[i], 0f);
            p.transform.localScale = Vector3.one * 1.35f;
        }
        var f = (GameObject)PrefabUtility.InstantiatePrefab(foxPrefab);
        f.transform.position = new Vector3(-2.7f, 0f, 0.2f);
        f.transform.rotation = Quaternion.Euler(0f, 55f, 0f);
        f.transform.localScale = Vector3.one * 1.2f;
    }

    static Vector3 OnTop(float x, float z)
    {
        RaycastHit hit;
        if (Physics.Raycast(new Vector3(x, 20f, z), Vector3.down, out hit, 40f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
        return new Vector3(x, PlateauY, z);
    }

    // ------------------------------------------------------------------ props
    static GameObject BuildAsteroid(Transform parent)
    {
        string[] cols = { "#EADFC6", "#E1D4B6", "#F0E7D2", "#D8CAAC", "#CDBE9F", "#C0B193", "#AC9E81", "#9C8E73" };
        float k = AstK;
        // a few shallow craters on the sides and underneath (kept away from the plateau and the baobab grove)
        var cr = new System.Random(77);
        var grove = new Vector3(0.2f, -1f, -0.35f).normalized;
        var craters = new List<Vector4>();
        while (craters.Count < 7)
        {
            var d = new Vector3((float)cr.NextDouble() * 2f - 1f, (float)cr.NextDouble() * 2f - 1f, (float)cr.NextDouble() * 2f - 1f);
            if (d.sqrMagnitude < 0.05f) continue;
            d.Normalize();
            if (d.y > 0.25f || Vector3.Angle(d, grove) < 40f) continue;
            craters.Add(new Vector4(d.x, d.y, d.z, 0.12f + (float)cr.NextDouble() * 0.12f));
        }
        var fm = Ico(4, 1f, u =>
        {
            float n = Noise3(u * 1.5f) * 0.65f + Noise3(u * 3.3f + Vector3.one * 7f) * 0.35f;
            float detail = Noise3(u * 7.5f + Vector3.one * 11f) * 0.035f + Noise3(u * 15f + Vector3.one * 23f) * 0.012f;
            float strata = Mathf.Sin(u.y * 9f + Noise3(u * 2.2f) * 3f) * 0.012f;     // faint rock layers
            float dent = 0f;
            foreach (var c in craters)
            {
                float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(u, new Vector3(c.x, c.y, c.z)), -1f, 1f)) / c.w;
                if (ang < 1f) dent -= (1f - ang * ang) * 0.045f;
                dent += Mathf.Exp(-Mathf.Pow((ang - 1.05f) / 0.18f, 2f)) * 0.014f;     // raised rim
            }
            var p = u * AstRadius * (1f + n * 0.42f + detail + strata + dent);
            p.y *= 0.84f;
            if (p.y > PlateauY) p.y = PlateauY + (p.y - PlateauY) * 0.14f + Noise3(u * 7f + Vector3.one * 3f) * 0.06f + Noise3(u * 19f) * 0.012f;
            return p;
        }, (c, n) =>
        {
            int r = rng.Next(2);
            float y = c.y + Noise3(c * 0.6f + Vector3.one * 5f) * 0.9f;            // wavy band edges
            if (n.y > 0.8f && c.y > PlateauY - 0.3f * k) return rng.Next(3);
            if (n.y > 0.25f && y > 0.8f * k) return 3 + r;
            if (y > -0.2f * k && n.y > -0.35f) return 4 + r;
            if (y > -1.4f * k) return 5 + r;
            return 6 + r;
        }, 8);
        // rock knobs sticking out of the sides
        // (own random sequence, kept below the plateau rim and away from the grove)
        var kr = new System.Random(1312);
        Func<float, float, float> KR = (lo, hi) => lo + (float)kr.NextDouble() * (hi - lo);
        for (int i = 0, made = 0; made < 13 && i < 200; i++)
        {
            var d = new Vector3(KR(-1f, 1f), KR(-0.7f, 0.12f), KR(-1f, 1f)).normalized;
            if (Vector3.Angle(d, grove) < 35f) continue;
            var p = d * AstRadius * 0.97f; p.y *= 0.84f;
            fm.Append(Rock(KR(0.25f, 0.5f) * k, 0), Matrix4x4.TRS(p, Quaternion.Euler(KR(0, 360), KR(0, 360), 0), Vector3.one), 4 + kr.Next(4));
            made++;
        }
        var ms = new Material[cols.Length];
        for (int i = 0; i < cols.Length; i++) ms[i] = Mat(cols[i], 0.08f);
        return Obj("Asteroid B-612", fm, parent, ms);
    }

    // ------------------------------------------------------------------ house helpers
    const float HouseScale = 1.4f;
    const int HouseLayer = 1;          // TransparentFX: the camera ignores it while the prince is inside
    const int DoorFace = 1;            // the octagon face centred on +z
    // door arch in wall-face coordinates (u across in metres, v up), from bottom right over the top to bottom left
    static readonly Vector2[] DoorArch = { new Vector2(0.21f, 0f), new Vector2(0.21f, 0.48f), new Vector2(0.17f, 0.6f), new Vector2(0.09f, 0.67f), new Vector2(0f, 0.69f), new Vector2(-0.09f, 0.67f), new Vector2(-0.17f, 0.6f), new Vector2(-0.21f, 0.48f), new Vector2(-0.21f, 0f) };
    static readonly Vector2[] DoorFrameOuter = { new Vector2(0.255f, 0f), new Vector2(0.255f, 0.49f), new Vector2(0.21f, 0.635f), new Vector2(0.11f, 0.715f), new Vector2(0f, 0.735f), new Vector2(-0.11f, 0.715f), new Vector2(-0.21f, 0.635f), new Vector2(-0.255f, 0.49f), new Vector2(-0.255f, 0f) };

    // A point on a (slightly tapering) wall face; u stays in metres even though the face narrows toward the top.
    static Vector3 FacePoint(Vector3 b0, Vector3 b1, Vector3 t0, Vector3 t1, float u, float v, float height)
    {
        float s = v / height;
        var l = Vector3.Lerp(b0, t0, s);
        var r = Vector3.Lerp(b1, t1, s);
        return Vector3.Lerp(l, r, Mathf.Clamp01(u / Mathf.Max(Vector3.Distance(l, r), 1e-4f) + 0.5f));
    }

    // One wall face; the door face gets an arch-shaped hole (triangulated around the arch from the panel edges).
    static void WallFace(FM fm, Vector3 b0, Vector3 b1, Vector3 t0, Vector3 t1, float h, Vector3 facing, bool door)
    {
        if (!door) { fm.QuadO(b0, b1, t1, t0, facing, 0); return; }
        Func<int, Vector3> A = k => FacePoint(b0, b1, t0, t1, DoorArch[k].x, DoorArch[k].y, h);
        Func<float, Vector3> R = v => Vector3.Lerp(b1, t1, v / h);
        Func<float, Vector3> L = v => Vector3.Lerp(b0, t0, v / h);
        float vs = DoorArch[1].y;
        fm.QuadO(A(0), b1, R(vs), A(1), facing, 0);
        fm.TriO(A(1), R(vs), t1, facing, 0);
        for (int k = 1; k < 4; k++) fm.TriO(A(k), t1, A(k + 1), facing, 0);
        fm.TriO(A(4), t1, t0, facing, 0);
        for (int k = 4; k < 7; k++) fm.TriO(A(k), t0, A(k + 1), facing, 0);
        fm.TriO(A(7), t0, L(vs), facing, 0);
        fm.QuadO(A(7), L(vs), b0, A(8), facing, 0);
    }

    // A see-through copy of a material for the house shell (alpha is driven at runtime).
    static readonly Dictionary<Material, Material> fades = new Dictionary<Material, Material>();
    static Material FadeVariant(Material m)
    {
        Material f;
        if (fades.TryGetValue(m, out f) && f) return f;
        f = new Material(m) { name = m.name + "_Fade" };
        f.SetFloat("_Surface", 1f);
        f.SetFloat("_Blend", 0f);
        f.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        f.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        f.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        f.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        f.SetFloat("_ZWrite", 0f);
        f.SetOverrideTag("RenderType", "Transparent");
        f.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        f.DisableKeyword("_ALPHATEST_ON");
        f.renderQueue = (int)RenderQueue.Transparent;
        AssetDatabase.CreateAsset(f, Gen + "/Materials/" + f.name + "_" + fades.Count + ".mat");
        fades[m] = f;
        return f;
    }

    static GameObject BuildHouse(Transform parent)
    {
        var root = new GameObject("Boiler House").transform;
        root.SetParent(parent, false);
        float pi8 = Mathf.PI / 8f;

        var interior = new GameObject("Interior").transform;
        interior.SetParent(root, false);

        // walls: an outer shell facing out and a wallpapered lining facing in, with an arched doorway on the +z face
        const float rB = 0.95f, rT = 0.9f, hW = 1.06f, lining = 0.045f;
        var outer = new FM(1); var inner = new FM(1); var jamb = new FM(1); var plinth = new FM(1);
        Vector3 doorB0 = Vector3.zero, doorB1 = Vector3.zero, doorT0 = Vector3.zero, doorT1 = Vector3.zero, doorFacing = Vector3.forward;
        Vector3 inB0 = Vector3.zero, inB1 = Vector3.zero, inT0 = Vector3.zero, inT1 = Vector3.zero;
        for (int i = 0; i < 8; i++)
        {
            float a0 = pi8 + i * Mathf.PI / 4f, a1 = a0 + Mathf.PI / 4f;
            Func<float, float, float, Vector3> P = (r, a, y) => new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            var facing = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f));
            bool door = i == DoorFace;
            Vector3 b0 = P(rB, a0, 0f), b1 = P(rB, a1, 0f), t0 = P(rT, a0, hW), t1 = P(rT, a1, hW);
            Vector3 c0 = P(rB - lining, a0, 0f), c1 = P(rB - lining, a1, 0f), d0 = P(rT - lining, a0, hW), d1 = P(rT - lining, a1, hW);
            WallFace(outer, b0, b1, t0, t1, hW, facing, door);
            WallFace(inner, c0, c1, d0, d1, hW, -facing, door);
            if (door) { doorB0 = b0; doorB1 = b1; doorT0 = t0; doorT1 = t1; doorFacing = facing; inB0 = c0; inB1 = c1; inT0 = d0; inT1 = d1; continue; }
            // a low plinth band round the base, open at the doorway so the prince can walk in
            Vector3 p0 = P(0.99f, a0, 0f), p1 = P(0.99f, a1, 0f), q0 = P(0.99f, a0, 0.13f), q1 = P(0.99f, a1, 0.13f);
            plinth.QuadO(p0, p1, q1, q0, facing, 0);
            plinth.QuadO(q0, q1, P(0.95f, a1, 0.13f), P(0.95f, a0, 0.13f), Vector3.up, 0);
        }
        // the doorway's reveal joins the shell to the lining
        var ctrOut = FacePoint(doorB0, doorB1, doorT0, doorT1, 0f, 0.3f, hW);
        for (int k = 0; k < DoorArch.Length - 1; k++)
        {
            Vector3 o0 = FacePoint(doorB0, doorB1, doorT0, doorT1, DoorArch[k].x, DoorArch[k].y, hW), o1 = FacePoint(doorB0, doorB1, doorT0, doorT1, DoorArch[k + 1].x, DoorArch[k + 1].y, hW);
            Vector3 i0 = FacePoint(inB0, inB1, inT0, inT1, DoorArch[k].x, DoorArch[k].y, hW), i1 = FacePoint(inB0, inB1, inT0, inT1, DoorArch[k + 1].x, DoorArch[k + 1].y, hW);
            jamb.QuadO(o0, o1, i1, i0, ctrOut - (o0 + o1) * 0.5f, 0);
        }
        Obj("Walls", outer, root, Mat("#F0E7D5"));
        Obj("Door Reveal", jamb, root, Mat("#E2D6BF"));

        var bands = new FM(2);
        bands.Append(plinth, Matrix4x4.identity, 0);
        bands.Append(Loft(new[] { new Ring(0.97f, 0.945f), new Ring(1.1f, 0.945f) }, 8, pi8, false, false), Matrix4x4.identity, 0);
        for (int k = 0; k < 8; k++)
        {
            float a = pi8 + (k + 0.5f) * Mathf.PI / 4f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            for (int s = -1; s <= 1; s += 2)
            {
                var side = Vector3.Cross(Vector3.up, dir) * 0.22f * s;
                if (k != DoorFace) bands.Append(Ico(0, 0.028f), Matrix4x4.Translate(dir * 0.99f * 0.94f + side + Vector3.up * 0.065f), 1);
                bands.Append(Ico(0, 0.028f), Matrix4x4.Translate(dir * 0.945f * 0.94f + side + Vector3.up * 1.035f), 1);
            }
        }
        Obj("Copper Bands", bands, root, Mat("#B86A3B", 0.35f, 0f, false, 0.3f), Mat("#8A4A28", 0.3f));
        Obj("Copper Bands", bands, root, Mat("#B86A3B", 0.35f, 0f, false, 0.3f), Mat("#8A4A28", 0.3f));

        // striped roof
        // bell-curved roof made of more rings
        var roof = Loft(new[] { new Ring(1.1f, 1.16f), new Ring(1.22f, 1.06f), new Ring(1.36f, 0.93f), new Ring(1.52f, 0.78f), new Ring(1.68f, 0.6f), new Ring(1.82f, 0.42f), new Ring(1.96f, 0.22f) }, 16, 0f, true, false, (k, i) => k < 0 ? 2 : (i % 2), 3);
        Obj("Roof", roof, root, Mat("#C8553A", 0.2f), Mat("#F2E8D8", 0.2f), Mat("#8A4A28"));
        var trim = new FM(1);
        trim.Append(Loft(new[] { new Ring(1.06f, 1.2f), new Ring(1.12f, 1.2f) }, 16), Matrix4x4.identity, 0);
        trim.Append(Loft(new[] { new Ring(1.93f, 0.24f), new Ring(2.02f, 0.24f) }, 8), Matrix4x4.identity, 0);
        trim.Append(Loft(new[] { new Ring(2.02f, 0.13f), new Ring(2.13f, 0.13f), new Ring(2.2f, 0.06f), new Ring(2.3f, 0f) }, 8, pi8), Matrix4x4.identity, 0);
        Obj("Roof Trim", trim, root, Mat("#B86A3B", 0.35f, 0f, false, 0.3f));

        // the door stands open, swung out beside the doorway, inside a brass frame round the arch
        var uDir = (doorB1 - doorB0).normalized;
        var hinge = FacePoint(doorB0, doorB1, doorT0, doorT1, 0.21f, 0f, hW) + doorFacing * 0.03f;
        var basis = Matrix4x4.identity;
        basis.SetColumn(0, uDir); basis.SetColumn(1, Vector3.up); basis.SetColumn(2, -doorFacing);
        basis.SetColumn(3, new Vector4(hinge.x, hinge.y, hinge.z, 1f));
        var leafM = basis * Matrix4x4.Rotate(Quaternion.Euler(0f, -160f, 0f)) * Matrix4x4.Translate(new Vector3(-0.21f, 0.01f, 0f));
        var leafPoly = new Vector2[DoorArch.Length];
        for (int k = 0; k < DoorArch.Length; k++) leafPoly[k] = DoorArch[k] * 0.97f;
        var doorWood = new FM(2);
        doorWood.Append(Extrude(leafPoly, 0.045f), leafM, 0);
        foreach (float x in new[] { -0.1f, 0f, 0.1f }) doorWood.Append(Box(new Vector3(0.012f, 0.56f, 0.01f)), leafM * Matrix4x4.Translate(new Vector3(x, 0.3f, -0.027f)), 1);
        Obj("Door", doorWood, root, Mat("#7A4E33"), Mat("#5A3622"));

        var doorFrame = new FM(1);
        for (int k = 0; k < DoorArch.Length - 1; k++)
        {
            Func<Vector2, float, Vector3> FP = (q, lift) => FacePoint(doorB0, doorB1, doorT0, doorT1, q.x, q.y, hW) + doorFacing * lift;
            Vector3 a0 = FP(DoorArch[k], 0.025f), a1 = FP(DoorArch[k + 1], 0.025f), o0 = FP(DoorFrameOuter[k], 0.025f), o1 = FP(DoorFrameOuter[k + 1], 0.025f);
            doorFrame.QuadO(a0, a1, o1, o0, doorFacing, 0);
            doorFrame.QuadO(FP(DoorFrameOuter[k], 0f), FP(DoorFrameOuter[k + 1], 0f), o1, o0, (o0 + o1) * 0.5f - ctrOut, 0);
            doorFrame.QuadO(a0, a1, FP(DoorArch[k + 1], 0f), FP(DoorArch[k], 0f), ctrOut - (a0 + a1) * 0.5f, 0);
        }
        doorFrame.Append(Ico(0, 0.022f), leafM * Matrix4x4.Translate(new Vector3(-0.15f, 0.3f, -0.035f)), 0);
        Obj("Door Frame", doorFrame, root, Mat("#C9973E", 0.45f, 0f, false, 0.4f));
        Obj("Door Frame", doorFrame, root, Mat("#C9973E", 0.45f, 0f, false, 0.4f));

        // glowing window on the front-left face
        Vector2[] win = { new Vector2(-0.11f, -0.12f), new Vector2(0.11f, -0.12f), new Vector2(0.11f, 0.08f), new Vector2(0f, 0.17f), new Vector2(-0.11f, 0.08f) };
        var winM = Matrix4x4.TRS(new Vector3(-0.6f, 0.62f, 0.6f), Quaternion.Euler(0f, -45f, 0f), Vector3.one);
        var glass = new FM(1); glass.Append(Extrude(win, 0.03f), winM * Matrix4x4.Translate(new Vector3(0, 0, 0.02f)), 0);
        Obj("Window Glow", glass, root, Mat("#FFC45A", 0.6f, isNight ? 4.5f : 2.2f));
        var wf = new FM(1);
        wf.Append(Extrude(Scale(win, 1.3f, 1.25f), 0.03f), winM, 0);
        wf.Append(Box(new Vector3(0.012f, 0.26f, 0.012f)), winM * Matrix4x4.Translate(new Vector3(0f, 0.01f, 0.04f)), 0);
        wf.Append(Box(new Vector3(0.21f, 0.012f, 0.012f)), winM * Matrix4x4.Translate(new Vector3(0f, -0.01f, 0.04f)), 0);
        Obj("Window Frame", wf, root, Mat("#C9973E", 0.45f, 0f, false, 0.4f));

        // pressure gauge on the front-right face
        var gM = Matrix4x4.TRS(new Vector3(0.6f, 0.84f, 0.6f), Quaternion.Euler(0f, 45f, 0f), Vector3.one);
        var gRim = new FM(1); gRim.Append(Disc(0.15f, 0.06f, 14), gM * Matrix4x4.Rotate(Quaternion.Euler(90f, 0f, 0f)), 0);
        Obj("Gauge Rim", gRim, root, Mat("#B86A3B", 0.35f, 0f, false, 0.3f));
        var gFace = new FM(2);
        gFace.Append(Disc(0.12f, 0.02f, 14), gM * Matrix4x4.TRS(new Vector3(0, 0, 0.03f), Quaternion.Euler(90f, 0f, 0f), Vector3.one), 0);
        for (int k = 0; k < 8; k++)
        {
            float a = k * 45f;
            gFace.Append(Box(new Vector3(0.008f, 0.025f, 0.008f)), gM * Matrix4x4.TRS(new Vector3(0, 0, 0.042f), Quaternion.Euler(0, 0, a), Vector3.one) * Matrix4x4.Translate(new Vector3(0, 0.09f, 0)), 1);
        }
        Obj("Gauge Face", gFace, root, Mat("#F7F0E1"), Mat("#3A2A22"));
        var needle = new FM(1); needle.Append(Box(new Vector3(0.012f, 0.09f, 0.008f)), gM * Matrix4x4.TRS(new Vector3(0, 0, 0.046f), Quaternion.Euler(0, 0, -35f), Vector3.one) * Matrix4x4.Translate(new Vector3(0, 0.035f, 0)), 0);
        Obj("Gauge Needle", needle, root, Mat("#C0392B"));

        // copper pipes
        var pipe = new FM(1); var flange = new FM(1);
        Pipe(pipe, flange, new[] { new Vector3(0.86f, 1.0f, -0.1f), new Vector3(1.1f, 1.0f, -0.1f), new Vector3(1.1f, 1.86f, -0.1f) }, 0.075f);
        Pipe(pipe, flange, new[] { new Vector3(-0.84f, 0.82f, 0.22f), new Vector3(-1.22f, 0.82f, 0.22f), new Vector3(-1.22f, 0.22f, 0.22f), new Vector3(-1.22f, 0.22f, 0.72f), new Vector3(-0.85f, 0.22f, 0.98f) }, 0.08f);
        Pipe(pipe, flange, new[] { new Vector3(-0.86f, 0.45f, -0.3f), new Vector3(-1.08f, 0.45f, -0.3f), new Vector3(-1.08f, 0.12f, -0.3f) }, 0.06f);
        Pipe(pipe, flange, new[] { new Vector3(0.84f, 0.38f, 0.1f), new Vector3(1.32f, 0.38f, 0.1f), new Vector3(1.7f, 0.38f, -0.25f), new Vector3(1.7f, 0.56f, -0.25f) }, 0.06f);
        Pipe(pipe, flange, new[] { new Vector3(1.32f, 0.38f, 0.1f), new Vector3(1.32f, 0.78f, 0.1f) }, 0.04f);
        flange.Append(Loft(new[] { new Ring(0f, 0.11f), new Ring(0.06f, 0.11f) }, 8), Matrix4x4.Translate(new Vector3(1.1f, 1.86f, -0.1f)), 0);
        Obj("Pipes", pipe, root, Mat("#B86A3B", 0.4f, 0f, false, 0.35f));
        Obj("Pipe Flanges", flange, root, Mat("#8A4A28", 0.35f, 0f, false, 0.3f));

        // brass valve wheel and round table
        var brass = new FM(1);
        brass.Append(Torus(0.13f, 0.022f, 16, 6), Matrix4x4.Translate(new Vector3(1.32f, 0.8f, 0.1f)), 0);
        for (int k = 0; k < 4; k++) brass.Append(Box(new Vector3(0.26f, 0.018f, 0.018f)), Matrix4x4.TRS(new Vector3(1.32f, 0.8f, 0.1f), Quaternion.Euler(0, k * 45f, 0), Vector3.one), 0);
        brass.Append(Disc(0.035f, 0.04f, 8), Matrix4x4.Translate(new Vector3(1.32f, 0.8f, 0.1f)), 0);
        brass.Append(Disc(0.36f, 0.05f, 16), Matrix4x4.Translate(new Vector3(1.7f, 0.6f, -0.25f)), 0);
        brass.Append(Torus(0.36f, 0.018f, 20, 5), Matrix4x4.Translate(new Vector3(1.7f, 0.625f, -0.25f)), 0);
        Obj("Brass Valve and Table", brass, root, Mat("#D4A443", 0.5f, 0f, false, 0.45f));

        // smoke
        var smoke = new FM(1);
        smoke.Append(Ico(2, 0.17f), Matrix4x4.Translate(new Vector3(1.12f, 2.25f, -0.1f)), 0);
        smoke.Append(Ico(2, 0.23f), Matrix4x4.Translate(new Vector3(1.2f, 2.72f, -0.08f)), 0);
        smoke.Append(Ico(2, 0.3f), Matrix4x4.Translate(new Vector3(1.32f, 3.32f, -0.05f)), 0);
        smoke = Warp(smoke, Lumpy(0.035f, 9f));
        Obj("Smoke", smoke, root, Mat("#EFEAE2", 0.05f, isNight ? 0.22f : 0f));

        // ---- the room inside
        Obj("Inner Walls", inner, interior, Mat("#EAD8B6"));
        var floor = new FM(2);
        floor.Append(Loft(new[] { new Ring(0f, 0.9f), new Ring(0.035f, 0.9f) }, 8, pi8), Matrix4x4.identity, 0);
        for (int k = -3; k <= 3; k++) floor.Append(Box(new Vector3(1.6f - Mathf.Abs(k) * 0.16f, 0.004f, 0.008f)), Matrix4x4.Translate(new Vector3(0f, 0.037f, k * 0.22f)), 1);
        Obj("Floor", floor, interior, Mat("#B08458"), Mat("#8C6442"));
        var rug = new FM(2);
        rug.Append(Loft(new[] { new Ring(0.035f, 0.46f, 0.34f), new Ring(0.041f, 0.46f, 0.34f) }, 24), Matrix4x4.Translate(new Vector3(0.02f, 0f, 0.12f)), 1);
        rug.Append(Loft(new[] { new Ring(0.041f, 0.4f, 0.28f), new Ring(0.046f, 0.4f, 0.28f) }, 24), Matrix4x4.Translate(new Vector3(0.02f, 0f, 0.12f)), 0);
        Obj("Rug", rug, interior, Mat("#B6463B"), Mat("#F1E1C2"));

        // bed against the back wall, with a blue quilt and a fat pillow
        var bedAt = new Vector3(0.15f, 0f, -0.42f);
        var bedWood = new FM(1);
        bedWood.Append(Box(new Vector3(0.64f, 0.14f, 0.38f)), Matrix4x4.Translate(bedAt + new Vector3(0f, 0.11f, 0f)), 0);
        bedWood.Append(Box(new Vector3(0.045f, 0.36f, 0.4f)), Matrix4x4.Translate(bedAt + new Vector3(0.33f, 0.22f, 0f)), 0);
        bedWood.Append(Box(new Vector3(0.04f, 0.22f, 0.4f)), Matrix4x4.Translate(bedAt + new Vector3(-0.33f, 0.15f, 0f)), 0);
        Obj("Bed Frame", bedWood, interior, Mat("#8A5A3B", 0.25f));
        var bedding = new FM(3);
        bedding.Append(Box(new Vector3(0.6f, 0.07f, 0.34f)), Matrix4x4.Translate(bedAt + new Vector3(0f, 0.215f, 0f)), 0);
        bedding.Append(Box(new Vector3(0.4f, 0.035f, 0.37f)), Matrix4x4.Translate(bedAt + new Vector3(-0.1f, 0.262f, 0f)), 1);
        bedding.Append(Box(new Vector3(0.05f, 0.04f, 0.372f)), Matrix4x4.Translate(bedAt + new Vector3(0.1f, 0.265f, 0f)), 2);
        bedding.Append(Warp(Box(new Vector3(0.13f, 0.06f, 0.25f)), Lumpy(0.012f, 18f)), Matrix4x4.Translate(bedAt + new Vector3(0.23f, 0.28f, 0f)), 0);
        Obj("Bedding", bedding, interior, Mat("#F6EEDF"), Mat("#4E73A8"), Mat("#E9C46A"));

        // desk with books, a candle and a little globe of B-612, and a stool
        var deskAt = new Vector3(-0.48f, 0f, -0.16f);
        var desk = new FM(1);
        desk.Append(Box(new Vector3(0.36f, 0.025f, 0.22f)), Matrix4x4.Translate(deskAt + new Vector3(0f, 0.34f, 0f)), 0);
        foreach (var c in new[] { new Vector2(-0.15f, -0.09f), new Vector2(0.15f, -0.09f), new Vector2(-0.15f, 0.09f), new Vector2(0.15f, 0.09f) })
            desk.Append(Box(new Vector3(0.025f, 0.33f, 0.025f)), Matrix4x4.Translate(deskAt + new Vector3(c.x, 0.165f, c.y)), 0);
        desk.Append(Disc(0.075f, 0.022f, 12), Matrix4x4.Translate(new Vector3(-0.42f, 0.2f, 0.12f)), 0);
        for (int k = 0; k < 3; k++)
        {
            float a = k * Mathf.PI * 2f / 3f;
            desk.Append(Box(new Vector3(0.018f, 0.19f, 0.018f)), Matrix4x4.Translate(new Vector3(-0.42f + Mathf.Cos(a) * 0.05f, 0.095f, 0.12f + Mathf.Sin(a) * 0.05f)), 0);
        }
        Obj("Desk and Stool", desk, interior, Mat("#9A6A45", 0.25f));
        var deskTop = new FM(4);
        deskTop.Append(Box(new Vector3(0.11f, 0.028f, 0.075f)), Matrix4x4.TRS(deskAt + new Vector3(-0.08f, 0.367f, 0.02f), Quaternion.Euler(0f, 12f, 0f), Vector3.one), 0);
        deskTop.Append(Box(new Vector3(0.1f, 0.025f, 0.07f)), Matrix4x4.TRS(deskAt + new Vector3(-0.08f, 0.393f, 0.02f), Quaternion.Euler(0f, -8f, 0f), Vector3.one), 1);
        deskTop.Append(Loft(new[] { new Ring(0f, 0.013f), new Ring(0.05f, 0.012f) }, 8), Matrix4x4.Translate(deskAt + new Vector3(0.12f, 0.353f, -0.05f)), 2);
        deskTop.Append(Loft(new[] { new Ring(0f, 0.012f), new Ring(0.05f, 0.012f) }, 6), Matrix4x4.Translate(deskAt + new Vector3(0.06f, 0.353f, 0.05f)), 3);
        deskTop.Append(Ico(1, 0.045f, u => u * 0.045f * (1f + Noise3(u * 3f) * 0.12f)), Matrix4x4.Translate(deskAt + new Vector3(0.06f, 0.445f, 0.05f)), 2);
        Obj("Desk Things", deskTop, interior, Mat("#B6463B"), Mat("#5E8C5A"), Mat("#F0E4CB"), Mat("#C9973E", 0.45f, 0f, false, 0.4f));
        var flame = new FM(1);
        flame.Append(Ico(1, 1f), Matrix4x4.TRS(deskAt + new Vector3(0.12f, 0.418f, -0.05f), Quaternion.identity, new Vector3(0.011f, 0.02f, 0.011f)), 0);
        Obj("Candle Flame", flame, interior, Mat("#FFB347", 0.5f, isNight ? 5f : 3f)).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

        // a small bookshelf by the right-hand wall
        var shelfM = Matrix4x4.TRS(new Vector3(0.62f, 0f, 0.22f), Quaternion.Euler(0f, -110f, 0f), Vector3.one);
        var shelf = new FM(1);
        shelf.Append(Box(new Vector3(0.3f, 0.02f, 0.09f)), shelfM * Matrix4x4.Translate(new Vector3(0f, 0.02f, 0f)), 0);
        shelf.Append(Box(new Vector3(0.3f, 0.02f, 0.09f)), shelfM * Matrix4x4.Translate(new Vector3(0f, 0.24f, 0f)), 0);
        shelf.Append(Box(new Vector3(0.3f, 0.02f, 0.09f)), shelfM * Matrix4x4.Translate(new Vector3(0f, 0.46f, 0f)), 0);
        shelf.Append(Box(new Vector3(0.02f, 0.46f, 0.09f)), shelfM * Matrix4x4.Translate(new Vector3(-0.15f, 0.23f, 0f)), 0);
        shelf.Append(Box(new Vector3(0.02f, 0.46f, 0.09f)), shelfM * Matrix4x4.Translate(new Vector3(0.15f, 0.23f, 0f)), 0);
        Obj("Bookshelf", shelf, interior, Mat("#8A5A3B", 0.25f));
        var books = new FM(4);
        for (int row = 0; row < 2; row++)
        {
            float x = -0.12f;
            while (x < 0.12f)
            {
                float w = Rand(0.022f, 0.04f), h = Rand(0.12f, 0.18f);
                books.Append(Box(new Vector3(w, h, 0.07f)), shelfM * Matrix4x4.TRS(new Vector3(x + w * 0.5f, 0.03f + row * 0.22f + h * 0.5f, 0f), Quaternion.Euler(0f, 0f, Rand(-4f, 4f)), Vector3.one), rng.Next(4));
                x += w + 0.004f;
            }
        }
        Obj("Books", books, interior, Mat("#B6463B"), Mat("#4E73A8"), Mat("#5E8C5A"), Mat("#E9C46A"));

        // "Drawing Number One" (the boa digesting an elephant) framed above the bed
        var picM = Matrix4x4.TRS(new Vector3(0.12f, 0.64f, -0.79f), Quaternion.Euler(-2.7f, 0f, 0f), Vector3.one);
        var pic = new FM(3);
        pic.Append(Box(new Vector3(0.27f, 0.19f, 0.015f)), picM, 0);
        pic.Append(Box(new Vector3(0.23f, 0.15f, 0.006f)), picM * Matrix4x4.Translate(new Vector3(0f, 0f, 0.009f)), 1);
        Vector2[] boa = { new Vector2(-0.1f, -0.045f), new Vector2(0.1f, -0.045f), new Vector2(0.085f, -0.03f), new Vector2(0.055f, -0.005f), new Vector2(0.03f, 0.03f), new Vector2(0.005f, 0.045f), new Vector2(-0.02f, 0.035f), new Vector2(-0.045f, 0.0f), new Vector2(-0.075f, -0.03f) };
        pic.Append(Extrude(boa, 0.004f), picM * Matrix4x4.Translate(new Vector3(0f, -0.01f, 0.013f)), 2);
        Obj("Picture", pic, interior, Mat("#7A4E33", 0.3f), Mat("#F4EBD7"), Mat("#6B4A35"));

        // hanging lamp and the warm light it gives
        var lamp = new FM(2);
        lamp.Append(Box(new Vector3(0.006f, 0.24f, 0.006f)), Matrix4x4.Translate(new Vector3(0f, 0.94f, 0f)), 0);
        lamp.Append(Loft(new[] { new Ring(0.74f, 0.1f), new Ring(0.82f, 0.035f) }, 12, 0f, false, true), Matrix4x4.identity, 1);
        Obj("Hanging Lamp", lamp, interior, Mat("#3B3431"), Mat("#E9C27A", 0.3f, 0f, true));
        var bulb = new FM(1);
        bulb.Append(Ico(1, 0.032f), Matrix4x4.Translate(new Vector3(0f, 0.76f, 0f)), 0);
        Obj("Lamp Bulb", bulb, interior, Mat("#FFE2A0", 0.5f, isNight ? 4f : 2.5f)).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        var roomLight = new GameObject("Room Light").AddComponent<Light>();
        roomLight.transform.SetParent(interior, false);
        roomLight.transform.localPosition = new Vector3(0f, 0.7f, 0f);
        roomLight.type = LightType.Point; roomLight.color = Hex("#FFC98A");
        roomLight.intensity = isNight ? 2.2f : 1.2f; roomLight.range = 2.4f * HouseScale; roomLight.shadows = LightShadows.None;
        roomLight.gameObject.AddComponent<UniversalAdditionalLightData>();

        // a trigger filling the room tells the house when the prince is inside
        var volume = new GameObject("Interior Volume");
        volume.transform.SetParent(root, false);
        var cap = volume.AddComponent<CapsuleCollider>();
        cap.isTrigger = true; cap.direction = 1; cap.radius = 0.78f; cap.height = 1.2f; cap.center = new Vector3(0f, 0.55f, 0f);
        var hi = volume.AddComponent<B612.HouseInterior>();
        hi.houseLayer = HouseLayer;
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (mr.transform.IsChildOf(interior) || mr.name == "Smoke") continue;
            var opaque = mr.sharedMaterials;
            var faded = new Material[opaque.Length];
            for (int k = 0; k < opaque.Length; k++) faded[k] = FadeVariant(opaque[k]);
            hi.shell.Add(new B612.HouseInterior.ShellPart { renderer = mr, opaque = opaque, faded = faded });
        }
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = HouseLayer;
        return root.gameObject;
    }

    static GameObject BuildRose(Transform parent)
    {
        var root = new GameObject("Rose").transform; root.SetParent(parent, false);
        var green = new FM(1);
        green.Append(Loft(new[] { new Ring(0f, 0.014f), new Ring(0.16f, 0.012f, -1f, 0.01f), new Ring(0.32f, 0.01f, -1f, -0.005f) }, 5), Matrix4x4.identity, 0);
        green.Append(Leaf(0.11f, 0.03f), Matrix4x4.TRS(new Vector3(0.008f, 0.11f, 0f), Quaternion.Euler(0f, 0f, -62f), Vector3.one), 0);
        green.Append(Leaf(0.09f, 0.026f), Matrix4x4.TRS(new Vector3(-0.004f, 0.18f, 0f), Quaternion.Euler(0f, 30f, 58f), Vector3.one), 0);
        green.Append(Loft(new[] { new Ring(0.29f, 0.012f), new Ring(0.33f, 0.04f), new Ring(0.345f, 0.02f) }, 5), Matrix4x4.identity, 0);
        Obj("Stem", green, root, Mat("#4E7A3A"));
        var red = new FM(2);
        red.Append(Ico(0, 0.045f), Matrix4x4.TRS(new Vector3(0f, 0.385f, 0f), Quaternion.identity, new Vector3(1f, 1.2f, 1f)), 1);
        for (int k = 0; k < 6; k++)
        {
            var d = Quaternion.Euler(0f, k * 60f, 0f) * new Vector3(0f, 0.85f, 0.55f);
            red.Append(Spike(0.042f, 0.085f, 4), Orient(new Vector3(0f, 0.345f, 0f) + Quaternion.Euler(0f, k * 60f, 0f) * new Vector3(0f, 0f, 0.012f), d, k * 30f, Vector3.one), k % 2);
        }
        Obj("Bloom", red, root, Mat("#C93038", 0.25f), Mat("#A62230", 0.25f));
        var soil = new FM(1); soil.Append(Ico(0, 0.05f), Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1.2f, 0.3f, 1.2f)), 0);
        Obj("Soil", soil, root, Mat("#CDBB95"));
        return root.gameObject;
    }

    static GameObject BuildVolcano(Transform parent, bool extinct)
    {
        var fm = Loft(new[] { new Ring(0f, 0.64f), new Ring(0.12f, 0.55f), new Ring(0.28f, 0.42f), new Ring(0.38f, 0.32f), new Ring(0.44f, 0.25f), new Ring(0.46f, 0.19f), new Ring(0.32f, 0.14f) }, 12, 0.3f, false, true, (k, i) => k >= 5 ? 2 : (i + k) % 2, 3, 5);
        fm = Warp(fm, VolcanoWarp);
        if (extinct) return Obj("Volcano", fm, parent, Mat("#CFC7B6"), Mat("#BFB6A4"), Mat("#8E8576"));
        return Obj("Volcano", fm, parent, Mat("#E2D0A8"), Mat("#D3C098"), Mat("#7E6A50"));
    }

    static readonly Func<Vector3, Vector3> VolcanoWarp = Lumpy(0.022f, 7f, 2f);

    // A baobab sprout: the weeds the little prince pulls up every morning.
    static GameObject BuildSprout(Transform parent)
    {
        var root = new GameObject("Baobab Sprout").transform; root.SetParent(parent, false);
        var g = new FM(2);
        g.Append(Loft(new[] { new Ring(0f, 0.03f), new Ring(0.12f, 0.022f, -1f, 0.01f), new Ring(0.22f, 0.016f, -1f, -0.004f) }, 5), Matrix4x4.identity, 0);
        for (int i = 0; i < 3; i++)
            g.Append(Leaf(0.13f, 0.05f), Matrix4x4.TRS(new Vector3(0f, 0.2f, 0f), Quaternion.Euler(0f, i * 120f + 15f, 0f) * Quaternion.Euler(0f, 0f, -58f), Vector3.one), 1);
        g.Append(Leaf(0.09f, 0.035f), Matrix4x4.TRS(new Vector3(0f, 0.21f, 0f), Quaternion.identity, Vector3.one), 1);
        Obj("Sprout", g, root, Mat("#7A6248"), Mat("#6F9A3E"));
        return root.gameObject;
    }

    // ------------------------------------------------------------------ the way home and the far side
    // Launch star on the clock planet's upper side, facing home. Standing in it and pressing jump flies the prince back.
    static GameObject BuildLaunchStar(Transform parent, Transform landing)
    {
        var home = Vector3.ProjectOnPlane(-ClockPlanetPos, Vector3.up).normalized;
        var d = (home * 0.55f + Vector3.up * 0.85f).normalized;
        var surface = ClockPlanetPos + d * ClockR;
        RaycastHit hit;
        if (Physics.Raycast(ClockPlanetPos + d * (ClockR + 3f), -d, out hit, 3f, ~0, QueryTriggerInteraction.Ignore)) surface = hit.point;

        var root = new GameObject("Launch Star").transform;
        root.SetParent(parent, false);
        root.position = surface + d * 0.3f;
        root.rotation = Quaternion.FromToRotation(Vector3.up, d);
        var vis = new GameObject("Visual").transform;
        vis.SetParent(root, false);
        var star = new FM(1);
        star.Append(StarPrism(0.36f, 0.16f, 0.08f), Matrix4x4.Rotate(Quaternion.Euler(90f, 0f, 0f)), 0);
        Obj("Star", star, vis, Mat("#FFB531", 0.5f, isNight ? 1.7f : 1.0f));
        var ring = new FM(1);
        ring.Append(Torus(0.5f, 0.028f, 28, 6), Matrix4x4.identity, 0);
        ring.Append(Torus(0.62f, 0.016f, 28, 5), Matrix4x4.Translate(Vector3.up * 0.04f), 0);
        Obj("Rings", ring, vis, Mat("#FFC94A", 0.6f, isNight ? 1.0f : 0.45f, false, 0.3f));

        var trigger = root.gameObject.AddComponent<SphereCollider>();
        trigger.isTrigger = true; trigger.radius = 0.6f;
        var ls = root.gameObject.AddComponent<B612.LaunchStar>();
        ls.landing = landing; ls.visual = vis;
        PointLight("Launch Star Light", root.position + d * 0.35f, parent, "#FFC060", isNight ? 1.8f : 0.8f, 2.4f, false);
        return root.gameObject;
    }

    // A big baobab growing out of the bottom of the asteroid, with hanging lanterns, glowing mushrooms and a star to find.
    static void BuildFarSide(Transform parent, out Vector3 starPos, out Vector3 starUp)
    {
        var dir = new Vector3(0.2f, -1f, -0.35f).normalized;
        var basePos = dir * AstRadius * 0.8f; var n = dir;
        RaycastHit hit;
        if (Physics.Raycast(dir * 20f, -dir, out hit, 20f, ~0, QueryTriggerInteraction.Ignore)) { basePos = hit.point; n = hit.normal; }
        var up = (dir * 0.7f + n * 0.3f).normalized;
        var grove = new GameObject("Far Side Grove").transform;
        grove.SetParent(parent, false);
        grove.position = basePos;
        grove.rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, 30f, 0f);

        // glowing mushrooms in three little clusters (placed before the tree gets a collider)
        Vector2[] clusters = { new Vector2(1.5f, 0.4f), new Vector2(-0.9f, 1.4f), new Vector2(-0.6f, -1.6f) };
        for (int c = 0; c < clusters.Length; c++)
        {
            for (int i = 0; i < 3; i++)
            {
                var o = clusters[c] + new Vector2(Rand(-0.35f, 0.35f), Rand(-0.35f, 0.35f));
                var probe = grove.TransformPoint(new Vector3(o.x, 1.5f, o.y));
                if (!Physics.Raycast(probe, -up, out hit, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                var m = BuildMushroom(grove, c == 1 ? "#FFA04A" : "#4FD2EA", isNight ? 1.3f : 0.6f);
                m.transform.position = hit.point - hit.normal * 0.01f;
                m.transform.rotation = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(up, hit.normal, 0.5f)) * Quaternion.Euler(0f, Rand(0f, 360f), 0f);
                m.transform.localScale = Vector3.one * Rand(0.8f, 1.6f);
            }
        }

        var tips = new List<Vector3>();
        var tree = BuildBaobab(grove, tips);
        tree.transform.localPosition = -Vector3.up * 0.12f;

        // lanterns hang (toward the planet) from three branch tips
        for (int i = 0; i < tips.Count; i += 2)
        {
            var tip = tips[i] - Vector3.up * 0.12f;
            var string1 = new FM(1);
            string1.Append(Box(new Vector3(0.008f, 0.32f, 0.008f)), Matrix4x4.Translate(tip - Vector3.up * 0.16f), 0);
            string1.Append(Disc(0.05f, 0.025f, 6), Matrix4x4.Translate(tip - Vector3.up * 0.33f), 0);
            Obj("Lantern String", string1, tree.transform, Mat("#3B3431"));
            var body = new FM(1);
            body.Append(Ico(1, 1f), Matrix4x4.TRS(tip - Vector3.up * 0.45f, Quaternion.identity, new Vector3(0.09f, 0.12f, 0.09f)), 0);
            Obj("Lantern", body, tree.transform, Mat(i % 4 == 0 ? "#FFA235" : "#FF7A45", 0.5f, isNight ? 1.9f : 1.1f)).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // warm light from the lanterns on two sides of the tree, and a cool glow over the teal mushrooms
        PointLight("Grove Lantern Light", grove.TransformPoint(new Vector3(0.95f, 1.3f, 0.55f)), grove, "#FFB565", isNight ? 1.7f : 1.0f, 3.4f, false);
        PointLight("Grove Lantern Light 2", grove.TransformPoint(new Vector3(-0.85f, 1.3f, -0.65f)), grove, "#FFA05A", isNight ? 1.5f : 0.9f, 3.4f, false);
        PointLight("Grove Mushroom Light", grove.TransformPoint(new Vector3(1.6f, 0.35f, 0.4f)), grove, "#5FD8F0", isNight ? 1.2f : 0.5f, 2.2f, false);

        tree.transform.Find("Baobab Trunk").gameObject.AddComponent<MeshCollider>();
        starPos = grove.TransformPoint(new Vector3(-1.0f, 1.45f, 0.2f));
        starUp = up;
    }

    static GameObject BuildBaobab(Transform parent, List<Vector3> tips)
    {
        var root = new GameObject("Baobab").transform;
        root.SetParent(parent, false);
        var bark = new FM(2);
        // bottle-shaped trunk
        bark.Append(Loft(new[] { new Ring(-0.1f, 0.6f), new Ring(0.15f, 0.66f), new Ring(0.7f, 0.74f), new Ring(1.3f, 0.64f), new Ring(1.75f, 0.42f), new Ring(2.0f, 0.33f), new Ring(2.1f, 0.28f) },
                         16, 0f, true, true, (k, i) => (i + k + 2) % 2, 2), Matrix4x4.identity);
        // roots gripping the planet
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f + Rand(-12f, 12f);
            var rd = Quaternion.Euler(0f, a, 0f) * new Vector3(1f, -0.3f, 0f);
            bark.Append(Spike(0.17f, Rand(0.6f, 0.85f), 5), Orient(Quaternion.Euler(0f, a, 0f) * new Vector3(0.5f, 0.08f, 0f), rd, 0f, new Vector3(1f, 1f, 0.6f)), i % 2);
        }
        // stubby branches fanning out from the top
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f + Rand(-14f, 14f);
            var bd = Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(0f, 0f, -Rand(38f, 58f)) * Vector3.up;
            var start = new Vector3(0f, 1.95f, 0f) + Quaternion.Euler(0f, a, 0f) * new Vector3(0.16f, 0f, 0f);
            float len = Rand(0.8f, 1.15f);
            bark.Append(Loft(new[] { new Ring(0f, 0.13f), new Ring(len * 0.6f, 0.09f), new Ring(len, 0.06f) }, 6), Orient(start, bd, 0f, Vector3.one), i % 2);
            tips.Add(start + bd * len);
        }
        bark = Warp(bark, Lumpy(0.05f, 2.6f, 3f));
        Obj("Baobab Trunk", bark, root, Mat("#A08670"), Mat("#8E745F"));

        // flat leaf clumps on every tip, and a crown
        var leaves = new FM(2);
        foreach (var t in tips)
            leaves.Append(Ico(2, 1f), Matrix4x4.TRS(t + Vector3.up * 0.1f, Quaternion.Euler(0f, Rand(0f, 360f), 0f), new Vector3(0.55f, 0.3f, 0.55f) * Rand(0.9f, 1.2f)), rng.Next(2));
        leaves.Append(Ico(2, 1f), Matrix4x4.TRS(new Vector3(0f, 2.3f, 0f), Quaternion.identity, new Vector3(0.62f, 0.32f, 0.62f)), 0);
        leaves = Warp(leaves, Lumpy(0.07f, 3.2f, 8f));
        Obj("Baobab Leaves", leaves, root, Mat("#4E7D3A"), Mat("#5F9345"));
        return root.gameObject;
    }

    static GameObject BuildMushroom(Transform parent, string capHex, float glow)
    {
        var root = new GameObject("Glow Mushroom").transform;
        root.SetParent(parent, false);
        var stem = new FM(1);
        stem.Append(Loft(new[] { new Ring(0f, 0.035f), new Ring(0.12f, 0.027f), new Ring(0.16f, 0.03f) }, 6), Matrix4x4.identity, 0);
        Obj("Stem", stem, root, Mat("#EFE6D2"));
        var cap = new FM(1);
        cap.Append(Loft(new[] { new Ring(0.14f, 0.11f), new Ring(0.19f, 0.09f), new Ring(0.23f, 0.05f), new Ring(0.245f, 0f) }, 8), Matrix4x4.identity, 0);
        Obj("Cap", cap, root, Mat(capHex, 0.4f, glow)).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        return root.gameObject;
    }

    static GameObject BuildClockPlanet(Transform parent, Vector3 camPos)
    {
        var root = new GameObject("Clock Planet").transform; root.SetParent(parent, false);
        root.position = ClockPlanetPos;
        float R = ClockR;
        var body = Ico(3, 1f, u => u * R * (1f + Noise3(u * 2.2f) * 0.12f + Noise3(u * 6f + Vector3.one * 9f) * 0.03f), (c, n) => rng.Next(3), 3);
        var pits = new FM(2);
        for (int i = 0; i < 11; i++)
        {
            var d = UnityEngine.Random.onUnitSphere;
            d = new Vector3(Rand(-1f, 1f), Rand(-1f, 1f), Rand(-1f, 1f)).normalized;
            var p = d * R * 0.98f;
            if (i < 7) pits.Append(Ico(0, Rand(0.14f, 0.24f)), Orient(p, d, Rand(0, 90), new Vector3(1f, 0.28f, 1f)), 0);
            else body.Append(Rock(Rand(0.08f, 0.14f), 0), Matrix4x4.TRS(d * R, Quaternion.Euler(Rand(0, 360), Rand(0, 360), 0), Vector3.one), 1);
        }
        var bo = Obj("Planet", body, root, Mat("#D6CFC2", 0.08f), Mat("#CAC2B4", 0.08f), Mat("#BDB5A7", 0.08f));
        bo.transform.localPosition = Vector3.zero;
        var pi = Obj("Craters", pits, root, Mat("#A39B8E", 0.08f), Mat("#968E81"));
        pi.transform.localPosition = Vector3.zero;

        // gear clock facing the camera, slightly to the right
        var toCam = (camPos - ClockPlanetPos).normalized;
        var gearDir = (toCam + new Vector3(0.35f, 0.05f, 0f)).normalized;
        var gearRoot = new GameObject("Gear Clock").transform; gearRoot.SetParent(root, false);
        gearRoot.localPosition = gearDir * R * 0.93f;
        gearRoot.rotation = Quaternion.LookRotation(gearDir, Vector3.up);
        var gear = new FM(1);
        gear.Append(Disc(0.52f, 0.11f, 20), Matrix4x4.Rotate(Quaternion.Euler(90f, 0f, 0f)), 0);
        for (int k = 0; k < 10; k++)
        {
            float a = k * 36f;
            gear.Append(Box(new Vector3(0.14f, 0.14f, 0.1f)), Matrix4x4.TRS(Quaternion.Euler(0, 0, a) * new Vector3(0f, 0.56f, 0f), Quaternion.Euler(0, 0, a), Vector3.one), 0);
        }
        Obj("Gear", gear, gearRoot, Mat("#D9A63A", 0.5f, 0f, false, 0.45f));
        var face = new FM(2);
        face.Append(Disc(0.38f, 0.04f, 20), Matrix4x4.TRS(new Vector3(0, 0, 0.065f), Quaternion.Euler(90f, 0f, 0f), Vector3.one), 0);
        for (int k = 0; k < 12; k++)
            face.Append(Box(new Vector3(0.02f, k % 3 == 0 ? 0.07f : 0.04f, 0.01f)), Matrix4x4.TRS(new Vector3(0, 0, 0.09f), Quaternion.Euler(0, 0, k * 30f), Vector3.one) * Matrix4x4.Translate(new Vector3(0, 0.31f, 0)), 1);
        face.Append(Box(new Vector3(0.03f, 0.22f, 0.012f)), Matrix4x4.TRS(new Vector3(0, 0, 0.095f), Quaternion.Euler(0, 0, 0f), Vector3.one) * Matrix4x4.Translate(new Vector3(0, 0.1f, 0)), 1);
        face.Append(Box(new Vector3(0.03f, 0.16f, 0.012f)), Matrix4x4.TRS(new Vector3(0, 0, 0.1f), Quaternion.Euler(0, 0, -90f), Vector3.one) * Matrix4x4.Translate(new Vector3(0, 0.07f, 0)), 1);
        face.Append(Disc(0.035f, 0.03f, 8), Matrix4x4.TRS(new Vector3(0, 0, 0.1f), Quaternion.Euler(90f, 0f, 0f), Vector3.one), 1);
        Obj("Clock Face", face, gearRoot, Mat("#F6EEDC"), Mat("#2E2622"));

        // little lighthouse cottage on top
        var top = new GameObject("Lookout").transform; top.SetParent(root, false);
        top.localPosition = new Vector3(0f, R * 0.97f, 0f);
        var cream = new FM(1);
        cream.Append(Loft(new[] { new Ring(-0.05f, 0.21f), new Ring(0.32f, 0.19f) }, 8, Mathf.PI / 8f), Matrix4x4.identity, 0);
        cream.Append(Loft(new[] { new Ring(0.34f, 0.13f), new Ring(0.5f, 0.13f) }, 8, Mathf.PI / 8f), Matrix4x4.identity, 0);
        Obj("Tower", cream, top, Mat("#F2E8D6"));
        var glow = new FM(1); glow.Append(Loft(new[] { new Ring(0.38f, 0.135f), new Ring(0.46f, 0.135f) }, 8, 0f), Matrix4x4.identity, 0);
        Obj("Lamp", glow, top, Mat("#FFC45A", 0.6f, isNight ? 4.5f : 2.0f));
        var copper = new FM(1);
        copper.Append(Loft(new[] { new Ring(0.3f, 0.24f), new Ring(0.35f, 0.24f) }, 12), Matrix4x4.identity, 0);
        copper.Append(Torus(0.24f, 0.012f, 16, 4), Matrix4x4.Translate(new Vector3(0, 0.44f, 0)), 0);
        for (int k = 0; k < 8; k++) copper.Append(Box(new Vector3(0.012f, 0.1f, 0.012f)), Matrix4x4.Translate(Quaternion.Euler(0, k * 45f, 0) * new Vector3(0.24f, 0.39f, 0f)), 0);
        copper.Append(Loft(new[] { new Ring(0.68f, 0.03f), new Ring(0.78f, 0f) }, 6), Matrix4x4.identity, 0);
        copper.Append(Loft(new[] { new Ring(0.3f, 0.012f), new Ring(0.92f, 0.012f) }, 5), Matrix4x4.Translate(new Vector3(0.2f, 0f, -0.05f)), 0);
        Obj("Lookout Copper", copper, top, Mat("#B86A3B", 0.35f, 0f, false, 0.3f));
        var roofRed = new FM(1); roofRed.Append(Loft(new[] { new Ring(0.5f, 0.18f), new Ring(0.7f, 0f) }, 8, Mathf.PI / 8f), Matrix4x4.identity, 0);
        Obj("Lookout Roof", roofRed, top, Mat("#C8553A", 0.2f));
        var flag = new FM(1);
        TwoSidedTri(flag, new Vector3(0.2f, 0.92f, -0.05f), new Vector3(0.2f, 0.78f, -0.05f), new Vector3(0.42f, 0.86f, -0.08f));
        Obj("Flag", flag, top, Mat("#F2C14E", 0.2f));
        return root.gameObject;
    }

    // ------------------------------------------------------------------ characters
    static GameObject BuildPrince(Transform parent)
    {
        var root = new GameObject("Prince").transform; root.SetParent(parent, false);
        float pi8 = Mathf.PI / 8f;

        var green = new FM(2);
        foreach (float sx in new[] { -1f, 1f })
            green.Append(Loft(new[] { new Ring(0.055f, 0.088f, 0.08f), new Ring(0.3f, 0.076f, 0.07f), new Ring(0.43f, 0.07f, 0.065f) }, 8, 0.2f), Matrix4x4.Translate(new Vector3(sx * 0.068f, 0f, 0f)), 1);
        green.Append(Loft(new[] { new Ring(0.27f, 0.178f, 0.152f), new Ring(0.42f, 0.14f, 0.116f), new Ring(0.5f, 0.128f, 0.103f), new Ring(0.62f, 0.135f, 0.106f), new Ring(0.695f, 0.11f, 0.09f), new Ring(0.735f, 0.05f, 0.045f) }, 8, pi8), Matrix4x4.identity, 0);
        var handPos = new List<Vector3>();
        foreach (float sx in new[] { -1f, 1f })
        {
            var m = Matrix4x4.TRS(new Vector3(sx * 0.125f, 0.685f, 0f), Quaternion.Euler(4f, 0f, sx * 15f), Vector3.one);
            green.Append(Loft(new[] { new Ring(0f, 0.046f), new Ring(-0.17f, 0.058f), new Ring(-0.29f, 0.09f), new Ring(-0.3f, 0.078f) }, 7, 0.4f), m, 1);
            handPos.Add(m.MultiplyPoint3x4(new Vector3(0f, -0.32f, 0f)));
        }
        Obj("Coat", green, root, Mat("#6F8F3A", 0.12f), Mat("#64833A", 0.12f));

        var skin = new FM(1);
        skin.Append(Loft(new[] { new Ring(0.72f, 0.036f), new Ring(0.79f, 0.034f) }, 6), Matrix4x4.identity, 0);
        skin.Append(Ico(1, 0.132f), Matrix4x4.TRS(new Vector3(0f, 0.865f, 0f), Quaternion.identity, new Vector3(1f, 1.05f, 1f)), 0);
        foreach (float sx in new[] { -1f, 1f }) skin.Append(Ico(0, 0.03f), Matrix4x4.Translate(new Vector3(sx * 0.128f, 0.856f, -0.006f)), 0);
        foreach (var h in handPos) skin.Append(Ico(0, 0.034f), Matrix4x4.Translate(h), 0);
        Obj("Skin", skin, root, Mat("#F4D2B0", 0.2f));

        var face = new FM(3);
        foreach (float sx in new[] { -1f, 1f })
        {
            face.Append(Ico(0, 0.021f), Matrix4x4.TRS(new Vector3(sx * 0.047f, 0.866f, 0.12f), Quaternion.identity, new Vector3(0.75f, 1.25f, 0.45f)), 0);
            face.Append(Ico(0, 0.026f), Matrix4x4.TRS(new Vector3(sx * 0.078f, 0.832f, 0.106f), Quaternion.identity, new Vector3(1.1f, 0.55f, 0.35f)), 1);
            face.Append(Box(new Vector3(0.036f, 0.007f, 0.007f)), Matrix4x4.TRS(new Vector3(sx * 0.05f, 0.903f, 0.118f), Quaternion.Euler(0f, 0f, -sx * 12f), Vector3.one), 2);
        }
        face.Append(Box(new Vector3(0.024f, 0.006f, 0.007f)), Matrix4x4.Translate(new Vector3(0f, 0.81f, 0.124f)), 0);
        Obj("Face", face, root, Mat("#2B1F1A", 0.4f), Mat("#EF9C8A", 0.2f), Mat("#B8561E"));

        var hair = new FM(3);
        var hc = new Vector3(0f, 0.9f, -0.016f);
        hair.Append(Ico(1, 0.14f), Matrix4x4.TRS(hc, Quaternion.identity, new Vector3(1.05f, 0.9f, 1.07f)), 0);
        int N = 30;
        for (int i = 0; i < N; i++)
        {
            float y = 1f - (i + 0.5f) / N * 2f;
            float rr = Mathf.Sqrt(1f - y * y);
            float phi = i * 2.39996f;
            var d = new Vector3(Mathf.Cos(phi) * rr, y, Mathf.Sin(phi) * rr);
            if (d.y < -0.25f) continue;
            if (d.z > 0.4f && d.y < 0.6f) continue;
            var basePos = hc + Vector3.Scale(d, new Vector3(0.13f, 0.112f, 0.136f));
            var axis = (d + new Vector3(0f, 0.45f, -0.25f) + RandV() * 0.22f).normalized;
            hair.Append(Spike(Rand(0.05f, 0.065f), Rand(0.075f, 0.12f), 4), Orient(basePos, axis, Rand(0f, 90f), Vector3.one), rng.Next(3));
        }
        for (int k = 0; k < 6; k++)
        {
            float t = -1f + k * 0.4f;
            var pos = new Vector3(t * 0.08f, 0.965f - Mathf.Abs(t) * 0.02f, 0.098f - Mathf.Abs(t) * 0.03f);
            hair.Append(Spike(0.036f, 0.055f, 4), Orient(pos, new Vector3(t * 0.5f, -0.3f, 0.85f), Rand(0f, 90f), Vector3.one), k % 3);
        }
        foreach (float sx in new[] { -1f, 1f })
            hair.Append(Spike(0.032f, 0.07f, 4), Orient(new Vector3(sx * 0.12f, 0.9f, 0.03f), new Vector3(sx * 0.45f, -1f, 0.1f), 20f, Vector3.one), 1);
        Obj("Hair", hair, root, Mat("#E8802A", 0.15f), Mat("#F29A3C", 0.15f), Mat("#D46A20", 0.15f));

        var boots = new FM(1);
        foreach (float sx in new[] { -1f, 1f })
            boots.Append(Loft(new[] { new Ring(0f, 0.047f, 0.075f, 0f, 0.022f), new Ring(0.07f, 0.043f, 0.06f, 0f, 0.012f) }, 6, 0.3f), Matrix4x4.Translate(new Vector3(sx * 0.068f, 0f, 0f)), 0);
        Obj("Boots", boots, root, Mat("#6B4128", 0.2f));

        var belt = new FM(1);
        belt.Append(Loft(new[] { new Ring(0.465f, 0.135f, 0.109f), new Ring(0.503f, 0.133f, 0.107f) }, 8, pi8), Matrix4x4.identity, 0);
        Obj("Belt", belt, root, Mat("#E0662B", 0.2f));

        var gold = new FM(1);
        gold.Append(Box(new Vector3(0.042f, 0.034f, 0.012f)), Matrix4x4.Translate(new Vector3(0f, 0.484f, 0.105f)), 0);
        foreach (float y in new[] { 0.665f, 0.605f, 0.545f, 0.43f }) gold.Append(Ico(0, 0.012f), Matrix4x4.Translate(new Vector3(0f, y, y > 0.5f ? 0.1f : 0.112f)), 0);
        Obj("Buttons", gold, root, Mat("#D9A63A", 0.55f, 0f, false, 0.5f));

        // scarf: collar plus two zig-zag tails blown to one side
        var scarf = new FM(2);
        scarf.Append(Torus(0.064f, 0.03f, 9, 5), Matrix4x4.TRS(new Vector3(0f, 0.738f, 0f), Quaternion.identity, new Vector3(1.15f, 1f, 1.08f)), 0);
        scarf.Append(Ico(0, 0.032f), Matrix4x4.Translate(new Vector3(0.035f, 0.72f, -0.06f)), 1);
        Ribbon(scarf,
            new[] { new Vector3(0.04f, 0.725f, -0.05f), new Vector3(0.14f, 0.715f, -0.09f), new Vector3(0.24f, 0.69f, -0.11f), new Vector3(0.34f, 0.675f, -0.1f), new Vector3(0.44f, 0.655f, -0.12f), new Vector3(0.53f, 0.645f, -0.11f), new Vector3(0.61f, 0.64f, -0.12f) },
            new[] { 0.05f, 0.058f, 0.064f, 0.068f, 0.07f, 0.07f, 0.07f },
            new[] { 0f, 22f, -16f, 26f, -20f, 15f, 0f });
        Ribbon(scarf,
            new[] { new Vector3(0.04f, 0.705f, -0.055f), new Vector3(0.12f, 0.68f, -0.1f), new Vector3(0.21f, 0.64f, -0.12f), new Vector3(0.3f, 0.615f, -0.11f), new Vector3(0.38f, 0.6f, -0.13f) },
            new[] { 0.045f, 0.05f, 0.055f, 0.055f, 0.055f },
            new[] { 10f, -18f, 20f, -12f, 0f });
        Obj("Scarf", scarf, root, Mat("#E8B93E", 0.15f), Mat("#D5A332", 0.15f));
        return root.gameObject;
    }

    static GameObject BuildFox(Transform parent)
    {
        var root = new GameObject("Fox").transform; root.SetParent(parent, false);
        var orange = new FM(2);
        orange.Append(Ico(1, 0.14f), Matrix4x4.TRS(new Vector3(0f, 0.12f, -0.04f), Quaternion.identity, new Vector3(1f, 0.85f, 1.1f)), 0);
        orange.Append(Ico(1, 0.1f), Matrix4x4.TRS(new Vector3(0f, 0.27f, 0.05f), Quaternion.Euler(-12f, 0f, 0f), new Vector3(0.85f, 1.35f, 0.85f)), 0);
        orange.Append(Ico(1, 0.085f), Matrix4x4.TRS(new Vector3(0f, 0.44f, 0.08f), Quaternion.identity, new Vector3(1f, 0.92f, 1.05f)), 0);
        var snoutBase = new Vector3(0f, 0.425f, 0.13f);
        var snoutAxis = new Vector3(0f, -0.15f, 1f).normalized;
        orange.Append(Spike(0.045f, 0.12f, 4), Orient(snoutBase, snoutAxis, 45f, Vector3.one), 0);
        foreach (float sx in new[] { -1f, 1f })
        {
            orange.Append(Spike(0.042f, 0.14f, 3), Orient(new Vector3(sx * 0.05f, 0.5f, 0.06f), new Vector3(sx * 0.35f, 1f, -0.05f), sx * 20f, Vector3.one), 1);
            orange.Append(Loft(new[] { new Ring(0f, 0.022f), new Ring(0.24f, 0.022f) }, 5), Matrix4x4.Translate(new Vector3(sx * 0.045f, 0f, 0.09f)), 1);
            orange.Append(Ico(0, 0.05f), Matrix4x4.TRS(new Vector3(sx * 0.08f, 0.025f, 0.02f), Quaternion.identity, new Vector3(0.9f, 0.5f, 1.3f)), 1);
        }
        var tailM = Matrix4x4.TRS(new Vector3(0.06f, 0.06f, -0.12f), Quaternion.Euler(-70f, -50f, 0f), Vector3.one);
        orange.Append(Loft(new[] { new Ring(0f, 0.035f), new Ring(0.1f, 0.075f), new Ring(0.22f, 0.085f), new Ring(0.32f, 0.07f), new Ring(0.38f, 0.052f) }, 6, 0.2f, true, false), tailM, 0);
        Obj("Fur", orange, root, Mat("#E8782E", 0.15f), Mat("#D2632A", 0.15f));

        var cream = new FM(1);
        cream.Append(Ico(1, 0.07f), Matrix4x4.TRS(new Vector3(0f, 0.27f, 0.105f), Quaternion.identity, new Vector3(1f, 1.45f, 0.55f)), 0);
        foreach (float sx in new[] { -1f, 1f }) cream.Append(Ico(0, 0.045f), Matrix4x4.TRS(new Vector3(sx * 0.055f, 0.41f, 0.12f), Quaternion.identity, new Vector3(1f, 0.7f, 0.8f)), 0);
        cream.Append(Loft(new[] { new Ring(0.38f, 0.052f), new Ring(0.47f, 0f) }, 6, 0.2f, false, false), tailM, 0);
        Obj("Cream Fur", cream, root, Mat("#F3E2C4", 0.15f));

        var dark = new FM(1);
        dark.Append(Ico(0, 0.016f), Matrix4x4.Translate(snoutBase + snoutAxis * 0.115f), 0);
        foreach (float sx in new[] { -1f, 1f }) dark.Append(Ico(0, 0.013f), Matrix4x4.TRS(new Vector3(sx * 0.036f, 0.465f, 0.152f), Quaternion.identity, new Vector3(0.8f, 1.2f, 0.6f)), 0);
        Obj("Eyes and Nose", dark, root, Mat("#2B1F1A", 0.4f));
        return root.gameObject;
    }

    // ------------------------------------------------------------------ small shapes
    static FM Rock(float r, int sub)
    {
        float seed = Rand(0f, 50f);
        return Ico(1, 1f, u => u * r * (1f + Noise3(u * 3f + Vector3.one * seed) * 0.5f + Noise3(u * 7f + Vector3.one * seed) * 0.12f));
    }

    static FM Slab(float r, float h)
    {
        int n = 6; var pts = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n + Rand(-0.2f, 0.2f); float rr = r * Rand(0.8f, 1.1f); pts[i] = new Vector2(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr); }
        var fm = new FM(1);
        fm.Append(Extrude(pts, h), Matrix4x4.Rotate(Quaternion.Euler(90f, 0f, 0f)), 0);
        return fm;
    }

    static FM Leaf(float len, float w)
    {
        return Loft(new[] { new Ring(0f, 0f), new Ring(len * 0.45f, w, w * 0.25f), new Ring(len, 0f) }, 4);
    }

    static FM StarPrism(float outer, float inner, float depth)
    {
        var p = new Vector2[10];
        for (int i = 0; i < 10; i++) { float a = Mathf.PI / 2f + i * Mathf.PI / 5f; float r = i % 2 == 0 ? outer : inner; p[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r); }
        return Extrude(p, depth);
    }

    static FM Disc(float r, float thick, int sides) { return Loft(new[] { new Ring(-thick / 2f, r), new Ring(thick / 2f, r) }, sides); }
    static FM Spike(float r, float len, int sides) { return Loft(new[] { new Ring(0f, r), new Ring(len, 0f) }, sides, 0f, true, false); }
    static FM Box(Vector3 s) { return Loft(new[] { new Ring(-s.y / 2f, s.x * 0.7071f, s.z * 0.7071f), new Ring(s.y / 2f, s.x * 0.7071f, s.z * 0.7071f) }, 4, Mathf.PI / 4f); }

    static void TwoSidedTri(FM fm, Vector3 a, Vector3 b, Vector3 c) { fm.Tri(a, b, c, 0); fm.Tri(a, c, b, 0); }

    static void Ribbon(FM fm, Vector3[] pts, float[] w, float[] twist)
    {
        int n = pts.Length; var L = new Vector3[n]; var R = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var t = (i < n - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).normalized;
            var side = Vector3.ProjectOnPlane(Vector3.up, t).normalized;
            side = Quaternion.AngleAxis(twist[i], t) * side;
            L[i] = pts[i] + side * w[i]; R[i] = pts[i] - side * w[i];
        }
        for (int i = 0; i < n - 1; i++)
        {
            int sa = i % 2, sb = 1 - sa;
            fm.Tri(L[i], L[i + 1], R[i + 1], sa); fm.Tri(L[i], R[i + 1], L[i + 1], sa);
            fm.Tri(L[i], R[i + 1], R[i], sb); fm.Tri(L[i], R[i], R[i + 1], sb);
        }
        var tl = (pts[n - 1] - pts[n - 2]).normalized;
        var tipL = L[n - 1] + tl * 0.09f; var tipR = R[n - 1] + tl * 0.09f; var mid = pts[n - 1] + tl * 0.02f;
        fm.Tri(L[n - 1], tipL, mid, 0); fm.Tri(L[n - 1], mid, tipL, 0);
        fm.Tri(R[n - 1], mid, tipR, 1); fm.Tri(R[n - 1], tipR, mid, 1);
        fm.Tri(L[n - 1], mid, R[n - 1], 0); fm.Tri(L[n - 1], R[n - 1], mid, 0);
    }

    static void Pipe(FM pipe, FM flange, Vector3[] pts, float r)
    {
        for (int i = 0; i < pts.Length - 1; i++)
        {
            var a = pts[i]; var dir = pts[i + 1] - a; float len = dir.magnitude; var u = dir / len;
            var rot = Quaternion.FromToRotation(Vector3.up, u);
            pipe.Append(Loft(new[] { new Ring(0f, r), new Ring(len, r) }, 8, 0f, i == 0, i == pts.Length - 2), Matrix4x4.TRS(a, rot, Vector3.one), 0);
            flange.Append(Loft(new[] { new Ring(0f, r * 1.32f), new Ring(0.045f, r * 1.32f) }, 8), Matrix4x4.TRS(a + u * 0.02f, rot, Vector3.one), 0);
            flange.Append(Loft(new[] { new Ring(-0.045f, r * 1.32f), new Ring(0f, r * 1.32f) }, 8), Matrix4x4.TRS(pts[i + 1] - u * 0.02f, rot, Vector3.one), 0);
            if (i > 0) pipe.Append(Ico(1, r * 1.12f), Matrix4x4.Translate(a), 0);
        }
    }

    static Vector2[] Scale(Vector2[] p, float sx, float sy)
    {
        var o = new Vector2[p.Length];
        for (int i = 0; i < p.Length; i++) o[i] = new Vector2(p[i].x * sx, (p[i].y - 0.01f) * sy);
        return o;
    }

    // ------------------------------------------------------------------ generators
    struct Ring
    {
        public float y, rx, rz, ox, oz;
        public Ring(float y, float rx, float rz = -1f, float ox = 0f, float oz = 0f) { this.y = y; this.rx = rx; this.rz = rz < 0f ? rx : rz; this.ox = ox; this.oz = oz; }
    }

    static FM Loft(Ring[] R, int sides, float rot = 0f, bool capB = true, bool capT = true, Func<int, int, int> subOf = null, int subCount = 1, int inwardFrom = int.MaxValue)
    {
        var fm = new FM(subCount);
        Func<Ring, int, Vector3> P = (r, i) => { float a = rot + i * Mathf.PI * 2f / sides; return new Vector3(r.ox + Mathf.Cos(a) * r.rx, r.y, r.oz + Mathf.Sin(a) * r.rz); };
        for (int k = 0; k < R.Length - 1; k++)
            for (int i = 0; i < sides; i++)
            {
                Vector3 a = P(R[k], i), b = P(R[k], i + 1), c = P(R[k + 1], i + 1), d = P(R[k + 1], i);
                var cen = (a + b + c + d) * 0.25f;
                var hint = new Vector3(cen.x - (R[k].ox + R[k + 1].ox) * 0.5f, 0f, cen.z - (R[k].oz + R[k + 1].oz) * 0.5f);
                if (k >= inwardFrom) hint = -hint;
                fm.QuadO(a, b, c, d, hint, subOf != null ? subOf(k, i) : 0);
            }
        if (capB && R[0].rx > 0f)
        {
            var c0 = new Vector3(R[0].ox, R[0].y, R[0].oz);
            for (int i = 0; i < sides; i++) fm.TriO(c0, P(R[0], i), P(R[0], i + 1), Vector3.down, subOf != null ? subOf(-1, i) : 0);
        }
        int last = R.Length - 1;
        if (capT && R[last].rx > 0f)
        {
            var c1 = new Vector3(R[last].ox, R[last].y, R[last].oz);
            for (int i = 0; i < sides; i++) fm.TriO(c1, P(R[last], i), P(R[last], i + 1), Vector3.up, subOf != null ? subOf(last, i) : 0);
        }
        return fm;
    }

    static FM Extrude(Vector2[] poly, float depth)
    {
        var fm = new FM(1); float hz = depth / 2f; var c = Vector2.zero;
        foreach (var p in poly) c += p; c /= poly.Length;
        var cf = new Vector3(c.x, c.y, hz); var cb = new Vector3(c.x, c.y, -hz);
        for (int i = 0; i < poly.Length; i++)
        {
            var p0 = poly[i]; var p1 = poly[(i + 1) % poly.Length];
            fm.TriO(cf, new Vector3(p0.x, p0.y, hz), new Vector3(p1.x, p1.y, hz), Vector3.forward, 0);
            fm.TriO(cb, new Vector3(p0.x, p0.y, -hz), new Vector3(p1.x, p1.y, -hz), Vector3.back, 0);
            var mid = (p0 + p1) * 0.5f - c;
            fm.QuadO(new Vector3(p0.x, p0.y, hz), new Vector3(p1.x, p1.y, hz), new Vector3(p1.x, p1.y, -hz), new Vector3(p0.x, p0.y, -hz), new Vector3(mid.x, mid.y, 0f), 0);
        }
        return fm;
    }

    static FM Torus(float R, float r, int su, int sv)
    {
        var fm = new FM(1);
        Func<int, int, Vector3> P = (i, j) => { float u = i * Mathf.PI * 2f / su, v = j * Mathf.PI * 2f / sv; var d = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u)); return d * (R + r * Mathf.Cos(v)) + Vector3.up * (r * Mathf.Sin(v)); };
        for (int i = 0; i < su; i++)
            for (int j = 0; j < sv; j++)
            {
                Vector3 a = P(i, j), b = P(i + 1, j), c = P(i + 1, j + 1), d = P(i, j + 1);
                float um = (i + 0.5f) * Mathf.PI * 2f / su;
                var tc = new Vector3(Mathf.Cos(um), 0f, Mathf.Sin(um)) * R;
                fm.QuadO(a, b, c, d, (a + b + c + d) * 0.25f - tc, 0);
            }
        return fm;
    }

    static FM Ico(int subdiv, float r, Func<Vector3, Vector3> deform = null, Func<Vector3, Vector3, int> subOf = null, int subCount = 1)
    {
        List<Vector3> vs; List<int> ts;
        IcoData(subdiv, out vs, out ts);
        var p = new Vector3[vs.Count];
        for (int i = 0; i < vs.Count; i++) p[i] = deform != null ? deform(vs[i]) : vs[i] * r;
        var fm = new FM(subCount);
        for (int i = 0; i < ts.Count; i += 3)
        {
            Vector3 a = p[ts[i]], b = p[ts[i + 1]], c = p[ts[i + 2]];
            var cen = (a + b + c) / 3f;
            var n = Vector3.Cross(b - a, c - a).normalized;
            if (Vector3.Dot(n, cen) < 0f) n = -n;
            fm.TriO(a, b, c, cen, subOf != null ? subOf(cen, n) : 0);
        }
        return fm;
    }

    static void IcoData(int subdiv, out List<Vector3> verts, out List<int> tris)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        verts = new List<Vector3> { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
        for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
        tris = new List<int> { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8, 3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
        for (int s = 0; s < subdiv; s++)
        {
            var cache = new Dictionary<long, int>(); var nt = new List<int>();
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Mid(verts, cache, a, b), bc = Mid(verts, cache, b, c), ca = Mid(verts, cache, c, a);
                nt.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            tris = nt;
        }
    }

    static int Mid(List<Vector3> v, Dictionary<long, int> cache, int a, int b)
    {
        long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
        int idx;
        if (cache.TryGetValue(key, out idx)) return idx;
        v.Add(((v[a] + v[b]) * 0.5f).normalized); cache[key] = v.Count - 1; return v.Count - 1;
    }

    const float PaperTile = 1.4f;
    static float Frac(float x) { return x - Mathf.Floor(x); }

    // Moves every vertex through f. The same position always moves the same way, so facets stay sealed.
    static FM Warp(FM src, Func<Vector3, Vector3> f)
    {
        var o = new FM(src.subs.Count);
        for (int s = 0; s < src.subs.Count; s++)
        {
            var L = src.subs[s];
            for (int i = 0; i < L.Count; i += 3) o.Tri(f(src.v[L[i]]), f(src.v[L[i + 1]]), f(src.v[L[i + 2]]), s);
        }
        return o;
    }

    static Func<Vector3, Vector3> Lumpy(float amp, float freq, float seed = 0f)
    {
        return p =>
        {
            var q = p * freq + Vector3.one * seed;
            return p + new Vector3(Noise3(q), Noise3(q + Vector3.one * 17.3f), Noise3(q + Vector3.one * 41.7f)) * amp;
        };
    }

    // A small clump of grass: a few two-sided blades leaning out from the centre.
    static FM Tuft()
    {
        var fm = new FM(2);
        int blades = rng.Next(4, 8);
        for (int i = 0; i < blades; i++)
        {
            float a = Rand(0f, 360f);
            var yaw = Quaternion.Euler(0f, a, 0f);
            var root = yaw * new Vector3(Rand(0f, 0.035f), 0f, 0f);
            var tip = root + yaw * Quaternion.Euler(0f, 0f, -Rand(8f, 38f)) * Vector3.up * Rand(0.08f, 0.17f);
            var side = yaw * Vector3.forward * Rand(0.01f, 0.018f);
            int s = i % 2;
            fm.Tri(root - side, root + side, tip, s);
            fm.Tri(root - side, tip, root + side, s);
        }
        return fm;
    }

    static Matrix4x4 Orient(Vector3 pos, Vector3 axis, float roll, Vector3 scale)
    {
        return Matrix4x4.TRS(pos, Quaternion.FromToRotation(Vector3.up, axis.normalized) * Quaternion.Euler(0f, roll, 0f), scale);
    }

    // ------------------------------------------------------------------ utilities
    static float Noise3(Vector3 p)
    {
        float a = Mathf.PerlinNoise(p.x + 31.7f, p.y + 12.3f);
        float b = Mathf.PerlinNoise(p.y + 4.1f, p.z + 57.2f);
        float c = Mathf.PerlinNoise(p.z + 91.3f, p.x + 7.7f);
        return (a + b + c) / 3f * 2f - 1f;
    }

    static float Rand(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }
    static Vector3 RandV() { return new Vector3(Rand(-1f, 1f), Rand(-1f, 1f), Rand(-1f, 1f)); }
    static Color Hex(string h) { Color c; ColorUtility.TryParseHtmlString(h, out c); return c; }

    static Material Mat(string hex, float smooth = 0.12f, float emis = 0f, bool twoSided = false, float metal = 0f, bool paper = true)
    {
        string key = hex + "|" + smooth + "|" + emis + "|" + twoSided + "|" + metal + "|" + paper;
        Material m;
        if (mats.TryGetValue(key, out m)) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var c = Hex(hex);
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        if (emis > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * emis);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        if (twoSided) { m.SetFloat("_Cull", 0f); m.doubleSidedGI = true; }
        if (paper && paperAlbedo)
        {
            m.SetTexture("_BaseMap", paperAlbedo);
            m.SetTexture("_BumpMap", paperNormal);
            m.SetFloat("_BumpScale", 0.6f);
            m.EnableKeyword("_NORMALMAP");
        }
        m.name = "M_" + hex.TrimStart('#') + (emis > 0f ? "_Glow" : "") + (metal > 0f ? "_Metal" : "");
        AssetDatabase.CreateAsset(m, Gen + "/Materials/" + m.name + "_" + mats.Count + ".mat");
        mats[key] = m;
        return m;
    }

    static GameObject Obj(string name, FM fm, Transform parent, params Material[] ms)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        var mesh = fm.ToMesh(name);
        AssetDatabase.AddObjectToAsset(mesh, MeshAsset);
        meshCount++;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var used = new Material[fm.subs.Count];
        for (int i = 0; i < used.Length; i++) used[i] = ms[Mathf.Min(i, ms.Length - 1)];
        mr.sharedMaterials = used;
        return go;
    }

    // Flat-shaded mesh builder: every triangle gets its own vertices and face normal.
    class FM
    {
        public List<Vector3> v = new List<Vector3>();
        public List<Vector3> n = new List<Vector3>();
        public List<List<int>> subs = new List<List<int>>();
        public FM(int subCount) { for (int i = 0; i < Mathf.Max(1, subCount); i++) subs.Add(new List<int>()); }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, int s)
        {
            var nn = Vector3.Cross(b - a, c - a);
            if (nn.sqrMagnitude < 1e-14f) return;
            nn.Normalize();
            while (subs.Count <= s) subs.Add(new List<int>());
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); n.Add(nn); n.Add(nn); n.Add(nn);
            subs[s].Add(i); subs[s].Add(i + 1); subs[s].Add(i + 2);
        }
        public void TriO(Vector3 a, Vector3 b, Vector3 c, Vector3 hint, int s)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), hint) < 0f) Tri(a, c, b, s); else Tri(a, b, c, s);
        }
        public void QuadO(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 hint, int s) { TriO(a, b, c, hint, s); TriO(a, c, d, hint, s); }
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c, 0); Tri(a, c, d, 0); }
        public void Append(FM o, Matrix4x4 m, int targetSub = -1)
        {
            for (int s = 0; s < o.subs.Count; s++)
            {
                var L = o.subs[s]; int ts = targetSub >= 0 ? targetSub : s;
                for (int i = 0; i < L.Count; i += 3)
                    Tri(m.MultiplyPoint3x4(o.v[L[i]]), m.MultiplyPoint3x4(o.v[L[i + 1]]), m.MultiplyPoint3x4(o.v[L[i + 2]]), ts);
            }
        }
        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            m.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(v); m.SetNormals(n);
            // every facet gets its own planar UVs, like a separate piece of paper (about 0.7 m per texture tile)
            var uv = new List<Vector2>(v.Count);
            for (int i = 0; i < v.Count; i += 3)
            {
                var nn = n[i];
                var tu = Vector3.Cross(nn, Mathf.Abs(nn.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                var tv = Vector3.Cross(nn, tu);
                var off = new Vector2(Frac(v[i].x * 12.9898f + v[i].z * 78.233f), Frac(v[i].y * 37.719f + v[i].x * 4.581f));
                for (int k = 0; k < 3; k++) uv.Add(new Vector2(Vector3.Dot(v[i + k], tu), Vector3.Dot(v[i + k], tv)) * PaperTile + off);
            }
            m.SetUVs(0, uv);
            m.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i);
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }
    }
}
