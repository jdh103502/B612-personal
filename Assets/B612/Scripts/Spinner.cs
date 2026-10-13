using UnityEngine;

namespace B612
{
    /// <summary>Small ambient motion: constant spin and an optional bob.</summary>
    public class Spinner : MonoBehaviour
    {
        public Vector3 axis = Vector3.up;
        public float degreesPerSecond = 20f;
        public float bobHeight = 0f;
        public float bobSpeed = 1f;

        Vector3 basePos;

        void Start() { basePos = transform.localPosition; }

        void Update()
        {
            transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.Self);
            if (bobHeight > 0f) transform.localPosition = basePos + Vector3.up * Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        }
    }
}
