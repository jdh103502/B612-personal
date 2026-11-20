using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace B612
{
    /// <summary>Seeded continuous regions, reviewed traversal features and connected shallow rivers.</summary>
    public sealed class ProceduralPlanetTerrain : MonoBehaviour
    {
        [Serializable] public sealed class Feature
        {
            public int kind; // 0: valley + bypass, 1: high ledge + side ramp, 2: glide + return path.
            public Vector3 centre,right,north;
            public float reservedRadius;
            public Vector2 Coordinates(Vector3 direction,float radius)
                =>new Vector2(Mathf.Atan2(Vector3.Dot(direction,right),Vector3.Dot(direction,centre))*radius,
                    Mathf.Asin(Mathf.Clamp(Vector3.Dot(direction,north),-1,1))*radius);
            public Vector3 Direction(Vector2 point,float radius)
            {float x=point.x/radius,z=point.y/radius;return (centre*Mathf.Cos(x)+right*Mathf.Sin(x))*Mathf.Cos(z)+north*Mathf.Sin(z);}
        }
        [Serializable] public sealed class Plan
        {
            public int seed,version=PlanetGenerationSettings.GeneratorVersion,chapter=1,requestedFeatures,placementAttempts,buildAttempts;
            public int requiredKind = -1;
            public int reviewedLayout;
            public float radius,amplitude,maximumWalkSlope,validationMilliseconds;
            public Vector3 spawn,routeTangent,routeNormal,noiseOffset;
            public TerrainAppearanceSettings appearance = new TerrainAppearanceSettings();
            public string profileId;
            public NaturalTerrainSettings natural;
            public NaturalTerrainPlacement.Report placement;
            public int riverSeed, placementSeed, riverSourceAttempts, riverShortTraces, riverRejectedBanks;
            public string riverStatus;
            public float riverPlanMilliseconds, meshMilliseconds;
            public int macroSeed, hillSeed, warpSeed, ridgeSeed, surfaceSeed;
            public int landformSeed, landformAttempts;
            public List<TerrainLandforms.Landform> landforms = new List<TerrainLandforms.Landform>();
            public Vector3 macroOffset, hillOffset;
            public bool safeFallback, fixedPlacement, useNormalPlanetRelief;
            public Vector2 HillHeights => useNormalPlanetRelief ? appearance.normalPlanetHillHeights : appearance.hillHeights;
            public Vector2 BasinDepths => useNormalPlanetRelief ? appearance.normalPlanetBasinDepths : appearance.basinDepths;
            public List<Feature> features=new List<Feature>();
            public List<RouteConnector> connectors = new List<RouteConnector>();
            public List<string> repairs=new List<string>();
            public List<TerrainRegions.Patch> patches=new List<TerrainRegions.Patch>();
            public List<TerrainRegions.Cell> regions=new List<TerrainRegions.Cell>();
            public TerrainWater.River river;
            public float mainRouteSeconds;
            public List<TerrainTraversalVerifier.Cost> traversalCosts=new List<TerrainTraversalVerifier.Cost>();
        }
        [Serializable] public sealed class RouteConnector
        {
            public Vector3 anchor, pad;
            public Vector3 planeNormal;

            public float Distance(Vector3 direction, float radius)
            {
                bool withinArc = Vector3.Dot(Vector3.Cross(anchor, direction), planeNormal) >= 0f &&
                                 Vector3.Dot(Vector3.Cross(direction, pad), planeNormal) >= 0f;
                if (withinArc)
                    return Mathf.Asin(Mathf.Clamp01(Mathf.Abs(Vector3.Dot(direction, planeNormal)))) * radius;
                return Mathf.Min(Arc(anchor, direction, radius), Arc(pad, direction, radius));
            }
        }
        public const int MaxBuildAttempts = 5;
        // PhysX warns above this triangle count when Fast Midphase is enabled.
        const int FastMidphaseTriangleLimit = 2097152;
        [SerializeField] Plan plan;
        public Plan GenerationPlan=>plan;
        public Mesh OwnedMesh { get; private set; }
        public Mesh WaterMesh { get; private set; }
        internal NaturalTerrainPlacement NaturalPlacement { get; set; }
        Material waterMaterial;
        public const float ProtectedHalfWidth=1.25f;
        static float Smooth(float a,float b,float value){float t=Mathf.Clamp01((value-a)/(b-a));return t*t*(3-2*t);}
        static float Arc(Vector3 a,Vector3 b,float radius)=>Mathf.Acos(Mathf.Clamp(Vector3.Dot(a,b),-1,1))*radius;
        public static Plan CreatePlan(int seed,float radius,Vector3 spawn,int featureBudget,int chapter=1,int frequency=2,
            Vector3? weights=null, int requiredKind=-1, int reviewedLayout=0, TerrainAppearanceSettings appearance=null,
            bool useNormalPlanetRelief=false)
        {
            if(radius<2||float.IsNaN(radius)||float.IsInfinity(radius))throw new ArgumentOutOfRangeException(nameof(radius));
            if(spawn.sqrMagnitude<.01f||float.IsNaN(spawn.sqrMagnitude)||float.IsInfinity(spawn.sqrMagnitude))throw new ArgumentException("Invalid spawn direction.");
            var regionWeights=weights??ChapterTerrainDefinition.DefaultWeights(chapter);
            if(regionWeights.x<0||regionWeights.y<0||regionWeights.z<0||float.IsNaN(regionWeights.sqrMagnitude)||float.IsInfinity(regionWeights.sqrMagnitude)||regionWeights.sqrMagnitude<.001f)throw new ArgumentException("Invalid region weights.");
            var random=new System.Random(StarSystemFactory.DeriveSeed(seed,30,0));
            var start=spawn.normalized;
            var tangent=Vector3.Cross(start,Mathf.Abs(start.y)<.8f?Vector3.up:Vector3.right).normalized;
            tangent=Quaternion.AngleAxis((float)random.NextDouble()*360,start)*tangent;
            bool normalRelief = useNormalPlanetRelief && requiredKind < 0;
            var result=new Plan{seed=seed,radius=radius,chapter=chapter,spawn=start,routeTangent=tangent,routeNormal=Vector3.Cross(start,tangent).normalized,
                amplitude=normalRelief ? Mathf.Min(3.35f,radius*.28f) : Mathf.Min(1.6f,radius*.14f),
                useNormalPlanetRelief=normalRelief,
                noiseOffset=new Vector3((float)random.NextDouble()*40,(float)random.NextDouble()*40,(float)random.NextDouble()*40)};
            result.appearance = (appearance ?? new TerrainAppearanceSettings()).Snapshot();
            result.macroSeed = StarSystemFactory.DeriveSeed(seed, 51, 0);
            result.hillSeed = StarSystemFactory.DeriveSeed(seed, 52, 0);
            result.warpSeed = StarSystemFactory.DeriveSeed(seed, 53, 0);
            result.ridgeSeed = StarSystemFactory.DeriveSeed(seed, 54, 0);
            result.surfaceSeed = StarSystemFactory.DeriveSeed(seed, 55, 0);
            result.landformSeed = StarSystemFactory.DeriveSeed(seed, 56, 0);
            result.macroOffset = NoiseOffset(result.macroSeed);
            result.hillOffset = NoiseOffset(result.hillSeed);
            result.requiredKind = requiredKind;
            result.reviewedLayout = reviewedLayout;
            result.fixedPlacement = requiredKind >= 0 && reviewedLayout == MaxBuildAttempts - 1;
            result.requestedFeatures=radius<8?0:Mathf.Min(Mathf.Clamp(featureBudget,0,2),radius<11?1:2);
            if (requiredKind >= 0)
            {
                ReserveReviewedCourse(result);
                result.requestedFeatures = Mathf.Max(1, result.requestedFeatures);
            }
            TerrainRegions.Build(result,frequency,regionWeights);
            if (result.fixedPlacement)
            {
                result.amplitude = 0f;
                return result;
            }
            for(int index=result.features.Count;index<result.requestedFeatures;index++)
            {
                int kind=radius<10?0:random.Next(2);
                if(chapter==3&&radius>=12&&index==0)kind=2;
                else if(result.features.Count>0)kind=result.features[0].kind==0?1:0;
                float reserved = ReservationRadius(kind, result.appearance);
                bool placed=false;
                for(int attempt=0;attempt<64;attempt++)
                {
                    result.placementAttempts++;
                    float y=(float)random.NextDouble()*2-1,angle=(float)random.NextDouble()*Mathf.PI*2;
                    var centre=new Vector3(Mathf.Sqrt(1-y*y)*Mathf.Cos(angle),y,Mathf.Sqrt(1-y*y)*Mathf.Sin(angle));
                    if(MainRouteDistance(result,centre)<reserved+1.6f || Arc(start,centre,radius)<reserved+2f)continue;
                    if (ConnectorDistance(result, centre) < reserved + ProtectedHalfWidth + 1f) continue;
                    bool overlaps=false;foreach(var other in result.features)if(Arc(other.centre,centre,radius)<other.reservedRadius+reserved+1f){overlaps=true;break;}
                    if(overlaps)continue;
                    var right=Vector3.Cross(centre,Mathf.Abs(centre.y)<.8f?Vector3.up:Vector3.right).normalized;
                    right=Quaternion.AngleAxis((float)random.NextDouble()*360,centre)*right;
                    result.features.Add(new Feature{kind=kind,centre=centre,right=right,north=Vector3.Cross(centre,right).normalized,reservedRadius=reserved});
                    placed=true;break;
                }
                if(!placed)result.repairs.Add("Feature "+index+": omitted after 64 placement attempts; reserved space kept.");
            }
            TerrainLandforms.Build(result);
            TerrainWater.Plan(result);
            return result;
        }
        static float ReservationRadius(int kind, TerrainAppearanceSettings appearance)
            => (kind == 0 ? 7.5f : kind == 1 ? 11f : 13.5f) + appearance.transitionMeters - 1.5f;

        static void ReserveReviewedCourse(Plan plan)
        {
            // Keep the reviewed cross-section in its own spherical frame. Connect its pads
            // along meridians to the protected route on the side opposite the bypass.
            var random = new System.Random(StarSystemFactory.DeriveSeed(plan.seed, 46, plan.reviewedLayout));
            float along = plan.fixedPlacement ? .5f * Mathf.PI : Mathf.Lerp(.43f, .57f, (float)random.NextDouble()) * Mathf.PI;
            float offset = plan.fixedPlacement ? 1.34f : Mathf.Lerp(1.31f, 1.39f, (float)random.NextDouble());
            float side = plan.fixedPlacement || random.Next(2) == 0 ? 1f : -1f;
            Vector3 midpoint = plan.spawn * Mathf.Cos(along) + plan.routeTangent * Mathf.Sin(along);
            Vector3 forward = -plan.spawn * Mathf.Sin(along) + plan.routeTangent * Mathf.Cos(along);
            Vector3 centre = (midpoint * Mathf.Cos(offset) + side * plan.routeNormal * Mathf.Sin(offset)).normalized;
            var course = new Feature
            {
                kind = plan.requiredKind,
                centre = centre,
                right = side * forward,
                north = Vector3.Cross(centre, side * forward).normalized,
                reservedRadius = ReservationRadius(plan.requiredKind, plan.appearance)
            };
            plan.features.Add(course);
            AddConnector(TraversalTestCourseGeometry.Start(course.kind));
            AddConnector(TraversalTestCourseGeometry.Goal(course.kind));

            void AddConnector(Vector2 point)
            {
                float latitude = -Mathf.Atan2(Mathf.Cos(point.x / plan.radius) * Mathf.Sin(offset), Mathf.Cos(offset));
                Vector3 pad = course.Direction(point, plan.radius);
                Vector3 anchor = course.Direction(new Vector2(point.x, latitude * plan.radius), plan.radius);
                plan.connectors.Add(new RouteConnector
                {
                    anchor = anchor,
                    pad = pad,
                    planeNormal = Vector3.Cross(anchor, pad).normalized
                });
            }
        }

        public static float ConnectorDistance(Plan plan, Vector3 direction)
        {
            float distance = float.PositiveInfinity;
            foreach (var connector in plan.connectors)
                distance = Mathf.Min(distance, connector.Distance(direction, plan.radius));
            return distance;
        }
        public static float MainRouteDistance(Plan plan,Vector3 direction)
        {
            float along=Mathf.Atan2(Vector3.Dot(direction,plan.routeTangent),Vector3.Dot(direction,plan.spawn));
            return along>=0?Mathf.Abs(Mathf.Asin(Mathf.Clamp(Vector3.Dot(direction,plan.routeNormal),-1,1)))*plan.radius
                :Mathf.Min(Arc(direction,plan.spawn,plan.radius),Arc(direction,-plan.spawn,plan.radius));
        }
        public static float Height(Plan plan,Vector3 direction)
            =>TerrainWater.Height(plan,direction,BaseHeight(plan,direction));

        static Vector3 NoiseOffset(int seed)
        {
            var random = new System.Random(seed);
            return new Vector3((float)random.NextDouble() * 40f, (float)random.NextDouble() * 40f,
                (float)random.NextDouble() * 40f);
        }

        public static float FreeHeight(Plan plan, Vector3 direction)
        {
            if (plan.amplitude == 0f) return 0f;
            if (plan.natural != null)
            {
                Vector3 metres = direction * plan.radius;
                float broad = 2f * Noise(metres / plan.natural.broadScaleMeters + plan.macroOffset, plan.macroSeed) - 1f;
                float medium = 2f * Noise(metres / plan.natural.mediumScaleMeters + plan.hillOffset, plan.hillSeed) - 1f;
                return broad * plan.natural.broadAmplitudeMeters + medium * plan.natural.mediumAmplitudeMeters;
            }
            var settings = plan.appearance;
            Vector3 sample = direction;
            if (settings.warpStrength > 0f)
            {
                Vector3 p = direction * 1.35f + plan.noiseOffset;
                Vector3 displacement = new Vector3(Noise(p, plan.warpSeed), Noise(p, plan.warpSeed ^ 71),
                    Noise(p, plan.warpSeed ^ 139)) * 2f - Vector3.one;
                sample = (sample + displacement * settings.warpStrength).normalized;
            }
            var region = TerrainRegions.BlendWeights(plan, direction);
            float macro = settings.EvaluateMacro(Noise(sample * settings.macroFrequency + plan.macroOffset, plan.macroSeed));
            float scale = Mathf.Min(settings.hillScaleMeters, plan.radius / settings.macroFrequency);
            float hill = 2f * Noise(sample * (plan.radius / scale) + plan.hillOffset, plan.hillSeed) - 1f;
            float size = Mathf.Clamp(plan.radius / 12f, .3f, 1f);
            float macroBudget = settings.residualMacroMeters * size;
            float hillBudget = settings.residualHillMeters * size;
            float shapeBudget = plan.amplitude - macroBudget - hillBudget;
            float landform = TerrainLandforms.Height(plan, direction, out _);
            // Allocate the remainder to broad shapes and optional ridges. The scale is
            // constant per planet, so no peak is clipped and no plateau is stamped in.
            float shapeScale = Mathf.Min(1f, shapeBudget * (1f - settings.ridgeShare) /
                (Mathf.Max(plan.HillHeights.y, plan.BasinDepths.y) * size));
            float elevation = landform * shapeScale + macroBudget * macro + hillBudget * (.20f + .80f * region.x) * hill;
            if (settings.ridgeShare <= 0f) return elevation;
            float mountain = Smooth(.45f, .85f, region.x);
            float ridge = 1f - Mathf.Abs(2f * Noise(sample * (plan.radius / scale) + plan.macroOffset, plan.ridgeSeed) - 1f);
            return elevation + shapeBudget * settings.ridgeShare * mountain * ridge * ridge;
        }
        public static float BaseHeight(Plan plan,Vector3 direction)
        {
            if (plan.natural != null) return FreeHeight(plan, direction);
            if (plan.fixedPlacement && plan.requiredKind == 2)
                return FixedCourseHeight(plan, direction);

            float protectedDistance = Mathf.Min(MainRouteDistance(plan, direction), ConnectorDistance(plan, direction));
            float shoulder = 1.8f + .4f * Mathf.Sin(Vector3.Dot(direction, new Vector3(2.7f, 3.1f, 1.9f)) + plan.noiseOffset.x);
            float freeHeight = FreeHeight(plan, direction);
            float widerShoulder = shoulder + .9f * Smooth(.25f, 1.25f, Mathf.Abs(freeHeight));
            float routeFade = Smooth(ProtectedHalfWidth, ProtectedHalfWidth + widerShoulder, protectedDistance);
            float free = freeHeight * routeFade * Smooth(1.6f, 3f, Arc(direction, plan.spawn, plan.radius));
            float influence = 0f, courseHeight = 0f;
            foreach (var feature in plan.features)
            {
                if (Vector3.Dot(feature.centre, direction) <= 0f) continue;
                Vector2 local = feature.Coordinates(direction, plan.radius);
                if (local.sqrMagnitude > feature.reservedRadius * feature.reservedRadius) continue;
                float core = CourseCoreDistance(feature.kind, local);
                float weight = CourseInfluence(plan, feature, local, core, widerShoulder);
                if (weight <= influence) continue;

                // Blend the shoulders into the existing height field. The reviewed wall
                // stays sharp along the crossing and softens near the ends of the valley.
                float height = TraversalTestCourseGeometry.Height(feature.kind, local.x, local.y);
                if (feature.kind == 0)
                {
                    float outerWall = Smooth(0f, 1.25f, core) * Smooth(1.2f, 4.8f, Mathf.Abs(local.y));
                    float wallDistance = Mathf.Abs(local.x);
                    float rounded = -TraversalTestCourseGeometry.ValleyDepth *
                        (1f - Smooth(1.35f, 2.6f, wallDistance + .45f * outerWall)) *
                        Mathf.Clamp01((5f - Mathf.Abs(local.y)) / 4f);
                    height = Mathf.Lerp(height, rounded, outerWall);
                }
                courseHeight = height;
                influence = weight;
            }
            return Mathf.Lerp(free, courseHeight, influence);
        }

        static float FixedCourseHeight(Plan plan, Vector3 direction)
        {
            // The glide approach is sensitive to its long, steep bank.
            // Use the previously verified outer envelope for this fallback.
            var feature = plan.features[0];
            if (Vector3.Dot(feature.centre, direction) <= 0f) return 0f;
            Vector2 point = feature.Coordinates(direction, plan.radius);
            float distance = Mathf.Max(Mathf.Max(-10f - point.x, point.x - 11f), Mathf.Abs(point.y) - 8.5f);
            return TraversalTestCourseGeometry.Height(feature.kind, point.x, point.y) * (1f - Smooth(0f, 1.5f, distance));
        }

        public static float CourseCoreDistance(int kind, Vector2 point)
        {
            Vector2 start = TraversalTestCourseGeometry.Start(kind);
            Vector2 goal = TraversalTestCourseGeometry.Goal(kind);
            float side = kind == 0 ? 5.7f : kind == 1 ? 6.5f : 7.7f;
            float left = kind == 2 ? -4.65f : start.x - .75f;
            float right = kind == 1 ? 9.35f : kind == 2 ? 8.25f : goal.x + .75f;
            float distance = new Vector2(Mathf.Max(0f, Mathf.Max(left - point.x, point.x - right)),
                Mathf.Max(0f, Mathf.Abs(point.y) - 1.18f)).magnitude;
            var farStart = new Vector2(start.x, side);
            var farGoal = new Vector2(goal.x, side);
            distance = Mathf.Min(distance, SegmentDistance(point, start, farStart) - .85f);
            distance = Mathf.Min(distance, SegmentDistance(point, farStart, farGoal) - .85f);
            distance = Mathf.Min(distance, SegmentDistance(point, farGoal, goal) - .85f);
            if (kind != 1)
                distance = Mathf.Min(distance, SegmentDistance(point, Vector2.zero, new Vector2(0f, side)) - .72f);
            return Mathf.Max(0f, distance);
        }

        static float SegmentDistance(Vector2 point, Vector2 from, Vector2 to)
        {
            Vector2 edge = to - from;
            return Vector2.Distance(point, from + edge * Mathf.Clamp01(Vector2.Dot(point - from, edge) / edge.sqrMagnitude));
        }

        static float CourseInfluence(Plan plan, Feature feature, Vector2 point, float core, float shoulder)
        {
            if (core <= 0f) return 1f;
            float extent = feature.reservedRadius;
            float edge = Smooth(0f, .85f, extent - point.magnitude);
            float width = Mathf.Clamp(plan.appearance.transitionMeters + (shoulder - 1.8f) * .65f, 1.5f, 3f);
            return (1f - Smooth(0f, width, core)) * edge;
        }
        static float Hash(int x,int y,int z,int seed)
        {unchecked{uint h=(uint)seed^(uint)x*0x9e3779b9u^(uint)y*0x85ebca6bu^(uint)z*0xc2b2ae35u;h^=h>>16;h*=0x7feb352du;h^=h>>15;h*=0x846ca68bu;h^=h>>16;return (h&0xffffff)/16777215f;}}
        public static float Noise(Vector3 p,int seed)
        {
            int x=Mathf.FloorToInt(p.x),y=Mathf.FloorToInt(p.y),z=Mathf.FloorToInt(p.z);
            float a=Smooth(0,1,p.x-x),b=Smooth(0,1,p.y-y),c=Smooth(0,1,p.z-z);
            return Mathf.Lerp(Mathf.Lerp(Mathf.Lerp(Hash(x,y,z,seed),Hash(x+1,y,z,seed),a),Mathf.Lerp(Hash(x,y+1,z,seed),Hash(x+1,y+1,z,seed),a),b),
                Mathf.Lerp(Mathf.Lerp(Hash(x,y,z+1,seed),Hash(x+1,y,z+1,seed),a),Mathf.Lerp(Hash(x,y+1,z+1,seed),Hash(x+1,y+1,z+1,seed),a),b),c);
        }
        public static List<Vector3> MainRoute(Plan plan)
        {
            var result=new List<Vector3>();int steps=Mathf.CeilToInt(Mathf.PI*plan.radius/1.5f);
            for(int i=0;i<=steps;i++){float angle=Mathf.PI*i/steps;result.Add(plan.spawn*Mathf.Cos(angle)+plan.routeTangent*Mathf.Sin(angle));}return result;
        }
        public static List<Vector3> FeatureRoute(Plan plan,Feature feature,bool escape=false)
        {
            var result=new List<Vector3>();
            var points=escape?new[]{Vector2.zero,new Vector2(0,feature.kind==2?7.7f:5.7f)}:TraversalTestCourseGeometry.BasicRoute(feature.kind);
            foreach(var p in points)result.Add(feature.Direction(p,plan.radius));return result;
        }
        public bool AllowsSpawn(Vector3 point,Vector3 normal)
        {
            if (plan.natural != null) return NaturalPlacement != null && NaturalPlacement.AllowsBaobab(point, normal);
            var direction=transform.InverseTransformPoint(point).normalized;
            if(Vector3.Angle(normal,transform.TransformDirection(direction))>30 || MainRouteDistance(plan,direction)<ProtectedHalfWidth+.5f
                ||ConnectorDistance(plan,direction)<ProtectedHalfWidth+.5f
                ||Arc(direction,plan.spawn,plan.radius)<2.2f)return false;
            foreach(var feature in plan.features)if(Arc(feature.centre,direction,plan.radius)<feature.reservedRadius+1f)return false;
            foreach (var form in plan.landforms)
                if (form.Weight(direction, plan.radius) > 0f && form.approach.Distance(direction, plan.radius) > .35f)
                    return false;
            if(TerrainWater.Sample(plan,direction,out float distance,out _,out _)&&distance<plan.river.width*.5f+1f)return false;
            return true;
        }

        internal void PrepareNatural(int seed, float radius, int chapter, PlanetGenerationSettings settings,
            MeshFilter filter, MeshCollider collider)
        {
            if (OwnedMesh) throw new InvalidOperationException("Terrain is immutable after preparation.");
            var natural = (settings.naturalTerrain ?? new NaturalTerrainSettings()).Snapshot(radius);
            plan = new Plan
            {
                seed = seed, radius = radius, chapter = chapter,
                profileId = settings.profileId, natural = natural,
                appearance = (settings.terrainAppearance ?? new TerrainAppearanceSettings()).Snapshot(),
                amplitude = natural.broadAmplitudeMeters + natural.mediumAmplitudeMeters,
                macroSeed = StarSystemFactory.DeriveSeed(seed, 81, 0),
                hillSeed = StarSystemFactory.DeriveSeed(seed, 82, 0),
                riverSeed = StarSystemFactory.DeriveSeed(seed, 83, 0),
                placementSeed = StarSystemFactory.DeriveSeed(seed, 84, 0),
                surfaceSeed = StarSystemFactory.DeriveSeed(seed, 85, 0)
            };
            plan.macroOffset = NoiseOffset(plan.macroSeed);
            plan.hillOffset = NoiseOffset(plan.hillSeed);
            plan.noiseOffset = NoiseOffset(plan.surfaceSeed);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            NaturalTerrainRiver.Plan(plan);
            plan.riverPlanMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            AssignMesh(filter, collider);
            if (plan.river != null) CreateWater(settings.proceduralWaterMaterial);
            plan.meshMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
            plan.buildAttempts = 1;
        }

        public void Prepare(int seed,float radius,Vector3 spawn,int budget,MeshFilter filter,MeshCollider collider,
            CapsuleCollider player,int chapter=1,int frequency=2,Vector3? weights=null,int requiredKind=-1,
            TerrainAppearanceSettings appearance=null, Material waterTemplate=null, bool useNormalPlanetRelief=false)
        {
            if(OwnedMesh)throw new InvalidOperationException("Terrain is immutable after preparation.");
            plan=CreatePlan(seed,radius,spawn,budget,chapter,frequency,weights,requiredKind,appearance:appearance,
                useNormalPlanetRelief:useNormalPlanetRelief);
            var watch=System.Diagnostics.Stopwatch.StartNew();
            for(int build=0;build<MaxBuildAttempts;build++)
            {
                plan.buildAttempts = build + 1;
                AssignMesh(filter, collider);
                var verdict=TerrainTraversalVerifier.Verify(collider,player,plan);
                plan.maximumWalkSlope=verdict.maximumSlope;
                if(verdict.passed)
                {
                    plan.mainRouteSeconds=verdict.mainRouteSeconds;plan.traversalCosts=verdict.costs;
                    if(plan.river!=null)CreateWater(waterTemplate);
                    watch.Stop();plan.validationMilliseconds=(float)watch.Elapsed.TotalMilliseconds;return;
                }
                plan.repairs.Add(verdict.reason);
                filter.sharedMesh=null;collider.sharedMesh=null;DestroyImmediate(OwnedMesh);OwnedMesh=null;
                if (build + 2 < MaxBuildAttempts && verdict.landform >= 0)
                {
                    var form = plan.landforms[verdict.landform];
                    if (!form.reduced)
                    {
                        form.height *= .60f;
                        form.reduced = true;
                        plan.repairs.Add($"Reduced landform {verdict.landform} to {form.height:F2}m.");
                    }
                    else
                    {
                        plan.landforms.RemoveAt(verdict.landform);
                        plan.repairs.Add($"Removed inaccessible landform {verdict.landform}.");
                    }
                    continue;
                }
                if (requiredKind >= 0)
                {
                    if (build + 1 == MaxBuildAttempts) break;
                    bool lastCandidate = build + 2 == MaxBuildAttempts;
                    if (!lastCandidate && verdict.feature > 0)
                    {
                        plan.features.RemoveAt(verdict.feature);
                    }
                    else if (!lastCandidate && plan.river != null)
                    {
                        plan.repairs.Add("River removed before moving the reviewed course.");
                        plan.river = null;
                    }
                    else
                    {
                        var prior = plan;
                        plan = CreatePlan(seed, radius, spawn, budget, chapter, frequency, weights, requiredKind, build + 1, prior.appearance);
                        plan.repairs.InsertRange(0, prior.repairs);
                        if (plan.fixedPlacement) plan.repairs.Add("Using the fixed reviewed course and its protected connectors.");
                    }
                }
                else if (verdict.landform >= 0)
                {
                    plan.landforms.Clear();
                    plan.repairs.Add("Removed optional landforms to finish within the preparation budget.");
                }
                else if(verdict.feature>=0 && verdict.feature<plan.features.Count)plan.features.RemoveAt(verdict.feature);
                else if(plan.river!=null)plan.river=null;
                else {plan.features.Clear();plan.landforms.Clear();plan.patches.Clear();plan.regions.Clear();plan.amplitude=0;plan.safeFallback=true;}
            }
            throw new InvalidOperationException("Terrain walk verification failed after bounded repairs: " +
                string.Join(" | ", plan.repairs));
        }

        void AssignMesh(MeshFilter filter, MeshCollider collider)
        {
            OwnedMesh = BuildMesh(plan);
            filter.sharedMesh = OwnedMesh;
            collider.sharedMesh = null;
            if (OwnedMesh.GetIndexCount(0) / 3 > FastMidphaseTriangleLimit)
                collider.cookingOptions &= ~MeshColliderCookingOptions.UseFastMidphase;
            collider.sharedMesh = OwnedMesh;
            Physics.SyncTransforms();
        }

        static Mesh BuildMesh(Plan plan)
        {
            if (plan.requiredKind >= 0) return BuildReviewedMesh(plan);
            float spacing = plan.natural != null && plan.river != null ? Mathf.Min(.18f, plan.river.width * .3f) : .18f;
            // Normalized cube: the face centre is the coarsest spacing (2R/frequency).
            // Equal-angle cube: tan() spreads samples so the face centre spacing is
            // R*PI/(2*frequency) and corners are no longer oversampled.
            bool equalAngle = plan.natural != null && plan.natural.equalAngleMesh;
            int frequency = equalAngle ? Mathf.CeilToInt(Mathf.PI * plan.radius / (2f * spacing))
                : Mathf.CeilToInt(2 * plan.radius / spacing);
            float angleStep = Mathf.PI * .25f / frequency;
            // Pass 1: topology only. Face-edge vertices are shared through their integer cube key.
            int expected = 6 * frequency * frequency + 2;
            var directions = new List<Vector3>(expected);
            var indices = new int[6 * frequency * frequency * 6];
            int written = 0;
            // Pack the cube key into one integer: unique (and collision-free as a hash) while
            // (2f+1)^3 fits in 32 bits. Vector3Int's own hash collides often on this lattice.
            long span = 2L * frequency + 1;
            var shared=new Dictionary<long,int>(expected);
            var ns=new[]{Vector3Int.right,Vector3Int.left,Vector3Int.up,Vector3Int.down,new Vector3Int(0,0,1),new Vector3Int(0,0,-1)};
            var rs=new[]{new Vector3Int(0,0,-1),new Vector3Int(0,0,1),Vector3Int.right,Vector3Int.right,Vector3Int.right,Vector3Int.left};
            var ups=new[]{Vector3Int.up,Vector3Int.up,new Vector3Int(0,0,-1),new Vector3Int(0,0,1),Vector3Int.up,Vector3Int.up};
            var grid=new int[(frequency+1)*(frequency+1)];
            for(int face=0;face<6;face++)
            {
                for(int j=0;j<=frequency;j++)for(int i=0;i<=frequency;i++)
                {
                    var key=ns[face]*frequency+rs[face]*(2*i-frequency)+ups[face]*(2*j-frequency);
                    long packed = ((key.x + frequency) * span + (key.y + frequency)) * span + (key.z + frequency);
                    if(!shared.TryGetValue(packed,out int id))
                    {
                        id=directions.Count;shared.Add(packed,id);
                        directions.Add(equalAngle
                            ? new Vector3(Mathf.Tan(key.x * angleStep), Mathf.Tan(key.y * angleStep), Mathf.Tan(key.z * angleStep)).normalized
                            : ((Vector3)key).normalized);
                    }
                    grid[j*(frequency+1)+i]=id;
                }
                for(int j=0;j<frequency;j++)for(int i=0;i<frequency;i++)
                {
                    int a=grid[j*(frequency+1)+i],b=grid[j*(frequency+1)+i+1],c=grid[(j+1)*(frequency+1)+i],d=grid[(j+1)*(frequency+1)+i+1];
                    indices[written++]=a;indices[written++]=b;indices[written++]=d;indices[written++]=a;indices[written++]=d;indices[written++]=c;
                }
            }

            // Pass 2: heights and surface weights. The natural profile is a pure function of the
            // read-only plan, so it runs in parallel with bit-identical results per vertex.
            var vertices = new Vector3[directions.Count];
            var colors = new Color[directions.Count];
            if (plan.natural != null)
                ForEachRange(directions.Count, (from, to) =>
                {
                    for (int v = from; v < to; v++)
                    {
                        var n = directions[v];
                        float elevation = FreeHeight(plan, n);
                        vertices[v] = n * (plan.radius + TerrainWater.Height(plan, n, elevation));
                        colors[v] = NaturalSurfaceWeights(plan, n, elevation);
                    }
                });
            else
                for (int v = 0; v < directions.Count; v++)
                {
                    var n = directions[v];
                    vertices[v] = n * (plan.radius + Height(plan, n));
                    colors[v] = SurfaceWeights(plan, n);
                }
            var mesh=new Mesh{name="Seeded terrain "+plan.seed,indexFormat=IndexFormat.UInt32};
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices,0);
            mesh.RecalculateNormals();
            return mesh;
        }

        /// <summary>Runs pure, read-only work over [0, count) in fixed ranges; sequential where threads are unavailable.</summary>
        internal static void ForEachRange(int count, Action<int, int> body)
        {
            const int RangeSize = 4096;
#if UNITY_WEBGL && !UNITY_EDITOR
            body(0, count);
#else
            int ranges = (count + RangeSize - 1) / RangeSize;
            if (ranges <= 1) { body(0, count); return; }
            System.Threading.Tasks.Parallel.For(0, ranges, r => body(r * RangeSize, Mathf.Min(count, (r + 1) * RangeSize)));
#endif
        }
        static Mesh BuildReviewedMesh(Plan plan)
        {
            var course = plan.features[0];
            float half = Mathf.PI * plan.radius;
            var longitudes = SampleAxis(-half, half, TraversalTestCourseGeometry.LongitudinalCuts, false);
            var latitudes = SampleAxis(-half / 2, half / 2, TraversalTestCourseGeometry.LateralCuts, true);
            int columns = longitudes.Count, rows = latitudes.Count;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var indices = new List<int>();
            var grid = new int[rows, columns];
            for (int j = 0; j < rows; j++)
            {
                bool pole = j == 0 || j == rows - 1;
                for (int i = 0; i < columns; i++)
                {
                    if (pole && i > 0)
                    {
                        grid[j, i] = grid[j, 0];
                        continue;
                    }
                    Vector3 direction = pole
                        ? (j == 0 ? -course.north : course.north)
                        : course.Direction(new Vector2(longitudes[i], latitudes[j]), plan.radius);
                    grid[j, i] = vertices.Count;
                    vertices.Add(direction * (plan.radius + Height(plan, direction)));
                    colors.Add(SurfaceWeights(plan, direction));
                }
            }
            for (int j = 0; j < rows - 1; j++)
            for (int i = 0; i < columns; i++)
            {
                int next = (i + 1) % columns;
                int a = grid[j, i], b = grid[j, next], c = grid[j + 1, i], d = grid[j + 1, next];
                if (a != b) { indices.Add(a); indices.Add(b); indices.Add(d); }
                if (c != d) { indices.Add(a); indices.Add(d); indices.Add(c); }
            }
            var mesh = new Mesh { name = "Reviewed terrain " + plan.seed, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        static Color SurfaceWeights(Plan plan, Vector3 direction)
        {
            Vector2 region = TerrainRegions.BlendWeights(plan, direction);
            float bank = 0f;
            if (TerrainWater.Sample(plan, direction, out float distance, out _, out _))
                bank = 1f - Smooth(plan.river.width * .5f, plan.river.width * .5f + 1.2f, distance);
            TerrainLandforms.Height(plan, direction, out float basin);
            float tone = Noise(direction * 1.55f + plan.noiseOffset, plan.surfaceSeed);
            return new Color(region.x, Mathf.Max(region.y, basin), bank, tone);
        }

        static Color NaturalSurfaceWeights(Plan plan, Vector3 direction, float elevation)
        {
            float high = elevation / Mathf.Max(.001f, plan.amplitude);
            float bank = 0f;
            if (TerrainWater.Sample(plan, direction, out float distance, out _, out _))
                bank = 1f - Smooth(plan.river.width * .5f, plan.river.width * .5f + plan.river.bankWidth, distance);
            float tone = Noise(direction * (plan.radius / plan.natural.mediumScaleMeters) + plan.noiseOffset, plan.surfaceSeed);
            return new Color(Smooth(-.3f, .45f, high), Smooth(-.25f, .45f, -high), bank, tone);
        }

        static List<float> SampleAxis(float start, float end, float[] cuts, bool includeEnd)
        {
            var positions = new SortedSet<float> { start };
            if (includeEnd) positions.Add(end);
            foreach (float cut in cuts)
                if (cut > start && cut < end) positions.Add(cut);
            int segments = Mathf.CeilToInt((end - start) / .18f);
            for (int i = 1; i < segments; i++)
            {
                float sample = Mathf.Lerp(start, end, i / (float)segments);
                if (!Array.Exists(cuts, cut => Mathf.Abs(cut - sample) < .002f)) positions.Add(sample);
            }
            return new List<float>(positions);
        }

        void CreateWater(Material template)
        {
            WaterMesh = TerrainWater.CreateMesh(plan);
            waterMaterial = template ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            waterMaterial.name = "Owned shallow water";
            waterMaterial.color = new Color(.13f,.53f,.7f);
            waterMaterial.SetFloat("_Smoothness", .36f);
            var go=new GameObject("Shallow connected river");go.transform.SetParent(transform,false);go.AddComponent<MeshFilter>().sharedMesh=WaterMesh;go.AddComponent<MeshRenderer>().sharedMaterial=waterMaterial;
        }
        void OnDestroy()
        {foreach(var owned in new UnityEngine.Object[]{OwnedMesh,WaterMesh,waterMaterial})if(owned){if(Application.isPlaying)Destroy(owned);else DestroyImmediate(owned);}}
        void OnDrawGizmosSelected()
        {
            if(plan==null || plan.natural != null)return;Gizmos.color=new Color(.35f,.95f,.65f);Draw(MainRoute(plan));
            foreach (var connector in plan.connectors) Draw(new List<Vector3> { connector.anchor, connector.pad });
            foreach (var feature in plan.features)
            {
                Draw(FeatureRoute(plan, feature));
                Gizmos.color = new Color(1, .75f, .3f);
                Draw(new List<Vector3> { feature.Direction(TraversalTestCourseGeometry.Start(feature.kind), plan.radius),
                    feature.Direction(TraversalTestCourseGeometry.Goal(feature.kind), plan.radius) });
                DrawCourseMask(feature);
                Gizmos.color = new Color(.35f, .95f, .65f);
            }
        }

        void DrawCourseMask(Feature feature)
        {
            for (float y = -feature.reservedRadius; y <= feature.reservedRadius; y += .6f)
            for (float x = -feature.reservedRadius; x <= feature.reservedRadius; x += .6f)
            {
                var point = new Vector2(x, y);
                if (point.sqrMagnitude > feature.reservedRadius * feature.reservedRadius) continue;
                var direction = feature.Direction(point, plan.radius);
                float core = CourseCoreDistance(feature.kind, point);
                float influence = CourseInfluence(plan, feature, point, core, 1.8f);
                if (influence <= .05f) continue;
                Gizmos.color = core == 0f ? new Color(1, .72f, .3f) : new Color(.35f, .65f, .95f, influence);
                Gizmos.DrawSphere(transform.TransformPoint(direction * (plan.radius + Height(plan, direction) + .06f)), .045f);
            }
        }
        void Draw(List<Vector3> points)
        {
            for(int i=1;i<points.Count;i++)
            {
                var previous=points[i-1];int steps=Mathf.Max(1,Mathf.CeilToInt(Arc(previous,points[i],plan.radius)/.25f));
                for(int j=1;j<=steps;j++)
                {
                    var next=Vector3.Slerp(points[i-1],points[i],(float)j/steps).normalized;
                    Gizmos.DrawLine(transform.TransformPoint(previous*(plan.radius+Height(plan,previous)+.08f)),transform.TransformPoint(next*(plan.radius+Height(plan,next)+.08f)));previous=next;
                }
            }
        }
    }
}
