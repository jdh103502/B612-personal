using System;
using System.IO;
using System.Linq;
using B612;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ChapterJourneyBuilder
{
    public const string ScenePath="Assets/B612/Scenes/B612_ChapterJourney.unity";
    const string Folder="Assets/B612/Settings/Chapters";
    public static object Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Use a saved Edit-mode scene.");
        Ensure(Folder);var chapters=new ChapterTerrainDefinition[3];
        for(int i=0;i<3;i++)
        {
            string path=Folder+"/Chapter"+(i+1)+".asset";var definition=AssetDatabase.LoadAssetAtPath<ChapterTerrainDefinition>(path);
            if(!definition){definition=ScriptableObject.CreateInstance<ChapterTerrainDefinition>();AssetDatabase.CreateAsset(definition,path);}
            definition.chapter=i+1;definition.title=new[]{"첫 걸음","높은 곳으로","하늘을 건너"}[i];definition.regionWeights=ChapterTerrainDefinition.DefaultWeights(i+1);definition.guaranteedEntry=ChapterTerrainDefinition.Entry(i+1);definition.unlock=ChapterTerrainDefinition.Reward(i+1);chapters[i]=definition;EditorUtility.SetDirty(definition);AssetDatabase.SaveAssetIfDirty(definition);
        }
        foreach(string path in new[]{"Assets/B612/Settings/PlanetTravel/EmptyPlanetGeneration.asset","Assets/B612/Settings/PlanetTravel/ProceduralTerrainTestGeneration.asset"})
        {var settings=AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>(path);if(settings){settings.profileId=PlanetGenerationSettings.NaturalProfileId;settings.chapters=chapters;EditorUtility.SetDirty(settings);AssetDatabase.SaveAssetIfDirty(settings);}}
        const string campaignPath=Folder+"/ChapterJourneyGeneration.asset";var campaign=AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>(campaignPath);
        if(!campaign){campaign=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>("Assets/B612/Settings/PlanetTravel/ProceduralTerrainTestGeneration.asset"));campaign.name="ChapterJourneyGeneration";AssetDatabase.CreateAsset(campaign,campaignPath);}
        campaign.chapterCampaign=true;campaign.proceduralTerrain=true;campaign.chapters=chapters;campaign.nominalRadius=12;campaign.profileId=PlanetGenerationSettings.NaturalProfileId;EditorUtility.SetDirty(campaign);AssetDatabase.SaveAssetIfDirty(campaign);
        if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)){var source=EditorSceneManager.OpenScene("Assets/B612/Scenes/B612_EmptyPlanet.unity");EditorSceneManager.SaveScene(source,ScenePath,true);}
        var scene=EditorSceneManager.OpenScene(ScenePath);var flow=UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();flow.worldHost.settings=campaign;flow.randomizeRunSeed=false;flow.runSeed=612101;flow.player.respawnDistance=180;flow.player.cameraRig.pitch=50;flow.player.cameraRig.distance=6.4f;
        string characters=string.Concat(Enumerable.Range(32,95).Select(i=>(char)i))+"·°→–—잠김다음 챕터로첫 걸음높은 곳으로하늘을 건너";
        foreach(string file in new[]{"DeveloperDebugHUD.cs","StarSystemFlowController.cs","CampaignWorldSites.cs","ChapterJourneyHUD.cs","StarSystemMapView.cs"})characters+=File.ReadAllText("Assets/B612/Scripts/"+file);
        foreach(var label in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include,FindObjectsSortMode.None))characters+=label.text;
        var font=Font(characters);
        foreach(var label in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include,FindObjectsSortMode.None)){label.font=font;EditorUtility.SetDirty(label);}
        flow.view.font=font;flow.view.tooltip.sizeDelta=new Vector2(310,96);flow.view.tooltipLabel.rectTransform.sizeDelta=new Vector2(290,86);flow.view.tooltipLabel.textWrappingMode=TextWrappingModes.Normal;
        var panel=flow.view.closeButton.transform.parent;var next=panel.Find("NextChapter") as RectTransform;
        if(!next)
        {
            next=Rect("NextChapter",panel,new Vector2(340,60),new Vector2(.5f,.5f),new Vector2(210,-320));
            var image=next.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.9f,.79f,.44f);var button=next.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;
            var text=Label("Label",next,font,new Vector2(320,46),Vector2.zero,23);text.text="다음 챕터로";text.color=new Color(.08f,.12f,.12f);text.alignment=TextAlignmentOptions.Center;
        }
        flow.view.nextChapterButton=next.GetComponent<UnityEngine.UI.Button>();flow.view.closeButton.GetComponent<RectTransform>().anchoredPosition=new Vector2(-210,-320);
        var old=UnityEngine.Object.FindFirstObjectByType<ChapterJourneyHUD>();if(old)UnityEngine.Object.DestroyImmediate(old.gameObject);
        var root=new GameObject("Chapter Journey Guide",typeof(RectTransform),typeof(Canvas),typeof(UnityEngine.UI.CanvasScaler));var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=1000;
        var scaler=root.GetComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
        var hud=root.AddComponent<ChapterJourneyHUD>();hud.flow=flow;
        var status=Rect("Chapter Status",root.transform,new Vector2(440,92),new Vector2(1,1),new Vector2(-242,-68));Background(status);hud.status=Label("Text",status,font,new Vector2(414,78),Vector2.zero,22);
        var prompt=Rect("Interaction Prompt",root.transform,new Vector2(710,54),new Vector2(.5f,0),new Vector2(0,265));Background(prompt);hud.prompt=Label("Text",prompt,font,new Vector2(680,48),Vector2.zero,22);hud.prompt.alignment=TextAlignmentOptions.Center;
        foreach(var component in new UnityEngine.Object[]{flow,flow.worldHost,flow.player,flow.player.cameraRig,flow.view})EditorUtility.SetDirty(component);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);RegisterScene();AssetDatabase.SaveAssets();return new{scene=ScenePath,chapters=3,fontGlyphs=font.characterTable.Count,campaign.nominalRadius,generator=PlanetGenerationSettings.GeneratorVersion};
    }
    public static object RegisterScene()
    {
        var first = new EditorBuildSettingsScene(ScenePath, true);
        EditorBuildSettings.globalScenes = Promote(EditorBuildSettings.globalScenes);
        EditorBuildSettings.scenes = Promote(EditorBuildSettings.scenes);
        // This Editor's native setters update memory without flushing this project-settings file.
        // Move the campaign entry to the front while preserving the other scenes and settings.
        const string path="ProjectSettings/EditorBuildSettings.asset";string text=File.ReadAllText(path);
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        string marker = "  m_Scenes:" + newline;
        if (!text.Contains(marker)) throw new InvalidOperationException("Unknown build-settings serialization.");
        string entry = "  - enabled: 1" + newline + "    path: " + ScenePath + newline +
            "    guid: " + AssetDatabase.AssetPathToGUID(ScenePath) + newline;
        string pattern = @"(?m)^  - enabled: [01]\r?\n    path: " +
            System.Text.RegularExpressions.Regex.Escape(ScenePath) + @"\r?\n    guid: [0-9a-fA-F]{32}\r?\n";
        string updated = System.Text.RegularExpressions.Regex.Replace(text, pattern, "").Replace(marker, marker + entry);
        if (updated != text) File.WriteAllText(path, updated, new System.Text.UTF8Encoding(false));
        return new { startup = EditorBuildSettings.scenes.First(s => s.enabled).path };

        EditorBuildSettingsScene[] Promote(EditorBuildSettingsScene[] list)
            => new[] { first }.Concat(list.Where(scene => scene.path != ScenePath)).ToArray();
    }
    static TMP_FontAsset Font(string chars)
    {
        string path=Folder+"/Chapter Journey Korean SDF.asset";var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        chars=new string(chars.Where(c=>!char.IsControl(c)).Distinct().ToArray());
        if(font&&font.HasCharacters(chars))return font;
        // Only this builder's generated font is replaced; bake completely before persisting references.
        if(font)AssetDatabase.DeleteAsset(path);
        font=TMP_FontAsset.CreateFontAsset("C:/Windows/Fonts/malgun.ttf",0,48,7,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,2048,2048);
        if(!font.TryAddCharacters(chars,out string missing))throw new InvalidOperationException("Missing campaign glyphs: "+missing);
        font.atlasPopulationMode=AtlasPopulationMode.Static;font.name="Chapter Journey Korean SDF";AssetDatabase.CreateAsset(font,path);AssetDatabase.AddObjectToAsset(font.material,font);
        foreach(var texture in font.atlasTextures){if(!AssetDatabase.Contains(texture))AssetDatabase.AddObjectToAsset(texture,font);EditorUtility.SetDirty(texture);}EditorUtility.SetDirty(font);return font;
    }
    static void Ensure(string folder){if(AssetDatabase.IsValidFolder(folder))return;int slash=folder.LastIndexOf('/');Ensure(folder.Substring(0,slash));AssetDatabase.CreateFolder(folder.Substring(0,slash),folder.Substring(slash+1));}
    static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 anchor,Vector2 position)
    {var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=anchor;rect.pivot=new Vector2(.5f,.5f);rect.sizeDelta=size;rect.anchoredPosition=position;return rect;}
    static TMP_Text Label(string name,Transform parent,TMP_FontAsset font,Vector2 size,Vector2 position,float pointSize)
    {var rect=Rect(name,parent,size,new Vector2(.5f,.5f),position);var label=rect.gameObject.AddComponent<TextMeshProUGUI>();label.font=font;label.fontSize=pointSize;label.color=new Color(.92f,.96f,.92f);label.raycastTarget=false;label.textWrappingMode=TextWrappingModes.Normal;label.alignment=TextAlignmentOptions.MidlineLeft;return label;}
    static void Background(RectTransform rect){var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.025f,.04f,.07f,.91f);image.raycastTarget=false;}
}
