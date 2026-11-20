using System;
using System.Collections;
using System.Collections.Generic;
using B612;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class TraversalTestCourseSceneValidation
{
    public static object Start()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Use Play mode.");
        new GameObject("Traversal course input check").AddComponent<TraversalCourseInputProbe>();return "Started Temp/TraversalCourse/LiveInput.txt";
    }
}
public sealed class TraversalCourseInputProbe:MonoBehaviour
{
    Keyboard keyboard,previous;InputSettings.BackgroundBehavior background;InputSettings.EditorInputBehaviorInPlayMode editorInput;
    TraversalTestCourse course;DeveloperDebugController debug;GravityWalker motor;bool done,oldFocus;
    readonly List<string> passed=new List<string>();
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    void Input(params Key[] keys){InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));}
    IEnumerator Start()
    {
        course=FindFirstObjectByType<TraversalTestCourse>();debug=FindFirstObjectByType<DeveloperDebugController>();motor=course.player.GetComponent<GravityWalker>();
        previous=Keyboard.current;background=InputSystem.settings.backgroundBehavior;editorInput=InputSystem.settings.editorInputBehaviorInPlayMode;
        oldFocus=(bool)typeof(PlayerController).GetField("hasFocus",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(course.player);
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard=InputSystem.AddDevice<Keyboard>("TraversalCourseValidationKeyboard");keyboard.MakeCurrent();course.player.SendMessage("OnApplicationFocus",true);
        var run=Run();
        while(true)
        {
            bool more=false;object next=null;string error=null;
            try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception ex){error=ex.ToString();}
            if(error!=null){Finish(false,error);yield break;}if(!more){Finish(true,"");yield break;}yield return next;
        }
    }
    IEnumerator Run()
    {
        var mesh=course.ground.sharedMesh;
        for(int station=0;station<3;station++)
        {
            debug.Open();Check(debug.IsOpen && Time.timeScale==0,"F12 modal not paused");
            debug.hud.dashToggle.isOn=station==0;debug.hud.airJumpToggle.isOn=station==1;debug.hud.glideToggle.isOn=station==2;
            Check(motor.Traversal.AllowDash==(station==0)&&motor.Traversal.AllowAirJump==(station==1)&&motor.Traversal.AllowGlide==(station==2),"Existing UI did not configure abilities");
            Check(mesh==course.ground.sharedMesh,"Ability toggle regenerated terrain");
            debug.Close();Input();yield return null;yield return null;
            Input(station==0?Key.Digit1:station==1?Key.Digit2:Key.Digit3);yield return null;Input();yield return null;yield return null;
            Check(course.Station==station,"Number key station selection failed");
            float settle=Time.time+.35f;while(Time.time<settle)yield return null;
            Check(motor.Grounded,"No initial ground");
            float start=Time.time,launch=-1;bool released=false,pressedAgain=false,dashed=false,sawGlide=false;int groundJumps=0,airJumps=0;
            Action ground=()=>groundJumps++;Action air=()=>airJumps++;motor.Jumped+=ground;motor.Traversal.AirJumped+=air;
            bool reached=false;
            while(Time.time-start<9)
            {
                var p=TraversalTestCourseGeometry.Coordinates(station,course.player.transform.position-course.gravity.transform.position);
                float threshold=station==0?-2.4f:station==1?-1f:-2.3f;
                if(launch<0 && p.x>=threshold && motor.Grounded)launch=Time.time;
                var keys=new List<Key>{Key.W};
                if(launch>=0)
                {
                    float t=Time.time-launch;bool held=true;
                    if(station==1 && t>=.66f && !released){held=false;released=true;}
                    else if(station==1 && released && !pressedAgain)pressedAgain=true;
                    if(held)keys.Add(Key.Space);
                    if(station==0 && t>=.65f && !dashed){keys.Add(Key.LeftShift);dashed=true;}
                }
                Input(keys.ToArray());sawGlide|=motor.Traversal.IsGliding;
                if(motor.Grounded && p.x>TraversalTestCourseGeometry.Goal(station).x-.65f && Mathf.Abs(p.y)<.8f)
                {reached=true;break;}
                yield return null;
            }
            Input();motor.Jumped-=ground;motor.Traversal.AirJumped-=air;
            Check(reached,"Actual input failed station "+station+" at "+TraversalTestCourseGeometry.Coordinates(station,course.player.transform.position-course.gravity.transform.position));
            Check(groundJumps==1 && airJumps==(station==1?1:0),"Unexpected jump count");
            Check(station!=2 || sawGlide,"Space hold did not enter glide");
            passed.Add("Station "+(station+1)+": existing UI toggles + number key + real W/Space/Shift input landed; ground jumps="+groundJumps+", air jumps="+airJumps+", glide="+sawGlide);
            yield return null;yield return null;
        }
        debug.Open();debug.hud.dashToggle.isOn=false;debug.hud.airJumpToggle.isOn=false;debug.hud.glideToggle.isOn=false;debug.Close();Input();yield return null;yield return null;
        course.SelectStation(0);yield return null;
        float runUntil=Time.time+.7f;Input(Key.W);while(Time.time<runUntil)yield return null;
        Check(course.Timing,"Practice timer did not start");
        Input(Key.R);yield return null;Input();yield return null;yield return null;
        Check(!course.Timing && !motor.Traversal.AllowDash && !motor.Traversal.AllowAirJump && !motor.Traversal.AllowGlide,"R reset changed profile or left timer running");
        Check(Vector2.Distance(TraversalTestCourseGeometry.Coordinates(0,course.player.transform.position-course.gravity.transform.position),TraversalTestCourseGeometry.Start(0))<.15f,"R did not return to current practice start");
        passed.Add("R returns to selected start, resets measurement, and preserves the three ability settings");
    }
    void Finish(bool success,string error)
    {
        done=true;System.IO.Directory.CreateDirectory("Temp/TraversalCourse");System.IO.File.WriteAllText("Temp/TraversalCourse/LiveInput.txt","passed="+success+"\nerror="+error+"\n"+string.Join("\n",passed));
        if(!success)Debug.LogError(error);Cleanup();Destroy(gameObject);
    }
    void Cleanup()
    {
        if(debug && debug.IsOpen)debug.Close();if(motor)motor.ClearPendingRequests();
        if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(previous!=null&&previous.added)previous.MakeCurrent();
        InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=editorInput;
        if(course)course.player.SendMessage("OnApplicationFocus",oldFocus);
    }
    void OnDestroy(){if(!done)Cleanup();}
}
