using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using B612;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

public static class StarSystemMapTravelSceneValidation
{
    public static object Start()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Run in Play mode.");
        var root=new GameObject("Star system live validation");UnityEngine.Object.DontDestroyOnLoad(root);root.AddComponent<StarSystemTravelProbe>();
        return new{started=true,output="Temp/StarSystemMapTravelSceneValidation.txt"};
    }
    public static object StartDensity()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Run in Play mode.");
        var root=new GameObject("Planet tile density travel validation");UnityEngine.Object.DontDestroyOnLoad(root);root.AddComponent<StarSystemTravelProbe>().verifyDensity=true;
        return new{started=true,output="Temp/PlanetTileDensityTravelValidation.txt"};
    }
}
[DefaultExecutionOrder(1500)]
public sealed class StarSystemTravelProbe : MonoBehaviour
{
    public bool verifyDensity;
    StarSystemFlowController flow;
    BaobabSurge surge;
    PlayerController player;
    GravityWalker motor;
    LambCardHand hand;
    PlanetGenerationSettings settings;
    Vector3 originalSpawnDirection;
    Keyboard keyboard,oldKeyboard;Mouse mouse,oldMouse;
    InputSettings.BackgroundBehavior oldBackground;
    InputSettings.EditorInputBehaviorInPlayMode oldEditor;
    bool oldFocus,finished;
    UnityEngine.Object[] densitySelection;
    UnityEditor.SceneView densitySceneView;
    bool originalDrawGizmos;
    readonly List<string> passed=new List<string>();
    float started;
    void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    void Bind(){flow=UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();surge=flow.surge;player=flow.player;motor=player.GetComponent<GravityWalker>();hand=flow.hand;}
    IEnumerator Start()
    {
        started=Time.realtimeSinceStartup;Bind();settings=flow.worldHost.settings;originalSpawnDirection=settings.spawnDirection;
        oldFocus=(bool)typeof(PlayerController).GetField("hasFocus",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(player);player.SendMessage("OnApplicationFocus",true);
        oldKeyboard=Keyboard.current;oldMouse=Mouse.current;oldBackground=InputSystem.settings.backgroundBehavior;oldEditor=InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard=InputSystem.AddDevice<Keyboard>("StarTravelValidationKeyboard");mouse=InputSystem.AddDevice<Mouse>("StarTravelValidationMouse");InputSystem.EnableDevice(keyboard);InputSystem.EnableDevice(mouse);keyboard.MakeCurrent();mouse.MakeCurrent();
        var routine=Run();
        while(true)
        {
            object current;
            try{if(!routine.MoveNext())break;current=routine.Current;}
            catch(Exception ex){Finish(ex.Message);yield break;}
            yield return current;
        }
        Finish("");
    }
    void Keys(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));}
    void MouseAt(Vector2 point,bool down=false,Vector2 delta=default,Vector2 scroll=default)
    {InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=(ushort)(down?1:0),delta=delta,scroll=scroll});}
    Vector2 Node(string id)=>RectTransformUtility.WorldToScreenPoint(null,flow.view.NodeRect(id).position);
    string Specs()=>string.Join("/",flow.SystemData.planets.Select(p=>JsonUtility.ToJson(p.spec)));
    float Deadline()=>Time.realtimeSinceStartup+5f;
    void Waiting(float deadline,string name){Check(Time.realtimeSinceStartup<deadline,"Timeout: "+name);}
    void CompleteCurrent(){surge.durationSeconds=1;surge.Advance(1f);}
    IEnumerator Run()
    {
        Check(flow.WorldReady&&flow.SystemData.planets.Count==6,"Common scene did not initialize");
        if(verifyDensity)
        {
            foreach(var pair in new[]{new{id="system_01_B",size=2f},new{id="system_01_E",size=.7f},new{id="system_01_Destination",size=1f}})
            {
                var planet=flow.SystemData.Find(pair.id);planet.spec.terrain.sizeScale=pair.size;planet.spec.terrain.radius=settings.nominalRadius*pair.size;planet.spec.sizeClass=PlanetGenerationSettings.Classify(pair.size);
            }
            Check(flow.SystemData.planets.All(p=>p.spec.generatorVersion==PlanetGenerationSettings.GeneratorVersion),
                "New journey did not use the current generator version");
        }
        Check(surge.Attempts==1&&surge.ActiveSproutCount==1,"Initial surge began more than once");
        surge.threat.ResetForRun();surge.durationSeconds=1.2f;surge.targetSprouts=6;surge.Begin();player.Respawn();Keys();
        foreach(var record in surge.Spawned.ToArray())surge.RemoveSprout(record.instance);
        Check(surge.ActiveSproutCount==0&&!surge.IsComplete,"Zero-sprout setup failed");
        Keys(Key.M);yield return new WaitForSecondsRealtime(.1f);Check(!flow.IsMapOpen&&!flow.CanOpenMap,"Zero active sprouts unlocked map early");Keys();
        float deadline=Deadline();while(!surge.IsComplete){Waiting(deadline,"spawn end");yield return null;}yield return null;
        Check(surge.ActiveSproutCount>0&&flow.CanOpenMap&&flow.UnlockNotificationCount==1,"Completion/unlock policy");
        Check(flow.Current.state.phase==PlanetPhase.Clearing,"Unlock incorrectly marked ready/departed");
        string specs=Specs();Keys(Key.M);deadline=Deadline();while(!flow.IsMapOpen){Waiting(deadline,"M open");yield return null;}
        Keys();yield return null;
        float simulation=surge.SimulationSeconds,clock=Time.fixedTime,glide=motor.Traversal.GlideSecondsRemaining;int selected=hand.SelectedIndex,jumpId=motor.Traversal.JumpRequestId,dashId=motor.Traversal.DashRequestId;
        Vector3 position=motor.GetComponent<Rigidbody>().position,cameraPosition=player.cameraRig.transform.position;
        string b="system_01_B",c="system_01_C",d="system_01_D";
        MouseAt(Node(d));yield return new WaitForSecondsRealtime(.1f);
        Check(flow.view.HoveredPlanetId==d&&flow.view.tooltipLabel.text=="행성 크기: "+PlanetMapPreview.From(flow.SystemData.Find(d)).sizeLabel,"Locked node tooltip failed");
        Check(!flow.CanTravel(d)&&!flow.RequestTravel(d)&&!flow.RequestTravel(flow.Progress.currentPlanetId),"Non-outgoing/current node accepted");
        MouseAt(Node(d),true);yield return new WaitForSecondsRealtime(.05f);MouseAt(Node(d));
        Keys(Key.W,Key.Space,Key.LeftShift,Key.Q,Key.R,Key.Digit3,Key.F12);
        MouseAt(Node(d),false,new Vector2(70,30),new Vector2(0,120));yield return new WaitForSecondsRealtime(.2f);
        Check(flow.IsMapOpen&&!(DeveloperDebugController.Active&&DeveloperDebugController.Active.IsOpen),"Map and debug overlapped");
        Check(Time.timeScale==0&&Time.fixedTime==clock&&surge.SimulationSeconds==simulation&&motor.Traversal.GlideSecondsRemaining==glide,"Map clocks progressed");
        Check(motor.GetComponent<Rigidbody>().position==position&&Vector3.Distance(player.cameraRig.transform.position,cameraPosition)<.001f&&hand.SelectedIndex==selected,"Map gameplay/camera input leaked");
        Check(motor.Traversal.JumpRequestId==jumpId&&motor.Traversal.DashRequestId==dashId,"Map buffered skill input");Keys();
        foreach(var resolution in new[]{new Vector2(1280,720),new Vector2(1920,1080)})
        {
            var scaler=flow.view.canvasRect.GetComponent<CanvasScaler>();scaler.referenceResolution=resolution;Canvas.ForceUpdateCanvases();
            foreach(var corner in new[]{new Vector2(2,2),new Vector2(Screen.width-2,Screen.height-2)})
            {
                flow.view.Hover(d,corner);var corners=new Vector3[4];flow.view.tooltip.GetWorldCorners(corners);
                Check(corners.All(p=>p.x>=0&&p.x<=Screen.width&&p.y>=0&&p.y<=Screen.height),"Tooltip escaped canvas under scaling");
            }
        }
        flow.view.canvasRect.GetComponent<CanvasScaler>().referenceResolution=new Vector2(1600,900);Canvas.ForceUpdateCanvases();flow.view.HideTooltip();
        Check(Specs()==specs,"Hover/reopen changed future planet specs");
        passed.Add("M locked even with zero sprouts; completion unlocks once with roots remaining; actual hover/click, input/clock freeze, F12 exclusion, tooltip canvas bounds");

        var oldWorld=flow.worldHost.ActiveWorld;int cardCount=hand.InHandCount;string oldId=flow.Progress.currentPlanetId;
        settings.spawnDirection=Vector3.zero;
        Check(flow.RequestTravel(b)&&!flow.RequestTravel(c),"Travel queue accepted two destinations");
        yield return null;yield return null;
        Check(flow.IsTravelling&&Time.timeScale==0&&flow.pause.HasReason(GamePauseReason.PlanetTransition),"No transition pause");Keys(Key.M,Key.Escape,Key.F12);
        deadline=Deadline();while(flow.IsTravelling){Waiting(deadline,"failed preparation");yield return null;}
        settings.spawnDirection=originalSpawnDirection;Keys();
        Check(flow.IsMapOpen&&flow.worldHost.ActiveWorld==oldWorld&&oldWorld.gameObject.activeSelf&&oldWorld.gravity.enabled,"Failure changed active world");
        Check(flow.Progress.currentPlanetId==oldId&&flow.Progress.chosenPath.Count==1&&hand.InHandCount==cardCount&&flow.Progress.pendingDestinationId==null,"Failure changed progress/inventory");
        Check(flow.pause.HasReason(GamePauseReason.MapOverlay)&&!flow.pause.HasReason(GamePauseReason.PlanetTransition)&&Time.timeScale==0&&Specs()==specs,"Failure pause/spec rollback");
        passed.Add("deliberate spawn preparation failure preserves old world, data, cards, published appearance and map pause; rapid duplicate request rejected");
        Keys(Key.Escape);deadline=Deadline();while(flow.IsMapOpen){Waiting(deadline,"Esc close");yield return null;}Keys();yield return null;
        for(int i=0;i<4;i++)
        {
            var record=surge.Spawned.FirstOrDefault(r=>r.IsActive&&r.FeedingLamb==null)??surge.Spawned.First(r=>r.IsActive);
            var point=surge.surface.WorldPoint(record.slot);var normal=surge.surface.WorldNormal(record.slot);
            motor.Teleport(point+normal*.06f,normal);deadline=Deadline();while(!motor.Grounded){Waiting(deadline,"lamb placement support");yield return null;}
            Check(hand.TrySummonSelected(),"Could not deploy all original cards");yield return null;
        }
        var pickup=hand.Deployed[0];pickup.Advance(hand.activeSeconds+.1f);
        Check(hand.InHandCount==0&&hand.Deployed.Count==4&&pickup.State==LambAgent.LambState.Pickup,"Mixed grazing/pickup hand setup");
        motor.Traversal.SetAllowed(TraversalFeature.Dash,false);
        Keys(Key.M);deadline=Deadline();while(!flow.IsMapOpen){Waiting(deadline,"second map open");yield return null;}Keys();
        float[] ages=hand.Deployed.Select(l=>l.Age).ToArray();yield return new WaitForSecondsRealtime(.15f);
        Check(hand.Deployed.Select(l=>l.Age).SequenceEqual(ages),"Map advanced lamb lifetimes");
        var oldMaterials=oldWorld.groundRenderer.sharedMaterials;int remaining=surge.ActiveSproutCount;
        MouseAt(Node(b),true);yield return new WaitForSecondsRealtime(.05f);MouseAt(Node(b));
        deadline=Deadline();while(!flow.IsTravelling){Waiting(deadline,"actual destination click");yield return null;}
        while(flow.IsTravelling){Waiting(deadline,"arrival B");Check(Time.timeScale==0,"Unpaused frame during transition");yield return null;}
        Check(flow.Progress.currentPlanetId==b&&flow.CompletedTransitions==1&&flow.Progress.chosenPath.Count==2,"Transition did not commit exactly once");
        Check(flow.SystemData.Find(oldId).state.phase==PlanetPhase.Departed&&flow.SystemData.Find(oldId).state.surge.result==(remaining>0?SurgeResult.LeftWithRemaining:SurgeResult.Resolved),"Departure result was falsified");
        Check(hand.CardCount==4&&hand.InHandCount==4&&hand.Deployed.Count==0&&hand.ClaimCount==0,"Cards/feeding claims lost or duplicated");
        Check(!oldWorld&&oldMaterials.All(m=>!m),"Old world/material instances leaked");
        Check(surge.Attempts==1&&surge.ActiveSproutCount==1&&surge.ElapsedSeconds<.2f&&surge.durationSeconds==120&&surge.targetSprouts==40,"New surge started twice or did not reset");
        if(verifyDensity)
        {
            Check(surge.surface.PreparedFrequency==4&&surge.surface.Cells.Count==320,"2x arrival reverted to 80 cells");
            CheckStableDensity();
            var grid=surge.surface;var cellRefs=grid.Cells.ToArray();var slotRefs=grid.Cells.SelectMany(c=>c.slots).ToArray();var occupancy=slotRefs.Select(s=>s.occupied).ToArray();
            densitySelection=UnityEditor.Selection.objects;densitySceneView=UnityEditor.SceneView.lastActiveSceneView;
            if(densitySceneView){originalDrawGizmos=densitySceneView.drawGizmos;densitySceneView.drawGizmos=true;}
            UnityEditor.Selection.activeGameObject=grid.gameObject;UnityEditor.SceneView.RepaintAll();
            yield return new WaitForSecondsRealtime(.12f);
            Check(grid.Cells.SequenceEqual(cellRefs)&&grid.Cells.SelectMany(c=>c.slots).SequenceEqual(slotRefs)&&slotRefs.Select(s=>s.occupied).SequenceEqual(occupancy),"Native Scene selection/Gizmos changed live density");
            RestoreDensitySelection();passed.Add("2x arrival has 320 cells; Scene selection with Gizmos across real frames, map lifecycle and slots retain identity");
        }
        Check(motor.Source==flow.worldHost.ActiveWorld.gravity&&player.cameraRig.ReferenceSource==motor.Source,"Stale gravity/camera reference");
        Check(!motor.Traversal.AllowDash&&motor.Traversal.AirJumpsRemaining==1&&motor.Traversal.GlideSecondsRemaining==2.5f,"Travel reset debug switch or lost arrival resources");
        Check(Mathf.Abs(player.cameraRig.DesiredDistance-5.2f)<.01f&&Mathf.Abs(Camera.main.fieldOfView-42)<.1f,"Camera scaled with planet");
        Check(Specs()==specs,"Successful selection changed future specs");
        passed.Add("actual B click commits once; all four grazing/pickup lambs return same cards; claims, old world/materials cleared; new clocks/one first sprout; fresh camera/gravity; debug switch retained");
        var currentWorld=flow.worldHost.ActiveWorld;float sim=surge.SimulationSeconds;Keys(Key.R);yield return new WaitForSecondsRealtime(.15f);Keys();
        Check(flow.worldHost.ActiveWorld==currentWorld&&flow.Progress.chosenPath.Count==2&&surge.SimulationSeconds>=sim,"R rebuilt/reset the world");
        foreach(string next in new[]{"system_01_E","system_01_Destination"})
        {
            CompleteCurrent();yield return null;Keys(Key.M);deadline=Deadline();while(!flow.IsMapOpen){Waiting(deadline,"branch map");yield return null;}Keys();
            Check(!flow.RequestTravel(c)&&!flow.RequestTravel(oldId),"Invalid branch/backtracking accepted");Check(flow.RequestTravel(next),"Valid route refused");
            yield return null;yield return null;deadline=Deadline();while(flow.IsTravelling){Waiting(deadline,"branch arrival");yield return null;}
            Check(flow.Progress.currentPlanetId==next,"Wrong branch arrival");
            if(verifyDensity){Check(surge.surface.PreparedFrequency==2&&surge.surface.Cells.Count==80,"2x -> 0.7x -> 1x density accumulated");CheckStableDensity();}
        }
        CompleteCurrent();yield return null;Check(flow.SystemComplete&&flow.Progress.chosenPath.Count==4&&flow.CompletedTransitions==3,"Terminal state failed");
        Keys(Key.M);deadline=Deadline();while(!flow.IsMapOpen){Waiting(deadline,"terminal map");yield return null;}Keys();
        Check(flow.SystemData.planets.Count==6&&flow.SystemData.planets.All(p=>!flow.CanTravel(p.spec.planetId)),"Terminal offered extra planet");
        Check(flow.SystemData.Find(c).state.phase==PlanetPhase.Unvisited&&flow.SystemData.Find(d).state.phase==PlanetPhase.Unvisited,"Unused branches destroyed");
        float threat=surge.threat.RawThreat;Keys(Key.Escape);yield return null;Keys();yield return new WaitForSecondsRealtime(.15f);
        Check(surge.threat.RawThreat>threat,"Remaining roots stopped growing after close/completion");
        passed.Add("R preserves current world/clock/path; B→E→Destination, unused branches retained; terminal map has no outgoing travel; remaining roots continue growing");
        surge.Begin();surge.Advance(1f);foreach(var r in surge.Spawned)r.spawnedAt=surge.SimulationSeconds-60f;
        flow.RequestMap();Keys(Key.M);yield return null;yield return null;
        Check(surge.threat.IsGameOver&&!flow.IsMapOpen&&!flow.IsTravelling&&Time.timeScale==0&&flow.Progress.mode==RunMode.GameOver,"Same-frame defeat lost to map request");
        Check(!flow.pause.HasReason(GamePauseReason.MapOverlay)&&!flow.pause.HasReason(GamePauseReason.PlanetTransition),"Defeat left map/transition owners");
        passed.Add("queued map opening at spawn end cannot outrun final LateUpdate game-over decision");
        Keys(Key.Enter);yield return new WaitForSecondsRealtime(.5f);Keys();Bind();
        Check(flow.WorldReady&&flow.Progress.currentPlanetId==flow.SystemData.startPlanetId&&flow.Progress.chosenPath.Count==1&&flow.CompletedTransitions==0,"Retry did not start new journey");
        Check(Time.timeScale==1&&flow.pause.OwnerCount==0&&!flow.IsMapOpen&&!GameplayModalInput.IsOpen&&motor.Traversal.AllowDash,"Retry left modal/pause/debug state");
        Check(UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length==1,"Duplicate EventSystem after retry");
        passed.Add("actual Enter retry creates new journey, default abilities, one EventSystem, zero stale pauses/tooltips/pending travel");
    }
    void Finish(string error)
    {
        if(finished)return;finished=true;System.IO.Directory.CreateDirectory("Temp");
        System.IO.File.WriteAllText(verifyDensity?"Temp/PlanetTileDensityTravelValidation.txt":"Temp/StarSystemMapTravelSceneValidation.txt","passed="+(error=="")+"\nerror="+error+"\nelapsed="+(Time.realtimeSinceStartup-started)+"\n"+string.Join("\n",passed));Destroy(gameObject);
    }
    void CheckStableDensity()
    {
        var grid=surge.surface;var cells=grid.Cells.ToArray();var slots=cells.SelectMany(c=>c.slots).ToArray();var occupied=slots.Select(s=>s.occupied).ToArray();
        var selection=UnityEditor.Selection.objects;try
        {
            UnityEditor.Selection.activeGameObject=grid.gameObject;UnityEditor.SceneView.RepaintAll();
            var drawing=typeof(PlanetSurfaceGrid).GetMethod("DrawingCells",BindingFlags.NonPublic|BindingFlags.Instance);
            drawing.Invoke(grid,new object[]{true});drawing.Invoke(grid,new object[]{false});
            Check(grid.Cells.SequenceEqual(cells)&&grid.Cells.SelectMany(c=>c.slots).SequenceEqual(slots)&&slots.Select(s=>s.occupied).SequenceEqual(occupied),"Scene selection/Gizmos rebuilt the active grid");
        }
        finally{UnityEditor.Selection.objects=selection.Where(o=>o).ToArray();}
    }
    void OnDestroy()
    {
        RestoreDensitySelection();
        if(settings)settings.spawnDirection=originalSpawnDirection;
        if(flow)flow.CancelMapAndTravel();
        if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
        if(oldKeyboard!=null&&oldKeyboard.added)oldKeyboard.MakeCurrent();if(oldMouse!=null&&oldMouse.added)oldMouse.MakeCurrent();
        InputSystem.settings.backgroundBehavior=oldBackground;InputSystem.settings.editorInputBehaviorInPlayMode=oldEditor;
        if(player)player.SendMessage("OnApplicationFocus",oldFocus);
    }
    void RestoreDensitySelection()
    {
        if(densitySceneView)densitySceneView.drawGizmos=originalDrawGizmos;densitySceneView=null;
        if(densitySelection!=null){UnityEditor.Selection.objects=densitySelection.Where(o=>o).ToArray();densitySelection=null;}
    }
}
