using System;
using System.Linq;
using B612;
using UnityEngine;

public static class HorizonPrototypeValidation
{
    static void Require(bool condition,string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static object Validate()
    {
        Require(Application.isPlaying,"Run in Play mode.");
        var s=UnityEngine.Object.FindFirstObjectByType<BaobabSurge>();
        var hints=UnityEngine.Object.FindFirstObjectByType<SproutHorizonIndicators>();
        bool oldRandom=s.randomizeSeed, oldEnabled=s.enabled;
        int oldSeed=s.seed, oldCap=s.maxActiveSprouts;
        Vector3 cameraPosition=hints.viewCamera.transform.position;
        Quaternion cameraRotation=hints.viewCamera.transform.rotation;
        int hiddenCases=0, horizons=0, visibleCases=0;
        float minimumDistance=float.PositiveInfinity;
        try
        {
            s.enabled=false; s.randomizeSeed=false; s.seed=612; s.maxActiveSprouts=6;
            s.Begin(); s.Advance(60f);
            Require(s.ActiveSproutCount==6 && s.SpawnedCount==6,"Exceeded six active sprouts.");
            Require(s.CapacityBlockedSpawns==15 && s.SkippedSpawns==0,"Full capacity should not count as invalid placement.");
            var first=s.Spawned[0];
            Require(s.RemoveSprout(first.instance) && !first.slot.occupied && s.ActiveSproutCount==5,"Removal did not release its slot and capacity.");
            Require(!hints.IsBehindPlanet(first),"Removed sprout still has a marker target.");
            s.Advance(3f);
            Require(s.ActiveSproutCount==6 && s.SpawnedCount==7,"Next spawn did not refill the free slot.");
            var inactive=s.Spawned.First(r=>r.IsActive); inactive.instance.SetActive(false);
            s.Advance(3f);
            Require(inactive.released && !inactive.slot.occupied && s.ActiveSproutCount==6 && s.SpawnedCount==8,"External disable did not free/refill capacity.");
            var active=s.Spawned.Where(r=>r.IsActive).ToArray();
            for (int i=0;i<active.Length;i++)
                for (int j=0;j<i;j++) minimumDistance=Mathf.Min(minimumDistance,Vector3.Distance(s.surface.WorldPoint(active[i].slot),s.surface.WorldPoint(active[j].slot)));
            Require(minimumDistance>=s.minimumSpacing,"Refill violated spacing.");
            foreach (var record in active)
            {
                Vector3 p=record.instance.transform.position, normal=record.instance.transform.up;
                hints.viewCamera.transform.position=p+normal*3.5f;
                hints.viewCamera.transform.LookAt(p+normal*0.25f);
                Require(!hints.IsBehindPlanet(record),"Visible sprout incorrectly marked as behind the planet."); visibleCases++;
                Vector3 centre=s.surface.transform.position;
                hints.viewCamera.transform.position=centre-(p-centre).normalized*(s.surface.ground.bounds.extents.magnitude+3f);
                hints.viewCamera.transform.LookAt(p);
                Require(hints.IsBehindPlanet(record),"Back-side sprout was not detected as occluded."); hiddenCases++;
                Require(hints.TryGetHorizon(record,out var rim) && float.IsFinite(rim.x) && (rim-centre).magnitude>1f,"No finite horizon found for hidden sprout."); horizons++;
            }
            // Exercise every compass direction of the actual third-person view around the player.
            Vector3 up=(s.player.position-s.surface.transform.position).normalized;
            Vector3 forward=Vector3.ProjectOnPlane(s.player.forward,up).normalized;
            for (int yaw=0;yaw<360;yaw+=45)
            {
                hints.viewCamera.transform.position=s.player.position+up*2f+Quaternion.AngleAxis(yaw,up)*forward*4.8f;
                hints.viewCamera.transform.LookAt(s.player.position+up*0.5f,up);
                foreach (var record in active)
                    if (hints.IsBehindPlanet(record))
                    {
                        hiddenCases++;
                        Require(hints.TryGetHorizon(record,out var rim) && float.IsFinite(rim.x),$"Horizon missing at yaw {yaw}."); horizons++;
                    }
            }
            s.Advance(120f); int count=s.SpawnedCount; s.Advance(20f);
            Require(s.IsComplete && s.ActiveSproutCount==6 && s.SpawnedCount==count,"Completion did not stop spawning.");
            return new { cap=6, refillTotal=8, minimumDistance, visibleCases, hiddenCases, horizons, completed=true };
        }
        finally
        {
            hints.viewCamera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
            s.randomizeSeed=oldRandom; s.seed=oldSeed; s.maxActiveSprouts=oldCap; s.enabled=oldEnabled;
            s.Begin();
        }
    }
}
