using System;
using B612;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ProceduralTerrainStage2Builder
{
    public const string ScenePath="Assets/B612/Scenes/B612_ProceduralTerrainTest.unity";
    public static object Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Use a saved Edit-mode scene.");
        const string mainPath="Assets/B612/Settings/PlanetTravel/EmptyPlanetGeneration.asset";
        const string testPath="Assets/B612/Settings/PlanetTravel/ProceduralTerrainTestGeneration.asset";
        var main=AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>(mainPath);
        if(!main)throw new InvalidOperationException("Existing generation settings missing.");
        main.proceduralTerrain=true;main.profileId=PlanetGenerationSettings.NaturalProfileId;EditorUtility.SetDirty(main);AssetDatabase.SaveAssetIfDirty(main);
        var test=AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>(testPath);
        if(!test){test=UnityEngine.Object.Instantiate(main);test.name="ProceduralTerrainTestGeneration";AssetDatabase.CreateAsset(test,testPath);}
        test.proceduralTerrain=true;test.profileId=PlanetGenerationSettings.NaturalProfileId;test.nominalRadius=12;test.gravitySurfaceRadius=12;test.influenceRadius=32;EditorUtility.SetDirty(test);AssetDatabase.SaveAssetIfDirty(test);
        if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
        {
            var source=EditorSceneManager.OpenScene("Assets/B612/Scenes/B612_EmptyPlanet.unity");
            EditorSceneManager.SaveScene(source,ScenePath,true);
        }
        var scene=EditorSceneManager.OpenScene(ScenePath);
        var flow=UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();
        if(!flow)throw new InvalidOperationException("Expected existing common planet flow.");
        flow.worldHost.settings=test;flow.randomizeRunSeed=false;flow.runSeed=612101;
        flow.player.cameraRig.pitch=55;flow.player.cameraRig.distance=6.4f;flow.player.respawnDistance=180;
        EditorUtility.SetDirty(flow);EditorUtility.SetDirty(flow.worldHost);EditorUtility.SetDirty(flow.player);EditorUtility.SetDirty(flow.player.cameraRig);
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return new{scene=ScenePath,mainProfile=main.profileId,generatorVersion=PlanetGenerationSettings.GeneratorVersion,testRadius=test.nominalRadius,seed=flow.runSeed};
    }
}
