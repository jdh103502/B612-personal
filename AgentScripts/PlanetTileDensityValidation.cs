using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using B612;
using UnityEngine;

public static class PlanetTileDensityValidation
{
    static void Check(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    static void Reject(Action action,string reason)
    {bool rejected=false;try{action();}catch(ArgumentOutOfRangeException){rejected=true;}catch(InvalidOperationException){rejected=true;}Check(rejected,reason);}
    static readonly MethodInfo topology=typeof(PlanetSurfaceGrid).GetMethod("BuildCells",BindingFlags.NonPublic|BindingFlags.Static);
    static readonly MethodInfo drawing=typeof(PlanetSurfaceGrid).GetMethod("DrawingCells",BindingFlags.NonPublic|BindingFlags.Instance);
    static float Next(float value,int step)=>BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(value),0)+step),0);
    static string[] Fingerprints(PlanetSurfaceGrid grid)
    {
        using(var geometry=new MemoryStream())using(var candidates=new MemoryStream())using(var sha=SHA256.Create())
        {
            var a=new BinaryWriter(geometry);var b=new BinaryWriter(candidates);
            foreach(var cell in grid.Cells)
            {
                a.Write(cell.id);foreach(var v in new[]{cell.a,cell.b,cell.c}){a.Write(v.x);a.Write(v.y);a.Write(v.z);}a.Write(cell.solidAngle);foreach(var n in cell.neighbors)a.Write(n);
                foreach(var slot in cell.slots){b.Write(slot.cellId);foreach(var v in new[]{slot.localPoint,slot.localNormal}){b.Write(v.x);b.Write(v.y);b.Write(v.z);}}
            }
            return new[]{BitConverter.ToString(sha.ComputeHash(geometry.ToArray())).Replace("-",""),BitConverter.ToString(sha.ComputeHash(candidates.ToArray())).Replace("-","")};
        }
    }
    public static object Validate()
    {
        var passed=new List<string>();
        float[] sizes={.7f,1f,1.25f,1.5f,1.75f,2f};int[] frequencies={2,2,3,3,4,4};
        for(int i=0;i<sizes.Length;i++)Check(PlanetSurfaceGrid.CalculateFrequency(1,sizes[i])==frequencies[i],"Default resolution table");
        Check(PlanetSurfaceGrid.CalculateFrequency(1,Next(1,-1))==2&&PlanetSurfaceGrid.CalculateFrequency(1,Next(1,1))==3,"1x immediate float boundary");
        Check(PlanetSurfaceGrid.CalculateFrequency(1,Next(1.5f,-1))==3&&PlanetSurfaceGrid.CalculateFrequency(1,Next(1.5f,1))==4,"1.5x immediate float boundary");
        foreach(int level in new[]{-1,3})Reject(()=>PlanetSurfaceGrid.CalculateFrequency(level,1),"Unsupported baseline clamped");
        foreach(float size in new[]{.699f,2.001f,float.NaN,float.PositiveInfinity,float.NegativeInfinity})Reject(()=>PlanetSurfaceGrid.CalculateFrequency(1,size),"Invalid scale accepted");
        for(int level=0;level<=2;level++)foreach(float size in sizes)
        {
            int f=PlanetSurfaceGrid.CalculateFrequency(level,size),baseCount=20*(1<<(level*2));
            Check(size*size*baseCount/(20*f*f)<=1.000001f,"Average spherical cell area exceeds baseline");
        }
        passed.Add("resolution table, adjacent float boundaries, finite/range/baseline validation, average area bound");
        var stats=new List<object>();
        for(int f=1;f<=8;f++)
        {
            var cells=new List<PlanetSurfaceGrid.SurfaceCell>();object[] args={f,cells,0,0};topology.Invoke(null,args);
            var vertices=new Dictionary<Vector3,int>();var faces=new List<int[]>();var edges=new Dictionary<long,int>();var triangles=new HashSet<long>();double angle=0;
            for(int i=0;i<cells.Count;i++)
            {
                var c=cells[i];Check(c.id==i&&c.neighbors.Distinct().Count()==3&&c.neighbors.All(n=>n>=0&&n<cells.Count&&n!=i),"Invalid IDs/neighbors");
                Check(Vector3.Dot(Vector3.Cross(c.b-c.a,c.c-c.a),c.a+c.b+c.c)>1e-8f,"Degenerate/flipped triangle");
                int[] ids=new int[3];int index=0;
                foreach(var v in new[]{c.a,c.b,c.c}){Check(Mathf.Abs(v.magnitude-1)<.000001f,"Unnormalized vertex");if(!vertices.TryGetValue(v,out int id)){id=vertices.Count;vertices.Add(v,id);}ids[index++]=id;}
                faces.Add(ids);for(int side=0;side<3;side++){int a=ids[side],b=ids[(side+1)%3];long key=((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);edges.TryGetValue(key,out int uses);edges[key]=uses+1;}
                var sorted=ids.OrderBy(id=>id).ToArray();Check(triangles.Add(((long)sorted[0]<<42)|((long)sorted[1]<<21)|(uint)sorted[2]),"Duplicate triangle");
                Check(c.solidAngle>0,"Invalid solid angle");angle+=c.solidAngle;
            }
            Check(cells.Count==20*f*f&&vertices.Count==10*f*f+2&&edges.Count==30*f*f&&edges.Values.All(n=>n==2),"Sphere topology/seam count");
            Check((int)args[2]==vertices.Count&&(int)args[3]==edges.Count,"Reported vertex/edge count");
            Check(Math.Abs(angle-Math.PI*4)<.0002,"Solid angle sum is not 4pi");
            for(int i=0;i<cells.Count;i++)foreach(int n in cells[i].neighbors)
                Check(cells[n].neighbors.Contains(i)&&faces[i].Intersect(faces[n]).Count()==2,"Neighbor does not share a reciprocal edge");
            var random=new System.Random(612+f);int boundaries=0;
            for(int i=0;i<4096;i++)
            {
                float y=(float)(random.NextDouble()*2-1),phi=(float)(random.NextDouble()*Math.PI*2),ring=Mathf.Sqrt(1-y*y);
                var direction=new Vector3(ring*Mathf.Cos(phi),y,ring*Mathf.Sin(phi));int count=0;bool nearEdge=false;
                foreach(var cell in cells)if(PlanetSurfaceGrid.Contains(cell,direction))
                {
                    count++;var vs=new[]{cell.a,cell.b,cell.c};
                    for(int side=0;side<3;side++)nearEdge|=Mathf.Abs(Vector3.Dot(Vector3.Cross(vs[side],vs[(side+1)%3]).normalized,direction))<.0002f;
                }
                Check(count>=1,"Gap in spherical containment");Check(count==1||nearEdge,"Overlapping cell interiors");if(nearEdge)boundaries++;
            }
            Check(boundaries<205,"Containment test has too few interior samples");
            stats.Add(new{frequency=f,cells=cells.Count,vertices=vertices.Count,edges=edges.Count,solidAngle=angle,boundarySamples=boundaries});
        }
        passed.Add("all frequencies 1..8: closed shared seams, orientation, reciprocal neighbors, V/E/F counts, 4pi and 32,768 uniform containment samples");
        var host=UnityEngine.Object.FindFirstObjectByType<PlanetWorldHost>();var system=StarSystemFactory.Create(612101,host.settings);
        Check(system.planets.All(p=>p.spec.generatorVersion==PlanetGenerationSettings.GeneratorVersion&&p.modelVersion==1),"Generator/data versions conflated");system.planets[1].spec.generatorVersion=1;
        Reject(()=>StarSystemFactory.Validate(system,host.settings),"Legacy generator version accepted for new cell IDs");
        if(!Application.isPlaying)
        {
            var grid=host.initialWorld.surface;grid.ConfigureResolution(1,1);Physics.SyncTransforms();grid.Prepare(612101);
            var golden=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("AgentScripts/Fixtures/PlanetGridV1Baseline.json"));var fingerprints=Fingerprints(grid);
            Check(fingerprints[0]==golden.Value<string>("topology")&&fingerprints[1]==golden.Value<string>("candidates"),"1x baseline order/candidate coordinates changed");
            var cell=grid.Cells[0];var slot=cell.slots[0];slot.occupied=true;
            Reject(()=>grid.ConfigureResolution(3,1),"Invalid configure accepted");Check(grid.Cells[0]==cell&&grid.Cells[0].slots[0]==slot&&slot.occupied,"Rejected config damaged live cells");slot.occupied=false;
            var editor=UnityEditor.Editor.CreateEditor(grid);try{Check(editor.GetType().Name=="PlanetSurfaceGridEditor"&&editor.CreateInspectorGUI()!=null,"Read-only inspector failed");}finally{UnityEngine.Object.DestroyImmediate(editor);}
            passed.Add("1x exact pre-change topology/candidate SHA256 match; rejected config is non-destructive; custom read-only Inspector");
        }
        File.WriteAllText("Temp/PlanetTileDensityTopologyValidation.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{passed=true,cases=passed,topologies=stats},Newtonsoft.Json.Formatting.Indented));
        return new{passed=true,cases=passed.ToArray(),topologies=stats.ToArray()};
    }
    public static object ValidatePreparedWorlds()
    {
        Check(Application.isPlaying,"Run in Play mode");
        var flow=UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();var source=flow.worldHost.settings;
        var root=new GameObject("Density world validation");var host=root.AddComponent<PlanetWorldHost>();var testSettings=UnityEngine.Object.Instantiate(source);host.settings=testSettings;
        var rows=new List<object>();
        try
        {
            foreach(var input in new[]{new{size=2f,level=1},new{size=.7f,level=1},new{size=1f,level=1},new{size=1.25f,level=1},new{size=1.5f,level=1},new{size=1.75f,level=1},new{size=2f,level=2}})
            {
                host.settings.subdivisions=input.level;
                var data=JsonUtility.FromJson<PlanetData>(JsonUtility.ToJson(flow.Current));data.spec.terrain.sizeScale=input.size;data.spec.terrain.radius=source.nominalRadius*input.size;data.spec.sizeClass=PlanetGenerationSettings.Classify(input.size);
                var timer=Stopwatch.StartNew();var world=host.Prepare(data,flow.player.GetComponent<CapsuleCollider>());timer.Stop();
                var grid=world.surface;int f=PlanetSurfaceGrid.CalculateFrequency(input.level,input.size),expected=20*f*f;
                Check(grid.PreparedFrequency==f&&grid.Cells.Count==expected&&grid.SlotCount<=expected*32&&grid.SlotCount>0,"Prepared density/candidate cap");
                Check(world.ground.GetComponent<MeshFilter>().sharedMesh==source.groundTemplate.GetComponent<MeshFilter>().sharedMesh&&((MeshCollider)world.ground).sharedMesh==source.groundTemplate.GetComponent<MeshCollider>().sharedMesh,"Logical density replaced terrain mesh/collider");
                var before=Fingerprints(grid);grid.Prepare(data.spec.seed);Check(Fingerprints(grid).SequenceEqual(before),"Candidate regeneration changed seeded result");
                var cell=grid.Cells.First(c=>c.slots.Count>0);var slot=cell.slots[0];slot.occupied=true;
                for(int i=0;i<3;i++){Check(ReferenceEquals(drawing.Invoke(grid,new object[]{true}),grid.Cells),"Play Gizmos switched cells");drawing.Invoke(grid,new object[]{false});}
                Check(grid.Cells[cell.id]==cell&&cell.slots[0]==slot&&slot.occupied,"Gizmos preview mutated active slots/occupancy");slot.occupied=false;
                var surgeObject=new GameObject("Density surge validation");var surge=surgeObject.AddComponent<BaobabSurge>();surge.automaticStart=false;surge.surface=grid;surge.sproutPrefab=flow.surge.sproutPrefab;surge.spawnPoint=world.spawn;surge.player=flow.player.transform;surge.randomizeSeed=false;surge.seed=data.spec.seed;
                try
                {
                    surge.Begin();Check(grid.PreparedFrequency==f&&grid.Cells.Count==expected&&Fingerprints(grid).SequenceEqual(before),"Surge Begin reverted density");surge.Advance(120);
                    Check(surge.ActiveSproutCount==6&&surge.durationSeconds==120&&surge.targetSprouts==40&&surge.minimumSpacing==1.4f,"Density changed spawn policy");
                    Check(((int[])typeof(BaobabSurge).GetField("cellCounts",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(surge)).Length==expected,"Stale per-cell occupancy array");
                    var active=surge.Spawned.Where(r=>r.IsActive).ToArray();
                    for(int i=0;i<active.Length;i++)for(int j=0;j<i;j++)Check(Vector3.Distance(grid.WorldPoint(active[i].slot),grid.WorldPoint(active[j].slot))>=1.399f,"World spacing changed");
                    foreach(var record in active)Check(PlanetSurfaceGrid.Contains(grid.Cells[record.slot.cellId],record.slot.localPoint.normalized)&&record.slot.occupied,"Invalid slot ownership/containment");
                    var handObject=new GameObject("Density hand fixture");var hand=handObject.AddComponent<LambCardHand>();hand.surge=surge;hand.player=flow.player.GetComponent<GravityWalker>();hand.lambPrefab=flow.hand.lambPrefab;hand.ringMaterial=flow.hand.ringMaterial;
                    try
                    {
                        var target=active[0];var foodSlot=target.slot;var foodCell=grid.Cells[foodSlot.cellId];var normal=grid.WorldNormal(foodSlot);
                        var lambObject=UnityEngine.Object.Instantiate(hand.lambPrefab,grid.WorldPoint(foodSlot)+normal*.01f,Quaternion.FromToRotation(Vector3.up,normal),grid.transform);var lamb=lambObject.GetComponent<LambAgent>();
                        ((LambAgent[])typeof(LambCardHand).GetField("cards",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(hand))[0]=lamb;
                        ((List<LambAgent>)typeof(LambCardHand).GetField("deployed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(hand)).Add(lamb);
                        lamb.Initialize(hand,0,target);Check(lamb.Target==target&&target.FeedingLamb==lamb,"Dense-grid feeding claim");
                        drawing.Invoke(grid,new object[]{true});drawing.Invoke(grid,new object[]{false});
                        Check(lamb.Target==target&&grid.Cells[foodSlot.cellId]==foodCell&&foodSlot.occupied,"Drawing changed active feeding identity");
                        lamb.Advance(lamb.EatingDuration+.01f);Check(lamb.SproutsEaten==1&&!target.IsActive&&!foodSlot.occupied,"Eating did not release dense-grid occupancy");
                        hand.RecallForTravel();Check(hand.InHandCount==4&&hand.ClaimCount==0&&hand.Deployed.Count==0,"Dense-grid lamb recall leaked cards/claims");
                    }
                    finally{UnityEngine.Object.DestroyImmediate(handObject);}
                    surge.StopAndClear();Check(grid.Cells.All(c=>c.slots.All(s=>!s.occupied)),"Removed sprouts retained occupancy");
                    if(f==4||f==8)
                    {
                        surge.minimumSpacing=1000;surge.maxActiveSprouts=40;surge.Begin();surge.Advance(120);
                        Check(surge.SpawnedCount==1&&surge.SkippedSpawns==39&&surge.IsComplete,"Exhausted candidates did not terminate finitely");surge.StopAndClear();
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(surgeObject);}
                rows.Add(new{size=input.size,baseline=input.level,frequency=f,cells=grid.Cells.Count,slots=grid.SlotCount,emptyCells=grid.EmptyCellCount,prepareMs=timer.Elapsed.TotalMilliseconds});world.DisposeWorld();
            }
            File.WriteAllText("Temp/PlanetTileDensityPreparedValidation.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{passed=true,worlds=rows},Newtonsoft.Json.Formatting.Indented));
            return new{passed=true,worlds=rows.ToArray()};
        }
        finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(testSettings);}
    }
}
