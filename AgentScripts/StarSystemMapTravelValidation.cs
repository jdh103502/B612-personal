using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using B612;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class StarSystemMapTravelValidation
{
    static void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    static void Reject(Action action,string reason){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(InvalidOperationException){rejected=true;}Check(rejected,reason);}
    static StarSystemData Copy(StarSystemData data)=>JsonUtility.FromJson<StarSystemData>(JsonUtility.ToJson(data));
    public static object Validate()
    {
        var flow=UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();var settings=flow.worldHost.settings;
        var results=new List<string>();
        foreach(var pair in new[]{new{scale=.7f,size=PlanetSizeClass.Small},new{scale=1f,size=PlanetSizeClass.Medium},new{scale=1.5f,size=PlanetSizeClass.Large},new{scale=2f,size=PlanetSizeClass.Large}})
            Check(PlanetGenerationSettings.Classify(pair.scale)==pair.size,"Scale classification boundary");
        foreach(float value in new[]{.699f,2.001f,float.NaN,float.PositiveInfinity})Reject(()=>PlanetGenerationSettings.Classify(value),"Invalid scale accepted");
        var original=StarSystemFactory.Create(612101,settings);
        Check(JsonUtility.ToJson(original)==JsonUtility.ToJson(StarSystemFactory.Create(612101,settings)),"Same seed did not reproduce data");
        for(int i=0;i<100;i++){var data=StarSystemFactory.Create(i,settings);Check(data.planets.Count==6&&data.links.Count==7,"Wrong fixed DAG");StarSystemFactory.Validate(data,settings);}
        results.Add("0.7/1/1.5/2 boundaries; invalid scales rejected; 100 seeds and exact repeatability");
        var invalid=Copy(original);invalid.planets[1].spec.planetId=invalid.planets[0].spec.planetId;Reject(()=>StarSystemFactory.Validate(invalid,settings),"Duplicate IDs accepted");
        invalid=Copy(original);invalid.links[0].toPlanetId="missing";Reject(()=>StarSystemFactory.Validate(invalid,settings),"Unknown link target accepted");
        invalid=Copy(original);invalid.links[0].toPlanetId=invalid.startPlanetId;Reject(()=>StarSystemFactory.Validate(invalid,settings),"Cyclic/backwards link accepted");
        invalid=Copy(original);invalid.links.RemoveAll(l=>l.toPlanetId==invalid.planets[2].spec.planetId);Reject(()=>StarSystemFactory.Validate(invalid,settings),"Unreachable node accepted");
        invalid=Copy(original);invalid.links.RemoveAll(l=>l.fromPlanetId==invalid.planets[3].spec.planetId);Reject(()=>StarSystemFactory.Validate(invalid,settings),"Dead-end node accepted");
        invalid=Copy(original);invalid.planets[1].spec.terrain.radius+=1;Reject(()=>StarSystemFactory.Validate(invalid,settings),"Radius mismatch accepted");
        invalid=Copy(original);invalid.planets[1].spec.terrain.groundColor="#ABC";Reject(()=>StarSystemFactory.Validate(invalid,settings),"Short color accepted");
        original.planets[0].state.phase=PlanetPhase.Departed;Check(original.planets[1].state.phase==PlanetPhase.Unvisited,"Shared planet states");
        results.Add("duplicate/missing/cyclic/unreachable/dead-end graph, radius/color validation; independent state");
        if(Application.isPlaying)ValidateWorlds(flow,results);
        System.IO.Directory.CreateDirectory("Temp");System.IO.File.WriteAllLines("Temp/StarSystemMapTravelValidation.txt",results);
        return new{passed=true,cases=results.ToArray()};
    }
    static void ValidateWorlds(StarSystemFlowController flow,List<string> results)
    {
        float timeScale=Time.timeScale;Time.timeScale=1;
        var scene=SceneManager.CreateScene("Planet template physics validation",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var root=new GameObject("Planet scale fixture");SceneManager.MoveGameObjectToScene(root,scene);root.transform.position=new Vector3(1000,0,0);
        var host=root.AddComponent<PlanetWorldHost>();host.settings=flow.worldHost.settings;
        var actor=new GameObject("Walker");actor.transform.SetParent(root.transform,false);var body=actor.AddComponent<Rigidbody>();
        var capsule=actor.AddComponent<CapsuleCollider>();capsule.radius=.13f;capsule.height=.78f;capsule.center=Vector3.up*.39f;
        capsule.sharedMaterial=flow.player.GetComponent<CapsuleCollider>().sharedMaterial;
        var motor=actor.AddComponent<GravityWalker>();motor.enabled=false;var ability=actor.AddComponent<PlayerTraversalAbilities>();
        var tick=typeof(GravityWalker).GetMethod("TickTraversal",BindingFlags.Instance|BindingFlags.NonPublic);
        Action step=()=>{tick.Invoke(motor,new object[]{.02f});scene.GetPhysicsScene().Simulate(.02f);actor.transform.SetPositionAndRotation(body.position,body.rotation);};
        var originals=host.settings.originalMaterials.Select(m=>m.color).ToArray();
        try
        {
            foreach(float scale in new[]{2f,.7f,1f})
            {
                var planet=JsonUtility.FromJson<PlanetData>(JsonUtility.ToJson(flow.Current));planet.spec.terrain.sizeScale=scale;planet.spec.sizeClass=PlanetGenerationSettings.Classify(scale);planet.spec.terrain.radius=host.settings.nominalRadius*scale;planet.spec.terrain.groundColor="#B8A6D8";
                var world=host.Prepare(planet,capsule);host.Activate(world);
                Check(Vector3.Distance(world.ground.transform.localScale,host.settings.baselineScale*scale)<.0001f,"Cumulative scale");
                Check(world.surface.transform.lossyScale==Vector3.one,"Surface actors inherit planet scale");
                Check(world.gravity.influenceRadius>world.ground.bounds.extents.magnitude+6f,"Gravity margin too small");
                Check(world.groundRenderer.sharedMaterials.All(m=>!host.settings.originalMaterials.Contains(m)),"Shared original material modified");
                var colors=world.groundRenderer.sharedMaterials.Select(m=>m.color).ToArray();
                var repeat=host.Prepare(planet,capsule);Check(repeat.groundRenderer.sharedMaterials.Select(m=>m.color).SequenceEqual(colors),"Repeated material tint drift");repeat.DisposeWorld();
                foreach(var radial in new[]{Vector3.up,new Vector3(.35f,1,.2f).normalized,Vector3.right,Vector3.down})
                {
                    Check(world.surface.TryProject(radial,out var point,out var normal),"No directional ground");motor.Teleport(point+normal*.04f,normal);Physics.SyncTransforms();for(int i=0;i<12;i++)step();
                    Check(motor.Grounded,"No initial support on scaled world");int falseAir=0;var trace=new List<string>();
                    for(int i=0;i<220;i++)
                    {
                        Vector3 up=(body.position-root.transform.position).normalized;motor.moveInput=Vector3.Cross(up,Vector3.forward).normalized;step();if(!motor.Grounded)falseAir++;
                        Check(motor.Source==world.gravity,"Scaled world lost gravity");
                        trace.Add($"{i},grounded={motor.Grounded},support={motor.HasSupport},normalV={motor.SupportNormalVelocity:R},radialV={motor.RadialSpeed:R},speed={motor.TangentSpeed:R},p={body.position-root.transform.position},plane={motor.MovementUp}");
                    }
                    System.IO.Directory.CreateDirectory("Temp");System.IO.File.WriteAllLines("Temp/PlanetScaleTrace.txt",trace);
                    Check(falseAir==0,$"Scale {scale}, direction {radial}: {falseAir} false airborne ticks");
                    motor.moveInput=Vector3.zero;motor.jumpHeld=true;motor.Jump();step();for(int i=0;i<4;i++)step();motor.Jump();step();Check(ability.AirJumpsRemaining==0,"Scaled air jump failed");
                    motor.RequestDash();step();Check(ability.IsDashing,"Scaled air dash failed");bool glided=false;
                    for(int i=0;i<95;i++){step();glided|=ability.IsGliding;Check(motor.Source==world.gravity,"Flight escaped influence");}
                    Check(glided,"Scaled globe did not support glide");
                }
                world.DisposeWorld();results.Add($"scale {scale}: template/colors repeat, actors unscaled, four directions grounded, jump/air jump/dash/glide and gravity");
            }
            Check(host.settings.originalMaterials.Select(m=>m.color).SequenceEqual(originals),"Original material colors changed");
        }
        finally {UnityEngine.Object.DestroyImmediate(root);SceneManager.UnloadSceneAsync(scene);Time.timeScale=timeScale;}
    }
}
