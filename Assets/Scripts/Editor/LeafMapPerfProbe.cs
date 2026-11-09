using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Measures what the leaf map's frame time is spent on. In Play Mode, at the foot of the bubble cascade, it
/// records the main-thread loop with everything on, then switches each kind of scene content off in turn
/// (flower field, veils, bubbles, post effects, ...) and then each script type that runs every frame,
/// and writes how much each one costs to ArtCaptures/LeafMap/Perf.txt.
/// </summary>
[InitializeOnLoad]
public static class LeafMapPerfProbe
{
    private const string Key="LeafMap.PerfProbe";
    private const int Frames=60;
    private static readonly string[] Markers={"PlayerLoop","Update.ScriptRunBehaviourUpdate","FixedUpdate.ScriptRunBehaviourFixedUpdate",
        "PreLateUpdate.ScriptRunBehaviourLateUpdate","FixedUpdate.PhysicsFixedUpdate"};
    private static int stage=-1,frame;
    private static double frameSum;
    private static double[] markerSum;
    private static ProfilerRecorder[] recorders;
    private static List<(string name,System.Action off,System.Action on)> tests;
    private static readonly List<(string name,double frame,double scripts,double total)> results=new List<(string,double,double,double)>();

    static LeafMapPerfProbe(){EditorApplication.update+=Tick;}

    [MenuItem("Tools/Capstone/Leaf Map/Measure Performance In Play Mode")]
    /// <summary>Only the all-on baseline (about half a minute), to compare before and after a change.</summary>
    public static void BeginQuick(){SessionState.SetBool(Key+".Quick",true);Begin();}
    /// <summary>Only the scene content tests (a few minutes), no per-script tests.</summary>
    public static void BeginContent(){SessionState.SetBool(Key+".Content",true);Begin();}

    public static void Begin()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        SessionState.SetBool(Key,true);stage=-1;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if(!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying)return;
        var scene=SceneManager.GetSceneByPath(LeafMapSetup.ScenePath);
        if(!scene.IsValid())return;
        var world=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ProceduralWorld>()).FirstOrDefault();
        var carrier=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlayerCreatureCarrier>()).FirstOrDefault();
        if(stage==-1)
        {
            if(world==null || !world.GenerationComplete || carrier==null)return;
            Vector2 foot=LeafMapTerrain.ToWorld(new Vector2(-12,-74));
            carrier.PlayerBody.position=new Vector3(foot.x,LeafMapTerrain.Height(foot.x,foot.y)+2f,foot.y);
            carrier.transform.position=carrier.PlayerBody.position;Physics.SyncTransforms();
            Build(scene);results.Clear();stage=0;frame=-240;   // let the cascade fill the air first
            recorders=Markers.Select(m=>ProfilerRecorder.StartNew(ProfilerCategory.Internal,m)).ToArray();
            Reset();
            return;
        }
        frame++;
        if(frame<=0)return;
        frameSum+=Time.unscaledDeltaTime*1000.0;
        for(int i=0;i<recorders.Length;i++)if(recorders[i].Valid)markerSum[i]+=recorders[i].LastValue/1e6;
        if(frame<Frames)return;
        // Each test is measured on, then off, right after each other, so slow drift cancels out.
        var sample=(frameSum/Frames,markerSum[1]/Frames,markerSum[0]/Frames);
        if(stage==0)
        {
            baseline=sample;stage=1;measuringOff=false;frame=-20;Reset();
            if(SessionState.GetBool(Key+".Quick",false)){SessionState.SetBool(Key+".Quick",false);tests.Clear();Finish(scene);}
            return;
        }
        if(!measuringOff){onSample=sample;tests[stage-1].off();measuringOff=true;frame=-20;Reset();return;}
        tests[stage-1].on();
        results.Add((tests[stage-1].name,onSample.frame-sample.Item1,onSample.scripts-sample.Item2,onSample.total-sample.Item3));
        stage++;measuringOff=false;frame=-20;Reset();
        if(stage>tests.Count)Finish(scene);
    }
    private static (double frame,double scripts,double total) baseline,onSample;
    private static bool measuringOff;
    private static void Reset(){frameSum=0;markerSum=new double[Markers.Length];}

    private static void Finish(Scene scene)
    {
        var lines=new List<string>{
            "Batches "+UnityStats.batches+", SetPass "+UnityStats.setPassCalls+", triangles "+UnityStats.triangles+
            ", bubbles "+Object.FindObjectsByType<TrappingBubble>(FindObjectsSortMode.None).Length,
            $"All on: frame {baseline.frame:0.0} ms, player loop {baseline.total:0.0} ms, script Update {baseline.scripts:0.00} ms",
            "","Saving when switched off (frame / player loop / script Update), largest first:"};
        foreach(var r in results.OrderByDescending(r=>r.total))
            lines.Add($"  {r.name}: {r.frame:0.00} / {r.total:0.00} / {r.scripts:0.00} ms");
        File.WriteAllText("ArtCaptures/LeafMap/Perf.txt",string.Join("\n",lines));
        foreach(var r in recorders)r.Dispose();recorders=null;
        SessionState.SetBool(Key,false);stage=-1;EditorApplication.ExitPlaymode();
    }

    private static void Build(Scene scene)
    {
        var roots=scene.GetRootGameObjects();
        Transform Find(string name)=>roots.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t=>t.name==name);
        (string,System.Action,System.Action) Group(string label,string name)
        {
            var t=Find(name);
            return (label,()=>{if(t!=null)t.gameObject.SetActive(false);},()=>{if(t!=null)t.gameObject.SetActive(true);});
        }
        (string,System.Action,System.Action) Renderers(string label,System.Func<IEnumerable<Renderer>> pick)
        {
            List<Renderer> off=null;
            return (label,()=>{off=pick().Where(r=>r.enabled).ToList();foreach(var r in off)r.enabled=false;},()=>{if(off!=null)foreach(var r in off)if(r!=null)r.enabled=true;});
        }
        var camera=roots.SelectMany(g=>g.GetComponentsInChildren<PlayerCreatureCarrier>()).First().transform.root.GetComponentsInChildren<Camera>().FirstOrDefault(c=>c.isActiveAndEnabled);
        var cameraData=camera!=null?camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>():null;
        tests=new List<(string,System.Action,System.Action)>{
            Group("[content] flower field","01 One flowing flower field — purple to time blue"),
            Group("[content] time veils","04 Time veils — flowing from the mother over the field and into the sky"),
            Group("[content] all time-pass decoration","10 Time — flower field, bones and sky"),
            ("[content] post effects",()=>{if(cameraData!=null)cameraData.renderPostProcessing=false;},()=>{if(cameraData!=null)cameraData.renderPostProcessing=true;}),
            ("[content] instanced lily field",()=>LilyField.Hidden=true,()=>LilyField.Hidden=false),
            Renderers("[content] bubble rendering",()=>Object.FindObjectsByType<TrappingBubble>(FindObjectsSortMode.None).SelectMany(b=>b.GetComponentsInChildren<Renderer>())),
            Group("[content] field emitters","13 Field bubble zones — emitters by area"),
        };
        if(SessionState.GetBool(Key+".Content",false)){SessionState.SetBool(Key+".Content",false);return;}
        // Every script type that runs each frame, switched off as a whole.
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var behaviours=roots.SelectMany(g=>g.GetComponentsInChildren<MonoBehaviour>()).Where(b=>b!=null && b.enabled).ToList();
        foreach(var type in behaviours.Select(b=>b.GetType()).Distinct())
        {
            // The terrain and the player cannot be switched off without tearing the level down.
            if(typeof(ProceduralWorld).IsAssignableFrom(type) || type.Namespace!=null && type.Namespace.StartsWith("UnityEngine"))continue;
            if(type==typeof(PlayerCreatureCarrier) || type==typeof(SmoothFirstPersonController) || type.Name.Contains("Camera"))continue;
            if(type.GetMethod("Update",flags)==null && type.GetMethod("FixedUpdate",flags)==null && type.GetMethod("LateUpdate",flags)==null)continue;
            var group=behaviours.Where(b=>b.GetType()==type).ToList();
            tests.Add(("[script] "+type.Name+" x"+group.Count,()=>{foreach(var b in group)if(b!=null)b.enabled=false;},()=>{foreach(var b in group)if(b!=null)b.enabled=true;}));
        }
    }
}
