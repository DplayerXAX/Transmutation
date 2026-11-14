using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A leaf tornado launches each entering movable body once. No particle collision.</summary>
[RequireComponent(typeof(LeafGarden))]
public sealed class LeafTornadoLaunchVolume : MonoBehaviour
{
    [Tooltip("Minimum upward speed. Height-based launches can exceed this.")]
    [Min(1)] public float LaunchSpeed = 24;
    [Tooltip("Minimum rise above the entry point; roughly four times the original 9.6 m launch.")]
    [Min(1)] public float MinimumLaunchHeight = 40;
    [Tooltip("Aim above the tornado's full top, even if entered while it is still gathering.")]
    [Min(1)] public float TornadoHeightMultiplier = 1.15f;
    [Min(0)] public float ReentryCooldown = 0.8f;
    public int LaunchCount { get; private set; }
    public int QueryCapacity => hits.Length;
    private LeafGarden garden;
    private Collider[] hits = new Collider[128];
    private readonly HashSet<Rigidbody> seen = new HashSet<Rigidbody>();
    private readonly HashSet<Rigidbody> inside = new HashSet<Rigidbody>();
    private readonly Dictionary<Rigidbody, float> lastLaunch = new Dictionary<Rigidbody, float>();
    private readonly List<Rigidbody> removals = new List<Rigidbody>();
    private int eventNumber = -1;
    private void Awake() => garden = GetComponent<LeafGarden>();
    public void Clear()
    { inside.Clear(); lastLaunch.Clear(); LaunchCount = 0; eventNumber = garden != null ? garden.EventNumber : -1; }
    public static bool Eligible(Rigidbody body)
    {
        if (body == null || body.isKinematic || !body.detectCollisions) return false;
        return !body.TryGetComponent<Creature>(out var c) ||
            !(c.IsCarried || c.IsBubbleCaptured || c.IsConsumptionPending ||
            c.TryGetComponent<WebCapture>(out var web) && web.IsCaptured);
    }
    public float TargetApexY => garden.TornadoCenter.y + Mathf.Max(MinimumLaunchHeight, garden.Height * TornadoHeightMultiplier);
    public float SpeedAt(Vector3 entry)
    {
        float rise = Mathf.Max(MinimumLaunchHeight, TargetApexY - entry.y);
        return Mathf.Max(LaunchSpeed, Mathf.Sqrt(2 * Mathf.Max(0.01f, -Physics.gravity.y) * rise));
    }
    private void FixedUpdate()
    {
        if (garden.EventNumber != eventNumber) { inside.Clear(); lastLaunch.Clear(); eventNumber = garden.EventNumber; }
        if (!garden.LaunchActive) { inside.Clear(); return; }
        float h = garden.Height * garden.Blend, radius = Mathf.Max(garden.SourceRadius, garden.TopRadius) + 4;
        Vector3 a = garden.TornadoCenter, b = a + Vector3.up * h;
        int count;
        while (true)
        {
            count = ReflectionPhysics.OverlapCapsuleNonAlloc(a,b,radius,hits,~0,QueryTriggerInteraction.Ignore);
            if (count < hits.Length) break;
            if (hits.Length >= 16384) { Debug.LogWarning("Leaf tornado query saturated at 16k colliders.",this); break; }
            Array.Resize(ref hits,hits.Length*2);
        }
        seen.Clear();
        for (int i=0;i<count;i++)
        {
            Rigidbody body=hits[i].attachedRigidbody;
            if (!Eligible(body) || !garden.Contains(body.worldCenterOfMass) || !seen.Add(body)) continue;
            if (inside.Contains(body)) continue;
            if (lastLaunch.TryGetValue(body,out float previous) && Time.time-previous<ReentryCooldown) continue;
            float delta = Mathf.Max(0,SpeedAt(body.worldCenterOfMass)-body.linearVelocity.y);
            if (delta>0) { CreatureImpulse.Apply(body,Vector3.up*delta,0.3f); LaunchCount++; }
            inside.Add(body); lastLaunch[body]=Time.time;
        }
        removals.Clear();
        foreach (var body in inside) if (!seen.Contains(body)) removals.Add(body);
        foreach (var body in removals) inside.Remove(body);
        removals.Clear();
        foreach (var pair in lastLaunch) if (pair.Key==null || !inside.Contains(pair.Key) && Time.time-pair.Value>ReentryCooldown) removals.Add(pair.Key);
        foreach (var body in removals) lastLaunch.Remove(body);
    }
}
