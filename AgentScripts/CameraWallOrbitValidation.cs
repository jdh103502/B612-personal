using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using B612;
using UnityEditor;
using UnityEngine;

public static class CameraWallOrbitValidation
{
    public static object Before()
    {
        var c=Camera.main.GetComponent<OrbitCamera>();
        SessionState.SetVector3("B612.WallOrbit.Position",c.target.transform.position);
        SessionState.SetVector3("B612.WallOrbit.Up",c.target.Up);
        SessionState.SetVector3("B612.WallOrbit.Forward",c.target.transform.forward);
        SessionState.SetFloat("B612.WallOrbit.Pitch",(float)typeof(OrbitCamera).GetField("userPitch",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(c));
        return Begin("before");
    }
    public static object After()
    {
        var c=Camera.main.GetComponent<OrbitCamera>();
        var up=SessionState.GetVector3("B612.WallOrbit.Up",Vector3.up);
        c.target.Teleport(SessionState.GetVector3("B612.WallOrbit.Position",c.target.transform.position),up);
        c.target.transform.rotation=Quaternion.LookRotation(SessionState.GetVector3("B612.WallOrbit.Forward",Vector3.forward),up);
        return Begin("after");
    }
    static object Begin(string label)
    {
        var meter=UnityEngine.Object.FindFirstObjectByType<BaobabThreatMeter>();
        if(meter)meter.enabled=false;
        var surge=UnityEngine.Object.FindFirstObjectByType<BaobabSurge>();if(surge)surge.enabled=false;
        Time.timeScale=1f;
        var probe=Camera.main.gameObject.AddComponent<CameraWallOrbitProbe>();probe.label=label;
        return new {started=true,label,seconds=11,output="Temp/CameraWallOrbit-"+label+".csv"};
    }
}

[DefaultExecutionOrder(1300)]
public sealed class CameraWallOrbitProbe : MonoBehaviour
{
    public string label;
    OrbitCamera rig;GravityWalker walker;PlayerController input;
    FieldInfo heading,lastManual,pitch;
    float elapsed,lastRecorded;
    Vector3 lastPosition;
    float lastDistance;
    int samples,closeFrames;
    float minDistance=100f,maxStep,maxDistanceStep,maxPenetration;
    SphereCollider sphere;
    readonly System.Text.StringBuilder csv=new System.Text.StringBuilder("time,distance,step,distanceStep,lift,footY\n");
    void Start()
    {
        rig=GetComponent<OrbitCamera>();walker=rig.target;input=walker.GetComponent<PlayerController>();input.enabled=false;
        heading=typeof(OrbitCamera).GetField("flatForward",BindingFlags.NonPublic|BindingFlags.Instance);
        lastManual=typeof(OrbitCamera).GetField("lastManual",BindingFlags.NonPublic|BindingFlags.Instance);
        pitch=typeof(OrbitCamera).GetField("userPitch",BindingFlags.NonPublic|BindingFlags.Instance);
        heading.SetValue(rig,Vector3.ProjectOnPlane(SessionState.GetVector3("B612.WallOrbit.Forward",walker.transform.forward),rig.CameraUp).normalized);
        pitch.SetValue(rig,SessionState.GetFloat("B612.WallOrbit.Pitch",30f));
        var go=new GameObject("Wall orbit collision test sphere");sphere=go.AddComponent<SphereCollider>();sphere.radius=.19f;sphere.enabled=false;
    }
    void Update()
    {
        elapsed+=Time.deltaTime;walker.moveInput=Vector3.zero;walker.jumpHeld=false;
        float turn=elapsed>=1f&&elapsed<5f?90f:elapsed>=5f&&elapsed<9f?-90f:0f;
        var forward=(Vector3)heading.GetValue(rig);
        heading.SetValue(rig,Quaternion.AngleAxis(turn*Time.deltaTime,rig.CameraUp)*forward);
        lastManual.SetValue(rig,Time.time);
    }
    void LateUpdate()
    {
        if(elapsed<1f){lastPosition=transform.position;lastDistance=rig.CurrentDistance;return;}
        foreach(var col in Physics.OverlapSphere(transform.position,sphere.radius,rig.collisionMask & ~OrbitCamera.IgnoreMask,QueryTriggerInteraction.Ignore))
            if(!col.attachedRigidbody && !col.transform.IsChildOf(walker.transform)
                &&Physics.ComputePenetration(sphere,transform.position,Quaternion.identity,col,col.transform.position,col.transform.rotation,out _,out float depth))
                maxPenetration=Mathf.Max(maxPenetration,depth);
        minDistance=Mathf.Min(minDistance,rig.CurrentDistance);
        if(rig.CurrentDistance<1.4f)closeFrames++;
        if(elapsed-lastRecorded>=1f/60f)
        {
            float step=Vector3.Distance(lastPosition,transform.position),distanceStep=Mathf.Abs(lastDistance-rig.CurrentDistance);
            maxStep=Mathf.Max(maxStep,step);maxDistanceStep=Mathf.Max(maxDistanceStep,distanceStep);samples++;
            csv.AppendFormat(CultureInfo.InvariantCulture,"{0},{1},{2},{3},{4},{5}\n",elapsed,rig.CurrentDistance,step,distanceStep,rig.Lift,GetComponent<Camera>().WorldToViewportPoint(walker.transform.position).y);
            lastPosition=transform.position;lastDistance=rig.CurrentDistance;lastRecorded=elapsed;
        }
        if(elapsed<11f)return;
        Directory.CreateDirectory("Temp");File.WriteAllText("Temp/CameraWallOrbit-"+label+".csv",csv.ToString());
        File.WriteAllText("Temp/CameraWallOrbit-"+label+".txt",string.Format(CultureInfo.InvariantCulture,
            "samples={0}\nminDistance={1}\ncloseFrames={2}\nmaxStep={3}\nmaxDistanceStep={4}\nmaxPenetration={5}\n",samples,minDistance,closeFrames,maxStep,maxDistanceStep,maxPenetration));
        input.enabled=true;walker.moveInput=Vector3.zero;Destroy(sphere.gameObject);Destroy(this);
    }
}
