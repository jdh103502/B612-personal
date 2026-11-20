using System;
using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    public enum TerrainRegionKind { Plain,Hill,Valley }
    public static class TerrainRegions
    {
        [Serializable] public sealed class Patch { public int cellId;public Vector3 centre;public TerrainRegionKind kind; }
        [Serializable] public sealed class Cell
        { public int id,patch,parent=-1;public TerrainRegionKind region;public float elevation,roughness,moisture;public bool protectedRoute; }
        public static void Build(ProceduralPlanetTerrain.Plan plan,int frequency,Vector3 weights)
        {
            var topology=PlanetSurfaceGrid.CreateTopology(frequency);var directions=new Vector3[topology.Count];
            for(int i=0;i<directions.Length;i++)directions[i]=(topology[i].a+topology[i].b+topology[i].c).normalized;
            var random=new System.Random(StarSystemFactory.DeriveSeed(plan.seed,41,0));int count=Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(topology.Count)),6,18);
            for(int p=0;p<count;p++)
            {
                int selected=0;float best=-1;
                for(int i=0;i<directions.Length;i++)
                {
                    float nearest=2;foreach(var patch in plan.patches)nearest=Mathf.Min(nearest,1-Vector3.Dot(patch.centre,directions[i]));
                    float score=nearest+ProceduralPlanetTerrain.Noise(directions[i]*4.3f+plan.noiseOffset,plan.seed^4101)*.015f;
                    if(score>best){best=score;selected=i;}
                }
                var n=directions[selected];float elevation=ProceduralPlanetTerrain.Noise(n*2.1f+plan.noiseOffset,plan.seed);
                float rough=ProceduralPlanetTerrain.Noise(n*3.3f+plan.noiseOffset,plan.seed^4121);
                float moisture=ProceduralPlanetTerrain.Noise(n*2.8f+plan.noiseOffset,plan.seed^4141);
                var scores=new[]{weights.x*Mathf.Exp((.5f-Mathf.Abs(elevation-.5f))*1.5f),weights.y*Mathf.Exp((elevation-.5f)*2+rough*.3f),weights.z*Mathf.Exp((.5f-elevation)*2+moisture*.4f)};
                float choice=(float)random.NextDouble()*(scores[0]+scores[1]+scores[2]);int kind=choice<scores[0]?0:choice<scores[0]+scores[1]?1:2;
                plan.patches.Add(new Patch{cellId=selected,centre=n,kind=(TerrainRegionKind)kind});
            }
            var distance=new float[topology.Count];var visited=new bool[topology.Count];var owners=new int[topology.Count];var parents=new int[topology.Count];
            for(int i=0;i<distance.Length;i++){distance[i]=float.PositiveInfinity;parents[i]=-1;}
            for(int p=0;p<plan.patches.Count;p++){int cell=plan.patches[p].cellId;distance[cell]=0;owners[cell]=p;}
            for(int step=0;step<topology.Count;step++)
            {
                int best=-1;for(int i=0;i<distance.Length;i++)if(!visited[i]&&(best<0||distance[i]<distance[best]))best=i;
                visited[best]=true;
                foreach(int other in topology[best].neighbors)
                {
                    if(visited[other])continue;
                    float affinity=ProceduralPlanetTerrain.Noise(directions[other]*2.1f+plan.noiseOffset,plan.seed);
                    int kind=(int)plan.patches[owners[best]].kind;
                    float fit=kind==0?1-Mathf.Abs(affinity-.5f):kind==1?affinity:1-affinity;
                    float cost=distance[best]+Mathf.Acos(Mathf.Clamp(Vector3.Dot(directions[best],directions[other]),-1,1))*(1+2*(1-fit));
                    if(cost<distance[other]){distance[other]=cost;owners[other]=owners[best];parents[other]=best;}
                }
            }
            for(int i=0;i<topology.Count;i++)plan.regions.Add(new Cell{id=i,patch=owners[i],parent=parents[i],region=plan.patches[owners[i]].kind,
                elevation=ProceduralPlanetTerrain.Noise(directions[i]*2.1f+plan.noiseOffset,plan.seed),roughness=ProceduralPlanetTerrain.Noise(directions[i]*3.3f+plan.noiseOffset,plan.seed^4121),moisture=ProceduralPlanetTerrain.Noise(directions[i]*2.8f+plan.noiseOffset,plan.seed^4141),
                protectedRoute=Mathf.Min(ProceduralPlanetTerrain.MainRouteDistance(plan,directions[i]),
                    ProceduralPlanetTerrain.ConnectorDistance(plan,directions[i]))<ProceduralPlanetTerrain.ProtectedHalfWidth+1});
        }
        // The remaining weight belongs to plain terrain. No logical cell edges are sampled.
        public static Vector2 BlendWeights(ProceduralPlanetTerrain.Plan plan, Vector3 n)
        {
            float sum = 0f, hill = 0f, valley = 0f;
            foreach(var patch in plan.patches)
            {
                float w=Mathf.Max(0,(Vector3.Dot(n,patch.centre)+1)*.5f);float square=w*w;float fourth=square*square;w=fourth*fourth*fourth;
                sum+=w;if(patch.kind==TerrainRegionKind.Hill)hill+=w;if(patch.kind==TerrainRegionKind.Valley)valley+=w;
            }
            return sum > 0f ? new Vector2(hill / sum, valley / sum) : Vector2.zero;
        }
    }
}
