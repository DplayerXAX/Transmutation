using System.Collections.Generic;
using UnityEngine;

/// <summary>Plans kinematic motion before passenger motors. Frame motion is committed once per physics step.</summary>
[DefaultExecutionOrder(-900)]
[RequireComponent(typeof(Rigidbody), typeof(FrameVelocityCommit))]
public sealed class MovingFrame : MonoBehaviour, IRideSurface
{
    public IslandFlightPath route;
    [Min(0)] public float cruiseSpeed = 9;
    [Min(.01f)] public float acceleration = 1.8f;
    public bool paused;
    public Vector3 habitatCenter = new(0, 11, 0);
    public Vector3 habitatSize = new(72, 38, 96);
    [Min(0)] public float exitMargin = 2;
    public Collider deck;
    public Collider[] terrainSurfaces = new Collider[0];
    [Tooltip("Draw the habitat volume (everything inside, flyers included, moves with the frame) in the Scene and Game views. Green: entry box. Yellow: release box (entry plus exit margin).")]
    public bool showHabitat;
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
    /// <summary>
    /// During Update/LateUpdate: where a point that rides on the newest physics pose is drawn this
    /// frame. The frame Rigidbody interpolates, so it is rendered between its last two physics
    /// poses; caches transported with TransformStep are on the newest one and must be mapped here
    /// before they are written to a visible Transform.
    /// </summary>
    public Vector3 RenderedPoint(Vector3 physicsPoint) => transform.position + transform.rotation * (Quaternion.Inverse(toRotation) * (physicsPoint - toPosition));
    public Quaternion RenderedRotation(Quaternion physicsRotation) => transform.rotation * Quaternion.Inverse(toRotation) * physicsRotation;
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
    void OnDrawGizmos() { if (showHabitat) DrawHabitatGizmos(); }
    void OnDrawGizmosSelected() { if (!showHabitat) DrawHabitatGizmos(); }
    void DrawHabitatGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix; Gizmos.color = new Color(.2f, 1, .6f, .5f);
        Gizmos.DrawWireCube(habitatCenter, habitatSize);
        if (exitMargin > 0) { Gizmos.color = new Color(1, .85f, .2f, .35f); Gizmos.DrawWireCube(habitatCenter, habitatSize + Vector3.one * exitMargin * 2); }
    }
    [ContextMenu("Toggle Habitat Display")]
    void ToggleHabitat() => showHabitat = !showHabitat;

    // Game-view display: line renderers parented to the frame, so they follow its rendered pose.
    LineRenderer entryLines, releaseLines;
    Vector3 shownSize; float shownMargin; Vector3 shownCenter;
    void LateUpdate()
    {
        if (!showHabitat)
        {
            if (entryLines) { entryLines.enabled = false; releaseLines.enabled = false; }
            return;
        }
        if (!entryLines)
        {
            var shader = Shader.Find("Sprites/Default");
            if (!shader) return;
            var material = new Material(shader);
            entryLines = CreateLines("Habitat Entry (debug)", material, new Color(.2f, 1, .6f, .8f));
            releaseLines = CreateLines("Habitat Release (debug)", material, new Color(1, .85f, .2f, .6f));
            shownSize = -Vector3.one;
        }
        entryLines.enabled = true; releaseLines.enabled = exitMargin > 0;
        if (shownSize == habitatSize && shownMargin == exitMargin && shownCenter == habitatCenter) return;
        shownSize = habitatSize; shownMargin = exitMargin; shownCenter = habitatCenter;
        TraceBox(entryLines, habitatCenter, habitatSize);
        TraceBox(releaseLines, habitatCenter, habitatSize + Vector3.one * exitMargin * 2);
    }
    LineRenderer CreateLines(string label, Material material, Color color)
    {
        var child = new GameObject(label) { hideFlags = HideFlags.DontSave };
        child.transform.SetParent(transform, false);
        var lines = child.AddComponent<LineRenderer>();
        lines.useWorldSpace = false; lines.loop = false; lines.sharedMaterial = material;
        lines.startColor = lines.endColor = color; lines.widthMultiplier = .15f;
        lines.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lines.receiveShadows = false;
        return lines;
    }
    static void TraceBox(LineRenderer lines, Vector3 center, Vector3 size)
    {
        Vector3 e = size * .5f;
        Vector3 P(float x, float y, float z) => center + Vector3.Scale(e, new Vector3(x, y, z));
        Vector3 b0 = P(-1, -1, -1), b1 = P(1, -1, -1), b2 = P(1, -1, 1), b3 = P(-1, -1, 1);
        Vector3 t0 = P(-1, 1, -1), t1 = P(1, 1, -1), t2 = P(1, 1, 1), t3 = P(-1, 1, 1);
        // One path covering all 12 edges (some retraced).
        var path = new[] { b0, b1, b2, b3, b0, t0, t1, b1, t1, t2, b2, t2, t3, b3, t3, t0 };
        lines.positionCount = path.Length; lines.SetPositions(path);
    }
}
