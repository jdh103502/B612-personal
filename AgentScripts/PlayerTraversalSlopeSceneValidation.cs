using System;
using System.Collections.Generic;
using System.IO;
using B612;
using UnityEngine;

public static class PlayerTraversalSlopeSceneValidation
{
    public static object Start()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run in Play mode.");
        Camera.main.gameObject.AddComponent<PlayerTraversalSlopeSceneProbe>();
        return new { started=true, output="Temp/PlayerTraversalSlopeSceneValidation.txt" };
    }
}

// Real game loop, real player model, actual deformed planet and Animator/HUD consumers.
[DefaultExecutionOrder(1500)]
public sealed class PlayerTraversalSlopeSceneProbe : MonoBehaviour
{
    PlayerController player;
    GravityWalker walker;
    PrinceCharacterAnimator prince;
    BaobabSurge surge;
    BaobabThreatMeter meter;
    bool playerEnabled, surgeEnabled, meterEnabled, finished;
    float elapsed, distance, maxAngle, minSpeed=100f;
    int ticks, airTicks, airFrames, hudAirFrames, recoveries;
    Vector3 previous;
    readonly List<string> rows=new List<string>{"time,grounded,support,normalSpeed,radialSpeed,tangentSpeed,normalGravityAngle,animGrounded,animAir"};
    void Start()
    {
        player=UnityEngine.Object.FindFirstObjectByType<PlayerController>();walker=player.GetComponent<GravityWalker>();
        prince=player.GetComponentInChildren<PrinceCharacterAnimator>();
        surge=UnityEngine.Object.FindFirstObjectByType<BaobabSurge>();meter=UnityEngine.Object.FindFirstObjectByType<BaobabThreatMeter>();
        playerEnabled=player.enabled;surgeEnabled=surge.enabled;meterEnabled=meter.enabled;
        meter.ResetForRun();surge.enabled=meter.enabled=false;surge.Begin();Time.timeScale=1f;
        player.Respawn();player.enabled=false;previous=walker.transform.position;
        walker.Traversal.LandingRecovered+=Recovered;
    }
    void Recovered(){if(elapsed>.4f)recoveries++;}
    void FixedUpdate()
    {
        if(finished||!walker)return;
        if(elapsed>.4f)
        {
            ticks++;if(!walker.Grounded)airTicks++;
            var body=walker.GetComponent<Rigidbody>();distance+=Vector3.Distance(previous,body.position);previous=body.position;
            if(elapsed>1f)minSpeed=Mathf.Min(minSpeed,walker.TangentSpeed);
            var radial=(body.position-walker.Source.transform.position).normalized;
            maxAngle=Mathf.Max(maxAngle,Vector3.Angle(radial,walker.MovementUp));
        }
        if(elapsed>.35f)
        {
            var radial=(walker.transform.position-walker.Source.transform.position).normalized;
            walker.moveInput=Vector3.Cross(radial,Vector3.forward).normalized;
        }
    }
    void Update()
    {
        if(finished)return;
        elapsed+=Time.deltaTime;
        if(elapsed>.5f)
        {
            var animator=prince.animator;
            bool air=animator.GetCurrentAnimatorStateInfo(0).IsName("Air")
                ||animator.IsInTransition(0)&&animator.GetNextAnimatorStateInfo(0).IsName("Air");
            if(air)airFrames++;if(!walker.Grounded)hudAirFrames++;
            rows.Add($"{elapsed:R},{walker.Grounded},{walker.HasSupport},{walker.SupportNormalVelocity:R},{walker.RadialSpeed:R},{walker.TangentSpeed:R},{maxAngle:R},{animator.GetBool("Grounded")},{air}");
        }
        if(elapsed>=8f)Finish();
    }
    void Finish()
    {
        finished=true;walker.ClearInput();walker.Traversal.LandingRecovered-=Recovered;
        // Sharp mesh edges can briefly reduce actual tangential speed during PhysX contact resolution.
        bool passed=airTicks==0&&airFrames==0&&hudAirFrames==0&&recoveries==0
            &&distance/(elapsed-.4f)>1.8f&&minSpeed>1f;
        Directory.CreateDirectory("Temp");
        File.WriteAllLines("Temp/PlayerTraversalSlopeSceneValidation.csv",rows);
        File.WriteAllText("Temp/PlayerTraversalSlopeSceneValidation.txt",
            $"passed={passed},elapsed={elapsed:R},fixedTicks={ticks},airTicks={airTicks},AnimatorAirFrames={airFrames},hudAirFrames={hudAirFrames},extraRecoveries={recoveries},distance={distance:R},minSpeed={minSpeed:R},maxNormalGravityAngle={maxAngle:R}");
        player.enabled=playerEnabled;surge.enabled=surgeEnabled;meter.enabled=meterEnabled;
        Destroy(this);
    }
}
