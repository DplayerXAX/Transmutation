#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// Passive stutter recorder for play sessions. Every frame slower than the threshold is written to
/// Logs/HitchLog.txt with where the player stood and which engine phases took the time, so a slow spot can be
/// found without opening the Profiler. Editor and development builds only; costs almost nothing per frame.
/// </summary>
public sealed class HitchProbe : MonoBehaviour
{
    private const float ThresholdMs = 33f;

    private static readonly (string label, ProfilerCategory category, string marker)[] Phases =
    {
        ("scripts", ProfilerCategory.Scripts, "BehaviourUpdate"),
        ("fixed", ProfilerCategory.Physics, "FixedUpdate.PhysicsFixedUpdate"),
        ("gc", ProfilerCategory.Scripts, "GC.Collect"),
        ("instantiate", ProfilerCategory.Scripts, "Instantiate"),
        ("activate", ProfilerCategory.Scripts, "GameObject.Activate"),
        ("render", ProfilerCategory.Render, "PostLateUpdate.FinishFrameRendering"),
        ("bake", ProfilerCategory.Physics, "Erosion.CollisionBake"),
    };

    private readonly List<ProfilerRecorder> recorders = new List<ProfilerRecorder>();
    private ProfilerRecorder allocated;
    private StreamWriter log;
    private Transform player;
    private int hitches;
    private float worst, nextSearch;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var probe = new GameObject("Hitch probe") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(probe);
        probe.AddComponent<HitchProbe>();
    }

    private void OnEnable()
    {
        foreach (var phase in Phases) recorders.Add(ProfilerRecorder.StartNew(phase.category, phase.marker));
        allocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        try
        {
            Directory.CreateDirectory("Logs");
            log = new StreamWriter(Path.Combine("Logs", "HitchLog.txt"), false) { AutoFlush = true };
            log.WriteLine("Hitch log " + System.DateTime.Now + "  (frames over " + ThresholdMs + " ms; times in ms, gc alloc in KB)");
        }
        catch (IOException) { log = null; }
    }

    private void OnDisable()
    {
        foreach (var recorder in recorders) recorder.Dispose();
        recorders.Clear();
        allocated.Dispose();
        if (log != null) { log.WriteLine($"End: {hitches} hitches, worst {worst:0} ms"); log.Dispose(); log = null; }
    }

    private void LateUpdate()
    {
        // The frame just measured is the previous one (recorders report the last finished frame).
        float ms = Time.unscaledDeltaTime * 1000f;
        if (log == null || Time.frameCount < 30 || ms < ThresholdMs) return;
        hitches++;
        worst = Mathf.Max(worst, ms);
        if (player == null && Time.unscaledTime >= nextSearch)
        {
            nextSearch = Time.unscaledTime + 2f;
            var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
            if (carrier != null) player = carrier.PlayerBody != null ? carrier.PlayerBody.transform : carrier.transform;
        }
        var line = new StringBuilder();
        line.Append($"t={Time.time:0.0}s frame={ms:0}ms");
        if (player != null) line.Append($" at=({player.position.x:0},{player.position.y:0},{player.position.z:0})");
        for (int i = 0; i < recorders.Count; i++)
        {
            double phase = recorders[i].LastValue / 1e6;
            if (phase >= 1.0) line.Append($" {Phases[i].label}={phase:0.0}");
        }
        line.Append($" alloc={allocated.LastValue / 1024}KB bubbles={TrappingBubble.LiveCount}");
        log.WriteLine(line.ToString());
    }
}
#endif
