using System;
using UnityEngine;

namespace B612
{
    public enum TraversalFeature { Dash, AirJump, Glide }
    /// <summary>Player-only state. GravityWalker remains the sole physics/velocity writer.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(GravityWalker))]
    public sealed class PlayerTraversalAbilities : MonoBehaviour
    {
        [Header("Air jump")]
        [Min(0f)] public float doubleJumpSpeed = 3f;
        [Min(1f)] public float fallingGravityMultiplier = 1.6f;
        [Header("Dash")]
        [Min(0f)] public float dashSpeed = 8f;
        [Min(.01f)] public float dashDuration = .18f;
        [Min(0f)] public float dashBrakeSeconds = .06f;
        [Min(0f)] public float dashExitSpeed = 3f;
        [Min(0f)] public float dashCooldown = .65f;
        [Min(0f)] public float dashInputBuffer = .1f;
        [Min(0f)] public float dashCollisionSkin = .015f;
        [Tooltip("Outward speed (away from the planet) a dash may carry from the ground into the air. A ground dash runs along slopes and banks at dash speed; without this cap it launches off their tops. In the air a dash never adds rise (a jump keeps its own).")]
        [Min(0f)] public float dashMaxRiseSpeed = .8f;
        [Header("Glide")]
        [Min(0f)] public float glideHoldThreshold = .2f;
        [Min(0f)] public float glideBudgetSeconds = 2.5f;
        [Min(.01f)] public float glideFallSpeed = .75f;
        [Min(.01f)] public float glideMinimumFallSpeed = .15f;
        [Min(.01f)] public float glideVerticalAcceleration = 4f;
        [Min(0f)] public float glideMoveSpeed = 2.6f;
        [Min(0f)] public float glideSteeringAcceleration = 3f;
        [Header("Landing")]
        [Min(0f)] public float supportConfirmSeconds = .04f;
        [Min(0f)] public float takeoffRecoveryBlockSeconds = .1f;

        public enum TraversalMode { Locomotion, Dash, Glide, Suspended }
        public TraversalMode Mode => walker && walker.Suspended ? TraversalMode.Suspended
            : IsDashing ? TraversalMode.Dash : IsGliding ? TraversalMode.Glide : TraversalMode.Locomotion;
        public int AirJumpsRemaining { get; private set; }
        public bool DashChargeAvailable { get; private set; }
        public float GlideSecondsRemaining { get; private set; }
        public float DashCooldownRemaining { get; private set; }
        public float JumpHeldSeconds { get; private set; }
        public float SupportStableSeconds { get; private set; }
        public float TimeSinceTakeoff { get; private set; }
        public bool FlightActive { get; private set; }
        public bool JumpAscentActive { get; private set; }
        internal bool BlocksGroundSupport => JumpAscentActive || jumpSupportBlockRemaining > 0f;
        public bool IsDashing { get; private set; }
        public bool IsGliding { get; private set; }
        public bool InputEnabled { get; private set; } = true;
        public bool AllowDash { get; private set; } = true;
        public bool AllowAirJump { get; private set; } = true;
        public bool AllowGlide { get; private set; } = true;
        public TraversalCapability Unlocked { get; private set; } = TraversalCapability.All;
        public bool IsUnlocked(TraversalFeature feature)=>(Unlocked & (feature==TraversalFeature.Dash?TraversalCapability.Dash:feature==TraversalFeature.AirJump?TraversalCapability.AirJump:TraversalCapability.Glide))!=0;
        public bool IsEnabled(TraversalFeature feature)=>IsAllowed(feature)&&IsUnlocked(feature);
        public void SetUnlocked(TraversalCapability unlocked)
        {
            Unlocked=unlocked & TraversalCapability.All;
            if(!IsEnabled(TraversalFeature.Dash)){dashPending=0;if(IsDashing)EndDash();}
            if(!IsEnabled(TraversalFeature.Glide)){IsGliding=false;glideNeedsRelease=true;}
            AllowedChanged?.Invoke();
        }
        public event Action AllowedChanged;
        public bool PendingJump => jumpPending > 0f;
        public bool PendingDash => dashPending > 0f;
        public int JumpRequestId { get; private set; }
        public int DashRequestId { get; private set; }
        public int ConsumedJumpRequestId { get; private set; }
        public int ConsumedDashRequestId { get; private set; }
        public float DashElapsedSeconds { get; private set; }
        public Vector3 DashDirection => dashDirection;
        public event Action AirJumped;
        public event Action LandingRecovered;

        GravityWalker walker;
        float jumpPending, dashPending, coyoteRemaining, jumpSupportBlockRemaining;
        // From a dash until stable support: airborne outward speed may only fall (ballistic). Leaving the ground it may
        // start at most at dashMaxRiseSpeed; contacts in the air (a steep face) can stop a fall but never add rise.
        bool riseGuard; float riseLimit;
        public bool DashRiseGuardActive => riseGuard;
        Vector3 dashDirection, dashUp, lastFacing;
        bool groundJumpAvailable, clampResidual, glideNeedsRelease;

        public bool IsAllowed(TraversalFeature feature) => feature == TraversalFeature.Dash ? AllowDash
            : feature == TraversalFeature.AirJump ? AllowAirJump : AllowGlide;
        public void SetAllowed(TraversalFeature feature, bool allowed)
        {
            if (IsAllowed(feature) == allowed) return;
            switch (feature)
            {
                case TraversalFeature.Dash:
                    AllowDash = allowed;
                    if (!allowed) { dashPending = 0f; if (IsDashing) EndDash(); }
                    break;
                case TraversalFeature.AirJump: AllowAirJump = allowed; break;
                case TraversalFeature.Glide:
                    AllowGlide = allowed;
                    if (!allowed) { IsGliding = false; glideNeedsRelease = true; JumpHeldSeconds = 0f; }
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(feature));
            }
            AllowedChanged?.Invoke();
        }
        public void ClearPendingRequests() { jumpPending = dashPending = 0f; }

        void Awake()
        {
            walker = GetComponent<GravityWalker>();
            walker.BindTraversal(this);
            ResetForTeleport();
        }
        void OnDisable() { SetInputEnabled(false); }
        void OnEnable()
        {
            var controller = GetComponent<PlayerController>();
            SetInputEnabled(!controller || controller.isActiveAndEnabled);
        }

        public void RequestJump()
        {
            if (!InputEnabled || !isActiveAndEnabled || walker.Suspended || Time.timeScale <= 0f || GameplayModalInput.BlocksGameplayInput) return;
            JumpRequestId++; jumpPending = Mathf.Max(.001f, walker.jumpBuffer); JumpHeldSeconds = 0f;
        }
        public void RequestDash()
        {
            if (!IsEnabled(TraversalFeature.Dash) || !InputEnabled || !isActiveAndEnabled || walker.Suspended || Time.timeScale <= 0f || GameplayModalInput.BlocksGameplayInput) return;
            DashRequestId++; dashPending = Mathf.Max(.001f, dashInputBuffer);
        }
        public void SetInputEnabled(bool enabled)
        {
            if (!enabled) CancelInputAndActions();
            InputEnabled = enabled;
        }
        // Isolated preparation-time PhysicsScene probes have no UI/clock. Public player input keeps its modal gate.
        internal void QueueSimulationInput(bool jump,bool dash)
        {
            if(!InputEnabled || !isActiveAndEnabled || walker.Suspended)return;
            if(jump){JumpRequestId++;jumpPending=Mathf.Max(.001f,walker.jumpBuffer);JumpHeldSeconds=0f;}
            if(dash && IsEnabled(TraversalFeature.Dash)){DashRequestId++;dashPending=Mathf.Max(.001f,dashInputBuffer);}
        }
        public void CancelInputAndActions()
        {
            jumpPending = dashPending = JumpHeldSeconds = 0f;
            IsGliding = false;
            if (IsDashing) EndDash();
        }
        public void ResetForTeleport()
        {
            CancelInputAndActions();
            AirJumpsRemaining = 1; DashChargeAvailable = true; GlideSecondsRemaining = Mathf.Max(0f, glideBudgetSeconds);
            DashCooldownRemaining = SupportStableSeconds = TimeSinceTakeoff = coyoteRemaining = DashElapsedSeconds = 0f;
            FlightActive = groundJumpAvailable = clampResidual = false;
            JumpAscentActive = false; jumpSupportBlockRemaining = 0f;
            glideNeedsRelease = false; riseGuard = false;
            JumpRequestId = DashRequestId = ConsumedJumpRequestId = ConsumedDashRequestId = 0;
            dashDirection = lastFacing = Vector3.ProjectOnPlane(transform.forward, walker ? walker.Up : transform.up).normalized;
        }
        internal void Suspend()
        {
            CancelInputAndActions(); riseGuard = false;
            FlightActive = true; TimeSinceTakeoff = SupportStableSeconds = coyoteRemaining = 0f;
            groundJumpAvailable = false;
            JumpAscentActive = false; jumpSupportBlockRemaining = 0f;
        }

        internal bool BeginTick(float dt, bool grounded, Vector3 up)
        {
            DashCooldownRemaining = Mathf.Max(0f, DashCooldownRemaining - dt);
            if (!walker.jumpHeld) glideNeedsRelease = false;
            JumpHeldSeconds = InputEnabled && walker.jumpHeld ? JumpHeldSeconds + dt : 0f;
            SupportStableSeconds = grounded && !IsDashing ? SupportStableSeconds + dt : 0f;
            if (!grounded && !FlightActive) { FlightActive = true; TimeSinceTakeoff = 0f; }
            else if (FlightActive) TimeSinceTakeoff += dt;
            bool recovered = false;
            if (grounded && !IsDashing && SupportStableSeconds + .000001f >= supportConfirmSeconds)
            {
                if (FlightActive && TimeSinceTakeoff + .000001f >= takeoffRecoveryBlockSeconds)
                {
                    FlightActive = false; AirJumpsRemaining = 1; DashChargeAvailable = true;
                    JumpAscentActive = false;
                    GlideSecondsRemaining = Mathf.Max(0f, glideBudgetSeconds);
                    recovered = true; LandingRecovered?.Invoke();
                }
                if (!FlightActive)
                {
                    groundJumpAvailable = true;
                    if (DashCooldownRemaining <= 0f) DashChargeAvailable = true;
                }
            }
            if (grounded && !FlightActive) { groundJumpAvailable = true; coyoteRemaining = walker.coyoteTime; }
            else coyoteRemaining = Mathf.Max(0f, coyoteRemaining - dt);
            var facing = Vector3.ProjectOnPlane(transform.forward, up);
            if (facing.sqrMagnitude > .0001f && !IsDashing) lastFacing = facing.normalized;
            IsGliding = false;
            return recovered;
        }
        void Takeoff()
        {
            FlightActive = true; TimeSinceTakeoff = SupportStableSeconds = coyoteRemaining = 0f;
            groundJumpAvailable = false;
            JumpAscentActive = true;
            jumpSupportBlockRemaining = Mathf.Max(0f, takeoffRecoveryBlockSeconds);
        }
        internal void UpdateJumpAscent(float gravityVerticalSpeed)
        {
            if (gravityVerticalSpeed <= 0f) JumpAscentActive = false;
        }
        // 0 = none, 1 = ground/coyote, 2 = air. Input is consumed exactly once.
        internal int TryJump(bool grounded, ref Vector3 tangent, ref float vertical)
        {
            if (!InputEnabled || !PendingJump) return 0;
            bool ground = groundJumpAvailable && (grounded && !FlightActive || coyoteRemaining > 0f);
            if (!ground && (!IsEnabled(TraversalFeature.AirJump) || grounded || AirJumpsRemaining <= 0)) return 0;
            if (IsDashing) { EndDash(); ClampResidual(ref tangent); }
            IsGliding = false; jumpPending = 0f; ConsumedJumpRequestId = JumpRequestId;
            vertical = ground ? walker.jumpSpeed : doubleJumpSpeed; riseGuard = false;
            if (!ground) { AirJumpsRemaining--; AirJumped?.Invoke(); }
            Takeoff();
            return ground ? 1 : 2;
        }
        internal void TryStartDash(bool grounded, Vector3 up, float radialSpeed)
        {
            if (!IsEnabled(TraversalFeature.Dash) || !InputEnabled || !PendingDash || IsDashing || !DashChargeAvailable || DashCooldownRemaining > 0f) return;
            var direction = Vector3.ProjectOnPlane(transform.forward, up);
            if (direction.sqrMagnitude < .0001f) direction = Vector3.ProjectOnPlane(lastFacing, up);
            if (direction.sqrMagnitude < .0001f) return;
            dashDirection = direction.normalized; dashUp = up; DashElapsedSeconds = 0f;
            IsDashing = true; IsGliding = false; DashChargeAvailable = false;
            DashCooldownRemaining = Mathf.Max(0f, dashCooldown);
            dashPending = 0f; ConsumedDashRequestId = DashRequestId;
            riseGuard = true; riseLimit = grounded ? dashMaxRiseSpeed : Mathf.Max(0f, radialSpeed);
        }
        float DashIntegral(float t)
        {
            float duration = Mathf.Max(.001f, dashDuration), brake = Mathf.Clamp(dashBrakeSeconds, 0f, duration);
            float cruise = duration - brake;
            t = Mathf.Clamp(t, 0f, duration);
            float braking = Mathf.Max(0f, t - cruise);
            return dashSpeed * Mathf.Min(t, cruise) + (brake > 0f
                ? dashSpeed * braking + (dashExitSpeed - dashSpeed) * braking * braking / (2f * brake) : 0f);
        }
        internal bool ApplyDash(float dt, Vector3 up, out Vector3 tangent)
        {
            tangent = Vector3.zero;
            if (!IsDashing) return false;
            dashDirection = Vector3.ProjectOnPlane(Quaternion.FromToRotation(dashUp, up) * dashDirection, up).normalized;
            dashUp = up;
            float used = Mathf.Min(dt, Mathf.Max(0f, dashDuration - DashElapsedSeconds));
            float distance = DashIntegral(DashElapsedSeconds + used) - DashIntegral(DashElapsedSeconds);
            tangent = dashDirection * ((distance + dashExitSpeed * (dt - used)) / dt);
            DashElapsedSeconds += used;
            if (DashElapsedSeconds >= dashDuration - .000001f) EndDash();
            return true;
        }
        internal float GlideTime(float dt, float vertical, bool grounded, bool hasGravity, bool jumped, bool dashTick)
        {
            if (!IsEnabled(TraversalFeature.Glide) || glideNeedsRelease || !InputEnabled || grounded || !hasGravity || jumped || dashTick || !walker.jumpHeld
                || JumpHeldSeconds + .000001f < glideHoldThreshold || vertical > -.05f || GlideSecondsRemaining <= 0f) return 0f;
            float used = Mathf.Min(dt, GlideSecondsRemaining);
            GlideSecondsRemaining = Mathf.Max(0f, GlideSecondsRemaining - used);
            IsGliding = GlideSecondsRemaining > .000001f;
            return used;
        }
        /// <summary>
        /// Caps the outward speed of an airborne walker after a dash (vertical is along gravity up when airborne).
        /// A dash only replaces the tangent speed: rising in the air may continue a jump but never gain from the dash.
        /// </summary>
        internal float LimitDashRise(bool grounded, float vertical)
        {
            if (!riseGuard) return vertical;
            if (grounded)
            {
                riseLimit = dashMaxRiseSpeed;
                if (!IsDashing && SupportStableSeconds + .000001f >= supportConfirmSeconds) riseGuard = false;
                return vertical;
            }
            vertical = Mathf.Min(vertical, riseLimit);
            riseLimit = Mathf.Max(0f, vertical);
            return vertical;
        }
        internal void EndDash()
        {
            IsDashing = false; clampResidual = true;
        }
        internal void ClampResidual(ref Vector3 tangent)
        {
            if (!clampResidual) return;
            tangent = Vector3.ClampMagnitude(tangent, Mathf.Max(0f, dashExitSpeed)); clampResidual = false;
        }
        internal void EndTick(float dt)
        {
            jumpSupportBlockRemaining = Mathf.Max(0f, jumpSupportBlockRemaining - dt);
            jumpPending = Mathf.Max(0f, jumpPending - dt);
            dashPending = Mathf.Max(0f, dashPending - dt);
        }
    }
}
