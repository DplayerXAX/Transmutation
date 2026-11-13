using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>Opt-in fixed-camera benchmark, also usable in a development player with -leafGardenBenchmark.</summary>
public sealed class LeafGardenBenchmark : MonoBehaviour
{
    private readonly List<double> frames=new List<double>(4096), gpu=new List<double>(4096), cpu=new List<double>(4096);
    private readonly FrameTiming[] timing=new FrameTiming[1];
    private bool running;
    private IEnumerator Start()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-leafGardenBenchmark")<0) yield break;
        yield return null; yield return RunBenchmark(true);
    }
    public void Run() { if(!running) StartCoroutine(RunBenchmark(false)); }
    private IEnumerator RunBenchmark(bool quit)
    {
        running=true; var show=GetComponent<LeafGardenShowcase>();
        int vsync=QualitySettings.vSyncCount, target=Application.targetFrameRate;
        QualitySettings.vSyncCount=0; Application.targetFrameRate=-1;
        show.Garden.Automatic=false; show.ShowControls=false; show.SetView(2);
        string folder="ArtCaptures/LeafGarden"; Directory.CreateDirectory(folder);
        var report=new List<string> { "case,live_leaves,rendered_leaves,samples,median_frame_ms,p95_frame_ms,median_cpu_ms,median_gpu_ms" };
        var metadata=$"{SystemInfo.graphicsDeviceName}; CPU {SystemInfo.processorType}; {Screen.width}x{Screen.height}; Unity {Application.unityVersion}; development={Debug.isDebugBuild}; platform={Application.platform}; fixed overview; vsync off; warmup 2s, sample 4s; frame timing GPU=0 means unavailable.";
        File.WriteAllText(folder+"/benchmark-hardware.txt",metadata);
        for(int stage=0;stage<10;stage++)
        {
            show.ResetShowcase(); show.Garden.Automatic=false;
            int count=stage==0?200000:stage<=2?25000:stage<=4?50000:stage<=6?100000:stage<=8?200000:400000;
            bool tornado=stage>0 && (stage%2==0 || stage==9);
            if(stage==0) show.Leaves.enabled=false;
            else
            {
                show.Leaves.enabled=true; show.SetDensity(count);
                if(tornado) { show.Garden.GatherSeconds=0.2f; show.Garden.ActiveSeconds=30; show.Trigger(); }
            }
            yield return new WaitForSecondsRealtime(2);
            frames.Clear(); gpu.Clear(); cpu.Clear();
            double until=Time.realtimeSinceStartupAsDouble+4;
            while(Time.realtimeSinceStartupAsDouble<until)
            {
                FrameTimingManager.CaptureFrameTimings();
                frames.Add(Time.unscaledDeltaTime*1000.0);
                if(FrameTimingManager.GetLatestTimings(1,timing)>0)
                {
                    if(timing[0].gpuFrameTime>0) gpu.Add(timing[0].gpuFrameTime);
                    if(timing[0].cpuFrameTime>0) cpu.Add(timing[0].cpuFrameTime);
                }
                yield return null;
            }
            uint rendered=0;
            if(stage>0) { var args=new uint[5]; show.Leaves.DrawArguments.GetData(args); rendered=args[1]; }
            string name=stage==0?"baseline":tornado?"huge_tornado":"carpet";
            report.Add(string.Format(CultureInfo.InvariantCulture,"{0},{1},{2},{3},{4:F3},{5:F3},{6:F3},{7:F3}",
                name,stage==0?0:count,rendered,frames.Count,Percentile(frames,0.5),Percentile(frames,0.95),Percentile(cpu,0.5),Percentile(gpu,0.5)));
            File.WriteAllLines(folder+"/benchmark.csv",report);
        }
        QualitySettings.vSyncCount=vsync; Application.targetFrameRate=target;
        show.Garden.GatherSeconds=5; show.Garden.ActiveSeconds=12;
        show.Leaves.enabled=true; show.SetDensity(200000); show.ResetShowcase();
        running=false; if(quit) Application.Quit(0);
    }
    private static double Percentile(List<double> values,double q)
    { if(values.Count==0) return 0; values.Sort(); return values[Mathf.Clamp((int)(values.Count*q),0,values.Count-1)]; }
}
