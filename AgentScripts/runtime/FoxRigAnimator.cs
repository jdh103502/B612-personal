using System.Collections.Generic;
using UnityEngine;

namespace B612
{
    /// <summary>
    /// Procedural animation for the rigged fox: a diagonal trot with bending knees, a bobbing body and flexing spine,
    /// a tail that waves down its four joints, twitching ears, a stretched leap, and sitting down to watch the prince
    /// when it stops. Every frame the bones go back to their rest pose and rotations are layered on in world space,
    /// around the fox's own right / up / forward axes, so it does not depend on how the bones' local axes were exported.
    /// </summary>
    public class FoxRigAnimator : MonoBehaviour
    {
        public GravityWalker walker;
        public Transform model;
        [Tooltip("Who the fox watches while sitting (the prince).")]
        public Transform watch;

        Transform hips, spine, chest, neck, head, earL, earR;
        readonly Transform[] tail = new Transform[4];
        Transform fl1, fl2, fr1, fr2, bl1, bl2, br1, br2;
        readonly Dictionary<Transform, Quaternion> rest = new Dictionary<Transform, Quaternion>();
        Vector3 hipsRest, up = Vector3.up;
        float phase, sit, airBlend, idleTime, lookYaw, twitchAt, lastTwitch = -10f, twitchSide = 1f, scale = 1f;

        void Awake() { Init(); }

        void Init()
        {
            if (rest.Count > 0) return;
            if (!model) model = transform;
            hips = Find("Hips"); spine = Find("Spine"); chest = Find("Chest"); neck = Find("Neck"); head = Find("Head");
            earL = Find("Ear.L"); earR = Find("Ear.R");
            for (int i = 0; i < 4; i++) tail[i] = Find("Tail." + (i + 1));
            fl1 = Find("Leg.FL.1"); fl2 = Find("Leg.FL.2"); fr1 = Find("Leg.FR.1"); fr2 = Find("Leg.FR.2");
            bl1 = Find("Leg.BL.1"); bl2 = Find("Leg.BL.2"); br1 = Find("Leg.BR.1"); br2 = Find("Leg.BR.2");
            foreach (var b in new[] { hips, spine, chest, neck, head, earL, earR, fl1, fl2, fr1, fr2, bl1, bl2, br1, br2 }) if (b) rest[b] = b.localRotation;
            foreach (var b in tail) if (b) rest[b] = b.localRotation;
            if (hips) hipsRest = hips.localPosition;
            scale = model.lossyScale.y;
            twitchAt = Time.time + 2f;
        }

        Transform Find(string n) { return FindDeep(model, n); }

        void LateUpdate()
        {
            if (!walker || !hips) return;
            float dt = Time.deltaTime, t = Time.time;
            float speed01 = walker.Suspended ? 0f : Mathf.Clamp01(walker.TangentSpeed / Mathf.Max(0.01f, walker.moveSpeed));
            bool air = !walker.Grounded;
            airBlend = Mathf.MoveTowards(airBlend, air ? 1f : 0f, dt * 7f);
            idleTime = speed01 < 0.08f && !air ? idleTime + dt : 0f;
            sit = Mathf.MoveTowards(sit, idleTime > 0.6f ? 1f : 0f, dt * 2.5f);
            phase += dt * (3f + 14f * speed01);
            up = walker.Up;

            // the head looks at the prince while sitting, otherwise glances around
            float sitE = sit * sit * (3f - 2f * sit);
            float want = Mathf.Sin(t * 0.6f) * 12f * (1f - speed01);
            if (watch && sitE > 0.05f)
            {
                var to = Vector3.ProjectOnPlane(watch.position - transform.position, up);
                if (to.sqrMagnitude > 0.01f) want = Mathf.Clamp(Vector3.SignedAngle(transform.forward, to, up), -60f, 60f);
            }
            lookYaw = Mathf.Lerp(lookYaw, want, 1f - Mathf.Exp(-3f * dt));

            if (t > twitchAt) { lastTwitch = t; twitchAt = t + Random.Range(1.5f, 4.5f); twitchSide = Random.value < 0.5f ? -1f : 1f; }
            float flick = Mathf.Exp(-Mathf.Pow((t - lastTwitch - 0.1f) * 9f, 2f));

            Apply(speed01 * (1f - airBlend), speed01, airBlend, sitE, phase, t, lookYaw, flick);
        }

        /// <summary>Poses the fox directly (edit-mode previews): run 0..1, air 0..1, sit 0..1, gait phase in radians.</summary>
        public void PreviewPose(float run, float air, float sit01, float gaitPhase)
        {
            Init();
            up = transform.up;
            Apply(run, run, air, sit01, gaitPhase, 0.3f, 0f, 0f);
        }

        void Apply(float run, float speed01, float airBlend, float sitE, float phase, float t, float lookYaw, float flick)
        {
            foreach (var kv in rest) kv.Key.localRotation = kv.Value;
            hips.localPosition = hipsRest;
            float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
            var right = transform.right;

            // body: a bob and a gentle rock while trotting; when sitting the hips drop and the front rises
            hips.position += up * ((Mathf.Abs(s) * 0.018f * run - 0.07f * sitE) * scale);
            Pitch(hips, Mathf.Cos(phase * 2f) * 3f * run + 28f * sitE + 6f * airBlend);
            Pitch(spine, Mathf.Sin(phase * 2f) * 3f * run);
            Pitch(chest, -Mathf.Cos(phase * 2f) * 2f * run);

            // legs: diagonal pairs swing together; knees bend as each paw comes forward
            float amp = 34f * run;
            float liftA = Mathf.Max(0f, c) * run, liftB = Mathf.Max(0f, -c) * run;
            Leg(fl1, fl2, s * amp + 45f * airBlend - 28f * sitE, -45f * liftA - 25f * airBlend);
            Leg(br1, br2, s * amp - 40f * airBlend + 60f * sitE, 40f * liftA + 20f * airBlend - 105f * sitE);
            Leg(fr1, fr2, -s * amp + 45f * airBlend - 28f * sitE, -45f * liftB - 25f * airBlend);
            Leg(bl1, bl2, -s * amp - 40f * airBlend + 60f * sitE, 40f * liftB + 20f * airBlend - 105f * sitE);

            // head and neck
            Turn(neck, lookYaw * 0.4f);
            Turn(head, lookYaw * 0.6f);
            Pitch(neck, -12f * sitE + 4f * run);
            Pitch(head, -Mathf.Cos(phase * 2f) * 3f * run + 10f * sitE + Mathf.Sin(t * 1.3f) * 2f * (1f - run) - 8f * airBlend);
            Tilt(head, Mathf.Sin(t * 0.8f) * 6f * sitE);

            // ears: perked, with a quick flick now and then
            Tilt(earL, 6f * run + (twitchSide > 0f ? 22f * flick : 0f));
            Tilt(earR, -6f * run - (twitchSide < 0f ? 22f * flick : 0f));
            Pitch(earL, -10f * run);
            Pitch(earR, -10f * run);

            // tail: a wave travelling to the tip; raised when running, curled round the body when sitting
            float freq = sitE > 0.5f ? 2.6f : 7f + 5f * speed01;
            float wag = sitE > 0.5f ? 10f : 16f + 10f * speed01;
            for (int i = 0; i < 4; i++)
            {
                if (!tail[i]) continue;
                float k = (i + 1) / 4f;
                float raise = i == 0 ? -12f * run + 18f * airBlend - 20f * sitE : -4f * run;
                tail[i].rotation = Quaternion.AngleAxis(Mathf.Sin(t * freq - i * 0.7f) * wag * (0.5f + k) + 22f * sitE, up)
                                   * Quaternion.AngleAxis(raise, right) * tail[i].rotation;
            }
        }

        // positive = nose up / paw forward
        void Pitch(Transform b, float deg) { if (b) b.rotation = Quaternion.AngleAxis(-deg, transform.right) * b.rotation; }
        void Turn(Transform b, float deg) { if (b) b.rotation = Quaternion.AngleAxis(deg, up) * b.rotation; }
        void Tilt(Transform b, float deg) { if (b) b.rotation = Quaternion.AngleAxis(deg, transform.forward) * b.rotation; }
        void Leg(Transform upper, Transform lower, float swing, float bend) { Pitch(upper, swing); Pitch(lower, bend); }

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
