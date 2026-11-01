using System;
using System.Linq;
using B612;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BaobabGrowthBuilder
{
    public static object Build()
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path!="Assets/B612/Scenes/B612_EmptyPlanet.unity")
            throw new InvalidOperationException("Open B612_EmptyPlanet in Edit mode.");
        var type=AssetDatabase.LoadAssetAtPath<BaobabTypeDefinition>("Assets/B612/Settings/BasicBaobab.asset");
        if(!type)throw new InvalidOperationException("Build the threat settings first.");
        Undo.RecordObject(type,"Add baobab growth models");
        type.saplingPrefab=MakeVisual("Sapling","Meshy_AI_Lowpoly_Young_Tree_30k_20261031_texture_remesh_tri",1.05f);
        type.treePrefab=MakeVisual("Tree","Meshy_AI_Lowpoly_Ancient_Tree_30k_20261031_texture_remesh_tri",1.7f);
        type.saplingAtSeconds=20f;type.treeAtSeconds=40f;
        var hand=UnityEngine.Object.FindFirstObjectByType<LambCardHand>();
        Undo.RecordObject(hand,"Set eating time by growth stage");
        hand.secondsPerSprout=7f;hand.secondsPerSapling=10f;hand.secondsPerTree=13f;
        EditorUtility.SetDirty(type);EditorUtility.SetDirty(hand);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return new {scene=scene.path,type.saplingAtSeconds,type.treeAtSeconds,hand.secondsPerSprout,hand.secondsPerSapling,hand.secondsPerTree,
            sapling=Describe(type.saplingPrefab),tree=Describe(type.treePrefab)};
    }
    static object Describe(GameObject prefab)
    {
        var renderers=prefab.GetComponentsInChildren<Renderer>();
        return new {prefab=prefab.name,renderers=renderers.Length,
            vertices=prefab.GetComponentsInChildren<MeshFilter>().Sum(m=>m.sharedMesh?m.sharedMesh.vertexCount:0),
            bounds=renderers.Select(r=>r.bounds.size.ToString()).ToArray()};
    }
    static GameObject MakeVisual(string stage,string modelName,float height)
    {
        string prefabPath="Assets/B612/Prefabs/Baobab"+stage+"Visual.prefab";
        var existing=AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);if(existing)return existing;
        string root="Assets/B612/Models/"+stage;
        var importer=(ModelImporter)AssetImporter.GetAtPath(root+"/"+modelName+".fbx");
        if(!importer)throw new InvalidOperationException("Import the "+stage+" FBX first.");
        importer.importAnimation=false;importer.importCameras=false;importer.importLights=false;importer.addCollider=false;
        importer.materialImportMode=ModelImporterMaterialImportMode.None;
        importer.SaveAndReimport();
        var textureImporter=(TextureImporter)AssetImporter.GetAtPath(root+"/"+modelName+".png");
        textureImporter.sRGBTexture=true;textureImporter.maxTextureSize=2048;textureImporter.SaveAndReimport();
        string materialPath="Assets/B612/Materials/Baobab"+stage+".mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if(!material)
        {
            material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name="Baobab "+stage,enableInstancing=true};
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(root+"/"+modelName+".png"));
            material.SetFloat("_Smoothness",.2f);material.SetFloat("_Metallic",0f);material.SetFloat("_Cull",0f);
            AssetDatabase.CreateAsset(material,materialPath);
        }
        var visual=new GameObject("Baobab "+stage+" Visual");
        try
        {
            var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(root+"/"+modelName+".fbx"),visual.transform);
            var renderers=model.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0)throw new InvalidOperationException("No renderers in "+stage+" FBX.");
            Bounds bounds=renderers[0].bounds;
            foreach(var renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                renderer.sharedMaterials=Enumerable.Repeat(material,renderer.sharedMaterials.Length).ToArray();
            }
            float scale=height/Mathf.Max(bounds.size.y,.0001f);
            model.transform.localScale*=scale;
            model.transform.localPosition-=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)*scale;
            return PrefabUtility.SaveAsPrefabAsset(visual,prefabPath);
        }
        finally {UnityEngine.Object.DestroyImmediate(visual);}
    }
}
