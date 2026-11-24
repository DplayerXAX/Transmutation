using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Opt-in Play Mode measurement, never saved into scenes. Samples every island member's rendered
/// pose in the island's rendered (interpolated) frame and reports jitter as the per-frame second
/// difference of that local position: smooth walking or standing gives small values, a pose that
/// saws back and forth against the deck gives large ones. Runs the current timing first, then
/// puts each member's Rigidbody back on its original interpolation (the timing before the
/// rider-timing fix) for the same length, then restores the fix.
/// </summary>
public sealed class RiderJitterProbe : MonoBehaviour
{
    public float seconds = 6;
    public static string Result { get; private set; } = "";

    sealed class Track { public string name; public Vector3 a, b; public int n; public double sum; public float max; }

    IEnumerator Start()
    {
        var frame = MovingFrame.Active.Count > 0 ? MovingFrame.Active[0] : null;
        if (!frame) { Result = "No MovingFrame active."; Debug.LogWarning("[RiderJitter] " + Result); Destroy(gameObject); yield break; }
        yield return new WaitForSeconds(1);
        var report = new StringBuilder();
        report.AppendLine($"[RiderJitter] island speed {frame.Speed:0.00} m/s, fixed dt {Time.fixedDeltaTime:0.000} s");
        var fixedTracks = new Dictionary<MovementFrameMember, Track>();
        yield return Sample(frame, fixedTracks);
        Append(report, "Fixed timing", fixedTracks);

        var switched = new List<MovementFrameMember>();
        foreach (var member in new List<MovementFrameMember>(frame.Members))
            if (member && member.OriginalInterpolation is RigidbodyInterpolation original && member.TryGetComponent<Rigidbody>(out var body) && body.interpolation != original)
            { body.interpolation = original; switched.Add(member); }
        report.AppendLine($"Switched {switched.Count} member bodies back to their original interpolation for the legacy pass.");
        var legacyTracks = new Dictionary<MovementFrameMember, Track>();
        yield return Sample(frame, legacyTracks);
        foreach (var member in switched)
            if (member && member.OwnedInterpolation is RigidbodyInterpolation owned && member.TryGetComponent<Rigidbody>(out var body)) body.interpolation = owned;
        Append(report, "Legacy timing (switched bodies only differ)", legacyTracks);
        report.AppendLine("Per switched member, fixed vs legacy max (m):");
        foreach (var member in switched)
            if (member && fixedTracks.TryGetValue(member, out var f) && legacyTracks.TryGetValue(member, out var l))
                report.AppendLine($"  {member.name}: {f.max:0.0000} vs {l.max:0.0000}");
        Result = report.ToString();
        Debug.Log(Result);
        Destroy(gameObject);
    }

    IEnumerator Sample(MovingFrame frame, Dictionary<MovementFrameMember, Track> tracks)
    {
        float end = Time.time + seconds;
        while (Time.time < end)
        {
            yield return new WaitForEndOfFrame();
            Transform island = frame.transform;
            foreach (var member in frame.Members)
            {
                if (!member || member.ExternalMotion) continue;
                Vector3 local = island.InverseTransformPoint(member.transform.position);
                if (!tracks.TryGetValue(member, out var t)) { tracks[member] = t = new Track { name = member.name }; }
                if (t.n >= 2)
                {
                    float jerk = (local - 2 * t.b + t.a).magnitude;
                    t.sum += jerk * jerk; t.max = Mathf.Max(t.max, jerk);
                }
                t.a = t.b; t.b = local; t.n++;
            }
        }
    }

    static void Append(StringBuilder report, string label, Dictionary<MovementFrameMember, Track> tracks)
    {
        double rmsSum = 0; float worst = 0; string worstName = "-"; int count = 0;
        foreach (var t in tracks.Values)
        {
            if (t.n < 3) continue;
            float rms = Mathf.Sqrt((float)(t.sum / (t.n - 2)));
            rmsSum += rms; count++;
            if (t.max > worst) { worst = t.max; worstName = t.name; }
        }
        report.AppendLine($"{label}: {count} members, mean RMS {(count > 0 ? rmsSum / count : 0):0.00000} m, worst max {worst:0.0000} m ({worstName})");
    }
}
