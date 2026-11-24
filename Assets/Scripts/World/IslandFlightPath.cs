using System.Collections.Generic;
using UnityEngine;

/// <summary>Closed Catmull-Rom route. Child waypoint transforms are edited with normal scene handles.</summary>
public sealed class IslandFlightPath : MonoBehaviour
{
    public Transform[] waypoints = new Transform[0];
    [Min(8)] public int samplesPerSegment = 48;
    [Tooltip("Rise and fall added on top of the waypoints, in metres (0 = flat). The heading stays level: the frame rises and sinks without tilting, like the sun over a day.")]
    [Min(0)] public float verticalAmplitude = 12;
    [Tooltip("Whole up-and-down cycles per lap, so the closed route still meets itself.")]
    [Min(1)] public int verticalCycles = 1;
    readonly List<float> lengths = new();
    readonly List<Vector3> samples = new();
    readonly List<float> parameters = new();
    public float Length { get; private set; }
    public bool Valid => waypoints != null && waypoints.Length >= 4 && System.Array.TrueForAll(waypoints, p => p != null);
    public void Rebuild()
    {
        samples.Clear(); lengths.Clear(); parameters.Clear(); Length = 0;
        if (!Valid) return;
        int count = waypoints.Length * Mathf.Max(8, samplesPerSegment);
        for (int i = 0; i <= count; i++)
        {
            float parameter = (float)i / count * waypoints.Length;
            Vector3 point = Evaluate(parameter);
            if (i > 0) Length += Vector3.Distance(samples[i - 1], point);
            samples.Add(point); lengths.Add(Length); parameters.Add(parameter);
        }
    }
    /// <summary>
    /// Spline parameter at an arc length. The samples only map distance to parameter; positions and
    /// headings come from the curve itself. Following the straight lines between samples made the
    /// heading turn in steps at every sample (about 1.25 m apart): the deck's far edges then whipped
    /// sideways several times a second, which riders felt as a shake.
    /// </summary>
    float ParameterAt(float distance)
    {
        float s = Mathf.Repeat(distance, Length);
        int hi = lengths.BinarySearch(s);
        if (hi < 0) hi = ~hi;
        hi = Mathf.Clamp(hi, 1, samples.Count - 1);
        return Mathf.Lerp(parameters[hi - 1], parameters[hi], Mathf.InverseLerp(lengths[hi - 1], lengths[hi], s));
    }
    Vector3 Evaluate(float parameter)
    {
        int n = waypoints.Length, i = Mathf.FloorToInt(parameter) % n;
        float t = parameter - Mathf.Floor(parameter);
        Vector3 a = waypoints[(i + n - 1) % n].position, b = waypoints[i].position;
        Vector3 c = waypoints[(i + 1) % n].position, d = waypoints[(i + 2) % n].position;
        Vector3 point = .5f * ((2 * b) + (-a + c) * t + (2*a - 5*b + 4*c - d) * t*t + (-a + 3*b - 3*c + d) * t*t*t);
        // Smooth and periodic over the lap; included before arc-length sampling, so speed stays even along the 3D path.
        point.y += verticalAmplitude * Mathf.Sin(parameter / n * verticalCycles * Mathf.PI * 2);
        return point;
    }
    public Vector3 Position(float distance)
    {
        if (samples.Count < 2 || Length < .01f) return transform.position;
        return Evaluate(ParameterAt(distance));
    }
    public Quaternion Heading(float distance)
    {
        if (samples.Count < 2 || Length < .01f) return Quaternion.identity;
        // Level heading along the curve's own tangent (central difference in parameter space).
        float parameter = ParameterAt(distance), n = waypoints.Length;
        Vector3 tangent = Vector3.ProjectOnPlane(Evaluate(Mathf.Repeat(parameter + .002f, n)) - Evaluate(Mathf.Repeat(parameter - .002f, n)), Vector3.up);
        return tangent.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(tangent) : Quaternion.identity;
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
