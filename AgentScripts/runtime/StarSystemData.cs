using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace B612
{
    public enum PlanetSizeClass { Small, Medium, Large }
    public enum PlanetRole { Start, Normal, Special, Destination }
    public enum PlanetPhase { Unvisited, Surge, Clearing, Harvest, Quest, ReadyToLeave, Departed, GameOver }
    public enum SurgeResult { NotStarted, InProgress, Resolved, LeftWithRemaining, Failed }
    public enum RunMode { PlayingPlanet, SelectingRoute, Travelling, GameOver }
    [Serializable] public sealed class TerrainPlan
    { public string profileId,groundColor; public float sizeScale,radius,gravityStrength; }
    [Serializable] public sealed class BaobabPlan
    {
        public string profileId="baobab.prototype.v1";
        public float durationSeconds=120f,minimumSpacing=1.4f,threatCapacity=100f;
        public int spawnEventCount=40,spawnBatchSize=1,maxActive=6;
        public BaobabTypePlan[] typePool={new BaobabTypePlan{typeId="basic",weight=1,maxActive=6}};
    }
    [Serializable] public sealed class BaobabTypePlan { public string typeId; public float weight; public int maxActive; }
    [Serializable] public sealed class CropPlan { public string cropTypeId; public int count; }
    [Serializable] public sealed class CropAmount { public string cropTypeId; public int amount; }
    [Serializable] public sealed class WoolAmount { public string woolTypeId; public int amount; }
    [Serializable] public sealed class RewardOption { public string optionId,rewardKind,definitionId; }
    [Serializable] public sealed class NpcPlan
    { public string npcDefinitionId,encounterKind,contentId; public WoolAmount[] requiredWool=Array.Empty<WoolAmount>(); public RewardOption[] rewardOptions=Array.Empty<RewardOption>(); }
    [Serializable] public sealed class QuestProgress { public string status="NotStarted",chosenRewardOptionId; }
    [Serializable] public sealed class PlanetSpec
    {
        public string planetId; public int seed,generatorVersion=PlanetGenerationSettings.GeneratorVersion,chapter=1; public PlanetRole role; public PlanetSizeClass sizeClass;
        public TraversalCapability requiredCapabilities,unlock;
        public TerrainPlan terrain=new TerrainPlan(); public BaobabPlan baobab=new BaobabPlan();
        public CropPlan[] crops=Array.Empty<CropPlan>(); public NpcPlan npc;
    }
    [Serializable] public sealed class PlanetMapPlacement { public int depth; public Vector2 position01; }
    [Serializable] public sealed class SurgeProgress
    { public float elapsedSpawnSeconds; public int processedSpawnEvents; public SurgeResult result; public bool skippedByDebug; }
    [Serializable] public sealed class PlanetState
    { public PlanetPhase phase; public bool worldInitialized; public float simulationSeconds; public SurgeProgress surge=new SurgeProgress(); public CropAmount[] harvestedCrops=Array.Empty<CropAmount>(); public QuestProgress quest=new QuestProgress(); public int visitedSites;public float[] siteVisitSeconds=new float[3],siteTravelMetres=new float[3]; }
    [Serializable] public sealed class PlanetData
    { public int modelVersion=1; public PlanetSpec spec=new PlanetSpec(); public PlanetMapPlacement map=new PlanetMapPlacement(); public PlanetState state=new PlanetState(); }
    [Serializable] public sealed class PlanetLink { public string fromPlanetId,toPlanetId; }
    [Serializable] public sealed class StarSystemData
    {
        public string systemId,startPlanetId,destinationPlanetId; public int systemIndex=1,seed;
        public string exitRuleId="prototype.noExit.v1";
        public List<PlanetData> planets=new List<PlanetData>(); public List<PlanetLink> links=new List<PlanetLink>();
        public PlanetData Find(string id)=>planets.Find(p=>p.spec.planetId==id);
        public bool HasLink(string from,string to)=>links.Any(l=>l.fromPlanetId==from&&l.toPlanetId==to);
    }
    [Serializable] public sealed class VisitedPlanet { public string systemId,planetId; }
    [Serializable] public sealed class RunProgressData
    {
        public string currentSystemId,currentPlanetId,pendingDestinationId;
        public int chapter=1,journeySeed;public TraversalCapability unlocked;public bool journeyComplete;
        public List<VisitedPlanet> chosenPath=new List<VisitedPlanet>(); public RunMode mode;
    }
    // View-only preview. Future content may add display fields without owning generation rules here.
    public sealed class PlanetMapPreview
    {
        public string planetId,sizeLabel;
        public static PlanetMapPreview From(PlanetData planet)=>new PlanetMapPreview{planetId=planet.spec.planetId,
            sizeLabel=planet.spec.sizeClass==PlanetSizeClass.Small?"소":planet.spec.sizeClass==PlanetSizeClass.Medium?"중":"대"};
    }
    public static class StarSystemFactory
    {
        public static int DeriveSeed(int seed,int stream,int node)
        { unchecked { uint value=(uint)seed^((uint)stream*0x9e3779b9u)^((uint)(node+1)*0x85ebca6bu); value^=value>>16;value*=0x7feb352du;value^=value>>15;return (int)(value&0x7fffffffu); } }
        public static StarSystemData Create(int seed,PlanetGenerationSettings settings,int chapter=1)
        {
            if(!settings||settings.palette==null||settings.palette.Length==0)throw new InvalidOperationException("Planet generation palette is missing.");
            if(chapter<1||chapter>3)throw new ArgumentOutOfRangeException(nameof(chapter));
            if(settings.chapterCampaign)
            {
                if(settings.chapters==null||settings.chapters.Length!=3)throw new InvalidOperationException("Campaign needs three chapter definitions.");
                TraversalCapability available=TraversalCapability.None;
                for(int c=1;c<=3;c++){var definition=settings.Chapter(c);if(!definition||definition.chapter!=c||(available&definition.guaranteedEntry)!=definition.guaranteedEntry)throw new InvalidOperationException("Invalid chapter entry/unlock chain.");available|=definition.unlock;}
                if(available!=TraversalCapability.All)throw new InvalidOperationException("Campaign must unlock all traversal abilities.");
            }
            string system="system_"+chapter.ToString("00");
            var data=new StarSystemData{systemId=system,systemIndex=chapter,seed=seed,startPlanetId=system+"_Start",destinationPlanetId=system+"_Destination"};
            string[] names={"Start","B","C","D","E","Destination"};int[] depth={0,1,1,2,2,3};
            Vector2[] positions={new Vector2(.1f,.5f),new Vector2(.35f,.76f),new Vector2(.35f,.24f),new Vector2(.62f,.76f),new Vector2(.62f,.3f),new Vector2(.9f,.5f)};
            for(int i=0;i<names.Length;i++)
            {
                var appearance=new System.Random(DeriveSeed(seed,1,i));float scale=i==0?1f:Mathf.Lerp(.7f,2f,(float)appearance.NextDouble());
                var planet=new PlanetData();planet.spec.planetId=system+"_"+names[i];planet.spec.seed=DeriveSeed(seed,2,i);planet.spec.chapter=chapter;
                if(settings.chapterCampaign)
                {
                    var definition=settings.Chapter(chapter);var entry=definition?definition.guaranteedEntry:ChapterTerrainDefinition.Entry(chapter);var unlock=definition?definition.unlock:ChapterTerrainDefinition.Reward(chapter);
                    planet.spec.requiredCapabilities=i==0?entry:entry|unlock;planet.spec.unlock=i==0?unlock:TraversalCapability.None;
                }
                planet.spec.role=i==0?PlanetRole.Start:i==5?PlanetRole.Destination:PlanetRole.Normal;planet.spec.sizeClass=PlanetGenerationSettings.Classify(scale);
                planet.spec.terrain=new TerrainPlan{profileId=settings.profileId,sizeScale=scale,radius=settings.nominalRadius*scale,gravityStrength=settings.gravityStrength,
                    groundColor=i==0?"#"+ColorUtility.ToHtmlStringRGB(settings.baselineColor):settings.palette[appearance.Next(settings.palette.Length)]};
                planet.map.depth=depth[i];planet.map.position01=positions[i];data.planets.Add(planet);
            }
            int[,] edges={{0,1},{0,2},{1,3},{1,4},{2,4},{3,5},{4,5}};
            for(int i=0;i<edges.GetLength(0);i++)data.links.Add(new PlanetLink{fromPlanetId=data.planets[edges[i,0]].spec.planetId,toPlanetId=data.planets[edges[i,1]].spec.planetId});
            Validate(data,settings);return data;
        }
        public static void ValidatePlanet(PlanetData planet,PlanetGenerationSettings settings)
        {
            if(planet==null||planet.spec==null||planet.spec.terrain==null||planet.map==null||planet.state==null||string.IsNullOrEmpty(planet.spec.planetId))throw new InvalidOperationException("Incomplete planet data.");
            var terrain=planet.spec.terrain;
            if(planet.spec.chapter<1||planet.spec.chapter>3)throw new InvalidOperationException("Unknown chapter.");
            if(terrain.profileId!=settings.profileId||planet.spec.generatorVersion!=PlanetGenerationSettings.GeneratorVersion||planet.modelVersion!=1)throw new InvalidOperationException("Unknown planet template/version.");
            PlanetSurfaceGrid.CalculateFrequency(settings.subdivisions,terrain.sizeScale);
            if(PlanetGenerationSettings.Classify(terrain.sizeScale)!=planet.spec.sizeClass||!Mathf.Approximately(terrain.radius,settings.nominalRadius*terrain.sizeScale))throw new InvalidOperationException("Planet size/radius/class mismatch.");
            if(terrain.groundColor==null||terrain.groundColor.Length!=7||terrain.groundColor[0]!='#'||!ColorUtility.TryParseHtmlString(terrain.groundColor,out _))throw new InvalidOperationException("Expected six-digit ground color.");
            var plan=planet.spec.baobab;
            if(float.IsNaN(terrain.gravityStrength)||float.IsInfinity(terrain.gravityStrength)||terrain.gravityStrength<=0||plan==null||plan.durationSeconds<1||plan.spawnEventCount<1||plan.spawnBatchSize!=1||plan.maxActive<1||plan.minimumSpacing<=0||plan.threatCapacity<=0)throw new InvalidOperationException("Invalid planet gameplay plan.");
        }
        public static void Validate(StarSystemData data,PlanetGenerationSettings settings)
        {
            if(data==null||data.planets==null||data.links==null||data.planets.Count<2)throw new InvalidOperationException("Incomplete star system.");
            var ids=new HashSet<string>();foreach(var planet in data.planets){ValidatePlanet(planet,settings);if(!ids.Add(planet.spec.planetId))throw new InvalidOperationException("Duplicate planet ID.");}
            var start=data.Find(data.startPlanetId);var end=data.Find(data.destinationPlanetId);
            if(start==null||end==null||start.spec.role!=PlanetRole.Start||end.spec.role!=PlanetRole.Destination||data.planets.Count(p=>p.spec.role==PlanetRole.Start)!=1||data.planets.Count(p=>p.spec.role==PlanetRole.Destination)!=1)throw new InvalidOperationException("Invalid start/destination.");
            var edges=new HashSet<string>();foreach(var link in data.links)
            {
                var from=data.Find(link.fromPlanetId);var to=data.Find(link.toPlanetId);
                if(from==null||to==null||from.map.depth>=to.map.depth||!edges.Add(link.fromPlanetId+"/"+link.toPlanetId))throw new InvalidOperationException("Missing, duplicate, cyclic or backward link.");
            }
            var reachable=new HashSet<string>{data.startPlanetId};foreach(var p in data.planets.OrderBy(p=>p.map.depth))if(reachable.Contains(p.spec.planetId))foreach(var link in data.links.Where(l=>l.fromPlanetId==p.spec.planetId))reachable.Add(link.toPlanetId);
            var leadsToEnd=new HashSet<string>{data.destinationPlanetId};foreach(var p in data.planets.OrderByDescending(p=>p.map.depth))if(leadsToEnd.Contains(p.spec.planetId))foreach(var link in data.links.Where(l=>l.toPlanetId==p.spec.planetId))leadsToEnd.Add(link.fromPlanetId);
            if(reachable.Count!=data.planets.Count||leadsToEnd.Count!=data.planets.Count||data.links.Any(l=>l.fromPlanetId==data.destinationPlanetId))throw new InvalidOperationException("Unreachable or dead-end planet.");
        }
    }
}
