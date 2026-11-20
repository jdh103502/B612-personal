using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using B612;
using UnityEngine;

public static class ProceduralTerrainStage2Validation
{
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static string MeshHash(Mesh mesh)
    {
        using(var memory=new MemoryStream())using(var writer=new BinaryWriter(memory))
        {foreach(var p in mesh.vertices){writer.Write(p.x);writer.Write(p.y);writer.Write(p.z);}foreach(int index in mesh.triangles)writer.Write(index);return BitConverter.ToString(SHA256.Create().ComputeHash(memory.ToArray()));}
    }
    public static object ValidateBatch(int scaleIndex)
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Use Play mode.");
        var flow=UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();
        var settings=UnityEngine.Object.Instantiate(flow.worldHost.settings);settings.chapterCampaign=false;var results=new List<object>();
        float scale=new[]{.7f,1f,1.5f,2f}[scaleIndex];
        var root=new GameObject("Stage 2 terrain verification host");root.transform.position=new Vector3(1000,0,0);var host=root.AddComponent<PlanetWorldHost>();host.settings=settings;
        string originalMesh=MeshHash(settings.groundTemplate.GetComponent<MeshFilter>().sharedMesh);
        var originalColors=settings.originalMaterials.Select(m=>m.color).ToArray();
        var currentMesh=flow.worldHost.ActiveWorld.Terrain.OwnedMesh;
        var currentSlot=flow.surge.surface.Cells.SelectMany(c=>c.slots).First();bool occupied=currentSlot.occupied;
        try
        {
            foreach(int seed in new[]{612101,1,77})
            {
                var data=StarSystemFactory.Create(seed,settings).planets[0];data.spec.terrain.sizeScale=scale;data.spec.sizeClass=PlanetGenerationSettings.Classify(scale);data.spec.terrain.radius=settings.nominalRadius*scale;
                var stopwatch=System.Diagnostics.Stopwatch.StartNew();var world=host.Prepare(data,flow.player.GetComponent<CapsuleCollider>());stopwatch.Stop();
                try
                {
                    var terrain=world.Terrain;var plan=terrain.GenerationPlan;var mesh=terrain.OwnedMesh;
                    Check(plan.radius==data.spec.terrain.radius && world.ground.transform.lossyScale==Vector3.one,"Terrain scale applied twice");
                    Check(((MeshCollider)world.ground).sharedMesh==mesh && world.ground.GetComponent<MeshFilter>().sharedMesh==mesh,"Render/collision mismatch");
                    Check(plan.features.Count<=2 && plan.placementAttempts<=128 && plan.buildAttempts<=ProceduralPlanetTerrain.MaxBuildAttempts,"Unbounded generation");
                    Check(mesh.subMeshCount==1 && world.groundRenderer.sharedMaterials.Length==1 && mesh.colors.Length==mesh.vertexCount,"Missing continuous surface");
                    if(plan.radius>=12)Check(plan.features.Count>0,"Roomy test planet lost all reviewed features");
                    int frequency=Mathf.CeilToInt(2*plan.radius/.18f);
                    Check(mesh.vertexCount==6*frequency*frequency+2 && mesh.triangles.Length==36*frequency*frequency,"Cube-sphere topology formula");
                    Check(mesh.vertices.All(p=>p.magnitude>plan.radius-2.1f) && mesh.normals.All(n=>n.sqrMagnitude>.99f),"Invalid radius/normals");
                    Check(world.surface.SlotCount>40,"Too few safe spawn slots");
                    foreach(var cell in world.surface.Cells)foreach(var slot in cell.slots)
                    {
                        var p=world.surface.WorldPoint(slot);var n=world.surface.WorldNormal(slot);
                        Check(terrain.AllowsSpawn(p,n),"Spawn overlaps protected/feature space");
                        bool projected=world.surface.TryProject(slot.localPoint.normalized,out var point,out var normal);
                        // Reprojection can select the adjacent facet at a shared edge (floating-point origin offset).
                        Check(projected && Vector3.Distance(p,point)<.005f && Vector3.Angle(n,normal)<2f,"Slot reprojection: distance="+Vector3.Distance(p,point)+", normal angle="+Vector3.Angle(n,normal));
                    }
                    string hash=MeshHash(mesh);
                    if(scaleIndex==1 && seed==612101)
                    {
                        var repeated=host.Prepare(data,flow.player.GetComponent<CapsuleCollider>());
                        try{Check(MeshHash(repeated.Terrain.OwnedMesh)==hash,"Same seed changed geometry");Check(repeated.surface.SlotCount==world.surface.SlotCount,"Same seed changed slots");}
                        finally{UnityEngine.Object.DestroyImmediate(repeated.gameObject);}
                        var edges=new Dictionary<long,int>();var triangles=mesh.triangles;
                        for(int i=0;i<triangles.Length;i+=3)for(int j=0;j<3;j++)
                        {int a=triangles[i+j],b=triangles[i+(j+1)%3];long key=((long)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);edges.TryGetValue(key,out int count);edges[key]=count+1;}
                        Check(edges.Values.All(count=>count==2) && mesh.vertexCount-edges.Count+triangles.Length/3==2,"Crack/non-manifold seam");
                    }
                    results.Add(new{scale,seed,plan.radius,features=plan.features.Count,kinds=plan.features.Select(f=>f.kind).ToArray(),plan.placementAttempts,plan.buildAttempts,plan.safeFallback,plan.repairs,plan.maximumWalkSlope,world.surface.SlotCount,world.surface.EmptyCellCount,vertices=mesh.vertexCount,prepareMilliseconds=stopwatch.Elapsed.TotalMilliseconds,hash});
                }
                finally {UnityEngine.Object.DestroyImmediate(world.gameObject);}
            }
            Check(originalMesh==MeshHash(settings.groundTemplate.GetComponent<MeshFilter>().sharedMesh) && originalColors.SequenceEqual(settings.originalMaterials.Select(m=>m.color)),"Shared source mesh/material changed");
            Check(flow.worldHost.ActiveWorld.Terrain.OwnedMesh==currentMesh && currentSlot.occupied==occupied,"Preparing another world mutated active world");
            Directory.CreateDirectory("Temp/TerrainStage2");File.WriteAllText("Temp/TerrainStage2/Scale"+scaleIndex+".json",Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented));
            return new{passed=true,scale,results};
        }
        finally {UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(settings);}
    }
    public static object ValidateSmallAndFailure()
    {
        var flow=UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();
        var settings=UnityEngine.Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>(
            "Assets/B612/Settings/PlanetTravel/EmptyPlanetGeneration.asset"));
        var root=new GameObject("Main-sized terrain test");root.transform.position=new Vector3(2000,0,0);var host=root.AddComponent<PlanetWorldHost>();host.settings=settings;var results=new List<object>();
        try
        {
            foreach(float scale in new[]{.7f,1f,1.5f,2f})
            {
                var data=StarSystemFactory.Create(19,settings).planets[0];data.spec.terrain.sizeScale=scale;data.spec.sizeClass=PlanetGenerationSettings.Classify(scale);data.spec.terrain.radius=settings.nominalRadius*scale;
                var world=host.Prepare(data,flow.player.GetComponent<CapsuleCollider>());
                results.Add(new{scale,radius=world.Terrain.GenerationPlan.radius,features=world.Terrain.GenerationPlan.features.Count,world.Terrain.GenerationPlan.repairs,slots=world.surface.SlotCount});UnityEngine.Object.DestroyImmediate(world.gameObject);
            }
            var bad=StarSystemFactory.Create(1,settings).planets[0];bad.spec.generatorVersion=PlanetGenerationSettings.GeneratorVersion-1;bool rejected=false;try{host.Prepare(bad,flow.player.GetComponent<CapsuleCollider>());}catch(InvalidOperationException){rejected=true;}Check(rejected,"Old terrain version accepted");
            var report=new{passed=true,results,oldVersionRejected=rejected};Directory.CreateDirectory("Temp/TerrainStage2");File.WriteAllText("Temp/TerrainStage2/MainSized.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
        }
        finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(settings);}
    }
    public static object ValidateRepairs()
    {
        var flow=UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();var settings=UnityEngine.Object.Instantiate(flow.worldHost.settings);settings.nominalRadius=12;settings.chapterCampaign=false;
        var root=new GameObject("Repair branch check");root.transform.position=new Vector3(3000,0,0);var host=root.AddComponent<PlanetWorldHost>();host.settings=settings;
        var actor=new GameObject("Unpowered ability configuration",typeof(Rigidbody),typeof(CapsuleCollider),typeof(GravityWalker),typeof(PlayerTraversalAbilities));
        actor.transform.position=new Vector3(4000,0,0);var capsule=actor.GetComponent<CapsuleCollider>();capsule.radius=.13f;capsule.height=.78f;capsule.center=Vector3.up*.39f;
        var ability=actor.GetComponent<PlayerTraversalAbilities>();ability.doubleJumpSpeed=0;ability.dashSpeed=0;
        try
        {
            var data=StarSystemFactory.Create(612101,settings).planets[0];var world=host.Prepare(data,capsule);
            try
            {
                var plan=world.Terrain.GenerationPlan;
                Check(plan.features.Count==0&&plan.repairs.Count>=2&&plan.buildAttempts<=4&&world.surface.SlotCount>40,"Invalid ability shortcuts were not repaired within budget");
                var report=new{passed=true,plan.buildAttempts,plan.repairs,features=plan.features.Count,world.surface.SlotCount};Directory.CreateDirectory("Temp/TerrainStage2");File.WriteAllText("Temp/TerrainStage2/Repairs.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
            }
            finally{UnityEngine.Object.DestroyImmediate(world.gameObject);}
        }
        finally{UnityEngine.Object.DestroyImmediate(actor);UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(settings);}
    }
    public static object ValidateImmutability()
    {
        var flow=UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();var world=flow.worldHost.ActiveWorld;var mesh=world.Terrain.OwnedMesh;var data=world.Data;
        bool rejected=false;try{world.Prepare(data,flow.worldHost.settings,flow.player.GetComponent<CapsuleCollider>());}catch(InvalidOperationException){rejected=true;}
        Check(rejected&&world.Data==data&&world.Terrain.OwnedMesh==mesh&&world.GetComponents<ProceduralPlanetTerrain>().Length==1,"Repeat preparation mutated an active world");
        var settings = flow.worldHost.settings.terrainAppearance;
        var plan = world.Terrain.GenerationPlan;
        var material = world.groundRenderer.sharedMaterial;
        var originalEarth = settings.earth;
        var originalKey = settings.macroKeys[0];
        var plannedEarth = plan.appearance.earth;
        var plannedKey = plan.appearance.macroKeys[0];
        var preparedSwatch = material.GetColor("_EarthColor");
        var height = ProceduralPlanetTerrain.FreeHeight(plan, plan.routeNormal);
        try
        {
            settings.earth = Color.magenta;
            settings.macroKeys[0] = new Vector2(0f, 0f);
            Check(plan.appearance.earth == plannedEarth && plan.appearance.macroKeys[0] == plannedKey &&
                material.GetColor("_EarthColor") == preparedSwatch &&
                ProceduralPlanetTerrain.FreeHeight(plan, plan.routeNormal) == height,
                "Appearance settings changed a prepared world");
        }
        finally
        {
            settings.earth = originalEarth;
            settings.macroKeys[0] = originalKey;
        }
        var report = new { passed = true, repeatedPrepareRejected = rejected, appearanceSnapshot = true };
        Directory.CreateDirectory("Artifacts/TerrainAppearanceV6");
        File.WriteAllText("Artifacts/TerrainAppearanceV6/Immutability.json", Newtonsoft.Json.JsonConvert.SerializeObject(report));
        return report;
    }
}
