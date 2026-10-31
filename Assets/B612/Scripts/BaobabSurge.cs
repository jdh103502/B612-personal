using System;
using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    /// <summary>Prototype only: a timed, spatially balanced sprout spawning phase.</summary>
    public sealed class BaobabSurge : MonoBehaviour
    {
        public PlanetSurfaceGrid surface;
        public GameObject sproutPrefab;
        public Transform player;
        public Transform spawnPoint;
        public BaobabThreatMeter threat;
        [Min(1f)] public float durationSeconds = 120f;
        [Min(1)] public int targetSprouts = 40;
        [Min(1)] public int maxActiveSprouts = 6;
        [Min(0.1f)] public float minimumSpacing = 1.4f;
        [Min(0f)] public float playerClearance = 0.9f;
        [Min(0f)] public float spawnClearance = 0.7f;
        public bool randomizeSeed = true;
        public int seed = 612;

        public sealed class SpawnRecord
        {
            public PlanetSurfaceGrid.SpawnSlot slot;
            public GameObject instance;
            public float scheduledTime;
            public float spawnedAt;
            public BaobabTypeDefinition type;
            public BaobabGrowth growth = BaobabGrowth.Basic;
            public bool released;
            public bool IsActive => !released && instance && instance.activeInHierarchy;
        }

        readonly List<SpawnRecord> spawned = new List<SpawnRecord>();
        public IReadOnlyList<SpawnRecord> Spawned => spawned;
        public int SeedUsed { get; private set; }
        public float ElapsedSeconds { get; private set; }
        // Keeps advancing after spawning ends, so remaining roots continue growing.
        public float SimulationSeconds { get; private set; }
        public bool IsGameOver => threat && threat.IsGameOver;
        public float RemainingSeconds => Mathf.Max(0f, durationSeconds - ElapsedSeconds);
        public float RemainingFraction => Mathf.Clamp01(RemainingSeconds / Mathf.Max(1f, durationSeconds));
        public bool IsComplete { get; private set; }
        public int Attempts { get; private set; }
        public int SkippedSpawns { get; private set; }
        public int CapacityBlockedSpawns { get; private set; }
        public int SpawnedCount => spawned.Count;
        public int ActiveSproutCount
        {
            get { int count = 0; foreach (var record in spawned) if (record.IsActive) count++; return count; }
        }
        public event Action Changed;

        int[] cellCounts;
        System.Random random;
        Transform sproutRoot;
        bool ready;

        void Start() { Begin(); }

        public void Begin()
        {
            if (!surface || !surface.ground || !sproutPrefab)
            {
                Debug.LogError("BaobabSurge needs a planet surface, its collider and the sprout prefab.", this);
                enabled = false;
                return;
            }
            // Begin can also be used to repeat the same scene during playtesting.
            foreach (var record in spawned)
                if (record.instance) { record.instance.SetActive(false); Destroy(record.instance); }
            spawned.Clear();
            if (sproutRoot) Destroy(sproutRoot.gameObject);
            durationSeconds = Mathf.Max(1f, durationSeconds);
            targetSprouts = Mathf.Max(1, targetSprouts);
            maxActiveSprouts = Mathf.Max(1, maxActiveSprouts);
            SeedUsed = randomizeSeed ? Guid.NewGuid().GetHashCode() : seed;
            random = new System.Random(SeedUsed);
            Physics.SyncTransforms();
            surface.Prepare(SeedUsed);
            cellCounts = new int[surface.Cells.Count];
            sproutRoot = new GameObject("Spawned Sprouts").transform;
            sproutRoot.SetParent(surface.transform, false);
            ElapsedSeconds = 0f;
            SimulationSeconds = 0f;
            Attempts = SkippedSpawns = CapacityBlockedSpawns = 0;
            IsComplete = false;
            ready = true;
            if (threat) threat.ResetForRun();
            Advance(0f);
        }

        void Update()
        {
            if (!ready || IsGameOver) return;
            Advance(Time.deltaTime);
        }

        bool ReleaseRemovedSlots()
        {
            bool changed = false;
            foreach (var record in spawned)
                if (!record.released && (!record.instance || !record.instance.activeInHierarchy))
                {
                    record.released = true;
                    record.slot.occupied = false;
                    changed = true;
                }
            return changed;
        }

        // Shared removal entry point for later eating/interaction logic; no new player action yet.
        public bool RemoveSprout(GameObject instance)
        {
            if (IsGameOver) return false;
            foreach (var record in spawned)
                if (record.instance == instance && record.IsActive)
                {
                    record.released = true;
                    record.slot.occupied = false;
                    instance.SetActive(false);
                    Destroy(instance);
                    Changed?.Invoke();
                    return true;
                }
            return false;
        }

        /// <summary>Scaled game time. Only the spawn countdown is bounded at the end.</summary>
        public void Advance(float deltaSeconds)
        {
            if (!ready || IsGameOver || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds)) return;
            ReleaseRemovedSlots();
            SimulationSeconds += Mathf.Max(0f, deltaSeconds);
            if (IsComplete) { Changed?.Invoke(); return; }
            ElapsedSeconds = Mathf.Min(durationSeconds, ElapsedSeconds + Mathf.Max(0f, deltaSeconds));
            // First sprout at 0s, then 3s, 6s, ... 117s with the default 40 / 120s.
            int due = Mathf.Min(targetSprouts, 1 + Mathf.FloorToInt(ElapsedSeconds * targetSprouts / durationSeconds));
            while (Attempts < due)
            {
                float scheduledTime = Attempts * durationSeconds / targetSprouts;
                Attempts++;
                // A full planet consumes this scheduled opportunity; never build a catch-up backlog.
                if (ActiveSproutCount >= maxActiveSprouts) { CapacityBlockedSpawns++; continue; }
                if (!TrySpawn(scheduledTime)) SkippedSpawns++;
            }
            IsComplete = ElapsedSeconds >= durationSeconds;
            if (IsComplete && SkippedSpawns > 0)
                Debug.LogWarning($"Baobab surge seed {SeedUsed}: {SpawnedCount}/{targetSprouts} spawned; {SkippedSpawns} placements skipped to preserve spacing.", this);
            Changed?.Invoke();
        }

        bool TrySpawn(float scheduledTime)
        {
            PlanetSurfaceGrid.SpawnSlot best = null;
            int leastCount = int.MaxValue;
            float bestScore = float.NegativeInfinity;
            float spacingSquared = minimumSpacing * minimumSpacing;
            foreach (var cell in surface.Cells)
            {
                if (cellCounts[cell.id] > leastCount) continue;
                foreach (var slot in cell.slots)
                {
                    if (slot.occupied) continue;
                    Vector3 point = surface.WorldPoint(slot);
                    if (player && (point - player.position).sqrMagnitude < playerClearance * playerClearance) continue;
                    if (spawnPoint && (point - spawnPoint.position).sqrMagnitude < spawnClearance * spawnClearance) continue;
                    float nearest = float.PositiveInfinity;
                    foreach (var record in spawned)
                        if (record.IsActive) nearest = Mathf.Min(nearest, (point - surface.WorldPoint(record.slot)).sqrMagnitude);
                    if (nearest < spacingSquared) continue;
                    // Fewest-used valid cells first; randomized maximin placement spreads early spawns
                    // over the WHOLE sphere and checks neighbours across every cell boundary.
                    float score = float.IsPositiveInfinity(nearest) ? (float)random.NextDouble()
                        : nearest * Mathf.Lerp(0.72f, 1.28f, (float)random.NextDouble());
                    if (cellCounts[cell.id] < leastCount || score > bestScore)
                    {
                        leastCount = cellCounts[cell.id]; bestScore = score; best = slot;
                    }
                }
            }
            if (best == null) return false;
            Vector3 normal = surface.WorldNormal(best);
            var rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            var instance = Instantiate(sproutPrefab, surface.WorldPoint(best) - normal * 0.015f, rotation, sproutRoot);
            instance.name = $"Sprout {SpawnedCount + 1:00} (Cell {best.cellId:00})";
            instance.transform.localScale *= Mathf.Lerp(0.92f, 1.08f, (float)random.NextDouble());
            best.occupied = true;
            cellCounts[best.cellId]++;
            spawned.Add(new SpawnRecord { slot = best, instance = instance, scheduledTime = scheduledTime,
                spawnedAt = SimulationSeconds, type = threat ? threat.RunType : null,
                growth = threat ? threat.RunGrowth : BaobabGrowth.Basic });
            return true;
        }
    }
}
