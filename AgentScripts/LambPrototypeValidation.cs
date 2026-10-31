using System;
using System.Linq;
using B612;
using UnityEngine;

// Play-mode integration checks with temporary surface fixtures; no scene assets are edited.
public static class LambPrototypeValidation
{
    static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }

    public static object Validate()
    {
        var hand=UnityEngine.Object.FindFirstObjectByType<LambCardHand>();
        var surge=hand.surge; var walker=hand.player; var surface=surge.surface;
        Require(Application.isPlaying && hand.InHandCount==hand.CardCount && walker.Grounded,"Run with all cards in hand and the player grounded.");
        bool oldSurgeEnabled=surge.enabled,oldRandom=surge.randomizeSeed; int oldSeed=surge.seed;
        Vector3 oldPosition=walker.transform.position,oldUp=walker.Up;
        Quaternion oldRotation=walker.transform.rotation;
        LambAgent first=null,second=null;
        try
        {
            surge.enabled=false; surge.randomizeSeed=false; surge.seed=612; surge.Begin(); surge.Advance(15f);
            var records=surge.Spawned.ToArray();
            Vector3 centre=surface.transform.position;
            Vector3 radial=(oldPosition-centre).normalized;
            Vector3 forward=Vector3.ProjectOnPlane(walker.transform.forward,radial).normalized;
            Vector3 right=Vector3.Cross(radial,forward).normalized;
            for(int i=1;i<records.Length;i++) Place(surface,records[i],-radial+right*(i-3)*.08f);
            Place(surface,records[0],oldPosition-centre+forward*1.85f);
            Require(hand.SelectCard(0),"Could not select card 1.");
            Require(hand.TryGetPlacement(out var p,out _,out var intended) && intended==records[0],"Nearby intended sprout not selected.");
            float assistedDistance=LambCardHand.SurfaceDistance(p,surface.WorldPoint(intended.slot),centre);
            Require(assistedDistance<hand.eatingRadius,"Placement assistance did not put the intended sprout in range.");
            Require(hand.TrySummonSelected() && hand.InHandCount==3,"Summon did not consume exactly one card.");
            first=hand.LambForCard(0); first.enabled=false;
            Require(first.Target==records[0],"Summoned lamb did not lock the intended sprout.");
            Require(hand.SelectCard(1) && hand.TrySummonSelected(),"Second card could not be summoned.");
            second=hand.LambForCard(1); second.enabled=false;
            Require(second.Target!=records[0],"Two lambs reserved the same sprout.");
            first.Advance(9.99f);
            Require(records[0].IsActive && first.SproutsEaten==0,"Sprout was eaten before 10 seconds.");
            first.Advance(.01f);
            Require(!records[0].IsActive && first.SproutsEaten==1 && surge.ActiveSproutCount==5,"Ten-second bite did not release the active sprout slot.");
            second.gameObject.SetActive(false); hand.RecoverCard(1,second); UnityEngine.Object.Destroy(second.gameObject); second=null;
            // Just outside the nominal circle but inside its narrow grace band should still work.
            Vector3 lambRadial=(first.transform.position-centre).normalized;
            Vector3 side=Vector3.Cross(lambRadial,forward).normalized;
            PlaceAtDistance(surface,records[1],first.transform.position,side,1.82f);
            float edgeDistance=LambCardHand.SurfaceDistance(first.transform.position,surface.WorldPoint(records[1].slot),centre);
            Require(edgeDistance>hand.eatingRadius && edgeDistance<=hand.eatingRadius+hand.rangeGrace,$"Grace test fixture not at the range edge: {edgeDistance}.");
            PlaceAtDistance(surface,records[2],first.transform.position,-side,2.3f);
            first.Advance(10f);
            Require(first.SproutsEaten==2 && !records[1].IsActive && records[2].IsActive,"Second bite or limited range failed.");
            Require(first.State==LambAgent.LambState.Pickup && Mathf.Abs(first.Age-20f)<.001f,"Lamb did not become a pickup at 20 seconds.");
            walker.Teleport(first.transform.position+first.transform.up*.03f,first.transform.up);
            first.Advance(.79f);
            Require(first.State==LambAgent.LambState.Pickup,"Pickup cue was skipped while player was nearby.");
            first.Advance(.02f);
            Require(first.State==LambAgent.LambState.Collected && hand.InHandCount==4,"Nearby pickup did not return exactly one card.");
            Require(!hand.RecoverCard(0,first),"Duplicate card recovery was accepted.");
            int cardsBefore=hand.InHandCount;
            walker.Suspend(true);
            Require(!hand.TrySummonSelected() && hand.InHandCount==cardsBefore,"Invalid airborne summon consumed a card.");
            walker.Suspend(false);
            return new {passed=true,cardsRecovered=hand.InHandCount,assistedDistance,edgeDistance,firstBiteSeconds=10,
                secondBiteAndExpirySeconds=20,reservations=true,farSproutPreserved=true,duplicateRecoveryRejected=true,invalidSummonPreservedCard=true};
        }
        finally
        {
            walker.Suspend(false); walker.Teleport(oldPosition,oldUp);
            walker.transform.rotation=oldRotation; walker.GetComponent<Rigidbody>().rotation=oldRotation;
            if(first && first.State!=LambAgent.LambState.Collected) { hand.RecoverCard(first.CardIndex,first); first.gameObject.SetActive(false); UnityEngine.Object.Destroy(first.gameObject); }
            if(second) { hand.RecoverCard(second.CardIndex,second); second.gameObject.SetActive(false); UnityEngine.Object.Destroy(second.gameObject); }
            surge.enabled=oldSurgeEnabled; surge.randomizeSeed=oldRandom; surge.seed=oldSeed; surge.Begin();
            hand.SelectCard(0);
        }
    }

    public static object PrepareInputDemo()
    {
        var hand=UnityEngine.Object.FindFirstObjectByType<LambCardHand>();
        var surge=hand.surge; var walker=hand.player; var surface=surge.surface;
        Require(hand.InHandCount==hand.CardCount,"Use a fresh hand for the input demo.");
        surge.randomizeSeed=false; surge.seed=612; surge.Begin(); surge.Advance(15f);
        var records=surge.Spawned.ToArray();
        Vector3 radial=(walker.transform.position-surface.transform.position).normalized;
        Vector3 forward=Vector3.ProjectOnPlane(walker.transform.forward,radial).normalized;
        Vector3 right=Vector3.Cross(radial,forward);
        for(int i=1;i<records.Length;i++) Place(surface,records[i],-radial+right*(i-3)*.08f);
        Place(surface,records[0],walker.transform.position-surface.transform.position+forward*1.8f);
        return new {ready=true,intended=records[0].instance.name};
    }

    static void Place(PlanetSurfaceGrid surface,BaobabSurge.SpawnRecord record,Vector3 direction)
    {
        Require(surface.TryProject(surface.transform.InverseTransformVector(direction.normalized),out var p,out var n),"Fixture missed planet ground.");
        record.slot.localPoint=surface.transform.InverseTransformPoint(p);
        record.slot.localNormal=surface.transform.localToWorldMatrix.transpose.MultiplyVector(n).normalized;
        record.instance.transform.SetPositionAndRotation(p-n*.015f,Quaternion.FromToRotation(Vector3.up,n));
    }

    static void PlaceAtDistance(PlanetSurfaceGrid surface,BaobabSurge.SpawnRecord record,Vector3 origin,Vector3 tangent,float distance)
    {
        Vector3 radial=(origin-surface.transform.position).normalized;
        tangent=Vector3.ProjectOnPlane(tangent,radial).normalized;
        float low=0f,high=1.2f;
        for(int i=0;i<18;i++)
        {
            float mid=(low+high)*.5f;
            Vector3 direction=radial*Mathf.Cos(mid)+tangent*Mathf.Sin(mid);
            Require(surface.TryProject(surface.transform.InverseTransformVector(direction),out var p,out _),"Range fixture missed ground.");
            if(LambCardHand.SurfaceDistance(origin,p,surface.transform.position)<distance) low=mid; else high=mid;
        }
        float angle=(low+high)*.5f;
        Place(surface,record,radial*Mathf.Cos(angle)+tangent*Mathf.Sin(angle));
    }
}
