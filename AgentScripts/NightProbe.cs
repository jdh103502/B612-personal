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

    // Red vertices of the prince: histogram of height above the hips vs distance from the neck axis (in body heights).
    public static string ScarfStats()
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/B612/Models/Meshy/Prince_Walk.fbx"));
        var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
        var m = smr.sharedMesh; var bones = smr.bones; var bind = m.bindposes;
        System.Func<string, int> idx = n => System.Array.FindIndex(bones, b => b && b.name == n);
        System.Func<int, Vector3> at = i => bind[i].inverse.MultiplyPoint3x4(Vector3.zero);
        Vector3 hips = at(idx("Hips")), neck = at(idx("neck")), top = at(idx("head_end"));
        var up = (top - hips).normalized; float H = Vector3.Dot(top - hips, up);
        var tex = new Texture2D(2, 2); tex.LoadImage(System.IO.File.ReadAllBytes(Application.dataPath + "/B612/Models/Meshy/Prince_BaseColor.png"));
        var v = m.vertices; var uv = m.uv;
        var grid = new int[12, 8];
        for (int i = 0; i < v.Length; i++)
        {
            var c = tex.GetPixelBilinear(uv[i].x, uv[i].y);
            if (!(c.r > 0.45f && c.g < 0.35f && c.b < 0.32f && c.r - c.g > 0.25f)) continue;
            float h = Vector3.Dot(v[i] - hips, up) / H;
            var d = v[i] - neck; float r = (d - up * Vector3.Dot(d, up)).magnitude / H;
            int hi = Mathf.Clamp(Mathf.FloorToInt((h + 0.2f) * 10f), 0, 11), ri = Mathf.Clamp(Mathf.FloorToInt(r * 20f), 0, 7);
            grid[hi, ri]++;
        }
        Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
        var sb = new System.Text.StringBuilder("rows: height above hips in H, from -0.2 up by 0.1 | cols: radial 0..0.4 by 0.05\n");
        for (int hi = 11; hi >= 0; hi--)
        {
            sb.Append((hi * 0.1f - 0.2f).ToString("F1").PadLeft(5)).Append(": ");
            for (int ri = 0; ri < 8; ri++) sb.Append(grid[hi, ri].ToString().PadLeft(6));
            sb.Append("\n");
        }
        return sb.ToString();
    }

    // Play mode: park the camera close beside the prince (turns the orbit camera off; side = 1 right, -1 left, 0 behind).
    public static string CloseUp() { return Close(1f); }
    public static string CloseUpBack() { return Close(0f); }
    static string Close(float side)
    {
        var p = GameObject.Find("Player").transform;
        var w = p.GetComponent<B612.GravityWalker>();
        var cam = Camera.main;
        var oc = cam.GetComponent<B612.OrbitCamera>();
        if (oc) oc.enabled = false;
        var focus = p.position + w.Up * 0.45f;
        var dir = (p.right * side - p.forward * (1f - Mathf.Abs(side)) + w.Up * 0.35f).normalized;
        cam.fieldOfView = 40f;
        cam.transform.position = focus + dir * 1.8f;
        cam.transform.rotation = Quaternion.LookRotation(focus - cam.transform.position, w.Up);
        return "close-up";
    }

    public static string SkyState()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var sky = RenderSettings.skybox;
        var cam = Camera.main;
        return "scene=" + scene.path + " skybox=" + (sky ? sky.name + " shader=" + sky.shader.name + " noise=" + (sky.GetTexture("_Noise3D") ? "ok" : "MISSING") : "none")
               + " cam.clear=" + (cam ? cam.clearFlags.ToString() : "no camera");
    }

    // Point Unity's external script editor at VS Code and regenerate the .sln/.csproj files it reads.
    public static string UseVSCode()
    {
        string code = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData) + "\\Programs\\Microsoft VS Code\\Code.exe";
        if (!System.IO.File.Exists(code)) return "Code.exe not found";
        Unity.CodeEditor.CodeEditor.SetExternalScriptEditor(code);
        Unity.CodeEditor.CodeEditor.CurrentEditor.SyncAll();
        return "editor=" + Unity.CodeEditor.CodeEditor.CurrentEditorInstallation + " | sln exists=" + System.IO.File.Exists("B612.sln");
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
