using System;
using System.Linq;
using B612;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PlayerTraversalBuilder
{
    public static object Build()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/B612/Scenes/B612_EmptyPlanet.unity")
            throw new InvalidOperationException("Open B612_EmptyPlanet in Edit mode.");
        var player = UnityEngine.Object.FindFirstObjectByType<PlayerController>();
        if (!player) throw new InvalidOperationException("No player controller in the scene.");
        var abilities = player.GetComponent<PlayerTraversalAbilities>();
        if (!abilities) abilities = Undo.AddComponent<PlayerTraversalAbilities>(player.gameObject);
        var rig=player.cameraRig;
        if(rig)
        {
            Undo.RecordObject(rig,"Frame extended traversal");
            rig.traversalHeightAllowance=.2f;rig.traversalVerticalSharpness=10f;EditorUtility.SetDirty(rig);
        }
        EditorUtility.SetDirty(abilities);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        return new {scene=scene.path,player=player.name,abilities.doubleJumpSpeed,abilities.dashSpeed,abilities.dashDuration,
            abilities.glideBudgetSeconds,foxHasAbilities=UnityEngine.Object.FindObjectsByType<FoxFollower>(FindObjectsSortMode.None).Any(f=>f.GetComponent<PlayerTraversalAbilities>()),
            count=UnityEngine.Object.FindObjectsByType<PlayerTraversalAbilities>(FindObjectsSortMode.None).Length};
    }
}
