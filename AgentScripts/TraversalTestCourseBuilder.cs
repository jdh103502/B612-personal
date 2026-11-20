using System;
using System.Linq;
using B612;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TraversalTestCourseBuilder
{
    public const string ScenePath="Assets/B612/Scenes/B612_TraversalTest.unity";
    const string Folder="Assets/B612/Generated/TraversalTest";
    public const string Help="1 / 2 / 3 구간 선택 · R 다시 연습 · F12 능력 켜짐/꺼짐 비교\n민트색 길: 기본 이동 우회로   |   금색 길: 능력 지름길\nWASD 이동 · Space 점프/유지 · Shift 대쉬 · 마우스 드래그 / Z·C 카메라 회전 · Ctrl+휠 줌";
    public static object Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit mode.");
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/B612/Scenes/B612_EmptyPlanet.unity" && scene.path!=ScenePath)throw new InvalidOperationException("Open the prototype or traversal test scene first.");
        if(scene.path!=ScenePath)
        {
            if(scene.isDirty)throw new InvalidOperationException("Save the original scene before copying.");
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))throw new InvalidOperationException("Traversal test scene already exists; open it to update.");
            EditorSceneManager.SaveScene(scene,ScenePath,true);scene=EditorSceneManager.OpenScene(ScenePath);
        }
        foreach(string name in new[]{"B612 World","Baobab Surge","Surge Timer HUD","Lamb Card Hand","StarSystemFlow"})
        {var root=scene.GetRootGameObjects().FirstOrDefault(r=>r.name==name);if(root)root.SetActive(false);}
        foreach(var demo in UnityEngine.Object.FindObjectsByType<DemoHUD>(FindObjectsSortMode.None))demo.enabled=false;
        var previous=UnityEngine.Object.FindFirstObjectByType<TraversalTestCourse>();
        if(previous)UnityEngine.Object.DestroyImmediate(previous.gameObject);
        EnsureFolder(Folder);
        var mesh=TraversalTestCourseGeometry.CreateMesh();string meshPath=Folder+"/FixedTraversalPlanet.asset";
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if(saved){EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);mesh=saved;}else AssetDatabase.CreateAsset(mesh,meshPath);
        var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/CourseGround.mat");
        if(!material){material=new Material(Shader.Find("B612/Traversal Course"));AssetDatabase.CreateAsset(material,Folder+"/CourseGround.mat");}
        var rootCourse=new GameObject("Fixed Traversal Test Planet");
        var course=rootCourse.AddComponent<TraversalTestCourse>();
        course.gravity=rootCourse.AddComponent<GravitySource>();course.gravity.surfaceRadius=TraversalTestCourseGeometry.Radius;course.gravity.influenceRadius=32;course.gravity.strength=3.4f;
        var ground=new GameObject("Authored Ground");ground.transform.SetParent(rootCourse.transform,false);
        ground.AddComponent<MeshFilter>().sharedMesh=mesh;ground.AddComponent<MeshRenderer>().sharedMaterial=material;
        course.ground=ground.AddComponent<MeshCollider>();course.ground.sharedMesh=mesh;
        course.player=UnityEngine.Object.FindFirstObjectByType<PlayerController>();course.player.respawnDistance=80;
        course.practiceSpawn=new GameObject("Practice Spawn").transform;course.practiceSpawn.SetParent(rootCourse.transform,false);
        var start=TraversalTestCourseGeometry.Start(0);Physics.SyncTransforms();
        if(!course.Project(0,start.x,start.y,out var point,out var normal))throw new InvalidOperationException("No ground under practice spawn.");
        var forward=TraversalTestCourseGeometry.Direction(0,start.x+.1f,0)-TraversalTestCourseGeometry.Direction(0,start.x,0);
        course.practiceSpawn.SetPositionAndRotation(point+normal*.04f,Quaternion.LookRotation(Vector3.ProjectOnPlane(forward,normal),normal));
        course.player.spawnPoint=course.practiceSpawn;course.player.transform.SetPositionAndRotation(course.practiceSpawn.position,course.practiceSpawn.rotation);
        course.player.cameraRig.cardInputMode=true;course.player.cameraRig.introSeconds=0;course.player.cameraRig.pitch=55f;course.player.cameraRig.distance=6.4f;
        var debug=UnityEngine.Object.FindFirstObjectByType<DeveloperDebugHUD>();
        // Keep the existing ability UI and modal service, omit the unrelated baobab command in this scene.
        var panel=debug.closeButton.transform.parent as RectTransform;panel.sizeDelta=new Vector2(640,560);
        foreach(string name in new[]{"BaobabSkipTitle","BaobabSkipButton","BaobabSkipDescription"}){var item=panel.Find(name);if(item)item.gameObject.SetActive(false);}
        Move(panel,"Header",-80,210);Move(panel,"Status",218,210);Move(panel,"AbilityToggles",0,30);Move(panel,"Hint",0,-140);Move(panel,"CloseButton",0,-200);Move(panel,"SessionHint",0,-253);
        TMP_FontAsset font=Font();
        var canvasGO=new GameObject("Course Guide",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler));canvasGO.transform.SetParent(rootCourse.transform,false);
        var canvas=canvasGO.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=100;
        var scaler=canvasGO.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
        var info=Rect("Course Status",canvasGO.transform,new Vector2(720,240),new Vector2(0,1),new Vector2(24,-24));Background(info);
        course.titleLabel=Label("Title",info,font,new Vector2(680,72),new Vector2(20,-12),26);
        course.detailLabel=Label("Details",info,font,new Vector2(680,144),new Vector2(20,-88),19);
        var help=Rect("Practice Controls",canvasGO.transform,new Vector2(1320,98),new Vector2(.5f,0),new Vector2(-660,18));help.pivot=new Vector2(0,0);Background(help);
        var helpText=Label("Help",help,font,new Vector2(1276,88),new Vector2(20,-8),19);helpText.text=Help;
        EditorUtility.SetDirty(course.player);EditorUtility.SetDirty(course.player.cameraRig);EditorUtility.SetDirty(debug);EditorUtility.SetDirty(mesh);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        return new{scene=ScenePath,vertices=mesh.vertexCount,triangles=mesh.triangles.Length/3,gravity=course.gravity.strength,fontHasText=font.HasCharacters(TraversalTestCourse.Glyphs+Help),eventSystems=UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length};
    }
    static void EnsureFolder(string path)
    {if(AssetDatabase.IsValidFolder(path))return;int slash=path.LastIndexOf('/');EnsureFolder(path.Substring(0,slash));AssetDatabase.CreateFolder(path.Substring(0,slash),path.Substring(slash+1));}
    static void Move(RectTransform panel,string name,float x,float y){var t=panel.Find(name) as RectTransform;if(t)t.anchoredPosition=new Vector2(x,y);}
    static TMP_FontAsset Font()
    {
        string path=Folder+"/Traversal Guide Korean SDF.asset";var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if(font)
        {
            if(!font.HasCharacters(TraversalTestCourse.Glyphs+Help))
            {font.atlasPopulationMode=AtlasPopulationMode.Dynamic;try{if(!font.TryAddCharacters(TraversalTestCourse.Glyphs+Help,out string missingGlyphs))throw new InvalidOperationException("Missing glyphs: "+missingGlyphs);}finally{font.atlasPopulationMode=AtlasPopulationMode.Static;}EditorUtility.SetDirty(font);foreach(var texture in font.atlasTextures)EditorUtility.SetDirty(texture);}
            return font;
        }
        font=TMP_FontAsset.CreateFontAsset("Malgun Gothic","Regular",48);if(!font)throw new InvalidOperationException("Guide font unavailable.");
        string chars=string.Concat(Enumerable.Range(32,95).Select(i=>(char)i))+TraversalTestCourse.Glyphs+Help;
        if(!font.TryAddCharacters(chars,out string missing))throw new InvalidOperationException("Missing guide glyphs: "+missing);
        font.name="Traversal Guide Korean SDF";font.atlasPopulationMode=AtlasPopulationMode.Static;AssetDatabase.CreateAsset(font,path);
        AssetDatabase.AddObjectToAsset(font.material,font);foreach(var texture in font.atlasTextures)AssetDatabase.AddObjectToAsset(texture,font);EditorUtility.SetDirty(font);return font;
    }
    static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 anchor,Vector2 offset)
    {var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=anchor;rect.pivot=new Vector2(0,1);rect.sizeDelta=size;rect.anchoredPosition=offset;return rect;}
    static void Background(RectTransform rect){var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.025f,.04f,.065f,.9f);image.raycastTarget=false;}
    static TMP_Text Label(string name,Transform parent,TMP_FontAsset font,Vector2 size,Vector2 offset,float fontSize)
    {var rect=Rect(name,parent,size,new Vector2(0,1),offset);var text=rect.gameObject.AddComponent<TextMeshProUGUI>();text.font=font;text.fontSize=fontSize;text.color=new Color(.93f,.96f,.92f);text.textWrappingMode=TextWrappingModes.Normal;text.raycastTarget=false;return text;}
}
