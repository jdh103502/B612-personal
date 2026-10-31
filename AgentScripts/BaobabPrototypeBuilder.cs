using System;
using System.Linq;
using B612;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// unity command run_script --file AgentScripts/BaobabPrototypeBuilder.cs --entry BaobabPrototypeBuilder.Build
public static class BaobabPrototypeBuilder
{
    const string ModelRoot = "Assets/B612/Models/Sprout";
    const string ModelName = "Meshy_AI_Lowpoly_Sprout_20261028_texture";
    const string PrefabPath = "Assets/B612/Prefabs/BaobabSprout.prefab";

    public static object Build()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != EmptyScene)
            throw new InvalidOperationException("Open B612_EmptyPlanet in Edit mode first.");
        var prefab = MakeSprout();
        var planet = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GravitySource>()).Single();
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var grid = planet.GetComponent<PlanetSurfaceGrid>();
        if (!grid) grid = Undo.AddComponent<PlanetSurfaceGrid>(planet.gameObject);
        grid.ground = planet.GetComponent<MeshCollider>();
        grid.subdivisions = 1; grid.candidatesPerCell = 32; grid.maxSlope = 55f;
        var director = UnityEngine.Object.FindFirstObjectByType<BaobabSurge>();
        if (!director)
        {
            var go = new GameObject("Baobab Surge");
            Undo.RegisterCreatedObjectUndo(go, "Create baobab prototype");
            director = go.AddComponent<BaobabSurge>();
        }
        director.surface = grid; director.sproutPrefab = prefab;
        director.player = player.transform; director.spawnPoint = player.spawnPoint;
        director.durationSeconds = 120f; director.targetSprouts = 40; director.minimumSpacing = 1.4f;
        director.maxActiveSprouts = 6;
        director.randomizeSeed = true;
        var hud = UnityEngine.Object.FindFirstObjectByType<SurgeTimerHUD>();
        if (!hud) hud = MakeHUD();
        hud.surge = director;
        AttachIndicators(hud, director);
        hud.Refresh();
        Physics.SyncTransforms();
        grid.Prepare(612);
        EditorUtility.SetDirty(grid); EditorUtility.SetDirty(director); EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = director.gameObject;
        return new { scene = scene.path, cells = grid.Cells.Count, slots = grid.SlotCount, grid.EmptyCellCount,
            duration = director.durationSeconds, sprouts = director.targetSprouts, spacing = director.minimumSpacing,
            prefab = PrefabPath, font = hud.timeLabel.font.name };
    }

    public static object AddHorizonUI()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != EmptyScene)
            throw new InvalidOperationException("Open B612_EmptyPlanet in Edit mode first.");
        var director = UnityEngine.Object.FindFirstObjectByType<BaobabSurge>();
        var hud = UnityEngine.Object.FindFirstObjectByType<SurgeTimerHUD>();
        if (!director || !hud) throw new InvalidOperationException("Build the baobab prototype first.");
        Undo.RecordObject(director, "Limit simultaneous sprouts");
        director.maxActiveSprouts = 6;
        var indicators = AttachIndicators(hud, director);
        hud.Refresh();
        EditorUtility.SetDirty(director); EditorUtility.SetDirty(hud); EditorUtility.SetDirty(indicators);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        return new { scene = scene.path, director.maxActiveSprouts, indicators = indicators.name };
    }

    static SproutHorizonIndicators AttachIndicators(SurgeTimerHUD hud, BaobabSurge director)
    {
        var root = hud.transform.Find("Horizon Indicators") as RectTransform;
        if (!root)
        {
            root = new GameObject("Horizon Indicators", typeof(RectTransform)).GetComponent<RectTransform>();
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Create horizon indicators");
            root.SetParent(hud.transform, false); root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
        }
        var indicators = root.GetComponent<SproutHorizonIndicators>();
        if (!indicators) indicators = root.gameObject.AddComponent<SproutHorizonIndicators>();
        indicators.surge = director; indicators.viewCamera = Camera.main;
        indicators.canvasRect = hud.GetComponent<RectTransform>();
        return indicators;
    }

    const string EmptyScene = "Assets/B612/Scenes/B612_EmptyPlanet.unity";

    static GameObject MakeSprout()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)) return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Folder("Assets/B612/Prefabs"); Folder("Assets/B612/Materials");
        string modelPath = ModelRoot + "/" + ModelName + ".fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        importer.importCameras = false; importer.importLights = false; importer.importAnimation = false;
        importer.SaveAndReimport();
        var normalImporter = (TextureImporter)AssetImporter.GetAtPath(ModelRoot + "/" + ModelName + "_normal.png");
        normalImporter.textureType = TextureImporterType.NormalMap;
        normalImporter.SaveAndReimport();
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(ModelRoot + "/" + ModelName + ".png");
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Sprout" };
        material.SetTexture("_BaseMap", albedo);
        material.SetFloat("_Metallic", 0f); material.SetFloat("_Smoothness", 0.22f);
        material.SetFloat("_Cull", 0f);
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ModelRoot + "/" + ModelName + "_normal.png"));
        material.SetFloat("_BumpScale", 0.4f); material.EnableKeyword("_NORMALMAP");
        material.SetTexture("_EmissionMap", albedo);
        material.SetColor("_EmissionColor", Color.white * 0.08f); material.EnableKeyword("_EMISSION");
        AssetDatabase.CreateAsset(material, "Assets/B612/Materials/Sprout.mat");
        var root = new GameObject("Baobab Sprout");
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath), root.transform);
            model.name = "Sprout Visual";
            var renderers = model.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
                r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
            }
            float scale = 0.55f / Mathf.Max(bounds.size.y, 0.0001f);
            model.transform.localScale *= scale;
            model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static SurgeTimerHUD MakeHUD()
    {
        var go = new GameObject("Surge Timer HUD", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30;
        var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900); scaler.matchWidthOrHeight = 0.5f;
        var hud = go.AddComponent<SurgeTimerHUD>();
        var panel = Rect("Timer Panel", go.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0,-24), new Vector2(580,104));
        Image(panel, new Color(0.035f, 0.055f, 0.095f, 0.91f));
        var accent = Rect("Accent", panel, new Vector2(0,1), new Vector2(0,1), Vector2.zero, new Vector2(3,104));
        Image(accent, new Color(0.63f,0.87f,0.70f));
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        if (!font) throw new InvalidOperationException("Import TMP essentials before building the HUD.");
        hud.titleLabel = Label("Phase", panel, font, new Vector2(0,1), new Vector2(0,1), new Vector2(24,-16), new Vector2(350,28), 18, new Color(0.91f,0.96f,0.90f));
        hud.titleLabel.characterSpacing = 3f;
        hud.timeLabel = Label("Countdown", panel, font, new Vector2(1,1), new Vector2(1,1), new Vector2(-24,-12), new Vector2(120,36), 28, new Color(1f,0.91f,0.68f));
        hud.timeLabel.alignment = TextAlignmentOptions.Right;
        var track = Rect("Timer Track", panel, new Vector2(0,1), new Vector2(0,1), new Vector2(24,-56), new Vector2(532,10));
        Image(track, new Color(0.15f,0.20f,0.27f));
        var fill = Rect("Remaining Time", track, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        fill.anchorMax = Vector2.one; fill.offsetMin = fill.offsetMax = Vector2.zero;
        hud.fill = fill; hud.fillImage = Image(fill, hud.normalColor);
        hud.countLabel = Label("Sprout Count", panel, font, Vector2.zero, Vector2.zero, new Vector2(24,12), new Vector2(340,20), 13, new Color(0.64f,0.74f,0.80f));
        return hud;
    }

    static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot;
        rect.anchoredPosition = pos; rect.sizeDelta = size;
        return rect;
    }

    static UnityEngine.UI.Image Image(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color; image.raycastTarget = false;
        return image;
    }

    static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, float fontSize, Color color)
    {
        var rect = Rect(name, parent, anchor, pivot, pos, size);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSize = fontSize; text.color = color; text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.alignment = TextAlignmentOptions.Left;
        return text;
    }

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0,split), path.Substring(split+1));
    }
}
