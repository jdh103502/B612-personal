using UnityEngine;
using UnityEngine.InputSystem;

namespace B612
{
    /// <summary>
    /// Third-person camera for small planets. Its "up" follows the walker's gravity so it rolls around the asteroid.
    /// It eases toward a point a little ahead of the prince, keeps its height steady through jumps, swings in behind
    /// him while he runs, pulls back while he travels between planets, and rises over the ground instead of clipping.
    /// On play it swoops in from the scene's establishing shot.
    /// </summary>
    public class OrbitCamera : MonoBehaviour
    {
        public GravityWalker target;

        [Header("Framing")]
        public float fieldOfView = 42f;
        public float distance = 4.8f;
        public Vector2 zoomRange = new Vector2(2.6f, 9f);
        public float focusHeight = 0.55f;
        public float lookAhead = 0.6f;
        public float pitch = 20f;
        public float minPitch = -10f;
        public float maxPitch = 70f;
        [Tooltip("Extra distance after a moment in the air, so the landing spot stays in view.")]
        public float airPullBack = 1.0f;
        [Tooltip("Extra distance while heading for another planet or riding a launch star.")]
        public float travelPullBack = 3.0f;

        [Header("Input")]
        public float mouseSensitivity = 0.2f;
        public float stickSensitivity = 140f;
        public float keyTurnSpeed = 100f;
        [Tooltip("Reserve Q and the wheel for lamb cards; use Z/C and Ctrl+wheel for the camera.")]
        public bool cardInputMode;

        [Header("Smoothing")]
        public float followSharpness = 9f;
        public float verticalSharpness = 4f;
        [Tooltip("Seconds the camera takes to settle onto a new 'up' (e.g. after landing on another planet).")]
        public float upSmoothTime = 0.45f;
        [Tooltip("How eagerly the camera swings in behind a running prince.")]
        public float autoTurn = 1.4f;
        public float autoTurnMaxSpeed = 50f;
        public float introSeconds = 2.4f;

        [Header("Collision")]
        public float maxLift = 50f;
        public float minDistance = 1.4f;
        public LayerMask collisionMask = ~0;
        /// <summary>Layers the camera passes through for now (set while the prince is inside the house).</summary>
        public static int IgnoreMask;

        Camera cam;
        Vector3 camUp = Vector3.up, flatForward = Vector3.forward, focus, ahead, velocity, lastTargetPos;
        float userPitch, userDistance, lastManual = -10f, lift, wantDistance, curDistance, airTime, intro;
        Vector3 introPos;
        Quaternion introRot;
        float introFov;
        GravitySource groundSource;
        Vector3 anchorUp = Vector3.up, upVelocity;
        float turnSpeed, lowerClear, farClear;

        public float Lift { get { return lift; } }
        public float CurrentDistance { get { return curDistance; } }

        void Start()
        {
            if (!target) return;
            cam = GetComponent<Camera>();
            introPos = transform.position;
            introRot = transform.rotation;
            introFov = cam ? cam.fieldOfView : fieldOfView;
            intro = introSeconds > 0f ? 0f : 1f;

            camUp = anchorUp = target.Up;
            flatForward = Vector3.ProjectOnPlane(target.transform.forward, camUp).normalized;
            focus = target.transform.position + camUp * focusHeight;
            lastTargetPos = target.transform.position;
            userPitch = pitch;
            userDistance = wantDistance = curDistance = distance;
            groundSource = target.Source;
        }

        /// <summary>Turns a 2D stick/WASD input into a direction on the walker's tangent plane, relative to the view.</summary>
        public Vector3 PlanarDirection(Vector2 input, Vector3 up)
        {
            var f = Vector3.ProjectOnPlane(flatForward, up);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(transform.up, up);
            f.Normalize();
            var r = Vector3.Cross(up, f);
            return f * input.y + r * input.x;
        }

        void LateUpdate()
        {
            if (!target) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-5f);
            var tpos = target.transform.position;

            // velocity from movement (works while a launch star carries the prince too); ignore teleports
            var step = tpos - lastTargetPos;
            lastTargetPos = tpos;
            bool teleported = step.sqrMagnitude > 4f;
            velocity = teleported ? Vector3.zero : Vector3.Lerp(velocity, step / dt, 1f - Mathf.Exp(-8f * dt));

            // up: only take a new "up" while standing on a planet (or riding a launch star). Jumps and the little
            // stepping stones keep the current one, so the view doesn't roll every time the prince hops.
            bool onPlanet = target.Grounded && target.Source && target.Source.surfaceRadius >= 1f;
            if (onPlanet || target.Suspended) anchorUp = target.Up;
            if (teleported) camUp = anchorUp = target.Up;
            // a spring rather than a plain lerp, so a big change (landing on another planet) eases in and out
            camUp = Vector3.SmoothDamp(camUp, anchorUp, ref upVelocity, onPlanet ? upSmoothTime : upSmoothTime * 1.6f, 1.3f, dt).normalized;
            flatForward = Vector3.ProjectOnPlane(flatForward, camUp);
            if (flatForward.sqrMagnitude < 1e-5f) flatForward = Vector3.ProjectOnPlane(transform.forward, camUp);
            flatForward.Normalize();

            ReadInput(dt);
            AutoTurn(dt);

            // focus: a point slightly ahead; its height is held through jumps but follows falls
            var planarVel = Vector3.ProjectOnPlane(velocity, camUp);
            ahead = Vector3.Lerp(ahead, Vector3.ClampMagnitude(planarVel * (lookAhead / Mathf.Max(target.moveSpeed, 0.1f)), lookAhead * 1.5f), 1f - Mathf.Exp(-3f * dt));
            var goal = tpos + target.Up * focusHeight + ahead;
            if (teleported) focus = goal;
            var delta = goal - focus;
            float v = Vector3.Dot(delta, camUp);
            var h = delta - camUp * v;
            float vRate = target.Grounded ? verticalSharpness : (v < -0.4f ? verticalSharpness * 1.5f : 1.2f);
            focus += h * (1f - Mathf.Exp(-followSharpness * dt)) + camUp * v * (1f - Mathf.Exp(-vRate * dt));

            // distance: pull back while airborne and more while travelling to another planet
            airTime = target.Grounded ? 0f : airTime + dt;
            if (target.Grounded) groundSource = target.Source;
            bool travelling = target.Suspended || (!target.Grounded && target.Source && target.Source != groundSource && target.Source.surfaceRadius > 1f);
            float want = userDistance + (airTime > 0.3f ? airPullBack : 0f) + (travelling ? travelPullBack : 0f);
            wantDistance = Mathf.Lerp(wantDistance, want, 1f - Mathf.Exp(-(want > wantDistance ? 1.5f : 1f) * dt));

            // pitch eases back to the default after the player stops steering it
            if (Time.time - lastManual > 3f) userPitch = Mathf.Lerp(userPitch, pitch, 1f - Mathf.Exp(-0.8f * dt));

            Place(dt, travelling);
        }

        void ReadInput(float dt)
        {
            float yaw = 0f, dPitch = 0f;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed || mouse.leftButton.isPressed)
                {
                    var d = mouse.delta.ReadValue();
                    yaw += d.x * mouseSensitivity;
                    dPitch -= d.y * mouseSensitivity;
                }
                float scroll = mouse.scroll.ReadValue().y;
                var keys = Keyboard.current;
                bool zoom = !cardInputMode || (keys != null && (keys.leftCtrlKey.isPressed || keys.rightCtrlKey.isPressed));
                if (zoom && Mathf.Abs(scroll) > 0.01f) userDistance = Mathf.Clamp(userDistance - Mathf.Sign(scroll) * 0.5f, zoomRange.x, zoomRange.y);
            }
            var kb = Keyboard.current;
            if (kb != null)
            {
                if ((cardInputMode ? kb.zKey : kb.qKey).isPressed) yaw -= keyTurnSpeed * dt;
                if ((cardInputMode ? kb.cKey : kb.eKey).isPressed) yaw += keyTurnSpeed * dt;
            }
            var gp = Gamepad.current;
            if (gp != null)
            {
                var s = gp.rightStick.ReadValue();
                yaw += s.x * stickSensitivity * dt;
                dPitch -= s.y * stickSensitivity * dt;
            }
            if (Mathf.Abs(yaw) > 0.01f || Mathf.Abs(dPitch) > 0.01f) lastManual = Time.time;
            flatForward = Quaternion.AngleAxis(yaw, camUp) * flatForward;
            userPitch = Mathf.Clamp(userPitch + dPitch, minPitch, maxPitch);
        }

        // Swing in behind the prince while he runs, gently, and more for sideways running.
        // Running toward the camera leaves it alone so the player can turn around and see where they came from.
        void AutoTurn(float dt)
        {
            if (Time.time - lastManual < 1.2f) { turnSpeed = 0f; return; }
            float want = 0f;
            var move = Vector3.ProjectOnPlane(velocity, camUp);
            float speed = move.magnitude;
            if (speed > 0.3f)
            {
                move /= speed;
                if (Vector3.Dot(move, flatForward) > -0.55f)
                {
                    float side = Mathf.Abs(Vector3.Dot(move, Vector3.Cross(camUp, flatForward)));
                    float rate = target.Suspended ? autoTurn * 1.2f
                        : autoTurn * Mathf.Clamp01(speed / Mathf.Max(target.moveSpeed, 0.1f)) * (0.3f + 0.7f * side);
                    want = Mathf.Clamp(Vector3.SignedAngle(flatForward, move, camUp) * rate, -autoTurnMaxSpeed, autoTurnMaxSpeed);
                }
            }
            // the turn speed eases in and out and is capped, so a sharp change of direction never whips the view around
            turnSpeed = Mathf.Lerp(turnSpeed, want, 1f - Mathf.Exp(-4f * dt));
            flatForward = Quaternion.AngleAxis(turnSpeed * dt, camUp) * flatForward;
        }

        void Place(float dt, bool travelling)
        {
            var right = Vector3.Cross(camUp, flatForward).normalized;

            // when the planet is in the way (e.g. walking over the plateau rim), rise first, then move in
            float wantLift = maxLift;
            for (float extra = 0f; extra <= maxLift; extra += 10f)
            {
                if (userPitch + extra > 85f) break;
                if (!Blocked(Direction(right, userPitch + extra), wantDistance)) { wantLift = extra; break; }
            }
            // rise promptly (but not faster than 80 deg/s); only settle back down once the lower view has stayed clear
            if (wantLift > lift + 0.5f) { lift = Mathf.MoveTowards(lift, wantLift, Mathf.Min((wantLift - lift) * 4f, 60f) * dt); lowerClear = 0f; }
            else if (wantLift < lift - 0.5f) { lowerClear += dt; if (lowerClear > 0.6f) lift = Mathf.Lerp(lift, wantLift, 1f - Mathf.Exp(-1f * dt)); }
            else lowerClear = 0f;

            var dir = Direction(right, Mathf.Min(userPitch + lift, 85f));
            float allowed = wantDistance;
            RaycastHit hit;
            if (Physics.SphereCast(focus, 0.2f, dir, out hit, wantDistance, collisionMask & ~IgnoreMask, QueryTriggerInteraction.Ignore) && hit.rigidbody == null)
                allowed = Mathf.Max(minDistance, hit.distance - 0.1f);
            // move in immediately when something blocks the view, move back out slowly once it has stayed clear
            if (allowed < curDistance - 0.02f) { curDistance = Mathf.Lerp(curDistance, allowed, 1f - Mathf.Exp(-9f * dt)); farClear = 0f; }
            else { farClear += dt; if (farClear > 0.4f) curDistance = Mathf.Lerp(curDistance, allowed, 1f - Mathf.Exp(-2f * dt)); }

            var pos = focus + dir * curDistance;
            var rot = Quaternion.LookRotation(focus - pos, camUp);
            float fov = fieldOfView + (travelling ? 6f : 0f);

            // establishing shot -> gameplay swoop
            if (intro < 1f)
            {
                intro = Mathf.Min(1f, intro + dt / introSeconds);
                float s = intro * intro * (3f - 2f * intro);
                pos = Vector3.Lerp(introPos, pos, s);
                rot = Quaternion.Slerp(introRot, rot, s);
                fov = Mathf.Lerp(introFov, fov, s);
            }
            transform.SetPositionAndRotation(pos, rot);
            if (cam) cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, intro < 1f ? 1f : 1f - Mathf.Exp(-2f * dt));
        }

        Vector3 Direction(Vector3 right, float pitchDeg) { return Quaternion.AngleAxis(pitchDeg, right) * -flatForward; }

        // The view counts as blocked if the line to the prince's head or to his body is cut by scenery
        // (on a bumpy planet a ridge can hide his body while his head still shows).
        bool Blocked(Vector3 dir, float dist)
        {
            RaycastHit hit;
            if (Physics.SphereCast(focus, 0.2f, dir, out hit, dist, collisionMask & ~IgnoreMask, QueryTriggerInteraction.Ignore) && hit.rigidbody == null && hit.distance < dist * 0.7f)
                return true;
            var body = target.transform.position + target.Up * 0.25f;
            var eye = focus + dir * dist;
            var toEye = eye - body;
            return Physics.Raycast(body, toEye.normalized, out hit, toEye.magnitude * 0.85f, collisionMask & ~IgnoreMask, QueryTriggerInteraction.Ignore) && hit.rigidbody == null;
        }
    }
}
