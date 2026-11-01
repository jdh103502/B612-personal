using System;
using System.Linq;
using B612;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Unity CLI run_script --file AgentScripts/LambPrototypeBuilder.cs --entry LambPrototypeBuilder.Build
public static class LambPrototypeBuilder
{
    const string ModelRoot="Assets/B612/Models/Lamb";
    const string ModelName="Meshy_AI_Cotton_Cloud_Lamb_20260324_texture";
    const string PrefabPath="Assets/B612/Prefabs/CloudLamb.prefab";

    public static object Build()
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path!="Assets/B612/Scenes/B612_EmptyPlanet.unity")
            throw new InvalidOperationException("Open B612_EmptyPlanet in Edit mode first.");
        var surge=UnityEngine.Object.FindFirstObjectByType<BaobabSurge>();
        var walker=UnityEngine.Object.FindFirstObjectByType<PlayerController>().GetComponent<GravityWalker>();
        var timer=UnityEngine.Object.FindFirstObjectByType<SurgeTimerHUD>();
        var hand=UnityEngine.Object.FindFirstObjectByType<LambCardHand>();
        if(!hand)
        {
            var go=new GameObject("Lamb Card Hand"); Undo.RegisterCreatedObjectUndo(go,"Create lamb cards");
            hand=go.AddComponent<LambCardHand>();
        }
        hand.surge=surge; hand.player=walker; hand.lambPrefab=MakePrefab();
        hand.startingCards=4; hand.activeSeconds=20f;
        hand.secondsPerSprout=7f; hand.secondsPerSapling=10f; hand.secondsPerTree=13f;
        hand.eatingRadius=1.7f; hand.rangeGrace=.25f; hand.intentRadius=2.05f; hand.pickupRadius=.9f;
        string ringPath="Assets/B612/Materials/LambRange.mat";
        var ring=AssetDatabase.LoadAssetAtPath<Material>(ringPath);
        if(!ring)
        {
            ring=new Material(Shader.Find("Sprites/Default")) { name="Lamb Range" };
            ring.renderQueue=3020; AssetDatabase.CreateAsset(ring,ringPath);
        }
        hand.ringMaterial=ring;
        var hud=UnityEngine.Object.FindFirstObjectByType<LambCardHUD>();
        if(!hud) hud=MakeHUD(timer.transform);
        hud.hand=hand;
        hud.frameSprite=Sprite("Assets/B612/UI/Cards/LambCardFrame.png");
        hud.illustrationSprite=Sprite("Assets/B612/UI/Cards/LambCardIllustration.png");
        hud.font=timer.timeLabel.font;
        AttachFeedback(hand,timer);
        var camera=Camera.main.GetComponent<OrbitCamera>();
        Undo.RecordObject(camera,"Reserve lamb card inputs"); camera.cardInputMode=true;
        EditorUtility.SetDirty(hand); EditorUtility.SetDirty(hud); EditorUtility.SetDirty(camera);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject=hand.gameObject;
        return new { scene=scene.path, cards=hand.startingCards, duration=hand.activeSeconds, eatingSeconds=hand.secondsPerSprout,
            radius=hand.eatingRadius,grace=hand.rangeGrace,prefab=PrefabPath,frame=hud.frameSprite.name,illustration=hud.illustrationSprite.name };
    }

    public static object AddFeedbackUI()
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path!="Assets/B612/Scenes/B612_EmptyPlanet.unity")
            throw new InvalidOperationException("Open B612_EmptyPlanet in Edit mode first.");
        var hand=UnityEngine.Object.FindFirstObjectByType<LambCardHand>();
        var timer=UnityEngine.Object.FindFirstObjectByType<SurgeTimerHUD>();
        if(!hand || !timer) throw new InvalidOperationException("Build the lamb prototype first.");
        var progress=AttachFeedback(hand,timer);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        return new {scene=scene.path,progress=progress.name,pickupHints=true,nearScale=progress.horizon.nearScale,farScale=progress.horizon.farScale};
    }

    static LambEatingIndicators AttachFeedback(LambCardHand hand,SurgeTimerHUD timer)
    {
        var horizon=UnityEngine.Object.FindFirstObjectByType<SproutHorizonIndicators>();
        if(!horizon) throw new InvalidOperationException("Add the horizon UI before lamb feedback.");
        Undo.RecordObject(horizon,"Add lamb pickup hints and distance scale");
        horizon.lambHand=hand;
        horizon.nearDistance=1.5f; horizon.farDistance=12f; horizon.nearScale=1.2f; horizon.farScale=.62f;
        var root=timer.transform.Find("Lamb Eating Indicators") as RectTransform;
        if(!root)
        {
            root=new GameObject("Lamb Eating Indicators",typeof(RectTransform)).GetComponent<RectTransform>();
            Undo.RegisterCreatedObjectUndo(root.gameObject,"Add lamb eating progress");
            root.SetParent(timer.transform,false); root.anchorMin=Vector2.zero; root.anchorMax=Vector2.one;
            root.offsetMin=root.offsetMax=Vector2.zero;
        }
        var progress=root.GetComponent<LambEatingIndicators>();
        if(!progress) progress=root.gameObject.AddComponent<LambEatingIndicators>();
        progress.hand=hand; progress.horizon=horizon; progress.viewCamera=Camera.main; progress.canvasRect=timer.GetComponent<RectTransform>();
        EditorUtility.SetDirty(horizon); EditorUtility.SetDirty(progress);
        return progress;
    }

    static GameObject MakePrefab()
    {
        var existing=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if(existing) return existing;
        var modelImporter=(ModelImporter)AssetImporter.GetAtPath(ModelRoot+"/"+ModelName+".fbx");
        modelImporter.importAnimation=false; modelImporter.importCameras=false; modelImporter.importLights=false;
        modelImporter.SaveAndReimport();
        var normalImporter=(TextureImporter)AssetImporter.GetAtPath(ModelRoot+"/"+ModelName+"_normal.png");
        normalImporter.textureType=TextureImporterType.NormalMap; normalImporter.SaveAndReimport();
        var albedo=AssetDatabase.LoadAssetAtPath<Texture2D>(ModelRoot+"/"+ModelName+".png");
        var material=new Material(Shader.Find("Universal Render Pipeline/Lit")) {name="Cloud Lamb",enableInstancing=true};
        material.SetTexture("_BaseMap",albedo); material.SetFloat("_Smoothness",.18f); material.SetFloat("_Metallic",0f);
        material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(ModelRoot+"/"+ModelName+"_normal.png"));
        material.SetFloat("_BumpScale",.35f); material.EnableKeyword("_NORMALMAP");
        AssetDatabase.CreateAsset(material,"Assets/B612/Materials/CloudLamb.mat");
        var root=new GameObject("Cloud Lamb");
        try
        {
            var visual=new GameObject("Lamb Visual").transform; visual.SetParent(root.transform,false);
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelRoot+"/"+ModelName+".fbx"),visual);
            var renderers=model.GetComponentsInChildren<Renderer>(); Bounds bounds=renderers[0].bounds;
            foreach(var r in renderers) { bounds.Encapsulate(r.bounds); r.sharedMaterials=Enumerable.Repeat(material,r.sharedMaterials.Length).ToArray(); }
            float scale=.65f/Mathf.Max(bounds.size.y,.0001f);
            model.transform.localScale*=scale;
            model.transform.localPosition-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)*scale;
            var agent=root.AddComponent<LambAgent>(); agent.visual=visual;
            return PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static Sprite Sprite(string path)
    {
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType=TextureImporterType.Sprite; importer.spriteImportMode=SpriteImportMode.Single;
        importer.alphaIsTransparency=true; importer.mipmapEnabled=false; importer.maxTextureSize=2048;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static LambCardHUD MakeHUD(Transform canvas)
    {
        var root=Rect("Lamb Cards HUD",canvas,Vector2.zero,new Vector2(.5f,.5f),Vector2.zero,Vector2.zero);
        root.anchorMax=Vector2.one; root.offsetMin=root.offsetMax=Vector2.zero;
        var hud=root.gameObject.AddComponent<LambCardHUD>();
        hud.handRoot=Rect("Hand Fan",root,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,8),new Vector2(700,260));
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        hud.instructionLabel=Label("Controls",root,font,new Vector2(.5f,0),new Vector2(0,294),new Vector2(950,26),15,new Color(.95f,.92f,.81f));
        hud.messageLabel=Label("Placement Hint",root,font,new Vector2(.5f,0),new Vector2(0,266),new Vector2(850,25),14,new Color(.68f,.91f,.77f));
        return hud;
    }

    static TMP_Text Label(string name,Transform parent,TMP_FontAsset font,Vector2 anchor,Vector2 position,Vector2 size,float fontSize,Color color)
    {
        var rect=Rect(name,parent,anchor,new Vector2(.5f,.5f),position,size);
        var text=rect.gameObject.AddComponent<TextMeshProUGUI>(); text.font=font; text.fontSize=fontSize;
        text.alignment=TextAlignmentOptions.Center; text.color=color; text.textWrappingMode=TextWrappingModes.NoWrap; text.raycastTarget=false;
        return text;
    }

    static RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 position,Vector2 size)
    {
        var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent,false); rect.anchorMin=rect.anchorMax=anchor; rect.pivot=pivot;
        rect.anchoredPosition=position; rect.sizeDelta=size; return rect;
    }
}
