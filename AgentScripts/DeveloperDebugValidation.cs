using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using B612;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class DeveloperDebugValidation
{
    static Scene scene;
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    sealed class Fixture : IDisposable
    {
        public readonly GameObject root, floor;
        public readonly GravityWalker motor;
        public readonly PlayerTraversalAbilities ability;
        public readonly Rigidbody body;
        public readonly Vector3 origin=new Vector3(1000,0,0);
        public int groundJumps,airJumps,landings;
        static readonly MethodInfo tick=typeof(GravityWalker).GetMethod("TickTraversal",BindingFlags.Instance|BindingFlags.NonPublic);
        public Fixture(bool ground=true)
        {
            root=new GameObject("Debug fixture");SceneManager.MoveGameObjectToScene(root,scene);
            var gravity=new GameObject("Gravity");gravity.transform.SetParent(root.transform);gravity.transform.position=origin-Vector3.up*100;
            var source=gravity.AddComponent<GravitySource>();source.surfaceRadius=100;source.influenceRadius=130;
            floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(root.transform);floor.transform.position=origin-Vector3.up*.5f;floor.transform.localScale=new Vector3(40,1,40);floor.SetActive(ground);
            var actor=new GameObject("Player fixture");actor.transform.SetParent(root.transform);actor.transform.position=origin+Vector3.up*(ground?.02f:5f);
            body=actor.AddComponent<Rigidbody>();var cap=actor.AddComponent<CapsuleCollider>();cap.height=.78f;cap.radius=.13f;cap.center=Vector3.up*.39f;
            motor=actor.AddComponent<GravityWalker>();motor.enabled=false;ability=actor.AddComponent<PlayerTraversalAbilities>();
            motor.Jumped+=()=>groundJumps++;ability.AirJumped+=()=>airJumps++;ability.LandingRecovered+=()=>landings++;Physics.SyncTransforms();
        }
        public void Step(int count=1){for(int i=0;i<count;i++){tick.Invoke(motor,new object[]{.02f});scene.GetPhysicsScene().Simulate(.02f);motor.transform.SetPositionAndRotation(body.position,body.rotation);}}
        public void Stand(){Step(6);Check(motor.Grounded,"No fixture support");}
        public void Dispose(){UnityEngine.Object.DestroyImmediate(root);}
    }
    public static object Validate()
    {
        Check(Application.isPlaying,"Run in Play mode");
        Check(!DeveloperDebugController.BlocksGameplayInput,"Close debug overlay before isolated tests");
        float speed=Time.timeScale;Time.timeScale=1;
        scene=SceneManager.CreateScene("Developer debug unit validation",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var passed=new List<string>();
        try
        {
            for(int mask=0;mask<8;mask++)using(var f=new Fixture(false))
            {
                f.ability.SetAllowed(TraversalFeature.Dash,(mask&1)!=0);f.ability.SetAllowed(TraversalFeature.AirJump,(mask&2)!=0);f.ability.SetAllowed(TraversalFeature.Glide,(mask&4)!=0);
                f.Step();f.motor.Jump();f.Step();Check(f.airJumps==((mask&2)!=0?1:0),"Air jump permission failed: "+mask);
                f.motor.RequestDash();f.Step();Check(f.ability.IsDashing==((mask&1)!=0),"Dash permission failed: "+mask);
                f.Step(12);f.body.linearVelocity=Vector3.down;f.motor.jumpHeld=true;f.Step(15);
                Check(f.ability.IsGliding==((mask&4)!=0),"Glide permission failed: "+mask);
                Check(f.ability.enabled,"Feature toggle disabled component");passed.Add("actual motor requests, combination "+mask);
            }
            using(var f=new Fixture())
            {
                f.Stand();f.ability.SetAllowed(TraversalFeature.AirJump,false);f.motor.Jump();f.Step();Check(f.groundJumps==1,"Ground jump disabled");
                f.motor.Jump();f.Step();Check(f.airJumps==0&&f.ability.AirJumpsRemaining==1,"Disabled air jump was consumed");
                passed.Add("air jump off preserves ground jump");
            }
            using(var f=new Fixture())
            {
                f.Stand();f.ability.SetAllowed(TraversalFeature.AirJump,false);f.floor.SetActive(false);Physics.SyncTransforms();f.Step(2);f.motor.Jump();f.Step();
                Check(f.groundJumps==1&&f.airJumps==0,"Coyote jump disabled");passed.Add("air jump off preserves coyote jump");
            }
            using(var f=new Fixture(false))
            {
                f.ability.SetAllowed(TraversalFeature.AirJump,false);f.Step(10);f.body.position=f.origin+Vector3.up*.035f;f.motor.transform.position=f.body.position;f.body.linearVelocity=Vector3.down;
                f.motor.Jump();f.floor.SetActive(true);Physics.SyncTransforms();f.Step(5);
                Check(f.groundJumps==1&&f.airJumps==0,"Ground landing buffer disabled");passed.Add("air jump off preserves buffered landing jump");
            }
            using(var f=new Fixture(false))
            {
                f.Step();f.motor.Jump();f.Step();Vector3 jumpVelocity=f.body.linearVelocity;
                f.ability.SetAllowed(TraversalFeature.AirJump,false);Check(f.body.linearVelocity==jumpVelocity,"Disabling air jump rewrote velocity");
                f.ability.SetAllowed(TraversalFeature.AirJump,true);Check(f.ability.AirJumpsRemaining==0,"Air jump toggle refilled charge");
                f.motor.RequestDash();f.Step();Vector3 dashVelocity=f.body.linearVelocity;float cooldown=f.ability.DashCooldownRemaining;
                f.ability.SetAllowed(TraversalFeature.Dash,false);f.ability.SetAllowed(TraversalFeature.Dash,true);
                Check(!f.ability.IsDashing&&f.body.linearVelocity==dashVelocity,"Dash UI wrote velocity or restarted dash");
                Check(!f.ability.DashChargeAvailable&&f.ability.DashCooldownRemaining==cooldown,"Dash toggle refilled charge/cooldown");
                f.Step();Check(f.motor.TangentSpeed<=3.001f,"Dash cancellation did not clamp residual via motor");
                f.body.linearVelocity=Vector3.down;f.motor.jumpHeld=true;f.Step(15);Check(f.ability.IsGliding,"No glide to cancel");
                float budget=f.ability.GlideSecondsRemaining;
                f.ability.SetAllowed(TraversalFeature.Glide,false);f.ability.SetAllowed(TraversalFeature.Glide,true);f.Step();
                Check(!f.ability.IsGliding&&f.ability.GlideSecondsRemaining==budget,"Glide toggle restarted/refilled while held");
                f.motor.jumpHeld=false;f.Step();f.motor.jumpHeld=true;f.Step(15);Check(f.ability.IsGliding,"Fresh hold did not restart permitted glide");
                f.Step(140);Check(f.ability.GlideSecondsRemaining==0,"Glide did not exhaust");
                f.ability.SetAllowed(TraversalFeature.Glide,false);f.ability.SetAllowed(TraversalFeature.Glide,true);Check(f.ability.GlideSecondsRemaining==0,"Exhausted glide refilled");
                passed.Add("toggle cancellation, motor residual clamp, fresh glide input, consumed resources preserved");
            }
            using(var f=new Fixture())
            {
                f.Stand();f.motor.jumpHeld=true;f.motor.Jump();f.Step();f.Step(3);f.motor.Jump();f.Step();f.motor.RequestDash();f.Step(12);
                foreach(TraversalFeature feature in Enum.GetValues(typeof(TraversalFeature)))f.ability.SetAllowed(feature,false);
                f.motor.jumpHeld=false;for(int i=0;i<220&&f.ability.FlightActive;i++)f.Step();
                Check(f.motor.Grounded&&f.ability.AirJumpsRemaining==1&&f.ability.DashChargeAvailable&&f.ability.GlideSecondsRemaining==2.5f,"Disabled features prevented landing refill");
                f.motor.Teleport(f.origin,Vector3.up);Check(!f.ability.AllowDash&&!f.ability.AllowAirJump&&!f.ability.AllowGlide,"Respawn reset feature switches");
                passed.Add("disabled features allow valid landing recovery; teleport retains switches");
            }
            ValidateDeviceInputs();passed.Add("keyboard and gamepad input routes, all 8 feature combinations");
            var pauseObject=new GameObject("Pause fixture");SceneManager.MoveGameObjectToScene(pauseObject,scene);
            var pause=pauseObject.AddComponent<GamePauseCoordinator>();var ownerA=new GameObject("Owner A");var ownerB=new GameObject("Owner B");
            try
            {
                Time.timeScale=.42f;pause.AcquirePause(ownerA,GamePauseReason.DebugOverlay);pause.AcquirePause(ownerA,GamePauseReason.DebugOverlay);
                pause.AcquirePause(ownerB,GamePauseReason.GameOver);Check(pause.OwnerCount==2&&Time.timeScale==0,"Pause ownership count");
                pause.ReleasePause(ownerA);Check(Time.timeScale==0&&pause.HasReason(GamePauseReason.GameOver),"Closing debug resumed game over");
                pause.ReleasePause(ownerB);Check(Mathf.Abs(Time.timeScale-.42f)<.0001f,"Non-unit execution speed lost");
                Time.timeScale=0;pause.AcquirePause(ownerA,GamePauseReason.DebugOverlay);pause.ReleasePause(ownerA);Check(Time.timeScale==0,"External zero-time pause was resumed");
                passed.Add("idempotent owners, overlapping reasons, saved speed, external pause preservation");
            }
            finally {UnityEngine.Object.DestroyImmediate(pauseObject);UnityEngine.Object.DestroyImmediate(ownerA);UnityEngine.Object.DestroyImmediate(ownerB);}
            return new{passed=true,cases=passed.ToArray()};
        }
        finally {foreach(var root in scene.GetRootGameObjects())UnityEngine.Object.DestroyImmediate(root);SceneManager.UnloadSceneAsync(scene);Time.timeScale=speed;}
    }
    public static object StartLive()
    {
        Check(Application.isPlaying,"Run in Play mode");
        var root=new GameObject("Developer Debug Live Validation");UnityEngine.Object.DontDestroyOnLoad(root);root.AddComponent<DeveloperDebugLiveProbe>();
        return new{started=true,output="Temp/DeveloperDebugLiveValidation.txt"};
    }
    static void ValidateDeviceInputs()
    {
        var oldKeyboard=Keyboard.current;var oldPad=Gamepad.current;
        var background=InputSystem.settings.backgroundBehavior;var editor=InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var keyboard=InputSystem.AddDevice<Keyboard>("DebugFeatureKeyboard");var pad=InputSystem.AddDevice<Gamepad>("DebugFeatureGamepad");
        InputSystem.EnableDevice(keyboard);InputSystem.EnableDevice(pad);keyboard.MakeCurrent();pad.MakeCurrent();
        Action<bool,bool,bool> input=(usePad,jump,dash)=>
        {
            var keys=new List<Key>();if(!usePad&&jump)keys.Add(Key.Space);if(!usePad&&dash)keys.Add(Key.LeftShift);
            var state=new GamepadState();if(usePad&&jump)state=state.WithButton(GamepadButton.South);if(usePad&&dash)state=state.WithButton(GamepadButton.RightShoulder);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys.ToArray()));InputSystem.QueueStateEvent(pad,state);InputSystem.Update();
        };
        try
        {
            foreach(bool usePad in new[]{false,true})for(int mask=0;mask<8;mask++)using(var f=new Fixture(false))
            {
                f.ability.SetAllowed(TraversalFeature.Dash,(mask&1)!=0);f.ability.SetAllowed(TraversalFeature.AirJump,(mask&2)!=0);f.ability.SetAllowed(TraversalFeature.Glide,(mask&4)!=0);
                var controller=f.motor.gameObject.AddComponent<PlayerController>();controller.SendMessage("OnApplicationFocus",true);
                input(usePad,false,false);controller.SendMessage("Update");f.Step();
                input(usePad,true,false);controller.SendMessage("Update");f.Step();Check(f.airJumps==((mask&2)!=0?1:0),"Device air jump mask "+mask);
                input(usePad,false,false);controller.SendMessage("Update");f.Step();
                input(usePad,false,true);controller.SendMessage("Update");f.Step();Check(f.ability.IsDashing==((mask&1)!=0),"Device dash mask "+mask);
                input(usePad,false,false);controller.SendMessage("Update");f.Step(12);
                f.body.linearVelocity=Vector3.down;input(usePad,true,false);controller.SendMessage("Update");f.Step(20);
                // A fresh South/Space may spend a previously disabled air jump only when that feature is allowed.
                Check(f.ability.IsGliding==((mask&4)!=0),"Device glide mask "+mask);
            }
        }
        finally
        {
            InputSystem.RemoveDevice(keyboard);InputSystem.RemoveDevice(pad);
            if(oldKeyboard!=null&&oldKeyboard.added)oldKeyboard.MakeCurrent();if(oldPad!=null&&oldPad.added)oldPad.MakeCurrent();
            InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=editor;
        }
    }
}

[DefaultExecutionOrder(1500)]
public sealed class DeveloperDebugLiveProbe : MonoBehaviour
{
    DeveloperDebugController debug;
    PlayerController player;
    GravityWalker motor;
    PlayerTraversalAbilities ability;
    BaobabSurge surge;
    BaobabThreatMeter meter;
    LambCardHand hand;
    LambAgent lamb;
    Keyboard keyboard,oldKeyboard;
    Mouse mouse,oldMouse;
    InputSettings.BackgroundBehavior oldBackground;
    InputSettings.EditorInputBehaviorInPlayMode oldEditor;
    bool oldFocus,finished,action;
    int stage,handIndex,jumpId,dashId,boundaryStep;
    float elapsed,stageTime,dashTime,cooldown,glide,surgeTime,lambAge,eating,fixedTime;
    Vector3 position,velocity,cameraPosition;
    readonly List<string> results=new List<string>();
    void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    void Bind()
    {
        debug=UnityEngine.Object.FindFirstObjectByType<DeveloperDebugController>();player=debug.player;ability=debug.abilities;motor=player.GetComponent<GravityWalker>();
        surge=UnityEngine.Object.FindFirstObjectByType<BaobabSurge>();meter=surge.threat;hand=UnityEngine.Object.FindFirstObjectByType<LambCardHand>();
    }
    void Start()
    {
        Bind();meter.ResetForRun();surge.Begin();meter.enabled=false;Time.timeScale=1f;player.Respawn();
        oldFocus=(bool)typeof(PlayerController).GetField("hasFocus",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(player);player.SendMessage("OnApplicationFocus",true);
        oldKeyboard=Keyboard.current;oldMouse=Mouse.current;oldBackground=InputSystem.settings.backgroundBehavior;oldEditor=InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard=InputSystem.AddDevice<Keyboard>("DebugValidationKeyboard");mouse=InputSystem.AddDevice<Mouse>("DebugValidationMouse");InputSystem.EnableDevice(keyboard);InputSystem.EnableDevice(mouse);keyboard.MakeCurrent();mouse.MakeCurrent();Keys();
    }
    void Keys(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));}
    void MouseAt(Vector2 point,bool down=false,Vector2 delta=default,Vector2 scroll=default)
    {InputSystem.QueueStateEvent(mouse,new MouseState{position=point,buttons=(ushort)(down?1:0),delta=delta,scroll=scroll});}
    Vector2 Center(Component c)=>RectTransformUtility.WorldToScreenPoint(null,c.GetComponent<RectTransform>().position);
    void Next(int value){stage=value;stageTime=0;action=false;}
    void Snapshot()
    {
        dashTime=ability.DashElapsedSeconds;cooldown=ability.DashCooldownRemaining;glide=ability.GlideSecondsRemaining;surgeTime=surge.SimulationSeconds;
        lambAge=lamb?lamb.Age:0;eating=lamb?lamb.EatingProgress:0;fixedTime=Time.fixedTime;
        position=motor.transform.position;velocity=motor.GetComponent<Rigidbody>().linearVelocity;cameraPosition=player.cameraRig.transform.position;handIndex=hand.SelectedIndex;
        jumpId=ability.JumpRequestId;dashId=ability.DashRequestId;
    }
    void Frozen()
    {
        Check(Time.timeScale==0&&debug.IsOpen,"F12 did not pause");
        Check(ability.DashElapsedSeconds==dashTime&&ability.DashCooldownRemaining==cooldown&&ability.GlideSecondsRemaining==glide,"Paused ability clocks advanced");
        Check(surge.SimulationSeconds==surgeTime&&(!lamb||lamb.Age==lambAge&&lamb.EatingProgress==eating)&&Time.fixedTime==fixedTime,"Paused world clocks advanced");
        Check(Vector3.Distance(motor.transform.position,position)<.001f&&motor.GetComponent<Rigidbody>().linearVelocity==velocity,"Paused player changed");
        Check(Vector3.Distance(player.cameraRig.transform.position,cameraPosition)<.001f,"Paused camera moved");
        Check(ability.JumpRequestId==jumpId&&ability.DashRequestId==dashId&&hand.SelectedIndex==handIndex&&!ability.PendingJump&&!ability.PendingDash,"Panel gameplay input leaked");
    }
    void Update()
    {
        if(finished)return;elapsed+=Time.unscaledDeltaTime;stageTime+=Time.unscaledDeltaTime;
        try { Tick();if(elapsed>24f)throw new InvalidOperationException("Timeout stage "+stage); }
        catch(Exception ex){Finish(ex.Message);}
    }
    void Tick()
    {
        if(stage==0&&stageTime>.25f&&motor.Grounded)
        {
            Check(hand.TrySummonSelected(),"Could not summon test lamb");lamb=hand.LambForCard(0);
            Keys(Key.Space);Next(1);
        }
        else if(stage==1)
        {
            if(stageTime>.12f&&!action){Keys();action=true;}
            if(stageTime>.20f){Keys(Key.Space,Key.LeftShift);Next(2);}
        }
        else if(stage==2&&ability.IsDashing)
        {Keys(Key.Space,Key.LeftShift,Key.F12);Next(3);}
        else if(stage==3&&debug.IsOpen)
        {
            Check(ability.IsDashing,"Opening canceled dash");Snapshot();Next(4);
        }
        else if(stage==4)
        {
            Frozen();Check(ability.IsDashing,"Dash state was canceled by pause Update");
            if(stageTime>.12f&&!action)
            {Keys(Key.Space,Key.LeftShift,Key.Q,Key.R,Key.W,Key.Digit3,Key.F12);MouseAt(new Vector2(30,30),true,new Vector2(40,30),new Vector2(0,120));action=true;}
            if(stageTime>.32f){Check(debug.IsOpen,"Held F12 repeated");Keys(Key.Space,Key.LeftShift);Next(5);}
        }
        else if(stage==5&&stageTime>.1f)
        {Frozen();Keys(Key.Space,Key.LeftShift,Key.F12);Next(6);}
        else if(stage==6&&!debug.IsOpen)
        {
            Check(Time.timeScale==1&&ability.IsDashing&&ability.DashElapsedSeconds==dashTime,"Closing changed dash state/time");
            Check(!ability.PendingJump&&!ability.PendingDash,"Closing left pending input");
            results.Add("F12 open/hold/repress; dash state, velocity, world/lamb/eating/clocks preserved; panel gameplay input blocked");
            Keys(Key.Space);Next(7);
        }
        else if(stage==7)
        {
            if(!action&&stageTime>.05f){Check(DeveloperDebugController.BlocksMouseInput,"Held panel mouse was not gated");MouseAt(new Vector2(30,30));action=true;}
            if(ability.IsGliding){Keys(Key.Space,Key.F12);Next(8);}
        }
        else if(stage==8&&debug.IsOpen)
        {Check(ability.IsGliding,"Opening canceled glide");Snapshot();Next(9);}
        else if(stage==9)
        {
            Frozen();Check(ability.IsGliding,"Glide state was canceled by pause Update");
            if(stageTime>.15f&&!action){Keys();action=true;}
            if(stageTime>.3f){Keys(Key.Space,Key.LeftShift,Key.F12);Next(10);}
        }
        else if(stage==10&&!debug.IsOpen)
        {
            if(stageTime>.15f)
            {
                Check(!ability.IsGliding&&!motor.jumpHeld&&ability.JumpRequestId==jumpId&&ability.DashRequestId==dashId,"Repressed panel keys replayed or retained glide");
                results.Add("glide pause preserved; release/repress Space and Shift do not replay; camera mouse release boundary");
                Keys();Next(11);
            }
        }
        else if(stage==11&&stageTime>.08f){Keys(Key.F12);Next(12);}
        else if(stage==12&&debug.IsOpen)
        {
            int air=ability.AirJumpsRemaining;bool dash=ability.DashChargeAvailable;float remaining=ability.GlideSecondsRemaining;
            for(int mask=0;mask<8;mask++)
            {
                debug.hud.dashToggle.isOn=(mask&1)!=0;debug.hud.airJumpToggle.isOn=(mask&2)!=0;debug.hud.glideToggle.isOn=(mask&4)!=0;
                Check(ability.AllowDash==((mask&1)!=0)&&ability.AllowAirJump==((mask&2)!=0)&&ability.AllowGlide==((mask&4)!=0),"UI combination callback failed");
            }
            Check(ability.AirJumpsRemaining==air&&ability.DashChargeAvailable==dash&&ability.GlideSecondsRemaining==remaining,"UI toggle refilled resources");
            Keys();MouseAt(Center(debug.hud.dashToggle),true);Next(13);
        }
        else if(stage==13&&stageTime>.12f){MouseAt(Center(debug.hud.dashToggle));Next(14);}
        else if(stage==14&&stageTime>.12f)
        {
            Check(!ability.AllowDash,"Actual pointer click did not toggle dash row");
            results.Add("8 UI combinations, no resource refill, real pointer click on toggle label");
            MouseAt(Center(debug.hud.closeButton),true);Next(15);
        }
        else if(stage==15&&stageTime>.12f){MouseAt(Center(debug.hud.closeButton));Next(16);}
        else if(stage==16&&!debug.IsOpen&&stageTime>.12f)
        {
            if(boundaryStep==0)
            {
            Check(!ability.AllowDash&&ability.AllowAirJump&&ability.AllowGlide,"Close reset switches");
            player.Respawn();Check(!ability.AllowDash,"R respawn reset switches");
            player.enabled=false;player.cameraRig.enabled=false;debug.Open();debug.Close();
            Check(!player.enabled&&!player.cameraRig.enabled,"Debug re-enabled disabled components");player.enabled=true;player.SendMessage("OnApplicationFocus",true);player.cameraRig.enabled=true;
            boundaryStep++;return;
            }
            if(boundaryStep==1)
            {
            Time.timeScale=.6f;debug.Open();debug.Close();Check(Mathf.Abs(Time.timeScale-.6f)<.0001f,"Debug lost original speed");Time.timeScale=1;
            boundaryStep++;return;
            }
            if(boundaryStep==2)
            {
            var configuredPlayer=debug.player;debug.player=null;debug.Open();
            Check(!debug.hud.dashToggle.interactable&&!debug.hud.airJumpToggle.interactable&&!debug.hud.glideToggle.interactable,"Missing player UI remained interactive");
            debug.Close();debug.player=configuredPlayer;
            boundaryStep++;return;
            }
            // Force defeat while Debug already owns the pause; the other order is tested below too.
            debug.Open();surge.Begin();surge.Advance(15f);foreach(var record in surge.Spawned)record.spawnedAt=surge.SimulationSeconds-60f;
            surge.RefreshLifeStages();meter.Evaluate();
            Check(meter.IsGameOver&&debug.pause.OwnerCount==2&&!player.enabled&&!ability.IsDashing&&!ability.IsGliding,"Overlapping game over did not win");
            Next(17);Keys(Key.Enter);
        }
        else if(stage==17&&stageTime>.15f)
        {
            Check(debug.IsOpen&&meter.IsGameOver&&Time.timeScale==0,"Enter behind overlay retried game");
            debug.Close();Check(Time.timeScale==0&&meter.IsGameOver&&!player.enabled,"Debug close resumed defeat");
            debug.Open();debug.Close();Check(Time.timeScale==0,"Opening after defeat resumed game");
            results.Add("close pointer, respawn settings, disabled components, original speed, game-over overlap both orders, hidden Enter blocked");
            debug.Open();Destroy(debug.gameObject);Keys();Next(18);
        }
        else if(stage==18&&stageTime>.1f)
        {
            Check(!DeveloperDebugController.Active&&Time.timeScale==0&&GamePauseCoordinator.Active.HasReason(GamePauseReason.GameOver)
                &&GamePauseCoordinator.Active.OwnerCount==1,"Destroying debug UI released another pause owner");
            results.Add("destroying open debug root releases only its owner; independent GameOver pause survives");
            // Persist only this validation probe through a real scene reload, like the actual retry.
            meter.RestartAttempt();Next(19);
        }
        else if(stage==19&&stageTime>.4f)
        {
            Bind();Check(!debug.IsOpen&&debug.pause.OwnerCount==0&&Time.timeScale==1,"Retry left pause/modal state");
            Check(ability.AllowDash&&ability.AllowAirJump&&ability.AllowGlide,"Retry did not restore default switches");
            Check(UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length==1,"Duplicate EventSystem");
            results.Add("real game-over retry/reload: default features, one EventSystem, no stale pause, normal clock");Finish("");
        }
    }
    void Finish(string error)
    {
        if(finished)return;finished=true;
        System.IO.Directory.CreateDirectory("Temp");System.IO.File.WriteAllText("Temp/DeveloperDebugLiveValidation.txt","passed="+(error=="")+"\nerror="+error+"\nstage="+stage+"\nelapsed="+elapsed+"\n"+string.Join("\n",results));
        Destroy(gameObject);
    }
    void OnDestroy()
    {
        if(debug&&debug.IsOpen)debug.Close();
        if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
        if(oldKeyboard!=null&&oldKeyboard.added)oldKeyboard.MakeCurrent();if(oldMouse!=null&&oldMouse.added)oldMouse.MakeCurrent();
        InputSystem.settings.backgroundBehavior=oldBackground;InputSystem.settings.editorInputBehaviorInPlayMode=oldEditor;
        if(meter){meter.ResetForRun();meter.enabled=true;}if(surge){surge.enabled=true;surge.Begin();}
        if(player){player.enabled=true;player.SendMessage("OnApplicationFocus",oldFocus);if(player.cameraRig)player.cameraRig.enabled=true;}
    }
}
