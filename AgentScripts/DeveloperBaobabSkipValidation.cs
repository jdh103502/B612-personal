using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using B612;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// Focused Editor Play-mode check for the debug skip only; no full regression/build suite.
public static class DeveloperBaobabSkipValidation
{
    public static object Start()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Enter Play mode first.");
        new GameObject("Baobab skip validation").AddComponent<DeveloperBaobabSkipProbe>();
        return "Started: Temp/DeveloperBaobabSkipValidation.txt";
    }
}
[DefaultExecutionOrder(1500)]
public sealed class DeveloperBaobabSkipProbe : MonoBehaviour
{
    StarSystemFlowController flow;DeveloperDebugController debug;BaobabSurge surge;LambCardHand hand;
    readonly List<string> passed=new List<string>();
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    IEnumerator Start()
    {
        flow=FindFirstObjectByType<StarSystemFlowController>();debug=FindFirstObjectByType<DeveloperDebugController>();surge=flow.surge;hand=flow.hand;
        var run=Run();
        while(true)
        {
            object next=null;bool more=false;string error=null;
            try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception ex){error=ex.ToString();}
            if(error!=null){Finish(false,error);yield break;}
            if(!more){Finish(true,"");yield break;}
            yield return next;
        }
    }
    IEnumerator Run()
    {
        Check(flow.WorldReady && debug.flow==flow,"World/debug binding missing");
        Check(FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length==1,"Duplicate EventSystem");
        Check(debug.hud.skipButton && debug.hud.skipDescription.font.HasCharacters(DeveloperDebugHUD.SkipGlyphs),"UI binding/glyph missing");
        Check(!debug.RequestBaobabSkip(),"Closed debug accepted command");
        Time.timeScale=.6f;debug.Open();
        Check(debug.RequestBaobabSkip() && !debug.RequestBaobabSkip(),"Queue/duplicate guard failed");
        debug.Close();yield return null;yield return null;
        Check(!surge.WasSkippedByDebug && !flow.DebugSkipPending && Mathf.Approximately(Time.timeScale,.6f),"Close did not cancel/restore speed");
        passed.Add("Closed panel rejects; duplicate request rejected; closing cancels pending request and restores 0.6 speed");

        surge.Advance(6f);
        var records=surge.Spawned.Where(r=>r.IsActive).Take(3).ToArray();Check(records.Length==3,"Need three stage fixtures");
        records[0].spawnedAt=surge.SimulationSeconds-42f;records[1].spawnedAt=surge.SimulationSeconds-22f;surge.RefreshLifeStages();
        Check(records[0].Stage==BaobabStage.Tree && records[1].Stage==BaobabStage.Sapling,"Stage fixture failed");
        var grazing=Deploy(0,records[0]);var pickup=Deploy(1,records[1]);
        typeof(LambAgent).GetMethod("BecomePickup",Private).Invoke(pickup,null);
        Check(grazing.Target!=null && hand.ClaimCount==1 && pickup.State==LambAgent.LambState.Pickup,"Feeding/pickup fixture failed");
        hand.SelectCard(3);debug.Open();
        var world=flow.worldHost.ActiveWorld;var firstCell=surge.surface.Cells[0];var firstSlot=firstCell.slots[0];
        var body=flow.player.GetComponent<Rigidbody>();var ability=debug.abilities;
        Vector3 position=body.position,velocity=body.linearVelocity,cameraPosition=flow.player.cameraRig.transform.position;
        var budget=new[]{ability.GlideSecondsRemaining,ability.DashCooldownRemaining,ability.DashElapsedSeconds,(float)ability.AirJumpsRemaining};
        float simulation=surge.SimulationSeconds;
        int[] stats={surge.Attempts,surge.SpawnedCount,surge.SkippedSpawns,surge.CapacityBlockedSpawns};
        EventSystem.current.SetSelectedGameObject(debug.hud.skipButton.gameObject);
        debug.hud.skipButton.onClick.Invoke();
        Check(flow.DebugSkipPending && !debug.hud.skipButton.interactable,"UI did not queue once");
        Check(EventSystem.current.currentSelectedGameObject!=debug.hud.skipButton.gameObject,"Disabled button retained focus");
        yield return null;yield return null;
        Check(surge.WasSkippedByDebug && surge.IsRunning && surge.IsComplete && surge.RemainingSeconds==0 && surge.ElapsedSeconds==surge.durationSeconds,"Skip completion contract");
        Check(surge.ActiveSproutCount==0 && surge.Spawned.All(r=>r.released && !r.IsActive && !r.FeedingLamb && !r.slot.occupied),"Roots/slots/claims not cleared");
        Check(surge.SimulationSeconds==simulation && stats.SequenceEqual(new[]{surge.Attempts,surge.SpawnedCount,surge.SkippedSpawns,surge.CapacityBlockedSpawns}),"Simulation/statistics changed");
        Check(surge.threat.State==BaobabThreatMeter.Phase.Resolved && surge.threat.RawThreat==0 && surge.threat.GrowthPerSecond==0,"Threat not resolved");
        Check(hand.InHandCount==hand.CardCount && hand.SelectedIndex==3 && hand.Deployed.Count==0 && hand.ClaimCount==0,"Card identity/selection/cleanup failed");
        Check(flow.Current.state.surge.skippedByDebug && flow.Current.state.surge.result==SurgeResult.Resolved && flow.Current.state.phase==PlanetPhase.Clearing,"Snapshot marker missing");
        Check(debug.IsOpen && Time.timeScale==0 && debug.pause.HasReason(GamePauseReason.DebugOverlay) && !flow.CanOpenMap && !flow.IsMapOpen,"Modal pause changed");
        Check(world==flow.worldHost.ActiveWorld && ReferenceEquals(firstCell,surge.surface.Cells[0]) && ReferenceEquals(firstSlot,firstCell.slots[0]),"World/grid rebuilt");
        Check(body.position==position && body.linearVelocity==velocity && flow.player.cameraRig.transform.position==cameraPosition,"Movement/camera changed");
        Check(budget.SequenceEqual(new[]{ability.GlideSecondsRemaining,ability.DashCooldownRemaining,ability.DashElapsedSeconds,(float)ability.AirJumpsRemaining}),"Traversal budget changed");
        Check(!debug.RequestBaobabSkip() && debug.hud.skipButtonLabel.text==DeveloperDebugHUD.SkipDone,"Skip repeated/result text missing");
        var timer=FindFirstObjectByType<SurgeTimerHUD>();timer.Refresh();Check(timer.timeLabel.text=="00:00" && timer.titleLabel.text.Contains("SKIPPED"),"Timer result missing");
        passed.Add("Paused button clears all three stages, feeding and pickup lambs, slots and threat; preserves simulation, statistics, selection, grid, motion and traversal budget; records result and HUD");
        string departedId=flow.Current.spec.planetId;int notices=flow.UnlockNotificationCount;
        debug.Close();yield return null;yield return null;
        Check(Mathf.Approximately(Time.timeScale,.6f) && surge.ActiveSproutCount==0 && flow.CanOpenMap && flow.UnlockNotificationCount==notices,"Resume/map unlock/late destroy changed result");
        debug.Open();Check(flow.GetDebugSkipState(debug)==StarSystemFlowController.DebugSkipState.Skipped,"Reopen lost result");debug.Close();yield return null;
        yield return TravelNext();
        Check(!surge.WasSkippedByDebug && surge.durationSeconds==120 && !surge.IsComplete && flow.SystemData.Find(departedId).state.surge.skippedByDebug,"Next planet inherited skip or lost departed record");
        passed.Add("Close/reopen preserves skip; M map path travels; next planet starts normal 120-second phase and keeps prior marker");

        surge.Advance(surge.durationSeconds);yield return null;yield return null;
        Check(surge.IsComplete && surge.ActiveSproutCount>0 && !surge.IsGameOver,"Clearing fixture failed");
        notices=flow.UnlockNotificationCount;debug.Open();Check(debug.RequestBaobabSkip(),"Clearing command rejected");yield return null;yield return null;
        Check(surge.WasSkippedByDebug && flow.UnlockNotificationCount==notices,"Clearing skip repeated map notice");
        debug.Close();yield return null;yield return TravelNext();
        passed.Add("Remaining-only skip works without duplicate unlock notification");

        debug.Open();Check(debug.RequestBaobabSkip(),"Defeat request fixture failed");
        foreach(var record in surge.Spawned.Where(r=>r.IsActive))record.growth=new BaobabGrowth{initial=surge.threat.Capacity+1,maximum=surge.threat.Capacity+1};
        // Let the real LateUpdate(50) defeat check run before Flow.LateUpdate(300).
        yield return null;yield return null;
        Check(surge.IsGameOver && !surge.WasSkippedByDebug && !flow.DebugSkipPending && surge.ActiveSproutCount>0,"Skip beat same-frame defeat");
        Check(!debug.RequestBaobabSkip() && !debug.hud.skipButton.interactable,"GameOver command enabled");
        debug.Close();yield return null;
        Check(Time.timeScale==0 && flow.pause.HasReason(GamePauseReason.GameOver),"Debug close released defeat pause");
        passed.Add("Actual same-frame defeat wins over pending skip; post-defeat button rejects and closing keeps GameOver pause");
    }
    LambAgent Deploy(int index,BaobabSurge.SpawnRecord target)
    {
        var go=Instantiate(hand.lambPrefab,surge.surface.WorldPoint(target.slot),Quaternion.FromToRotation(Vector3.up,surge.surface.WorldNormal(target.slot)),surge.surface.transform);
        var agent=go.GetComponent<LambAgent>();
        ((LambAgent[])typeof(LambCardHand).GetField("cards",Private).GetValue(hand))[index]=agent;
        ((List<LambAgent>)typeof(LambCardHand).GetField("deployed",Private).GetValue(hand)).Add(agent);
        agent.Initialize(hand,index,target);return agent;
    }
    IEnumerator TravelNext()
    {
        Check(flow.OpenMap(),"Existing map failed to open");
        string id=flow.SystemData.links.First(l=>l.fromPlanetId==flow.Current.spec.planetId).toPlanetId;
        Check(flow.RequestTravel(id),"Map travel rejected");
        float deadline=Time.realtimeSinceStartup+8;
        while(flow.Current.spec.planetId!=id || flow.IsTravelling)
        { Check(Time.realtimeSinceStartup<deadline,"Travel timed out");yield return null; }
        yield return null;
    }
    void Finish(bool success,string error)
    {
        System.IO.File.WriteAllText("Temp/DeveloperBaobabSkipValidation.txt","passed="+success+"\nerror="+error+"\n"+string.Join("\n",passed));
        if(!success)Debug.LogError("Baobab skip validation: "+error);
        Destroy(gameObject);
    }
}
