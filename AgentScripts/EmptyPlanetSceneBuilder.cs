using System;
using System.Linq;
using B612;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Executed in the open Editor with Unity CLI run_script; does not enter the game assembly.
public static class EmptyPlanetSceneBuilder
{
    public const string Source = "Assets/B612/Scenes/B612_Night.unity";
    public const string Destination = "Assets/B612/Scenes/B612_EmptyPlanet.unity";

    public static object Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play mode before creating the scene.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("An open scene has unsaved changes; preserving it.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Destination))
            throw new InvalidOperationException("Destination scene already exists; refusing to overwrite.");
        if (!AssetDatabase.CopyAsset(Source, Destination))
            throw new InvalidOperationException("Could not copy the reference scene.");

        var scene = EditorSceneManager.OpenScene(Destination, OpenSceneMode.Single);
        var roots = scene.GetRootGameObjects();
        var world = roots.Single(g => g.name == "B612 World");
        var player = roots.Single(g => g.name == "Player");
        var camera = roots.Single(g => g.name == "Main Camera").GetComponent<Camera>();
        var planet = world.transform.Find("Asteroid B-612");
        var spawn = world.transform.Find("Spawn Point");
        if (!planet || !spawn || !planet.GetComponent<MeshCollider>())
            throw new InvalidOperationException("Reference planet, collision mesh or spawn is missing.");

        foreach (var child in world.GetComponentsInChildren<Transform>(true)
                     .Where(t => t.parent == world.transform && t != planet && t != spawn).ToArray())
            UnityEngine.Object.DestroyImmediate(child.gameObject);
        string[] keep = { "Main Camera", "Moon Key", "Moon Rim", "Global Volume", "B612 World", "Player", "Night Sky" };
        foreach (var root in roots.Where(g => !keep.Contains(g.name)))
            UnityEngine.Object.DestroyImmediate(root);

        var controller = player.GetComponent<PlayerController>();
        var walker = player.GetComponent<GravityWalker>();
        var orbit = camera.GetComponent<OrbitCamera>();
        controller.spawnPoint = spawn;
        controller.cameraRig = orbit;
        controller.NearbyLaunch = null;
        orbit.target = walker;
        orbit.introSeconds = 1.5f;
        player.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

        // A single-planet establishing view, followed by the reference scene's playable orbit camera.
        Vector3 focus = planet.position + Vector3.up * 0.8f;
        camera.transform.position = focus + new Vector3(9f, 8f, 12f);
        camera.transform.rotation = Quaternion.LookRotation(focus - camera.transform.position, Vector3.up);
        camera.fieldOfView = 42f;
        if (SceneView.lastActiveSceneView)
            SceneView.lastActiveSceneView.LookAt(focus, camera.transform.rotation, 7f);

        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("New scene could not be saved.");
        Selection.activeGameObject = player;
        return Inspect();
    }

    public static object Inspect()
    {
        var scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var all = roots.SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var player = all.Select(t => t.GetComponent<PlayerController>()).FirstOrDefault(c => c);
        var planet = all.Select(t => t.GetComponent<GravitySource>()).Single(g => g);
        return new
        {
            scene = scene.path,
            scene.isDirty,
            roots = roots.Select(g => g.name).ToArray(),
            worldChildren = roots.Single(g => g.name == "B612 World").transform.Cast<Transform>().Select(t => t.name).ToArray(),
            gravitySources = all.Count(t => t.GetComponent<GravitySource>()),
            players = all.Count(t => t.GetComponent<PlayerController>()),
            props = all.Count(t => t.GetComponent<Collectible>() || t.GetComponent<LaunchStar>() || t.GetComponent<FoxFollower>() || t.GetComponent<DemoHUD>() || t.GetComponent<HouseInterior>()),
            missingScripts = all.Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)),
            planetCollider = planet.GetComponent<MeshCollider>().sharedMesh != null,
            cameraLinked = player.cameraRig && player.cameraRig.target == player.GetComponent<GravityWalker>(),
            spawnLinked = player.spawnPoint != null,
            animatorLinked = player.GetComponent<PrinceCharacterAnimator>().animator != null
        };
    }
}
