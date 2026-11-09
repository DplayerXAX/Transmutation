using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Plays the leaf map from the normal start and records (request `mothertest`): how long entering Play Mode
/// takes, long frames in the first seconds, when the terrain is ready, and how smoothly the sleeping mother's
/// ribs move (largest turn in one frame, share of frames they move). Results: ArtCaptures/LeafMap/MotherTest.txt.
/// </summary>
[InitializeOnLoad]
public static class LeafMapMotherTest
{
    private const string Key = "LeafMap.MotherTest", StartKey = "LeafMap.MotherTest.Start";
    private static float firstFrame = -1f, worldReady = -1f, ribsFrom = -1f;
    // True: also record the profiler and list what long frames spent their time on (slower start).
    public static bool Profile = false;
    private static readonly StringBuilder log = new StringBuilder();
    private static readonly List<string> spikes = new List<string>();
    private static Transform[] roots;
    private static Quaternion[] last;
    private static BoneRib[] ribList;
    private static int[] movedFrames;
    private static float maxStep, sumStep;
    private static int frames, moving;

    static LeafMapMotherTest()
    {
        EditorApplication.update += Tick;
        // Timestamps of the steps of entering Play Mode, to see which one takes the time.
        EditorApplication.playModeStateChanged += state => Mark(state.ToString());
        AssemblyReloadEvents.beforeAssemblyReload += () => Mark("BeforeAssemblyReload");
        AssemblyReloadEvents.afterAssemblyReload += () => Mark("AfterAssemblyReload");
    }
    private static void Mark(string step)
    {
        if (!SessionState.GetBool(Key, false)) return;
        float start = SessionState.GetFloat(StartKey, 0f);
        SessionState.SetString(Key + ".Steps", SessionState.GetString(Key + ".Steps", "") + "  " + step + " at " + ((float)EditorApplication.timeSinceStartup - start).ToString("0.0") + " s" + System.Environment.NewLine);
    }

    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        SessionState.SetBool(Key, true);
        SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
        SessionState.SetString(Key + ".Steps", "");
        ProfilerDriver.ClearAllFrames();
        ProfilerDriver.profileEditor = false;
        ProfilerDriver.enabled = Profile;
        EditorApplication.EnterPlaymode();
    }

    // The profiler's view of every frame over half a second: the samples that took the time, three levels deep.
    private static void LongFrames()
    {
        for (int frame = ProfilerDriver.firstFrameIndex; frame >= 0 && frame <= ProfilerDriver.lastFrameIndex; frame++)
        {
            using (var view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
            {
                if (view == null || !view.valid || view.frameTimeMs < 500f) continue;
                log.AppendLine("Profiled long frame " + frame + ": " + view.frameTimeMs.ToString("0") + " ms");
                Walk(view, view.GetRootItemID(), 0);
            }
        }
    }
    private static void Walk(HierarchyFrameDataView view, int item, int depth)
    {
        var children = new List<int>();
        view.GetItemChildren(item, children);
        foreach (int child in children.OrderByDescending(c => view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime)))
        {
            float ms = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnTotalTime);
            if (ms < 150f) break;
            log.AppendLine(new string(' ', 2 + depth * 2) + view.GetItemName(child) + "  " + ms.ToString("0") + " ms");
            if (depth < 6) Walk(view, child, depth + 1);
        }
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        float now = (float)EditorApplication.timeSinceStartup;
        if (firstFrame < 0f)
        {
            firstFrame = now; log.Clear(); spikes.Clear(); worldReady = ribsFrom = -1f; roots = null; frames = moving = 0; maxStep = sumStep = 0f;
            log.AppendLine("Enter Play Mode until the first frame: " + (now - SessionState.GetFloat(StartKey, now)).ToString("0.00") + " s");
            log.Append(SessionState.GetString(Key + ".Steps", ""));
            return;
        }
        float since = now - firstFrame;
        if (Time.unscaledDeltaTime > 0.08f && since < 30f) spikes.Add(since.ToString("0.0") + " s: " + (Time.unscaledDeltaTime * 1000f).ToString("0") + " ms");
        var scene = SceneManager.GetSceneByPath(LeafMapSetup.ScenePath);
        var world = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ProceduralWorld>()).FirstOrDefault();
        if (worldReady < 0f && world != null && world.GenerationComplete) worldReady = since;
        if (roots == null)
        {
            var ribs = Object.FindObjectsByType<BoneRib>(FindObjectsSortMode.None);
            if (ribs.Length == 0 || since < 8f) return;
            ribList = ribs.Where(r => r.segments != null && r.segments.Length > 0).ToArray();
            roots = ribList.Select(r => r.segments[0]).ToArray();
            movedFrames = new int[roots.Length];
            last = roots.Select(t => t.localRotation).ToArray();
            ribsFrom = since;
            return;
        }
        frames++;
        float frameMax = 0f;
        for (int i = 0; i < roots.Length; i++)
        {
            float step = Quaternion.Angle(last[i], roots[i].localRotation);
            last[i] = roots[i].localRotation;
            frameMax = Mathf.Max(frameMax, step); sumStep += step;
            if (step > 0.001f) { moving++; movedFrames[i]++; }
        }
        maxStep = Mathf.Max(maxStep, frameMax);
        if (since - ribsFrom < 12f) return;
        var camera = Camera.main;
        var kiln = Object.FindFirstObjectByType<BoneKiln>();
        log.AppendLine("Terrain ready after: " + (worldReady < 0f ? "not yet" : worldReady.ToString("0.0") + " s"));
        log.AppendLine("Frames over 80 ms in the first 30 s: " + spikes.Count);
        foreach (string s in spikes.Take(25)) log.AppendLine("  " + s);
        if (camera != null && kiln != null) log.AppendLine("Camera to mother: " + Vector3.Distance(camera.transform.position, kiln.transform.position).ToString("0") + " m");
        log.AppendLine("Ribs: " + roots.Length + ", watched " + frames + " frames");
        log.AppendLine("Rib root turn per frame: largest " + maxStep.ToString("0.00") + " deg, mean " + (sumStep / Mathf.Max(1, frames * roots.Length)).ToString("0.000") + " deg");
        log.AppendLine("Share of rib-frames that moved: " + (moving / (float)Mathf.Max(1, frames * roots.Length)).ToString("P0"));
        if (kiln != null)
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            Vector3 heart = (Vector3)typeof(BoneKiln).GetField("heartPoint", flags).GetValue(kiln);
            var attached = (System.Collections.IList)typeof(BoneKiln).GetField("attachedFire", flags).GetValue(kiln);
            var near = Physics.OverlapSphere(heart, 2.6f, ~0, QueryTriggerInteraction.Collide).Select(c => c.GetComponentInParent<Creature>()).Where(c => c != null).Distinct()
                .Select(c => c.name + " heat " + c.Heat.ToString("0.0"));
            log.AppendLine("Mother warmth " + kiln.Warmth.ToString("0.00") + ", awake " + kiln.Awake + ", fire bubbles bound to her " + attached.Count + "; creatures at her heart: " + string.Join(", ", near));
        }
        // Ribs that stood still most of the time, and what their bones touch.
        for (int i = 0; i < ribList.Length; i++)
        {
            float share = movedFrames[i] / (float)Mathf.Max(1, frames);
            if (share > 0.6f) continue;
            var rib = ribList[i];
            var touching = new HashSet<string>();
            for (int k = 0; k < rib.segments.Length; k++)
            {
                Vector3 a = rib.segments[k].position;
                Vector3 b = k + 1 < rib.segments.Length ? rib.segments[k + 1].position : rib.segments[k].TransformPoint(rib.tipLocal);
                for (int step = 0; step <= 4; step++)
                    foreach (var c in Physics.OverlapSphere(Vector3.Lerp(a, b, step / 4f), 0.3f, ~0, QueryTriggerInteraction.Ignore))
                        if (rib.owner == null || !c.transform.IsChildOf(rib.owner)) touching.Add(c.name + (c.GetComponentInParent<ProceduralWorld>() != null ? " (terrain)" : "") + " L" + c.gameObject.layer);
            }
            log.AppendLine("  " + rib.transform.parent.name + "/" + rib.name + " moved " + share.ToString("P0") + ", open " + rib.open.ToString("0.00") + (rib.propped ? " propped" : "") + "; touches: " + string.Join(", ", touching.Take(6)));
        }
        ProfilerDriver.enabled = false;
        LongFrames();
        File.WriteAllText("ArtCaptures/LeafMap/MotherTest.txt", log.ToString());
        SessionState.SetBool(Key, false); firstFrame = -1f;
        EditorApplication.ExitPlaymode();
    }
}
