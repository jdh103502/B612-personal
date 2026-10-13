using UnityEditor;
using UnityEngine;

public static class NightProbe
{
    public static string Shaders()
    {
        string s = "";
        foreach (var n in new[] { "B612/Night Sky", "B612/Glow Particle" })
        {
            var sh = Shader.Find(n);
            if (!sh) { s += n + ": missing\n"; continue; }
            s += n + ": hasError=" + ShaderUtil.ShaderHasError(sh) + "\n";
            foreach (var m in ShaderUtil.GetShaderMessages(sh)) s += "  " + m.severity + " line " + m.line + ": " + m.message + "\n";
        }
        return s;
    }

    public static string OpenDay() { UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/B612/Scenes/B612_Diorama.unity"); return "day"; }
    public static string OpenTurnaround() { UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/B612/Scenes/B612_Turnaround.unity"); return "turnaround"; }
    public static string OpenNight() { UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/B612/Scenes/B612_Night.unity"); return "night"; }

    // Edit-mode look at the far-side grove (reopen the scene afterwards without saving).
    public static string ViewGrove()
    {
        var grove = GameObject.Find("Far Side Grove").transform;
        var cam = Camera.main;
        var up = grove.up;
        var side = Vector3.Cross(up, Vector3.forward).normalized;
        cam.fieldOfView = 40f;
        cam.transform.position = grove.position + up * 3.2f + side * 4.2f + Vector3.Cross(side, up) * 3.2f;
        cam.transform.rotation = Quaternion.LookRotation(grove.position + up * 1.3f - cam.transform.position, up);
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) { ps.Simulate(10f, true, true, false); ps.Pause(); }
        return "grove";
    }

    public static string Keys()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return "no keyboard";
        string s = "";
        foreach (var k in kb.allKeys) if (k.isPressed) s += k.name + " ";
        return "pressed: [" + s + "] devices=" + UnityEngine.InputSystem.InputSystem.devices.Count;
    }

    public static string AnimState()
    {
        var p = GameObject.Find("Player");
        var w = p.GetComponent<B612.GravityWalker>();
        var a = p.GetComponentInChildren<Animator>();
        var st = a.GetCurrentAnimatorStateInfo(0);
        return string.Format("t={0:F1} speed={1:F2} grounded={2} animSpeedParam={3:F2} state={4} animator.speed={5:F2} pos={6}",
            Time.time, w.TangentSpeed, w.Grounded, a.GetFloat("Speed"), st.IsName("Air") ? "Air" : (st.IsName("Locomotion") ? "Locomotion" : "?"), a.speed, p.transform.position.ToString("F2"));
    }

    // Edit-mode contact sheet in the turnaround scene: idle, walk, run and air poses side by side (not saved).
    public static string PoseSheet()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/B612/Scenes/B612_Turnaround.unity");
        var walk = System.Linq.Enumerable.First(System.Linq.Enumerable.OfType<AnimationClip>(AssetDatabase.LoadAllAssetsAtPath("Assets/B612/Models/Meshy/Prince_Walk.fbx")), c => !c.name.StartsWith("__preview"));
        var run = System.Linq.Enumerable.First(System.Linq.Enumerable.OfType<AnimationClip>(AssetDatabase.LoadAllAssetsAtPath("Assets/B612/Models/Meshy/Prince_Run.fbx")), c => !c.name.StartsWith("__preview"));
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/B612/Generated/Prince_Idle.anim");
        var air = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/B612/Generated/Prince_Air.anim");
        var princes = new System.Collections.Generic.List<GameObject>();
        foreach (var go in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (go.name.StartsWith("PrinceRiggedVisual")) princes.Add(go.gameObject);
        princes.Sort((a, b) => b.transform.position.x.CompareTo(a.transform.position.x));
        var fox = GameObject.Find("FoxVisual");
        // reuse the fox slot for a fourth prince
        if (fox) { var extra = Object.Instantiate(princes[0]); extra.transform.position = new Vector3(-2.6f, 0f, 0f); princes.Add(extra); fox.SetActive(false); }
        AnimationClip[] clips = { idle, walk, run, air };
        float[] times = { 0f, walk.length * 0.2f, run.length * 0.3f, 0f };
        for (int i = 0; i < princes.Count && i < 4; i++)
        {
            princes[i].transform.rotation = Quaternion.Euler(0f, 35f, 0f);
            var a = princes[i].GetComponentInChildren<Animator>();
            clips[i].SampleAnimation(a.gameObject, times[i]);
        }
        return "princes=" + princes.Count;
    }

    // Edit-mode look at the house door from the path (reopen the scene afterwards without saving).
    public static string ViewHouse()
    {
        var house = GameObject.Find("Boiler House").transform;
        var cam = Camera.main;
        var door = house.TransformPoint(new Vector3(0f, 0.35f, 0.9f));
        var outward = (house.TransformPoint(new Vector3(0f, 0f, 1f)) - house.position).normalized;
        cam.fieldOfView = 42f;
        cam.transform.position = door + outward * 3.6f + Vector3.up * 1.4f + Vector3.Cross(Vector3.up, outward) * 1.2f;
        cam.transform.rotation = Quaternion.LookRotation(door + Vector3.up * 0.2f - cam.transform.position, Vector3.up);
        return "house";
    }

    // Play mode: put the prince inside the house, standing on the floor.
    public static string GoInside()
    {
        var house = GameObject.Find("Boiler House").transform;
        var p = GameObject.Find("Player");
        var w = p.GetComponent<B612.GravityWalker>();
        w.Teleport(house.TransformPoint(new Vector3(-0.1f, 0.12f, 0.25f)), Vector3.up);
        var f = GameObject.Find("Fox");
        if (f) f.GetComponent<B612.GravityWalker>().Teleport(house.TransformPoint(new Vector3(0.25f, 0.12f, 0.3f)), Vector3.up);
        return "inside? " + B612.HouseInterior.PlayerInside;
    }

    public static string Inside() { return "inside=" + B612.HouseInterior.PlayerInside + " ignoreMask=" + B612.OrbitCamera.IgnoreMask; }

    // Test helpers: make the prince run forward (relative to the camera) or stop, without keyboard input.
    public static string PushForward()
    {
        var p = GameObject.Find("Player");
        p.GetComponent<B612.PlayerController>().enabled = false;
        var w = p.GetComponent<B612.GravityWalker>();
        var cam = Object.FindFirstObjectByType<B612.OrbitCamera>();
        w.moveInput = cam.PlanarDirection(new Vector2(0.3f, 1f).normalized, w.Up);
        return "running";
    }

    public static string Stop()
    {
        var p = GameObject.Find("Player");
        var w = p.GetComponent<B612.GravityWalker>();
        w.moveInput = Vector3.zero;
        p.GetComponent<B612.PlayerController>().enabled = true;
        return "stopped";
    }

    // Edit-mode fox sheet in the turnaround scene: trot (two phases), leap and sit, seen from the side (not saved).
    public static string FoxPoseSheet()
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/B612/Scenes/B612_Turnaround.unity");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/B612/Generated/Prefabs/FoxRigVisual.prefab");
        foreach (var go in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (go.parent == null && (go.name.StartsWith("PrinceRiggedVisual") || go.name.StartsWith("FoxRigVisual"))) go.gameObject.SetActive(false);
        float[] runs = { 1f, 1f, 0f, 0f }, airs = { 0f, 0f, 1f, 0f }, sits = { 0f, 0f, 0f, 1f }, phases = { 1.57f, 4.71f, 0f, 0f };
        for (int i = 0; i < 4; i++)
        {
            var f = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            f.transform.position = new Vector3(1.4f - i * 1.0f, 0f, 0f);
            f.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
            f.transform.localScale = Vector3.one * 1.3f;
            var a = f.AddComponent<B612.FoxRigAnimator>();
            a.model = f.transform;
            a.PreviewPose(runs[i], airs[i], sits[i], phases[i]);
        }
        return "foxes posed";
    }

    public static string Background()
    {
        Application.runInBackground = true;
        return "runInBackground=" + Application.runInBackground;
    }

    public static string State()
    {
        string s = "t=" + Time.time.ToString("F1") + " fps~" + (1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f)).ToString("F0");
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) s += " | " + ps.name + "=" + ps.particleCount;
        s += " | meteorA=" + Shader.GetGlobalVector("_MeteorA").w.ToString("F2") + " meteorB=" + Shader.GetGlobalVector("_MeteorB").w.ToString("F2");
        s += " | skyTime=" + Shader.GetGlobalFloat("_NightSkyTime").ToString("F1");
        var cam = Camera.main;
        foreach (var n in new[] { "_MeteorA", "_MeteorB" })
        {
            var m = Shader.GetGlobalVector(n); var md = Shader.GetGlobalVector(n + "Dir");
            var start = new Vector3(m.x, m.y, m.z);
            var head = Quaternion.AngleAxis(m.w * md.w * Mathf.Rad2Deg, Vector3.Cross(start, new Vector3(md.x, md.y, md.z))) * start;
            s += "\n" + n + " p=" + m.w.ToString("F2") + " arc=" + md.w.ToString("F3") + " startVP=" + cam.WorldToViewportPoint(cam.transform.position + start).ToString("F2") + " headVP=" + cam.WorldToViewportPoint(cam.transform.position + head).ToString("F2");
        }
        return s;
    }

    // Play-mode capture helper: launch a shooting star that is mid-flight right now.
    public static string Meteor()
    {
        var ns = Object.FindFirstObjectByType<B612.NightSky>();
        if (!ns) return "no NightSky";
        var keep = ns.meteorDuration;
        ns.meteorDuration = new Vector2(14f, 14f);
        ns.Launch(0.2f);
        ns.meteorDuration = keep;
        return "launched";
    }

    // Edit-mode capture helper: settle the particle systems and park a shooting star in view.
    public static string Prepare()
    {
        foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
        {
            ps.Simulate(12f, true, true, false);
            ps.Pause();
        }
        var cam = Camera.main;
        var start = cam.ViewportPointToRay(new Vector3(0.42f, 0.93f, 0f)).direction;
        var dir = Vector3.ProjectOnPlane(cam.transform.TransformDirection(new Vector3(-0.8f, -0.55f, 0f)), start).normalized;
        Shader.SetGlobalVector("_MeteorA", new Vector4(start.x, start.y, start.z, 0.55f));
        Shader.SetGlobalVector("_MeteorADir", new Vector4(dir.x, dir.y, dir.z, 0.12f));
        Shader.SetGlobalVector("_MeteorB", new Vector4(0, 1, 0, -1));
        Shader.SetGlobalFloat("_NightSkyTime", 0f);
        return "prepared";
    }
}
