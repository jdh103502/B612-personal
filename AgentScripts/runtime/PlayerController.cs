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
            if (walker.Suspended) return;   // flying on a launch star
            Vector2 move = Vector2.zero;
            bool jumpDown = false, jumpHeld = false, respawn = false;
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
            }
            var gp = Gamepad.current;
            if (gp != null)
            {
                var stick = gp.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.04f) move += stick;
                jumpDown |= gp.buttonSouth.wasPressedThisFrame;
                jumpHeld |= gp.buttonSouth.isPressed;
            }
            move = Vector2.ClampMagnitude(move, 1f);

            walker.moveInput = cameraRig ? cameraRig.PlanarDirection(move, walker.Up) : new Vector3(move.x, 0f, move.y);
            walker.jumpHeld = jumpHeld;
            if (jumpDown)
            {
                if (NearbyLaunch && !NearbyLaunch.Busy && walker.Grounded) NearbyLaunch.Launch(walker);
                else walker.Jump();
            }

            bool lost = walker.TimeWithoutGravity > 2.5f || (spawnPoint && Vector3.Distance(transform.position, spawnPoint.position) > respawnDistance);
            if (respawn || lost) Respawn();
        }

        public void Respawn()
        {
            if (spawnPoint) walker.Teleport(spawnPoint.position, spawnPoint.up);
        }

        public void Collect()
        {
            StarsCollected++;
        }
    }
}
