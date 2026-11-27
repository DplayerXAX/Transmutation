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
    RigidbodyInterpolation previousInterpolation, ownedInterpolation;
    Vector3 renderedFramePosition;
    Quaternion renderedFrameRotation;
    bool RenderDrivenFlight => creature is SeaAngelCreature angel && !angel.Materialized;
    bool RenderDrivenMotion => RenderDrivenFlight || (creature is TentacleCreature tentacle && !tentacle.IsDead && body && body.isKinematic);
    /// <summary>No Rigidbody (rooted flowers and similar): nothing interpolates it, so it must follow the rendered frame.</summary>
    bool TransformOnly => !body && !player;
    /// <summary>Members whose visible pose follows the frame's rendered (interpolated) pose in Update, not physics steps.</summary>
    bool RenderTimeline => RenderDrivenMotion || TransformOnly;
    /// <summary>Interpolation the body had before joining, while this member owns it (diagnostics).</summary>
    public RigidbodyInterpolation? OriginalInterpolation => ownsInterpolation ? previousInterpolation : (RigidbodyInterpolation?)null;
    /// <summary>Interpolation this member set on joining, while it owns it (diagnostics).</summary>
    public RigidbodyInterpolation? OwnedInterpolation => ownsInterpolation ? ownedInterpolation : (RigidbodyInterpolation?)null;
    public Vector3 LastTransport { get; private set; }
    /// <summary>Already-applied transport, for locomotion writers running in Update after physics.</summary>
    public Vector3 AppliedVelocity => appliedVelocity;
    public bool ExternalMotion => IsExternallyMoved(creature);
    /// <summary>Carry, capture, throw and species flights own this creature's motion (shared with wind).</summary>
    public static bool IsExternallyMoved(Creature creature) => creature && (creature.IsCarried || creature.IsBubbleCaptured ||
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
        // Carry can add a Rigidbody to a previously body-less creature; pick it up on rejoin.
        if (!body) body = GetComponent<Rigidbody>();
        if (Frame) Frame.Members.Add(this);
        if (Frame)
        {
            renderedFramePosition=Frame.transform.position; renderedFrameRotation=Frame.transform.rotation;
        }
        SyncInterpolation();
        wasRenderTimeline = RenderTimeline;
        // First subtract the existing frame velocity before the motor runs: entering changes
        // coordinates without adding an instantaneous world-velocity impulse.
        if (Frame && body && !body.isKinematic && !player) appliedVelocity = Frame.VelocityAt(body.position);
        if (creature is SeaAngelCreature fish && fish.isActiveAndEnabled) DimensionalSchool.Existing(fish)?.Reindex(fish);
    }
    public void Leave()
    {
        bool changed = Frame != null;
        // Restore only if nothing else (carry, capture) has changed the setting in the meantime.
        if (ownsInterpolation && body && body.interpolation == ownedInterpolation) body.interpolation = previousInterpolation;
        ownsInterpolation = false;
        if (Frame) Frame.Members.Remove(this);
        Frame = null; appliedVelocity = Vector3.zero;
        if (changed && creature is SeaAngelCreature fish && fish.isActiveAndEnabled) DimensionalSchool.Existing(fish)?.Reindex(fish);
    }
    /// <summary>
    /// The frame renders one physics step behind (Rigidbody interpolation). Every physics passenger
    /// must render on that same delayed timeline, or it is drawn up to one step (speed x
    /// fixedDeltaTime) ahead of the deck and visibly saws back and forth on it. Render-driven
    /// members (intangible flight, live kinematic tentacles) author their pose in Update and need
    /// interpolation off. Members change mode without rejoining (a tentacle dies, a sea angel
    /// materializes), so this is re-evaluated every physics step, not only on Join.
    /// </summary>
    void SyncInterpolation()
    {
        if (!Frame || !body || player) return;
        if (!ownsInterpolation) { previousInterpolation = body.interpolation; ownsInterpolation = true; }
        // Something else (a creature's own state change) chose a setting since: restore that one on leaving.
        else if (body.interpolation != ownedInterpolation) previousInterpolation = body.interpolation;
        ownedInterpolation = RenderDrivenMotion ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
        body.interpolation = ownedInterpolation;
    }
    bool wasRenderTimeline;
    /// <summary>
    /// A render-timeline member's Transform (and kinematic body) holds its drawn pose, which trails
    /// the frame's newest physics pose by up to one step. When it starts simulating (death turns a
    /// live tentacle into a falling corpse), carry that pose onto the physics timeline first;
    /// otherwise the new dynamic body starts embedded in, or hovering behind, the moving deck.
    /// </summary>
    void EnterPhysicsTimeline()
    {
        if (!Frame || !body || transform.IsChildOf(Frame.transform)) return;
        // Called from BeginStep, before the frame plans this step: the last planned pose is where its body is now.
        Pose physics = Frame.PlannedPhysicsPose;
        Quaternion delta = physics.rotation * Quaternion.Inverse(renderedFrameRotation);
        Vector3 point = physics.position + delta * (transform.position - renderedFramePosition);
        Quaternion rotation = delta * transform.rotation;
        transform.SetPositionAndRotation(point, rotation);
        body.position = point; body.rotation = rotation;
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
        if (!Frame) return;
        // Frame.transform is the interpolated pose being rendered this frame.
        Vector3 position=Frame.transform.position; Quaternion rotation=Frame.transform.rotation;
        // A child of the frame already moves with it; transporting it again would double the motion.
        if (!ExternalMotion && RenderTimeline && !transform.IsChildOf(Frame.transform))
        {
            // Intangible flight and body-less inhabitants are not interpolated by physics. Their
            // visible pose is authored in Update, so transport it in that same rendered frame,
            // even while their AI sleeps.
            Quaternion delta=rotation*Quaternion.Inverse(renderedFrameRotation);
            var motion=new FramePoseDelta(new Pose(renderedFramePosition,renderedFrameRotation),new Pose(position,rotation));
            if (creature is SeaAngelCreature fish) fish.TransportFlightHome(renderedFramePosition,renderedFrameRotation,position,rotation);
            if (creature is TentacleCreature tentacle) tentacle.TransportRenderedCaches(Frame,motion);
            transform.SetPositionAndRotation(position+delta*(transform.position-renderedFramePosition),delta*transform.rotation);
            if (body && body.isKinematic) { body.position=transform.position; body.rotation=transform.rotation; }
        }
        // Always track the rendered pose, so a member that switches timeline does not jump.
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
        bool renderTimeline = RenderTimeline;
        if (wasRenderTimeline && !renderTimeline) EnterPhysicsTimeline();
        wasRenderTimeline = renderTimeline;
        SyncInterpolation();
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
        if (TransformOnly)
        {
            // Logic caches stay on the physics timeline; the visible pose moves in Update.
            if (creature) creature.TransportFrameCaches(Frame);
            if (TryGetComponent<BoneChild>(out var cachedBone)) cachedBone.TransportFrameCaches(Frame);
            return;
        }
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
