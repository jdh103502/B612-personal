using UnityEngine;
using UnityEngine.InputSystem;

namespace B612
{
    /// <summary>Keyboard / gamepad input for the prince, plus respawn and star counting.</summary>
    [RequireComponent(typeof(GravityWalker))]
    public class PlayerController : MonoBehaviour
    {
        public OrbitCamera cameraRig;
        public Transform spawnPoint;
        public float respawnDistance = 40f;

        public int StarsCollected { get; private set; }
        public int StarsTotal { get; private set; }
        /// <summary>The launch star the prince is standing in, if any (set by the star's trigger).</summary>
        public LaunchStar NearbyLaunch { get; set; }

        GravityWalker walker;
        bool hasFocus = true, waitJumpRelease, waitDashRelease;
        bool continuingModalJumpHold;

        void Awake()
        {
            walker = GetComponent<GravityWalker>();
        }

        void Start()
        {
            StarsTotal = FindObjectsByType<Collectible>(FindObjectsSortMode.None).Length;
        }

        void Update()
        {
            if (walker.Suspended || !hasFocus) { ClearControls(); return; }
            if (GameplayModalInput.BlocksGameplayInput) { walker.moveInput = Vector3.zero; walker.ClearPendingRequests(); return; }
            if (Time.timeScale <= 0f) { ClearControls(); return; }
            walker.SetTraversalInputEnabled(true);
            Vector2 move = Vector2.zero;
            bool jumpDown = false, jumpHeld = false, dashDown = false, dashHeld = false, respawn = false;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
                jumpDown |= kb.spaceKey.wasPressedThisFrame;
                jumpHeld |= kb.spaceKey.isPressed;
                respawn |= kb.rKey.wasPressedThisFrame;
                dashDown |= kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame;
                dashHeld |= kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }
            var gp = Gamepad.current;
            if (gp != null)
            {
                var stick = gp.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.04f) move += stick;
                jumpDown |= gp.buttonSouth.wasPressedThisFrame;
                jumpHeld |= gp.buttonSouth.isPressed;
                dashDown |= gp.rightShoulder.wasPressedThisFrame;
                dashHeld |= gp.rightShoulder.isPressed;
            }
            if (waitJumpRelease) { if (!jumpHeld) waitJumpRelease = false; jumpDown = jumpHeld = false; }
            if (waitDashRelease) { if (!dashHeld) waitDashRelease = false; dashDown = false; }
            bool lost = walker.TimeWithoutGravity > 2.5f || (spawnPoint && Vector3.Distance(transform.position, spawnPoint.position) > respawnDistance);
            if (respawn || lost) { Respawn(); return; }
            move = Vector2.ClampMagnitude(move, 1f);

            walker.moveInput = cameraRig ? cameraRig.PlanarDirection(move, walker.Up) : new Vector3(move.x, 0f, move.y);
            walker.jumpHeld = jumpHeld;
            if (jumpDown)
            {
                if (NearbyLaunch && !NearbyLaunch.Busy && walker.Grounded)
                {
                    ClearControls();
                    NearbyLaunch.Launch(walker);
                    return;
                }
                else walker.Jump();
            }
            if (dashDown) walker.RequestDash();
        }

        public void Respawn()
        {
            ClearControls();
            if (spawnPoint) walker.Teleport(spawnPoint.position, spawnPoint.up);
        }

        public void BeginModalPause()
        {
            continuingModalJumpHold = walker && walker.jumpHeld && RawJumpHeld();
            if (walker) { walker.moveInput = Vector3.zero; walker.ClearPendingRequests(); }
        }
        public void ObserveModalPauseInput() { if (!RawJumpHeld()) continuingModalJumpHold = false; }
        public void EndModalPause()
        {
            ObserveModalPauseInput();
            bool keepHold = continuingModalJumpHold && isActiveAndEnabled && hasFocus;
            waitJumpRelease = !keepHold; waitDashRelease = true;
            if (walker) { walker.jumpHeld = keepHold; walker.moveInput = Vector3.zero; walker.ClearPendingRequests(); }
        }
        static bool RawJumpHeld() => Keyboard.current != null && Keyboard.current.spaceKey.isPressed
            || Gamepad.current != null && Gamepad.current.buttonSouth.isPressed;

        void ClearControls()
        {
            continuingModalJumpHold = false;
            waitJumpRelease = waitDashRelease = true;
            if (!walker) return;
            walker.ClearInput(); walker.SetTraversalInputEnabled(false);
        }
        void OnDisable() { ClearControls(); }
        void OnEnable()
        {
            if (!walker) walker = GetComponent<GravityWalker>();
            walker.SetTraversalInputEnabled(hasFocus && Time.timeScale > 0f);
        }
        void OnApplicationFocus(bool focus)
        {
            hasFocus = focus;
            if (!focus) ClearControls();
        }
        void OnApplicationPause(bool paused) { if (paused) ClearControls(); }

        public void Collect()
        {
            StarsCollected++;
        }
    }
}
