using System;
using B612;
using UnityEditor;
using UnityEngine;

public static class TerrainAppearanceBuilder
{
    const string MaterialPath = "Assets/B612/Settings/PlanetTravel/StylizedPlanetGround.mat";

    public static object Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        var material = GetOrCreateMaterial();
        var water = GetOrCreateTemplate("ShallowRiver", "Universal Render Pipeline/Lit");
        var guides = GetOrCreateTemplate("RouteGuide", "Universal Render Pipeline/Unlit");
        foreach (string path in new[]
        {
            "Assets/B612/Settings/Chapters/ChapterJourneyGeneration.asset",
            "Assets/B612/Settings/PlanetTravel/EmptyPlanetGeneration.asset",
            "Assets/B612/Settings/PlanetTravel/ProceduralTerrainTestGeneration.asset"
        })
        {
            var settings = AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>(path);
            if (!settings) throw new InvalidOperationException("Missing " + path);
            if (!settings.proceduralGroundMaterial) settings.proceduralGroundMaterial = material;
            if (!settings.proceduralWaterMaterial) settings.proceduralWaterMaterial = water;
            if (!settings.routeGuideMaterial) settings.routeGuideMaterial = guides;
            settings.terrainAppearance ??= new TerrainAppearanceSettings();
            if (settings.profileId == "terrain.stylized.v1")
            {
                var keys = settings.terrainAppearance.macroKeys;
                if (keys != null && keys.Length == 4 && keys[1] == new Vector2(.3f, -.12f) &&
                    keys[2] == new Vector2(.65f, .12f))
                    settings.terrainAppearance.macroKeys = new TerrainAppearanceSettings().macroKeys;
            }
            settings.profileId = PlanetGenerationSettings.StylizedProfileId;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }
        return new { material = MaterialPath, passes = material.passCount, generator = PlanetGenerationSettings.GeneratorVersion };
    }

    static Material GetOrCreateMaterial()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/B612/Shaders/PlanetGround.shader");
        var paper = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/B612/Generated/Textures/Paper_Albedo.png");
        if (!shader || !paper || ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("Check the planet ground shader and paper texture.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material) return material;
        material = new Material(shader) { name = "Stylized Planet Ground" };
        material.SetTexture("_Paper", paper);
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    static Material GetOrCreateTemplate(string name, string shaderName)
    {
        string path = "Assets/B612/Settings/PlanetTravel/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        var shader = Shader.Find(shaderName);
        if (!shader) throw new InvalidOperationException("Missing " + shaderName);
        material = new Material(shader) { name = name };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
