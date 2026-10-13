using UnityEngine;

namespace B612
{
    /// <summary>The fox trots after the prince, hops when he hops, and catches up if it gets left behind.</summary>
    [RequireComponent(typeof(GravityWalker))]
    public class FoxFollower : MonoBehaviour
    {
        public GravityWalker leader;
        public float followDistance = 0.8f;
        public float sprintDistance = 2.0f;
        public float teleportDistance = 7f;
        public float jumpReaction = 0.18f;

        GravityWalker self;
        float leaderJumpTime = -10f;
        float cooldown;

        void Awake()
        {
            self = GetComponent<GravityWalker>();
        }

        void Start()
        {
            if (!leader) return;
            var a = GetComponent<Collider>();
            var b = leader.GetComponent<Collider>();
            if (a && b) Physics.IgnoreCollision(a, b);
        }

        void OnEnable() { if (leader) leader.Jumped += OnLeaderJumped; }
        void OnDisable() { if (leader) leader.Jumped -= OnLeaderJumped; }
        void OnLeaderJumped() { leaderJumpTime = Time.time; }

        void Update()
        {
            if (!leader || self.Suspended) return;
            if (leader.Suspended) { self.moveInput = Vector3.zero; self.jumpHeld = false; return; }   // waiting for its own launch
            Vector3 to = leader.transform.position - transform.position;
            Vector3 flat = Vector3.ProjectOnPlane(to, self.Up);
            float d = flat.magnitude;
            float h = Vector3.Dot(to, self.Up);

            float want = d > followDistance ? Mathf.Clamp01((d - followDistance) / 0.8f + 0.15f) : 0f;
            self.speedMultiplier = d > sprintDistance ? 1.4f : 1f;
            self.moveInput = d > 0.01f ? flat / d * want : Vector3.zero;

            cooldown -= Time.deltaTime;
            float since = Time.time - leaderJumpTime;
            bool echo = since > jumpReaction && since < jumpReaction + 0.3f && d < 2.6f;
            bool leaderElsewhere = leader.Source != null && leader.Source != self.Source && leader.Grounded;
            if (self.Grounded && cooldown <= 0f && (echo || h > 0.45f || leaderElsewhere))
            {
                self.Jump();
                cooldown = 0.5f;
                leaderJumpTime = -10f;
            }
            self.jumpHeld = !self.Grounded && self.VerticalSpeed > 0f;

            if (to.magnitude > teleportDistance || self.TimeWithoutGravity > 2f)
            {
                var p = leader.transform.position - leader.transform.forward * 0.6f + leader.Up * 0.3f;
                self.Teleport(p, leader.Up);
            }
        }
    }
}
