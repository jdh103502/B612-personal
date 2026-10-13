using UnityEngine;

namespace B612
{
    /// <summary>Procedural run, jump, and idle motion for the prince's part-based model, plus a fluttering scarf.</summary>
    public class PrinceAnimator : MonoBehaviour
    {
        public GravityWalker walker;
        public Transform model;

        PartRig hips, body, head, armL, armR, legL, legR;
        PartRig[] scarf;
        Vector3 baseScale;
        float phase, squash, airBlend;

        void Awake()
        {
            if (!model) model = transform;
            baseScale = model.localScale;
            hips = PartRig.Find(model, "Hips");
            body = PartRig.Find(model, "Body");
            head = PartRig.Find(model, "Head");
            armL = PartRig.Find(model, "Arm.L");
            armR = PartRig.Find(model, "Arm.R");
            legL = PartRig.Find(model, "Leg.L");
            legR = PartRig.Find(model, "Leg.R");
            scarf = new[] { PartRig.Find(model, "Scarf.1"), PartRig.Find(model, "Scarf.2"), PartRig.Find(model, "Scarf.3") };
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

        void OnJumped() { squash = 0.16f; }
        void OnLanded() { squash = -0.18f; }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float t = Time.time;
            float speed01 = walker ? Mathf.Clamp01(walker.TangentSpeed / Mathf.Max(0.01f, walker.moveSpeed)) : 0f;
            bool air = walker && !walker.Grounded;
            airBlend = Mathf.MoveTowards(airBlend, air ? 1f : 0f, dt * 6f);
            phase += dt * (2f + 10f * speed01);
            float s = Mathf.Sin(phase);
            float ground = 1f - airBlend;

            float legAmp = 40f * speed01 * ground;
            float armAmp = 32f * speed01 * ground;
            if (legL != null) legL.Pose(s * legAmp - 28f * airBlend);
            if (legR != null) legR.Pose(-s * legAmp + 18f * airBlend);
            if (armL != null) armL.Pose(-s * armAmp - 20f * airBlend, 0f, -70f * airBlend);
            if (armR != null) armR.Pose(s * armAmp - 20f * airBlend, 0f, 70f * airBlend);

            float bob = Mathf.Abs(Mathf.Cos(phase)) * 0.022f * speed01 * ground + Mathf.Sin(t * 2.2f) * 0.004f * (1f - speed01);
            if (hips != null) hips.Offset(Vector3.up * bob);
            if (body != null) body.Pose(7f * speed01 * ground - 6f * airBlend, Mathf.Sin(phase) * 4f * speed01);
            if (head != null) head.Pose(-5f * speed01 + Mathf.Sin(t * 1.7f) * 2f + 8f * airBlend, Mathf.Sin(t * 0.6f) * 6f * (1f - speed01));

            float flutter = 10f + 22f * speed01 + 14f * airBlend;
            for (int i = 0; i < scarf.Length; i++)
            {
                if (scarf[i] == null) continue;
                float w = Mathf.Sin(t * (5f + 4f * speed01) - i * 1.1f);
                scarf[i].Pose(w * flutter * 0.35f - 8f * speed01, w * flutter, Mathf.Cos(t * 3f - i) * 6f);
            }

            squash = Mathf.Lerp(squash, 0f, 1f - Mathf.Exp(-9f * dt));
            if (air && walker.VerticalSpeed > 0f) squash = Mathf.Max(squash, 0.05f * Mathf.Clamp01(walker.VerticalSpeed / 3f));
            model.localScale = new Vector3(baseScale.x * (1f - squash * 0.5f), baseScale.y * (1f + squash), baseScale.z * (1f - squash * 0.5f));
        }
    }
}
