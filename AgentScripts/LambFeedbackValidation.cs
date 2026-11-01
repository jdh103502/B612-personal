using System;
using System.Linq;
using B612;
using UnityEngine;

// Run in Play mode with Unity CLI run_script. Fixtures/camera changes are never saved.
public static class LambFeedbackValidation
{
    static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }

    public static object PrepareEating()
    {
        var hand=UnityEngine.Object.FindFirstObjectByType<LambCardHand>();
        Require(Application.isPlaying && hand.InHandCount==hand.CardCount && hand.player.Grounded,"Use a grounded player with all cards in hand.");
        var surge=hand.surge; var surface=surge.surface;
        surge.enabled=false; surge.randomizeSeed=false; surge.seed=612; surge.Begin(); surge.Advance(15f);
        Vector3 origin=hand.player.transform.position,up=hand.player.Up;
        Vector3 radial=(origin-surface.transform.position).normalized;
        Vector3 forward=Vector3.ProjectOnPlane(hand.player.transform.forward,up).normalized,right=Vector3.Cross(up,forward);
        var records=surge.Spawned.ToArray();
        for(int i=0;i<records.Length;i++)
        {
            Vector3 direction=i==0?(origin-surface.transform.position+forward*1.8f).normalized:(-radial+right*(i-3)*.08f).normalized;
            Require(surface.TryProject(surface.transform.InverseTransformVector(direction),out var p,out var n),"Fixture missed ground.");
            records[i].slot.localPoint=surface.transform.InverseTransformPoint(p);
            records[i].slot.localNormal=surface.transform.localToWorldMatrix.transpose.MultiplyVector(n).normalized;
            records[i].instance.transform.SetPositionAndRotation(p-n*.015f,Quaternion.FromToRotation(Vector3.up,n));
        }
        hand.SelectCard(0); Require(hand.TrySummonSelected(),"Summon failed.");
        var lamb=hand.LambForCard(0); lamb.enabled=false; lamb.Advance(lamb.EatingDuration*.5f);
        Camera.main.GetComponent<OrbitCamera>().enabled=false;
        Camera.main.transform.position=origin+forward*3.6f+right*2.4f+up*2.4f;
        Camera.main.transform.LookAt(origin+up*.45f+forward*.6f,up);
        var ui=UnityEngine.Object.FindFirstObjectByType<LambEatingIndicators>();
        Require(ui.TryGetPresentation(lamb,out _,out float fraction) && Mathf.Abs(fraction-.5f)<.001f,"Half-eaten UI is incorrect.");
        return new {progress=fraction,lambAge=lamb.Age,expectedVisible=true};
    }

    public static object Validate()
    {
        var hand=UnityEngine.Object.FindFirstObjectByType<LambCardHand>();
        var lamb=hand.LambForCard(0);
        var ui=UnityEngine.Object.FindFirstObjectByType<LambEatingIndicators>();
        var hints=ui.horizon;
        Require(lamb && lamb.State==LambAgent.LambState.Grazing && Mathf.Abs(lamb.EatingFraction-.5f)<.01f,"Prepare half-eating first.");
        var camera=ui.viewCamera;
        Vector3 oldPosition=camera.transform.position; Quaternion oldRotation=camera.transform.rotation;
        Vector3 playerPosition=hand.player.transform.position,playerUp=hand.player.Up;
        try
        {
            var graphic=ui.GetComponentsInChildren<EatingProgressGraphic>().Single();
            Require(ui.VisibleCount==1 && Mathf.Abs(graphic.Progress-.5f)<.001f,"Rendered pie not at 50%.");
            camera.transform.position=lamb.transform.position+lamb.transform.up*3.5f;
            camera.transform.LookAt(lamb.transform.position+lamb.transform.up*.6f);
            Require(ui.TryGetPresentation(lamb,out _,out var fraction) && Mathf.Abs(fraction-.5f)<.001f,"Visible lamb progress hidden.");
            Vector3 centre=hand.surge.surface.transform.position,radial=(lamb.transform.position-centre).normalized;
            camera.transform.position=centre-radial*(hand.surge.surface.ground.bounds.extents.magnitude+3f);
            camera.transform.LookAt(lamb.transform.position);
            Require(!ui.TryGetPresentation(lamb,out _,out _),"Eating UI reveals lamb through the planet.");
            lamb.Advance(lamb.EatingDuration-lamb.EatingProgress);
            Require(lamb.SproutsEaten==1 && !ui.TryGetPresentation(lamb,out _,out _),"Idle/completed bite retained progress.");
            lamb.Advance(hand.activeSeconds-lamb.Age);
            Require(lamb.State==LambAgent.LambState.Pickup && !ui.TryGetPresentation(lamb,out _,out _),"Pickup retained an eating pie.");
            Require(hints.IsPickupBehindPlanet(lamb) && hints.TryGetHorizon(lamb.transform.position,out var rim) && float.IsFinite(rim.x),"Hidden pickup has no horizon hint.");
            float near=hints.ScaleForDistance(.1f),far=hints.ScaleForDistance(1000f),previous=near;
            for(float distance=0;distance<=30;distance+=.25f)
            {
                float scale=hints.ScaleForDistance(distance);
                Require(scale<=previous+.00001f && scale>=hints.farScale-.00001f && scale<=hints.nearScale+.00001f,"Bubble scale not monotonic/bounded.");
                previous=scale;
            }
            Require(near>far && Mathf.Abs(near-hints.nearScale)<.001f && Mathf.Abs(far-hints.farScale)<.001f,"Near/far size limits failed.");
            camera.transform.position=lamb.transform.position+lamb.transform.up*3.5f;
            camera.transform.LookAt(lamb.transform.position+lamb.transform.up*.1f);
            Require(!hints.IsPickupBehindPlanet(lamb),"Visible pickup retains a hidden-object hint.");
            return new {passed=true,renderedProgress=.5f,hiddenProgressSuppressed=true,idleProgressSuppressed=true,
                pickupHasHorizon=true,visiblePickupHintSuppressed=true,nearScale=near,farScale=far,monotonicDistanceCases=121};
        }
        finally { camera.transform.SetPositionAndRotation(oldPosition,oldRotation); }
    }

    public static object ShowHiddenPickup()
    {
        var hand=UnityEngine.Object.FindFirstObjectByType<LambCardHand>();
        var lamb=hand.LambForCard(0); var surface=hand.surge.surface;
        Require(lamb && lamb.State==LambAgent.LambState.Pickup,"Run Validate first.");
        Vector3 radial=-(lamb.transform.position-surface.transform.position).normalized;
        Require(surface.TryProject(surface.transform.InverseTransformVector(radial),out var p,out var n),"No opposite ground.");
        Vector3 forward=Vector3.ProjectOnPlane(Vector3.forward,n).normalized;
        if(forward.sqrMagnitude<.001f) forward=Vector3.ProjectOnPlane(Vector3.right,n).normalized;
        hand.player.Teleport(p+n*.04f,n);
        Quaternion rotation=Quaternion.LookRotation(forward,n);
        hand.player.transform.rotation=rotation; hand.player.GetComponent<Rigidbody>().rotation=rotation;
        Camera.main.transform.position=p+n*2.5f-forward*4.8f;
        Camera.main.transform.LookAt(p+n*.5f,n);
        lamb.enabled=true;
        var hints=UnityEngine.Object.FindFirstObjectByType<SproutHorizonIndicators>();
        Require(hints.IsPickupBehindPlanet(lamb),"Pickup not hidden from the opposite side.");
        return new {hidden=true,distanceScale=hints.ScaleForPosition(lamb.transform.position),expectedIcon="Lamb"};
    }
}
