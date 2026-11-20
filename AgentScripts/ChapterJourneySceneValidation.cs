using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using B612;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class ChapterJourneySceneValidation
{
    public static object Start()
    {
        System.IO.Directory.CreateDirectory("Temp/ReviewedCourses");
        System.IO.File.WriteAllText("Temp/ReviewedCourses/Live.txt", "running=True\n");
        new GameObject("Chapter journey validation").AddComponent<ChapterJourneyProbe>();
        // This Editor session stalls its unfocused player loop despite auto-tick. Step the real
        // Unity player loop from Editor updates; gameplay and physics callbacks still execute normally.
        UnityEditor.EditorApplication.update+=Step;
        return "Started Temp/ReviewedCourses/Live.txt";
    }
    static void Step()
    {
        var probe = GameObject.Find("Chapter journey validation")?.GetComponent<ChapterJourneyProbe>();
        if (!Application.isPlaying || !probe) { UnityEditor.EditorApplication.update -= Step; return; }
        UnityEditor.EditorApplication.isPaused=false;
        probe.KeepFocus();
        InputSystem.Update();
        UnityEditor.EditorApplication.Step();
        if (probe) probe.Pump();
        UnityEditor.EditorApplication.isPaused=false;
    }
}
public sealed class ChapterJourneyProbe:MonoBehaviour
{
    StarSystemFlowController flow;DeveloperDebugController debug;GravityWalker motor;Keyboard keyboard,previous;
    static readonly System.Reflection.FieldInfo CameraHeading = typeof(OrbitCamera).GetField("flatForward",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    InputSettings.BackgroundBehavior background;InputSettings.EditorInputBehaviorInPlayMode editorInput;bool oldFocus;
    GameObject originalTemplate;readonly List<string> passed=new List<string>();
    Mesh departingMesh;
    Mesh departingWater;
    Material departingGround;
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    void Input(params Key[] keys)
    {
        keyboard.MakeCurrent();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    }
    Stack<IEnumerator> stack;
    void Start()
    {
        flow=FindFirstObjectByType<StarSystemFlowController>();debug=FindFirstObjectByType<DeveloperDebugController>();motor=flow.player.GetComponent<GravityWalker>();
        previous=Keyboard.current;background=InputSystem.settings.backgroundBehavior;editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
        oldFocus=(bool)typeof(PlayerController).GetField("hasFocus",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(flow.player);
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard=InputSystem.AddDevice<Keyboard>("ChapterJourneyKeyboard");keyboard.MakeCurrent();flow.player.SendMessage("OnApplicationFocus",true);
        stack=new Stack<IEnumerator>();stack.Push(Run());
    }
    public void KeepFocus()
    {
        // CLI activation can briefly send a real focus-loss event during automated Play.
        if (flow) flow.player.SendMessage("OnApplicationFocus", true);
    }
    public void Pump()
    {
        if(stack==null)return;
        while(stack.Count>0)
        {
            object next=null;bool more=false;string error=null;
            try{more=stack.Peek().MoveNext();if(more)next=stack.Peek().Current;}catch(Exception ex){error=ex.ToString();}
            if(error!=null){stack=null;Finish(false,error);return;}
            if(!more){stack.Pop();continue;}if(next is IEnumerator nested){stack.Push(nested);continue;}return;
        }
        stack=null;Finish(true,"");
    }
    IEnumerator Skip()
    {
        debug.Open();Check(debug.RequestBaobabSkip(),"Debug skip rejected");yield return null;yield return null;
        Check(flow.surge.IsComplete&&flow.surge.ActiveSproutCount==0,"Skip incomplete");debug.Close();yield return null;yield return null;
    }
    Vector3 Origin => flow.worldHost.ActiveWorld.transform.position;
    Vector3 Radial => (motor.GetComponent<Rigidbody>().position - Origin).normalized;

    void Aim(Vector3 target)
    {
        // Hold W through the regular player input. Aim the orbit heading at the next waypoint.
        var direction = Vector3.ProjectOnPlane(target - Radial, motor.Up).normalized;
        CameraHeading.SetValue(flow.player.cameraRig, direction);
    }

    IEnumerator Press(Key key)
    {
        Input(key); yield return null; yield return null;
        Input(); yield return null; yield return null;
    }

    IEnumerator Walk(IEnumerable<Vector3> points, string route)
    {
        var plan = flow.worldHost.ActiveWorld.Terrain.GenerationPlan;
        float started = Time.time;
        float lastGrounded = Time.time;
        foreach (Vector3 target in points)
        {
            while (LambCardHand.SurfaceDistance(Origin + Radial * plan.radius, Origin + target * plan.radius, Origin) > .20f)
            {
                if (motor.Grounded) lastGrounded = Time.time;
                Check(Time.time - lastGrounded < .25f && !flow.surge.IsGameOver, "Lost support on " + route);
                if (Time.time - started >= 55f)
                {
                    var focus = typeof(PlayerController).GetField("hasFocus", System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic).GetValue(flow.player);
                    throw new InvalidOperationException($"Walk timed out on {route}: radial={Radial}, target={target}, " +
                        $"W={keyboard.wKey.isPressed}, move={motor.moveInput}, speed={motor.TangentSpeed}, " +
                        $"focus={focus}, modal={GameplayModalInput.IsOpen}");
                }
                Aim(target);
                Input(Key.W);
                yield return null;
            }
        }
        Input();
        float settle = Time.time + .22f;
        while (Time.time < settle) yield return null;
        Check(motor.Grounded, "Did not finish on the ground: " + route);
    }

    IEnumerator FollowMain(Vector3 target)
    {
        var plan = flow.worldHost.ActiveWorld.Terrain.GenerationPlan;
        float Angle(Vector3 n) => Mathf.Atan2(Vector3.Dot(n, plan.routeTangent), Vector3.Dot(n, plan.spawn));
        float from = Mathf.Max(0f, Angle(Radial));
        float to = Angle(target);
        int count = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(to - from) * plan.radius / 1.4f));
        var points = new List<Vector3>();
        for (int i = 1; i <= count; i++)
        {
            float angle = Mathf.Lerp(from, to, i / (float)count);
            points.Add(plan.spawn * Mathf.Cos(angle) + plan.routeTangent * Mathf.Sin(angle));
        }
        yield return Walk(points, "main route");
    }

    IEnumerator Visit(int index)
    {
        var sites = flow.Sites;
        var p = sites.Positions[index];
        yield return FollowMain((p - Origin).normalized);
        Check(motor.Grounded&&sites.CanInteract(index),"Cannot interact with reserved site "+index+" at "+Vector3.Distance(flow.player.transform.position,p));
        yield return Press(Key.E);
        Check((flow.Current.state.visitedSites&(1<<index))!=0,"Real E did not visit site "+index);
    }

    IEnumerator Shortcut(ProceduralPlanetTerrain.Plan plan, ProceduralPlanetTerrain.Feature feature)
    {
        int kind = feature.kind;
        var goal = TraversalTestCourseGeometry.Goal(kind);
        Vector3 target = feature.Direction(goal, plan.radius);
        float threshold = kind == 0 ? -2.4f : kind == 1 ? -1f : -2.3f;
        float start = Time.time, launch = -1f;
        bool jumped = false, released = false, second = false, abilityUsed = false;
        int groundJumps = 0, airJumps = 0, recoveries = 0;
        Action takeoff = () => { groundJumps++; launch = Time.fixedTime; };
        Action airJump = () => airJumps++;
        Action landed = () => recoveries++;
        motor.Jumped += takeoff;
        motor.Traversal.AirJumped += airJump;
        motor.Traversal.LandingRecovered += landed;
        try
        {
            while (Time.time - start < 10f)
            {
                Vector2 local = feature.Coordinates(Radial, plan.radius);
                float altitude = Vector3.Distance(motor.GetComponent<Rigidbody>().position, Origin) - plan.radius;
                abilityUsed |= kind == 0 ? motor.Traversal.IsDashing :
                    kind == 1 ? airJumps > 0 : motor.Traversal.IsGliding;
                float height = TraversalTestCourseGeometry.Height(kind, goal.x, goal.y);
                if (abilityUsed && motor.Grounded && recoveries == 1 && Mathf.Abs(altitude - height) < .08f &&
                    Vector2.Distance(local, goal) < .42f)
                {
                    Check(groundJumps == 1 && airJumps == (kind == 1 ? 1 : 0), "Unexpected jump count");
                    passed.Add($"Chapter {kind + 1}: ability used and stable landing at ({local.x:F2}, {local.y:F2}), height {altitude:F2}m, {Time.time - start:F2}s");
                    yield break;
                }

                if (!jumped && motor.Grounded && local.x >= threshold) jumped = true;
                float flight = launch < 0f ? 0f : Time.time - launch;
                var keys = new List<Key> { Key.W };
                if (jumped)
                {
                    if (kind == 1 && flight >= .64f && !released) released = true;
                    else
                    {
                        if (kind != 1 || !released || flight >= .70f) keys.Add(Key.Space);
                    }
                    if (kind == 0 && !second && flight >= .64f)
                    {
                        second = true;
                        keys.Add(Key.LeftShift);
                    }
                }
                Aim(target);
                Input(keys.ToArray());
                yield return null;
            }
            throw new InvalidOperationException($"Live shortcut failed for kind {kind}. Position {feature.Coordinates(Radial, plan.radius)}, grounded {motor.Grounded}, ability {abilityUsed}, jumps {groundJumps}/{airJumps}, recoveries {recoveries}");
        }
        finally
        {
            motor.Jumped -= takeoff;
            motor.Traversal.AirJumped -= airJump;
            motor.Traversal.LandingRecovered -= landed;
            Input();
        }
    }
    IEnumerator Transition(string id,int chapter)
    {
        float until=Time.realtimeSinceStartup+35;
        while(flow.Progress.chapter!=chapter||flow.Current.spec.planetId!=id||flow.IsTravelling)
        {yield return null;Check(Time.realtimeSinceStartup<until,"Transition timed out: "+id);}
        yield return null;yield return null;
        Check(!flow.surge.IsGameOver&&flow.WorldReady,"Arrival failed");
        Check(!departingMesh && !departingWater && !departingGround, "Departed terrain mesh or material leaked");
    }
    void RememberDeparture()
    {
        var world = flow.worldHost.ActiveWorld;
        departingMesh = world.Terrain.OwnedMesh;
        departingWater = world.Terrain.WaterMesh;
        departingGround = world.groundRenderer.sharedMaterial;
    }
    IEnumerator Graze()
    {
        var world = flow.worldHost.ActiveWorld;
        var root = flow.surge.Spawned.First(record => record.IsActive);
        Vector3 direction = (world.surface.WorldPoint(root.slot) - Origin).normalized;
        Vector3 tangent = Vector3.Cross(direction, Vector3.up).normalized;
        if (tangent.sqrMagnitude < .1f) tangent = Vector3.Cross(direction, Vector3.right).normalized;
        Check(world.surface.TryProject((direction + tangent / world.Data.spec.terrain.radius).normalized,
            out var point, out var normal), "Missing sheep placement ground");
        motor.Teleport(point + normal * .04f, normal);
        float until = Time.time + .4f;
        while (Time.time < until) yield return null;
        Check(motor.Grounded, "Sheep setup lost ground");
        yield return Press(Key.Q);
        var lamb = flow.hand.Deployed.Single();
        Check(lamb.Target == root && flow.hand.InHandCount == flow.hand.CardCount - 1, "Sheep did not claim the live root");
        until = Time.time + 9f;
        while (root.IsActive)
        {
            Check(Time.time < until && !flow.surge.IsGameOver, "Sheep did not remove the root");
            yield return null;
        }
        Check(lamb.SproutsEaten > 0, "Root disappeared without grazing");
        passed.Add("A summoned sheep claimed and ate an active root on a normal campaign planet");
    }
    IEnumerator Run()
    {
        Check(flow.CampaignEnabled&&flow.Progress.chapter==1&&flow.Progress.unlocked==TraversalCapability.None,"Not a fresh campaign");
        Check(flow.gameObject.scene.buildIndex == 0, "Campaign is not build scene 0");
        passed.Add($"Scene {flow.gameObject.scene.path}, build index 0, run seed {flow.Progress.journeySeed}");
        debug.Open();yield return null;
        Check(!debug.hud.dashToggle.interactable&&!debug.hud.airJumpToggle.interactable&&!debug.hud.glideToggle.interactable,"Locked debug toggle enabled");
        Check(!motor.Traversal.IsEnabled(TraversalFeature.Dash),"Locked dash effective");
        passed.Add("Fresh campaign locks abilities independently of developer allow flags");
        for(int chapter=1;chapter<=3;chapter++)
        {
            var world = flow.worldHost.ActiveWorld;
            var mesh = world.Terrain.OwnedMesh;
            var plan = world.Terrain.GenerationPlan;
            var entry = ChapterTerrainDefinition.Entry(chapter);
            Check(plan.requiredKind == chapter - 1 && plan.features[0].kind == plan.requiredKind && plan.connectors.Count == 2,
                "Missing chapter start terrain or connectors");
            Check(((MeshCollider)world.ground).sharedMesh == mesh, "Visible terrain and collision differ");
            Check(flow.surge.Spawned.Any(record => record.IsActive) && flow.surge.Spawned.Where(record => record.IsActive)
                .All(record => world.Terrain.AllowsSpawn(world.surface.WorldPoint(record.slot), world.surface.WorldNormal(record.slot))),
                "Baobab inside protected course or not spawned");
            var course = plan.features[0];
            var entryRoad = plan.connectors[0];
            var exitRoad = plan.connectors[1];
            Check(flow.Progress.unlocked==entry,"Wrong entry capabilities "+chapter);
            if (!flow.surge.WasSkippedByDebug) yield return Skip();
            yield return Press(Key.M);
            Check(flow.IsMapOpen, "M did not open the map");
            foreach(var link in flow.SystemData.links.Where(l=>l.fromPlanetId==flow.Current.spec.planetId))Check(!flow.CanTravel(link.toPlanetId),"Travel bypassed unlock beacon");
            yield return Press(Key.M);
            Check(!flow.IsMapOpen, "M did not close the map");

            yield return FollowMain(entryRoad.anchor);
            yield return Walk(new[] { entryRoad.pad }, "entry connector");
            float start = Time.time;
            yield return Walk(ProceduralPlanetTerrain.FeatureRoute(plan, course).Skip(1), "basic bypass");
            passed.Add($"Chapter {chapter}: planet seed {plan.seed}, kind {course.kind}, layout {plan.reviewedLayout}, fixed {plan.fixedPlacement}, builds {plan.buildAttempts}, basic bypass {Time.time - start:F2}s");
            yield return Walk(new[] { exitRoad.anchor }, "return connector");

            yield return Visit(0);
            var expected=entry|ChapterTerrainDefinition.Reward(chapter);Check(flow.Progress.unlocked==expected&&motor.Traversal.Unlocked==expected,"Reward not granted");
            Check(flow.worldHost.ActiveWorld.Terrain.OwnedMesh==mesh,"Unlock regenerated terrain");
            Check(!flow.RequestSiteInteraction(flow.Sites,0),"Duplicate reward accepted");
            yield return FollowMain(entryRoad.anchor);
            yield return Walk(new[] { entryRoad.pad }, "entry from unlock beacon");
            yield return Shortcut(plan, course);
            yield return Walk(new[] { exitRoad.anchor }, "return after landing");
            yield return Visit(1);
            yield return Visit(2);
            Check(flow.Current.state.siteVisitSeconds.All(t=>t>0),"Encounter cost timestamps missing");
            if(chapter==1)
            {
                debug.Open();yield return null;debug.hud.dashToggle.isOn=false;Check(!motor.Traversal.IsEnabled(TraversalFeature.Dash),"Developer disable ignored");
                debug.Close();yield return null;yield return null;
                yield return Press(Key.R);
                Check(flow.Progress.unlocked==expected&&!motor.Traversal.AllowDash,"Respawn lost progression or developer override");
                Check(Vector3.Distance(motor.transform.position, world.spawn.position) < .25f, "R did not respawn at the start");
                debug.Open();debug.hud.dashToggle.isOn=true;debug.Close();yield return null;yield return null;
            }
            passed.Add("Chapter "+chapter+": actual E unlock, reachable sites, immutable terrain and no repeated grant");
            for(int hop=0;hop<3;hop++)
            {
                yield return Press(Key.M);
                Check(flow.IsMapOpen,"Map failed after unlock");var choices=flow.SystemData.links.Where(l=>l.fromPlanetId==flow.Current.spec.planetId).ToArray();
                string next=choices[(chapter+hop)%choices.Length].toPlanetId;
                RememberDeparture();
                Check(flow.RequestTravel(next),"Branch travel rejected");
                yield return Transition(next,chapter);Check(flow.Progress.unlocked==expected,"Travel lost unlock");
                if (chapter == 1 && hop == 0) yield return Graze();
                yield return Skip();
                Check(flow.hand.Deployed.Count == 0 && flow.hand.InHandCount == flow.hand.CardCount, "Sheep cards not recalled");
            }
            Check(flow.SystemComplete&&flow.OpenMap(),"Chapter destination not complete");
            if(chapter<3)
            {
                Check(flow.CanAdvanceChapter&&flow.view.nextChapterButton.interactable,"Next chapter UI blocked");
                if(chapter==1)
                {
                    string system=flow.SystemData.systemId;int path=flow.Progress.chosenPath.Count;var departure=flow.worldHost.ActiveWorld;
                    var settings=flow.worldHost.settings;originalTemplate=settings.groundTemplate;settings.groundTemplate=null;
                    Check(flow.RequestNextChapter(),"Failed transition did not queue");yield return null;
                    float until=Time.realtimeSinceStartup+12;while(flow.IsTravelling){yield return null;Check(Time.realtimeSinceStartup<until,"Rollback timeout");}
                    settings.groundTemplate=originalTemplate;originalTemplate=null;
                    Check(flow.IsMapOpen&&flow.SystemData.systemId==system&&flow.Progress.chapter==chapter&&flow.Progress.chosenPath.Count==path&&flow.worldHost.ActiveWorld==departure&&departure.Terrain.OwnedMesh,"Cross-chapter failure changed live state");
                    passed.Add("Failed next-chapter preparation preserves current world, chapter, path and unlocks");
                }
                RememberDeparture();
                Check(flow.RequestNextChapter(),"Next chapter request rejected");
                yield return Transition("system_"+(chapter+1).ToString("00")+"_Start",chapter+1);
                Check(!mesh,"Departed terrain mesh leaked");
            }
        }
        Check(flow.Progress.journeyComplete&&flow.Progress.unlocked==TraversalCapability.All&&flow.CompletedSystems.Count==2&&flow.Progress.chosenPath.Count==12,"Journey history incomplete");
        Check(!flow.CanAdvanceChapter&&!flow.RequestNextChapter(),"Unexpected fourth chapter");
        Check(FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length==1,"Duplicate EventSystem");
        passed.Add("Three chapters, alternate DAG routes, 11 transitions, persistent unlocks, complete history, terminal state and single EventSystem");flow.CloseMap();
        passed.Add("Departed ground and water meshes and owned ground materials released on every transition");
    }
    void Finish(bool success,string error)
    {
        if(originalTemplate)flow.worldHost.settings.groundTemplate=originalTemplate;
        if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(previous!=null&&previous.added)previous.MakeCurrent();
        InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;flow.player.SendMessage("OnApplicationFocus",oldFocus);
        System.IO.File.WriteAllText("Temp/ReviewedCourses/Live.txt","passed="+success+"\nerror="+error+"\n"+string.Join("\n",passed));
        if(!success)Debug.LogError(error);if(!flow.IsMapOpen)debug.Open();Destroy(gameObject);
    }
}
