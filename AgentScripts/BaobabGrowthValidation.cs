using System;
using System.Linq;
using B612;
using UnityEngine;

public static class BaobabGrowthValidation
{
    static LambCardHand hand;
    static BaobabSurge surge;
    static BaobabThreatMeter meter;
    static void Require(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    static void Near(float actual,float expected,string message) {Require(Mathf.Abs(actual-expected)<.015f,$"{message}: {actual} != {expected}");}
    static void Setup()
    {
        Require(Application.isPlaying,"Run in Play mode.");
        hand=UnityEngine.Object.FindFirstObjectByType<LambCardHand>();surge=hand.surge;meter=surge.threat;
        Require(hand.InHandCount==hand.CardCount && hand.player.Grounded,"Use all cards and a grounded player.");
        surge.enabled=false;meter.enabled=false;Time.timeScale=0f;surge.randomizeSeed=false;surge.seed=612;
    }
    static BaobabSurge.SpawnRecord One(float age)
    {
        surge.targetSprouts=1;surge.Begin();surge.Advance(age);
        var record=surge.Spawned.Single();Place(record,hand.player.transform.position+Forward()*1.6f);
        return record;
    }
    static Vector3 Forward() => Vector3.ProjectOnPlane(hand.player.transform.forward,hand.player.Up).normalized;
    static void Place(BaobabSurge.SpawnRecord record,Vector3 near)
    {
        var surface=surge.surface;
        Require(surface.TryProject(surface.transform.InverseTransformVector(near-surface.transform.position),out var point,out var normal),"Fixture missed ground.");
        record.slot.localPoint=surface.transform.InverseTransformPoint(point);record.slot.localNormal=surface.transform.InverseTransformDirection(normal);
        record.instance.transform.SetPositionAndRotation(point-normal*.015f,Quaternion.FromToRotation(Vector3.up,normal));
    }
    static LambAgent Summon(BaobabSurge.SpawnRecord target)
    {
        hand.SelectCard(0);Require(hand.TrySummonSelected(),"Could not summon a lamb.");
        var lamb=hand.LambForCard(0);Require(lamb.Target==target,"Lamb did not reserve fixture.");return lamb;
    }
    static void Clear(LambAgent lamb)
    {
        if(!lamb)return;
        hand.RecoverCard(lamb.CardIndex,lamb);lamb.gameObject.SetActive(false);UnityEngine.Object.Destroy(lamb.gameObject);
    }
    public static object Validate()
    {
        Setup();
        int oldTarget=surge.targetSprouts;
        var position=hand.player.transform.position;var up=hand.player.Up;
        LambAgent lamb=null;
        try
        {
            var record=One(19.99f);int identity=record.instance.GetInstanceID();var slot=record.slot;
            Require(record.Stage==BaobabStage.Sprout,"Early sapling transition.");
            surge.Advance(20f-surge.SimulationSeconds);Require(record.Stage==BaobabStage.Sapling,"Missing 20-second transition.");
            Require(record.Visual.name=="Sapling Visual","Sapling FBX not installed.");
            surge.Advance(19.99f);Require(record.Stage==BaobabStage.Sapling,"Early tree transition.");
            surge.Advance(40f-surge.SimulationSeconds);Require(record.Stage==BaobabStage.Tree && record.Visual.name=="Tree Visual","Missing tree FBX transition.");
            Require(record.instance.GetInstanceID()==identity && record.slot==slot && surge.ActiveSproutCount==1,"Stage change replaced identity or slot.");
            Require(record.instance.GetComponentsInChildren<Renderer>().Length==1,"Old and new models are both active.");
            Require(record.instance.GetComponentsInChildren<Collider>().Length==0,"Growth unexpectedly added collisions.");

            var stages=new[]{BaobabStage.Sprout,BaobabStage.Sapling,BaobabStage.Tree};
            var ages=new[]{19f,39f,45f};var durations=new[]{7f,10f,13f};
            for(int i=0;i<stages.Length;i++)
            {
                record=One(ages[i]);lamb=Summon(record);
                Require(record.Stage==stages[i],"Wrong stage at feeding start.");Near(lamb.EatingDuration,durations[i],"Stage duration");
                int visual=record.Visual.GetInstanceID();
                meter.Evaluate();float before=meter.RawThreat;
                surge.Advance(durations[i]*.5f);lamb.Advance(durations[i]*.5f);
                Require(record.Stage==stages[i] && record.Visual.GetInstanceID()==visual,"Eating target changed stage/model at threshold.");
                Near(lamb.EatingDuration,durations[i],"Feeding duration changed mid-bite");Near(lamb.EatingFraction,.5f,"Stage pie fraction");
                Require(record.IsBeingEaten && UnityEngine.Object.FindFirstObjectByType<SproutHorizonIndicators>().IsBeingEaten(record),"Feeding target lost growth/hint lock.");
                meter.Evaluate();Require(meter.RawThreat>=before,"Threat was reset by a stage lock.");
                lamb.Advance(durations[i]*.5f-.01f);Require(record.IsActive,"Bite completed early.");
                lamb.Advance(.01f);Require(!record.IsActive && lamb.SproutsEaten==1 && !record.slot.occupied,"Bite did not remove/release the same target.");
                Require(record.Stage==stages[i],"Completion briefly grew the consumed target.");
                meter.Evaluate();Near(meter.RawThreat,0f,"Removed target contribution");
                Clear(lamb);lamb=null;
            }

            // Interruption lifts the lock; the survivor catches up to its actual age.
            record=One(19f);lamb=Summon(record);surge.Advance(4f);lamb.Advance(2f);
            Require(record.Stage==BaobabStage.Sprout,"Feeding did not hold sprout at 23 seconds.");
            lamb.transform.position+=lamb.transform.right*10f;lamb.Advance(.01f);
            Require(lamb.Target==null && !record.IsBeingEaten,"Out-of-range feeding retained its lock.");
            surge.RefreshLifeStages();Require(record.Stage==BaobabStage.Sapling,"Interrupted survivor did not resume growth.");
            Clear(lamb);lamb=null;

            // Lifecycle continues beyond the 120-second spawn timer.
            record=One(0f);surge.Advance(120f);Require(surge.IsComplete && record.Stage==BaobabStage.Tree,"Post-surge growth stopped.");
            surge.Advance(30f);Near(surge.SimulationSeconds,150f,"Growth clock after spawning");

            // Exactly 7+13 seconds: the last bite counts before the 20-second expiry.
            surge.targetSprouts=2;surge.Begin();surge.Advance(60f);
            var pair=surge.Spawned.ToArray();Require(pair.Length==2,"Missing mixed-stage fixture.");
            // Spawn #2 is actually born at 60s, while #1 has matured.
            var tree=pair[0];var sprout=pair[1];
            Place(sprout,position+Forward()*1.6f);Place(tree,position+Forward()*1.9f);
            lamb=Summon(sprout);lamb.Advance(20f);
            Require(lamb.SproutsEaten==2 && !tree.IsActive && !sprout.IsActive,"Seven-plus-thirteen-second sequence failed.");
            Require(lamb.State==LambAgent.LambState.Pickup,"Lamb did not expire after exact final bite.");
            Clear(lamb);lamb=null;
            return new {passed=true,saplingAt=20,treeAt=40,eatingSeconds=durations,feedingModelLocked=true,
                pieAtHalf=.5f,interruptionResumesGrowth=true,identityPreserved=true,exactTwentySecondBites=true};
        }
        finally
        {
            Clear(lamb);hand.player.Teleport(position,up);
            surge.targetSprouts=oldTarget;surge.Begin();hand.SelectCard(0);Time.timeScale=1f;
        }
    }

    public static object Showcase()
    {
        Setup();surge.targetSprouts=40;surge.Begin();surge.Advance(6f);
        var records=surge.Spawned.ToArray();var forward=Forward();var up=hand.player.Up;var right=Vector3.Cross(up,forward).normalized;
        var origin=hand.player.transform.position;
        for(int i=0;i<records.Length;i++)
        {
            records[i].spawnedAt=surge.SimulationSeconds-i*20f;
            Place(records[i],origin+forward*1.8f+right*(i-1)*1.8f);
        }
        surge.RefreshLifeStages();meter.Evaluate();
        Camera.main.GetComponent<OrbitCamera>().enabled=false;
        Camera.main.transform.position=origin-forward*4.4f+up*2.6f;
        Camera.main.transform.LookAt(origin+forward*1.8f+up*.65f,up);
        return new {stages=records.Select(r=>r.Stage.ToString()).ToArray(),threat=meter.RawThreat,paused=true};
    }
}
