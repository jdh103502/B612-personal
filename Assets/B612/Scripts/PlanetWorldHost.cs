using System;
using UnityEngine;

namespace B612
{
    public sealed class PlanetWorldHost : MonoBehaviour
    {
        public PlanetGenerationSettings settings;
        public PlanetWorldInstance initialWorld;
        public PlanetWorldInstance ActiveWorld { get; private set; }
        public PlanetWorldInstance Initialize(PlanetData planet,CapsuleCollider capsule)
        {
            if(!initialWorld)throw new InvalidOperationException("Initial planet world is missing.");
            initialWorld.Prepare(planet,settings,capsule);ActiveWorld=initialWorld;return ActiveWorld;
        }
        public PlanetWorldInstance Prepare(PlanetData planet,CapsuleCollider capsule)
        {
            if(!settings||!settings.groundTemplate)throw new InvalidOperationException("Ground template is missing.");
            GameObject root=new GameObject("Prepared "+planet.spec.planetId);root.transform.SetParent(transform,false);
            PlanetWorldInstance world=null;
            try
            {
                world=root.AddComponent<PlanetWorldInstance>();
                var ground=Instantiate(settings.groundTemplate,root.transform);ground.name="GroundSurface";
                world.ground=ground.GetComponent<Collider>();world.groundRenderer=ground.GetComponent<Renderer>();
                world.surface=root.AddComponent<PlanetSurfaceGrid>();world.gravity=root.AddComponent<GravitySource>();world.gravity.enabled=false;
                world.spawn=new GameObject("Spawn Point").transform;world.spawn.SetParent(root.transform,false);
                world.Prepare(planet,settings,capsule);return world;
            }
            catch {root.SetActive(false);Destroy(root);throw;}
        }
        public void Activate(PlanetWorldInstance world)
        {
            if(ActiveWorld&&ActiveWorld!=world){ActiveWorld.gravity.enabled=false;ActiveWorld.gameObject.SetActive(false);}
            ActiveWorld=world;world.name="ActivePlanetWorld";world.gravity.enabled=true;
            Physics.SyncTransforms();
        }
    }
}
