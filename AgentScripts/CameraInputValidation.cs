using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using B612;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class CameraInputValidation
{
    public static object Start()
    {
        Camera.main.gameObject.AddComponent<CameraInputProbe>();
        return new {started=true,seconds=7,output="Temp/CameraInput.txt"};
    }
}

[DefaultExecutionOrder(1200)]
public sealed class CameraInputProbe : MonoBehaviour
{
    Keyboard keyboard,originalKeyboard;
    Mouse mouse,originalMouse;
    InputSettings.BackgroundBehavior background;
    InputSettings.EditorInputBehaviorInPlayMode editorBehavior;
    OrbitCamera rig;
    LambCardHand hand;
    float elapsed,beforeDistance,wheelDistance,zoomDistance,beforePitch,finalPitch,yawDegrees,qYaw;
    Quaternion beforeYaw,beforeQ;
    int beforeCard,wheelCard,numberCard;
    bool wheel,zoom,number,zDown,zUp,qDown,qUp,drag,release,summoned;
    FieldInfo pitchField;

    void Start()
    {
        rig=GetComponent<OrbitCamera>();hand=UnityEngine.Object.FindFirstObjectByType<LambCardHand>();
        originalKeyboard=Keyboard.current;originalMouse=Mouse.current;
        background=InputSystem.settings.backgroundBehavior;editorBehavior=InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard=InputSystem.AddDevice<Keyboard>("CameraValidationKeyboard");mouse=InputSystem.AddDevice<Mouse>("CameraValidationMouse");
        keyboard.MakeCurrent();mouse.MakeCurrent();
        beforeCard=hand.SelectedIndex;beforeDistance=rig.DesiredDistance;
        pitchField=typeof(OrbitCamera).GetField("userPitch",BindingFlags.NonPublic|BindingFlags.Instance);
    }
    void Update()
    {
        elapsed+=Time.deltaTime;
        if(elapsed>.2f&&!wheel) {wheel=true;InputSystem.QueueStateEvent(mouse,new MouseState {scroll=new Vector2(0,120)});}
        if(elapsed>.6f&&!zoom)
        {
            wheelCard=hand.SelectedIndex;wheelDistance=rig.DesiredDistance;zoom=true;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftCtrl));
            InputSystem.QueueStateEvent(mouse,new MouseState {scroll=new Vector2(0,120)});
        }
        if(elapsed>1.3f&&!number)
        {
            zoomDistance=rig.DesiredDistance;number=true;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Digit3));
        }
        if(elapsed>1.5f&&!zDown)
        {
            numberCard=hand.SelectedIndex;zDown=true;beforeYaw=transform.rotation;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Z));
        }
        if(elapsed>1.8f&&!zUp)
        {
            zUp=true;yawDegrees=Quaternion.Angle(beforeYaw,transform.rotation);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
        }
        if(elapsed>2.1f&&!qDown)
        {
            qDown=true;beforeQ=transform.rotation;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Q));
        }
        if(elapsed>2.3f&&!qUp)
        {
            qUp=true;qYaw=Quaternion.Angle(beforeQ,transform.rotation);summoned=hand.InHandCount==hand.CardCount-1;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());
        }
        if(elapsed>2.5f&&!drag)
        {
            drag=true;InputSystem.QueueStateEvent(mouse,new MouseState {buttons=1,delta=new Vector2(0,40)});
        }
        if(elapsed>2.7f&&!release)
        {
            release=true;beforePitch=(float)pitchField.GetValue(rig);
            InputSystem.QueueStateEvent(mouse,new MouseState());
        }
        if(elapsed<7f)return;
        finalPitch=(float)pitchField.GetValue(rig);
        bool passed=wheelCard!=beforeCard && Mathf.Abs(wheelDistance-beforeDistance)<.01f && zoomDistance<beforeDistance-.3f
            && numberCard==2 && yawDegrees>10f && summoned && qYaw<1f && Mathf.Abs(beforePitch-finalPitch)<.01f && beforePitch<rig.pitch-1f;
        File.WriteAllText("Temp/CameraInput.txt",string.Format(CultureInfo.InvariantCulture,
            "passed={0}\nbeforeCard={1}\nwheelCard={2}\nwheelDistance={3}\nbeforeDistance={4}\nzoomDistance={5}\nnumberCard={6}\nzYaw={7}\nqSummoned={8}\nqYaw={9}\nmanuallySetPitch={10}\npitchAfter4s={11}\n",
            passed,beforeCard,wheelCard,wheelDistance,beforeDistance,zoomDistance,numberCard,yawDegrees,summoned,qYaw,beforePitch,finalPitch));
        Destroy(this);
    }
    void OnDestroy()
    {
        if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
        if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
        if(originalKeyboard!=null&&originalKeyboard.added)originalKeyboard.MakeCurrent();
        if(originalMouse!=null&&originalMouse.added)originalMouse.MakeCurrent();
        InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=editorBehavior;
    }
}
