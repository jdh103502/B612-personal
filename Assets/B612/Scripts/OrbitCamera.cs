using UnityEngine;
using UnityEngine.InputSystem;

namespace B612
{
    /// <summary>Planet-relative orbit frame, ground-following focus and collision-safe placement.</summary>
    public class OrbitCamera : MonoBehaviour
    {
        public GravityWalker target;
        [Header("Framing")]
        public float fieldOfView = 42f;
        public float distance = 5.2f;
        public Vector2 zoomRange = new Vector2(2.6f, 9f);
        public float focusHeight = .45f;
        public float lookAhead = .25f;
        public float pitch = 30f;
        public float minPitch = -10f;
        public float maxPitch = 70f;
        [Tooltip("Legacy scene setting. Ordinary jumps no longer add zoom.")]
        public float airPullBack = 0f;
        public float travelPullBack = 3f;
        [Header("Input")]
        public float mouseSensitivity = .2f;
        public float stickSensitivity = 140f;
        public float keyTurnSpeed = 100f;
        [Tooltip("Reserve Q and the wheel for lamb cards; use Z/C and Ctrl+wheel for the camera.")]
        public bool cardInputMode;
        [Header("Smoothing")]
        public float followSharpness = 9f;
        public float verticalSharpness = 4f;
        public float upSmoothTime = .28f;
        public float autoTurn = .7f;
        public float autoTurnMaxSpeed = 25f;
        public float introSeconds = 2.4f;
        [Header("Stable planet frame")]
        [Min(0f)] public float jumpHeightAllowance = 1f;
        [Min(0f)] public float fallHeightAllowance = .45f;
        [Min(.01f)] public float sourceConfirmSeconds = .25f;
        [Min(1f)] public float referenceTurnSpeed = 100f;
        [Header("Collision")]
        [Range(0f, 15f)] public float maxLift = 12f;
        public float minDistance = 1.4f;
        [Min(.05f)] public float collisionRadius = .2f;
        [Min(0f)] public float occlusionDelay = .2f;
        public LayerMask collisionMask = ~0;
        public static int IgnoreMask;

        public enum FollowState { Grounded, Airborne, Transfer }
        public FollowState State { get; private set; }
        public GravitySource ReferenceSource => groundSource;
        public Vector3 CameraUp => camUp;
        public Vector3 GroundPoint => groundPoint;
        public Vector3 FocusPoint => focus;
        public float Lift => lift;
        public float CurrentDistance => initialized ? Vector3.Distance(focus, transform.position) : curDistance;
        public float DesiredDistance => wantDistance;
        public float AutoTurnSpeed => turnSpeed;

        Camera cam;
        Vector3 camUp, flatForward, focus, ahead, velocity, lastTargetPos, groundPoint, lastGroundPoint;
        float userPitch, userDistance, lastManual = -10f, lift, wantDistance, curDistance, intro;
        Vector3 introPos;
        Quaternion introRot;
        float introFov;
        GravitySource groundSource, candidateSource;
        Collider groundCollider;
        float sourceHeld, transferHeld, turnSpeed, lowerClear, farClear, blockedTime;
        bool initialized;
        SphereCollider collisionProbe;
        readonly RaycastHit[] castHits = new RaycastHit[64];
        readonly Collider[] overlaps = new Collider[64];

        void Start()
        {
            if (!target) return;
            cam = GetComponent<Camera>();
            introPos = transform.position; introRot = transform.rotation;
            introFov = cam ? cam.fieldOfView : fieldOfView;
            intro = introSeconds > 0f ? 0f : 1f;
            userPitch = pitch; userDistance = distance;
            wantDistance = curDistance = distance;
            var probeObject = new GameObject("Camera collision query") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
            collisionProbe = probeObject.AddComponent<SphereCollider>();
            collisionProbe.enabled = false;
            SetSource(PlanetAtTarget());
            camUp = StableUp();
            flatForward = Tangent(target.transform.forward, camUp);
            groundPoint = target.transform.position;
            groundPoint = SampleGround(); lastGroundPoint = groundPoint;
            focus = FocusGoal(); lastTargetPos = target.transform.position;
            initialized = true;
        }

        GravitySource PlanetAtTarget()
        {
            var source = target.Source ? target.Source : GravitySource.Find(target.transform.position);
            return source && source.surfaceRadius >= 1f ? source : null;
        }
        void SetSource(GravitySource source)
        {
            groundSource = source;
            groundCollider = source ? source.GetComponent<Collider>() : null;
            if (source && !groundCollider) groundCollider = source.GetComponentInChildren<Collider>();
            candidateSource = null; sourceHeld = transferHeld = 0f;
        }
        Vector3 StableUp()
        {
            if (groundSource)
            {
                Vector3 radial = target.transform.position - groundSource.transform.position;
                if (radial.sqrMagnitude > .001f) return radial.normalized;
            }
            return target.Up.sqrMagnitude > .01f ? target.Up : Vector3.up;
        }
        static Vector3 Tangent(Vector3 heading, Vector3 up)
        {
            var tangent = Vector3.ProjectOnPlane(heading, up);
            if (tangent.sqrMagnitude < .0001f) tangent = Vector3.ProjectOnPlane(Vector3.forward, up);
            if (tangent.sqrMagnitude < .0001f) tangent = Vector3.ProjectOnPlane(Vector3.right, up);
            return tangent.normalized;
        }
        public Vector3 PlanarDirection(Vector2 input, Vector3 up)
        {
            var forward = Tangent(flatForward.sqrMagnitude > .01f ? flatForward : transform.forward, up);
            return forward * input.y + Vector3.Cross(up, forward) * input.x;
        }
        void UpdateSource(float dt)
        {
            var source = PlanetAtTarget();
            if (!groundSource && source) SetSource(source);
            if (target.Grounded && source && source != groundSource)
            {
                if (candidateSource != source) { candidateSource = source; sourceHeld = 0f; }
                sourceHeld += dt;
                if (sourceHeld >= sourceConfirmSeconds) SetSource(source);
            }
            else { candidateSource = null; sourceHeld = 0f; }
            bool differentField = !target.Grounded && source && groundSource && source != groundSource;
            transferHeld = differentField ? transferHeld + dt : 0f;
            State = target.Suspended || transferHeld >= sourceConfirmSeconds ? FollowState.Transfer
                : target.Grounded ? FollowState.Grounded : FollowState.Airborne;
        }
        Vector3 SampleGround()
        {
            if (!groundSource || !groundCollider) return target.Grounded ? target.transform.position : groundPoint;
            var centre = groundSource.transform.position;
            var radial = (target.transform.position - centre).normalized;
            float radius = Mathf.Max(groundCollider.bounds.extents.magnitude + 2f, Vector3.Distance(target.transform.position, centre) + 2f);
            if (groundCollider.Raycast(new Ray(centre + radial * radius, -radial), out var hit, radius * 2f)) return hit.point;
            return target.Grounded ? target.transform.position : groundPoint;
        }
        Vector3 FocusGoal()
        {
            if (State == FollowState.Transfer || !groundSource)
                return target.transform.position + camUp * focusHeight + ahead;
            // Spend ordinary jump height inside the frame instead of moving the rig with it.
            float height = Vector3.Dot(target.transform.position - groundPoint, StableUp());
            float excess = Mathf.Max(0f, height - jumpHeightAllowance) + Mathf.Min(0f, height + fallHeightAllowance);
            return groundPoint + camUp * (focusHeight + excess) + ahead;
        }

        void LateUpdate()
        {
            if (!initialized || !target || Time.deltaTime <= 0f) return;
            float dt = Time.deltaTime;
            Vector3 tpos = target.transform.position;
            bool teleported = (tpos - lastTargetPos).sqrMagnitude > 4f;
            lastTargetPos = tpos;
            if (teleported)
            {
                SetSource(PlanetAtTarget());
                camUp = StableUp(); flatForward = Tangent(target.transform.forward, camUp);
                velocity = ahead = Vector3.zero; lift = blockedTime = lowerClear = farClear = turnSpeed = 0f;
                curDistance = wantDistance = userDistance; intro = 1f;
            }
            UpdateSource(dt);
            Vector3 upGoal = State == FollowState.Transfer && target.Suspended ? target.Up : StableUp();
            float angle = Vector3.Angle(camUp, upGoal);
            float change = Mathf.Min(angle * (1f - Mathf.Exp(-dt / Mathf.Max(.01f, upSmoothTime))), referenceTurnSpeed * dt);
            var newUp = Vector3.RotateTowards(camUp, upGoal, change * Mathf.Deg2Rad, 0f).normalized;
            var transport = Quaternion.FromToRotation(camUp, newUp);
            flatForward = Tangent(transport * flatForward, newUp);
            ahead = transport * ahead; velocity = transport * velocity; camUp = newUp;
            groundPoint = SampleGround();
            var groundStep = teleported ? Vector3.zero : groundPoint - lastGroundPoint;
            lastGroundPoint = groundPoint;
            var groundVelocity = Vector3.ProjectOnPlane(groundStep / dt, StableUp());
            velocity = Vector3.Lerp(velocity, groundVelocity, 1f - Mathf.Exp(-8f * dt));
            ReadInput(dt); AutoTurn(dt);
            var anticipation = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(velocity, camUp) * (lookAhead / Mathf.Max(target.moveSpeed, .1f)), lookAhead * 1.5f);
            ahead = Vector3.Lerp(ahead, anticipation, 1f - Mathf.Exp(-3f * dt));
            var goal = FocusGoal();
            if (teleported) focus = goal;
            var delta = goal - focus;
            var vertical = camUp * Vector3.Dot(delta, camUp);
            focus += (delta - vertical) * (1f - Mathf.Exp(-followSharpness * dt)) + vertical * (1f - Mathf.Exp(-verticalSharpness * dt));
            bool travelling = State == FollowState.Transfer;
            float want = userDistance + (travelling ? travelPullBack : 0f);
            wantDistance = Mathf.Lerp(wantDistance, want, 1f - Mathf.Exp(-3f * dt));
            Place(dt, travelling);
        }

        void ReadInput(float dt)
        {
            float yaw = 0f, dPitch = 0f;
            var mouse = Mouse.current;
            var keys = Keyboard.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed || mouse.leftButton.isPressed)
                {
                    var delta = mouse.delta.ReadValue(); yaw += delta.x * mouseSensitivity; dPitch -= delta.y * mouseSensitivity;
                }
                bool zoom = !cardInputMode || (keys != null && (keys.leftCtrlKey.isPressed || keys.rightCtrlKey.isPressed));
                float scroll = mouse.scroll.ReadValue().y;
                if (zoom && Mathf.Abs(scroll) > .01f)
                {
                    userDistance = Mathf.Clamp(userDistance - Mathf.Sign(scroll) * .5f, zoomRange.x, zoomRange.y);
                    lastManual = Time.time;
                }
            }
            if (keys != null)
            {
                if ((cardInputMode ? keys.zKey : keys.qKey).isPressed) yaw -= keyTurnSpeed * dt;
                if ((cardInputMode ? keys.cKey : keys.eKey).isPressed) yaw += keyTurnSpeed * dt;
            }
            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                var stick = gamepad.rightStick.ReadValue(); yaw += stick.x * stickSensitivity * dt; dPitch -= stick.y * stickSensitivity * dt;
            }
            if (Mathf.Abs(yaw) > .01f || Mathf.Abs(dPitch) > .01f) lastManual = Time.time;
            flatForward = Quaternion.AngleAxis(yaw, camUp) * flatForward;
            userPitch = Mathf.Clamp(userPitch + dPitch, minPitch, maxPitch);
        }
        void AutoTurn(float dt)
        {
            if (!target.Grounded || State == FollowState.Transfer || Time.time - lastManual < 1.2f) { turnSpeed = 0f; return; }
            float want = 0f;
            var move = Vector3.ProjectOnPlane(velocity, camUp);
            float speed = move.magnitude;
            if (speed > .3f)
            {
                move /= speed;
                if (Vector3.Dot(move, flatForward) > -.55f)
                {
                    float side = Mathf.Abs(Vector3.Dot(move, Vector3.Cross(camUp, flatForward)));
                    float rate = autoTurn * Mathf.Clamp01(speed / Mathf.Max(target.moveSpeed, .1f)) * (.3f + .7f * side);
                    want = Mathf.Clamp(Vector3.SignedAngle(flatForward, move, camUp) * rate, -autoTurnMaxSpeed, autoTurnMaxSpeed);
                }
            }
            turnSpeed = Mathf.Lerp(turnSpeed, want, 1f - Mathf.Exp(-4f * dt));
            flatForward = Quaternion.AngleAxis(turnSpeed * dt, camUp) * flatForward;
        }
        bool Obstacle(Collider collider)
        {
            return collider && !collider.isTrigger && collider.attachedRigidbody == null
                && !collider.transform.IsChildOf(target.transform) && !collider.transform.IsChildOf(transform);
        }
        float SafeDistance(Vector3 direction, float requested)
        {
            float safe = requested;
            int count = Physics.SphereCastNonAlloc(focus, collisionRadius, direction, castHits, requested, collisionMask & ~IgnoreMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (Obstacle(castHits[i].collider)) safe = Mathf.Min(safe, Mathf.Max(0f, castHits[i].distance - .04f));
            return safe;
        }
        bool RayBlocked(Vector3 origin, Vector3 end)
        {
            var offset = end - origin;
            int count = Physics.RaycastNonAlloc(origin, offset.normalized, castHits, offset.magnitude, collisionMask & ~IgnoreMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) if (Obstacle(castHits[i].collider)) return true;
            return false;
        }
        bool ViewBlocked(Vector3 eye)
        {
            return RayBlocked(target.transform.position + camUp * .25f, eye)
                && RayBlocked(target.transform.position + camUp * .8f, eye);
        }
        Vector3 Direction(float angle)
        {
            return Quaternion.AngleAxis(angle, Vector3.Cross(camUp, flatForward).normalized) * -flatForward;
        }
        void Place(float dt, bool travelling)
        {
            Vector3 direction = Direction(userPitch + lift);
            float safe = SafeDistance(direction, wantDistance);
            bool obstructed = ViewBlocked(focus + direction * Mathf.Min(curDistance, safe));
            blockedTime = obstructed ? blockedTime + dt : 0f;
            float wantedLift = lift;
            if (blockedTime >= occlusionDelay)
            {
                // No clear candidate means keep the angle, not jump to maximum lift.
                float limit = Mathf.Clamp(maxLift, 0f, 15f);
                for (float extra = 0f; extra <= limit; extra += 2f)
                {
                    if (userPitch + extra > 80f) break;
                    var candidate = Direction(userPitch + extra);
                    float candidateDistance = SafeDistance(candidate, wantDistance);
                    if (candidateDistance >= minDistance && !ViewBlocked(focus + candidate * candidateDistance)) { wantedLift = extra; break; }
                }
                lowerClear = 0f;
            }
            else if (!obstructed)
            {
                lowerClear += dt;
                if (lowerClear > .6f) wantedLift = 0f;
            }
            lift = Mathf.MoveTowards(lift, wantedLift, (wantedLift > lift ? 12f : 5f) * dt);
            direction = Direction(Mathf.Min(userPitch + lift, 85f));
            safe = SafeDistance(direction, wantDistance);
            // Comfort distance never overrides physical safety. Pull in immediately; return slowly.
            if (safe < curDistance) { curDistance = safe; farClear = 0f; }
            else
            {
                farClear += dt;
                if (farClear > .5f) curDistance = Mathf.Lerp(curDistance, safe, 1f - Mathf.Exp(-2f * dt));
            }
            var position = focus + direction * curDistance;
            var rotation = Quaternion.LookRotation((focus - position).sqrMagnitude > .0001f ? focus - position : -direction, camUp);
            float fov = fieldOfView + (travelling ? 6f : 0f);
            if (intro < 1f)
            {
                intro = Mathf.Min(1f, intro + dt / Mathf.Max(.01f, introSeconds));
                float eased = intro * intro * (3f - 2f * intro);
                position = Vector3.Lerp(introPos, position, eased); rotation = Quaternion.Slerp(introRot, rotation, eased);
                fov = Mathf.Lerp(introFov, fov, eased);
            }
            var offset = position - focus;
            if (offset.sqrMagnitude > .0001f)
                position = focus + offset.normalized * Mathf.Min(offset.magnitude, SafeDistance(offset.normalized, offset.magnitude));
            position = Depenetrate(position);
            if (intro >= 1f && (focus - position).sqrMagnitude > .0001f) rotation = Quaternion.LookRotation(focus - position, camUp);
            transform.SetPositionAndRotation(position, rotation);
            if (cam) cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, intro < 1f ? 1f : 1f - Mathf.Exp(-2f * dt));
        }
        Vector3 Depenetrate(Vector3 position)
        {
            collisionProbe.radius = collisionRadius;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                bool moved = false;
                int count = Physics.OverlapSphereNonAlloc(position, collisionRadius, overlaps, collisionMask & ~IgnoreMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var obstacle = overlaps[i];
                    if (!Obstacle(obstacle)) continue;
                    if (Physics.ComputePenetration(collisionProbe, position, Quaternion.identity, obstacle, obstacle.transform.position, obstacle.transform.rotation, out var direction, out float depth))
                    { position += direction * (depth + .01f); moved = true; }
                }
                if (!moved) break;
            }
            return position;
        }
        void OnDestroy() { if (collisionProbe) Destroy(collisionProbe.gameObject); }
    }
}
