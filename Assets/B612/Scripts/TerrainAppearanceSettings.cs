using System;
using UnityEngine;

namespace B612
{
    [Serializable]
    public sealed class TerrainAppearanceSettings
    {
        [Min(.1f)] public float macroFrequency = 1.2f;
        [Min(.1f)] public float hillScaleMeters = 3.2f;
        [Range(0, 2)] public int broadHills = 2;
        [Range(0, 1)] public int broadBasins = 1;
        public Vector2 hillHeights = new Vector2(1.05f, 1.4f);
        public Vector2 hillRadii = new Vector2(7.6f, 8f);
        public Vector2 basinDepths = new Vector2(.55f, .8f);
        public Vector2 basinRadii = new Vector2(6.3f, 7f);
        [Range(0f, .25f)] public float residualMacroMeters = .24f;
        [Range(0f, .10f)] public float residualHillMeters = .07f;
        [Range(0f, .25f)] public float ridgeShare;
        [Range(0f, .2f)] public float warpStrength;
        [Range(1.5f, 3f)] public float transitionMeters = 2.4f;
        public Vector2[] macroKeys = { new Vector2(0f, -1f), new Vector2(1f, 1f) };
        public Color earth = new Color(.83f, .57f, .42f);
        public Color vegetation = new Color(.57f, .80f, .49f);
        public Color rock = new Color(.69f, .61f, .76f);
        [Range(0f, 1f)] public float paletteStrength = .70f;
        public Vector2 vegetationRemap = new Vector2(.35f, .63f);
        [Min(.1f)] public float heightColorRange = 1.05f;

        internal TerrainAppearanceSettings Snapshot()
        {
            if (!InRange(macroFrequency, .1f, 4f) || !InRange(hillScaleMeters, .1f, 30f) ||
                !InRange(residualMacroMeters, 0f, .25f) || !InRange(residualHillMeters, 0f, .1f) ||
                !InRange(ridgeShare, 0f, .25f) || !InRange(warpStrength, 0f, .2f) || !InRange(transitionMeters, 1.5f, 3f) ||
                broadHills < 0 || broadHills > 2 || broadBasins < 0 || broadBasins > 1 ||
                !ValidRange(hillHeights, .8f, 1.4f) || !ValidRange(hillRadii, 5f, 8f) ||
                !ValidRange(basinDepths, .4f, .8f) || !ValidRange(basinRadii, 4f, 7f) ||
                !InRange(paletteStrength, 0f, 1f) || !ValidRange(vegetationRemap, 0f, 1f) ||
                vegetationRemap.y <= vegetationRemap.x || !InRange(heightColorRange, .1f, 3f) ||
                !ValidColor(earth) || !ValidColor(vegetation) || !ValidColor(rock))
                throw new ArgumentException("Invalid terrain appearance settings.");
            if (macroKeys == null || macroKeys.Length < 2 || macroKeys[0].x != 0f || macroKeys[macroKeys.Length - 1].x != 1f)
                throw new ArgumentException("Terrain height curve must cover [0, 1].");
            for (int i = 0; i < macroKeys.Length; i++)
            {
                var key = macroKeys[i];
                if (!InRange(key.x, 0f, 1f) || !InRange(key.y, -1f, 1f) ||
                    (i > 0 && (key.x <= macroKeys[i - 1].x || key.y < macroKeys[i - 1].y)))
                    throw new ArgumentException("Terrain height curve must be finite and monotone.");
            }
            var copy = (TerrainAppearanceSettings)MemberwiseClone();
            copy.macroKeys = (Vector2[])macroKeys.Clone();
            return copy;
        }

        internal float EvaluateMacro(float value)
        {
            for (int i = 1; i < macroKeys.Length; i++)
            {
                Vector2 next = macroKeys[i];
                if (value > next.x) continue;
                Vector2 previous = macroKeys[i - 1];
                float t = Mathf.InverseLerp(previous.x, next.x, value);
                return Mathf.Lerp(previous.y, next.y, t);
            }
            return macroKeys[macroKeys.Length - 1].y;
        }

        static bool InRange(float value, float min, float max) => value >= min && value <= max;
        static bool ValidRange(Vector2 values, float min, float max) =>
            InRange(values.x, min, max) && InRange(values.y, values.x, max);
        static bool ValidColor(Color color) => InRange(color.r, 0f, 1f) && InRange(color.g, 0f, 1f) && InRange(color.b, 0f, 1f);
    }
}
