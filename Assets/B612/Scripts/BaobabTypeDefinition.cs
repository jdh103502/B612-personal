using UnityEngine;

namespace B612
{
    [CreateAssetMenu(menuName = "B612/Baobab Type")]
    public sealed class BaobabTypeDefinition : ScriptableObject
    {
        public string typeId = "basic";
        public string displayName = "Basic Sprout";
        [Min(0f)] public float initialThreat = 2f;
        [Min(0f)] public float linearGrowth = 0.15f;
        [Min(0f)] public float quadraticGrowth = 0.005f;
        [Min(0f)] public float maximumThreat = 25f;

        public BaobabGrowth Snapshot() => new BaobabGrowth
        {
            initial = Mathf.Max(0f, initialThreat), linear = Mathf.Max(0f, linearGrowth),
            quadratic = Mathf.Max(0f, quadraticGrowth), maximum = Mathf.Max(0f, maximumThreat)
        };
    }

    [System.Serializable]
    public struct BaobabGrowth
    {
        public float initial, linear, quadratic, maximum;
        public static BaobabGrowth Basic => new BaobabGrowth { initial = 2f, linear = .15f, quadratic = .005f, maximum = 25f };
        public float Evaluate(float age)
        {
            age = Mathf.Max(0f, age);
            return Mathf.Min(maximum, initial + linear * age + quadratic * age * age);
        }
        public float Rate(float age) => Evaluate(age) >= maximum ? 0f : linear + 2f * quadratic * Mathf.Max(0f, age);
    }
}
