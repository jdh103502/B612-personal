using UnityEngine;

namespace B612
{
    /// <summary>
    /// Drives the B612/Night Sky skybox: keeps the painted moon opposite the moonlight's direction
    /// (rotate the light and the moon follows) and launches shooting stars inside the current view.
    /// </summary>
    [ExecuteAlways]
    public class NightSky : MonoBehaviour
    {
        public Light moonLight;
        public Vector2 meteorInterval = new Vector2(2.5f, 6.5f);
        public Vector2 meteorDuration = new Vector2(0.6f, 1.1f);
        [Tooltip("Arc a shooting star travels, in radians.")]
        public Vector2 meteorArc = new Vector2(0.07f, 0.15f);

        static readonly int MoonDirId = Shader.PropertyToID("_MoonDir");
        static readonly int SkyTimeId = Shader.PropertyToID("_NightSkyTime");
        static readonly int[] PosIds = { Shader.PropertyToID("_MeteorA"), Shader.PropertyToID("_MeteorB") };
        static readonly int[] DirIds = { Shader.PropertyToID("_MeteorADir"), Shader.PropertyToID("_MeteorBDir") };

        struct Streak { public Vector3 start, dir; public float t0, duration, arc; }
        readonly Streak[] streaks = new Streak[2];
        float nextLaunch;

        void OnEnable()
        {
            Clear();
            nextLaunch = Time.time + 1.2f;
        }

        void OnDisable() { Clear(); Shader.SetGlobalFloat(SkyTimeId, 0f); }

        void Clear()
        {
            for (int i = 0; i < streaks.Length; i++)
            {
                streaks[i] = default;
                Shader.SetGlobalVector(PosIds[i], new Vector4(0f, 1f, 0f, -1f));
                Shader.SetGlobalVector(DirIds[i], new Vector4(1f, 0f, 0f, 0f));
            }
        }

        void Update()
        {
            var sky = RenderSettings.skybox;
            if (moonLight && sky && sky.HasProperty(MoonDirId))
            {
                Vector4 md = -moonLight.transform.forward;
                if ((sky.GetVector(MoonDirId) - md).sqrMagnitude > 1e-8f) sky.SetVector(MoonDirId, md);
            }
            Shader.SetGlobalFloat(SkyTimeId, Application.isPlaying ? Time.time : 0f);
            if (!Application.isPlaying) return;

            float now = Time.time;
            if (now >= nextLaunch)
            {
                Launch(0f);
                nextLaunch = now + Random.Range(meteorInterval.x, meteorInterval.y);
            }
            for (int i = 0; i < streaks.Length; i++)
            {
                var s = streaks[i];
                float p = s.duration > 0f ? (now - s.t0) / s.duration : -1f;
                if (p >= 1f) p = -1f;
                Shader.SetGlobalVector(PosIds[i], new Vector4(s.start.x, s.start.y, s.start.z, p));
                Shader.SetGlobalVector(DirIds[i], new Vector4(s.dir.x, s.dir.y, s.dir.z, s.arc));
            }
        }

        /// <summary>Starts a shooting star in the upper part of the main camera's view.</summary>
        public void Launch(float startProgress)
        {
            var cam = Camera.main;
            if (!cam) return;
            float now = Time.time;
            int slot = now - streaks[0].t0 >= streaks[0].duration ? 0 : 1;
            float arc = Random.Range(meteorArc.x, meteorArc.y);
            Vector3 start = Vector3.zero, dir = Vector3.zero;
            // try a few spots and keep one whose whole path is open sky (not behind the house or a planet)
            for (int attempt = 0; attempt < 12; attempt++)
            {
                start = cam.ViewportPointToRay(new Vector3(Random.Range(0.12f, 0.88f), Random.Range(0.5f, 0.95f), 0f)).direction;
                var screenDir = new Vector3(Random.Range(-0.9f, 0.9f), -Random.Range(0.35f, 1f), 0f);
                dir = Vector3.ProjectOnPlane(cam.transform.TransformDirection(screenDir), start).normalized;
                var end = Quaternion.AngleAxis(arc * Mathf.Rad2Deg, Vector3.Cross(start, dir)) * start;
                var origin = cam.transform.position;
                if (!Physics.Raycast(origin, start, 300f, ~0, QueryTriggerInteraction.Ignore) &&
                    !Physics.Raycast(origin, end, 300f, ~0, QueryTriggerInteraction.Ignore)) break;
            }
            float duration = Random.Range(meteorDuration.x, meteorDuration.y);
            streaks[slot] = new Streak
            {
                start = start,
                dir = dir,
                t0 = now - startProgress * duration,
                duration = duration,
                arc = arc,
            };
        }
    }
}
