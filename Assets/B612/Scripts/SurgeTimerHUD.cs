using TMPro;
using UnityEngine;

namespace B612
{
    /// <summary>Presentation only; the director owns all time and spawn state.</summary>
    public sealed class SurgeTimerHUD : MonoBehaviour
    {
        public BaobabSurge surge;
        public RectTransform fill;
        public UnityEngine.UI.Image fillImage;
        public TMP_Text titleLabel;
        public TMP_Text timeLabel;
        public TMP_Text countLabel;
        public Color normalColor = new Color(0.61f, 0.87f, 0.66f);
        public Color urgentColor = new Color(1f, 0.65f, 0.38f);

        void OnEnable()
        {
            if (surge)
            {
                surge.Changed += Refresh;
                if (surge.threat) surge.threat.Changed += Refresh;
                Refresh();
            }
        }

        void OnDisable()
        {
            if (!surge) return;
            surge.Changed -= Refresh;
            if (surge.threat) surge.threat.Changed -= Refresh;
        }

        public void Refresh()
        {
            if (!surge) return;
            float fraction = surge.RemainingFraction;
            if (fill) fill.anchorMax = new Vector2(fraction, 1f);
            if (fillImage) fillImage.color = fraction > 0.25f ? normalColor : urgentColor;
            int seconds = Mathf.CeilToInt(surge.RemainingSeconds);
            if (timeLabel) timeLabel.SetText("{0:00}:{1:00}", seconds / 60, seconds % 60);
            if (titleLabel) titleLabel.text = surge.IsGameOver ? "PLANET OVERWHELMED" : !surge.IsComplete ? "BAOBAB SURGE"
                : surge.ActiveSproutCount > 0 ? "CLEAR REMAINING SPROUTS" : "BAOBAB THREAT CLEARED";
            if (countLabel) countLabel.SetText("ACTIVE  {0} / {1}    |    TOTAL  {2}", surge.ActiveSproutCount, surge.maxActiveSprouts, surge.SpawnedCount);
        }
    }
}
