using UnityEngine;

namespace B612
{
    [CreateAssetMenu(menuName="B612/Planet Generation Settings")]
    public sealed class PlanetGenerationSettings : ScriptableObject
    {
        public string profileId="terrain.emptyPlanet.v1";
        public GameObject groundTemplate;
        public Vector3 baselineScale=Vector3.one;
        public float nominalRadius=5.3f, gravitySurfaceRadius=4.052941f, influenceRadius=16f, gravityStrength=3.4f;
        public Material[] originalMaterials;
        public Material proceduralGroundMaterial;
        public Material proceduralWaterMaterial;
        [Tooltip("Natural profile river and pond water (B612/Water). Falls back to proceduralWaterMaterial when empty.")]
        public Material riverWaterMaterial;
        public Material routeGuideMaterial;
        public TerrainAppearanceSettings terrainAppearance = new TerrainAppearanceSettings();
        public NaturalTerrainSettings naturalTerrain = new NaturalTerrainSettings();
        public const string StylizedProfileId = "terrain.stylized.v2";
        public const string NaturalProfileId = "terrain.natural.v1";
        public Color baselineColor=new Color(234f/255,223f/255,198f/255);
        public string[] palette={"#EADFC6","#A6B88C","#B8A6D8","#D9A6AC","#9EBCCB"};
        public Vector3 spawnDirection=Vector3.up, spawnForward=Vector3.forward;
        public float spawnClearance=.05f, gravityOuterMargin=7f;
        [Tooltip("1x baseline density. Higher levels increase cells and bounded candidate/Raycast work; level 2 reaches 1,280 cells at 2x.")]
        [Range(0,2)] public int subdivisions=1;
        [Range(4,96)] public int candidatesPerCell=32;
        public float maxSpawnSlope=55f;
        [Tooltip("Generate a continuous spherical surface and shallow rivers using the selected profile.")]
        public bool proceduralTerrain;
        [Range(0,2)] public int terrainFeatureBudget=2;
        public bool chapterCampaign;
        public ChapterTerrainDefinition[] chapters;
        public ChapterTerrainDefinition Chapter(int index)=>chapters!=null&&index>=1&&index<=chapters.Length?chapters[index-1]:null;
        public const float MinimumScale=.7f, MaximumScale=2f;
        public const int GeneratorVersion=13;
        public static PlanetSizeClass Classify(float scale)
        {
            if(float.IsNaN(scale)||float.IsInfinity(scale)||scale<MinimumScale||scale>MaximumScale)
                throw new System.ArgumentOutOfRangeException(nameof(scale));
            return scale<1f?PlanetSizeClass.Small:scale<1.5f?PlanetSizeClass.Medium:PlanetSizeClass.Large;
        }
    }
}
