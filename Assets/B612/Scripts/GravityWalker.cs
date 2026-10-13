using System;
using UnityEngine;

namespace B612
{
    /// <summary>
    /// Rigidbody walker for small planets. Standing: "down" follows the ground normal, so the walker
    /// can circle an asteroid. Airborne: it falls toward the nearest GravitySource with weak, floaty gravity.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class GravityWalker : MonoBehaviour
    {
        [Header("Movement")]
        public float moveSpeed = 2.0f;
        public float acceleration = 12f;
        public float airAcceleration = 5f;
        public float turnSpeed = 10f;

        [Header("Jump")]
        public float jumpSpeed = 3.1f;
        public float coyoteTime = 0.12f;
        public float jumpBuffer = 0.15f;
        [Tooltip("Extra gravity while rising with the jump button released (short hops).")]
        public float shortHopGravity = 2.2f;

        [Header("Ground")]
        public float groundProbe = 0.12f;
        public float maxGroundAngle = 60f;
        public LayerMask groundMask = ~0;

        [NonSerialized] public Vector3 moveInput;
        [NonSerialized] public bool jumpHeld;
        [NonSerialized] public float speedMultiplier = 1f;

        public bool Grounded { get; private set; }
        public Vector3 Up { get; private set; } = Vector3.up;
        public GravitySource Source { get; private set; }
        public float TangentSpeed { get; private set; }
        public float VerticalSpeed { get; private set; }
        public float TimeWithoutGravity { get; private set; }
        /// <summary>True while something else (a launch star) carries the walker; physics and input are paused.</summary>
        public bool Suspended { get; private set; }

        public event Action Jumped;
        public event Action Landed;

        Rigidbody rb;
        CapsuleCollider capsule;
        float lastGrounded = -10f, jumpRequest = -10f;
        Vector3 groundNormal = Vector3.up;
        bool wasGrounded;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Up = transform.up;
            groundNormal = Up;
        }

        public void Jump() { jumpRequest = Time.time; }

        public void Teleport(Vector3 position, Vector3 up)
        {
            rb.position = position;
            transform.position = position;
            rb.linearVelocity = Vector3.zero;
            Up = up.normalized;
            groundNormal = Up;
            var fwd = Vector3.ProjectOnPlane(transform.forward, Up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(Vector3.forward, Up);
            rb.rotation = Quaternion.LookRotation(fwd.normalized, Up);
            transform.rotation = rb.rotation;
        }

        /// <summary>Hands the walker over to a scripted flight (on) or back to physics (off).</summary>
        public void Suspend(bool on)
        {
            if (Suspended == on) return;
            Suspended = on;
            if (on) rb.linearVelocity = Vector3.zero;
            rb.collisionDetectionMode = on ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.ContinuousDynamic;
            rb.isKinematic = on;
            rb.interpolation = on ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            Grounded = false;
            wasGrounded = false;
            TangentSpeed = 0f;
            VerticalSpeed = 0f;
            moveInput = Vector3.zero;
            if (!on) rb.linearVelocity = Vector3.zero;
        }

        /// <summary>Places a suspended walker somewhere along its flight.</summary>
        public void Carry(Vector3 position, Vector3 up, Quaternion rotation)
        {
            Up = up.normalized;
            groundNormal = Up;
            rb.position = position;
            rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
        }

        void FixedUpdate()
        {
            if (Suspended) { TimeWithoutGravity = 0f; return; }
            float dt = Time.fixedDeltaTime;
            Source = GravitySource.Find(rb.position);
            Vector3 pull = Source ? Source.Pull(rb.position) : Vector3.zero;
            TimeWithoutGravity = Source ? 0f : TimeWithoutGravity + dt;

            float radius = capsule ? capsule.radius * Mathf.Abs(transform.lossyScale.x) : 0.1f;
            Vector3 origin = rb.position + Up * (radius + 0.05f);
            Vector3 v = rb.linearVelocity;
            float vUp = Vector3.Dot(v, Up);

            RaycastHit hit;
            bool hitGround = Physics.SphereCast(origin, radius * 0.9f, -Up, out hit, 0.05f + groundProbe, groundMask, QueryTriggerInteraction.Ignore)
                             && hit.rigidbody != rb
                             && Vector3.Angle(hit.normal, Up) < maxGroundAngle;
            Grounded = hitGround && vUp < 0.6f;
            if (Grounded)
            {
                lastGrounded = Time.time;
                groundNormal = Vector3.Slerp(groundNormal, hit.normal, 1f - Mathf.Exp(-12f * dt)).normalized;
            }

            Vector3 targetUp = Grounded ? groundNormal : (pull.sqrMagnitude > 1e-6f ? -pull.normalized : Up);
            Up = Vector3.Slerp(Up, targetUp, 1f - Mathf.Exp(-(Grounded ? 14f : 4.5f) * dt)).normalized;

            vUp = Vector3.Dot(v, Up);
            Vector3 vTan = v - Up * vUp;
            Vector3 want = Vector3.ProjectOnPlane(moveInput, Up);
            if (want.sqrMagnitude > 1f) want.Normalize();
            want *= moveSpeed * speedMultiplier;
            vTan = Vector3.MoveTowards(vTan, want, (Grounded ? acceleration : airAcceleration) * dt);

            float g = pull.magnitude;
            if (Grounded)
            {
                vUp = Mathf.Max(Mathf.Min(vUp, 0f) - g * dt, -1.5f);
            }
            else
            {
                vUp -= g * dt;
                if (!jumpHeld && vUp > 0f) vUp -= g * shortHopGravity * dt;
            }

            if (Time.time - jumpRequest < jumpBuffer && Time.time - lastGrounded < coyoteTime)
            {
                vUp = jumpSpeed;
                jumpRequest = -10f;
                lastGrounded = -10f;
                Grounded = false;
                if (Jumped != null) Jumped();
            }

            rb.linearVelocity = vTan + Up * vUp;
            TangentSpeed = vTan.magnitude;
            VerticalSpeed = vUp;

            Vector3 face = vTan.sqrMagnitude > 0.02f ? vTan : Vector3.ProjectOnPlane(rb.rotation * Vector3.forward, Up);
            if (face.sqrMagnitude > 1e-6f)
            {
                var target = Quaternion.LookRotation(face.normalized, Up);
                rb.MoveRotation(Quaternion.Slerp(rb.rotation, target, 1f - Mathf.Exp(-turnSpeed * dt)));
            }

            if (Grounded && !wasGrounded && Landed != null) Landed();
            wasGrounded = Grounded;
        }
    }
}
