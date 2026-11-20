using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using B612;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class TraversalTestCourseValidation
{
    [Serializable] public sealed class Trial
    {
        public int station;public string profile,route;public bool success;public float seconds,peakHeight,landingX,furthestX,glideUsed;
        public int groundJumps,airJumps,dashes,extraAirRecoveries,airTicksOnWalk;public float maxSlope;
    }
    [Serializable] public sealed class Report{public bool passed;public List<Trial> trials=new List<Trial>();}
    public static object Validate()
    {
        var course=UnityEngine.Object.FindFirstObjectByType<TraversalTestCourse>();
        if(!course)throw new InvalidOperationException("Open the fixed traversal scene.");
        var report=new Report();
        using(var fixture=new Fixture(course))
        {
            for(int station=0;station<3;station++)
            {
                report.trials.Add(fixture.Run(station,"Basic",false,false,false,true,.7f,.65f));
                report.trials.Add(fixture.Run(station,"Basic",false,false,false,false,.7f,.65f));
                report.trials.Add(fixture.Run(station,"Dash",true,false,false,false,.7f,.65f));
                report.trials.Add(fixture.Run(station,"AirJump",false,true,false,false,.7f,.65f));
                report.trials.Add(fixture.Run(station,"All",true,true,true,false,.7f,.65f));
                if(station==2)report.trials.Add(fixture.Run(station,"Glide",false,false,true,false,.7f,.65f));
            }
            // A small input window on the chosen shortcut; don't adopt a one-tick success.
            foreach(float offset in new[]{-.1f,.1f})
            {
                report.trials.Add(fixture.Run(0,"Dash timing "+offset,true,false,false,false,.7f,.65f+offset));
                report.trials.Add(fixture.Run(1,"AirJump timing "+offset,false,true,false,false,.7f+offset,.65f));
                report.trials.Add(fixture.Run(0,"Dash launch "+offset,true,false,false,false,.7f,.65f,offset));
                report.trials.Add(fixture.Run(2,"Glide launch "+offset,false,false,true,false,.7f,.65f,offset));
            }
            // Walk out from the bottom of both depressions with every extended ability off.
            foreach(int station in new[]{0,2})report.trials.Add(fixture.Escape(station));
        }
        report.passed=report.trials.Where(t=>t.route=="Bypass"||t.route=="Escape"||t.profile.Contains("timing")||t.profile.Contains("launch")).All(t=>t.success)
            &&report.trials.Where(t=>t.profile=="Basic"&&t.route=="Shortcut").All(t=>!t.success)
            &&report.trials.Any(t=>t.station==0&&t.profile=="Dash"&&t.success)
            &&report.trials.Any(t=>t.station==1&&t.profile=="AirJump"&&t.success)
            &&report.trials.Any(t=>t.station==2&&t.profile=="Glide"&&t.success)
            &&report.trials.Where(t=>t.profile=="All").All(t=>t.success);
        System.IO.File.WriteAllText("Temp/TraversalTestCourseValidation.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        return report;
    }
    sealed class Fixture:IDisposable
    {
        readonly Scene scene;readonly GameObject root;readonly MeshCollider ground;
        readonly Rigidbody body;readonly GravityWalker motor;readonly PlayerTraversalAbilities ability;
        readonly MethodInfo tick=typeof(GravityWalker).GetMethod("TickTraversal",BindingFlags.Instance|BindingFlags.NonPublic);
        Trial current;int recoveries;bool jumped;float stepSeconds=.02f;
        Vector3 Origin=>root.transform.position;
        public Fixture(TraversalTestCourse course)
        {
            scene=SceneManager.CreateScene("Fixed course physics check",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            root=new GameObject("Course fixture");SceneManager.MoveGameObjectToScene(root,scene);root.transform.position=new Vector3(1000,0,0);
            var gravity=root.AddComponent<GravitySource>();gravity.surfaceRadius=12;gravity.influenceRadius=32;gravity.strength=course.gravity.strength;
            var groundGO=new GameObject("Exact baked course collider");groundGO.transform.SetParent(root.transform,false);ground=groundGO.AddComponent<MeshCollider>();ground.sharedMesh=course.ground.sharedMesh;
            var actor=new GameObject("Actual motor fixture");actor.transform.SetParent(root.transform,false);body=actor.AddComponent<Rigidbody>();
            var capsule=actor.AddComponent<CapsuleCollider>();var sourceCapsule=course.player.GetComponent<CapsuleCollider>();
            capsule.radius=sourceCapsule.radius;capsule.height=sourceCapsule.height;capsule.center=sourceCapsule.center;capsule.sharedMaterial=sourceCapsule.sharedMaterial;
            motor=actor.AddComponent<GravityWalker>();JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(course.player.GetComponent<GravityWalker>()),motor);motor.enabled=false;
            ability=actor.AddComponent<PlayerTraversalAbilities>();JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(course.player.GetComponent<PlayerTraversalAbilities>()),ability);
            motor.Jumped+=()=>{if(current!=null)current.groundJumps++;};ability.AirJumped+=()=>{if(current!=null)current.airJumps++;};
            ability.LandingRecovered+=()=>{if(jumped)recoveries++;};
        }
        Vector2 Local(int station)=>TraversalTestCourseGeometry.Coordinates(station,body.position-Origin);
        Vector3 Point(int station,Vector2 local)
        {
            var radial=TraversalTestCourseGeometry.Direction(station,local.x,local.y);
            if(!ground.Raycast(new Ray(Origin+radial*25,-radial),out var hit,30))throw new InvalidOperationException("No baked ground");
            return hit.point;
        }
        void Reset(int station,Vector2 start,bool dash,bool air,bool glide)
        {
            current=null;jumped=false;recoveries=0;
            var radial=TraversalTestCourseGeometry.Direction(station,start.x,start.y);var point=Point(station,start);
            body.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(TraversalTestCourseGeometry.Direction(station,start.x+.1f,start.y)-radial,radial),radial);
            motor.Teleport(point+radial*.04f,radial);ability.SetAllowed(TraversalFeature.Dash,dash);ability.SetAllowed(TraversalFeature.AirJump,air);ability.SetAllowed(TraversalFeature.Glide,glide);ability.SetInputEnabled(true);
            Physics.SyncTransforms();for(int i=0;i<12;i++)Step();
            if(!motor.Grounded)throw new InvalidOperationException("No support at start");
        }
        void Move(int station,Vector2 destination)
        {
            var p=Local(station);var difference=destination-p;
            var radial=(body.position-Origin).normalized;
            var east=Vector3.ProjectOnPlane(TraversalTestCourseGeometry.Direction(station,p.x+.05f,p.y)-radial,radial).normalized;
            var north=Vector3.ProjectOnPlane(TraversalTestCourseGeometry.Direction(station,p.x,p.y+.05f)-radial,radial).normalized;
            motor.moveInput=(east*difference.x+north*difference.y).normalized;
        }
        void Step()
        {
            tick.Invoke(motor,new object[]{stepSeconds});scene.GetPhysicsScene().Simulate(stepSeconds);
            motor.transform.SetPositionAndRotation(body.position,body.rotation);
            if(current!=null)
            {
                current.seconds+=stepSeconds;current.peakHeight=Mathf.Max(current.peakHeight,(body.position-Origin).magnitude-12f);
                current.furthestX=Mathf.Max(current.furthestX,Local(current.station).x);
                current.maxSlope=Mathf.Max(current.maxSlope,Vector3.Angle(motor.MovementUp,(body.position-Origin).normalized));
                if(current.route!="Shortcut"&&!motor.Grounded)current.airTicksOnWalk++;
            }
        }
        public Trial Run(int station,string profile,bool dash,bool air,bool glide,bool bypass,float secondAt,float dashAt,float launchOffset=0)
        {
            Reset(station,TraversalTestCourseGeometry.Start(station),dash,air,glide);
            current=new Trial{station=station,profile=profile,route=bypass?"Bypass":"Shortcut",furthestX=-20,landingX=-20,peakHeight=-20};
            var route=bypass?TraversalTestCourseGeometry.BasicRoute(station):new[]{TraversalTestCourseGeometry.Start(station),TraversalTestCourseGeometry.Goal(station)};
            int next=1;float launch=-1;bool didAir=false,didDash=false,wasDashing=false;int landingTicks=0;
            for(int i=0;i<1600;i++)
            {
                var local=Local(station);
                if(Vector2.Distance(local,route[next])<.22f)
                {if(next==route.Length-1&&motor.Grounded){current.success=true;break;}if(next<route.Length-1)next++;}
                Move(station,route[next]);motor.jumpHeld=!bypass;
                if(!bypass)
                {
                    float launchX=(station==0?-2.4f:station==1?-1f:-2.3f)+launchOffset;
                    if(launch<0&&local.x>=launchX&&motor.Grounded){motor.Jump();launch=current.seconds;jumped=true;}
                    if(launch>=0)
                    {
                        float t=current.seconds-launch;
                        if(air&&!didAir&&t>=secondAt){motor.Jump();didAir=true;}
                        if(dash&&!didDash&&t>=dashAt){motor.RequestDash();didDash=true;}
                    }
                }
                Step();if(ability.IsDashing&&!wasDashing)current.dashes++;wasDashing=ability.IsDashing;
                if(ability.IsGliding)current.glideUsed+=stepSeconds;
                if(launch>=0&&current.seconds-launch>.2f&&motor.Grounded)
                {
                    if(landingTicks++==0)current.landingX=Local(station).x;
                    float expected=TraversalTestCourseGeometry.Height(station,TraversalTestCourseGeometry.Goal(station).x,0);
                    if((body.position-Origin).magnitude-12<expected-.3f || recoveries>1)break;
                }
                if(!bypass&&current.seconds>8)break;
            }
            current.extraAirRecoveries=Mathf.Max(0,recoveries-1);var result=current;current=null;return result;
        }
        public Trial Escape(int station)
        {
            Reset(station,new Vector2(0,0),false,false,false);
            current=new Trial{station=station,profile="Basic",route="Escape",peakHeight=-20,furthestX=-20};
            for(int i=0;i<800;i++){Move(station,new Vector2(0,station==0?5.7f:7.7f));Step();if(Local(station).y>(station==0?5.5f:7.5f)&&motor.Grounded){current.success=true;break;}}
            var result=current;current=null;return result;
        }
        public void Dispose(){UnityEngine.Object.DestroyImmediate(root);SceneManager.UnloadSceneAsync(scene);}
    }
}
