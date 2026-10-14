using UnityEngine;

namespace B612
{
    /// <summary>
    /// Drives the rigged prince's Animator (idle / walk / run blend and an air pose) from the gravity walker,
    /// and layers small procedural touches on top: breathing, glancing at the fox, squash and stretch on
    /// jumps and landings, and a lean into turns.
    /// </summary>
    public class PrinceCharacterAnimator : MonoBehaviour
    {
        public GravityWalker walker;
        public Animator animator;
        [Tooltip("The visual's root under the player; it gets the squash and the lean.")]
        public Transform visual;
        [Tooltip("Ground speed (m/s) at which the walk and run clips play at their natural rate.")]
        public float walkSpeed = 0.5f;
        public float runSpeed = 1.55f;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int GroundedId = Animator.StringToHash("Grounded");

        Transform spine, chest, head;
        [Header("Scarf")]
        [Tooltip("Pull back toward the scarf's sculpted shape, at the knot and at the tip.")]
        public float scarfStiffnessRoot = 70f, scarfStiffnessTip = 9f;
        public float scarfGravity = 1.6f;
        [Tooltip("How strongly moving through the air drags the scarf backwards.")]
        public float scarfDrag = 2.4f;
        public float scarfWind = 1.4f;
        [Range(0.8f, 1f)] public float scarfDamping = 0.93f;

        Transform[] scarf;
        Quaternion[] scarfRest;
        Transform scarfEnd;
        Vector3[] sp, spPrev;
        float[] segLen;
        Vector3 lastRoot;
        float simAccum;
        bool simReady;
        Transform fox;
        Vector3 baseScale;
        Quaternion baseRotation;
        float squash, lean, headYaw, lastYaw;
        bool hasLastYaw;

        void Awake()
        {
            if (!visual) visual = transform;
            baseScale = visual.localScale;
            baseRotation = visual.localRotation;
            spine = FindDeep(visual, "Spine01");
            chest = FindDeep(visual, "Spine");
            head = FindDeep(visual, "Head");
            var chain = new System.Collections.Generic.List<Transform>();
            for (int i = 1; ; i++) { var b = FindDeep(visual, "Scarf." + i); if (!b) break; chain.Add(b); }
            scarfEnd = FindDeep(visual, "Scarf.End");
            if (chain.Count > 0 && scarfEnd)
            {
                scarf = chain.ToArray();
                scarfRest = new Quaternion[scarf.Length];
                for (int i = 0; i < scarf.Length; i++) scarfRest[i] = scarf[i].localRotation;
            }
            var f = GameObject.Find("Fox");
            if (f) fox = f.transform;
        }

        void OnEnable()
        {
            if (!walker) return;
            walker.Jumped += OnJumped;
            walker.Landed += OnLanded;
        }

        void OnDisable()
        {
            if (!walker) return;
            walker.Jumped -= OnJumped;
            walker.Landed -= OnLanded;
        }

        void OnJumped() { squash = 0.12f; }
        void OnLanded() { squash = -0.14f; }

        void Update()
        {
            if (!walker || !animator) return;
            float speed = walker.Suspended ? 0f : walker.TangentSpeed;
            animator.SetFloat(SpeedId, speed, 0.08f, Time.deltaTime);
            animator.SetBool(GroundedId, walker.Grounded && !walker.Suspended);
            // above the run clip's natural speed, play it faster so the feet keep up with the ground
            animator.speed = speed > runSpeed ? Mathf.Min(speed / runSpeed, 1.6f) : 1f;
        }

        void LateUpdate()
        {
            if (!walker) return;
            float dt = Time.deltaTime;
            float t = Time.time;
            float speed = walker.Suspended ? 0f : walker.TangentSpeed;
            float idle = walker.Grounded ? 1f - Mathf.Clamp01(speed / Mathf.Max(walkSpeed, 0.01f)) : 0f;
            var up = walker.Up;
            var right = transform.right;

            // breathing: the chest rises and falls a little while standing
            float breath = Mathf.Sin(t * 2.1f) * idle;
            if (spine) spine.rotation = Quaternion.AngleAxis(breath * 1.6f, right) * spine.rotation;
            if (chest) chest.rotation = Quaternion.AngleAxis(breath * 1.2f, right) * chest.rotation;

            // the head glances at the fox when it is close, otherwise looks around slowly
            float wantYaw = Mathf.Sin(t * 0.45f) * 14f + Mathf.Sin(t * 1.3f) * 4f;
            if (fox)
            {
                var to = Vector3.ProjectOnPlane(fox.position - transform.position, up);
                if (to.magnitude < 3f && to.magnitude > 0.05f)
                    wantYaw = Mathf.Clamp(Vector3.SignedAngle(transform.forward, to, up), -55f, 55f);
            }
            headYaw = Mathf.Lerp(headYaw, wantYaw * idle, 1f - Mathf.Exp(-3f * dt));
            if (head) head.rotation = Quaternion.AngleAxis(headYaw, up) * Quaternion.AngleAxis(-breath * 1.5f, right) * head.rotation;

            if (scarf != null) SimulateScarf(dt, t, speed);

            // lean into turns while running
            float yaw = transform.eulerAngles.y;
            float yawRate = hasLastYaw ? Mathf.DeltaAngle(lastYaw, yaw) / Mathf.Max(dt, 1e-4f) : 0f;
            lastYaw = yaw; hasLastYaw = true;
            float wantLean = Mathf.Clamp(-yawRate * 0.05f, -12f, 12f) * Mathf.Clamp01(speed / Mathf.Max(runSpeed, 0.01f));
            lean = Mathf.Lerp(lean, wantLean, 1f - Mathf.Exp(-6f * dt));

            // squash and stretch
            squash = Mathf.Lerp(squash, 0f, 1f - Mathf.Exp(-9f * dt));
            if (!walker.Grounded && walker.VerticalSpeed > 0f) squash = Mathf.Max(squash, 0.04f * Mathf.Clamp01(walker.VerticalSpeed / 3f));
            visual.localScale = new Vector3(baseScale.x * (1f - squash * 0.5f), baseScale.y * (1f + squash), baseScale.z * (1f - squash * 0.5f));
            visual.localRotation = baseRotation * Quaternion.Euler(0f, 0f, lean);
        }

        // The scarf tail is a little chain of points (one per bone, plus the end) moved with Verlet integration:
        // springs pull it back toward its sculpted shape, weak gravity tugs it down, moving through the air drags it
        // back, and a drifting wind makes it ripple. The bones are then turned to follow the points.
        void SimulateScarf(float dt, float t, float speed)
        {
            int n = scarf.Length;
            for (int j = 0; j < n; j++) scarf[j].localRotation = scarfRest[j];
            System.Func<int, Vector3> rest = k => k < n ? scarf[k].position : scarfEnd.position;

            var root = rest(0);
            if (!simReady || (root - lastRoot).sqrMagnitude > 1f)
            {
                sp = new Vector3[n + 1]; spPrev = new Vector3[n + 1]; segLen = new float[n];
                for (int k = 0; k <= n; k++) sp[k] = spPrev[k] = rest(k);
                for (int k = 0; k < n; k++) segLen[k] = Vector3.Distance(rest(k), rest(k + 1));
                lastRoot = root;
                simReady = true;
            }
            var bodyVel = (root - lastRoot) / Mathf.Max(dt, 1e-4f);
            lastRoot = root;

            var restPts = new Vector3[n + 1];
            for (int k = 0; k <= n; k++) restPts[k] = rest(k);
            var up = walker.Up;
            var right = transform.right;
            var fwd = transform.forward;
            float sp01 = Mathf.Clamp01(speed / Mathf.Max(runSpeed, 0.01f));
            const float h = 1f / 60f;
            simAccum = Mathf.Min(simAccum + dt, 4f * h);
            while (simAccum >= h)
            {
                simAccum -= h;
                sp[0] = restPts[0];
                for (int k = 1; k <= n; k++)
                {
                    float along = (k - 1f) / Mathf.Max(n - 1, 1);
                    float stiff = Mathf.Lerp(scarfStiffnessRoot, scarfStiffnessTip, along);
                    // wind: slow drifting gusts that travel down the scarf, plus a quick flap toward the tip at speed
                    float ph = t * 0.8f - k * 0.18f;
                    var wind = (right * (Mathf.PerlinNoise(ph, 1.3f) - 0.5f) + up * (Mathf.PerlinNoise(ph, 7.7f) - 0.5f) * 0.6f
                                + fwd * (Mathf.PerlinNoise(ph, 4.2f) - 0.5f) * 0.5f) * (2f * scarfWind)
                               + right * Mathf.Sin(t * 13f - k * 1.1f) * (0.4f + 1.6f * sp01) * along;
                    var acc = -up * scarfGravity + wind - bodyVel * scarfDrag * along + (restPts[k] - sp[k]) * stiff;
                    var vel = (sp[k] - spPrev[k]) * scarfDamping;
                    spPrev[k] = sp[k];
                    sp[k] += vel + acc * (h * h);
                }
                // keep every segment its own length
                for (int it = 0; it < 3; it++)
                    for (int k = 1; k <= n; k++)
                    {
                        var d = sp[k] - sp[k - 1];
                        float len = d.magnitude;
                        if (len > 1e-6f) sp[k] = sp[k - 1] + d * (segLen[k - 1] / len);
                    }
            }

            // turn each bone so its child lands on the simulated point
            for (int k = 0; k < n; k++)
            {
                var cur = (k + 1 < n ? scarf[k + 1].position : scarfEnd.position) - scarf[k].position;
                var want = sp[k + 1] - scarf[k].position;
                if (cur.sqrMagnitude > 1e-10f && want.sqrMagnitude > 1e-10f)
                    scarf[k].rotation = Quaternion.FromToRotation(cur, want) * scarf[k].rotation;
            }
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r) return r;
            }
            return null;
        }
    }
}
