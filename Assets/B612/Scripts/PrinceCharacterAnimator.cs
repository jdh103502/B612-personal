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
        Transform[] scarf;
        Quaternion[] scarfRest;
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
            scarf = new[] { FindDeep(visual, "Scarf.1"), FindDeep(visual, "Scarf.2"), FindDeep(visual, "Scarf.3") };
            if (scarf[0] && scarf[1] && scarf[2]) scarfRest = new[] { scarf[0].localRotation, scarf[1].localRotation, scarf[2].localRotation };
            else scarf = null;
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

            // the scarf tail flutters: a travelling wave that grows with speed and in the air
            if (scarf != null)
            {
                float sp01 = Mathf.Clamp01(speed / Mathf.Max(runSpeed, 0.01f));
                float gust = 0.6f + 0.4f * Mathf.Sin(t * 0.9f) * Mathf.Sin(t * 1.7f + 1f);
                float amp = (5f + 15f * sp01 + (walker.Grounded ? 0f : 10f)) * gust;
                float freq = 5f + 7f * sp01;
                for (int j = 0; j < scarf.Length; j++)
                {
                    scarf[j].localRotation = scarfRest[j];
                    float a1 = Mathf.Sin(t * freq - j * 1.3f) * amp * (0.6f + 0.35f * j);
                    float a2 = Mathf.Sin(t * freq * 0.7f - j * 0.9f + 2f) * amp * 0.5f;
                    scarf[j].rotation = Quaternion.AngleAxis(a1, up) * Quaternion.AngleAxis(a2, right) * scarf[j].rotation;
                }
            }

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
