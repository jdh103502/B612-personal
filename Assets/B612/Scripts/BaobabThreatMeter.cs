using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace B612
{
    // Lamb Update completes removals before the single end-of-frame defeat decision.
    [DefaultExecutionOrder(50)]
    public sealed class BaobabThreatMeter : MonoBehaviour
    {
        public enum Phase { Surging, Clearing, Resolved, GameOver }
        public BaobabSurge surge;
        public PlanetBaobabProfile profile;
        public PlayerController playerInput;
        public OrbitCamera cameraInput;
        public Phase State { get; private set; }
        public bool IsGameOver => State == Phase.GameOver;
        public float Capacity { get; private set; } = 100f;
        public float RawThreat { get; private set; }
        public float Fraction => IsGameOver ? 1f : Mathf.Clamp01(RawThreat / Capacity);
        public float GrowthPerSecond { get; private set; }
        public BaobabTypeDefinition RunType { get; private set; }
        public BaobabGrowth RunGrowth { get; private set; } = BaobabGrowth.Basic;
        public BaobabLifeCycle RunLifeCycle { get; private set; } = BaobabLifeCycle.Basic;
        public event Action Changed;
        public event Action GameOver;

        float previousTimeScale = 1f;
        bool ownsPause, playerWasEnabled, cameraWasEnabled, restarting;

        public void ResetForRun()
        {
            RestoreProgress();
            Capacity = profile ? Mathf.Max(.01f, profile.threatCapacity) : 100f;
            RunType = profile ? profile.defaultType : null;
            RunGrowth = RunType ? RunType.Snapshot() : BaobabGrowth.Basic;
            RunLifeCycle = RunType ? RunType.SnapshotLifeCycle() : BaobabLifeCycle.Basic;
            RawThreat = GrowthPerSecond = 0f;
            State = Phase.Surging;
            restarting = false;
            Changed?.Invoke();
        }

        void LateUpdate() { Evaluate(); }

        public float Contribution(BaobabSurge.SpawnRecord record)
            => record != null && record.IsActive ? record.growth.Evaluate(Mathf.Max(0f, surge.SimulationSeconds - record.spawnedAt)) : 0f;

        public void Evaluate()
        {
            if (!surge || IsGameOver) return;
            RawThreat = GrowthPerSecond = 0f;
            foreach (var record in surge.Spawned)
            {
                if (!record.IsActive) continue;
                float age = Mathf.Max(0f, surge.SimulationSeconds - record.spawnedAt);
                RawThreat += record.growth.Evaluate(age);
                GrowthPerSecond += record.growth.Rate(age);
            }
            if (RawThreat >= Capacity)
            {
                State = Phase.GameOver;
                previousTimeScale = Time.timeScale;
                ownsPause = true;
                playerWasEnabled = playerInput && playerInput.enabled;
                cameraWasEnabled = cameraInput && cameraInput.enabled;
                if (playerInput) playerInput.enabled = false;
                if (cameraInput) cameraInput.enabled = false;
                Time.timeScale = 0f;
                GameOver?.Invoke();
            }
            else State = !surge.IsComplete ? Phase.Surging : surge.ActiveSproutCount > 0 ? Phase.Clearing : Phase.Resolved;
            Changed?.Invoke();
        }

        public void RestartAttempt()
        {
            if (!IsGameOver || restarting) return;
            restarting = true;
            RestoreProgress();
            SceneManager.LoadScene(gameObject.scene.path);
        }

        void RestoreProgress()
        {
            if (!ownsPause) return;
            ownsPause = false;
            Time.timeScale = previousTimeScale;
            if (playerInput) playerInput.enabled = playerWasEnabled;
            if (cameraInput) cameraInput.enabled = cameraWasEnabled;
        }

        void OnDestroy() { RestoreProgress(); }
    }
}
