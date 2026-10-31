using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using B612;
using UnityEngine;

public static class CameraScenarioValidation
{
    public static object Start()
    {
        if(!Application.isPlaying) throw new InvalidOperationException("Run in Play mode.");
        Camera.main.gameObject.AddComponent<CameraScenarioProbe>();
        return new {started=true,seconds=18,output="Temp/CameraScenarios.txt"};
    }
}

[DefaultExecutionOrder(1100)]
public sealed class CameraScenarioProbe : MonoBehaviour
{
    OrbitCamera rig;
    GravityWalker walker;
    PlayerController input;
    PlanetSurfaceGrid surface;
    GameObject wall,secondPlanet,starObject;
    SphereCollider sphere;
    float elapsed,maxPenetration,closestWall=float.MaxValue,tallJumpMaxFoot,tallJumpFocusRise;
    float sideMinFoot=1f,bottomMinFoot=1f,transferMaxFov,finalUpError;
    Vector3 jumpStartFocus;
    bool placedWall,removedWall,side,bottom,jumped,flight,sourceConfirmed;
    int frames,transferFrames;
    readonly List<string> failures=new List<string>();

    void Start()
    {
        rig=GetComponent<OrbitCamera>(); walker=rig.target; input=walker.GetComponent<PlayerController>(); input.enabled=false;
        surface=UnityEngine.Object.FindFirstObjectByType<PlanetSurfaceGrid>();
        walker.Teleport(input.spawnPoint.position,input.spawnPoint.up); walker.moveInput=Vector3.zero;
        var sphereObject=new GameObject("Validation camera sphere"); sphere=sphereObject.AddComponent<SphereCollider>(); sphere.radius=rig.collisionRadius*.95f; sphere.enabled=false;
    }
    void PlacePlayer(Vector3 direction)
    {
        if(!surface.TryProject(direction,out var p,out var n)) {failures.Add("Could not project surface fixture.");return;}
        walker.Teleport(p+n*.04f,n); walker.moveInput=Vector3.zero;
    }
    void Update()
    {
        elapsed+=Time.deltaTime; walker.moveInput=Vector3.zero; walker.jumpHeld=false;
        if(elapsed>=7f && elapsed<7.6f) walker.jumpHeld=true;
        if(elapsed>=1f && !placedWall)
        {
            placedWall=true; wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name="Validation camera wall";
            wall.GetComponent<Renderer>().enabled=false;
            var direction=(transform.position-rig.FocusPoint).normalized;
            wall.transform.SetPositionAndRotation(rig.FocusPoint+direction*1.5f,transform.rotation);
            wall.transform.localScale=new Vector3(3f,3f,.4f); Physics.SyncTransforms();
        }
        if(elapsed>=2f && !removedWall) {removedWall=true; wall.SetActive(false);Destroy(wall);}
        if(elapsed>=4f && !side) {side=true;PlacePlayer(Vector3.right);}
        if(elapsed>=5.5f && !bottom) {bottom=true;PlacePlayer(Vector3.down);}
        if(elapsed>=7f && !jumped && walker.Grounded)
        {
            jumped=true;jumpStartFocus=rig.FocusPoint;
            walker.GetComponent<Rigidbody>().linearVelocity=walker.Up*6f;
        }
        if(elapsed>=12f && !flight)
        {
            flight=true;
            walker.Teleport(input.spawnPoint.position,input.spawnPoint.up);
            secondPlanet=GameObject.CreatePrimitive(PrimitiveType.Sphere); secondPlanet.name="Validation destination planet";
            secondPlanet.transform.position=surface.transform.position+Vector3.right*18f;
            secondPlanet.transform.localScale=Vector3.one*4f;
            var source=secondPlanet.AddComponent<GravitySource>(); source.surfaceRadius=2f;source.influenceRadius=6f;
            starObject=new GameObject("Validation Launch Star"); var star=starObject.AddComponent<LaunchStar>();
            var landing=new GameObject("Validation landing").transform; landing.SetParent(secondPlanet.transform,true);
            landing.position=secondPlanet.transform.position+Vector3.up*2.04f;
            star.landing=landing;star.flightTime=2.6f;star.arcHeight=6f;
            star.Launch(walker);
        }
    }
    void LateUpdate()
    {
        frames++;
        foreach(var collider in Physics.OverlapSphere(transform.position,sphere.radius,rig.collisionMask & ~OrbitCamera.IgnoreMask,QueryTriggerInteraction.Ignore))
        {
            if(collider.attachedRigidbody || collider.transform.IsChildOf(walker.transform))continue;
            if(Physics.ComputePenetration(sphere,transform.position,Quaternion.identity,collider,collider.transform.position,collider.transform.rotation,out _,out float depth))
                maxPenetration=Mathf.Max(maxPenetration,depth);
        }
        float foot=GetComponent<Camera>().WorldToViewportPoint(walker.transform.position).y;
        if(elapsed>1.1f && elapsed<1.9f)closestWall=Mathf.Min(closestWall,rig.CurrentDistance);
        if(elapsed>4.4f && elapsed<5.4f)sideMinFoot=Mathf.Min(sideMinFoot,foot);
        if(elapsed>5.9f && elapsed<6.9f)bottomMinFoot=Mathf.Min(bottomMinFoot,foot);
        if(elapsed>7f && elapsed<11.8f)
        {
            tallJumpMaxFoot=Mathf.Max(tallJumpMaxFoot,foot);
            tallJumpFocusRise=Mathf.Max(tallJumpFocusRise,Vector3.Dot(rig.FocusPoint-jumpStartFocus,Vector3.down));
        }
        if(rig.State==OrbitCamera.FollowState.Transfer) {transferFrames++;transferMaxFov=Mathf.Max(transferMaxFov,GetComponent<Camera>().fieldOfView);}
        if(elapsed>16.5f && secondPlanet)
        {
            sourceConfirmed=rig.ReferenceSource && rig.ReferenceSource.gameObject==secondPlanet;
            finalUpError=Vector3.Angle(rig.CameraUp,(walker.transform.position-secondPlanet.transform.position).normalized);
        }
        if(elapsed<18f)return;
        if(maxPenetration>.005f)failures.Add("Camera penetrated geometry.");
        if(closestWall>1.4f)failures.Add("Close wall did not override comfort distance.");
        if(sideMinFoot<.3f || bottomMinFoot<.3f)failures.Add("Surface framing fell into card area.");
        if(tallJumpFocusRise<.5f || tallJumpMaxFoot>.95f)failures.Add("Large-jump height follow failed.");
        if(transferFrames==0 || transferMaxFov<45f || !sourceConfirmed || finalUpError>5f)failures.Add("Launch-star transfer/source confirmation failed.");
        var report=string.Format(CultureInfo.InvariantCulture,
            "passed={0}\nframes={1}\nmaxPenetration={2}\nclosestWall={3}\nsideMinFootY={4}\nbottomMinFootY={5}\ntallJumpMaxFootY={6}\ntallJumpFocusRise={7}\ntransferFrames={8}\ntransferMaxFov={9}\nsourceConfirmed={10}\nfinalUpError={11}\nfailures={12}\n",
            failures.Count==0,frames,maxPenetration,closestWall,sideMinFoot,bottomMinFoot,tallJumpMaxFoot,tallJumpFocusRise,transferFrames,transferMaxFov,sourceConfirmed,finalUpError,string.Join(";",failures));
        File.WriteAllText("Temp/CameraScenarios.txt",report);
        walker.Teleport(input.spawnPoint.position,input.spawnPoint.up);input.enabled=true;
        Destroy(secondPlanet);Destroy(starObject);Destroy(sphere.gameObject);Destroy(this);
    }
}
