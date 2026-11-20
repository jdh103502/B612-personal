using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using B612;
using UnityEngine;

public static class ProceduralTerrainStage2SceneValidation
{
    public static object Start()
    {if(!Application.isPlaying)throw new InvalidOperationException("Use Play mode.");new GameObject("Stage 2 live validation").AddComponent<TerrainStage2LiveProbe>();return "Started Temp/TerrainStage2/Live.txt";}
}
public sealed class TerrainStage2LiveProbe:MonoBehaviour
{
    StarSystemFlowController flow;DeveloperDebugController debug;Mesh oldMesh;PlanetData destination;float originalRadius;
    readonly List<string> passed=new List<string>();
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    IEnumerator Start()
    {
        flow=FindFirstObjectByType<StarSystemFlowController>();debug=FindFirstObjectByType<DeveloperDebugController>();
        var run=Run();
        while(true)
        {
            object next=null;bool more=false;string error=null;
            try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception ex){error=ex.ToString();}
            if(error!=null){Finish(false,error);yield break;}if(!more){Finish(true,"");yield break;}yield return next;
        }
    }
    IEnumerator Run()
    {
        Check(flow.WorldReady&&!flow.surge.IsGameOver,"Fresh journey required");debug.Open();
        var world=flow.worldHost.ActiveWorld;oldMesh=world.Terrain.OwnedMesh;var plan=world.Terrain.GenerationPlan;
        var cell=world.surface.Cells.First(c=>c.slots.Count>0);var slot=cell.slots[0];bool occupied=slot.occupied;
        bool dash=debug.abilities.AllowDash;debug.hud.dashToggle.isOn=!dash;debug.hud.dashToggle.isOn=dash;
        UnityEditor.Selection.activeGameObject=world.gameObject;UnityEditor.SceneView.RepaintAll();yield return null;yield return null;
        Check(world.Terrain.OwnedMesh==oldMesh&&ReferenceEquals(cell,world.surface.Cells[cell.id])&&ReferenceEquals(slot,cell.slots[0])&&slot.occupied==occupied,"Debug/Gizmos rebuilt terrain or slots");
        var inspector=UnityEditor.Editor.CreateEditor(world.Terrain);try{Check(inspector.CreateInspectorGUI()!=null,"Read-only terrain inspector missing");}finally{DestroyImmediate(inspector);}
        Check(Time.timeScale==0&&debug.IsOpen,"Debug pause changed during inspection");
        Check(debug.RequestBaobabSkip(),"Skip request rejected");yield return null;yield return null;
        Check(flow.surge.IsComplete&&flow.surge.ActiveSproutCount==0&&world.Terrain.OwnedMesh==oldMesh,"Skip regenerated ground");
        string priorPlanet=flow.Current.spec.planetId;
        passed.Add("Read-only inspector, F12 toggles, selected Gizmos and baobab skip retain the same terrain/cells/slots");
        debug.Close();yield return null;yield return null;Check(flow.OpenMap(),"Map did not unlock");
        string nextId=flow.SystemData.links.First(l=>l.fromPlanetId==priorPlanet).toPlanetId;destination=flow.SystemData.Find(nextId);originalRadius=destination.spec.terrain.radius;
        destination.spec.terrain.radius+=1;Check(flow.RequestTravel(nextId),"Failed travel test did not queue");
        float until=Time.realtimeSinceStartup+12;bool sawTravel=false;
        do{yield return null;sawTravel|=flow.IsTravelling;Check(Time.realtimeSinceStartup<until,"Failure rollback timeout");}while(!sawTravel||flow.IsTravelling);
        Check(flow.IsMapOpen&&flow.Current.spec.planetId==priorPlanet&&flow.worldHost.ActiveWorld==world&&oldMesh,"Preparation failure destroyed active world");
        destination.spec.terrain.radius=originalRadius;
        passed.Add("Invalid target preparation rolls back without changing current planet, mesh, cards or chosen path");
        Check(flow.RequestTravel(nextId),"Valid travel did not queue");until=Time.realtimeSinceStartup+25;
        while(flow.Current.spec.planetId!=nextId||flow.IsTravelling){yield return null;Check(Time.realtimeSinceStartup<until,"Generated travel timeout");}
        yield return null;yield return null;
        var current=flow.worldHost.ActiveWorld;Check(current.Terrain&&current.Data==destination&&current.Terrain.GenerationPlan.seed==destination.spec.seed,"Travel lost frozen generation data");
        Check(!oldMesh&&current.Terrain.OwnedMesh,"Old runtime mesh retained or new mesh missing");
        Check(flow.hand.InHandCount==flow.hand.CardCount&&flow.hand.ClaimCount==0,"Travel did not recall original cards");
        Check(!flow.surge.WasSkippedByDebug&&flow.surge.durationSeconds==120&&flow.surge.maxActiveSprouts==6,"Travel inherited debug skip or changed spawn balance");
        Check(flow.SystemData.Find(priorPlanet).state.surge.skippedByDebug,"Departed state lost debug marker");
        foreach(var record in flow.surge.Spawned)if(record.IsActive)
            Check(current.Terrain.AllowsSpawn(current.surface.WorldPoint(record.slot),current.surface.WorldNormal(record.slot)),"Actual sprout entered reserved space");
        float landUntil=Time.time+.4f;while(Time.time<landUntil)yield return null;
        Check(flow.player.GetComponent<GravityWalker>().Grounded,"Player not grounded after generated-world arrival");
        Check(FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length==1,"Duplicate EventSystem");
        passed.Add("Actual paused-map travel prepares verified terrain, disposes the prior mesh, preserves data/cards, starts a fresh 120-second phase and lands on final ground");
        debug.Open();
    }
    void Finish(bool success,string error)
    {
        if(destination!=null)destination.spec.terrain.radius=originalRadius;
        System.IO.Directory.CreateDirectory("Temp/TerrainStage2");System.IO.File.WriteAllText("Temp/TerrainStage2/Live.txt","passed="+success+"\nerror="+error+"\n"+string.Join("\n",passed));
        if(!success)Debug.LogError(error);Destroy(gameObject);
    }
}
