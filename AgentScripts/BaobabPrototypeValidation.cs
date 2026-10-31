using System;
using System.Linq;
using System.Collections.Generic;
using B612;
using UnityEngine;

// Run inside Play mode using Unity CLI run_script. All changes are Play-mode only.
public static class BaobabPrototypeValidation
{
    static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    public static object Inspect()
    {
        var s = UnityEngine.Object.FindFirstObjectByType<BaobabSurge>();
        var h = UnityEngine.Object.FindFirstObjectByType<SurgeTimerHUD>();
        var active = s.Spawned.Where(r => r.IsActive).ToArray();
        var directions = active.Select(r => r.slot.localPoint.normalized).ToArray();
        var octants = new int[8];
        var mean = Vector3.zero;
        float minDistance = float.PositiveInfinity;
        foreach (var d in directions) { octants[(d.x >= 0 ? 1 : 0) | (d.y >= 0 ? 2 : 0) | (d.z >= 0 ? 4 : 0)]++; mean += d; }
        for (int i = 0; i < active.Length; i++)
            for (int j = 0; j < i; j++)
                minDistance = Mathf.Min(minDistance, Vector3.Distance(s.surface.WorldPoint(active[i].slot),s.surface.WorldPoint(active[j].slot)));
        return new
        {
            s.ElapsedSeconds, s.RemainingSeconds, s.IsComplete, s.SeedUsed, s.SpawnedCount, s.Attempts, s.SkippedSpawns,
            s.ActiveSproutCount, s.maxActiveSprouts, s.CapacityBlockedSpawns,
            cells = s.surface.Cells.Count, slots = s.surface.SlotCount,
            usedCells = active.Select(r => r.slot.cellId).Distinct().Count(),
            minDistance, octants, upper = directions.Count(d => d.y >= 0), lower = directions.Count(d => d.y < 0),
            directionalMean = directions.Length > 0 ? (mean / directions.Length).magnitude : 0f,
            maximumAlignmentError = active.Select(r => Vector3.Angle(r.instance.transform.up,s.surface.WorldNormal(r.slot))).DefaultIfEmpty(0f).Max(),
            timeLabel = h.timeLabel.text, barFraction = h.fill.anchorMax.x, phase = h.titleLabel.text
        };
    }

    public static object ValidateSeeds()
    {
        Require(Application.isPlaying, "Run validation in Play mode.");
        var s = UnityEngine.Object.FindFirstObjectByType<BaobabSurge>();
        bool oldRandom = s.randomizeSeed, oldEnabled = s.enabled;
        int oldSeed = s.seed;
        int oldCap = s.maxActiveSprouts;
        float oldSpacing = s.minimumSpacing;
        var results = new List<object>();
        try
        {
            s.enabled = false; s.randomizeSeed = false;
            s.maxActiveSprouts = 40; // Exercise the original full-budget distribution separately from the live cap.
            foreach (int seed in new[] { 0, 1, 7, 42, 612, 2026, 65535, -1, -612, int.MaxValue, int.MinValue, 100003 })
            {
                s.seed = seed; s.Begin();
                for (int step = 0; step < 120; step++) s.Advance(1f);
                Require(s.IsComplete && s.SpawnedCount == 40 && s.SkippedSpawns == 0, $"Incomplete seed {seed}.");
                Require(s.surface.Cells.Count == 80 && s.surface.EmptyCellCount == 0, $"Invalid cells for seed {seed}.");
                Require(s.Spawned.Select(r => r.slot.cellId).Distinct().Count() == 40, $"Repeated cells in first pass for seed {seed}.");
                float min = float.PositiveInfinity;
                var octants = new int[8];
                var mean = Vector3.zero;
                foreach (var record in s.Spawned)
                {
                    var direction = record.slot.localPoint.normalized;
                    Require(PlanetSurfaceGrid.Contains(s.surface.Cells[record.slot.cellId], direction), "Slot outside its logical cell.");
                    Require(Vector3.Angle(record.instance.transform.up, s.surface.WorldNormal(record.slot)) < 0.1f, "Sprout not aligned with surface.");
                    octants[(direction.x >= 0 ? 1 : 0) | (direction.y >= 0 ? 2 : 0) | (direction.z >= 0 ? 4 : 0)]++;
                    mean += direction;
                }
                for (int i = 0; i < s.Spawned.Count; i++)
                    for (int j = 0; j < i; j++)
                        min = Mathf.Min(min, Vector3.Distance(s.surface.WorldPoint(s.Spawned[i].slot),s.surface.WorldPoint(s.Spawned[j].slot)));
                Require(min >= s.minimumSpacing - 0.001f, $"Spacing violation: seed {seed}, distance {min}.");
                Require(octants.All(n => n >= 2), $"Insufficient globe coverage for seed {seed}.");
                Require((mean / 40f).magnitude < 0.16f, $"Direction bias for seed {seed}.");
                Require(s.surface.Cells.All(c => c.neighbors.Distinct().Count() == 3 && c.neighbors.All(n => s.surface.Cells[n].neighbors.Contains(c.id))), "Invalid cell adjacency.");
                var first = s.Spawned.Select(r => r.slot.localPoint).ToArray();
                s.Advance(100f);
                Require(s.SpawnedCount == 40 && s.RemainingSeconds == 0f, "Spawns continued after completion.");
                s.Begin(); s.Advance(120f);
                Require(first.SequenceEqual(s.Spawned.Select(r=>r.slot.localPoint)), $"Seed {seed} did not reproduce across time steps.");
                results.Add(new { seed, count = 40, minimumDistance = min, octants, directionalMean = (mean / 40f).magnitude });
            }
            // Crowded-surface behaviour must terminate without weakening the minimum distance.
            s.minimumSpacing = 100f; s.Begin(); s.Advance(120f);
            Require(s.IsComplete && s.SpawnedCount == 1 && s.SkippedSpawns == 39, "Exhausted candidates did not terminate safely.");
            return new { passedSeeds = results.Count, results, exhaustionSkipped = s.SkippedSpawns, reproducible = true };
        }
        finally
        {
            s.minimumSpacing = oldSpacing; s.randomizeSeed = oldRandom; s.seed = oldSeed; s.enabled = oldEnabled; s.maxActiveSprouts = oldCap;
            s.Begin();
        }
    }

    public static object ValidateSurfaceTransform()
    {
        var s = UnityEngine.Object.FindFirstObjectByType<BaobabSurge>();
        var t = s.surface.transform;
        Vector3 position = t.position, scale = t.localScale;
        Quaternion rotation = t.rotation;
        try
        {
            s.surface.Prepare(612);
            var before = s.surface.Cells.SelectMany(c => c.slots).Select(slot => slot.localPoint).ToArray();
            t.position += new Vector3(8f, -3f, 5f); t.rotation = Quaternion.Euler(33f, 78f, -21f) * rotation; t.localScale = scale * 1.2f;
            Physics.SyncTransforms();
            s.surface.Prepare(612);
            var after = s.surface.Cells.SelectMany(c => c.slots).Select(slot => slot.localPoint).ToArray();
            Require(before.Length == after.Length, "Moving/scaling the planet changed its slot count.");
            float maxError = before.Zip(after, (a,b) => Vector3.Distance(a,b)).Max();
            Require(maxError < 0.001f, "Logical slots did not stay attached to the transformed planet.");
            return new { slotCount = after.Length, maxLocalPositionError = maxError, moved = true, rotated = true, uniformScale = 1.2f };
        }
        finally
        {
            t.SetPositionAndRotation(position, rotation); t.localScale = scale;
            Physics.SyncTransforms(); s.Begin();
        }
    }
}
