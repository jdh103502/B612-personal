using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace B612
{
    [DefaultExecutionOrder(-1000)]
    public sealed class DeveloperDebugController : MonoBehaviour
    {
        public PlayerController player;
        public PlayerTraversalAbilities abilities;
        public GamePauseCoordinator pause;
        public DeveloperDebugHUD hud;
        public StarSystemFlowController flow;
        public static DeveloperDebugController Active { get; private set; }
        public static bool Available => Application.isEditor || Debug.isDebugBuild;
        public bool IsOpen { get; private set; }
        public static bool BlocksGameplayInput => GameplayModalInput.BlocksGameplayInput;
        public static bool BlocksMouseInput => GameplayModalInput.BlocksMouseInput;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = null; }
        void Awake() { Active = this; if (!flow) flow = FindFirstObjectByType<StarSystemFlowController>(); }
        public bool RequestBaobabSkip() => Available && flow && flow.RequestDebugSkipBaobab(this);
        void Update()
        {
            if (!Available) return;
            if (IsOpen) GameplayModalInput.Observe(this);
            if (Keyboard.current != null && Keyboard.current.f12Key.wasPressedThisFrame)
            {
                if (IsOpen) Close(); else Open();
            }
        }
        public void Open()
        {
            if (!Available || IsOpen || !isActiveAndEnabled) return;
            if (!GameplayModalInput.TryBegin(this, player)) return;
            if (!pause) pause = GamePauseCoordinator.GetOrCreate(gameObject.scene);
            IsOpen = true;
            if (abilities) abilities.ClearPendingRequests();
            pause.AcquirePause(this, GamePauseReason.DebugOverlay);
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            if (hud) hud.Show(true);
        }
        public void Close()
        {
            if (flow) flow.CancelDebugSkipBaobab(this);
            if (!IsOpen) return;
            if (abilities) abilities.ClearPendingRequests();
            IsOpen = false;
            if (hud) hud.Show(false);
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(null);
            if (pause) pause.ReleasePause(this);
            GameplayModalInput.End(this);
        }
        void OnDisable() { Close(); }
        void OnDestroy() { Close(); if (Active == this) Active = null; }
    }
}
