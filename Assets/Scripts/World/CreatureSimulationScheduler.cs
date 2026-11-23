using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public interface ICreatureRelevancePolicy
{
    float RelevanceInterval { get; }
    void RefreshRelevance();
}

/// <summary>Scene-local clock for activation policy. Never owns physical movement or animation.</summary>
[DefaultExecutionOrder(-500)]
public sealed class CreatureSimulationScheduler : MonoBehaviour
{
    sealed class Work { public MonoBehaviour owner; public ICreatureRelevancePolicy policy; public float next; }
    static readonly Dictionary<int,CreatureSimulationScheduler> scenes=new();
    readonly List<Work> work=new();
    static readonly Unity.Profiling.ProfilerMarker marker=new("CreatureSimulation.Relevance");
    int sceneKey;
    public int PoliciesLastFrame {get;private set;}
    public double LastWorkMicroseconds {get;private set;}
    public int PolicyCount=>work.Count;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()=>scenes.Clear();
    public static CreatureSimulationScheduler For(Scene scene)
    {
        if(!scene.IsValid()||!scene.isLoaded)return null;
        if(scenes.TryGetValue(scene.handle,out var current)&&current)return current;
        var go=new GameObject("Creature Simulation Scheduler");SceneManager.MoveGameObjectToScene(go,scene);
        var scheduler=go.AddComponent<CreatureSimulationScheduler>();scheduler.sceneKey=scene.handle;
        scenes[scene.handle]=scheduler;return scheduler;
    }
    public void Register(MonoBehaviour owner,ICreatureRelevancePolicy policy)
    {
        if(work.Exists(w=>w.owner==owner))return;
        work.Add(new Work{owner=owner,policy=policy,next=Time.time});
    }
    public void Unregister(MonoBehaviour owner)=>work.RemoveAll(w=>w.owner==owner);
    void Update()
    {
        using var scope=marker.Auto();PoliciesLastFrame=0;
        long started=System.Diagnostics.Stopwatch.GetTimestamp();
        for(int i=work.Count-1;i>=0;i--)
        {
            if(i>=work.Count)continue;
            var item=work[i];
            if(!item.owner){work.RemoveAt(i);continue;}
            if(!item.owner.isActiveAndEnabled||Time.time<item.next)continue;
            item.next=Time.time+Mathf.Max(0,item.policy.RelevanceInterval);
            item.policy.RefreshRelevance();PoliciesLastFrame++;
        }
        LastWorkMicroseconds=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000000.0/System.Diagnostics.Stopwatch.Frequency;
    }
    void OnDestroy(){if(scenes.TryGetValue(sceneKey,out var current)&&current==this)scenes.Remove(sceneKey);}
}
