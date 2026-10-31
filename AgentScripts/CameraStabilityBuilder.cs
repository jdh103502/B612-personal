using System;
using B612;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CameraStabilityBuilder
{
    public static object Apply()
    {
        var scene=SceneManager.GetActiveScene();
        if(EditorApplication.isPlayingOrWillChangePlaymode || scene.path!="Assets/B612/Scenes/B612_EmptyPlanet.unity")
            throw new InvalidOperationException("Open B612_EmptyPlanet in Edit mode.");
        var camera=Camera.main.GetComponent<OrbitCamera>();
        Undo.RecordObject(camera,"Stabilize planet camera");
        camera.distance=5.2f; camera.pitch=30f; camera.focusHeight=.45f; camera.lookAhead=.25f;
        camera.airPullBack=0f; camera.upSmoothTime=.28f; camera.autoTurn=.7f; camera.autoTurnMaxSpeed=25f;
        camera.jumpHeightAllowance=1f; camera.fallHeightAllowance=.45f; camera.sourceConfirmSeconds=.25f;
        camera.referenceTurnSpeed=100f; camera.maxLift=12f; camera.collisionRadius=.2f; camera.occlusionDelay=.2f;
        EditorUtility.SetDirty(camera); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        return new {scene=scene.path,camera.distance,camera.pitch,camera.focusHeight,camera.lookAhead,camera.maxLift,
            camera.jumpHeightAllowance,camera.cardInputMode};
    }
}
