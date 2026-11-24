using UnityEditor;
using UnityEngine;

/// <summary>Starts the opt-in rider jitter measurement. Play Scene-Fishing first.</summary>
public static class RiderJitterProbeMenu
{
    [MenuItem("Tools/Creatures/Fishing/Measure Island Rider Jitter (Play Mode)")]
    public static void Start()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[RiderJitter] Enter Play Mode in Scene-Fishing first."); return; }
        new GameObject("Rider Jitter Probe (temporary)").AddComponent<RiderJitterProbe>();
    }
}
