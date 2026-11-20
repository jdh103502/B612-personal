using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace B612
{
    public sealed class DeveloperDebugHUD : MonoBehaviour
    {
        public DeveloperDebugController controller;
        public CanvasGroup panel;
        public UnityEngine.UI.Toggle dashToggle, airJumpToggle, glideToggle;
        public UnityEngine.UI.Button closeButton;
        public TMP_Text statusLabel;
        public UnityEngine.UI.Button skipButton;
        public TMP_Text skipButtonLabel, skipDescription;
        public const string SkipTitle = "바오밥 페이즈 스킵";
        public const string SkipDone = "스킵 완료";
        public const string SkipWaiting = "처리 중";
        public const string SkipReady = "현재 행성의 바오밥을 정리하고 지도 진행을 해금합니다.";
        public const string SkipSuccess = "바오밥 페이즈를 스킵했습니다. 닫은 뒤 M으로 지도를 여세요.";
        public const string SkipResolved = "이미 종료된 바오밥 페이즈입니다.";
        public const string SkipGameOver = "게임 오버 상태에서는 사용할 수 없습니다.";
        public const string SkipBusy = "현재 행성 준비 또는 이동 중입니다.";
        public const string SkipUnavailable = "현재 씬에서는 사용할 수 없습니다.";
        public const string SkipGlyphs = SkipTitle+SkipDone+SkipWaiting+SkipReady+SkipSuccess+SkipResolved+SkipGameOver+SkipBusy+SkipUnavailable;
        PlayerTraversalAbilities subscribed;
        void OnEnable()
        {
            if (dashToggle) dashToggle.onValueChanged.AddListener(SetDash);
            if (airJumpToggle) airJumpToggle.onValueChanged.AddListener(SetAirJump);
            if (glideToggle) glideToggle.onValueChanged.AddListener(SetGlide);
            if (closeButton) closeButton.onClick.AddListener(Close);
            if (skipButton) skipButton.onClick.AddListener(Skip);
            subscribed = controller ? controller.abilities : null;
            if (subscribed) subscribed.AllowedChanged += Refresh;
            Show(controller && controller.IsOpen);
        }
        void OnDisable()
        {
            if (dashToggle) dashToggle.onValueChanged.RemoveListener(SetDash);
            if (airJumpToggle) airJumpToggle.onValueChanged.RemoveListener(SetAirJump);
            if (glideToggle) glideToggle.onValueChanged.RemoveListener(SetGlide);
            if (closeButton) closeButton.onClick.RemoveListener(Close);
            if (skipButton) skipButton.onClick.RemoveListener(Skip);
            if (subscribed) subscribed.AllowedChanged -= Refresh;
        }
        public void Show(bool visible)
        {
            if (panel) { panel.alpha = visible ? 1f : 0f; panel.interactable = panel.blocksRaycasts = visible; }
            Refresh();
            if (visible && dashToggle && dashToggle.interactable && EventSystem.current)
                EventSystem.current.SetSelectedGameObject(dashToggle.gameObject);
        }
        public void Refresh()
        {
            var ability = controller && controller.player ? controller.abilities : null;
            SetToggle(dashToggle, ability&&ability.IsUnlocked(TraversalFeature.Dash), ability && ability.AllowDash);
            SetToggle(airJumpToggle, ability&&ability.IsUnlocked(TraversalFeature.AirJump), ability && ability.AllowAirJump);
            SetToggle(glideToggle, ability&&ability.IsUnlocked(TraversalFeature.Glide), ability && ability.AllowGlide);
            SetFeatureLabel(dashToggle,"대쉬 사용",ability&&ability.IsUnlocked(TraversalFeature.Dash));
            SetFeatureLabel(airJumpToggle,"더블점프 사용",ability&&ability.IsUnlocked(TraversalFeature.AirJump));
            SetFeatureLabel(glideToggle,"활공 사용",ability&&ability.IsUnlocked(TraversalFeature.Glide));
            if (statusLabel) statusLabel.text = ability ? "일시정지 중" : "플레이어 기술 구성이 없습니다";
            RefreshSkip();
        }
        void Update() { if (controller && controller.IsOpen) RefreshSkip(); }
        void RefreshSkip()
        {
            var state=controller && controller.flow ? controller.flow.GetDebugSkipState(controller)
                : StarSystemFlowController.DebugSkipState.Unavailable;
            bool ready=state==StarSystemFlowController.DebugSkipState.Ready;
            if (skipButton)
            {
                skipButton.interactable=ready;
                if (!ready && EventSystem.current && EventSystem.current.currentSelectedGameObject==skipButton.gameObject)
                    EventSystem.current.SetSelectedGameObject(closeButton ? closeButton.gameObject : null);
            }
            if (skipButtonLabel) skipButtonLabel.text=state==StarSystemFlowController.DebugSkipState.Skipped ? SkipDone
                : state==StarSystemFlowController.DebugSkipState.Pending ? SkipWaiting : SkipTitle;
            if (skipDescription) skipDescription.text=state==StarSystemFlowController.DebugSkipState.Skipped ? SkipSuccess
                : state==StarSystemFlowController.DebugSkipState.Resolved ? SkipResolved
                : state==StarSystemFlowController.DebugSkipState.GameOver ? SkipGameOver
                : state==StarSystemFlowController.DebugSkipState.Unavailable ? SkipUnavailable
                : state==StarSystemFlowController.DebugSkipState.Ready ? SkipReady
                : state==StarSystemFlowController.DebugSkipState.Pending ? SkipWaiting : SkipBusy;
        }
        void Skip() { if (controller) controller.RequestBaobabSkip(); RefreshSkip(); }
        static void SetToggle(UnityEngine.UI.Toggle toggle, bool available, bool value)
        {
            if (!toggle) return;
            toggle.interactable = available; toggle.SetIsOnWithoutNotify(value);
        }
        static void SetFeatureLabel(UnityEngine.UI.Toggle toggle,string text,bool unlocked)
        {if(toggle){var label=toggle.GetComponentInChildren<TMP_Text>();if(label)label.text=text+(unlocked?"":" (잠김)");}}
        void SetDash(bool value) { Set(TraversalFeature.Dash, value); }
        void SetAirJump(bool value) { Set(TraversalFeature.AirJump, value); }
        void SetGlide(bool value) { Set(TraversalFeature.Glide, value); }
        void Set(TraversalFeature feature, bool value) { if (controller && controller.player && controller.abilities) controller.abilities.SetAllowed(feature, value); }
        void Close() { if (controller) controller.Close(); }
    }
}
