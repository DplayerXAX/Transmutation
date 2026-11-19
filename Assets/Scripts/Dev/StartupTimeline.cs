#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Startup timeline for play sessions (editor and development builds only). Writes Logs/StartupTimeline.txt with the
/// time of every log message from the moment the scene starts loading, plus milestones: scene objects awake, first
/// frame, terrain complete, loading veil gone. Gaps between lines show which Awake/OnEnable/Start work is slow.
/// </summary>
public sealed class StartupTimeline : MonoBehaviour
{
    private static readonly Stopwatch clock = new Stopwatch();
    private static readonly StringBuilder lines = new StringBuilder();
    private static double last;
    private static bool recording;
    private bool terrainLogged, veilSeen, veilLogged;
    private int frames;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BeforeScene()
    {
        lines.Clear();
        last = 0;
#if UNITY_EDITOR
        Creature.AwakeTicks.Clear(); // statics survive between Plays while script reload is off
#endif
        clock.Restart();
        recording = true;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        Mark("scene starts loading (Awake / OnEnable of every scene object follow)");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AfterScene()
    {
        Mark("all scene objects awake (Start comes next, inside the first frame)");
#if UNITY_EDITOR
        double total = 0;
        var species = new System.Collections.Generic.List<(double ms, string name)>();
        foreach (var pair in Creature.AwakeTicks)
        {
            double ms = pair.Value * 1000.0 / Stopwatch.Frequency;
            total += ms;
            species.Add((ms, pair.Key.Name));
        }
        species.Sort((a, b) => b.ms.CompareTo(a.ms));
        var text = new StringBuilder($"creature setup in Awake: {total:0} ms total;");
        for (int i = 0; i < species.Count && i < 14; i++) text.Append($" {species[i].name} {species[i].ms:0}");
        Mark(text.ToString());
#endif
        var go = new GameObject("Startup timeline") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(go);
        go.AddComponent<StartupTimeline>();
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (!recording) return;
        if (clock.Elapsed.TotalSeconds > 90) { recording = false; Application.logMessageReceived -= OnLog; return; }
        int cut = message.IndexOf('\n');
        Mark("log: " + (cut > 0 ? message.Substring(0, cut) : message));
    }

    private static void Mark(string what)
    {
        double now = clock.Elapsed.TotalMilliseconds;
        lines.AppendLine($"{now,9:0} ms  (+{now - last,7:0})  {what}");
        last = now;
        // Written as it goes, so the file is there even if Play is stopped early.
        try { Directory.CreateDirectory("Logs"); File.WriteAllText(Path.Combine("Logs", "StartupTimeline.txt"), lines.ToString()); }
        catch (IOException) { }
    }

    private void Update()
    {
        frames++;
        if (frames == 1) Mark("first Update (Start of every scene object has run)");
        if (frames == 2) Mark("second frame (the first frame has been drawn)");
        if (!terrainLogged)
        {
            var world = FindFirstObjectByType<ProceduralWorld>();
            if (world == null || world.GenerationComplete) { terrainLogged = true; Mark("terrain complete"); }
        }
        bool veil = FindFirstObjectByType<LoadingVeil>() != null;
        if (veil) veilSeen = true;
        if (!veilLogged && frames > 2 && !veil) { veilLogged = true; Mark(veilSeen ? "loading veil gone" : "no loading veil in this scene"); }
        if ((terrainLogged && veilLogged) || frames > 3000) Finish();
    }

    private void OnDisable() { if (recording) Finish(); }

    private void Finish()
    {
        if (!recording) return;
        Mark("timeline end");
        recording = false;
        Application.logMessageReceived -= OnLog;
        try { Directory.CreateDirectory("Logs"); File.WriteAllText(Path.Combine("Logs", "StartupTimeline.txt"), lines.ToString()); }
        catch (IOException) { }
        enabled = false;
    }
}
#endif
