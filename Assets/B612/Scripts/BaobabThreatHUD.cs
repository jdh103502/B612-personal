using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace B612
{
    [DefaultExecutionOrder(60)]
    public sealed class BaobabThreatHUD : MonoBehaviour
    {
        public BaobabThreatMeter meter;
        public RectTransform fill;
        public UnityEngine.UI.Image fillImage;
        public TMP_Text percentLabel, statusLabel;
        public GameObject gameOverPanel;
        public UnityEngine.UI.Button retryButton;

        void OnEnable()
        {
            if (meter) meter.Changed += Refresh;
            if (retryButton) retryButton.onClick.AddListener(Retry);
            Refresh();
        }
        void OnDisable()
        {
            if (meter) meter.Changed -= Refresh;
            if (retryButton) retryButton.onClick.RemoveListener(Retry);
        }
        void Update()
        {
            if (meter && meter.IsGameOver && Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame) Retry();
        }
        void Retry() { if (meter) meter.RestartAttempt(); }
        public void Refresh()
        {
            if (!meter) return;
            float fraction = meter.Fraction;
            if (fill) fill.anchorMax = new Vector2(fraction, 1f);
            Color color = fraction >= .8f ? new Color(1f,.38f,.34f) : fraction >= .5f ? new Color(1f,.75f,.36f) : new Color(.59f,.86f,.67f);
            if (fillImage) fillImage.color = color;
            // Floor prevents a rounded-up 100% before the actual defeat threshold.
            if (percentLabel) { percentLabel.text = (meter.IsGameOver ? 100 : Mathf.FloorToInt(fraction * 100f)) + "%"; percentLabel.color = color; }
            if (statusLabel) statusLabel.text = meter.IsGameOver ? "LIMIT REACHED" : meter.State == BaobabThreatMeter.Phase.Resolved ? "PLANET SAFE"
                : fraction >= .8f ? "CRITICAL  -  FEED THE LAMBS" : fraction >= .5f ? "ROOTS ARE GROWING" : "KEEP THE ROOTS UNDER CONTROL";
            if (gameOverPanel && gameOverPanel.activeSelf != meter.IsGameOver) gameOverPanel.SetActive(meter.IsGameOver);
        }
    }
}
