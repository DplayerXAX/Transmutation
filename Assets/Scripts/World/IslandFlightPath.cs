using System.Collections.Generic;
using UnityEngine;

/// <summary>Closed Catmull-Rom route. Child waypoint transforms are edited with normal scene handles.</summary>
public sealed class IslandFlightPath : MonoBehaviour
{
    public Transform[] waypoints = new Transform[0];
    [Min(8)] public int samplesPerSegment = 48;
    readonly List<float> lengths = new();
    readonly List<Vector3> samples = new();
    public float Length { get; private set; }
    public bool Valid => waypoints != null && waypoints.Length >= 4 && System.Array.TrueForAll(waypoints, p => p != null);
    public void Rebuild()
    {
        samples.Clear(); lengths.Clear(); Length = 0;
        if (!Valid) return;
        int count = waypoints.Length * Mathf.Max(8, samplesPerSegment);
        for (int i = 0; i <= count; i++)
        {
            Vector3 point = Evaluate((float)i / count * waypoints.Length);
            if (i > 0) Length += Vector3.Distance(samples[i - 1], point);
            samples.Add(point); lengths.Add(Length);
        }
    }
    Vector3 Evaluate(float parameter)
    {
        int n = waypoints.Length, i = Mathf.FloorToInt(parameter) % n;
        float t = parameter - Mathf.Floor(parameter);
        Vector3 a = waypoints[(i + n - 1) % n].position, b = waypoints[i].position;
        Vector3 c = waypoints[(i + 1) % n].position, d = waypoints[(i + 2) % n].position;
        return .5f * ((2 * b) + (-a + c) * t + (2*a - 5*b + 4*c - d) * t*t + (-a + 3*b - 3*c + d) * t*t*t);
    }
    public Vector3 Position(float distance)
    {
        if (samples.Count < 2 || Length < .01f) return transform.position;
        float s = Mathf.Repeat(distance, Length);
        int hi = lengths.BinarySearch(s);
        if (hi < 0) hi = ~hi;
        hi = Mathf.Clamp(hi, 1, samples.Count - 1);
        return Vector3.Lerp(samples[hi - 1], samples[hi], Mathf.InverseLerp(lengths[hi - 1], lengths[hi], s));
    }
    public Quaternion Heading(float distance)
    {
        Vector3 tangent = Vector3.ProjectOnPlane(Position(distance + .5f) - Position(distance - .5f), Vector3.up);
        return tangent.sqrMagnitude > .001f ? Quaternion.LookRotation(tangent) : Quaternion.identity;
    }
    void Awake() => Rebuild();
    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) Rebuild();
        Gizmos.color = Color.cyan;
        for (int i = 1; i < samples.Count; i++) Gizmos.DrawLine(samples[i-1], samples[i]);
        if (waypoints == null) return;
        foreach (var point in waypoints) if (point) Gizmos.DrawWireSphere(point.position, 2);
    }
}
