using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Request-file capture of a slow-down that builds up over time (no menu). Write "x y z seconds" to Temp/Stall.request:
/// the editor enters Play, puts the player at that point and waits. Every 20 s it notes what is awake; when frames stay
/// slow, or the time is up, it profiles a few seconds, stops Play and writes Logs/StallReport.txt.
/// </summary>
[InitializeOnLoad]
public static class StallCapture
{
    private const string Request = "Temp/Stall.request", Status = "Temp/Stall.status", ReportPath = "Logs/StallReport.txt", Key = "StallCapture";
    private const double LoadWait = 12.0, ProfileSeconds = 6.0;
    private const float SlowFrame = 0.14f, SlowHold = 3f;

    private static double placedAt = -1, profileAt = -1, nextNote;
    private static float average, slowFor;
    private static readonly StringBuilder notes = new StringBuilder();
    private static string trigger;

    static StallCapture() => EditorApplication.update += Tick;

    private static void Tick()
    {
        if (SessionState.GetInt(Key, 0) == 0)
        {
            if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetString(Key + ".args", File.ReadAllText(Request).Trim());
            File.Delete(Request);
            SessionState.SetInt(Key, 1);
            File.WriteAllText(Status, "entering play");
            EditorApplication.EnterPlaymode();
            return;
        }
        if (!EditorApplication.isPlaying) return;
        try { Advance(); }
        catch (System.Exception e) { Finish("error: " + e); }
    }

    private static void Advance()
    {
        double now = EditorApplication.timeSinceStartup;
        string[] args = SessionState.GetString(Key + ".args", "0 0 0 300").Split(' ');
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (placedAt < 0)
        {
            if (nextNote <= 0) { nextNote = now + LoadWait; notes.Clear(); return; }
            if (now < nextNote) return;
            Rigidbody body = Player();
            if (body == null) throw new System.InvalidOperationException("no player body");
            body.position = new Vector3(float.Parse(args[0], culture), float.Parse(args[1], culture), float.Parse(args[2], culture));
            body.transform.position = body.position;
            body.linearVelocity = Vector3.zero;
            placedAt = now; nextNote = now; average = 0f; slowFor = 0f;
            return;
        }
        double elapsed = now - placedAt;
        if (profileAt < 0)
        {
            float frame = Time.unscaledDeltaTime;
            average = average <= 0f ? frame : Mathf.Lerp(average, frame, 0.1f);
            slowFor = elapsed > 10.0 && average > SlowFrame ? slowFor + frame : 0f;
            if (now >= nextNote) { nextNote = now + 20.0; Note(elapsed); File.WriteAllText(Status, $"waiting {elapsed:0}s, frame {average * 1000f:0} ms"); }
            bool slow = slowFor >= SlowHold, timeUp = elapsed >= double.Parse(args[3], culture);
            if (!slow && !timeUp) return;
            trigger = slow ? $"slow frames reached after {elapsed:0} s" : $"no sustained slow-down within {elapsed:0} s";
            Note(elapsed);
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.enabled = true;
            profileAt = now;
            File.WriteAllText(Status, "profiling");
            return;
        }
        if (now - profileAt < ProfileSeconds) return;
        ProfilerDriver.enabled = false;
        File.WriteAllText(ReportPath, trigger + "\n\n" + notes + "\n" + Summary());
        Finish("done: " + trigger);
    }

    private static Rigidbody Player()
    {
        var carrier = Object.FindFirstObjectByType<PlayerCreatureCarrier>();
        return carrier != null ? carrier.PlayerBody : null;
    }

    private static string Kind(Rigidbody body)
    {
        var creature = body.GetComponentInParent<Creature>();
        if (creature != null) return creature.GetType().Name;
        return System.Text.RegularExpressions.Regex.Replace(body.transform.root.name, @"[\s(]*\d+\)?$", "");
    }

    // What is moving at this moment: awake bodies by kind, with how many are close to the player.
    private static void Note(double elapsed)
    {
        Vector3 at = Player() != null ? Player().position : Vector3.zero;
        var bodies = Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
        var awake = bodies.Where(b => !b.isKinematic && !b.IsSleeping()).ToList();
        var fluid = Object.FindFirstObjectByType<ErosiveFluidSystem>();
        var world = Object.FindFirstObjectByType<ProceduralWorld>();
        notes.Append($"t={elapsed,4:0}s frame={average * 1000f,4:0}ms player=({at.x:0},{at.y:0},{at.z:0}) bodies={bodies.Length} awake={awake.Count}");
        if (fluid != null) notes.Append($" water={fluid.ActiveCount}");
        if (world != null) notes.Append($" editedSamples={world.ModifiedSampleCount} pending={world.PendingSampleCount} workers={world.ActiveWorkers}");
        notes.Append(" | ");
        notes.AppendLine(string.Join(", ", awake.GroupBy(Kind).OrderByDescending(g => g.Count()).Take(7)
            .Select(g => $"{g.Key} {g.Count()} (near {g.Count(b => (b.position - at).sqrMagnitude < 900f)})")));
    }

    private static string Summary()
    {
        int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex, frames = 0;
        double frameMs = 0;
        var self = new Dictionary<string, double>();
        var alloc = new Dictionary<string, double>();
        var calls = new Dictionary<string, double>();
        var kids = new List<int>();
        for (int f = first; f <= last; f++)
        {
            using (var view = ProfilerDriver.GetHierarchyFrameDataView(f, 0, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false))
            {
                if (view == null || !view.valid) continue;
                frames++;
                frameMs += view.frameTimeMs;
                var stack = new Stack<int>();
                stack.Push(view.GetRootItemID());
                while (stack.Count > 0)
                {
                    int id = stack.Pop();
                    string name = view.GetItemName(id);
                    if (!string.IsNullOrEmpty(name))
                    {
                        Add(self, name, view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnSelfTime));
                        Add(alloc, name, view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnGcMemory) / 1024f);
                        Add(calls, name, view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls));
                    }
                    view.GetItemChildren(id, kids);
                    foreach (int child in kids) stack.Push(child);
                }
            }
        }
        int n = Mathf.Max(1, frames);
        var text = new StringBuilder($"Profile: {frames} frames, average {frameMs / n:0.0} ms\nSelf time per frame (ms, calls per frame):\n");
        foreach (var p in self.OrderByDescending(p => p.Value).Take(35)) text.AppendLine($"  {p.Value / n,8:0.00}  x{calls[p.Key] / n,-8:0}  {p.Key}");
        text.AppendLine("Managed allocation per frame (KB):");
        foreach (var p in alloc.OrderByDescending(p => p.Value).Take(15)) text.AppendLine($"  {p.Value / n,8:0.0}  {p.Key}");
        return text.ToString();
    }

    private static void Add(Dictionary<string, double> table, string key, double value)
    {
        table.TryGetValue(key, out double old);
        table[key] = old + value;
    }

    private static void Finish(string result)
    {
        SessionState.SetInt(Key, 0);
        placedAt = profileAt = -1;
        nextNote = 0;
        ProfilerDriver.enabled = false;
        File.WriteAllText(Status, result);
        EditorApplication.delayCall += EditorApplication.ExitPlaymode;
    }
}
