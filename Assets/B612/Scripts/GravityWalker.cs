using System;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        [Tooltip("Small outward contact impulses tolerated while already supported. Explicit jumps bypass ground following.")]
        [Min(0f)] public float groundFollowSeparationSpeed = .5f;

        [NonSerialized] public Vector3 moveInput;
        [NonSerialized] public bool jumpHeld;
        [NonSerialized] public float speedMultiplier = 1f;

        public bool Grounded { get; private set; }
        public Vector3 Up { get; private set; } = Vector3.up;
        public GravitySource Source { get; private set; }
        public float TangentSpeed { get; private set; }
        public float VerticalSpeed { get; private set; }
        public float TimeWithoutGravity { get; private set; }
        // Physics support is independent of the smoothed visual Up and landing/resource confirmation.
        public bool HasSupport { get; private set; }
        public Vector3 MovementUp { get; private set; } = Vector3.up;
        public float SupportNormalVelocity { get; private set; }
        public float RadialSpeed { get; private set; }
        /// <summary>True while something else (a launch star) carries the walker; physics and input are paused.</summary>
        public bool Suspended { get; private set; }

        public event Action Jumped;
        public event Action Landed;

        Rigidbody rb;
        CapsuleCollider capsule;
        float lastGrounded = -10f, jumpRequest = -10f;
        Vector3 groundNormal = Vector3.up;
        bool wasGrounded;
        PlayerTraversalAbilities traversal;
        public PlayerTraversalAbilities Traversal => traversal;
        readonly RaycastHit[] traversalHits = new RaycastHit[32];
        readonly Collider[] traversalOverlaps = new Collider[32];
        PhysicsScene physicsScene;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            rb.useGravity = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Up = transform.up;
            MovementUp = Up;
            groundNormal = Up;
            traversal = GetComponent<PlayerTraversalAbilities>();
        }

        internal void BindTraversal(PlayerTraversalAbilities abilities) { traversal = abilities; }
        public void Jump()
        {
            if (traversal && traversal.isActiveAndEnabled) traversal.RequestJump();
            else jumpRequest = Time.time;
        }
        public void RequestDash() { if (traversal && traversal.isActiveAndEnabled) traversal.RequestDash(); }
        public void SetTraversalInputEnabled(bool on) { if (traversal) traversal.SetInputEnabled(on); }
        public void ClearInput()
        {
            moveInput = Vector3.zero; jumpHeld = false; jumpRequest = -10f;
            if (traversal) traversal.CancelInputAndActions();
        }
        public void ClearPendingRequests()
        {
            jumpRequest = -10f;
            if (traversal) traversal.ClearPendingRequests();
        }

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
            Grounded = wasGrounded = false;
            Source = GravitySource.Find(position);
            HasSupport = false; MovementUp = Up;
            SupportNormalVelocity = RadialSpeed = 0f;
            TangentSpeed = VerticalSpeed = TimeWithoutGravity = 0f;
            lastGrounded = jumpRequest = -10f;
            ClearInput();
            if (traversal) traversal.ResetForTeleport();
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
            HasSupport = false;
            TangentSpeed = 0f;
            VerticalSpeed = 0f;
            moveInput = Vector3.zero;
            ClearInput(); lastGrounded = -10f;
            if (traversal) traversal.Suspend();
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
            if (traversal && traversal.isActiveAndEnabled) { TickTraversal(Time.fixedDeltaTime); return; }
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

        public void GetWorldCapsule(out Vector3 a, out Vector3 b, out float radius)
        {
            var scale = transform.lossyScale;
            int axis = capsule ? capsule.direction : 1;
            Vector3 localAxis = axis == 0 ? Vector3.right : axis == 2 ? Vector3.forward : Vector3.up;
            float along = Mathf.Abs(scale[axis]);
            float across = Mathf.Max(Mathf.Abs(scale[(axis + 1) % 3]), Mathf.Abs(scale[(axis + 2) % 3]));
            radius = capsule ? capsule.radius * across : .1f;
            float height = capsule ? Mathf.Max(capsule.height * along, radius * 2f) : radius * 2f;
            Vector3 centre = rb.position + rb.rotation * Vector3.Scale(capsule ? capsule.center : Vector3.zero, scale);
            Vector3 half = rb.rotation * localAxis * (height * .5f - radius);
            a = centre + half; b = centre - half;
        }
        bool OwnCollider(Collider col) => !col || col.isTrigger || col.attachedRigidbody == rb || col.transform.IsChildOf(transform);

        bool TraversalSupport(Vector3 gravityUp, Vector3 probeUp, out RaycastHit support)
        {
            GetWorldCapsule(out var a, out var b, out float radius);
            Vector3 bottom = Vector3.Dot(a, probeUp) < Vector3.Dot(b, probeUp) ? a : b;
            float margin = .04f;
            int count = physicsScene.SphereCast(bottom + probeUp * margin, radius * .9f, -probeUp,
                traversalHits, groundProbe + radius * .1f + margin, groundMask, QueryTriggerInteraction.Ignore);
            support = default; float closest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = traversalHits[i];
                if (OwnCollider(hit.collider) || Vector3.Angle(hit.normal, gravityUp) > maxGroundAngle || hit.distance >= closest) continue;
                closest = hit.distance; support = hit;
            }
            return closest < float.PositiveInfinity;
        }
        internal void TickTraversal(float dt)
        {
            if (Suspended || dt <= 0f) return;
            physicsScene = gameObject.scene.GetPhysicsScene();
            Source = GravitySource.Find(rb.position);
            Vector3 pull = Source ? Source.Pull(rb.position) : Vector3.zero;
            TimeWithoutGravity = Source ? 0f : TimeWithoutGravity + dt;
            Vector3 gravityUp = pull.sqrMagnitude > .000001f ? -pull.normalized : Up;
            Vector3 velocity = rb.linearVelocity;
            RadialSpeed = Vector3.Dot(velocity, gravityUp);
            traversal.UpdateJumpAscent(RadialSpeed);
            bool support = TraversalSupport(gravityUp, gravityUp, out var hit);
            // At triangle seams try the previous support direction too, but require a real nearby hit.
            // There is no timer that grants physical grounding after walking off a ledge.
            if (!support && wasGrounded && !traversal.BlocksGroundSupport)
                support = TraversalSupport(gravityUp, MovementUp, out hit);
            HasSupport = support;
            Vector3 supportVelocity = support && hit.rigidbody ? hit.rigidbody.GetPointVelocity(hit.point) : Vector3.zero;
            Vector3 relativeVelocity = velocity - supportVelocity;
            SupportNormalVelocity = support ? Vector3.Dot(relativeVelocity, hit.normal) : 0f;
            // PhysX can add a small separation impulse where the capsule rolls over a convex edge.
            // This tolerance applies only to an already supported walker, with no jump in progress.
            bool followingSurface = wasGrounded && (Vector3.Dot(relativeVelocity, MovementUp) <= groundFollowSeparationSpeed
                || SupportNormalVelocity <= groundFollowSeparationSpeed);
            Grounded = support && !traversal.BlocksGroundSupport && (SupportNormalVelocity <= .15f || followingSurface);
            MovementUp = Grounded ? hit.normal : gravityUp;
            if (Grounded) groundNormal = Vector3.Slerp(groundNormal, hit.normal, 1f - Mathf.Exp(-12f * dt)).normalized;
            Up = Grounded ? Vector3.Slerp(Up, groundNormal, 1f - Mathf.Exp(-14f * dt)).normalized : gravityUp;
            bool landed = traversal.BeginTick(dt, Grounded, MovementUp);
            if (landed) Landed?.Invoke();
            Vector3 movementVelocity = Grounded ? relativeVelocity : velocity;
            float vertical = Vector3.Dot(movementVelocity, MovementUp);
            Vector3 tangent = movementVelocity - MovementUp * vertical;
            traversal.ClampResidual(ref tangent);
            int jump = traversal.TryJump(Grounded, ref tangent, ref vertical);
            if (jump != 0)
            {
                Grounded = false;
                // Jump ascent uses the gravity frame immediately; visual alignment can keep smoothing.
                tangent = Vector3.ProjectOnPlane(tangent, gravityUp);
                MovementUp = gravityUp;
            }
            if (jump == 1) Jumped?.Invoke();
            traversal.TryStartDash(Grounded, MovementUp);
            bool dashTick = traversal.ApplyDash(dt, MovementUp, out var dashVelocity);
            float glideTime = traversal.GlideTime(dt, vertical, Grounded, pull.sqrMagnitude > .000001f, jump != 0, dashTick);
            if (dashTick) tangent = dashVelocity;
            else
            {
                var input = Vector3.ProjectOnPlane(moveInput, MovementUp);
                if (input.sqrMagnitude > .000001f) input = input.normalized * Mathf.Min(1f, moveInput.magnitude);
                if (glideTime > 0f) tangent = Vector3.MoveTowards(tangent, input * traversal.glideMoveSpeed, traversal.glideSteeringAcceleration * glideTime);
                float normalTime = dt - glideTime;
                float terrainFactor=Grounded&&Source&&Source.terrainPlan!=null?TerrainWater.MovementFactor(Source.terrainPlan,gravityUp,Vector3.Distance(rb.position,Source.transform.position)-Source.terrainPlan.radius):1f;
                if (normalTime > 0f) tangent = Vector3.MoveTowards(tangent, input * moveSpeed * speedMultiplier * terrainFactor, (Grounded ? acceleration : airAcceleration) * normalTime);
            }
            if (jump == 0)
            {
                if (Grounded)
                {
                    // Counter curvature only while the dash still has real foot support; never magnetise a ledge.
                    float stick = pull.magnitude * dt;
                    if (dashTick && Source) stick += tangent.sqrMagnitude / Mathf.Max(.1f, Vector3.Distance(rb.position, Source.transform.position)) * dt;
                    vertical = Mathf.Max(Mathf.Min(vertical, 0f) - stick, -1.5f);
                }
                else
                {
                    if (glideTime > 0f) vertical = Mathf.Min(-traversal.glideMinimumFallSpeed,
                        Mathf.MoveTowards(vertical, -traversal.glideFallSpeed, traversal.glideVerticalAcceleration * glideTime));
                    float normalTime = dt - glideTime;
                    if (normalTime > 0f)
                    {
                        float multiplier = vertical <= 0f ? traversal.fallingGravityMultiplier
                            : 1f + (traversal.JumpAscentActive && !jumpHeld ? shortHopGravity : 0f);
                        vertical -= pull.magnitude * multiplier * normalTime;
                    }
                }
            }
            if (dashTick) SweepDash(ref tangent, ref vertical, dt, gravityUp);
            rb.linearVelocity = tangent + MovementUp * vertical + (Grounded ? supportVelocity : Vector3.zero);
            TangentSpeed = tangent.magnitude; VerticalSpeed = vertical;
            Vector3 face = Vector3.ProjectOnPlane(tangent.sqrMagnitude > .02f ? tangent : rb.rotation * Vector3.forward, Up);
            if (face.sqrMagnitude > .000001f) rb.MoveRotation(Quaternion.Slerp(rb.rotation, Quaternion.LookRotation(face.normalized, Up), 1f - Mathf.Exp(-turnSpeed * dt)));
            wasGrounded = Grounded;
            traversal.EndTick(dt);
        }
        void SweepDash(ref Vector3 tangent, ref float vertical, float dt, Vector3 gravityUp)
        {
            Vector3 velocity = tangent + MovementUp * vertical;
            float distance = velocity.magnitude * dt;
            if (distance < .000001f) return;
            Vector3 direction = velocity.normalized;
            GetWorldCapsule(out var a, out var b, out float radius);
            float safe = distance;
            // A capsule touching its floor can report an initial-overlap normal opposite the cast.
            // Inset by skin, then compensate along the hit normal; still sweep the same ground mesh for walls.
            float skin = Mathf.Max(.001f, traversal.dashCollisionSkin);
            float sweepRadius = Mathf.Max(.001f, radius - Mathf.Min(skin, radius * .1f));
            int count = physicsScene.CapsuleCast(a, b, sweepRadius, direction, traversalHits, distance + skin * 20f, groundMask, QueryTriggerInteraction.Ignore);
            float slopeCos = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
            for (int i = 0; i < count; i++)
            {
                var hit = traversalHits[i];
                if (OwnCollider(hit.collider) || Vector3.Dot(hit.normal, gravityUp) >= slopeCos || Vector3.Dot(direction, hit.normal) >= -.01f) continue;
                float margin = skin / Mathf.Max(.05f, -Vector3.Dot(direction, hit.normal));
                safe = Mathf.Min(safe, Mathf.Max(0f, hit.distance - margin));
            }
            // Preserve PhysX overlap resolution, but don't keep driving into an existing wall/ceiling overlap.
            count = physicsScene.OverlapCapsule(a, b, radius * .98f, traversalOverlaps, groundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var col = traversalOverlaps[i];
                if (OwnCollider(col) || !capsule) continue;
                if (Physics.ComputePenetration(capsule, rb.position, rb.rotation, col, col.transform.position, col.transform.rotation, out var away, out _)
                    && Vector3.Dot(away, gravityUp) < slopeCos && Vector3.Dot(direction, away) < -.01f) safe = 0f;
            }
            if (safe >= distance) return;
            float fraction = safe / distance;
            tangent *= fraction; vertical *= fraction;
            traversal.EndDash();
        }
    }
}
