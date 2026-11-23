#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>Opt-in live-population baseline, never installed in a saved gameplay scene.</summary>
public sealed class CreatureSimulationProfileProbe : MonoBehaviour
{
    public static string Result;
    public float settleSeconds=3, sampleSeconds=10;
    IEnumerator Start()
    {
        Result="Settling";
        yield return new WaitForSecondsRealtime(settleSeconds);
        var scheduler=CreatureSimulationScheduler.For(gameObject.scene);
        var distance=FindFirstObjectByType<CreatureSleep>();
        var populationBefore=Count();
        using var main=ProfilerRecorder.StartNew(ProfilerCategory.Internal,"Main Thread",1);
        using var physics=ProfilerRecorder.StartNew(ProfilerCategory.Physics,"Physics.Simulate",1);
        using var gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);
        var mainTimes=new List<double>(4096);var physicsTimes=new List<double>(4096);
        var gcBytes=new List<double>(4096);var schedulerTimes=new List<double>(4096);
        int peakChecks=0,frames=0;
        float until=Time.realtimeSinceStartup+sampleSeconds;
        Result="Sampling";
        while(Time.realtimeSinceStartup<until)
        {
            yield return null;
            frames++;
            schedulerTimes.Add(scheduler.LastWorkMicroseconds/1000);
            peakChecks=Mathf.Max(peakChecks,distance?distance.ChecksLastFrame:0);
            if(main.Valid && main.Count>0)mainTimes.Add(main.LastValue/1000000.0);
            if(physics.Valid && physics.Count>0)physicsTimes.Add(physics.LastValue/1000000.0);
            if(gc.Valid && gc.Count>0)gcBytes.Add(gc.LastValue);
        }
        Result=$"Live scene baseline: {frames} frames over {sampleSeconds:F1}s real time; creatures before [{populationBefore}], after [{Count()}]. " +
            $"Relevance {Stats(schedulerTimes,"ms")}; peak distance checks {peakChecks}. " +
            $"Main Thread {Stats(mainTimes,"ms")}; Physics.Simulate {Stats(physicsTimes,"ms")}; GC Allocated In Frame {Stats(gcBytes,"bytes")}. " +
            "Editor measurement includes live combat/ecology and Editor overhead; not a matched before/after performance comparison.";
    }
    string Count()
    {
        int total=0,active=0,fish=0;
        foreach(var creature in CreatureRegistry.All)
            if(creature && creature.gameObject.scene==gameObject.scene)
            {total++;if(creature.SimulationEnabled)active++;if(creature is SeaAngelCreature)fish++;}
        return $"{total} registered / {active} active / {fish} sea angels";
    }
    static string Stats(List<double> values,string unit)
    {
        if(values.Count==0)return "unavailable";
        values.Sort();double total=0;foreach(double value in values)total+=value;
        return $"mean {total/values.Count:F3}, p50 {values[values.Count/2]:F3}, p95 {values[Math.Min(values.Count-1,(int)(values.Count*.95))]:F3}, max {values[^1]:F3} {unit} ({values.Count} samples)";
    }
}
#endif
