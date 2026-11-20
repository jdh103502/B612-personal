using System;
using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    public static class TerrainWater
    {
        [Serializable] public sealed class River
        {
            public List<Vector3> directions=new List<Vector3>();
            public List<Vector3> points=new List<Vector3>();
            public List<float> levels=new List<float>(),beds=new List<float>();
            public List<float> lengths = new List<float>();
            public float bankWidth;
            public Vector3 centre;public float bound,width=.9f;public int fordIndex=10;
        }
        public static void Plan(ProceduralPlanetTerrain.Plan plan)
        {
            if(plan.radius<5)return;
            var random=new System.Random(StarSystemFactory.DeriveSeed(plan.seed,44,0));
            for(int attempt=0;attempt<128;attempt++)
            {
                float along=Mathf.Lerp(.22f,.82f,(float)random.NextDouble())*Mathf.PI;
                var cross=plan.spawn*Mathf.Cos(along)+plan.routeTangent*Mathf.Sin(along);
                // Separate moisture stream biases the bounded source/basin search toward wetter areas.
                if(ProceduralPlanetTerrain.Noise(cross*2.8f+plan.noiseOffset,plan.seed^4141)<.3f)continue;
                bool site=false;foreach(float fraction in new[]{.18f,.42f,.72f,1f})if(Mathf.Abs(along-fraction*Mathf.PI)*plan.radius<2)site=true;
                if(site)continue;
                float length=Mathf.Min(5.2f,plan.radius*.65f);var across=plan.routeNormal*(random.Next(2)==0?1:-1);
                var first=(cross*Mathf.Cos(length/plan.radius)+across*Mathf.Sin(length/plan.radius)).normalized;
                var last=(cross*Mathf.Cos(length/plan.radius)-across*Mathf.Sin(length/plan.radius)).normalized;
                if(ProceduralPlanetTerrain.BaseHeight(plan,first)<ProceduralPlanetTerrain.BaseHeight(plan,last)){across=-across;var tmp=first;first=last;last=tmp;}
                float source=Mathf.Clamp(ProceduralPlanetTerrain.BaseHeight(plan,first)-.03f,.08f,.25f);
                float sink=Mathf.Clamp(ProceduralPlanetTerrain.BaseHeight(plan,last)-.03f,-.35f,-.12f);
                var river=new River{centre=cross*plan.radius,bound=length+2};bool valid=true;
                for(int i=0;i<=20;i++)
                {
                    float s=Mathf.Lerp(length,-length,i/20f);float bend=Mathf.Sin(i*Mathf.PI/10)*.35f;
                    var n=(cross*Mathf.Cos(s/plan.radius)+across*Mathf.Sin(s/plan.radius)+plan.routeTangent*(bend/plan.radius)).normalized;
                    foreach(var feature in plan.features)if(Mathf.Acos(Mathf.Clamp(Vector3.Dot(n,feature.centre),-1,1))*plan.radius<feature.reservedRadius+1)valid=false;
                    if (ProceduralPlanetTerrain.ConnectorDistance(plan, n) < ProceduralPlanetTerrain.ProtectedHalfWidth + 2f) valid = false;
                    if(Mathf.Acos(Mathf.Clamp(Vector3.Dot(n,plan.spawn),-1,1))*plan.radius<2.5f)valid=false;
                    float level=i<=10?Mathf.Lerp(source,-.02f,i/10f):Mathf.Lerp(-.02f,sink,(i-10)/10f);
                    float depth=Mathf.Lerp(.04f,.16f,Mathf.Clamp01(Mathf.Abs(i-10)/2f));float bed=level-depth;
                    float baseHeight=ProceduralPlanetTerrain.BaseHeight(plan,n);
                    if(baseHeight-bed>.7f||level+.06f-baseHeight>.4f)valid=false;
                    river.directions.Add(n);river.points.Add(n*plan.radius);river.levels.Add(level);river.beds.Add(bed);
                }
                if(valid){plan.river=river;return;}
            }
            plan.repairs.Add("River omitted after bounded source/basin/protected-space search.");
        }
        public static bool Sample(ProceduralPlanetTerrain.Plan plan,Vector3 n,out float distance,out float level,out float bed)
            => Sample(plan, n, out distance, out level, out bed, out _);

        static bool Sample(ProceduralPlanetTerrain.Plan plan, Vector3 n, out float distance, out float level,
            out float bed, out float along)
        {
            distance=float.PositiveInfinity;level=bed=along=0;var river=plan.river;if(river==null)return false;var point=n*plan.radius;
            if((point-river.centre).sqrMagnitude>river.bound*river.bound)return false;
            float best=float.PositiveInfinity;
            for(int i=1;i<river.points.Count;i++)
            {
                var a=river.points[i-1];var d=river.points[i]-a;float t=Mathf.Clamp01(Vector3.Dot(point-a,d)/d.sqrMagnitude);
                float sq=(point-a-d*t).sqrMagnitude;if(sq>=best)continue;
                best=sq;level=Mathf.Lerp(river.levels[i-1],river.levels[i],t);bed=Mathf.Lerp(river.beds[i-1],river.beds[i],t);
                if (river.lengths.Count != 0) along = Mathf.Lerp(river.lengths[i - 1], river.lengths[i], t);
            }
            distance=Mathf.Sqrt(best);return true;
        }
        public static float Height(ProceduralPlanetTerrain.Plan plan,Vector3 n,float original)
        {
            if(!Sample(plan,n,out float distance,out float level,out float bed,out float along))return original;
            if (plan.natural != null)
            {
                return NaturalChannelHeight(plan.river, original, distance, level, bed, EndFade(plan.river, along));
            }
            float inner=plan.river.width*.5f;
            if(distance<=inner)return bed;
            if(distance>inner+1.2f)return original;
            float t=Mathf.Clamp01((distance-inner)/.7f);t=t*t*(3-2*t);
            float bank=Mathf.Lerp(bed,Mathf.Max(original,level+.06f),t);
            float outer=Mathf.Clamp01((distance-inner-.7f)/.5f);outer=outer*outer*(3-2*outer);
            return Mathf.Lerp(bank,original,outer);
        }
        public static float MovementFactor(ProceduralPlanetTerrain.Plan plan,Vector3 direction,float footHeight)
        {
            if (plan.natural != null)
                return IsWet(plan, direction, 0f, out float surface) && footHeight < surface + .08f ? .65f : 1f;
            return Sample(plan,direction,out float distance,out float level,out _)&&distance<plan.river.width*.5f+.07f&&footHeight<level+.08f?.65f:1f;
        }

        public static bool IsWet(ProceduralPlanetTerrain.Plan plan, Vector3 direction, float margin, out float surface)
        {
            surface = 0f;
            if (!Sample(plan, direction, out float distance, out float level, out float bed, out float along)) return false;
            if (plan.natural == null)
            {
                surface = level;
                return distance <= plan.river.width * .5f + margin;
            }
            float fade = EndFade(plan.river, along);
            surface = level;
            float halfWidth = ChannelHalfWidth(plan.river, fade);
            if (fade <= 0f || distance > halfWidth * .94f + margin) return false;
            if (fade >= 1f) return true;
            float height = NaturalChannelHeight(plan.river, ProceduralPlanetTerrain.FreeHeight(plan, direction),
                Mathf.Min(distance, halfWidth * .94f), level, bed, fade);
            return height < level + ShoreInset(plan.river);
        }

        static float NaturalChannelHeight(River river, float original, float distance, float level, float bed, float fade)
        {
            float halfWidth = ChannelHalfWidth(river, fade);
            if (distance >= halfWidth + river.bankWidth) return original;
            float channel = distance <= halfWidth
                ? Mathf.Lerp(bed, level, Mathf.SmoothStep(0, 1, distance / halfWidth))
                : Mathf.Lerp(level, original, Mathf.SmoothStep(0, 1, (distance - halfWidth) / river.bankWidth));
            return Mathf.Lerp(original, channel, fade);
        }

        static float EndFade(River river, float along)
            => Mathf.SmoothStep(0, 1, Mathf.Min(along, river.lengths[river.lengths.Count - 1] - along) /
                Mathf.Max(.001f, river.width * .85f));

        static float ChannelHalfWidth(River river, float fade)
            => river.width * .5f * Mathf.Lerp(.22f, 1f, fade);

        static float ShoreInset(River river) => Mathf.Min(.009f, river.width * .012f);

        public static Mesh CreateMesh(ProceduralPlanetTerrain.Plan plan)
        {
            var river=plan.river;var vertices=new List<Vector3>();var triangles=new List<int>();
            for(int i=0;i<river.directions.Count;i++)
            {
                var n=river.directions[i];var tangent=river.directions[Mathf.Min(i+1,river.directions.Count-1)]-river.directions[Mathf.Max(0,i-1)];var side=Vector3.Cross(n,tangent).normalized;
                // Bury the ends of the visual ribbon in the bed so the full-width cut
                // does not appear as a bright blue facet against the horizon.
                int fromEnd = Mathf.Min(i, river.directions.Count - 1 - i);
                float edge = Mathf.SmoothStep(0f, 1f, fromEnd * .5f);
                float halfWidth = river.width * .5f * Mathf.Lerp(.45f, 1f, edge) / plan.radius;
                float height = plan.radius + Mathf.Lerp(river.beds[i] - .08f, river.levels[i] + .008f, edge);
                if (plan.natural != null)
                {
                    float fade = EndFade(river, river.lengths[i]);
                    halfWidth = ChannelHalfWidth(river, fade) * .94f / plan.radius;
                    // The uncut ground hides the tips. Keep the actual surface on the
                    // same downhill levels all the way to both submerged ends.
                    height = plan.radius + river.levels[i] + ShoreInset(river);
                }
                vertices.Add((n - side * halfWidth).normalized * height);
                vertices.Add((n + side * halfWidth).normalized * height);
                if(i==0)continue;int a=(i-1)*2,b=a+1,c=i*2,d=c+1;
                // Only the radial top surface is visible, even at the horizon.
                triangles.AddRange(new[]{c,b,a,c,d,b});
            }
            var mesh=new Mesh{name="Connected shallow river"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetNormals(vertices.ConvertAll(v=>v.normalized));mesh.RecalculateBounds();return mesh;
        }
    }
}
