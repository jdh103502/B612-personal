using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using B612;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Isolated 3D PhysicsScene: real colliders/PhysX with explicitly scheduled motor and render ticks.
public static class PlayerTraversalValidation
{
    static readonly MethodInfo tick = typeof(GravityWalker).GetMethod("TickTraversal",BindingFlags.NonPublic|BindingFlags.Instance);
    static Scene scene;
    static readonly List<string> passed = new List<string>();
    static void Check(bool condition,string reason) { if(!condition)throw new InvalidOperationException(reason); }
    static void Near(float value,float expected,float tolerance,string reason) {Check(Mathf.Abs(value-expected)<=tolerance,$"{reason}: {value} vs {expected}");}

    sealed class Fixture : IDisposable
    {
        public readonly GameObject root,actor;
        public readonly GravityWalker motor;
        public readonly PlayerTraversalAbilities abilities;
        public readonly Rigidbody body;
        public readonly GravitySource source;
        public readonly Vector3 origin = new Vector3(1000,0,0);
        public int groundJumps,airJumps,landings;
        public Fixture(bool floor=true,bool sphere=false,Vector3 up=default)
        {
            if(up==Vector3.zero)up=Vector3.up;
            root=New("Traversal fixture");
            var gravity=New("Fixture gravity");gravity.transform.SetParent(root.transform);
            gravity.transform.position=sphere?origin:origin-Vector3.up*100f;
            source=gravity.AddComponent<GravitySource>();source.surfaceRadius=sphere?3f:100f;source.influenceRadius=sphere?20f:130f;
            if(sphere)
            {
                var ground=Primitive(PrimitiveType.Sphere,"Spherical ground");ground.transform.position=origin;ground.transform.localScale=Vector3.one*6f;
            }
            else if(floor) Box(origin-Vector3.up*.5f,new Vector3(40,1,40),"Floor");
            actor=New("Fixture player");actor.transform.SetParent(root.transform);
            actor.transform.position=sphere?origin+up*3.02f:origin+Vector3.up*(floor?.02f:5f);
            var forward=Vector3.ProjectOnPlane(Vector3.forward,up).normalized;if(forward.sqrMagnitude<.01f)forward=Vector3.right;
            actor.transform.rotation=Quaternion.LookRotation(forward,up);
            body=actor.AddComponent<Rigidbody>();
            var capsule=actor.AddComponent<CapsuleCollider>();capsule.height=.78f;capsule.radius=.13f;capsule.center=Vector3.up*.39f;
            var actualPlayer=UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None).FirstOrDefault(p=>p.gameObject.scene!=scene);
            if(actualPlayer)capsule.sharedMaterial=actualPlayer.GetComponent<CapsuleCollider>().sharedMaterial;
            motor=actor.AddComponent<GravityWalker>();motor.enabled=false;
            abilities=actor.AddComponent<PlayerTraversalAbilities>();
            motor.Jumped+=()=>groundJumps++;abilities.AirJumped+=()=>airJumps++;abilities.LandingRecovered+=()=>landings++;
            Physics.SyncTransforms();
        }
        public GameObject Box(Vector3 position,Vector3 size,string name="Obstacle")
        {
            var box=Primitive(PrimitiveType.Cube,name);box.transform.SetPositionAndRotation(position,Quaternion.identity);box.transform.localScale=size;Physics.SyncTransforms();return box;
        }
        GameObject Primitive(PrimitiveType type,string name)
        {
            var go=GameObject.CreatePrimitive(type);SceneManager.MoveGameObjectToScene(go,scene);go.name=name;
            go.transform.SetParent(root.transform);go.GetComponent<Renderer>().enabled=false;return go;
        }
        public void Step(float dt=.02f)
        {
            tick.Invoke(motor,new object[]{dt});scene.GetPhysicsScene().Simulate(dt);
            actor.transform.SetPositionAndRotation(body.position,body.rotation);
        }
        public void Steps(int count,float dt=.02f) {for(int i=0;i<count;i++)Step(dt);}
        public void Stand() {Steps(6);Check(motor.Grounded,"Fixture did not settle on support.");}
        public void GroundJump() {Stand();motor.jumpHeld=true;motor.Jump();Step();Check(groundJumps==1,"Ground jump not approved.");}
        public void Dispose()
        {
            source.enabled=false;UnityEngine.Object.DestroyImmediate(root);
        }
    }
    static GameObject New(string name)
    {
        var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);return go;
    }
    static void Case(string name,Action action) {action();passed.Add(name);}

    public static object Validate()
    {
        Check(Application.isPlaying,"Run in Play mode.");
        float previousTimeScale=Time.timeScale;Time.timeScale=1f;
        passed.Clear();
        scene=SceneManager.CreateScene("Traversal validation "+Guid.NewGuid().ToString("N"),new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        try
        {
            Case("held Space: one jump, then glide",()=>
            {
                using(var f=new Fixture())
                {
                    f.GroundJump();f.Steps(65);
                    Check(f.groundJumps==1&&f.airJumps==0&&f.abilities.AirJumpsRemaining==1,"Held jump repeated or spent air jump.");
                    Check(f.abilities.IsGliding&&f.abilities.GlideSecondsRemaining<2.5f,"Holding jump did not enter falling glide.");
                    f.motor.jumpHeld=false;f.motor.Jump();f.Step();
                    Check(f.airJumps==1&&!f.abilities.IsGliding,"Glide did not allow the preserved air jump.");
                    Near(f.motor.VerticalSpeed,3f,.001f,"Air jump impulse");
                    f.motor.Jump();f.Steps(10);Check(f.airJumps==1&&f.abilities.AirJumpsRemaining==0,"Third jump was approved.");
                }
            });
            Case("variable jump height",()=>
            {
                float shortHeight=JumpHeight(false),longHeight=JumpHeight(true);
                Check(longHeight>shortHeight+.5f,$"Short/long jumps were indistinguishable: {shortHeight}/{longHeight}");
            });
            Case("coyote inside/outside",()=>
            {
                foreach(bool inside in new[]{true,false})using(var f=new Fixture())
                {
                    f.Stand();var floor=f.root.GetComponentsInChildren<Collider>().First(c=>c.name=="Floor");floor.enabled=false;
                    f.Steps(inside?2:8);f.motor.Jump();f.Step();
                    Check(inside?f.groundJumps==1&&f.airJumps==0:f.groundJumps==0&&f.airJumps==1,"Coyote classification failed.");
                }
            });
            Case("double jump -> dash",()=>
            {
                using(var f=new Fixture(false))
                {
                    f.Step();f.motor.Jump();f.Step();f.motor.RequestDash();f.Step();
                    Check(f.airJumps==1&&f.abilities.IsDashing&&!f.abilities.DashChargeAvailable,"Air jump/dash chain failed.");
                    f.Steps(40);f.motor.Jump();f.motor.RequestDash();f.Steps(10);
                    Check(f.airJumps==1&&!f.abilities.IsDashing&&f.abilities.AirJumpsRemaining==0,"Cooldown restored air resources.");
                }
            });
            Case("dash -> double jump cancel",()=>
            {
                using(var f=new Fixture(false))
                {
                    f.Step();f.motor.RequestDash();f.Step();f.motor.Jump();f.Step();
                    Check(f.airJumps==1&&!f.abilities.IsDashing&&!f.abilities.DashChargeAvailable,"Jump did not cancel dash without refill.");
                    Check(f.motor.TangentSpeed<=3.001f,"Cancelled dash retained peak momentum.");Near(f.motor.VerticalSpeed,3f,.001f,"Cancel jump vertical");
                }
            });
            Case("same tick Space + Shift",()=>
            {
                using(var f=new Fixture())
                {
                    f.Stand();f.motor.Jump();f.motor.RequestDash();f.Step();
                    Check(f.groundJumps==1&&f.abilities.IsDashing,"Simultaneous actions were not both approved.");
                    Near(f.motor.VerticalSpeed,3.1f,.001f,"Dash overwrote jump vertical");Near(f.motor.TangentSpeed,8f,.001f,"Jump overwrote dash tangent");
                    Check(f.abilities.AirJumpsRemaining==1,"Ground jump consumed air jump.");
                }
            });
            Case("dash forward, fixed heading and no fall reset",()=>
            {
                using(var f=new Fixture(false))
                {
                    f.Step();f.body.linearVelocity=-Vector3.up*5f;f.motor.moveInput=Vector3.left;f.motor.RequestDash();f.Step();
                    Check(Vector3.Dot(f.abilities.DashDirection,Vector3.forward)>.99f,"Dash used movement input instead of root facing.");
                    Check(f.motor.VerticalSpeed < -5f,"Air dash erased fall velocity.");
                    var direction=f.abilities.DashDirection;
                    f.actor.transform.rotation=Quaternion.LookRotation(Vector3.left);f.body.rotation=f.actor.transform.rotation;f.Step();
                    Check(Vector3.Dot(f.abilities.DashDirection,direction)>.99f,"Dash was re-aimed by later facing/input.");
                }
            });
            var distances=new List<float>();
            Case("dash duration/distance at three fixed timesteps",()=>
            {
                foreach(float dt in new[]{.01f,.02f,1f/30f})using(var f=new Fixture())
                {
                    f.Stand();Vector3 start=f.body.position;f.motor.RequestDash();
                    int guard=0;do{f.Step(dt);guard++;}while(f.abilities.IsDashing&&guard<100);
                    Near(f.abilities.DashElapsedSeconds,.18f,.001f,"Dash duration");
                    float distance=Vector3.Distance(start,f.body.position);distances.Add(distance);
                    Near(distance,1.29f,.15f,"Unobstructed dash displacement");
                    f.Step(dt);Check(f.motor.TangentSpeed<=3.001f,"Peak dash velocity survived its exit.");
                }
            });
            Case("ground dash recharge and ledge expenditure",()=>
            {
                using(var f=new Fixture())
                {
                    f.Stand();f.motor.RequestDash();f.Steps(9);Check(!f.abilities.DashChargeAvailable,"Dash charge returned before cooldown.");
                    f.Steps(30);Check(f.abilities.DashChargeAvailable,"Stable ground did not recharge dash.");
                    f.motor.RequestDash();f.Step();f.root.GetComponentsInChildren<Collider>().First(c=>c.name=="Floor").enabled=false;
                    f.Steps(40);Check(!f.abilities.DashChargeAvailable&&f.abilities.AirJumpsRemaining==1,"Ledge exit restored dash or changed jump budget.");
                }
            });
            Case("glide budget toggling and gradual fall braking",()=>
            {
                using(var f=new Fixture(false))
                {
                    f.Step();f.body.linearVelocity=-Vector3.up*5f;f.motor.jumpHeld=true;f.Steps(11);
                    Check(f.abilities.IsGliding&&f.motor.VerticalSpeed < -4f,"Fast fall was snapped to zero or glide failed.");
                    float used=0f;
                    for(int i=0;i<350;i++)
                    {
                        f.motor.jumpHeld=i%45<35;float before=f.abilities.GlideSecondsRemaining;f.Step();used+=before-f.abilities.GlideSecondsRemaining;
                        Check(f.motor.VerticalSpeed<=0f,"Glide manufactured upward velocity.");
                    }
                    Near(f.abilities.GlideSecondsRemaining,0f,.001f,"Glide budget never exhausted");
                    Check(used<=2.5f+.001f,"Toggling regenerated glide budget.");
                    f.motor.jumpHeld=true;f.Steps(20);Check(!f.abilities.IsGliding,"Exhausted glide restarted.");
                }
            });
            Case("valid landing refills once, buffered jump is single use",()=>
            {
                using(var f=new Fixture())
                {
                    f.GroundJump();f.motor.Jump();f.Step();f.motor.RequestDash();f.Step();
                    f.motor.jumpHeld=false;
                    for(int i=0;i<400&&f.abilities.FlightActive;i++)f.Step();
                    Check(f.motor.Grounded&&!f.abilities.FlightActive&&f.landings==1,"Real landing was not confirmed exactly once.");
                    Check(f.abilities.AirJumpsRemaining==1&&f.abilities.DashChargeAvailable,"Landing did not refill jump/dash.");Near(f.abilities.GlideSecondsRemaining,2.5f,.001f,"Landing glide refill");
                    f.motor.Jump();f.Step();f.motor.Jump();f.Step(); // use both jumps before approaching the floor
                    f.body.position=f.origin+Vector3.up*.16f;f.actor.transform.position=f.body.position;f.body.linearVelocity=-Vector3.up;
                    f.motor.Jump();f.Steps(6);
                    Check(f.groundJumps==3&&f.airJumps==2,"Landing jump buffer was lost or duplicated.");
                }
            });
            Case("landing preserves dash cooldown",()=>
            {
                using(var f=new Fixture())
                {
                    f.GroundJump();f.motor.RequestDash();f.Step();f.abilities.CancelInputAndActions();
                    f.body.position=f.origin+Vector3.up*.02f;f.actor.transform.position=f.body.position;f.body.linearVelocity=-Vector3.up;
                    f.Steps(7);Check(!f.abilities.FlightActive&&f.abilities.DashChargeAvailable,"Controlled landing did not refill charge.");
                    Check(f.abilities.DashCooldownRemaining>.3f,"Landing skipped cooldown.");f.motor.RequestDash();f.Step();Check(!f.abilities.IsDashing,"Cooldown was ignored after landing.");
                }
            });
            Case("wall sweep, trigger and starting overlap",()=>
            {
                foreach(bool trigger in new[]{false,true})using(var f=new Fixture())
                {
                    f.Stand();var wall=f.Box(f.origin+new Vector3(0,.7f,.8f),new Vector3(2,2,.08f));wall.GetComponent<Collider>().isTrigger=trigger;
                    f.motor.RequestDash();f.Steps(12);
                    Check(trigger?f.body.position.z>1f:f.body.position.z<.66f,"Wall/trigger dash sweep failed.");
                    if(!trigger)Check(f.motor.VerticalSpeed<=.1f,"Wall dash became upward force.");
                }
                using(var f=new Fixture())
                {
                    f.Stand();f.Box(f.body.position+new Vector3(0,.4f,.1f),new Vector3(1,1,.15f));f.motor.RequestDash();f.Step();
                    Check(!f.abilities.IsDashing,"Starting wall overlap did not end dash.");
                }
            });
            Case("ceiling contact does not recharge",()=>
            {
                using(var f=new Fixture(false))
                {
                    f.Box(f.body.position+Vector3.up*1.1f,new Vector3(4,.1f,4));f.Step();f.motor.Jump();f.Step();f.Steps(12);
                    Check(f.abilities.AirJumpsRemaining==0&&f.abilities.FlightActive&&!f.motor.Grounded,"Ceiling was treated as landing.");
                }
            });
            var sphericalRise=new List<float>();
            Case("sphere top, side and underside dash",()=>
            {
                foreach(var up in new[]{Vector3.up,Vector3.right,Vector3.down})using(var f=new Fixture(true,true,up))
                {
                    f.Stand();f.motor.RequestDash();float rise=0f;for(int i=0;i<10;i++){f.Step();rise=Mathf.Max(rise,Vector3.Distance(f.body.position,f.origin)-3f);}
                    sphericalRise.Add(rise);Check(rise<.16f,$"Spherical dash lifted off unexpectedly: {up}, {rise}");
                    Check(f.motor.Up.sqrMagnitude>.99f,"Planet-relative up lost normalization.");
                }
            });
            Case("source change, field loss, suspension and teleport",()=>
            {
                using(var f=new Fixture(false))
                {
                    f.Step();f.motor.Jump();f.Step();f.motor.RequestDash();f.Steps(12);f.motor.jumpHeld=true;f.Steps(30);
                    float budget=f.abilities.GlideSecondsRemaining;
                    var other=New("Alternate gravity");other.transform.SetParent(f.root.transform);other.transform.position=f.body.position-Vector3.right*10f;
                    var source=other.AddComponent<GravitySource>();source.surfaceRadius=10f;source.influenceRadius=20f;f.Step();
                    Check(f.motor.Source==source&&f.abilities.AirJumpsRemaining==0&&!f.abilities.DashChargeAvailable&&f.abilities.GlideSecondsRemaining<=budget,"Gravity source change refilled resources.");
                    source.enabled=false;f.source.enabled=false;f.Step();Check(!f.abilities.IsGliding&&f.motor.TimeWithoutGravity>0,"Glide survived gravity loss.");
                    int jumps=f.abilities.AirJumpsRemaining;float cooldown=f.abilities.DashCooldownRemaining;
                    f.motor.Suspend(true);f.Steps(5);Near(f.abilities.DashCooldownRemaining,cooldown,.001f,"Suspended cooldown progressed");
                    f.motor.Suspend(false);Check(f.abilities.AirJumpsRemaining==jumps&&!f.abilities.DashChargeAvailable,"Suspension refilled air resources.");
                    f.motor.Teleport(f.origin,Vector3.up);
                    Check(f.abilities.AirJumpsRemaining==1&&f.abilities.DashChargeAvailable&&f.abilities.Mode==PlayerTraversalAbilities.TraversalMode.Locomotion,"Teleport did not reset traversal.");
                    Near(f.body.linearVelocity.magnitude,0f,.001f,"Teleport velocity");Near(f.abilities.GlideSecondsRemaining,2.5f,.001f,"Teleport glide budget");
                }
            });
            var fps=new List<int>();
            Case("30/60/120 fps input and multi-device lifecycle",()=>ValidateInput(fps));
            return new {passed=true,cases=passed.ToArray(),dashDistances=distances.ToArray(),sphericalRise=sphericalRise.ToArray(),renderFps=fps.ToArray()};
        }
        finally
        {
            foreach(var root in scene.GetRootGameObjects())UnityEngine.Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);
            Time.timeScale=previousTimeScale;
        }
    }
    public static object ValidateSlopes()
    {
        Check(Application.isPlaying,"Run in Play mode.");
        float oldTimeScale=Time.timeScale;Time.timeScale=1f;
        scene=SceneManager.CreateScene("Traversal slope regression",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var rows=new List<string>{"case,tick,grounded,support,normalSpeed,radialSpeed,tangentSpeed,upX,upY,upZ,delta,airState"};
        var summaries=new List<string>();
        var template=UnityEngine.Object.FindObjectsByType<PrinceCharacterAnimator>(FindObjectsSortMode.None).First(p=>p.walker&&p.walker.Traversal);
        try
        {
            foreach(float angle in new[]{15f,30f,45f})foreach(var direction in new[]{Vector3.right,Vector3.left,Vector3.forward})
            using(var f=new Fixture(false))
            {
                var slope=f.Box(f.origin-Quaternion.Euler(0,0,angle)*Vector3.up*.5f,new Vector3(40,1,40),"Slope");
                slope.transform.rotation=Quaternion.Euler(0,0,angle);
                var normal=slope.transform.up;
                f.motor.Teleport(f.origin+normal*.02f,normal);Physics.SyncTransforms();f.Stand();
                var model=UnityEngine.Object.Instantiate(template.animator.gameObject,f.actor.transform);
                var animator=model.GetComponent<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                animator.SetBool("Grounded",true);animator.Update(.3f);
                int initialLandings=f.landings,airTicks=0,airStates=0;float minSpeed=10f,maxRadial=-10f,distance=0f;
                var previous=f.body.position;f.motor.moveInput=direction;
                string name=$"slope{angle}/{direction}";
                for(int i=0;i<150;i++)
                {
                    f.Step();float delta=Vector3.Distance(previous,f.body.position);distance+=delta;previous=f.body.position;
                    animator.SetBool("Grounded",f.motor.Grounded);animator.SetFloat("Speed",f.motor.TangentSpeed);animator.Update(.02f);
                    bool air=animator.GetCurrentAnimatorStateInfo(0).IsName("Air") || animator.IsInTransition(0)&&animator.GetNextAnimatorStateInfo(0).IsName("Air");
                    if(!f.motor.Grounded)airTicks++;if(air)airStates++;
                    if(i>20)minSpeed=Mathf.Min(minSpeed,f.motor.TangentSpeed);
                    maxRadial=Mathf.Max(maxRadial,f.motor.RadialSpeed);
                    rows.Add($"{name},{i},{f.motor.Grounded},{f.motor.HasSupport},{f.motor.SupportNormalVelocity:R},{f.motor.RadialSpeed:R},{f.motor.TangentSpeed:R},{f.motor.Up.x:R},{f.motor.Up.y:R},{f.motor.Up.z:R},{delta:R},{air}");
                }
                Check(airTicks==0&&airStates==0,$"{name}: false airborne {airTicks}, Animator Air {airStates}");
                Check(minSpeed>1.98f&&distance>5.2f,$"{name}: lost slope speed {minSpeed}, displacement {distance}");
                Check(f.landings==initialLandings,$"{name}: walking generated resource-recovery landings.");
                summaries.Add($"{name}: airTicks={airTicks}, AirStates={airStates}, minSpeed={minSpeed:F3}, distance={distance:F3}, maxRadial={maxRadial:F3}");
            }
            foreach(float angle in new[]{15f,30f,45f})using(var f=new Fixture(false))
            {
                var slope=f.Box(f.origin-Quaternion.Euler(0,0,angle)*Vector3.up*.5f,new Vector3(40,1,40),"Slope");
                slope.transform.rotation=Quaternion.Euler(0,0,angle);var normal=slope.transform.up;
                f.motor.Teleport(f.origin+normal*.02f,normal);Physics.SyncTransforms();f.Stand();
                int landings=f.landings;
                f.motor.jumpHeld=true;f.motor.Jump();f.Step();
                Check(!f.motor.Grounded&&f.groundJumps==1,"Slope jump failed.");
                f.Steps(3);f.motor.Jump();f.Step();f.motor.RequestDash();f.Step();
                Check(f.airJumps==1&&f.abilities.IsDashing,"Slope air jump/dash failed.");
                bool glided=false;
                for(int i=0;i<65;i++)
                {
                    f.Step();glided|=f.abilities.IsGliding;
                    Check(f.abilities.AirJumpsRemaining==0&&!f.abilities.DashChargeAvailable,"Slope ascent refilled air resources.");
                }
                Check(glided,"Slope flight did not glide.");
                f.motor.jumpHeld=false;
                for(int i=0;i<250&&f.abilities.FlightActive;i++)f.Step();
                Check(f.motor.Grounded&&!f.abilities.FlightActive&&f.landings==landings+1,$"Slope {angle}: did not land exactly once.");
                Check(f.abilities.AirJumpsRemaining==1&&f.abilities.DashChargeAvailable&&f.abilities.GlideSecondsRemaining==2.5f,"Slope landing did not restore resources.");
                f.motor.moveInput=Vector3.right;f.motor.RequestDash();int dashAir=0;
                for(int i=0;i<10;i++){f.Step();if(!f.motor.Grounded)dashAir++;}
                Check(dashAir==0,$"Slope {angle}: ground dash lost support.");
                summaries.Add($"slope{angle}: jump/airJump/dash/glide, one landing and ground dash passed");
            }
            // Explicit ascent flag must distinguish an externally moving support from a released jump.
            using(var f=new Fixture())
            {
                f.Stand();var floor=f.root.GetComponentsInChildren<Collider>().First(c=>c.name=="Floor");
                var platform=floor.gameObject.AddComponent<Rigidbody>();platform.useGravity=false;platform.mass=1000f;
                platform.constraints=RigidbodyConstraints.FreezeRotation;platform.linearVelocity=Vector3.up*1.2f;
                f.body.linearVelocity=platform.linearVelocity;
                for(int i=0;i<10;i++){f.Step();Check(f.motor.Grounded,"Moving support radial ascent became airborne.");}
                Check(f.motor.RadialSpeed>1f&&Mathf.Abs(f.motor.SupportNormalVelocity)<.15f,"Support-relative velocity was not used.");
                summaries.Add("upward moving support: grounded with radial speed above 1m/s, relative normal speed below 0.15m/s");
            }
            using(var f=new Fixture(false))
            {
                f.Step();f.body.linearVelocity=Vector3.up*2f;f.motor.jumpHeld=false;f.Step();
                Check(f.motor.VerticalSpeed>1.9f,"Non-jump upward motion received short-hop gravity.");
                summaries.Add("non-jump upward velocity: no short-hop multiplier");
            }
            // A convex ridge has a new normal, but still has real foot support on both triangles.
            using(var f=new Fixture(false))
            {
                var mesh=new Mesh();mesh.vertices=new[]{new Vector3(-6,-3,-4),new Vector3(-6,-3,4),new Vector3(0,0,-4),new Vector3(0,0,4),new Vector3(6,-3,-4),new Vector3(6,-3,4)};
                mesh.triangles=new[]{0,1,2,2,1,3,2,3,4,4,3,5};mesh.RecalculateNormals();
                var terrain=New("Convex ridge triangles");terrain.transform.SetParent(f.root.transform);terrain.transform.position=f.origin;
                terrain.AddComponent<MeshCollider>().sharedMesh=mesh;
                var normal=new Vector3(-.5f,1f,0).normalized;
                f.motor.Teleport(f.origin+new Vector3(-2,-1,0)+normal*.03f,normal);Physics.SyncTransforms();f.Stand();
                int landings=f.landings,air=0;f.motor.moveInput=Vector3.right;
                for(int i=0;i<200;i++)
                {
                    f.Step();if(!f.motor.Grounded)air++;
                    rows.Add($"ridge,{i},{f.motor.Grounded},{f.motor.HasSupport},{f.motor.SupportNormalVelocity:R},{f.motor.RadialSpeed:R},{f.motor.TangentSpeed:R},{f.motor.MovementUp.x:R},{f.motor.MovementUp.y:R},{f.motor.Up.z:R},{f.body.position.x-f.origin.x:R}/{f.body.position.y-f.origin.y:R},False");
                }
                Check(air==0&&f.landings==landings,$"Convex ridge lost support {air}, extra landings {f.landings-landings}, endpoint {f.body.position-f.origin}.");
                Check(f.body.position.x>f.origin.x+4f,"Convex ridge traversal stalled.");
                summaries.Add($"convex triangle ridge: airTicks={air}, endpoint={f.body.position-f.origin}");
                UnityEngine.Object.DestroyImmediate(terrain);UnityEngine.Object.DestroyImmediate(mesh);
            }
            ValidatePlanetMesh(rows,summaries);
            System.IO.Directory.CreateDirectory("Temp");System.IO.File.WriteAllLines("Temp/PlayerTraversalSlopeValidation.csv",rows);
            System.IO.File.WriteAllLines("Temp/PlayerTraversalSlopeValidation.txt",summaries);
            return new {passed=true,cases=summaries.ToArray(),trace="Temp/PlayerTraversalSlopeValidation.csv"};
        }
        finally
        {
            System.IO.Directory.CreateDirectory("Temp");System.IO.File.WriteAllLines("Temp/PlayerTraversalSlopeValidation.csv",rows);
            foreach(var root in scene.GetRootGameObjects())UnityEngine.Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);Time.timeScale=oldTimeScale;
        }
    }
    static void ValidatePlanetMesh(List<string> rows,List<string> summaries)
    {
        var original=UnityEngine.Object.FindObjectsByType<GravitySource>(FindObjectsSortMode.None).First(s=>s.GetComponentInChildren<MeshCollider>());
        var collider=original.GetComponentInChildren<MeshCollider>();
        using(var f=new Fixture(false))
        {
            f.source.transform.position=f.origin;f.source.surfaceRadius=original.surfaceRadius;f.source.influenceRadius=original.influenceRadius;
            var ground=New("Actual deformed planet collider");ground.transform.SetParent(f.root.transform);
            ground.transform.SetPositionAndRotation(f.origin,collider.transform.rotation);ground.transform.localScale=collider.transform.lossyScale;
            var planetCollider=ground.AddComponent<MeshCollider>();planetCollider.sharedMesh=collider.sharedMesh;Physics.SyncTransforms();
            foreach(var radial in new[]{Vector3.up,new Vector3(.35f,1,.2f).normalized,Vector3.right,Vector3.down})
            {
                Check(planetCollider.Raycast(new Ray(f.origin+radial*15,-radial),out var hit,20),"No actual planet support.");
                f.motor.Teleport(hit.point+hit.normal*.025f,hit.normal);Physics.SyncTransforms();f.Stand();
                int air=0,landings=f.landings;float maxNormalAngle=0;Vector3 previous=f.body.position;
                for(int i=0;i<400;i++)
                {
                    var up=(f.body.position-f.origin).normalized;
                    f.motor.moveInput=Vector3.Cross(up,Mathf.Abs(up.z)<.9f?Vector3.forward:Vector3.up).normalized;
                    f.Step();if(!f.motor.Grounded)air++;
                    maxNormalAngle=Mathf.Max(maxNormalAngle,Vector3.Angle(f.motor.MovementUp,up));
                    rows.Add($"planet/{radial},{i},{f.motor.Grounded},{f.motor.HasSupport},{f.motor.SupportNormalVelocity:R},{f.motor.RadialSpeed:R},{f.motor.TangentSpeed:R},{f.motor.Up.x:R},{f.motor.Up.y:R},{f.motor.Up.z:R},{Vector3.Distance(previous,f.body.position):R},False");previous=f.body.position;
                }
                Check(air==0&&landings==f.landings,$"Actual planet {radial}: false air {air}, extra landings {f.landings-landings}.");
                summaries.Add($"actual planet/{radial}: airTicks={air}, max normal/gravity angle={maxNormalAngle:F2}");
            }
        }
    }
    public static object DiagnoseDash()
    {
        float previousTimeScale=Time.timeScale;Time.timeScale=1f;
        scene=SceneManager.CreateScene("Dash diagnosis",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        try
        {
            using(var f=new Fixture())
            {
                f.Stand();f.motor.RequestDash();
                var log=new List<string>();
                for(int i=0;i<22;i++)
                {
                    f.motor.GetWorldCapsule(out var a,out var b,out float radius);
                    var hits=new RaycastHit[16];var direction=(f.abilities.IsDashing?f.abilities.DashDirection:Vector3.forward)-Vector3.up*.01f;
                    int count=scene.GetPhysicsScene().CapsuleCast(a,b,radius,direction.normalized,hits,.1f,~0,QueryTriggerInteraction.Ignore);
                    f.Step(.01f);
                    log.Add($"i={i} dash={f.abilities.IsDashing} elapsed={f.abilities.DashElapsedSeconds:R} pos={f.body.position} grounded={f.motor.Grounded} hits="+string.Join(";",hits.Take(count).Select(h=>$"{h.collider.name}/{h.distance}/{h.normal}")));
                }
                return log.ToArray();
            }
        }
        finally {SceneManager.UnloadSceneAsync(scene);Time.timeScale=previousTimeScale;}
    }
    public static object ValidateGeometryAndLegacy()
    {
        float oldTimeScale=Time.timeScale;Time.timeScale=1f;
        scene=SceneManager.CreateScene("Traversal geometry regression",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        float meshStop=0f,scaledStop=0f;
        try
        {
            using(var f=new Fixture())
            {
                var floor=f.root.GetComponentsInChildren<Collider>().First(c=>c.name=="Floor");
                var wall=f.Box(f.origin+new Vector3(0,.7f,.8f),new Vector3(2,2,.08f));
                var mesh=new Mesh();mesh.CombineMeshes(new[]{
                    new CombineInstance{mesh=floor.GetComponent<MeshFilter>().sharedMesh,transform=floor.transform.localToWorldMatrix},
                    new CombineInstance{mesh=wall.GetComponent<MeshFilter>().sharedMesh,transform=wall.transform.localToWorldMatrix}});
                floor.enabled=false;wall.GetComponent<Collider>().enabled=false;
                var solid=New("Combined floor and wall");solid.transform.SetParent(f.root.transform);solid.AddComponent<MeshCollider>().sharedMesh=mesh;
                Physics.SyncTransforms();f.Stand();f.motor.RequestDash();f.Steps(12);
                meshStop=f.body.position.z;Check(meshStop<.66f,"Dash ignored a wall in the same ground MeshCollider.");
                UnityEngine.Object.DestroyImmediate(solid);UnityEngine.Object.DestroyImmediate(mesh);
            }
            using(var f=new Fixture())
            {
                f.Stand();var capsule=f.actor.GetComponent<CapsuleCollider>();capsule.direction=0;f.actor.transform.localScale=new Vector3(1.5f,.8f,1.2f);
                f.actor.transform.rotation=Quaternion.Euler(0,33,0);f.body.rotation=f.actor.transform.rotation;Physics.SyncTransforms();
                f.motor.GetWorldCapsule(out var a,out var b,out float radius);
                var bounds=new Bounds();bounds.SetMinMax(Vector3.Min(a,b)-Vector3.one*radius,Vector3.Max(a,b)+Vector3.one*radius);
                Near((bounds.size-capsule.bounds.size).magnitude,0f,.003f,"Scaled capsule bounds disagree with PhysX");
                Near((bounds.center-capsule.bounds.center).magnitude,0f,.003f,"Scaled capsule centre disagrees with PhysX");
                var forward=f.actor.transform.forward;var start=f.body.position;
                var wall=f.Box(start+forward*.8f+Vector3.up*.4f,new Vector3(4,2,.08f));wall.transform.rotation=f.body.rotation;Physics.SyncTransforms();
                f.motor.RequestDash();f.Steps(12);scaledStop=Vector3.Dot(f.body.position-start,forward);
                Check(scaledStop<.66f,"Scaled/rotated capsule crossed wall.");
            }
            using(var f=new Fixture())
            {
                UnityEngine.Object.DestroyImmediate(f.abilities);
                // The untouched legacy motor queries the default Physics world.
                SceneManager.MoveGameObjectToScene(f.root,SceneManager.GetActiveScene());Physics.SyncTransforms();
                var fixedTick=typeof(GravityWalker).GetMethod("FixedUpdate",BindingFlags.NonPublic|BindingFlags.Instance);
                Action step=()=>fixedTick.Invoke(f.motor,null);
                for(int i=0;i<6;i++)step();Check(f.motor.Grounded,"Legacy walker did not stand.");
                f.motor.Jump();step();Check(f.groundJumps==1,"Legacy ground jump was changed.");
                f.motor.RequestDash();f.motor.Jump();for(int i=0;i<3;i++)step();
                Check(f.groundJumps==1&&f.motor.TangentSpeed<.1f,"Legacy walker acquired player-only jump/dash.");
            }
            return new {passed=true,combinedMeshStop=meshStop,scaledCapsuleStop=scaledStop,legacyWalkerUnchanged=true};
        }
        finally
        {
            foreach(var root in scene.GetRootGameObjects())UnityEngine.Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);Time.timeScale=oldTimeScale;
        }
    }
    static float JumpHeight(bool held)
    {
        using(var f=new Fixture())
        {
            f.GroundJump();float initial=f.origin.y,peak=0f;
            for(int i=0;i<80;i++){f.motor.jumpHeld=held&&i<35;f.Step();peak=Mathf.Max(peak,f.body.position.y-initial);}return peak;
        }
    }
    static void ValidateInput(List<int> fpsCases)
    {
        var oldKeyboard=Keyboard.current;var oldGamepad=Gamepad.current;
        var keyboard=InputSystem.AddDevice<Keyboard>("TraversalValidationKeyboard");var gamepad=InputSystem.AddDevice<Gamepad>("TraversalValidationGamepad");
        var background=InputSystem.settings.backgroundBehavior;var editor=InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.EnableDevice(keyboard);InputSystem.EnableDevice(gamepad);
        keyboard.MakeCurrent();gamepad.MakeCurrent();
        Action<bool,bool,bool> input=(space,shift,south)=>
        {
            var keys=new List<Key>();if(space)keys.Add(Key.Space);if(shift)keys.Add(Key.LeftShift);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys.ToArray()));
            InputSystem.QueueStateEvent(gamepad,south?new GamepadState().WithButton(GamepadButton.South):new GamepadState());
            InputSystem.Update();
        };
        try
        {
            foreach(int fps in new[]{30,60,120})using(var f=new Fixture())
            {
                f.Stand();var controller=f.actor.AddComponent<PlayerController>();controller.SendMessage("OnApplicationFocus",true);input(false,false,false);controller.SendMessage("Update");
                float accumulator=0f;
                for(int frame=0;frame<fps*1.2f;frame++)
                {
                    float time=frame/(float)fps;
                    bool space=time>=.05f&&time<.16f||time>=.25f&&time<.34f||time>=.6f&&time<.68f;
                    input(space,time>=.4f&&time<.95f,false);controller.SendMessage("Update");accumulator+=1f/fps;
                    while(accumulator>=.02f-.000001f){f.Step();accumulator-=.02f;}
                }
                Check(f.groundJumps==1&&f.airJumps==1&&f.abilities.ConsumedDashRequestId==1,$"Input counts differ at {fps}fps: {f.groundJumps}/{f.airJumps}/{f.abilities.ConsumedDashRequestId}");fpsCases.Add(fps);
            }
            using(var f=new Fixture())
            {
                f.Stand();var controller=f.actor.AddComponent<PlayerController>();controller.SendMessage("OnApplicationFocus",true);
                input(false,false,false);controller.SendMessage("Update");
                input(true,false,false);controller.SendMessage("Update");f.Steps(3);Check(f.groundJumps==1&&f.airJumps==0,"One input was reused by multiple fixed ticks.");
                input(true,false,true);controller.SendMessage("Update");f.Step();input(false,false,true);controller.SendMessage("Update");
                Check(f.motor.jumpHeld,"Keyboard release cancelled a held gamepad button.");
                input(false,false,false);InputSystem.QueueStateEvent(gamepad,new GamepadState().WithButton(GamepadButton.RightShoulder));InputSystem.Update();controller.SendMessage("Update");f.Step();
                Check(f.abilities.IsDashing&&f.abilities.ConsumedDashRequestId==1,"Gamepad right shoulder did not dash.");
                controller.SendMessage("OnApplicationFocus",false);Check(!f.abilities.PendingJump&&!f.motor.jumpHeld&&!f.abilities.InputEnabled,"Focus loss did not clear input.");
                controller.SendMessage("OnApplicationFocus",true);controller.SendMessage("Update");Check(!f.motor.jumpHeld&&!f.abilities.PendingJump,"Held input replayed on focus gain.");
                input(false,false,false);controller.SendMessage("Update");f.motor.RequestDash();
                float cooldown=f.abilities.DashCooldownRemaining,budget=f.abilities.GlideSecondsRemaining;
                Time.timeScale=0f;controller.SendMessage("Update");Check(!f.abilities.PendingDash,"Pause retained dash request.");
                Near(f.abilities.DashCooldownRemaining,cooldown,.001f,"Pause cooldown");Near(f.abilities.GlideSecondsRemaining,budget,.001f,"Pause budget");Time.timeScale=1f;
                controller.enabled=false;Check(!f.abilities.InputEnabled&&!f.abilities.PendingJump,"Disabled controller retained input.");
            }
        }
        finally
        {
            Time.timeScale=1f;
            InputSystem.RemoveDevice(keyboard);InputSystem.RemoveDevice(gamepad);
            if(oldKeyboard!=null&&oldKeyboard.added)oldKeyboard.MakeCurrent();if(oldGamepad!=null&&oldGamepad.added)oldGamepad.MakeCurrent();
            InputSystem.settings.backgroundBehavior=background;InputSystem.settings.editorInputBehaviorInPlayMode=editor;
        }
    }
}
