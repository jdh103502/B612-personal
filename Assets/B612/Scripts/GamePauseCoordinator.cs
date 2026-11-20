using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace B612
{
    public enum GamePauseReason { DebugOverlay, GameOver, MapOverlay, PlanetTransition }

    /// <summary>Scene-local owners share one saved execution speed. Never persists into a new attempt.</summary>
    public sealed class GamePauseCoordinator : MonoBehaviour
    {
        public static GamePauseCoordinator Active { get; private set; }
        readonly Dictionary<Object, GamePauseReason> owners = new Dictionary<Object, GamePauseReason>();
        public bool IsPaused => owners.Count > 0;
        public int OwnerCount => owners.Count;
        float resumeSpeed = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = null; }
        void Awake() { if (!Active) Active = this; }
        public static GamePauseCoordinator GetOrCreate(Scene scene)
        {
            if (Active) return Active;
            var root = new GameObject("Game Pause Coordinator");
            SceneManager.MoveGameObjectToScene(root, scene);
            return root.AddComponent<GamePauseCoordinator>();
        }
        public bool HasReason(GamePauseReason reason) => owners.ContainsValue(reason);
        public void AcquirePause(Object owner, GamePauseReason reason)
        {
            if (!owner) return;
            if (owners.Count == 0) resumeSpeed = Time.timeScale;
            owners[owner] = reason;
            Time.timeScale = 0f;
        }
        public void ReleasePause(Object owner)
        {
            if (ReferenceEquals(owner, null) || !owners.Remove(owner)) return;
            Time.timeScale = owners.Count > 0 ? 0f : resumeSpeed;
        }
        public void ResetForSceneReload()
        {
            owners.Clear();
            Time.timeScale = resumeSpeed > 0f ? resumeSpeed : 1f;
        }
        void LateUpdate() { if (IsPaused) Time.timeScale = 0f; }
        void OnDestroy()
        {
            if (owners.Count > 0) Time.timeScale = resumeSpeed;
            owners.Clear();
            if (Active == this) Active = null;
        }
    }
}
