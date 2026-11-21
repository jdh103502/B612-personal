using System;
using UnityEngine;

namespace B612
{
    /// <summary>Value: v9 lattice value noise (comparison only). Simplex: v10 gradient noise.</summary>
    public enum NaturalNoiseBasis { Value, Simplex }

    /// <summary>Trace: v10 steepest-descent tracer from a high source (comparison and fallback). Flow: v11 flow-accumulation river into a pond.</summary>
    public enum NaturalRiverMode { Trace, Flow }

    [Serializable]
    public sealed class NaturalTerrainSettings
    {
        [Min(2f)] public float broadScaleMeters = 12f;
        [Min(1f)] public float mediumScaleMeters = 5f;
        [Min(0f)] public float broadAmplitudeMeters = 1.4f;
        [Min(0f)] public float mediumAmplitudeMeters = .3f;
        [Tooltip("Generator v10 default: gradient (simplex) noise for both height layers. Value reproduces v9 for comparison.")]
        public NaturalNoiseBasis noiseBasis = NaturalNoiseBasis.Simplex;
        [Tooltip("Size of the low-frequency mask that varies the medium layer between calm and rough areas.")]
        [Min(5f)] public float roughnessScaleMeters = 18f;
        [Tooltip("Medium-layer multiplier from calm (x) to rough (y) areas, y up to 2 within the height budget. (1, 1) disables the mask, as in v9.")]
        public Vector2 roughnessRange = new Vector2(.2f, 1.6f);
        [Tooltip("Rendering only: how strongly drainage lines and basins turn green and ridges dry out (0 keeps the v11 colours).")]
        [Range(0f, 1.5f)] public float moistureStrength = 1f;
        [Tooltip("Rendering only: softening radius of the moisture bands.")]
        [Range(.4f, 3f)] public float moistureBlurMeters = 1.5f;
        [Tooltip("Generator v12: depth of the shallow dry gullies carved along the drainage network (0 reproduces v11).")]
        [Range(0f, .4f)] public float erosionDepthMeters = .2f;
        [Tooltip("Upstream flow cells (about 0.3 m2 each) a cell needs before a gully starts there. Larger means fewer, longer gullies.")]
        [Range(5f, 200f)] public float erosionMinCells = 40f;
        [Tooltip("Half width of a full-depth gully. Never narrower than 6.5 x its depth, which bounds the added slope.")]
        [Range(.5f, 3f)] public float erosionWidthMeters = 1f;

        public bool generateRiver = true;
        public Vector2 riverLengthMeters = new Vector2(8f, 18f);
        [Min(.6f)] public float riverWidthMeters = .9f;
        [Tooltip("Generator v13: channel width where a flow river meets its pond (at most 80% of the pond radius). The river widens from riverWidthMeters at the spring. 0 or narrower than the spring keeps a constant width (v12).")]
        [Range(0f, 5f)] public float riverMouthWidthMeters = 3f;
        [Tooltip("Generator v9 default: equal-angle cube-sphere ground mesh. Keeps the coarsest vertex spacing and removes the face-corner oversampling of the normalized cube. Off reproduces the v8 normalized mapping for comparison only.")]
        public bool equalAngleMesh = true;
        public Vector2 riverDepthMeters = new Vector2(.12f, .20f);
        [Min(.75f)] public float bankTransitionMeters = 1.2f;
        [Range(1, 64)] public int riverSourceAttempts = 64;
        [Range(32, 256)] public int riverTraceSteps = 256;
        [Tooltip("Generator v11 default: follow the largest inflow of a basin down into a shallow, walkable pond. Trace reproduces v10 and is the fallback.")]
        public NaturalRiverMode riverMode = NaturalRiverMode.Flow;
        [Tooltip("Cell spacing of the flow-routing graph.")]
        [Range(.4f, 1.2f)] public float flowSpacingMeters = .6f;
        [Tooltip("Deepest point of the pond below its surface. The pond floor is the natural ground and stays walkable.")]
        [Range(.08f, .4f)] public float pondMaxDepthMeters = .25f;
        [Range(1f, 8f)] public float pondMaxRadiusMeters = 3.5f;

        [Range(.35f, 1f)] public float analysisSpacingMeters = .6f;
        [Range(1000, 30000)] public int maxAnalysisNodes = 30000;
        [Range(.1f, 1f)] public float minConnectedAreaRatio = .60f;
        [Min(1.5f)] public float siteSpacingMeters = 2f;
        [Range(8, 64)] public int siteCandidateLimit = 64;

        internal NaturalTerrainSettings Snapshot(float radius)
        {
            static bool Valid(float value, float min, float max) => value >= min && value <= max;
            if (!Enum.IsDefined(typeof(NaturalNoiseBasis), noiseBasis) || !Valid(roughnessScaleMeters, 5, 60) ||
                !Valid(roughnessRange.x, .1f, 1) || !Valid(roughnessRange.y, roughnessRange.x, 2) ||
                !Valid(moistureStrength, 0, 1.5f) || !Valid(moistureBlurMeters, .4f, 3) ||
                !Valid(erosionDepthMeters, 0, .4f) || !Valid(erosionMinCells, 5, 200) || !Valid(erosionWidthMeters, .5f, 3) ||
                !Valid(broadScaleMeters, 2, 60) || !Valid(mediumScaleMeters, 1, broadScaleMeters) ||
                !Valid(broadAmplitudeMeters, 0, 8) || !Valid(mediumAmplitudeMeters, 0, 8) ||
                !Valid(riverLengthMeters.x, 2, 30) || !Valid(riverLengthMeters.y, riverLengthMeters.x, 40) ||
                !Valid(riverWidthMeters, .6f, 2) || !Valid(riverMouthWidthMeters, 0, 5) || !Valid(riverDepthMeters.x, .08f, .3f) ||
                !Valid(riverDepthMeters.y, riverDepthMeters.x, .3f) || !Valid(bankTransitionMeters, .75f, 4) ||
                riverSourceAttempts < 1 || riverSourceAttempts > 64 || riverTraceSteps < 32 || riverTraceSteps > 256 ||
                !Enum.IsDefined(typeof(NaturalRiverMode), riverMode) || !Valid(flowSpacingMeters, .4f, 1.2f) ||
                !Valid(pondMaxDepthMeters, .08f, .4f) || !Valid(pondMaxRadiusMeters, 1, 8) ||
                !Valid(analysisSpacingMeters, .35f, 1) || maxAnalysisNodes < 1000 || maxAnalysisNodes > 30000 ||
                !Valid(minConnectedAreaRatio, .1f, 1) || !Valid(siteSpacingMeters, 1.5f, 8) ||
                siteCandidateLimit < 8 || siteCandidateLimit > 64)
                throw new ArgumentException("Invalid natural terrain settings.");

            float scale = Mathf.Min(1f, radius / 12f);
            var used = (NaturalTerrainSettings)MemberwiseClone();
            used.broadScaleMeters *= scale;
            used.mediumScaleMeters *= scale;
            used.roughnessScaleMeters *= scale;
            used.moistureBlurMeters *= scale;
            used.erosionDepthMeters *= scale;
            used.erosionWidthMeters *= scale;
            // The budget bounds the peak: rough patches may raise the medium layer above its base amplitude.
            float amplitude = (broadAmplitudeMeters + mediumAmplitudeMeters * Mathf.Max(1f, roughnessRange.y)) * scale;
            float budget = Mathf.Min(2f, .17f * radius);
            float amplitudeScale = amplitude > budget ? scale * budget / amplitude : scale;
            used.broadAmplitudeMeters *= amplitudeScale;
            used.mediumAmplitudeMeters *= amplitudeScale;
            used.riverLengthMeters *= scale;
            used.riverWidthMeters *= scale;
            used.riverMouthWidthMeters *= scale;
            used.riverDepthMeters *= scale;
            used.bankTransitionMeters *= scale;
            used.flowSpacingMeters *= scale;
            used.pondMaxDepthMeters *= scale;
            used.pondMaxRadiusMeters *= scale;
            return used;
        }
    }
}
