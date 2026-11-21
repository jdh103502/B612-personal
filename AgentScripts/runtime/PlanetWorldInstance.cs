using System;
using UnityEngine;

namespace B612
{
    public sealed class PlanetWorldInstance : MonoBehaviour
    {
        public PlanetSurfaceGrid surface;
        public GravitySource gravity;
        public Transform spawn;
        public Collider ground;
        public Renderer groundRenderer;
        public PlanetData Data { get; private set; }
        public ProceduralPlanetTerrain Terrain { get; private set; }
        Material[] ownedMaterials;
        public void Prepare(PlanetData data,PlanetGenerationSettings settings,CapsuleCollider playerCapsule)
        {
            if(Data!=null)throw new InvalidOperationException("A prepared world is immutable; prepare a fresh world instance.");
            StarSystemFactory.ValidatePlanet(data,settings);
            if(!ground||!groundRenderer||!surface||!gravity||!spawn||
                (!settings.proceduralTerrain && (settings.originalMaterials==null||settings.originalMaterials.Length==0)))
                throw new InvalidOperationException("Incomplete planet world template.");
            if(settings.spawnDirection.sqrMagnitude<.01f||!Finite(settings.spawnDirection))throw new InvalidOperationException("Invalid projected spawn direction.");
            Data=data;gravity.enabled=false;
            surface.ground = ground;
            transform.localScale=Vector3.one;ground.transform.localScale=Vector3.Scale(settings.baselineScale,Vector3.one*data.spec.terrain.sizeScale);
            ColorUtility.TryParseHtmlString(data.spec.terrain.groundColor,out var color);
            if(settings.proceduralTerrain)
            {
                var template = settings.proceduralGroundMaterial;
                var shader = template ? template.shader : Shader.Find("B612/Planet Ground");
                if (!shader || !shader.isSupported) throw new InvalidOperationException("Procedural ground shader is unavailable.");
                var material = template ? new Material(template) : new Material(shader);
                ownedMaterials = new[] { material };
                material.name = data.spec.planetId + " Ground";
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Radius", data.spec.terrain.radius);
                if (!template && settings.originalMaterials != null && settings.originalMaterials.Length > 0 &&
                    settings.originalMaterials[0] && settings.originalMaterials[0].HasProperty("_BaseMap"))
                    material.SetTexture("_Paper", settings.originalMaterials[0].GetTexture("_BaseMap"));
                groundRenderer.sharedMaterials = ownedMaterials;
                if(!(ground is MeshCollider meshCollider)||!ground.TryGetComponent<MeshFilter>(out var filter))throw new InvalidOperationException("Procedural ground needs a MeshFilter and MeshCollider.");
                ground.transform.localScale=Vector3.one;
                ground.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);
                Terrain=gameObject.AddComponent<ProceduralPlanetTerrain>();
                if (settings.profileId == PlanetGenerationSettings.NaturalProfileId)
                {
                    Terrain.PrepareNatural(data.spec.seed, data.spec.terrain.radius, data.spec.chapter, settings, filter, meshCollider);
                    var natural = Terrain.GenerationPlan;
                    Terrain.NaturalPlacement = new NaturalTerrainPlacement(natural, surface, playerCapsule,
                        settings.spawnClearance, settings.spawnDirection.normalized, settings.chapterCampaign);
                    natural.spawn = natural.placement.spawn.direction;
                }
                else
                {
                    var chapter = settings.Chapter(data.spec.chapter);
                    int requiredKind = settings.chapterCampaign && data.spec.role == PlanetRole.Start
                        ? data.spec.unlock switch
                        {
                            TraversalCapability.Dash => 0, TraversalCapability.AirJump => 1,
                            TraversalCapability.Glide => 2, _ => -1
                        }
                        : -1;
                    Terrain.Prepare(data.spec.seed, data.spec.terrain.radius, settings.spawnDirection.normalized,
                        chapter ? chapter.featureBudget : settings.terrainFeatureBudget, filter, meshCollider,
                        playerCapsule, data.spec.chapter,
                        PlanetSurfaceGrid.CalculateFrequency(settings.subdivisions, data.spec.terrain.sizeScale),
                        chapter ? (Vector3?)chapter.regionWeights : null, requiredKind, settings.terrainAppearance,
                        settings.proceduralWaterMaterial, useNormalPlanetRelief: data.spec.role == PlanetRole.Normal);
                }
                var appearance = Terrain.GenerationPlan.appearance;
                material.SetColor("_EarthColor", appearance.earth);
                material.SetColor("_VegetationColor", appearance.vegetation);
                material.SetColor("_RockColor", appearance.rock);
                material.SetFloat("_PaletteStrength", appearance.paletteStrength);
                material.SetVector("_VegetationRemap", new Vector4(appearance.vegetationRemap.x, appearance.vegetationRemap.y, 0, 0));
                material.SetFloat("_HeightColorRange", appearance.heightColorRange);
                var generated = Terrain.GenerationPlan;
                if (generated.natural != null && generated.moisture != null)
                {
                    material.SetFloat("_MoistureStrength", generated.natural.moistureStrength);
                    material.SetFloat("_MoistureMean", generated.moistureMean);
                }
                gravity.terrainPlan=Terrain.GenerationPlan;
                surface.CandidateFilter=Terrain.AllowsSpawn;
            }
            else
            {
                ownedMaterials = new Material[settings.originalMaterials.Length];
                bool originalColor = data.spec.terrain.groundColor.Equals("#" + ColorUtility.ToHtmlStringRGB(settings.baselineColor),
                    StringComparison.OrdinalIgnoreCase);
                for (int i = 0; i < ownedMaterials.Length; i++)
                {
                    var source = settings.originalMaterials[i];
                    if (!source) throw new InvalidOperationException("Missing ground material.");
                    var material = new Material(source) { name = data.spec.planetId + " Ground " + i };
                    float ratio = source.color.grayscale / Mathf.Max(.001f, settings.baselineColor.grayscale);
                    material.color = originalColor ? source.color : new Color(Mathf.Clamp01(color.r * ratio),
                        Mathf.Clamp01(color.g * ratio), Mathf.Clamp01(color.b * ratio), 1);
                    ownedMaterials[i] = material;
                }
                groundRenderer.sharedMaterials = ownedMaterials;
            }
            surface.ConfigureResolution(settings.subdivisions,data.spec.terrain.sizeScale);surface.candidatesPerCell=settings.candidatesPerCell;surface.maxSlope=settings.maxSpawnSlope;
            var plan = Terrain ? Terrain.GenerationPlan : null;
            var placement = plan?.placement;
            Physics.SyncTransforms();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            surface.Prepare(placement == null ? data.spec.seed : StarSystemFactory.DeriveSeed(plan.placementSeed, 1, 0));
            if (placement != null)
            {
                Terrain.NaturalPlacement.CheckCapacity(data.spec.baobab.maxActive, data.spec.baobab.minimumSpacing);
                placement.candidates = surface.SlotCount;
                placement.emptyCells = surface.EmptyCellCount;
                placement.candidateMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
            }
            var spawnDirection = placement == null ? settings.spawnDirection.normalized : placement.spawn.direction;
            if(surface.SlotCount==0||!surface.TryProject(spawnDirection,out var point,out var normal)
                ||Vector3.Angle(normal,(point-transform.position).normalized)>60f)throw new InvalidOperationException("No usable surface/spawn.");
            Vector3 heading = plan != null && plan.requiredKind >= 0 ? plan.routeTangent : settings.spawnForward;
            if (placement != null && placement.sites.Length > 0)
                heading = transform.TransformPoint(placement.sites[0].localPoint) - point;
            Vector3 forward=Vector3.ProjectOnPlane(heading,normal);
            if(forward.sqrMagnitude<.001f)forward=Vector3.ProjectOnPlane(Vector3.forward,normal);
            if(forward.sqrMagnitude<.001f)forward=Vector3.ProjectOnPlane(Vector3.right,normal);
            spawn.SetPositionAndRotation(point+normal*settings.spawnClearance,Quaternion.LookRotation(forward.normalized,normal));
            if(playerCapsule&&Physics.ComputePenetration(playerCapsule,spawn.position,spawn.rotation,ground,ground.transform.position,ground.transform.rotation,out _,out float depth)&&depth>.01f)
                throw new InvalidOperationException("Projected spawn overlaps the planet.");
            float scale=data.spec.terrain.sizeScale;
            gravity.surfaceRadius=settings.proceduralTerrain?data.spec.terrain.radius:settings.gravitySurfaceRadius*scale;gravity.strength=data.spec.terrain.gravityStrength;
            float outer=Vector3.Distance(ground.bounds.center,transform.position)+ground.bounds.extents.magnitude;
            gravity.influenceRadius=Mathf.Max(settings.influenceRadius*scale,outer+settings.gravityOuterMargin);
        }
        static bool Finite(Vector3 v)=>!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&!float.IsNaN(v.z)&&!float.IsInfinity(v.x)&&!float.IsInfinity(v.y)&&!float.IsInfinity(v.z);
        public void DisposeWorld(){gravity.enabled=false;gameObject.SetActive(false);Destroy(gameObject);}
        void OnDestroy(){if(ownedMaterials!=null)foreach(var material in ownedMaterials)if(material)Destroy(material);}
    }
}
