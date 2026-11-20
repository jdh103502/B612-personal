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

public static class DeveloperDebugBuilder
{
    public const string Text = "개발자 디버그일시정지 중대쉬 사용더블점프 활공F12로 닫기 · 계속하기플레이어 기술 구성이 없습니다이 설정은 현재 시도에만 유지됩니다";
    public static object Build()
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode||scene.path!="Assets/B612/Scenes/B612_EmptyPlanet.unity")
            throw new InvalidOperationException("Open B612_EmptyPlanet in Edit mode first.");
        var player=UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        var events=UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
        if(!player||events.Length!=1||!events[0].GetComponent<InputSystemUIInputModule>())
            throw new InvalidOperationException("Expected the existing player and one InputSystem EventSystem.");
        var existing=UnityEngine.Object.FindFirstObjectByType<DeveloperDebugController>();
        if(existing)
        {
            // The pause service must outlive the debug UI: destroying that UI must not release GameOver.
            if(existing.pause && existing.pause.transform.IsChildOf(existing.transform))
            {
                var previous=existing.pause;var service=new GameObject("GamePauseCoordinator");Undo.RegisterCreatedObjectUndo(service,"Separate pause ownership from debug UI");
                existing.pause=service.AddComponent<GamePauseCoordinator>();Undo.DestroyObjectImmediate(previous);EditorUtility.SetDirty(existing);
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            }
            AddBaobabSkip(existing);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
            return new{alreadyConnected=true,root=existing.name,skipConnected=existing.hud.skipButton!=null};
        }
        TMP_FontAsset font=FontAsset();
        var root=new GameObject("DeveloperDebugRoot");Undo.RegisterCreatedObjectUndo(root,"Add developer debug mode");
        var pauseRoot=new GameObject("GamePauseCoordinator");Undo.RegisterCreatedObjectUndo(pauseRoot,"Add shared pause service");
        var pause=pauseRoot.AddComponent<GamePauseCoordinator>();
        var controller=root.AddComponent<DeveloperDebugController>();
        var hud=root.AddComponent<DeveloperDebugHUD>();
        controller.player=player;controller.abilities=player.GetComponent<PlayerTraversalAbilities>();controller.pause=pause;controller.hud=hud;hud.controller=controller;
        var canvasRect=Rect("DebugCanvas",root.transform);
        var canvas=canvasRect.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=30000;
        var scaler=canvasRect.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
        canvasRect.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        var panelRoot=Rect("DebugPanelRoot",canvasRect);Stretch(panelRoot);
        hud.panel=panelRoot.gameObject.AddComponent<CanvasGroup>();hud.panel.alpha=0;hud.panel.interactable=hud.panel.blocksRaycasts=false;
        var backdrop=Rect("Backdrop",panelRoot);Stretch(backdrop);Image(backdrop,new Color(.01f,.018f,.035f,.8f),true);
        var panel=Rect("Panel",panelRoot,new Vector2(640,560));Image(panel,new Color(.035f,.055f,.085f,.99f),true);
        var edge=Rect("Accent",panel,new Vector2(640,3));edge.anchorMin=edge.anchorMax=new Vector2(.5f,1);edge.anchoredPosition=new Vector2(0,-1.5f);Image(edge,new Color(.59f,.86f,.72f));
        Label("Header",panel,font,"개발자 디버그",new Vector2(-80,210),new Vector2(400,50),32,new Color(.94f,.95f,.91f));
        hud.statusLabel=Label("Status",panel,font,"일시정지 중",new Vector2(218,210),new Vector2(160,40),18,new Color(.65f,.9f,.76f));hud.statusLabel.alignment=TextAlignmentOptions.Right;
        var rows=Rect("AbilityToggles",panel,new Vector2(544,240));rows.anchoredPosition=new Vector2(0,30);
        var layout=rows.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();layout.spacing=12;
        layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandWidth=true;layout.childForceExpandHeight=false;
        hud.dashToggle=Toggle("DashToggle",rows,font,"대쉬 사용");
        hud.airJumpToggle=Toggle("AirJumpToggle",rows,font,"더블점프 사용");
        hud.glideToggle=Toggle("GlideToggle",rows,font,"활공 사용");
        var hint=Label("Hint",panel,font,"F12로 닫기",new Vector2(0,-140),new Vector2(544,32),18,new Color(.65f,.73f,.78f));hint.alignment=TextAlignmentOptions.Center;
        var button=Rect("CloseButton",panel,new Vector2(544,60));button.anchoredPosition=new Vector2(0,-200);
        var buttonImage=Image(button,new Color(.62f,.88f,.73f),true);
        hud.closeButton=button.gameObject.AddComponent<UnityEngine.UI.Button>();hud.closeButton.targetGraphic=buttonImage;
        var buttonText=Label("Label",button,font,"닫기 · 계속하기",Vector2.zero,new Vector2(500,40),23,new Color(.06f,.12f,.12f));buttonText.alignment=TextAlignmentOptions.Center;
        var footer=Label("SessionHint",panel,font,"이 설정은 현재 시도에만 유지됩니다",new Vector2(0,-253),new Vector2(560,26),14,new Color(.54f,.63f,.69f));footer.alignment=TextAlignmentOptions.Center;
        EditorUtility.SetDirty(controller);EditorUtility.SetDirty(hud);
        AddBaobabSkip(controller);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        return new {scene=scene.path,eventSystems=events.Length,canvasOrder=canvas.sortingOrder,hasHangul=font.HasCharacters(Text),font=AssetDatabase.GetAssetPath(font)};
    }
    static void AddBaobabSkip(DeveloperDebugController controller)
    {
        var hud=controller.hud;
        if(!hud || !hud.closeButton || !hud.statusLabel)throw new InvalidOperationException("Existing debug panel references are incomplete.");
        var panel=hud.closeButton.transform.parent as RectTransform;
        if(!panel || !panel.Find("AbilityToggles") || !panel.Find("Header"))throw new InvalidOperationException("Existing debug panel hierarchy is incomplete.");
        var font=FontAsset();
        if(!font.HasCharacters(DeveloperDebugHUD.SkipGlyphs))
        {
            font.atlasPopulationMode=AtlasPopulationMode.Dynamic;
            try { if(!font.TryAddCharacters(DeveloperDebugHUD.SkipGlyphs,out string missing))throw new InvalidOperationException("Missing skip UI glyphs: "+missing); }
            finally { font.atlasPopulationMode=AtlasPopulationMode.Static; }
            EditorUtility.SetDirty(font);
            foreach(var texture in font.atlasTextures)EditorUtility.SetDirty(texture);
        }
        Undo.RecordObjects(new UnityEngine.Object[]{controller,hud,panel},"Add developer baobab skip");
        controller.flow=UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();
        panel.sizeDelta=new Vector2(640,744);
        Move(panel,"Header",-80,302);Move(panel,"Status",218,302);Move(panel,"AbilityToggles",0,132);
        Move(panel,"Hint",0,-206);Move(panel,"CloseButton",0,-260);Move(panel,"SessionHint",0,-328);
        var section=panel.Find("BaobabSkipTitle");
        if(!section)Label("BaobabSkipTitle",panel,font,"바오밥 페이즈",new Vector2(0,-26),new Vector2(544,32),20,new Color(.93f,.95f,.94f));
        var button=panel.Find("BaobabSkipButton") as RectTransform;
        if(!button)
        {
            button=Rect("BaobabSkipButton",panel,new Vector2(544,54));button.anchoredPosition=new Vector2(0,-82);
            var image=Image(button,new Color(.62f,.88f,.73f),true);
            var component=button.gameObject.AddComponent<UnityEngine.UI.Button>();component.targetGraphic=image;
            var label=Label("Label",button,font,DeveloperDebugHUD.SkipTitle,Vector2.zero,new Vector2(500,40),23,new Color(.06f,.12f,.12f));label.alignment=TextAlignmentOptions.Center;
            Undo.RegisterCreatedObjectUndo(button.gameObject,"Add developer baobab skip");
        }
        hud.skipButton=button.GetComponent<UnityEngine.UI.Button>();hud.skipButtonLabel=button.GetComponentInChildren<TMP_Text>();
        var description=panel.Find("BaobabSkipDescription");
        hud.skipDescription=description?description.GetComponent<TMP_Text>():Label("BaobabSkipDescription",panel,font,DeveloperDebugHUD.SkipReady,new Vector2(0,-148),new Vector2(544,70),19,new Color(.7f,.8f,.79f));
        hud.skipDescription.textWrappingMode=TextWrappingModes.Normal;hud.skipDescription.alignment=TextAlignmentOptions.Center;
        EditorUtility.SetDirty(controller);EditorUtility.SetDirty(hud);
    }
    static void Move(RectTransform parent,string name,float x,float y)
    { var child=parent.Find(name) as RectTransform;if(child){Undo.RecordObject(child,"Fit baobab skip row");child.anchoredPosition=new Vector2(x,y);} }
    static TMP_FontAsset FontAsset()
    {
        const string folder="Assets/B612/UI/Debug";const string path=folder+"/DeveloperDebug Korean SDF.asset";
        var existing=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);if(existing)return existing;
        if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/B612/UI","Debug");
        // Same system font family as DemoHUD. Bake only this panel's fixed text; no OS font dependency in builds.
        var font=TMP_FontAsset.CreateFontAsset("Malgun Gothic","Regular",48);
        if(!font)throw new InvalidOperationException("Malgun Gothic is unavailable.");
        var characters=string.Concat(Enumerable.Range(32,95).Select(i=>(char)i))+Text+DeveloperDebugHUD.SkipGlyphs;
        if(!font.TryAddCharacters(characters,out string missing))throw new InvalidOperationException("Missing UI glyphs: "+missing);
        font.name="DeveloperDebug Korean SDF";font.atlasPopulationMode=AtlasPopulationMode.Static;
        var serializedFont=new SerializedObject(font);serializedFont.FindProperty("m_ClearDynamicDataOnBuild").boolValue=true;serializedFont.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.CreateAsset(font,path);
        font.material.name=font.name+" Material";AssetDatabase.AddObjectToAsset(font.material,font);
        foreach(var texture in font.atlasTextures){texture.name=font.name+" Atlas";AssetDatabase.AddObjectToAsset(texture,font);}
        EditorUtility.SetDirty(font);return font;
    }
    static UnityEngine.UI.Toggle Toggle(string name,Transform parent,TMP_FontAsset font,string content)
    {
        var row=Rect(name,parent,new Vector2(544,68));
        var element=row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();element.preferredHeight=68;
        var background=Image(row,new Color(.075f,.10f,.14f),true);
        var toggle=row.gameObject.AddComponent<UnityEngine.UI.Toggle>();toggle.targetGraphic=background;toggle.isOn=true;toggle.group=null;
        var box=Rect("Checkbox",row,new Vector2(30,30));box.anchorMin=box.anchorMax=new Vector2(0,.5f);box.anchoredPosition=new Vector2(30,0);Image(box,new Color(.22f,.29f,.34f));
        var mark=Rect("Checkmark",box,new Vector2(20,20));var markImage=Image(mark,new Color(.65f,.94f,.77f));toggle.graphic=markImage;
        Label("Label",row,font,content,new Vector2(14,0),new Vector2(420,40),25,new Color(.93f,.95f,.94f));
        return toggle;
    }
    static RectTransform Rect(string name,Transform parent,Vector2 size=default)
    {
        var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=size;return rect;
    }
    static void Stretch(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
    static UnityEngine.UI.Image Image(RectTransform rect,Color color,bool raycast=false)
    {var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=color;image.raycastTarget=raycast;return image;}
    static TMP_Text Label(string name,Transform parent,TMP_FontAsset font,string text,Vector2 position,Vector2 size,float pointSize,Color color)
    {
        var rect=Rect(name,parent,size);rect.anchoredPosition=position;var label=rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font=font;label.text=text;label.fontSize=pointSize;label.color=color;label.raycastTarget=false;label.textWrappingMode=TextWrappingModes.NoWrap;label.alignment=TextAlignmentOptions.MidlineLeft;
        return label;
    }
}
