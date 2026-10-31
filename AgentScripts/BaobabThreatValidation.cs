using System;
using System.Linq;
using System.Collections.Generic;
using B612;
using UnityEngine;
using UnityEngine.EventSystems;

// Run in Play mode. Fixtures are runtime-only and are discarded by retry/stop.
public static class BaobabThreatValidation
{
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static void Near(float actual, float expected, string message)
    { Require(Mathf.Abs(actual-expected)<.015f, $"{message}: expected {expected}, got {actual}."); }

    static BaobabSurge.SpawnRecord[] Fixture(BaobabSurge surge)
    {
        surge.Begin(); surge.Advance(15f);
        var records=surge.Spawned.Where(r=>r.IsActive).ToArray();
        Require(records.Length==6,"Expected six valid spawn placements.");
        return records;
    }
    static void Ages(BaobabSurge surge,BaobabSurge.SpawnRecord[] records,params float[] ages)
    { for(int i=0;i<records.Length;i++) records[i].spawnedAt=surge.SimulationSeconds-ages[i]; }

    public static object Validate()
    {
        Require(Application.isPlaying,"Run in Play mode.");
        var meter=UnityEngine.Object.FindFirstObjectByType<BaobabThreatMeter>();
        var surge=meter.surge;
        var hud=UnityEngine.Object.FindFirstObjectByType<BaobabThreatHUD>();
        var hand=UnityEngine.Object.FindFirstObjectByType<LambCardHand>();
        surge.enabled=false; meter.enabled=false; surge.randomizeSeed=false; surge.seed=612;
        Time.timeScale=1f;
        var basic=BaobabGrowth.Basic;
        Near(basic.Evaluate(0),2,"New sprout"); Near(basic.Evaluate(30),11,"30-second sprout");
        Near(basic.Evaluate(10000),25,"Individual cap");
        Require(basic.Rate(30)>basic.Rate(10) && basic.Rate(10000)==0,"Acceleration/capped rate failed.");
        var records=Fixture(surge);
        Require(records.Skip(1).All(r=>r.spawnedAt==15f),"Catch-up spawns were aged from scheduled time.");
        Ages(surge,records,40,40,30,30,20,10); meter.Evaluate(); Near(meter.RawThreat,65,"Mixed ages");
        float capacity=meter.Capacity;
        Require(surge.RemoveSprout(records[0].instance),"First removal failed.");
        Require(!surge.RemoveSprout(records[0].instance),"Duplicate removal accepted.");
        meter.Evaluate(); Near(meter.RawThreat,49,"Old-sprout removal"); Near(meter.Capacity,capacity,"Fixed denominator");
        records[1].instance.SetActive(false); meter.Evaluate(); Near(meter.RawThreat,33,"Inactive sprout exclusion");
        UnityEngine.Object.DestroyImmediate(records[2].instance); meter.Evaluate(); Near(meter.RawThreat,22,"Destroyed sprout exclusion");
        foreach(var r in records) if(r.IsActive) surge.RemoveSprout(r.instance);
        meter.Evaluate(); Near(meter.RawThreat,0,"No active sprouts");

        records=Fixture(surge); Ages(surge,records,40,40,30,30,20,10);
        surge.RemoveSprout(records[5].instance); meter.Evaluate(); Near(meter.RawThreat,61,"Young-sprout removal");
        records=Fixture(surge);
        // A shared settings edit cannot change a run that has already started.
        float oldCapacity=meter.profile.threatCapacity,oldLinear=meter.profile.defaultType.linearGrowth;
        try
        {
            meter.profile.threatCapacity=999f; meter.profile.defaultType.linearGrowth=99f;
            Ages(surge,records,30,30,30,30,30,30); meter.Evaluate();
            Near(meter.Capacity,100,"Capacity snapshot"); Near(meter.RawThreat,66,"Growth snapshot");
        }
        finally { meter.profile.threatCapacity=oldCapacity; meter.profile.defaultType.linearGrowth=oldLinear; }

        // The spawn timer ends, but a survivor continues growing and can still be removed.
        surge.Begin(); surge.RemoveSprout(surge.Spawned[0].instance); surge.Advance(120f);
        records=surge.Spawned.Where(r=>r.IsActive).ToArray();
        foreach(var r in records.Skip(1)) surge.RemoveSprout(r.instance);
        meter.Evaluate(); Require(meter.State==BaobabThreatMeter.Phase.Clearing,"Missing clearing phase.");
        Near(meter.RawThreat,2,"Fresh late-frame sprout"); surge.Advance(30f); meter.Evaluate();
        Near(surge.ElapsedSeconds,120,"Bounded timer"); Near(surge.SimulationSeconds,150,"Unbounded growth clock");
        Near(meter.RawThreat,11,"Growth after 120 seconds");
        surge.RemoveSprout(records[0].instance); meter.Evaluate();
        Require(meter.State==BaobabThreatMeter.Phase.Resolved && meter.Fraction==0,"Empty cleared planet not resolved.");

        records=Fixture(surge); Ages(surge,records,41.2f,41.2f,41.2f,41.2f,41.2f,41.2f);
        surge.RemoveSprout(records[0].instance); meter.Evaluate();
        Require(!meter.IsGameOver,"Same-frame removal did not precede defeat decision.");

        // Reuse the lamb fixture to verify that a real reserved/eating sprout continues growing.
        records=Fixture(surge);
        var walker=hand.player; Vector3 centre=surge.surface.transform.position;
        Vector3 forward=Vector3.ProjectOnPlane(walker.transform.forward,walker.Up).normalized;
        for(int i=1;i<records.Length;i++) Place(surge,records[i],centre-walker.Up*6f+forward*(i-3)*.1f);
        Place(surge,records[0],walker.transform.position+forward*1.5f);
        hand.SelectCard(0); Require(hand.TrySummonSelected(),"Could not summon eating-test lamb.");
        var lamb=hand.LambForCard(0); lamb.enabled=false;
        Require(lamb.Target==records[0],"Lamb did not reserve fixture sprout.");
        Ages(surge,records,20,0,0,0,0,0); meter.Evaluate(); float before=meter.RawThreat;
        surge.Advance(5); lamb.Advance(5); meter.Evaluate();
        Require(records[0].IsActive && lamb.EatingProgress==5 && meter.RawThreat>before,"Eating suppressed threat growth.");
        surge.Advance(5); float expected=surge.Spawned.Where(r=>r.IsActive && r!=records[0]).Sum(r=>meter.Contribution(r));
        lamb.Advance(5); meter.Evaluate();
        Require(!records[0].IsActive && lamb.SproutsEaten==1,"Eating did not remove the sprout.");
        Near(meter.RawThreat,expected,"Eating removes only its contribution");

        records=Fixture(surge); Ages(surge,records,55,55,55,55,55,55);
        int events=0; Action onGameOver=()=>events++; meter.GameOver+=onGameOver;
        float lambAge=lamb.Age;
        meter.Evaluate(); meter.Evaluate();
        Require(meter.IsGameOver && events==1,"Defeat was not latched exactly once.");
        Require(Time.timeScale==0 && !meter.playerInput.enabled && !meter.cameraInput.enabled,"Game progression/input not frozen.");
        Require(hud.gameOverPanel.activeInHierarchy && hud.percentLabel.text=="100%" && hud.fill.anchorMax.x==1,"Game-over HUD not at 100%.");
        float clock=surge.SimulationSeconds; int spawned=surge.SpawnedCount;
        surge.Advance(100); lamb.Advance(100);
        Require(surge.SimulationSeconds==clock && surge.SpawnedCount==spawned && lamb.Age==lambAge,"Progress changed after defeat.");
        Require(!hand.TrySummonSelected() && !surge.RemoveSprout(records[0].instance),"Post-defeat gameplay accepted.");
        records[0].instance.SetActive(false); meter.Evaluate(); Require(meter.IsGameOver && events==1,"Defeat reversed after removal.");
        meter.GameOver-=onGameOver;
        return new {passed=true,mixedThreat=65,oldRemoved=49,youngRemoved=61,postTimerAge=30,
            defeatEvents=events,postDefeatFrozen=true};
    }

    static void Place(BaobabSurge surge,BaobabSurge.SpawnRecord record,Vector3 near)
    {
        var surface=surge.surface;
        Require(surface.TryProject(surface.transform.InverseTransformVector(near-surface.transform.position),out var point,out var normal),"Could not place sprout fixture.");
        record.slot.localPoint=surface.transform.InverseTransformPoint(point);
        record.slot.localNormal=surface.transform.InverseTransformDirection(normal);
        record.instance.transform.position=point;
    }

    public static object RunUnattended()
    {
        var meter=UnityEngine.Object.FindFirstObjectByType<BaobabThreatMeter>(); var surge=meter.surge;
        surge.enabled=false; meter.enabled=false; surge.randomizeSeed=false; surge.seed=612;
        surge.Begin();
        while(!meter.IsGameOver && surge.SimulationSeconds<60f) { surge.Advance(.01f); meter.Evaluate(); }
        Require(meter.IsGameOver && Mathf.Abs(surge.SimulationSeconds-48.47f)<.05f,"Unexpected unattended defeat time.");
        return new {passed=true,defeatSeconds=surge.SimulationSeconds,threat=meter.RawThreat,active=surge.ActiveSproutCount};
    }

    // Run on a later frame, after the newly activated overlay enters uGUI's graphic registry.
    public static object ValidateUI()
    {
        var hud=UnityEngine.Object.FindFirstObjectByType<BaobabThreatHUD>();
        Require(hud.meter.IsGameOver && hud.gameOverPanel.activeInHierarchy,"Expected the game-over overlay.");
        var data=new PointerEventData(EventSystem.current) { position=RectTransformUtility.WorldToScreenPoint(null,hud.retryButton.transform.position) };
        var hits=new List<RaycastResult>(); EventSystem.current.RaycastAll(data,hits);
        Require(hits.Count>0 && hits[0].gameObject==hud.retryButton.gameObject,"Retry button is blocked from pointer input.");
        return new {passed=true,retryRaycast=true,percentage=hud.percentLabel.text,paused=Time.timeScale==0};
    }
}
