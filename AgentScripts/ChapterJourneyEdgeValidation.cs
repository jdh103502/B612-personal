using System;
using System.Linq;
using System.Reflection;
using B612;
using UnityEngine;

public static class ChapterJourneyEdgeValidation
{
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static object Gates()
    {
        var actor=new GameObject("Isolated progression gate probe");actor.transform.position=Vector3.one*10000;
        try
        {
            actor.AddComponent<CapsuleCollider>();var motor=actor.AddComponent<GravityWalker>();motor.enabled=false;var ability=actor.AddComponent<PlayerTraversalAbilities>();
            var tick=typeof(GravityWalker).GetMethod("TickTraversal",BindingFlags.Instance|BindingFlags.NonPublic);var queue=typeof(PlayerTraversalAbilities).GetMethod("QueueSimulationInput",BindingFlags.Instance|BindingFlags.NonPublic);
            ability.SetUnlocked(TraversalCapability.None);queue.Invoke(ability,new object[]{true,true});tick.Invoke(motor,new object[]{.02f});
            Check(ability.AirJumpsRemaining==1&&!ability.IsDashing&&!ability.PendingDash,"Locked ability consumed resource or started");ability.ClearPendingRequests();
            ability.SetUnlocked(TraversalCapability.All);queue.Invoke(ability,new object[]{true,false});tick.Invoke(motor,new object[]{.02f});Check(ability.AirJumpsRemaining==0,"Unlocked air jump failed");
            queue.Invoke(ability,new object[]{false,true});tick.Invoke(motor,new object[]{.02f});Check(ability.IsDashing&&!ability.DashChargeAvailable,"Unlocked dash failed");
            ability.SetUnlocked(TraversalCapability.None);Check(!ability.IsDashing&&ability.AirJumpsRemaining==0&&!ability.DashChargeAvailable,"Relock refilled resources or retained dash");
            System.IO.File.WriteAllText("Temp/ChapterJourney/Gates.txt","passed=True\nLocked real motor cannot air-jump/dash; unlocked actions consume resources; lock cancels dash without refilling.");return new{passed=true};
        }
        finally{UnityEngine.Object.DestroyImmediate(actor);}
    }
    public static object DefeatPriority()
    {
        var flow=UnityEngine.Object.FindFirstObjectByType<StarSystemFlowController>();Check(flow.Progress.chapter==1&&flow.Progress.unlocked==TraversalCapability.None,"Use fresh campaign");
        var motor=flow.player.GetComponent<GravityWalker>();var point=flow.Sites.Positions[0];var normal=(point-flow.worldHost.ActiveWorld.transform.position).normalized;motor.Teleport(point+normal*.04f,normal);
        for(int i=0;i<20;i++){UnityEditor.EditorApplication.isPaused=false;UnityEditor.EditorApplication.Step();}
        Check(flow.Sites.CanInteract(0)&&flow.RequestSiteInteraction(flow.Sites,0),"Interaction did not queue");
        var record=flow.surge.Spawned.First(r=>r.IsActive);var growth=record.growth;record.growth=new BaobabGrowth{initial=1000,maximum=1000};
        try
        {
            // Same frame's final threat decision precedes flow, exactly as execution orders 50/300 do.
            flow.surge.threat.Evaluate();flow.SendMessage("LateUpdate");
            Check(flow.surge.IsGameOver&&flow.Progress.unlocked==TraversalCapability.None&&flow.Current.state.visitedSites==0,"Pending interaction overrode defeat");
            Check(!flow.RequestSiteInteraction(flow.Sites,0)&&!flow.RequestNextChapter(),"Defeat accepts gameplay request");
            System.IO.File.WriteAllText("Temp/ChapterJourney/DefeatPriority.txt","passed=True\nSame-frame game over cancels pending unlock/site interaction and chapter requests.");return new{passed=true};
        }
        finally{record.growth=growth;}
    }
}
