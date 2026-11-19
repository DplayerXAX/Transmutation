using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Request-file profiler run for the open scene (no menu). Write "run" to Temp/Perf.request and the open editor:
///   1. presses Play and times the enter-Play freeze (domain reload + scene load + first frame),
///   2. records the Unity profiler in four phases: startup (first seconds), idle with the normal editor layout,
///      walking (the player body is carried along a path), and walking with the Game view maximised (no Scene view),
///   3. stops Play and writes Logs/PerfReport.txt (EditorLoop = editor-only cost, PlayerLoop = the game itself).
/// Progress goes to Temp/Perf.status.
/// </summary>
[InitializeOnLoad]
public static class PerfCapture
{
    private const string Request = "Temp/Perf.request", Status = "Temp/Perf.status", ReportPath = "Logs/PerfReport.txt";
    private const string Key = "PerfCapture";
    private static readonly string[] PhaseNames = { "", "startup", "idle (editor layout)", "walking (editor layout)", "walking (Game view maximised)" };
    private static readonly float[] PhaseSeconds = { 0f, 14f, 8f, 10f, 10f };

    private static double phaseStart = -1;
    private static int firstPlayFrame = -1;
    private static Vector3 walkOrigin, walkDirection;
    private static float walked;
    private static EditorWindow maximised;

    static PerfCapture()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += PlayModeChanged;
    }

    private static int Stage { get => SessionState.GetInt(Key, 0); set => SessionState.SetInt(Key, value); }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (Stage == 0) return;
        if (change == PlayModeStateChange.EnteredEditMode) { Stage = 0; phaseStart = -1; File.WriteAllText(Status, "stopped: Play ended early"); return; }
        if (change == PlayModeStateChange.EnteredPlayMode)
            SessionState.SetString(Key + ".entered", System.DateTime.Now.Ticks.ToString());
    }

    private static void Tick()
    {
        if (Stage == 0)
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string command = File.ReadAllText(Request).Trim();
            File.Delete(Request);
            if (command != "run") { File.WriteAllText(Status, "unknown command " + command); return; }
            Begin();
            return;
        }
        if (!EditorApplication.isPlaying) return;
        try { Advance(); }
        catch (System.Exception e) { Finish("error: " + e); }
    }

    private static void Begin()
    {
        File.WriteAllText(ReportPath, "");
        SessionState.SetString(Key + ".pressed", System.DateTime.Now.Ticks.ToString());
        SessionState.EraseString(Key + ".report");
        Stage = 1;
        phaseStart = -1;
        firstPlayFrame = -1;
        ProfilerDriver.ClearAllFrames();
        ProfilerDriver.profileEditor = true; // so the enter-Play scene reload frame is recorded too
        ProfilerDriver.enabled = true;
        File.WriteAllText(Status, "entering play");
        EditorApplication.EnterPlaymode();
    }

    private static void Advance()
    {
        int stage = Stage;
        if (phaseStart < 0)
        {
            // First editor tick inside Play: stamp the enter-Play time once.
            if (stage == 1 && firstPlayFrame < 0)
            {
                firstPlayFrame = Time.frameCount;
                var pressed = new System.DateTime(long.Parse(SessionState.GetString(Key + ".pressed", "0")));
                var entered = new System.DateTime(long.Parse(SessionState.GetString(Key + ".entered", System.DateTime.Now.Ticks.ToString())));
                AppendReport($"Enter Play: {(System.DateTime.Now - pressed).TotalSeconds:0.0} s from pressing Play to the first Play frame " +
                             $"({(entered - pressed).TotalSeconds:0.0} s until Unity reported 'entered Play', i.e. domain reload + scene reload).\n" +
                             "(Unity's own split of that time is in Editor.log: 'Domain Reload Profiling' and the 'Integration' line after 'Loaded scene'.)\n");
            }
            phaseStart = EditorApplication.timeSinceStartup;
            if (stage >= 3) StartWalk();
            if (stage == 4) Maximise(true);
            if (stage > 1) ProfilerDriver.ClearAllFrames(); // keep the very first Play frames in the startup phase
            ProfilerDriver.profileEditor = stage <= 2; // startup + idle also break down the editor's own work
            ProfilerDriver.enabled = true;
        }
        if (stage >= 3) Walk();
        double elapsed = EditorApplication.timeSinceStartup - phaseStart;
        if (stage == 1)
        {
            var world = Object.FindFirstObjectByType<ProceduralWorld>();
            File.WriteAllText(Status, $"startup {elapsed:0}s, terrain {(world != null ? world.LoadProgress : 0f):P0}");
        }
        else File.WriteAllText(Status, $"{PhaseNames[stage]} {elapsed:0}s");
        if (elapsed < PhaseSeconds[stage]) return;

        AppendReport(Summarise(PhaseNames[stage], stage == 1));
        if (stage == 4) Maximise(false);
        phaseStart = -1;
        if (stage < 4) { Stage = stage + 1; return; }
        Finish("done");
    }

    private static void Finish(string status)
    {
        Maximise(false);
        ProfilerDriver.enabled = false;
        Stage = 0;
        File.WriteAllText(ReportPath, SessionState.GetString(Key + ".report", "") + (status == "done" ? "" : "\n" + status));
        File.WriteAllText(Status, status);
        EditorApplication.ExitPlaymode();
    }

    private static void AppendReport(string text)
    {
        string all = SessionState.GetString(Key + ".report", "") + text + "\n";
        SessionState.SetString(Key + ".report", all);
        File.WriteAllText(ReportPath, all);
    }

    // ---------------- Walking ----------------

    private static Rigidbody PlayerBody()
    {
        var carrier = Object.FindFirstObjectByType<PlayerCreatureCarrier>();
        return carrier != null ? carrier.PlayerBody : null;
    }

    private static void StartWalk()
    {
        var body = PlayerBody();
        if (body == null) return;
        if (walked <= 0f) { walkOrigin = body.position; walkDirection = Vector3.ProjectOnPlane(body.transform.forward, Vector3.up).normalized; }
        if (walkDirection.sqrMagnitude < 0.5f) walkDirection = Vector3.forward;
        walked = 0f;
    }

    // Carries the body along a straight line at walking speed, resting on the ground (stress test for culling/streaming).
    private static void Walk()
    {
        var body = PlayerBody();
        if (body == null) return;
        walked += 6f * Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        Vector3 p = walkOrigin + walkDirection * walked;
        if (Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out var hit, 120f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y + 1.1f;
        else p.y = body.position.y;
        body.position = p;
        body.linearVelocity = Vector3.zero;
        body.rotation = Quaternion.LookRotation(walkDirection, Vector3.up);
    }

    private static void Maximise(bool on)
    {
        if (on)
        {
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var views = Resources.FindObjectsOfTypeAll(type);
            if (views.Length == 0) return;
            maximised = (EditorWindow)views[0];
            maximised.maximized = true;
        }
        else if (maximised != null) { maximised.maximized = false; maximised = null; }
    }

    // ---------------- Profiler summary ----------------

    private static string Summarise(string phase, bool startup)
    {
        int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
        var paths = new Dictionary<string, double>();
        var self = new Dictionary<string, double>();
        var frames = new List<(float ms, string top)>();
        double renderThread = 0;
        int renderFrames = 0, renderThreadIndex = FindThread(first, "Render Thread");
        var children = new List<int>();
        for (int f = first; f <= last; f++)
        {
            using (var view = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
            {
                if (view == null || !view.valid) continue;
                var items = new List<(string, float)>();
                Walk(view, view.GetRootItemID(), "", 0, paths, self, items, children);
                string top = string.Join(", ", items.OrderByDescending(i => i.Item2).Take(5).Select(i => $"{i.Item1} {i.Item2:0.0}"));
                frames.Add((view.frameTimeMs, top));
            }
            if (renderThreadIndex < 0) continue;
            using (var view = ProfilerDriver.GetHierarchyFrameDataView(f, renderThreadIndex, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
            {
                if (view == null || !view.valid) continue;
                view.GetItemChildren(view.GetRootItemID(), children);
                float busy = 0f;
                foreach (int c in children) if (!view.GetItemName(c).StartsWith("Semaphore") && !view.GetItemName(c).Contains("Wait")) busy += view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime);
                renderThread += busy; renderFrames++;
            }
        }
        int n = Mathf.Max(1, frames.Count);
        var sorted = frames.Select(x => x.ms).OrderBy(x => x).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"=== {phase}: {frames.Count} frames ===");
        if (sorted.Count > 0)
            sb.AppendLine($"Frame time: average {sorted.Average():0.0} ms ({1000f / sorted.Average():0} fps), median {sorted[sorted.Count / 2]:0.0}, 95% {sorted[(int)(sorted.Count * 0.95f)]:0.0}, worst {sorted[sorted.Count - 1]:0.0}, frames over 33 ms: {sorted.Count(x => x > 33f)}");
        paths.TryGetValue("PlayerLoop", out double player); paths.TryGetValue("EditorLoop", out double editor);
        sb.AppendLine($"Main thread per frame: PlayerLoop (game) {player / n:0.0} ms, EditorLoop (editor only) {editor / n:0.0} ms");
        if (renderFrames > 0) sb.AppendLine($"Render thread busy per frame: {renderThread / renderFrames:0.0} ms");
        sb.AppendLine("Biggest blocks (total ms per frame, by call path, depth <= 4):");
        foreach (var p in paths.Where(p => p.Key.Count(c => c == '/') >= 1).OrderByDescending(p => p.Value).Take(40))
            sb.AppendLine($"  {p.Value / n,7:0.00}  {p.Key}");
        sb.AppendLine("Biggest self time (ms per frame, any depth):");
        foreach (var p in self.OrderByDescending(p => p.Value).Take(30))
            sb.AppendLine($"  {p.Value / n,7:0.00}  {p.Key}");
        sb.AppendLine(startup ? "Slowest startup frames:" : "Slowest frames:");
        foreach (var w in frames.OrderByDescending(w => w.ms).Take(startup ? 12 : 6)) sb.AppendLine($"  {w.ms:0.0} ms: {w.top}");
        // Call tree of the slowest frames (startup) or one typical frame (otherwise), blocks over 1 ms.
        var order = Enumerable.Range(0, frames.Count).OrderByDescending(i => frames[i].ms).ToList();
        var pick = startup ? order.Take(3).ToList() : new List<int> { order[order.Count / 2] };
        foreach (int i in pick)
        {
            using (var view = ProfilerDriver.GetHierarchyFrameDataView(first + i, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false))
            {
                if (view == null || !view.valid) continue;
                sb.AppendLine($"Call tree of a {view.frameTimeMs:0.0} ms frame (blocks over {(startup ? 10 : 1)} ms):");
                Tree(view, view.GetRootItemID(), 1, startup ? 10f : 1f, sb);
            }
        }
        return sb.ToString();
    }

    private static void Tree(HierarchyFrameDataView view, int id, int depth, float minMs, StringBuilder sb)
    {
        if (depth > 14) return;
        var kids = new List<int>();
        view.GetItemChildren(id, kids);
        foreach (int c in kids)
        {
            float total = view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime);
            if (total < minMs) continue;
            int calls = (int)view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnCalls);
            sb.AppendLine($"{new string(' ', depth * 2)}{total:0.0} ms  {view.GetItemName(c)}{(calls > 1 ? "  x" + calls : "")}");
            Tree(view, c, depth + 1, minMs, sb);
        }
    }

    private static void Walk(HierarchyFrameDataView view, int id, string path, int depth, Dictionary<string, double> paths,
        Dictionary<string, double> self, List<(string, float)> items, List<int> scratch)
    {
        var kids = new List<int>();
        view.GetItemChildren(id, kids);
        foreach (int c in kids)
        {
            string name = view.GetItemName(c);
            string p = path.Length == 0 ? name : path + "/" + name;
            float total = view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnTotalTime);
            float own = view.GetItemColumnDataAsFloat(c, HierarchyFrameDataView.columnSelfTime);
            if (depth <= 4) { paths.TryGetValue(p, out double t); paths[p] = t + total; }
            self.TryGetValue(name, out double s); self[name] = s + own;
            items.Add((name, own));
            Walk(view, c, p, depth + 1, paths, self, items, scratch);
        }
    }

    private static int FindThread(int frame, string name)
    {
        for (int i = 0; i < 128; i++)
        {
            using (var raw = ProfilerDriver.GetRawFrameDataView(frame, i))
            {
                if (raw == null || !raw.valid) return -1;
                if (raw.threadName == name) return i;
            }
        }
        return -1;
    }
}
