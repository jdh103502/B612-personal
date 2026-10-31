using System;
using System.Linq;
using B612;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

public static class BaobabThreatBuilder
{
    const string ScenePath = "Assets/B612/Scenes/B612_EmptyPlanet.unity";
    public static object Build()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath)
            throw new InvalidOperationException("Open B612_EmptyPlanet in Edit mode first.");
        var surge = UnityEngine.Object.FindFirstObjectByType<BaobabSurge>();
        var timer = UnityEngine.Object.FindFirstObjectByType<SurgeTimerHUD>();
        if (!surge || !timer) throw new InvalidOperationException("Build the existing sprout prototype first.");
        if (!AssetDatabase.IsValidFolder("Assets/B612/Settings")) AssetDatabase.CreateFolder("Assets/B612", "Settings");
        var type = Asset<BaobabTypeDefinition>("Assets/B612/Settings/BasicBaobab.asset");
        var profile = AssetDatabase.LoadAssetAtPath<PlanetBaobabProfile>("Assets/B612/Settings/PrototypeBaobabProfile.asset");
        if (!profile)
        {
            profile = ScriptableObject.CreateInstance<PlanetBaobabProfile>();
            profile.defaultType = type;
            AssetDatabase.CreateAsset(profile, "Assets/B612/Settings/PrototypeBaobabProfile.asset");
        }
        var meter = surge.GetComponent<BaobabThreatMeter>();
        if (!meter) meter = Undo.AddComponent<BaobabThreatMeter>(surge.gameObject);
        Undo.RecordObject(surge, "Connect baobab defeat");
        surge.threat = meter;
        meter.surge = surge; meter.profile = profile;
        meter.playerInput = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        meter.cameraInput = Camera.main.GetComponent<OrbitCamera>();
        var hud = timer.GetComponent<BaobabThreatHUD>();
        if (!hud) hud = Undo.AddComponent<BaobabThreatHUD>(timer.gameObject);
        hud.meter = meter;
        if (!hud.fill) MakeHUD(hud, timer.timeLabel.font);
        if (!UnityEngine.Object.FindFirstObjectByType<EventSystem>())
        {
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            Undo.RegisterCreatedObjectUndo(events, "Enable retry button input");
        }
        var scenes = EditorBuildSettings.scenes.ToList();
        int index = scenes.FindIndex(s => s.path == ScenePath);
        if (index < 0) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        else scenes[index].enabled = true;
        EditorBuildSettings.scenes = scenes.ToArray();
        meter.ResetForRun(); hud.Refresh();
        EditorUtility.SetDirty(surge); EditorUtility.SetDirty(meter); EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        return new { scene = scene.path, capacity = profile.threatCapacity, type = type.typeId,
            initial = type.initialThreat, linear = type.linearGrowth, quadratic = type.quadraticGrowth,
            cap = type.maximumThreat, maxActive = surge.maxActiveSprouts,
            retryButton = hud.retryButton != null, eventSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length };
    }

    static T Asset<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (!asset) { asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); }
        return asset;
    }

    static void MakeHUD(BaobabThreatHUD hud, TMP_FontAsset font)
    {
        var panel = Rect("Baobab Threat Panel", hud.transform, new Vector2(.5f,1f), new Vector2(.5f,1f), new Vector2(0,-136), new Vector2(580,86));
        Image(panel, new Color(.035f,.055f,.095f,.94f));
        Label("Title", panel, font, new Vector2(0,1), new Vector2(0,1), new Vector2(24,-12), new Vector2(390,26), 16, "BAOBAB THREAT", new Color(.93f,.91f,.83f)).characterSpacing=2f;
        hud.percentLabel = Label("Percentage", panel, font, new Vector2(1,1), new Vector2(1,1), new Vector2(-24,-9), new Vector2(100,30), 25, "0%", Color.white);
        hud.percentLabel.alignment=TextAlignmentOptions.Right;
        var track=Rect("Threat Track",panel,new Vector2(0,1),new Vector2(0,1),new Vector2(24,-43),new Vector2(532,12));
        Image(track,new Color(.16f,.19f,.24f));
        hud.fill=Rect("Threat Amount",track,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero);
        hud.fill.anchorMax=Vector2.one; hud.fill.offsetMin=hud.fill.offsetMax=Vector2.zero;
        hud.fillImage=Image(hud.fill,new Color(.59f,.86f,.67f));
        hud.statusLabel=Label("Threat Status",panel,font,Vector2.zero,Vector2.zero,new Vector2(24,9),new Vector2(532,20),12,"KEEP THE ROOTS UNDER CONTROL",new Color(.72f,.79f,.81f));

        var overlay=Rect("Game Over Overlay",hud.transform,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero);
        overlay.anchorMax=Vector2.one; overlay.offsetMin=overlay.offsetMax=Vector2.zero;
        var canvas=overlay.gameObject.AddComponent<Canvas>(); canvas.overrideSorting=true; canvas.sortingOrder=100;
        overlay.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        Image(overlay,new Color(.015f,.025f,.045f,.82f)).raycastTarget=true;
        var card=Rect("Result Panel",overlay,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(580,350));
        Image(card,new Color(.045f,.065f,.10f,.98f));
        var top=Rect("Gold Line",card,new Vector2(.5f,1),new Vector2(.5f,1),Vector2.zero,new Vector2(580,3));
        Image(top,new Color(.95f,.74f,.43f));
        var title=Label("Game Over",card,font,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-45),new Vector2(520,64),44,"GAME OVER",new Color(1f,.84f,.62f));
        title.alignment=TextAlignmentOptions.Center; title.characterSpacing=4f;
        var reason=Label("Reason",card,font,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-128),new Vector2(520,60),20,"BAOBAB THREAT REACHED 100%\nThe roots have overwhelmed this planet.",new Color(.9f,.92f,.94f));
        reason.alignment=TextAlignmentOptions.Center;
        var buttonRect=Rect("Try Again",card,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,65),new Vector2(240,56));
        var buttonImage=Image(buttonRect,new Color(.92f,.77f,.5f)); buttonImage.raycastTarget=true;
        hud.retryButton=buttonRect.gameObject.AddComponent<UnityEngine.UI.Button>(); hud.retryButton.targetGraphic=buttonImage;
        var colors=hud.retryButton.colors; colors.highlightedColor=new Color(1f,.94f,.79f); colors.pressedColor=new Color(.72f,.64f,.51f); hud.retryButton.colors=colors;
        var buttonText=Label("Label",buttonRect,font,new Vector2(.5f,.5f),new Vector2(.5f,.5f),Vector2.zero,new Vector2(220,42),20,"TRY AGAIN",new Color(.12f,.14f,.19f));
        buttonText.alignment=TextAlignmentOptions.Center;
        var help=Label("Retry Key",card,font,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,27),new Vector2(520,24),13,"ENTER  /  TRY AGAIN TO RESTART",new Color(.68f,.75f,.8f));
        help.alignment=TextAlignmentOptions.Center;
        hud.gameOverPanel=overlay.gameObject; overlay.gameObject.SetActive(false);
    }
    static RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 pos,Vector2 size)
    {
        var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent,false); rect.anchorMin=rect.anchorMax=anchor; rect.pivot=pivot;
        rect.anchoredPosition=pos; rect.sizeDelta=size; return rect;
    }
    static UnityEngine.UI.Image Image(RectTransform rect,Color color)
    {
        var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color=color; image.raycastTarget=false; return image;
    }
    static TMP_Text Label(string name,Transform parent,TMP_FontAsset font,Vector2 anchor,Vector2 pivot,Vector2 pos,Vector2 size,float fontSize,string content,Color color)
    {
        var rect=Rect(name,parent,anchor,pivot,pos,size);
        var label=rect.gameObject.AddComponent<TextMeshProUGUI>(); label.font=font; label.fontSize=fontSize;
        label.color=color; label.text=content; label.raycastTarget=false; label.textWrappingMode=TextWrappingModes.NoWrap; return label;
    }
}
