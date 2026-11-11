using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

/// <summary>Explicit local-file requests for repeatable showcase authoring and verification.</summary>
[InitializeOnLoad]
public static class LeafGardenVerification
{
    [Serializable] public class Request { public string id, action; public int value; }
    private const string Input="Temp/LeafGardenRequest.json", Output="Temp/LeafGardenResult.json";
    private static double nextTick;
    private static int errors;
    private static string lastError;
    private static bool ticking;
    static LeafGardenVerification()
    {
        ticking=SessionState.GetBool("LeafGardenTick",false);
        EditorApplication.update+=Poll;
        Application.logMessageReceived+=(message,trace,type)=>
        { if(type==LogType.Error || type==LogType.Exception) { errors++; lastError=message; } };
    }
    private static void Poll()
    {
        if(ticking && EditorApplication.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
        if(EditorApplication.timeSinceStartup<nextTick || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        nextTick=EditorApplication.timeSinceStartup+0.1;
        if(!File.Exists(Input)) return;
        Request request;
        try { request=JsonUtility.FromJson<Request>(File.ReadAllText(Input)); }
        catch { return; }
        if(request==null || string.IsNullOrEmpty(request.id) || SessionState.GetString("LeafGardenLastRequest","")==request.id) return;
        SessionState.SetString("LeafGardenLastRequest",request.id);
        try { Execute(request); }
        catch(Exception e) { File.WriteAllText(Output,JsonUtility.ToJson(new Result { id=request.id, ok=false, message=e.ToString() },true)); Debug.LogException(e); }
    }
    [Serializable] private class Result { public string id; public bool ok; public string message; }
    private static void Done(Request r,string message) => File.WriteAllText(Output,JsonUtility.ToJson(new Result { id=r.id,ok=true,message=message },true));
    private static LeafGardenShowcase Show() => UnityEngine.Object.FindFirstObjectByType<LeafGardenShowcase>();
    private static void Execute(Request r)
    {
        switch(r.action)
        {
            case "landscape": LeafGardenLandscapeSetup.Create(); Done(r,"Replaced Leaf Garden with the golden meadow landscape."); break;
            case "prefab":
                var prefab=LeafGardenShowcaseSetup.EnsurePrefab();
                Done(r,$"Saved {AssetDatabase.GetAssetPath(prefab)}; components={prefab.GetComponents<Component>().Length}; Rigidbody={prefab.GetComponent<Rigidbody>()!=null}; Collider={prefab.GetComponent<Collider>()!=null}"); break;
            case "setup":
                if(EditorSceneManager.GetActiveScene().isDirty && EditorSceneManager.GetActiveScene().path!=LeafGardenShowcaseSetup.ScenePath)
                    throw new InvalidOperationException("Save your current scene before switching to the garden.");
                LeafGardenShowcaseSetup.Create(); EditorSceneManager.OpenScene(LeafGardenShowcaseSetup.ScenePath);
                Done(r,"Created and opened Scene_LeafGarden."); break;
            case "play":
                ticking=true; SessionState.SetBool("LeafGardenTick",true); EditorApplication.isPlaying=true; Done(r,"Play requested."); break;
            case "stop":
                ticking=false; SessionState.SetBool("LeafGardenTick",false); EditorApplication.isPlaying=false; Done(r,"Stopped."); break;
            case "status":
                var show=Show();
                var timing=new FrameTiming[1]; FrameTimingManager.CaptureFrameTimings(); FrameTimingManager.GetLatestTimings(1,timing);
                Done(r,$"playing={EditorApplication.isPlaying}; frame={Time.frameCount}; time={Time.time:F3}; errors={errors}; lastError={lastError}; GPU={SystemInfo.graphicsDeviceName}; resolution={Screen.width}x{Screen.height}; gpuMs={timing[0].gpuFrameTime:F3}; cpuMs={timing[0].cpuFrameTime:F3}; memoryMB={Profiler.GetTotalAllocatedMemoryLong()/1048576}; " +
                    (show==null ? "no showcase" : $"phase={show.Garden.Phase}; blend={show.Garden.Blend}; height={show.Garden.Height}; leaves={show.Leaves.Capacity}; launches={show.Launch.LaunchCount}; render={show.Leaves.Status}; actors={show.Leaves.ActiveActors}; " +
                    string.Join("; ",show.DemoBodies.Take(3).Select(b=>$"{b.name}: pos={b.position} velocity={b.linearVelocity} simulation={b.GetComponent<Creature>().SimulationEnabled} {b.GetComponent<LeafGardenWalker>().Diagnostic}")))); break;
            case "trigger": Show().Trigger(); Done(r,"Triggered at center."); break;
            case "size": Show().SelectSize(r.value); Done(r,"Size selected."); break;
            case "density": Show().SetDensity(r.value); Done(r,"Density selected."); break;
            case "view": Show().SetView(r.value); Done(r,"View selected."); break;
            case "clean": Show().ShowControls=false; Done(r,"Controls hidden."); break;
            case "auto": Show().Garden.Automatic=r.value!=0; Done(r,"Automatic cycle set."); break;
            case "reset": Show().ResetShowcase(); Done(r,"Reset."); break;
            case "player": Show().MovePlayerToTornado(); Done(r,"Player moved into tornado."); break;
            case "sample": Done(r,Sample()); break;
            case "capture":
                var recorder=Show().GetComponent<LeafGardenCapture>(); if(recorder==null) recorder=Show().gameObject.AddComponent<LeafGardenCapture>();
                recorder.Capture(r.value); Done(r,"Capture started."); break;
            case "validate":
                if(Show().Landscape)
                {
                    var landscapeTest=Show().GetComponent<LeafGardenLandscapeChecks>(); if(landscapeTest==null) landscapeTest=Show().gameObject.AddComponent<LeafGardenLandscapeChecks>();
                    landscapeTest.Run(); Done(r,"Landscape checks started."); break;
                }
                var test=Show().GetComponent<LeafGardenRuntimeChecks>(); if(test==null) test=Show().gameObject.AddComponent<LeafGardenRuntimeChecks>();
                test.Run(); Done(r,"Runtime checks started."); break;
            case "benchmark": Show().GetComponent<LeafGardenBenchmark>().Run(); Done(r,"Benchmark started."); break;
            case "build":
                Directory.CreateDirectory("Temp/LeafGardenBuild");
                bool oldTiming=PlayerSettings.enableFrameTimingStats;
                BuildReport build;
                try {
                    PlayerSettings.enableFrameTimingStats=true;
                    build=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                        scenes=new[]{LeafGardenShowcaseSetup.ScenePath}, locationPathName="Temp/LeafGardenBuild/LeafGarden.exe",
                        target=BuildTarget.StandaloneWindows64, options=BuildOptions.Development });
                } finally { PlayerSettings.enableFrameTimingStats=oldTiming; }
                Done(r,$"Build {build.summary.result}; errors={build.summary.totalErrors}; seconds={build.summary.totalTime.TotalSeconds:F1}");
                if(build.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("Leaf Garden development build failed.");
                break;
            case "shaders":
                var shader=AssetDatabase.LoadAssetAtPath<Shader>(LeafGardenShowcaseSetup.Folder+"/GoldenLeaf.shader");
                Done(r,string.Join("\n",ShaderUtil.GetShaderMessages(shader).Select(m=>m.severity+": "+m.message))); break;
            default: throw new ArgumentException("Unknown Leaf Garden verification action: "+r.action);
        }
    }
    private struct State { public Vector4 home,position,velocity,rotation; }
    private static string Sample()
    {
        var show=Show(); var states=new State[show.Leaves.Capacity]; show.Leaves.StateBuffer.GetData(states);
        int high=0,source=0,outsideMoved=0,nan=0; float maxY=0;
        foreach(var s in states)
        {
            if(s.position.w<=0) continue;
            if(float.IsNaN(s.position.x) || float.IsNaN(s.position.y) || float.IsInfinity(s.position.y)) nan++;
            if(s.position.y-s.home.y>2) high++;
            maxY=Mathf.Max(maxY,s.position.y);
            bool local=Vector2.Distance(new Vector2(s.home.x,s.home.z),new Vector2(show.Garden.TornadoCenter.x,show.Garden.TornadoCenter.z))<show.Garden.SourceRadius;
            if(local) source++; else if(s.position.y-s.home.y>2) outsideMoved++;
        }
        var args=new uint[5]; show.Leaves.DrawArguments.GetData(args);
        return $"leaves={states.Length}; rendered={args[1]}; source={source}; airborneAbove2m={high}; outsideSourceAbove2m={outsideMoved}; maxY={maxY:F2}; invalid={nan}; frame={Time.frameCount}";
    }
}
