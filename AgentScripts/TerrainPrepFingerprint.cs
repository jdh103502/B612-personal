using System;
using B612;
using UnityEngine;

// Exact fingerprint of a prepared natural planet: ground vertices/colors/indices bits, water mesh,
// spawn/sites and baobab slot positions. Use it before/after refactors that must not change output.
//   unity command run_script --file AgentScripts/TerrainPrepFingerprint.cs --entry TerrainPrepFingerprint.Run --args "[612101,2,\"system_02_B\",1]"
public static class TerrainPrepFingerprint
{
    /// <summary>Same as Run with the v9 noise (value basis, no roughness mask), for checking that v9 is reproducible.</summary>
    public static object RunV9Noise(int journey, int chapter, string planetId, float size)
    {
#if UNITY_EDITOR
        var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>("Assets/B612/Settings/Chapters/ChapterJourneyGeneration.asset");
        string original = JsonUtility.ToJson(settings.naturalTerrain);
        try
        {
            settings.naturalTerrain.noiseBasis = NaturalNoiseBasis.Value;
            settings.naturalTerrain.roughnessRange = Vector2.one;
            return Run(journey, chapter, planetId, size);
        }
        finally { JsonUtility.FromJsonOverwrite(original, settings.naturalTerrain); }
#else
        throw new InvalidOperationException("Editor only");
#endif
    }

    /// <summary>Same as Run with the v10 traced river (no flow routing, no pond).</summary>
    public static object RunV10River(int journey, int chapter, string planetId, float size)
    {
#if UNITY_EDITOR
        var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>("Assets/B612/Settings/Chapters/ChapterJourneyGeneration.asset");
        string original = JsonUtility.ToJson(settings.naturalTerrain);
        try
        {
            settings.naturalTerrain.riverMode = NaturalRiverMode.Trace;
            return Run(journey, chapter, planetId, size);
        }
        finally { JsonUtility.FromJsonOverwrite(original, settings.naturalTerrain); }
#else
        throw new InvalidOperationException("Editor only");
#endif
    }

    public static object Run(int journey, int chapter, string planetId, float size)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Use Play mode");
#if UNITY_EDITOR
        var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<PlanetGenerationSettings>("Assets/B612/Settings/Chapters/ChapterJourneyGeneration.asset");
#else
        PlanetGenerationSettings settings = null;
#endif
        var flow = UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();
        var data = StarSystemFactory.Create(StarSystemFactory.DeriveSeed(journey, 50, chapter), settings, chapter).planets.Find(p => p.spec.planetId == planetId);
        if (size > 0f)
        {
            data.spec.terrain.sizeScale = size;
            data.spec.terrain.radius = settings.nominalRadius * size;
            data.spec.sizeClass = PlanetGenerationSettings.Classify(size);
        }
        var root = new GameObject("Terrain prep fingerprint");
        root.transform.position = new Vector3(4000, 0, 0);
        var host = root.AddComponent<PlanetWorldHost>();
        host.settings = settings;
        try
        {
            var world = host.Prepare(data, flow.player.GetComponent<CapsuleCollider>());
            var terrain = world.Terrain;
            var plan = terrain.GenerationPlan;
            var mesh = terrain.OwnedMesh;
            ulong ground = Fnv(), positions = Fnv(), tints = Fnv();
            foreach (var v in mesh.vertices) { ground = Mix(Mix(Mix(ground, v.x), v.y), v.z); positions = Mix(Mix(Mix(positions, v.x), v.y), v.z); }
            foreach (var c in mesh.colors) { ground = Mix(Mix(Mix(Mix(ground, c.r), c.g), c.b), c.a); tints = Mix(Mix(Mix(Mix(tints, c.r), c.g), c.b), c.a); }
            foreach (var n in mesh.normals) ground = Mix(Mix(Mix(ground, n.x), n.y), n.z);
            foreach (int i in mesh.triangles) ground = Mix(ground, i);
            ulong water = Fnv();
            if (terrain.WaterMesh) foreach (var v in terrain.WaterMesh.vertices) water = Mix(Mix(Mix(water, v.x), v.y), v.z);
            ulong placement = Fnv();
            var report = plan.placement;
            placement = Mix(Mix(Mix(placement, report.spawn.direction.x), report.spawn.direction.y), report.spawn.direction.z);
            foreach (var site in report.sites) placement = Mix(Mix(Mix(placement, site.direction.x), site.direction.y), site.direction.z);
            placement = Mix(Mix(Mix(placement, report.standable), report.linkedEdges), report.connectedAreaRatio);
            ulong slots = Fnv();
            foreach (var cell in world.surface.Cells)
            foreach (var slot in cell.slots)
                slots = Mix(Mix(Mix(slots, slot.localPoint.x), slot.localPoint.y), slot.localPoint.z);
            return new
            {
                planetId, plan.radius, vertices = mesh.vertexCount, ground = ground.ToString("x16"),
                positions = positions.ToString("x16"), tints = tints.ToString("x16"), water = water.ToString("x16"),
                placement = placement.ToString("x16"), slots = slots.ToString("x16"), world.surface.SlotCount,
                plan.meshMilliseconds, report.analysisMilliseconds, report.candidateMilliseconds
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static ulong Fnv() => 14695981039346656037UL;
    static ulong Mix(ulong hash, float value) => Mix(hash, BitConverter.ToInt32(BitConverter.GetBytes(value), 0));
    static ulong Mix(ulong hash, int value)
    {
        unchecked
        {
            for (int i = 0; i < 4; i++) { hash ^= (byte)(value >> (8 * i)); hash *= 1099511628211UL; }
            return hash;
        }
    }
}
