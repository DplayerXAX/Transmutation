using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

/// <summary>Coalesces mesh uploads and spreads colony work across frames. Simulation remains independent.</summary>
[DefaultExecutionOrder(1500)]
public sealed class SurfaceWorkQueue : MonoBehaviour
{
    struct Work { internal MonoBehaviour owner; internal Action run; internal int id; }
    static SurfaceWorkQueue instance;
    readonly Queue<Work> pending = new Queue<Work>();
    readonly HashSet<int> queued = new HashSet<int>();
    readonly Stopwatch timer = new Stopwatch();
    public static double LastWorkMilliseconds { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { instance=null;LastWorkMilliseconds=0; }
    public static void Enqueue(MonoBehaviour owner,Action run)
    {
        if(owner==null || run==null)return;
        if(!Application.isPlaying){run();return;}
        if(instance==null)
        {
            var go=new GameObject("Surface geometry work queue"){hideFlags=HideFlags.HideInHierarchy};
            DontDestroyOnLoad(go);instance=go.AddComponent<SurfaceWorkQueue>();
        }
        int id=owner.GetInstanceID();
        if(instance.queued.Add(id))instance.pending.Enqueue(new Work{owner=owner,run=run,id=id});
    }
    void LateUpdate()
    {
        timer.Restart();int completed=0;
        while(pending.Count>0 && timer.Elapsed.TotalMilliseconds<2.0 && completed<1)
        {
            Work work=pending.Dequeue();queued.Remove(work.id);
            if(work.owner==null || !work.owner.isActiveAndEnabled)continue;
            work.run();completed++;
        }
        timer.Stop();LastWorkMilliseconds=timer.Elapsed.TotalMilliseconds;
    }
}
