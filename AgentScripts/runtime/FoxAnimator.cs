using UnityEngine;

namespace B612
{
    /// <summary>Procedural trot, leap, and sit for the fox's part-based model.</summary>
    public class FoxAnimator : MonoBehaviour
    {
        public GravityWalker walker;
        public Transform model;

        PartRig body, head, tail, fl, fr, bl, br;
        float phase, sit, airBlend, idleTime;

        void Awake()
        {
            if (!model) model = transform;
            body = PartRig.Find(model, "Body");
            head = PartRig.Find(model, "Head");
            tail = PartRig.Find(model, "Tail");
            fl = PartRig.Find(model, "Leg.FL");
            fr = PartRig.Find(model, "Leg.FR");
            bl = PartRig.Find(model, "Leg.BL");
            br = PartRig.Find(model, "Leg.BR");
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float t = Time.time;
            float speed01 = walker ? Mathf.Clamp01(walker.TangentSpeed / Mathf.Max(0.01f, walker.moveSpeed)) : 0f;
            bool air = walker && !walker.Grounded;
            airBlend = Mathf.MoveTowards(airBlend, air ? 1f : 0f, dt * 7f);
            idleTime = speed01 < 0.08f && !air ? idleTime + dt : 0f;
            sit = Mathf.MoveTowards(sit, idleTime > 0.6f ? 1f : 0f, dt * 3f);
            phase += dt * (3f + 13f * speed01);
            float s = Mathf.Sin(phase);
            float run = speed01 * (1f - airBlend);

            float amp = 42f * run;
            if (fl != null) fl.Pose(s * amp - 45f * airBlend + 10f * sit);
            if (br != null) br.Pose(s * amp + 40f * airBlend - 75f * sit);
            if (fr != null) fr.Pose(-s * amp - 40f * airBlend + 10f * sit);
            if (bl != null) bl.Pose(-s * amp + 45f * airBlend - 75f * sit);

            if (body != null)
            {
                body.Pose(-28f * sit + Mathf.Cos(phase * 2f) * 4f * run - 8f * airBlend);
                body.Offset(Vector3.up * (Mathf.Abs(Mathf.Sin(phase)) * 0.025f * run - 0.035f * sit));
            }
            if (head != null) head.Pose(22f * sit - 6f * run + Mathf.Sin(t * 1.3f) * 3f * (1f - run), Mathf.Sin(t * 0.7f) * 18f * sit);
            if (tail != null)
            {
                float wag = Mathf.Sin(t * (sit > 0.5f ? 3f : 10f)) * (sit > 0.5f ? 12f : 22f);
                tail.Pose(-20f * run + 15f * airBlend + 25f * sit, wag);
            }
        }
    }
}
