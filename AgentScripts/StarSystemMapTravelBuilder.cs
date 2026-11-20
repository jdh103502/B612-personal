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

public static class StarSystemMapTravelBuilder
{
    const string FontText="항성계 지도바오밥 생성 종료 후 해금행성 활성화 · M 키로 다음 선택테스트 완료끝나면 지도를 열 수 있습니다준비하지 못했습니다 다시 선택해 주세요지금 여기 가능지나온 시작도착밝은 테두리의 행성을 클릭해 이동하세요행성 크기: 소중대닫기Esc행성으로 이동 중";
    public static object Build()
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!="Assets/B612/Scenes/B612_EmptyPlanet.unity")throw new InvalidOperationException("Open the existing EmptyPlanet scene in Edit mode.");
        var existing=UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();if(existing){FontAsset();return new{alreadyConnected=true};}
        var surge=UnityEngine.Object.FindFirstObjectByType<BaobabSurge>();var player=UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var hand=UnityEngine.Object.FindFirstObjectByType<LambCardHand>();var pause=UnityEngine.Object.FindFirstObjectByType<GamePauseCoordinator>();
        var events=UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
        if(!surge||!player||!hand||!pause||events.Length!=1||!events[0].GetComponent<InputSystemUIInputModule>())throw new InvalidOperationException("Existing services/EventSystem missing.");
        var oldGrid=surge.surface;var ground=oldGrid.ground;var oldGravity=ground.GetComponent<GravitySource>();var oldSpawn=surge.spawnPoint;
        if(!oldGravity||!ground.GetComponent<MeshFilter>()||!ground.GetComponent<MeshCollider>()||ground.transform.lossyScale!=Vector3.one)throw new InvalidOperationException("Unexpected original planet template.");
        Folder("Assets/B612/Settings/PlanetTravel");Folder("Assets/B612/Prefabs/PlanetTravel");Folder("Assets/B612/UI/Map");
        const string settingsPath="Assets/B612/Settings/PlanetTravel/EmptyPlanetGeneration.asset";
        var settings=AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>(settingsPath);
        if(!settings)
        {
            settings=ScriptableObject.CreateInstance<PlanetGenerationSettings>();
            settings.baselineScale=ground.transform.localScale;settings.nominalRadius=5.3f;settings.gravitySurfaceRadius=oldGravity.surfaceRadius;
            settings.influenceRadius=oldGravity.influenceRadius;settings.gravityStrength=oldGravity.strength;
            settings.originalMaterials=ground.GetComponent<Renderer>().sharedMaterials;settings.baselineColor=settings.originalMaterials[0].color;
            settings.spawnDirection=(oldSpawn.position-ground.transform.position).normalized;settings.spawnForward=oldSpawn.forward;
            settings.subdivisions=oldGrid.subdivisions;settings.candidatesPerCell=oldGrid.candidatesPerCell;settings.maxSpawnSlope=oldGrid.maxSlope;
            var template=new GameObject("GroundSurface",typeof(MeshFilter),typeof(MeshRenderer),typeof(MeshCollider));
            template.GetComponent<MeshFilter>().sharedMesh=ground.GetComponent<MeshFilter>().sharedMesh;
            template.GetComponent<MeshRenderer>().sharedMaterials=settings.originalMaterials;
            var meshCollider=template.GetComponent<MeshCollider>();meshCollider.sharedMesh=ground.GetComponent<MeshCollider>().sharedMesh;meshCollider.sharedMaterial=ground.sharedMaterial;
            template.transform.localScale=settings.baselineScale;
            settings.groundTemplate=PrefabUtility.SaveAsPrefabAsset(template,"Assets/B612/Prefabs/PlanetTravel/EmptyPlanetGround.prefab");UnityEngine.Object.DestroyImmediate(template);
            AssetDatabase.CreateAsset(settings,settingsPath);
        }
        var parent=ground.transform.parent;
        var hostObject=new GameObject("PlanetWorldHost");hostObject.transform.SetParent(parent,false);Undo.RegisterCreatedObjectUndo(hostObject,"Add common planet host");
        var host=hostObject.AddComponent<PlanetWorldHost>();host.settings=settings;
        var root=new GameObject("ActivePlanetWorld");root.transform.SetParent(host.transform,false);
        var world=root.AddComponent<PlanetWorldInstance>();world.gravity=root.AddComponent<GravitySource>();
        world.gravity.surfaceRadius=oldGravity.surfaceRadius;world.gravity.influenceRadius=oldGravity.influenceRadius;world.gravity.strength=oldGravity.strength;
        world.surface=root.AddComponent<PlanetSurfaceGrid>();world.surface.ground=ground;world.surface.subdivisions=oldGrid.subdivisions;world.surface.candidatesPerCell=oldGrid.candidatesPerCell;world.surface.maxSlope=oldGrid.maxSlope;
        Undo.SetTransformParent(ground.transform,root.transform,"Separate ground scale");Undo.SetTransformParent(oldSpawn,root.transform,"Bind original spawn");
        ground.name="GroundSurface";world.ground=ground;world.groundRenderer=ground.GetComponent<Renderer>();world.spawn=oldSpawn;host.initialWorld=world;
        Undo.RecordObject(surge,"Manage surge from star-system flow");surge.surface=world.surface;surge.automaticStart=false;surge.randomizeSeed=false;
        Undo.DestroyObjectImmediate(oldGrid);Undo.DestroyObjectImmediate(oldGravity);
        var flowObject=new GameObject("StarSystemFlow");Undo.RegisterCreatedObjectUndo(flowObject,"Add map and travel flow");
        var flow=flowObject.AddComponent<StarSystemFlowController>();flow.worldHost=host;flow.player=player;flow.hand=hand;flow.surge=surge;flow.pause=pause;
        var view=flowObject.AddComponent<StarSystemMapView>();flow.view=view;view.flow=flow;view.font=FontAsset();BuildUI(view);
        foreach(var component in new UnityEngine.Object[]{host,world,world.surface,world.gravity,surge,flow,view})EditorUtility.SetDirty(component);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        return new{scene=scene.path,settings=settingsPath,groundOnlyTemplate=AssetDatabase.GetAssetPath(settings.groundTemplate),events=events.Length,originalMaterialCount=settings.originalMaterials.Length,scale=world.surface.transform.lossyScale.ToString(),font=AssetDatabase.GetAssetPath(view.font)};
    }
    static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;int slash=path.LastIndexOf('/');Folder(path.Substring(0,slash));AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));}
    static TMP_FontAsset FontAsset()
    {
        const string path="Assets/B612/UI/Map/Planet Map Korean SDF.asset";
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if(font)
        {
            if(!font.HasCharacters(FontText))
            {
                font.atlasPopulationMode=AtlasPopulationMode.DynamicOS;
                if(!font.TryAddCharacters(FontText,out string absent))throw new InvalidOperationException("Missing map glyphs: "+absent);
                font.atlasPopulationMode=AtlasPopulationMode.Static;EditorUtility.SetDirty(font);AssetDatabase.SaveAssetIfDirty(font);
            }
            return font;
        }
        font=TMP_FontAsset.CreateFontAsset("Malgun Gothic","Regular",48);if(!font)throw new InvalidOperationException("Korean system font missing.");
        if(!font.TryAddCharacters(string.Concat(Enumerable.Range(32,95).Select(i=>(char)i))+FontText,out string missing))throw new InvalidOperationException("Missing map glyphs: "+missing);
        font.name="Planet Map Korean SDF";font.atlasPopulationMode=AtlasPopulationMode.Static;
        var serialized=new SerializedObject(font);serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue=true;serialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(font,path);font.material.name=font.name+" Material";AssetDatabase.AddObjectToAsset(font.material,font);
        foreach(var texture in font.atlasTextures){texture.name=font.name+" Atlas";AssetDatabase.AddObjectToAsset(texture,font);}EditorUtility.SetDirty(font);return font;
    }
    static void BuildUI(StarSystemMapView view)
    {
        var canvas=Rect("StarSystemCanvas",view.transform,new Vector2(1600,900));view.canvasRect=canvas;
        var render=canvas.gameObject.AddComponent<Canvas>();render.renderMode=RenderMode.ScreenSpaceOverlay;render.sortingOrder=20000;
        var scaler=canvas.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
        canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        view.statusLabel=Label("MapStatus",canvas,view.font,"바오밥 생성 종료 후 지도 해금",new Vector2(240,-32),new Vector2(440,36),20);view.statusLabel.rectTransform.anchorMin=view.statusLabel.rectTransform.anchorMax=new Vector2(0,1);
        view.noticeLabel=Label("MapNotice",canvas,view.font,"",new Vector2(245,-88),new Vector2(450,80),18);view.noticeLabel.rectTransform.anchorMin=view.noticeLabel.rectTransform.anchorMax=new Vector2(0,1);view.noticeLabel.textWrappingMode=TextWrappingModes.Normal;
        var panel=Rect("MapPanel",canvas);Stretch(panel);view.mapPanel=Group(panel);Image(panel,new Color(.012f,.025f,.045f,.96f),true);
        Label("Title",panel,view.font,"항성계 지도",new Vector2(0,340),new Vector2(1100,70),40).alignment=TextAlignmentOptions.Center;
        view.mapStatus=Label("Instructions",panel,view.font,"밝은 테두리의 행성을 클릭해 이동하세요",new Vector2(0,275),new Vector2(1100,48),22);view.mapStatus.alignment=TextAlignmentOptions.Center;
        view.board=Rect("NodesAndRoutes",panel,new Vector2(1160,430));view.board.anchoredPosition=new Vector2(0,0);
        var button=Rect("CloseMap",panel,new Vector2(340,60));button.anchoredPosition=new Vector2(0,-320);var graphic=Image(button,new Color(.6f,.86f,.74f),true);
        view.closeButton=button.gameObject.AddComponent<UnityEngine.UI.Button>();view.closeButton.targetGraphic=graphic;
        var caption=Label("Label",button,view.font,"닫기 · M / Esc",Vector2.zero,new Vector2(320,42),22);caption.color=new Color(.05f,.12f,.13f);caption.alignment=TextAlignmentOptions.Center;
        view.tooltip=Rect("PlanetTooltip",panel,new Vector2(200,52));view.tooltip.SetParent(canvas,false);view.tooltip.pivot=new Vector2(0,1);view.tooltipGroup=Group(view.tooltip);
        Image(view.tooltip,new Color(.08f,.12f,.18f,.99f));view.tooltipLabel=Label("Size",view.tooltip,view.font,"행성 크기: 중",Vector2.zero,new Vector2(180,42),22);view.tooltipLabel.alignment=TextAlignmentOptions.Center;
        // Tooltip uses canvas coordinates but stays under a dedicated group; it never receives pointer hits.
        var cover=Rect("PlanetTransition",canvas);Stretch(cover);view.transitionPanel=Group(cover);Image(cover,new Color(.012f,.02f,.035f,1f),true);
        var transit=Label("TransitionLabel",cover,view.font,"행성으로 이동 중",Vector2.zero,new Vector2(700,80),32);transit.alignment=TextAlignmentOptions.Center;
    }
    static CanvasGroup Group(RectTransform rect){var group=rect.gameObject.AddComponent<CanvasGroup>();group.alpha=0;group.interactable=group.blocksRaycasts=false;return group;}
    static RectTransform Rect(string name,Transform parent,Vector2 size=default)
    {var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=size;return rect;}
    static void Stretch(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
    static UnityEngine.UI.Image Image(RectTransform rect,Color color,bool raycast=false)
    {var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=color;image.raycastTarget=raycast;return image;}
    static TMP_Text Label(string name,Transform parent,TMP_FontAsset font,string text,Vector2 position,Vector2 size,float pointSize)
    {
        var rect=Rect(name,parent,size);rect.anchoredPosition=position;var label=rect.gameObject.AddComponent<TextMeshProUGUI>();label.font=font;label.text=text;label.fontSize=pointSize;label.color=new Color(.91f,.95f,.96f);label.raycastTarget=false;label.textWrappingMode=TextWrappingModes.NoWrap;return label;
    }
}
