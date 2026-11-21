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

            // v11 flow river. flowFit: the channel only lowers the ground. pond: the river ends in a
            // shallow pond whose water fills all ground below pondLevel within pondRadius (metres).
            public bool flowFit, pond;
            public Vector3 pondDirection;
            public float pondLevel, pondRadius, pondBowl;
        }

        /// <summary>
        /// Depth removed to deepen a shallow basin into a pond. It fades out with the height above the water
        /// (so the new shore follows the terrain's contours instead of a circle) and radially to zero, with
        /// zero slope, at the pond rim, so the rim keeps its height.
        /// </summary>
        public static float PondCarve(Vector3 pondDirection, float pondRadius, float pondBowl, float pondLevel,
            float planetRadius, Vector3 direction, float elevation)
        {
            if (pondBowl <= 0f) return 0f;
            // cos(x) >= 1 - x^2/2, so a smaller dot product is certainly outside the rim; skips Acos per vertex.
            float x = pondRadius / planetRadius, dot = Vector3.Dot(direction, pondDirection);
            if (dot < 1f - .5f * x * x) return 0f;
            float t = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * planetRadius / pondRadius;
            if (t >= 1f) return 0f;
            float k = 1f - t * t;
            float above = Mathf.Clamp01((elevation - pondLevel) / Mathf.Max(.05f, 1.5f * pondBowl));
            return pondBowl * k * k * (1f - above * above * (3f - 2f * above));
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
            if (plan.natural != null && plan.river != null && plan.river.pond)
                original -= PondCarve(plan.river.pondDirection, plan.river.pondRadius, plan.river.pondBowl, plan.river.pondLevel,
                    plan.radius, n, original);
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
            if (plan.natural != null && plan.river != null && plan.river.pond && PondWet(plan, direction, margin))
            {
                surface = plan.river.pondLevel;
                return true;
            }
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
            // Near the pond the ground can already lie below the channel level; never raise it.
            if (river.flowFit) channel = Mathf.Min(channel, original);
            return Mathf.Lerp(original, channel, fade);
        }

        static float EndFade(River river, float along)
        {
            // A river that ends in a pond keeps its full width into the water; only the spring fades.
            if (river.pond) return Mathf.SmoothStep(0, 1, along / Mathf.Max(.001f, river.width * .85f));
            return Mathf.SmoothStep(0, 1, Mathf.Min(along, river.lengths[river.lengths.Count - 1] - along) /
                Mathf.Max(.001f, river.width * .85f));
        }

        static float Ground(ProceduralPlanetTerrain.Plan plan, Vector3 direction)
            => Height(plan, direction, ProceduralPlanetTerrain.FreeHeight(plan, direction));

        static float PondArc(River river, Vector3 direction, float radius)
            => Mathf.Acos(Mathf.Clamp(Vector3.Dot(direction, river.pondDirection), -1f, 1f)) * radius;

        /// <summary>Pond water covers the final ground below pondLevel inside pondRadius; margin widens the test sideways.</summary>
        static bool PondWet(ProceduralPlanetTerrain.Plan plan, Vector3 direction, float margin)
        {
            var river = plan.river;
            if (PondArc(river, direction, plan.radius) > river.pondRadius + margin) return false;
            if (Ground(plan, direction) < river.pondLevel) return true;
            if (margin <= 0f) return false;
            var tangent = Vector3.Cross(direction, Mathf.Abs(direction.y) < .8f ? Vector3.up : Vector3.right).normalized;
            float angle = margin / plan.radius;
            for (int k = 0; k < 6; k++)
            {
                var heading = Quaternion.AngleAxis(60f * k, direction) * tangent;
                var probe = (direction * Mathf.Cos(angle) + heading * Mathf.Sin(angle)).normalized;
                if (PondArc(river, probe, plan.radius) <= river.pondRadius && Ground(plan, probe) < river.pondLevel) return true;
            }
            return false;
        }

        /// <summary>0..1 wet-shore tint around the pond, from the pre-carve elevation.</summary>
        public static float PondShore(ProceduralPlanetTerrain.Plan plan, Vector3 direction, float elevation)
        {
            var river = plan.river;
            if (river == null || !river.pond || PondArc(river, direction, plan.radius) > river.pondRadius + river.bankWidth) return 0f;
            float rise = elevation - PondCarve(river.pondDirection, river.pondRadius, river.pondBowl, river.pondLevel,
                plan.radius, direction, elevation) - river.pondLevel;
            return 1f - Mathf.SmoothStep(0, 1, Mathf.Clamp01(rise / Mathf.Max(.01f, river.bankWidth * .12f)));
        }

        static float ChannelHalfWidth(River river, float fade)
            => river.width * .5f * Mathf.Lerp(.22f, 1f, fade);

        static float ShoreInset(River river) => Mathf.Min(.009f, river.width * .012f);

        /// <summary>
        /// Flat pond surface: a polar disc at the pond level. The ground above the level hides the rest of
        /// the disc, and planning guarantees the ground at the rim stays above the water.
        /// </summary>
        static void AppendPond(ProceduralPlanetTerrain.Plan plan, List<Vector3> vertices, List<int> triangles,
            List<Vector2> flow = null, List<Vector4> water = null)
        {
            const int Rings = 10, Segments = 64;
            var river = plan.river;
            var centre = river.pondDirection;
            var tangent = Vector3.Cross(centre, Mathf.Abs(centre.y) < .8f ? Vector3.up : Vector3.right).normalized;
            var bitangent = Vector3.Cross(centre, tangent);
            float height = plan.radius + river.pondLevel + ShoreInset(river);
            int first = vertices.Count;
            vertices.Add(centre * height);
            AddPondData(centre, 0f, 0f);
            for (int ring = 1; ring <= Rings; ring++)
            {
                float angle = river.pondRadius * ring / Rings / plan.radius;
                for (int s = 0; s < Segments; s++)
                {
                    float theta = s * Mathf.PI * 2f / Segments;
                    var heading = tangent * Mathf.Cos(theta) + bitangent * Mathf.Sin(theta);
                    var direction = (centre * Mathf.Cos(angle) + heading * Mathf.Sin(angle)).normalized;
                    vertices.Add(direction * height);
                    AddPondData(direction, angle * plan.radius * Mathf.Cos(theta), angle * plan.radius * Mathf.Sin(theta));
                }
            }

            // Still water: planar metres for the ripples, depth to the final ground (0 where it is dry ground).
            void AddPondData(Vector3 direction, float x, float y)
            {
                if (flow == null) return;
                flow.Add(new Vector2(x, y));
                water.Add(new Vector4(Mathf.Max(0f, river.pondLevel - Ground(plan, direction)), 0f, 0f, 1f));
            }
            int Index(int ring, int s) => ring == 0 ? first : first + 1 + (ring - 1) * Segments + (s % Segments);
            void Face(int a, int b, int c)
            {
                // Visible from above: Cross(b - a, c - a) points away from the planet, like the river ribbon.
                if (Vector3.Dot(Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]), vertices[a]) < 0f)
                { int t = b; b = c; c = t; }
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }
            for (int s = 0; s < Segments; s++) Face(Index(0, 0), Index(1, s), Index(1, s + 1));
            for (int ring = 1; ring < Rings; ring++)
            for (int s = 0; s < Segments; s++)
            {
                Face(Index(ring, s), Index(ring + 1, s), Index(ring + 1, s + 1));
                Face(Index(ring, s), Index(ring + 1, s + 1), Index(ring, s + 1));
            }
        }

        public static Mesh CreateMesh(ProceduralPlanetTerrain.Plan plan)
        {
            if (plan.natural != null) return CreateNaturalMesh(plan);
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

        /// <summary>
        /// Natural river and pond water for the B612/Water shader. The ribbon has five columns across so the
        /// depth can go from the banks to the centre. UV0 holds flow-space metres (x across, y downstream along
        /// the river, planar metres on the pond). UV1 holds water depth (m), flow speed (m/s, from the surface
        /// slope), distance from the centre line as 0..1 and the kind (0 river, 1 pond).
        /// </summary>
        static Mesh CreateNaturalMesh(ProceduralPlanetTerrain.Plan plan)
        {
            const int Columns = 5;
            var river = plan.river;
            int count = river.directions.Count;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var flow = new List<Vector2>();
            var water = new List<Vector4>();
            for (int i = 0; i < count; i++)
            {
                var n = river.directions[i];
                var tangent = river.directions[Mathf.Min(i + 1, count - 1)] - river.directions[Mathf.Max(0, i - 1)];
                var side = Vector3.Cross(n, tangent).normalized;
                float fade = EndFade(river, river.lengths[i]);
                float halfMeters = ChannelHalfWidth(river, fade) * .94f;
                // Same downhill surface as before, all the way to both ends.
                float height = plan.radius + river.levels[i] + ShoreInset(river);
                int upstream = Mathf.Max(0, i - 2), downstream = Mathf.Min(count - 1, i + 2);
                float run = Mathf.Max(.01f, river.lengths[downstream] - river.lengths[upstream]);
                float slope = Mathf.Max(0f, river.levels[upstream] - river.levels[downstream]) / run;
                float speed = Mathf.Clamp(.25f + 6f * slope, .2f, 1f);
                float centreDepth = Mathf.Max(0f, river.levels[i] - river.beds[i]);
                for (int c = 0; c < Columns; c++)
                {
                    float t = (c - (Columns - 1) * .5f) / ((Columns - 1) * .5f);
                    vertices.Add((n + side * (halfMeters * t / plan.radius)).normalized * height);
                    flow.Add(new Vector2(halfMeters * t, river.lengths[i]));
                    water.Add(new Vector4(centreDepth * (1f - t * t), speed, Mathf.Abs(t), 0f));
                }
                if (i == 0) continue;
                for (int c = 0; c < Columns - 1; c++)
                {
                    int a = (i - 1) * Columns + c, b = a + 1, cc = i * Columns + c, d = cc + 1;
                    // Only the radial top surface is visible, even at the horizon.
                    triangles.AddRange(new[] { cc, b, a, cc, d, b });
                }
            }
            if (river.pond) AppendPond(plan, vertices, triangles, flow, water);
            var mesh = new Mesh { name = "Connected shallow river" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, flow);
            mesh.SetUVs(1, water);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(vertices.ConvertAll(v => v.normalized));
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
