using System.Collections;
using UnityEngine;

namespace B612
{
    /// <summary>
    /// Launch star (after Mario Galaxy): stand in it and press jump to be flung along an arc to the landing point.
    /// The fox rides the same arc a moment later. Uses a trigger collider to know when the prince is standing in it.
    /// </summary>
    public class LaunchStar : MonoBehaviour
    {
        public Transform landing;           // end of the flight; its up axis is the "up" on arrival
        public Transform visual;            // the star and ring, spun and pulsed
        public float flightTime = 2.6f;
        public float arcHeight = 6f;
        public float foxDelay = 0.3f;

        public bool Busy { get; private set; }

        Vector3 visualScale;

        void Start() { if (visual) visualScale = visual.localScale; }

        void Update()
        {
            if (!visual) return;
            float rate = Busy ? 720f : 70f;
            visual.Rotate(Vector3.up, rate * Time.deltaTime, Space.Self);
            visual.localScale = visualScale * (1f + Mathf.Sin(Time.time * 3f) * 0.05f);
        }

        void OnTriggerEnter(Collider other)
        {
            var pc = other.GetComponentInParent<PlayerController>();
            if (pc) pc.NearbyLaunch = this;
        }

        void OnTriggerExit(Collider other)
        {
            var pc = other.GetComponentInParent<PlayerController>();
            if (pc && pc.NearbyLaunch == this) pc.NearbyLaunch = null;
        }

        public void Launch(GravityWalker prince)
        {
            if (Busy || !landing) return;
            StartCoroutine(Fly(prince, 0f, Vector3.zero, true));
            var fox = FindFirstObjectByType<FoxFollower>();
            if (fox) StartCoroutine(Fly(fox.GetComponent<GravityWalker>(), foxDelay, -landing.right * 0.7f - landing.forward * 0.3f, false));
        }

        IEnumerator Fly(GravityWalker w, float delay, Vector3 offset, bool owner)
        {
            if (owner) Busy = true;
            w.Suspend(true);
            // wind-up: a short crouch while the star spins up
            float wait = 0.35f + delay;
            var p0 = w.transform.position;
            var up0 = w.Up;
            var fwd0 = Vector3.ProjectOnPlane(w.transform.forward, up0).normalized;
            for (float t = 0f; t < wait; t += Time.deltaTime)
            {
                w.Carry(p0 - up0 * 0.05f * Mathf.Sin(Mathf.Clamp01(t / 0.35f) * Mathf.PI), up0, w.transform.rotation);
                yield return null;
            }

            var p3 = landing.position + offset;
            var up3 = landing.up;
            var p1 = p0 + up0 * arcHeight;
            var p2 = p3 + up3 * arcHeight * 0.85f;
            for (float t = 0f; t < 1f; )
            {
                t = Mathf.Min(1f, t + Time.deltaTime / flightTime);
                float s = 1f - Mathf.Pow(1f - t, 1.8f);                 // fast take-off, gentle arrival
                var pos = Bezier(p0, p1, p2, p3, s);
                var tan = Bezier(p0, p1, p2, p3, Mathf.Min(1f, s + 0.01f)) - pos;
                var up = Vector3.Slerp(up0, up3, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.85f, t))).normalized;
                // face along the flight, then turn to the landing's forward as the prince comes down
                var arrive = Vector3.ProjectOnPlane(landing.forward, up).normalized;
                var travel = Vector3.ProjectOnPlane(tan, up);
                var heading = travel.sqrMagnitude > 1e-8f
                    ? Vector3.Slerp(travel.normalized, arrive, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.95f, t)))
                    : arrive;
                if (heading.sqrMagnitude < 1e-6f) heading = Vector3.ProjectOnPlane(fwd0, up);
                float spin = 1080f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.7f));   // three pirouettes, ending facing forward
                w.Carry(pos, up, Quaternion.LookRotation(heading.normalized, up) * Quaternion.Euler(0f, spin, 0f));
                yield return null;
            }

            w.Suspend(false);
            w.Teleport(p3, up3);
            if (owner) Busy = false;
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }
    }
}
