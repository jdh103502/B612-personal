using System;
using System.Collections.Generic;
using System.IO;
using B612;
using UnityEngine;

// CLI runtime probe: drives real walker physics, records the rendered camera after LateUpdate.
public static class CameraStabilityValidation
{
    public static object StartBefore() => Start("before");
    public static object StartAfter() => Start("after");
    static object Start(string label)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run in Play mode.");
        var meter=UnityEngine.Object.FindFirstObjectByType<BaobabThreatMeter>();
        var surge=UnityEngine.Object.FindFirstObjectByType<BaobabSurge>();
        if(meter) meter.enabled=false;
        if(surge) surge.enabled=false;
        Time.timeScale=1f;
        var probe=Camera.main.gameObject.AddComponent<CameraStabilityProbe>();
        probe.label=label;
        return new {started=true,label,seconds=24,output="Temp/CameraStability-"+label+".csv"};
    }
}

[DefaultExecutionOrder(1000)]
public sealed class CameraStabilityProbe : MonoBehaviour
{
    [Serializable] public sealed class Sample
    {
        public float t,dt,lift,distance,fov,relativeDegreesPerSecond,normalToRadial,pitch,footY;
        public bool grounded;
        public string stage;
    }
    [Serializable] public sealed class Result
    {
        public string label;
        public List<Sample> frames=new List<Sample>();
    }
    public string label;
    OrbitCamera rig;
    GravityWalker walker;
    PlayerController input;
    Vector3 heading,lastRadial;
    Quaternion lastRotation;
    float elapsed;
    bool jumped,walkingJump,returnJump;
    readonly Result result=new Result();

    void Start()
    {
        rig=GetComponent<OrbitCamera>(); walker=rig.target;
        input=walker.GetComponent<PlayerController>(); input.enabled=false;
        heading=walker.transform.forward;
        lastRadial=Radial(); lastRotation=transform.rotation;
        result.label=label;
    }
    Vector3 Radial()
    {
        var source=walker.Source ? walker.Source : GravitySource.Find(walker.transform.position);
        return source ? (walker.transform.position-source.transform.position).normalized : walker.Up;
    }
    void Update()
    {
        elapsed+=Time.deltaTime;
        Vector3 radial=Radial();
        heading=Quaternion.FromToRotation(lastRadial,radial)*heading;
        walker.moveInput=Vector3.zero; walker.jumpHeld=false;
        if(elapsed>=3f && !jumped && walker.Grounded) { walker.Jump(); jumped=true; }
        if(elapsed>=3f && elapsed<3.35f) walker.jumpHeld=true;
        if(elapsed>=6f && elapsed<14f) walker.moveInput=Vector3.ProjectOnPlane(heading,walker.Up).normalized;
        if(elapsed>=9f && !walkingJump && walker.Grounded) {walker.Jump(); walkingJump=true;}
        if(elapsed>=9f && elapsed<9.35f) walker.jumpHeld=true;
        if(elapsed>=14f && elapsed<22f) walker.moveInput=-Vector3.ProjectOnPlane(heading,walker.Up).normalized;
        if(elapsed>=17f && !returnJump && walker.Grounded) {walker.Jump();returnJump=true;}
        if(elapsed>=17f && elapsed<17.35f) walker.jumpHeld=true;
    }
    void LateUpdate()
    {
        Vector3 radial=Radial();
        var transported=Quaternion.FromToRotation(lastRadial,radial)*lastRotation;
        result.frames.Add(new Sample {t=elapsed,dt=Time.deltaTime,lift=rig.Lift,distance=rig.CurrentDistance,
            fov=GetComponent<Camera>().fieldOfView,relativeDegreesPerSecond=Quaternion.Angle(transported,transform.rotation)/Mathf.Max(Time.deltaTime,.00001f),
            normalToRadial=Vector3.Angle(walker.Up,radial),pitch=Mathf.Asin(Mathf.Clamp(Vector3.Dot(transform.forward,radial),-1f,1f))*Mathf.Rad2Deg,
            footY=GetComponent<Camera>().WorldToViewportPoint(walker.transform.position).y,
            grounded=walker.Grounded,stage=elapsed<3?"settle":elapsed<6?"standingJump":elapsed<14?"outward":elapsed<22?"return":"settleEnd"});
        lastRadial=radial; lastRotation=transform.rotation;
        if(elapsed<24f) return;
        walker.moveInput=Vector3.zero; walker.jumpHeld=false;
        input.enabled=true;
        Directory.CreateDirectory("Temp");
        var csv=new System.Text.StringBuilder("t,dt,lift,distance,fov,relativeDegreesPerSecond,normalToRadial,pitch,footY,grounded,stage\n");
        foreach(var f in result.frames)
            csv.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,"{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}\n",
                f.t,f.dt,f.lift,f.distance,f.fov,f.relativeDegreesPerSecond,f.normalToRadial,f.pitch,f.footY,f.grounded,f.stage);
        File.WriteAllText("Temp/CameraStability-"+label+".csv",csv.ToString());
        Destroy(this);
    }
}
