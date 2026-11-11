using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Batch only: plays Scene_LeafMap, records the Unity profiler for a few seconds once the world is built, and writes
/// where the main thread's time goes (self time per profiler sample, which names each script's Update/FixedUpdate)
/// to ArtCaptures/LeafMap/Profile.txt. -executeMethod LeafMapProfileBatch.Run [-profileSpot x,z]
/// </summary>
[InitializeOnLoad]
public static class LeafMapProfileBatch
{
    private const string Key = "LeafMapProfileBatch";
    private const int Frames = 240;
    private static int frame;
    private static double readyAt = -1;

    static LeafMapProfileBatch() { EditorApplication.update += Tick; }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new System.InvalidOperationException("Run in an isolated batch project.");
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Scene_LeafMap.unity", UnityEditor.SceneManagement.OpenSceneMode.Single);
        SessionState.SetInt(Key, 1);
        SessionState.SetFloat(Key + ".start", (float)EditorApplication.timeSinceStartup);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        int stage = SessionState.GetInt(Key, 0);
        if (stage == 0) return;
        if (EditorApplication.timeSinceStartup > 1200.0) EditorApplication.Exit(2);
        if (!EditorApplication.isPlaying) return;
        if (stage == 1)
        {
            var world = Object.FindFirstObjectByType<ProceduralWorld>();
            if (world == null || !world.GenerationComplete) return;
            if (readyAt < 0) readyAt = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - readyAt < 10.0) return; // let creatures settle and bubbles fill the air
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.enabled = true;
            UnityEngine.Profiling.Profiler.enabled = true;
            frame = 0;
            SessionState.SetInt(Key, 2);
            return;
        }
        if (stage == 2)
        {
            if (++frame < Frames) return;
            ProfilerDriver.enabled = false;
            Write();
            SessionState.SetInt(Key, 0);
            EditorApplication.Exit(0);
        }
    }

    private static void Write()
    {
        var self = new Dictionary<string, double>();
        var calls = new Dictionary<string, int>();
        int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex, counted = 0;
        double frameMs = 0;
        for (int f = first; f <= last; f++)
        {
            using (var view = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false))
            {
                if (view == null || !view.valid) continue;
                counted++;
                frameMs += view.frameTimeMs;
                var stack = new Stack<int>();
                var children = new List<int>();
                stack.Push(view.GetRootItemID());
                while (stack.Count > 0)
                {
                    int id = stack.Pop();
                    string name = view.GetItemName(id);
                    float ms = view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime);
                    int n = (int)view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls);
                    if (!string.IsNullOrEmpty(name))
                    {
                        self.TryGetValue(name, out double s); self[name] = s + ms;
                        calls.TryGetValue(name, out int c); calls[name] = c + n;
                    }
                    view.GetItemChildren(id, children);
                    foreach (int child in children) stack.Push(child);
                }
            }
        }
        // The worst frames (hitches), each with what took longest in it.
        var worst = new List<(int frame, float ms, string top)>();
        for (int f = first; f <= last; f++)
        {
            using (var view = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false))
            {
                if (view == null || !view.valid) continue;
                var items = new List<(string, float)>();
                var stack = new Stack<int>(); var children = new List<int>();
                stack.Push(view.GetRootItemID());
                while (stack.Count > 0)
                {
                    int id = stack.Pop();
                    items.Add((view.GetItemName(id), view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime)));
                    view.GetItemChildren(id, children);
                    foreach (int c in children) stack.Push(c);
                }
                string top = string.Join(", ", items.OrderByDescending(i => i.Item2).Take(6).Select(i => $"{i.Item1} {i.Item2:0.0}"));
                worst.Add((f, view.frameTimeMs, top));
            }
        }
        var frameTimes = worst.Select(w => w.ms).OrderBy(x => x).ToList();
        var lines = new List<string> { $"{counted} frames, average frame {frameMs / Mathf.Max(1, counted):0.00} ms (batch mode: no GPU work)", "", "Self time per frame (ms), largest first:" };
        if (frameTimes.Count > 0)
            lines.Add($"Frame time: median {frameTimes[frameTimes.Count / 2]:0.0} ms, 95% {frameTimes[(int)(frameTimes.Count * 0.95f)]:0.0} ms, worst {frameTimes[frameTimes.Count - 1]:0.0} ms, frames over 25 ms: {frameTimes.Count(x => x > 25f)}");
        lines.Add("Worst frames:");
        foreach (var w in worst.OrderByDescending(w => w.ms).Take(8)) lines.Add($"  {w.ms:0.0} ms: {w.top}");
        lines.Add("");
        foreach (var pair in self.OrderByDescending(p => p.Value).Take(60))
            lines.Add($"  {pair.Value / Mathf.Max(1, counted),7:0.000}  x{calls[pair.Key] / Mathf.Max(1, counted),-6}  {pair.Key}");
        Directory.CreateDirectory("ArtCaptures/LeafMap");
        File.WriteAllText("ArtCaptures/LeafMap/Profile.txt", string.Join("\n", lines));
    }
}
