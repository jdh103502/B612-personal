using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using B612;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class PlayerTraversalSceneValidation
{
    public static object Start()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Run in Play mode.");
        UnityEngine.Object.FindFirstObjectByType<BaobabThreatMeter>().ResetForRun();
        Camera.main.gameObject.AddComponent<PlayerTraversalSceneProbe>();
        return new {started=true,output="Temp/PlayerTraversalSceneValidation.txt",timeoutSeconds=18};
    }
}

[DefaultExecutionOrder(1500)]
public sealed class PlayerTraversalSceneProbe : MonoBehaviour
{
    PlayerController player;
    GravityWalker walker;
    PlayerTraversalAbilities abilities;
    LambCardHand hand;
    BaobabSurge surge;
    BaobabThreatMeter meter;
    OrbitCamera rig;
    Keyboard keyboard,oldKeyboard;
    InputSettings.BackgroundBehavior oldBackground;
    InputSettings.EditorInputBehaviorInPlayMode oldEditor;
    GameObject starObject;
    float elapsed,stageTime,minFov=100f,maxFov,maxDesiredChange,initialDesired,cooldownAtPause,glideAtPause,maxHeadY;
    int stage,groundJumps,airJumps,groundDashRequest;
    bool released,pressedAgain,shifted,shiftReleased,releasedGlide,airRejected,dashSeen,glideSeen,landingRefilled,cardSummoned,launchSeen,launchClean,overClear;
    bool finished,oldFocus;

    void Start()
    {
        rig=GetComponent<OrbitCamera>();player=UnityEngine.Object.FindFirstObjectByType<PlayerController>();walker=player.GetComponent<GravityWalker>();abilities=walker.Traversal;
        hand=UnityEngine.Object.FindFirstObjectByType<LambCardHand>();surge=hand.surge;meter=surge.threat;
        surge.enabled=false;meter.enabled=false;Time.timeScale=1f;
        surge.Begin();meter.Evaluate();
        oldKeyboard=Keyboard.current;oldBackground=InputSystem.settings.backgroundBehavior;oldEditor=InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard=InputSystem.AddDevice<Keyboard>("TraversalSceneValidationKeyboard");InputSystem.EnableDevice(keyboard);keyboard.MakeCurrent();
        oldFocus=(bool)typeof(PlayerController).GetField("hasFocus",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(player);
        player.enabled=true;player.SendMessage("OnApplicationFocus",true);player.Respawn();
        Queue();walker.Jumped+=GroundJumped;abilities.AirJumped+=AirJumped;initialDesired=rig.distance;
    }
    void GroundJumped(){groundJumps++;}
    void AirJumped(){airJumps++;}
    void Queue(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));}
    void Next(int next){stage=next;stageTime=0f;}
    void Update()
    {
        elapsed+=Time.unscaledDeltaTime;stageTime+=Time.unscaledDeltaTime;
        dashSeen|=abilities.IsDashing;glideSeen|=abilities.IsGliding;
        if(elapsed>1.6f&&stage==0)
        {
            minFov=Mathf.Min(minFov,GetComponent<Camera>().fieldOfView);maxFov=Mathf.Max(maxFov,GetComponent<Camera>().fieldOfView);
            maxDesiredChange=Mathf.Max(maxDesiredChange,Mathf.Abs(rig.DesiredDistance-initialDesired));
        }
        if(stage==0)
        {
            if(elapsed>.5f&&groundJumps==0)Queue(Key.Space);
            if(elapsed>.7f&&!released){released=true;Queue();}
            if(elapsed>.85f&&!pressedAgain){pressedAgain=true;Queue(Key.Space);}
            if(elapsed>1f&&!shifted){shifted=true;Queue(Key.Space,Key.LeftShift);}
            if(elapsed>1.05f&&!shiftReleased){shiftReleased=true;Queue(Key.Space);}
            if(!walker.Grounded&&groundJumps>0&&!airRejected)airRejected=!hand.TrySummonSelected();
            if(elapsed>3f&&!releasedGlide){releasedGlide=true;Queue();}
            if(elapsed>3.2f&&!abilities.FlightActive&&walker.Grounded&&abilities.DashChargeAvailable)
            {
                landingRefilled=abilities.AirJumpsRemaining==1&&Mathf.Abs(abilities.GlideSecondsRemaining-2.5f)<.001f;
                Queue(Key.LeftShift);Next(1);
            }
        }
        else if(stage==1)
        {
            if(stageTime>.2f)groundDashRequest=abilities.ConsumedDashRequestId;
            if(stageTime>.8f)Queue();
            if(stageTime>1f&&walker.Grounded)
            {
                bool noRepeat=abilities.ConsumedDashRequestId==groundDashRequest;
                if(!noRepeat){Finish("Shift hold repeated");return;}
                Queue(Key.Digit2,Key.Q);Next(2);
            }
        }
        else if(stage==2)
        {
            cardSummoned|=hand.InHandCount==hand.CardCount-1;
            if(stageTime>.15f)Queue();
            if(stageTime>.3f){Queue(Key.R);Next(3);}
        }
        else if(stage==3)
        {
            if(stageTime>.1f)Queue();
            if(stageTime>.4f&&walker.Grounded)
            {
                starObject=new GameObject("Traversal validation launch star");var star=starObject.AddComponent<LaunchStar>();
                var landing=new GameObject("Temporary traversal landing").transform;landing.SetParent(starObject.transform);
                var surface=surge.surface;var direction=(walker.transform.position-surface.transform.position+walker.transform.forward*2f).normalized;
                if(!surface.TryProject(surface.transform.InverseTransformVector(direction),out var p,out var n)){Finish("Landing fixture failed");return;}
                landing.position=p+n*.05f;landing.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(walker.transform.forward,n),n);
                star.landing=landing;star.flightTime=.6f;star.arcHeight=2f;player.NearbyLaunch=star;
                Queue(Key.Space,Key.LeftShift);Next(4);
            }
        }
        else if(stage==4)
        {
            launchSeen|=walker.Suspended;
            if(stageTime>.15f)Queue();
            if(stageTime>1.2f&&!walker.Suspended&&walker.Grounded)
            {
                launchClean=abilities.AirJumpsRemaining==1&&abilities.DashChargeAvailable&&!abilities.PendingDash&&!abilities.PendingJump&&!abilities.IsDashing;
                player.NearbyLaunch=null;Queue();Next(5);
            }
        }
        else if(stage==5&&stageTime>.25f)
        {
            surge.Begin();surge.Advance(15f);
            foreach(var r in surge.Spawned)r.spawnedAt=surge.SimulationSeconds-60f;
            surge.RefreshLifeStages();walker.Jump();walker.RequestDash();meter.Evaluate();
            cooldownAtPause=abilities.DashCooldownRemaining;glideAtPause=abilities.GlideSecondsRemaining;Queue(Key.Space,Key.LeftShift);Next(6);
        }
        else if(stage==6&&stageTime>.25f)
        {
            overClear=meter.IsGameOver&&Time.timeScale==0f&&!player.enabled&&!rig.enabled&&!abilities.InputEnabled
                &&!abilities.PendingJump&&!abilities.PendingDash&&!abilities.IsDashing&&!abilities.IsGliding
                &&Mathf.Abs(abilities.DashCooldownRemaining-cooldownAtPause)<.001f&&Mathf.Abs(abilities.GlideSecondsRemaining-glideAtPause)<.001f;
            Finish("");return;
        }
        if(elapsed>18f)Finish("Timed out at stage "+stage);
    }
    void LateUpdate()
    {
        if(elapsed>1.6f&&stage==0)maxHeadY=Mathf.Max(maxHeadY,GetComponent<Camera>().WorldToViewportPoint(walker.transform.position+walker.Up*.85f).y);
        if(glideSeen&&!screenshot)
        {
            screenshot=true;ScreenCapture.CaptureScreenshot("Assets/Captures/B612_TraversalGlide.png");
        }
    }
    bool screenshot;
    void Finish(string error)
    {
        if(finished)return;finished=true;
        bool passed=error==""&&groundJumps==1&&airJumps==1&&dashSeen&&glideSeen&&landingRefilled&&airRejected&&cardSummoned&&launchSeen&&launchClean&&overClear
            &&Mathf.Abs(maxFov-42f)<.1f&&Mathf.Abs(minFov-42f)<.1f&&maxDesiredChange<.02f&&maxHeadY<.76f;
        File.WriteAllText("Temp/PlayerTraversalSceneValidation.txt",string.Format(CultureInfo.InvariantCulture,
            "passed={0}\nerror={1}\ngroundJumps={2}\nairJumps={3}\ndashSeen={4}\nglideSeen={5}\nlandingRefilled={6}\nairSummonRejected={7}\ngroundCardSummoned={8}\nlaunchSeen={9}\nlaunchClean={10}\ngameOverClearAndFrozen={11}\nminFov={12}\nmaxFov={13}\nmaxAutomaticDistanceChange={14}\nelapsed={15}\nmaxHeadViewportY={16}\n",
            passed,error,groundJumps,airJumps,dashSeen,glideSeen,landingRefilled,airRejected,cardSummoned,launchSeen,launchClean,overClear,minFov,maxFov,maxDesiredChange,elapsed,maxHeadY));
        Destroy(this);
    }
    void OnDestroy()
    {
        if(walker)walker.Jumped-=GroundJumped;if(abilities)abilities.AirJumped-=AirJumped;
        if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(oldKeyboard!=null&&oldKeyboard.added)oldKeyboard.MakeCurrent();
        InputSystem.settings.backgroundBehavior=oldBackground;InputSystem.settings.editorInputBehaviorInPlayMode=oldEditor;
        if(meter){meter.ResetForRun();meter.enabled=true;}
        if(surge){surge.enabled=true;surge.Begin();}
        if(player){player.NearbyLaunch=null;player.enabled=true;player.SendMessage("OnApplicationFocus",oldFocus);}
        if(rig)rig.enabled=true;
        if(starObject)Destroy(starObject);
    }
}
