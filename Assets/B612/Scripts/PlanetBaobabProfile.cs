using UnityEngine;

namespace B612
{
    /// <summary>Threat settings only. Spawn quantities remain on BaobabSurge in this prototype.</summary>
    [CreateAssetMenu(menuName = "B612/Planet Baobab Profile")]
    public sealed class PlanetBaobabProfile : ScriptableObject
    {
        [Min(.01f)] public float threatCapacity = 100f;
        public BaobabTypeDefinition defaultType;
    }
}
