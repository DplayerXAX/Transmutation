using UnityEngine;

/// <summary>Transport is independent of whether creature AI is awake. Carry/capture/throw retain authority.</summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class MovementFrameMember : MonoBehaviour
{
    public MovingFrame Frame { get; private set; }
    public bool allowAutomaticEntry = true;
    Rigidbody body;
    Creature creature;
    SmoothFirstPersonController player;
    Vector3 appliedVelocity;
    float noEntryUntil;
    bool ownsInterpolation;
    RigidbodyInterpolation previousInterpolation;
    Vector3 renderedFramePosition;
    Quaternion renderedFrameRotation;
    bool RenderDrivenFlight => creature is SeaAngelCreature angel && !angel.Materialized;
    bool RenderDrivenMotion => RenderDrivenFlight || (creature is TentacleCreature tentacle && !tentacle.IsDead && body && body.isKinematic);
    public Vector3 LastTransport { get; private set; }
    /// <summary>Already-applied transport, for locomotion writers running in Update after physics.</summary>
    public Vector3 AppliedVelocity => appliedVelocity;
    public bool ExternalMotion => creature && (creature.IsCarried || creature.IsBubbleCaptured ||
        CreatureThrower.IsFlying(creature) || creature is Boomerang boomerang && boomerang.InFlight ||
        creature is ProjectileCreature projectile && projectile.FlightState == ProjectileFlightState.Flying ||
        creature is SkateGecko gecko && gecko.Ridden ||
        creature is TentacleCreature tentacle && (tentacle.ExternalMovement || tentacle.Suspended) ||
        (creature.TryGetComponent<WebCapture>(out var web) && web.IsCaptured));
    void Awake() { body = GetComponent<Rigidbody>(); creature = GetComponent<Creature>(); player = GetComponent<SmoothFirstPersonController>(); }
    public void Join(MovingFrame frame)
    {
        if (Frame == frame) return;
        Leave(); Frame = frame;
        if (Frame) Frame.Members.Add(this);
        if (Frame)
        {
            renderedFramePosition=Frame.transform.position; renderedFrameRotation=Frame.transform.rotation;
        }
        if (Frame && body && body.isKinematic)
        {
            previousInterpolation = body.interpolation;
            body.interpolation = RenderDrivenMotion ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            ownsInterpolation = true;
        }
        // First subtract the existing frame velocity before the motor runs: entering changes
        // coordinates without adding an instantaneous world-velocity impulse.
        if (Frame && body && !body.isKinematic && !player) appliedVelocity = Frame.VelocityAt(body.position);
        if (creature is SeaAngelCreature fish && fish.isActiveAndEnabled) DimensionalSchool.Existing(fish)?.Reindex(fish);
    }
    public void Leave()
    {
        bool changed = Frame != null;
        if (ownsInterpolation && body) body.interpolation = previousInterpolation;
        ownsInterpolation = false;
        if (Frame) Frame.Members.Remove(this);
        Frame = null; appliedVelocity = Vector3.zero;
        if (changed && creature is SeaAngelCreature fish && fish.isActiveAndEnabled) DimensionalSchool.Existing(fish)?.Reindex(fish);
    }
    public void ReleaseForThrow() { Leave(); noEntryUntil = Time.time + .5f; }
    public void TeleportStep(MovingFrame frame)
    {
        if (!body) body = GetComponent<Rigidbody>();
        Vector3 point = frame.TransformStep(transform.position);
        Quaternion rotation = frame.RotationStep * transform.rotation;
        if (creature) creature.TransportFrameCaches(frame);
        if (TryGetComponent<BoneChild>(out var bone)) bone.TransportFrameCaches(frame);
        transform.SetPositionAndRotation(point, rotation);
        if (body)
        {
            body.position = point; body.rotation = rotation;
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        }
        appliedVelocity = Vector3.zero;
        renderedFramePosition=frame.PlannedPosition; renderedFrameRotation=frame.PlannedRotation;
    }
    void Update()
    {
        if (!Frame || ExternalMotion) return;
        if (!RenderDrivenMotion) return;
        // Intangible flight has no physical collider. Its autonomous pose is authored in
        // Update, so transport it in that same rendered frame, even while its AI sleeps.
        Vector3 position=Frame.transform.position; Quaternion rotation=Frame.transform.rotation;
        Quaternion delta=rotation*Quaternion.Inverse(renderedFrameRotation);
        var motion=new FramePoseDelta(new Pose(renderedFramePosition,renderedFrameRotation),new Pose(position,rotation));
        if (creature is SeaAngelCreature fish) fish.TransportFlightHome(renderedFramePosition,renderedFrameRotation,position,rotation);
        if (creature is TentacleCreature tentacle) tentacle.TransportRenderedCaches(Frame,motion);
        transform.SetPositionAndRotation(position+delta*(transform.position-renderedFramePosition),delta*transform.rotation);
        if (body && body.isKinematic) { body.position=transform.position; body.rotation=transform.rotation; }
        renderedFramePosition=position; renderedFrameRotation=rotation;
    }
    void OnDisable() => Leave();
    void FixedUpdate()
    {
        if (ExternalMotion) { Leave(); return; }
        if (Frame && Frame.Contains(transform.position, true)) return;
        Leave();
        if (!allowAutomaticEntry || Time.time < noEntryUntil) return;
        foreach (var frame in MovingFrame.Active)
            if (frame.gameObject.scene == gameObject.scene && frame.Contains(transform.position)) { Join(frame); break; }
    }
    public void BeginStep()
    {
        if (ExternalMotion) return;
        if (body && !body.isKinematic && !player) body.linearVelocity -= appliedVelocity;
        appliedVelocity = Vector3.zero;
        if (body && !body.isKinematic && !player && creature && !creature.SimulationEnabled && Frame &&
            Physics.Raycast(body.worldCenterOfMass, Vector3.down, out var hit, 3f, 1 << 7, QueryTriggerInteraction.Ignore) && Frame.Supports(hit.collider))
        {
            Vector3 velocity = body.linearVelocity;
            Vector3 planar = Vector3.MoveTowards(new Vector3(velocity.x,0,velocity.z), Vector3.zero, 20 * Time.fixedDeltaTime);
            body.linearVelocity = new Vector3(planar.x, velocity.y, planar.z);
        }
    }
    public void TransportKinematic()
    {
        LastTransport = Vector3.zero;
        if (!Frame || ExternalMotion || player || RenderDrivenMotion || (body && !body.isKinematic)) return;
        // Rigidbody poses belong to simulation time. Transform may be the previous,
        // interpolated render pose and must not feed the next physics target.
        Vector3 before = body ? body.position : transform.position, after = Frame.TransformStep(before);
        Quaternion rotation = Frame.RotationStep * (body ? body.rotation : transform.rotation);
        LastTransport = after - before;
        if (creature) creature.TransportFrameCaches(Frame);
        if (TryGetComponent<BoneChild>(out var bone)) bone.TransportFrameCaches(Frame);
        if (body) { body.MovePosition(after); body.MoveRotation(rotation); }
        else transform.SetPositionAndRotation(after, rotation);
    }
    public void CommitStep()
    {
        if (!Frame || ExternalMotion || player || !body || body.isKinematic) return;
        // Scripted retrieval already follows a moving world target.
        if (creature is SeaAngelCreature fish && fish.CaughtRetrieval) return;
        appliedVelocity = Frame.VelocityAt(body.position);
        body.linearVelocity += appliedVelocity;
    }
    public Vector3 PointVelocity => Frame ? Frame.VelocityAt(transform.position) : Vector3.zero;
}
