using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace B612
{
    /// <summary>Reachable unlock, crop survey and guide encounters; no collision/support geometry.</summary>
    public sealed class CampaignWorldSites:MonoBehaviour
    {
        public const string Glyphs="금색 기둥에서 배우기대쉬추가 점프활공작물 조사길 안내상호작용완료해금잠김없음챕터여정목표수집한능력모든능력통로기본이동사용가능분기지도를열어다음으로가세요여정완료다음챕터로이동새행성전투물길여울얕은물보행감속물가로우회기록이동거리게임시간";
        public StarSystemFlowController Flow { get; private set; }
        public PlanetWorldInstance World { get; private set; }
        public Vector3[] Positions { get; }=new Vector3[3];
        public float ElapsedSeconds=>Time.time-arrived;
        public float TravelMetres { get; private set; }
        float arrived;Vector3 previous;
        Material[] materials;
        Material routeMaterial;
        public static float Fraction(int index)=>index==0?.18f:index==1?.42f:.72f;
        public void Initialize(StarSystemFlowController flow,PlanetWorldInstance world)
        {
            Flow=flow;World=world;arrived=Time.time;previous=flow.player.transform.position;
            materials=new Material[3];
            var settings = flow.worldHost.settings;
            for(int i=0;i<3;i++)
            {
                var plan=world.Terrain.GenerationPlan;float a=Fraction(i)*Mathf.PI;
                var n=plan.spawn*Mathf.Cos(a)+plan.routeTangent*Mathf.Sin(a);
                if(!world.surface.TryProject(n,out var p,out var normal))throw new System.InvalidOperationException("Reserved encounter pad is missing.");Positions[i]=p;
                materials[i] = settings.proceduralWaterMaterial
                    ? new Material(settings.proceduralWaterMaterial)
                    : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                materials[i].name = "Owned campaign site " + i;
                materials[i].SetFloat("_Smoothness", .5f);
                materials[i].color=i==0?new Color(1,.72f,.22f):i==1?new Color(.38f,.8f,.42f):new Color(.32f,.73f,.95f);
                materials[i].EnableKeyword("_EMISSION");materials[i].SetColor("_EmissionColor",materials[i].color*.22f);
                var root=new GameObject(i==0?"Ability beacon":i==1?"Crop survey plot":"Guide encounter");root.transform.SetParent(transform,false);root.transform.SetPositionAndRotation(p,Quaternion.FromToRotation(Vector3.up,normal));
                if(i==0){Primitive(PrimitiveType.Cylinder,root.transform,new Vector3(0,.45f,0),new Vector3(.32f,.45f,.32f),materials[i]);Primitive(PrimitiveType.Sphere,root.transform,new Vector3(0,1.1f,0),Vector3.one*.35f,materials[i]);}
                else if(i==1){for(int j=0;j<3;j++)Primitive(PrimitiveType.Sphere,root.transform,new Vector3((j-1)*.3f,.15f,0),new Vector3(.24f,.28f,.24f),materials[i]);}
                else Primitive(PrimitiveType.Capsule,root.transform,new Vector3(0,.5f,0),new Vector3(.28f,.5f,.28f),materials[i]);
            }
            routeMaterial = settings.routeGuideMaterial
                ? new Material(settings.routeGuideMaterial)
                : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            routeMaterial.name = "Owned basic route guide";
            routeMaterial.color = new Color(.32f,.86f,.66f);
            var generation=world.Terrain.GenerationPlan;DrawRoute(ProceduralPlanetTerrain.MainRoute(generation));
            foreach(var feature in generation.features)DrawRoute(ProceduralPlanetTerrain.FeatureRoute(generation,feature));
            foreach (var connector in generation.connectors)
                DrawRoute(new List<Vector3> { connector.anchor, connector.pad });
            if (generation.requiredKind >= 0)
            {
                var course = generation.features[0];
                DrawPad(course, TraversalTestCourseGeometry.Start(course.kind), materials[2]);
                DrawPad(course, TraversalTestCourseGeometry.Goal(course.kind), materials[0]);
            }
        }
        void DrawPad(ProceduralPlanetTerrain.Feature course, Vector2 centre, Material material)
        {
            var directions = new List<Vector3>();
            for (int i = 0; i <= 24; i++)
            {
                float angle = i * (2f * Mathf.PI / 24f);
                var point = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .55f;
                directions.Add(course.Direction(point, World.Terrain.GenerationPlan.radius));
            }
            DrawRoute(directions, material);
        }
        void DrawRoute(List<Vector3> directions, Material material = null)
        {
            var points=new System.Collections.Generic.List<Vector3>();var plan=World.Terrain.GenerationPlan;
            for(int i=1;i<directions.Count;i++)
            {
                int steps=Mathf.Max(1,Mathf.CeilToInt(Mathf.Acos(Mathf.Clamp(Vector3.Dot(directions[i-1],directions[i]),-1,1))*plan.radius/.2f));
                for(int j=i==1?0:1;j<=steps;j++)
                {
                    var n=Vector3.Slerp(directions[i-1],directions[i],j/(float)steps).normalized;
                    // Final Collider projection keeps the visual guide on exactly the validated ground.
                    if(World.surface.TryProject(n,out var point,out var normal))points.Add(point+normal*.018f);
                }
            }
            var go=new GameObject("Basic walking route guide");go.transform.SetParent(transform,false);var line=go.AddComponent<LineRenderer>();
            line.sharedMaterial=material ? material : routeMaterial;line.useWorldSpace=true;line.widthMultiplier=.055f;line.positionCount=points.Count;line.SetPositions(points.ToArray());line.numCapVertices=2;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;
        }
        static void Primitive(PrimitiveType type,Transform parent,Vector3 position,Vector3 scale,Material material)
        {var go=GameObject.CreatePrimitive(type);go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;var collider=go.GetComponent<Collider>();collider.enabled=false;Destroy(collider);go.GetComponent<Renderer>().sharedMaterial=material;}
        public bool CanInteract(int index)
        {
            if(index<0||index>2||!Flow||!World||Flow.Sites!=this||Flow.IsTravelling||!Flow.WorldReady||Flow.surge.IsGameOver||GameplayModalInput.BlocksGameplayInput||Time.timeScale<=0)return false;
            return (World.Data.state.visitedSites&(1<<index))==0&&Flow.player.GetComponent<GravityWalker>().Grounded&&Vector3.Distance(Flow.player.transform.position,Positions[index])<1.4f;
        }
        public int Nearest()
        {int nearest=-1;float distance=2.2f;for(int i=0;i<3;i++){float d=Vector3.Distance(Flow.player.transform.position,Positions[i]);if(d<distance){nearest=i;distance=d;}}return nearest;}
        public string Prompt()
        {
            int index=Nearest();if(index<0)return "금색 기둥 · 작물 조사 · 길 안내 지점에서 E";
            if((World.Data.state.visitedSites&(1<<index))!=0)return "상호작용 완료";
            return index==0&&World.Data.spec.unlock!=TraversalCapability.None?"[E] "+ChapterTerrainDefinition.AbilityName(World.Data.spec.unlock)+" 배우기":index==1?"[E] 작물 조사":"[E] 길 안내";
        }
        void Update()
        {
            if(!Flow||Flow.IsTravelling)return;
            var current=Flow.player.transform.position;float moved=Vector3.Distance(current,previous);previous=current;
            if(Time.timeScale>0&&moved<1)TravelMetres+=moved;
            if(Keyboard.current!=null&&Keyboard.current.eKey.wasPressedThisFrame){int index=Nearest();if(index>=0)Flow.RequestSiteInteraction(this,index);}
        }
        void OnDestroy(){if(materials!=null)foreach(var material in materials)if(material)Destroy(material);if(routeMaterial)Destroy(routeMaterial);}
    }
}
