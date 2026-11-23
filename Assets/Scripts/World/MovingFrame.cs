using System.Collections.Generic;
using UnityEngine;

/// <summary>Plans kinematic motion before passenger motors. Frame motion is committed once per physics step.</summary>
[DefaultExecutionOrder(-900)]
[RequireComponent(typeof(Rigidbody), typeof(FrameVelocityCommit))]
public sealed class MovingFrame : MonoBehaviour, IRideSurface
{
    public IslandFlightPath route;
    [Min(0)] public float cruiseSpeed = 3;
    [Min(.01f)] public float acceleration = .6f;
    public bool paused;
    public Vector3 habitatCenter = new(0, 11, 0);
    public Vector3 habitatSize = new(72, 38, 96);
    [Min(0)] public float exitMargin = 2;
    public Collider deck;
    public Collider[] terrainSurfaces = new Collider[0];
    public static readonly List<MovingFrame> Active = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => Active.Clear();
    public readonly HashSet<MovementFrameMember> Members = new();
    Rigidbody body;
    Vector3 fromPosition, toPosition;
    Quaternion fromRotation, toRotation;
    float distance, speed, duration;
    public float Travelled => distance;
    public float Speed => speed;
    public Vector3 PlannedPosition => toPosition;
    public Quaternion PlannedRotation => toRotation;
    public Pose PreviousPhysicsPose => new(fromPosition,fromRotation);
    public Pose PlannedPhysicsPose => new(toPosition,toRotation);
    public bool Supports(Collider candidate) => candidate == deck || System.Array.IndexOf(terrainSurfaces, candidate) >= 0;
    public Vector3 VelocityAt(Vector3 point)
    {
        if (duration <= 0) return Vector3.zero;
        return (toPosition + toRotation * (Quaternion.Inverse(fromRotation) * (point - fromPosition)) - point) / duration;
    }
    public bool Contains(Vector3 point, bool retaining = false)
    {
        var bounds = new Bounds(habitatCenter, habitatSize + Vector3.one * (retaining ? exitMargin * 2 : 0));
        return bounds.Contains(transform.InverseTransformPoint(point));
    }
    public Vector3 TransformStep(Vector3 point) => toPosition + toRotation * (Quaternion.Inverse(fromRotation) * (point - fromPosition));
    public Quaternion RotationStep => toRotation * Quaternion.Inverse(fromRotation);
    void Awake()
    {
        body = GetComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        if (route && route.Valid)
        {
            route.Rebuild();
            fromPosition = body.position; fromRotation = body.rotation;
            toPosition = route.Position(0); toRotation = route.Heading(0);
            foreach (var member in FindObjectsByType<MovementFrameMember>(FindObjectsSortMode.None))
                if (member.gameObject.scene == gameObject.scene && Contains(member.transform.position)) member.TeleportStep(this);
            body.position = toPosition; body.rotation = toRotation;
            transform.SetPositionAndRotation(toPosition, toRotation);
        }
        ResetMotion();
    }
    void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    void OnDisable()
    {
        Active.Remove(this);
        foreach (var member in new List<MovementFrameMember>(Members)) if (member) member.Leave();
        Members.Clear(); duration = 0;
    }
    public void ResetMotion()
    {
        fromPosition = toPosition = transform.position;
        fromRotation = toRotation = transform.rotation;
        duration = 0;
    }
    [ContextMenu("Reset Flight To First Waypoint")]
    public void ResetFlight()
    {
        if (!Application.isPlaying || !route || !route.Valid) return;
        route.Rebuild();
        fromPosition = body.position; fromRotation = body.rotation;
        toPosition = route.Position(0); toRotation = route.Heading(0);
        foreach (var member in Members) if (member && !member.ExternalMotion) member.TeleportStep(this);
        body.position = toPosition; body.rotation = toRotation;
        transform.SetPositionAndRotation(toPosition, toRotation);
        distance = speed = 0; ResetMotion();
    }
    void FixedUpdate()
    {
        foreach (var member in Members) if (member) member.BeginStep();
        fromPosition = body.position; fromRotation = body.rotation;
        toPosition = fromPosition; toRotation = fromRotation; duration = Time.fixedDeltaTime;
        if (route && route.Valid && route.Length > .01f)
        {
            speed = Mathf.MoveTowards(speed, paused ? 0 : cruiseSpeed, acceleration * duration);
            distance += speed * duration;
            toPosition = route.Position(distance); toRotation = route.Heading(distance);
        }
        foreach (var member in Members) if (member) member.TransportKinematic();
        body.MovePosition(toPosition); body.MoveRotation(toRotation);
    }
    public void CommitVelocities()
    {
        foreach (var member in Members) if (member) member.CommitStep();
    }
    void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix; Gizmos.color = new Color(.2f, 1, .6f, .5f);
        Gizmos.DrawWireCube(habitatCenter, habitatSize);
    }
}
