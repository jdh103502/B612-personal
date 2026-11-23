using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace B612
{
    /// <summary>Bounded preparation-time walk checks on the final collider using the real, unpowered motor.</summary>
    public static class TerrainTraversalVerifier
    {
        static int sequence;
        [Serializable] public sealed class Cost { public int kind;public float basicSeconds,shortcutSeconds; }
        public sealed class Verdict{public bool passed;public int feature=-1,landform=-1;public string reason;public float maximumSlope,mainRouteSeconds;public List<Cost> costs=new List<Cost>();}
        public static Verdict Verify(MeshCollider source,CapsuleCollider player,ProceduralPlanetTerrain.Plan plan)
        {
            var verdict=new Verdict();var scene=SceneManager.CreateScene("Terrain walk check "+(++sequence),new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var root=new GameObject("Temporary terrain verification");SceneManager.MoveGameObjectToScene(root,scene);root.transform.position=new Vector3(10000,10000,10000);
            try
            {
                var gravity=root.AddComponent<GravitySource>();gravity.surfaceRadius=plan.radius;gravity.influenceRadius=plan.radius+10;gravity.terrainPlan=plan;
                var sourceGravity=source.GetComponentInParent<PlanetWorldInstance>();gravity.strength=sourceGravity?sourceGravity.Data.spec.terrain.gravityStrength:3.4f;
                var groundGO=new GameObject("Final terrain collider");groundGO.transform.SetParent(root.transform,false);var ground=groundGO.AddComponent<MeshCollider>();
                ground.cookingOptions = source.cookingOptions;
                ground.sharedMesh=source.sharedMesh;
                var actor=new GameObject("Basic traversal probe");actor.transform.SetParent(root.transform,false);var body=actor.AddComponent<Rigidbody>();var capsule=actor.AddComponent<CapsuleCollider>();
                capsule.radius=player?player.radius:.13f;capsule.height=player?player.height:.78f;capsule.center=player?player.center:Vector3.up*.39f;capsule.direction=player?player.direction:1;capsule.sharedMaterial=player?player.sharedMaterial:null;
                if(player)actor.transform.localScale=player.transform.lossyScale;
                var motor=actor.AddComponent<GravityWalker>();if(player && player.TryGetComponent<GravityWalker>(out var existing))JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(existing),motor);motor.enabled=false;
                var ability=actor.AddComponent<PlayerTraversalAbilities>();if(player && player.TryGetComponent<PlayerTraversalAbilities>(out var existingAbility))JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(existingAbility),ability);
                ability.SetUnlocked(TraversalCapability.All);
                ability.SetAllowed(TraversalFeature.Dash,false);ability.SetAllowed(TraversalFeature.AirJump,false);ability.SetAllowed(TraversalFeature.Glide,false);
                var main=ProceduralPlanetTerrain.MainRoute(plan);
                if(!Walk(main,ground,root.transform.position,motor,body,scene.GetPhysicsScene(),plan.radius,ref verdict.maximumSlope,out verdict.mainRouteSeconds))
                {verdict.reason="Main protected route failed; use flat safe template.";return verdict;}
                main.Reverse();
                if(!Walk(main,ground,root.transform.position,motor,body,scene.GetPhysicsScene(),plan.radius,ref verdict.maximumSlope,out _))
                {verdict.reason="Main protected return route failed; use flat safe template.";return verdict;}
                foreach (var connector in plan.connectors)
                {
                    var route = new List<Vector3> { connector.anchor, connector.pad };
                    bool reachedPad = Walk(route, ground, root.transform.position, motor, body, scene.GetPhysicsScene(),
                        plan.radius, ref verdict.maximumSlope, out _);
                    route.Reverse();
                    if (!reachedPad || !Walk(route, ground, root.transform.position, motor, body, scene.GetPhysicsScene(),
                        plan.radius, ref verdict.maximumSlope, out _))
                    {
                        verdict.feature = 0;
                        verdict.reason = "Reviewed course connector failed in one or both directions.";
                        return verdict;
                    }
                }
                for(int i=0;i<plan.features.Count;i++)
                {
                    var feature=plan.features[i];var route=ProceduralPlanetTerrain.FeatureRoute(plan,feature);
                    if(!Walk(route,ground,root.transform.position,motor,body,scene.GetPhysicsScene(),plan.radius,ref verdict.maximumSlope,out float basicSeconds))
                    {verdict.feature=i;verdict.reason="Feature "+i+": basic bypass failed.";return verdict;}
                    route.Reverse();
                    if(!Walk(route,ground,root.transform.position,motor,body,scene.GetPhysicsScene(),plan.radius,ref verdict.maximumSlope,out _)
                        ||feature.kind!=1&&!Walk(ProceduralPlanetTerrain.FeatureRoute(plan,feature,true),ground,root.transform.position,motor,body,scene.GetPhysicsScene(),plan.radius,ref verdict.maximumSlope,out _))
                    {verdict.feature=i;verdict.reason="Feature "+i+": return/valley escape failed.";return verdict;}
                    bool basic=Shortcut(plan,feature,false,ground,root.transform.position,motor,body,scene.GetPhysicsScene(),out _);
                    bool powered=Shortcut(plan,feature,true,ground,root.transform.position,motor,body,scene.GetPhysicsScene(),out float shortcutSeconds);
                    if(basic || !powered)
                    {verdict.feature=i;verdict.reason="Feature "+i+": shortcut basic="+basic+", powered="+powered+" at this radius.";return verdict;}
                    if(shortcutSeconds>=basicSeconds){verdict.feature=i;verdict.reason="Feature "+i+": shortcut has no time benefit; omitted.";return verdict;}
                    verdict.costs.Add(new Cost{kind=feature.kind,basicSeconds=basicSeconds,shortcutSeconds=shortcutSeconds});
                }
                for (int i = 0; i < plan.landforms.Count; i++)
                {
                    var approach = plan.landforms[i].approach;
                    var route = new List<Vector3> { approach.anchor, approach.pad };
                    bool reached = Walk(route, ground, root.transform.position, motor, body, scene.GetPhysicsScene(),
                        plan.radius, ref verdict.maximumSlope, out _);
                    route.Reverse();
                    if (!reached || !Walk(route, ground, root.transform.position, motor, body, scene.GetPhysicsScene(),
                        plan.radius, ref verdict.maximumSlope, out _))
                    {
                        verdict.landform = i;
                        verdict.reason = $"Landform {i}: basic access or return failed.";
                        return verdict;
                    }
                }
                verdict.passed=true;return verdict;
            }
            finally {root.SetActive(false);UnityEngine.Object.DestroyImmediate(root);SceneManager.UnloadSceneAsync(scene);}
        }
        static bool Walk(List<Vector3> route,MeshCollider ground,Vector3 origin,GravityWalker motor,Rigidbody body,PhysicsScene physics,float radius,ref float maximumSlope,out float seconds)
        {
            seconds=0;
            motor.Traversal.SetAllowed(TraversalFeature.Dash,false);motor.Traversal.SetAllowed(TraversalFeature.AirJump,false);motor.Traversal.SetAllowed(TraversalFeature.Glide,false);
            if(!ground.Raycast(new Ray(origin+route[0]*(radius+20),-route[0]),out var hit,radius+40))return false;
            body.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(route[1]-route[0],hit.normal),hit.normal);motor.Teleport(hit.point+hit.normal*.04f,hit.normal);motor.Traversal.SetInputEnabled(true);Physics.SyncTransforms();
            for(int i=0;i<12;i++){motor.TickTraversal(.02f);physics.Simulate(.02f);motor.transform.SetPositionAndRotation(body.position,body.rotation);}
            if(!motor.Grounded)return false;
            float length=0;for(int i=1;i<route.Count;i++)length+=Mathf.Acos(Mathf.Clamp(Vector3.Dot(route[i-1],route[i]),-1,1))*radius;
            int limit=Mathf.Min(9000,Mathf.CeilToInt((length/Mathf.Max(.1f,motor.moveSpeed)+8)/.02f));int next=1,airTicks=0;
            for(int tick=0;tick<limit;tick++)
            {
                seconds=(tick+1)*.02f;
                var radial=(body.position-origin).normalized;float distance=Mathf.Acos(Mathf.Clamp(Vector3.Dot(radial,route[next]),-1,1))*radius;
                if(distance<.22f){if(++next==route.Count)return motor.Grounded;}
                motor.moveInput=Vector3.ProjectOnPlane(route[next]-radial,radial).normalized;
                motor.TickTraversal(.02f);physics.Simulate(.02f);motor.transform.SetPositionAndRotation(body.position,body.rotation);
                maximumSlope=Mathf.Max(maximumSlope,Vector3.Angle(motor.MovementUp,radial));
                airTicks=motor.Grounded?0:airTicks+1;if(airTicks>4 ||Vector3.Distance(body.position,origin)<radius-8)return false;
            }
            return false;
        }
        static bool Shortcut(ProceduralPlanetTerrain.Plan plan,ProceduralPlanetTerrain.Feature feature,bool powered,MeshCollider ground,Vector3 origin,GravityWalker motor,Rigidbody body,PhysicsScene physics,out float seconds)
        {
            seconds=0;
            var start=feature.Direction(TraversalTestCourseGeometry.Start(feature.kind),plan.radius);
            var goal=feature.Direction(TraversalTestCourseGeometry.Goal(feature.kind),plan.radius);
            if(!ground.Raycast(new Ray(origin+start*(plan.radius+20),-start),out var hit,plan.radius+40))return false;
            body.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(goal-start,hit.normal),hit.normal);motor.Teleport(hit.point+hit.normal*.04f,hit.normal);
            motor.Traversal.SetAllowed(TraversalFeature.Dash,powered&&feature.kind==0);motor.Traversal.SetAllowed(TraversalFeature.AirJump,powered&&feature.kind==1);motor.Traversal.SetAllowed(TraversalFeature.Glide,powered&&feature.kind==2);motor.Traversal.SetInputEnabled(true);
            for(int i=0;i<12;i++){motor.TickTraversal(.02f);physics.Simulate(.02f);motor.transform.SetPositionAndRotation(body.position,body.rotation);}
            bool jumped=false,extra=false,dash=false, usedAbility=false;float flight=0;
            for(int i=0;i<450;i++)
            {
                seconds=(i+1)*.02f;
                var radial=(body.position-origin).normalized;var local=feature.Coordinates(radial,plan.radius);
                motor.moveInput=Vector3.ProjectOnPlane(goal-radial,radial).normalized;motor.jumpHeld=true;
                if(!jumped && motor.Grounded && local.x>=(feature.kind==0?-2.4f:feature.kind==1?-1f:-2.3f)){motor.Traversal.QueueSimulationInput(true,false);jumped=true;}
                if(jumped)
                {
                    flight+=.02f;
                    if(powered&&feature.kind==0&&!dash&&flight>=.65f){motor.Traversal.QueueSimulationInput(false,true);dash=true;}
                    if(powered&&feature.kind==1&&!extra&&flight>=.70f){motor.Traversal.QueueSimulationInput(true,false);extra=true;}
                }
                motor.TickTraversal(.02f);physics.Simulate(.02f);motor.transform.SetPositionAndRotation(body.position,body.rotation);
                var ability = motor.Traversal;
                usedAbility |= feature.kind == 0 ? ability.IsDashing :
                    feature.kind == 1 ? ability.AirJumpsRemaining == 0 : ability.IsGliding;
                if(jumped&&motor.Grounded&&flight>.2f && Vector3.Distance(body.position,origin)<plan.radius+TraversalTestCourseGeometry.Height(feature.kind,TraversalTestCourseGeometry.Goal(feature.kind).x,0)-.3f)return false;
                if(motor.Grounded&&Mathf.Acos(Mathf.Clamp(Vector3.Dot((body.position-origin).normalized,goal),-1,1))*plan.radius<.3f)
                {
                    if (!powered || plan.requiredKind != feature.kind) return true;
                    if (usedAbility && !ability.FlightActive) return true;
                }
            }
            return false;
        }
    }
}
