using UnityEngine;

/// <summary>A rigid pose change with explicit endpoints, usable at physics or presentation time.</summary>
public readonly struct FramePoseDelta
{
    readonly Vector3 from, to;
    readonly Quaternion rotation;
    public FramePoseDelta(Pose previous, Pose current)
    { from=previous.position;to=current.position;rotation=current.rotation*Quaternion.Inverse(previous.rotation); }
    public Vector3 Point(Vector3 point)=>to+rotation*(point-from);
    public Vector3 Direction(Vector3 direction)=>rotation*direction;
}
