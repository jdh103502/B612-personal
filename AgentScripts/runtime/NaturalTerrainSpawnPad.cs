using UnityEngine;

namespace B612
{
    /// <summary>
    /// Generator v21: a flat clearing around the spawn, room for the planet's NPC and its objects. Near the preferred
    /// spawn direction it picks the spot that needs the least cutting and filling, away from the long river's valley
    /// and lake and not in a closed hollow (water would pond on it). Inside a slightly irregular core the ground sits at
    /// one height (constant radius, so it follows the planet's curve); a smooth band blends it back into the terrain,
    /// widened where the height difference would make that band steeper than the set slope.
    /// Generator v22 (planar): the core is a true plane perpendicular to the centre direction, like the original
    /// asteroid's start, instead of following the planet's curve. Past the core edge the plane's rise keeps going with
    /// a slope that eases to zero over half the band, so the ground has no crease where the walker could lose contact.
    /// </summary>
    public sealed class NaturalTerrainSpawnPad
    {
        public Vector3 centre;
        public float level, core, blend, maxCut, maxFill, offsetMeters;
        public bool planar;
        public string status;
        Vector3 noiseOffset;
        int noiseSeed;
        float cosOuter, radius;

        /// <summary>Core radius at a direction (metres); the outline wanders by up to 15%.</summary>
        float Edge(Vector3 direction)
            => core * (1f + .15f * ProceduralPlanetTerrain.Simplex(direction * radius / Mathf.Max(1f, 1.3f * core) + noiseOffset, noiseSeed));

        /// <summary>1 on the flat core, smoothly falling to 0 at the outer edge of the blend band.</summary>
        public float Weight(Vector3 direction)
        {
            float dot = Vector3.Dot(direction, centre);
            if (dot <= cosOuter) return 0f;
            float distance = Mathf.Acos(Mathf.Clamp(dot, -1f, 1f)) * radius;
            float edge = Edge(direction);
            if (distance <= edge) return 1f;
            float t = (distance - edge) / blend;
            if (t >= 1f) return 0f;
            return 1f - t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        public float Apply(Vector3 direction, float height)
        {
            float w = Weight(direction);
            return w <= 0f ? height : height + (Target(direction) - height) * w;
        }

        /// <summary>The clearing's own height at a direction: v21 one radius; v22 the plane, eased out past its edge.</summary>
        public float Target(Vector3 direction)
        {
            if (!planar) return level;
            float theta = Mathf.Acos(Mathf.Clamp(Vector3.Dot(direction, centre), -1f, 1f));
            float edge = Edge(direction) / radius;
            if (theta <= edge) return (radius + level) / Mathf.Cos(theta) - radius;
            float cos = Mathf.Cos(edge), atEdge = (radius + level) / cos - radius;
            float grade = (radius + level) * Mathf.Sin(edge) / (cos * cos * radius);
            float ease = .5f * blend, x = Mathf.Min((theta - edge) * radius, ease);
            return atEdge + grade * (x - x * x / (2f * ease));
        }

        /// <summary>A sample's height as the plane level through it (v22), or the height itself (v21).</summary>
        float LevelThrough(Vector3 direction, float height)
            => planar ? (radius + height) * Vector3.Dot(direction, centre) - radius : height;

        /// <summary>Inside the flat core (with a margin in metres, negative = shrink).</summary>
        public bool InCore(Vector3 direction, float margin = 0f)
        {
            float distance = Mathf.Acos(Mathf.Clamp(Vector3.Dot(direction, centre), -1f, 1f)) * radius;
            return distance <= Edge(direction) + margin;
        }

        internal static NaturalTerrainSpawnPad Build(ProceduralPlanetTerrain.Plan plan, Vector3 preferred)
        {
            var n = plan.natural;
            if (n == null || n.spawnPadRadiusMeters <= 0f) return null;
            float R = plan.radius;
            float core = Mathf.Min(n.spawnPadRadiusMeters, .3f * R);
            float blendMin = Mathf.Max(.5f, n.spawnPadBlendMeters);
            preferred = preferred.normalized;
            var basis = Vector3.Cross(preferred, Mathf.Abs(preferred.y) < .9f ? Vector3.up : Vector3.right).normalized;
            Vector3 Move(Vector3 d, Vector3 t, float metres) => (d * Mathf.Cos(metres / R) + t * Mathf.Sin(metres / R)).normalized;
            Vector3 Tangent(Vector3 d, float degrees)
            {
                var t0 = Vector3.ProjectOnPlane(basis, d);
                if (t0.sqrMagnitude < 1e-6f) t0 = Vector3.Cross(d, Vector3.right);
                return Quaternion.AngleAxis(degrees, d) * t0.normalized;
            }
            var valley = plan.valley;
            float H(Vector3 d) => ProceduralPlanetTerrain.FreeHeight(plan, d);

            NaturalTerrainSpawnPad best = null; float bestCost = float.PositiveInfinity;
            int rejectedWater = 0, rejectedHollow = 0, rejectedCliff = 0, tried = 0;
            float searchMeters = Mathf.Min(n.spawnPadSearchMeters, Mathf.PI * R);
            // Candidates: the preferred direction, then an even 2 m lattice (Fibonacci sphere), nearest first. Beyond the
            // search arc only while nothing fits yet (a long river can cover the whole neighbourhood). The core plus
            // half the narrowest band keeps off the river's widest channel and its banks.
            var candidates = new System.Collections.Generic.List<Vector3> { preferred };
            int lattice = Mathf.Max(8, Mathf.CeilToInt(4f * Mathf.PI * R * R / 4f));
            float cosSearch = Mathf.Cos(Mathf.Min(Mathf.PI, searchMeters / R)), golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < lattice; i++)
            {
                float y = 1f - 2f * (i + .5f) / lattice, r = Mathf.Sqrt(1f - y * y), a = golden * i;
                var local = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                var c = Quaternion.FromToRotation(Vector3.up, preferred) * local;
                candidates.Add(c.normalized);
            }
            candidates.Sort((x, y) => Vector3.Dot(y, preferred).CompareTo(Vector3.Dot(x, preferred)));
            float waterClear = core * 1.15f + .5f * blendMin + .5f * Mathf.Max(n.riverWidthMeters, n.riverMouthWidthMeters) + 1f;
            foreach (var c in candidates)
            {
                {
                    float distance = Mathf.Acos(Mathf.Clamp(Vector3.Dot(c, preferred), -1f, 1f)) * R;
                    if (best != null && Vector3.Dot(c, preferred) < cosSearch) break;
                    tried++;
                    if (valley != null)
                    {
                        bool near = Mathf.Acos(Mathf.Clamp(Vector3.Dot(c, valley.lakeCentre.normalized), -1f, 1f)) * R < valley.lakeRadius + waterClear;
                        for (int i = 0; i < valley.route.Count && !near; i++)
                            near = Mathf.Acos(Mathf.Clamp(Vector3.Dot(c, valley.route[i]), -1f, 1f)) * R < waterClear;
                        if (near) { rejectedWater++; continue; }
                    }
                    // Core samples: the centre and three rings of 12.
                    var probe = new NaturalTerrainSpawnPad { centre = c, radius = R, planar = n.spawnPadPlanar, core = core, blend = blendMin,
                        noiseSeed = plan.placementSeed ^ 0x51a7, noiseOffset = new Vector3(13.1f, 7.7f, 3.9f) + c * 5f };
                    float sum = H(c), weight = 1f, low = sum, high = sum;
                    bool cliff = ProceduralPlanetTerrain.InCliffZone(plan, c);
                    for (int k = 1; k <= 3; k++)
                        for (int j = 0; j < 12; j++)
                        {
                            var d = Move(c, Tangent(c, 30f * j + 10f * k), core * k / 3f);
                            float h = probe.LevelThrough(d, H(d));
                            sum += h * k; weight += k; low = Mathf.Min(low, h); high = Mathf.Max(high, h);
                            cliff |= ProceduralPlanetTerrain.InCliffZone(plan, d);
                        }
                    if (cliff) { rejectedCliff++; continue; }
                    float level = sum / weight;
                    probe.level = level;
                    // An outlet: some of the surrounding ring lies below the pad, so it is not a closed hollow.
                    bool outlet = false;
                    for (int j = 0; j < 16 && !outlet; j++)
                    {
                        var d = Move(c, Tangent(c, 22.5f * j), core * 1.15f + blendMin);
                        outlet = H(d) < probe.Target(d) - .05f;
                    }
                    if (!outlet) { rejectedHollow++; continue; }
                    float deviation = Mathf.Max(high - level, level - low);
                    // Gentle surroundings need a narrower band: the mean height difference just outside the core.
                    float around = 0f;
                    for (int j = 0; j < 12; j++)
                    {
                        var d = Move(c, Tangent(c, 30f * j + 15f), core * 1.15f + .6f * blendMin);
                        around += Mathf.Abs(H(d) - probe.Target(d)) / 12f;
                    }
                    float cost = deviation + .6f * around + .03f * distance;
                    if (cost >= bestCost) continue;
                    bestCost = cost;
                    best = probe;
                    best.maxCut = high - level; best.maxFill = level - low; best.offsetMeters = distance;
                }
            }
            if (best == null)
            {
                plan.spawnPadStatus = $"No spawn clearing: {tried} spots, {rejectedWater} near the valley or lake, {rejectedCliff} on a bluff, {rejectedHollow} in a hollow.";
                return null;
            }
            // The narrowest band (0.5 m steps) in which the blended ground is nowhere steeper than both the set slope and
            // the natural ground at that point (+3 degrees): the clearing does not add a scarp, it only flattens.
            float Slope(Vector3 d, bool padded)
            {
                var a = Vector3.Cross(d, Mathf.Abs(d.y) < .9f ? Vector3.up : Vector3.right).normalized;
                var b = Vector3.Cross(d, a);
                const float step = .2f;
                var da = (d + a * (step / R)).normalized; var db = (d + b * (step / R)).normalized;
                float h0 = H(d), ha = H(da), hb = H(db);
                if (padded) { h0 = best.Apply(d, h0); ha = best.Apply(da, ha); hb = best.Apply(db, hb); }
                float gx = (ha - h0) / step, gy = (hb - h0) / step;
                return Mathf.Atan(Mathf.Sqrt(gx * gx + gy * gy)) * Mathf.Rad2Deg;
            }
            for (float blend = blendMin; ; blend += .5f)
            {
                best.blend = Mathf.Min(blend, n.spawnPadMaxBlendMeters);
                best.cosOuter = Mathf.Cos(Mathf.Min(Mathf.PI, (core * 1.15f + best.blend) / R));
                if (best.blend >= n.spawnPadMaxBlendMeters) break;
                bool fits = true;
                for (int j = 0; j < 16 && fits; j++)
                {
                    var t = Tangent(best.centre, 22.5f * j);
                    float edge = best.Edge(Move(best.centre, t, core));
                    for (int k = 1; k <= 5 && fits; k++)
                    {
                        var d = Move(best.centre, t, edge + best.blend * (k - .5f) / 5f);
                        fits = Slope(d, true) <= Mathf.Max(n.spawnPadMaxBlendSlope, Slope(d, false) + 3f);
                    }
                }
                if (fits) break;
            }
            best.status = $"{(best.planar ? "Planar s" : "S")}pawn clearing {2f * core:F1} m across at {best.offsetMeters:F1} m from the preferred spawn, " +
                $"cut {best.maxCut:F2} m / fill {best.maxFill:F2} m, blend {best.blend:F1} m ({tried} spots, " +
                $"{rejectedWater} near water, {rejectedCliff} bluff, {rejectedHollow} hollow).";
            plan.spawnPadStatus = best.status;
            return best;
        }
    }
}
