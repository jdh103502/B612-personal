using UnityEngine;

namespace B612
{
    /// <summary>A floating star. Spins and bobs; the prince picks it up by touching it.</summary>
    public class Collectible : MonoBehaviour
    {
        public float spinSpeed = 90f;
        public float bobHeight = 0.06f;

        Vector3 basePos;
        Vector3 baseScale;
        float seed;
        float takenAt = -1f;

        void Start()
        {
            basePos = transform.position;
            baseScale = transform.localScale;
            seed = UnityEngine.Random.value * 6f;
        }

        void Update()
        {
            transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
            transform.position = basePos + transform.up * Mathf.Sin(Time.time * 2f + seed) * bobHeight;
            if (takenAt >= 0f)
            {
                float k = (Time.time - takenAt) / 0.35f;
                transform.localScale = baseScale * Mathf.Max(0f, 1f + k * 0.6f) * (1f - k);
                if (k >= 1f) gameObject.SetActive(false);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            if (takenAt >= 0f) return;
            var player = other.GetComponentInParent<PlayerController>();
            if (!player) return;
            takenAt = Time.time;
            spinSpeed *= 6f;
            player.Collect();
        }
    }
}
