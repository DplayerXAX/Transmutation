using UnityEngine;

/// <summary>A catchable fleeing animal, and a separate physical board while airborne.</summary>
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
[DefaultExecutionOrder(1400)]
public sealed class SkateGecko : Creature
{
    public const float RideHeight = .4f;
    [SerializeField] float sightRange = 18f;
    [SerializeField] float fleeSpeed = 10f;
    [SerializeField] float fleeAcceleration = 13f;
    [SerializeField] float catchDistance = 2.2f;
    [SerializeField] float airborneTurnAngle = 50f;
    [SerializeField] LayerMask groundMask = 1 << 7;
    [Header("Wild gecko terrain edges")]
    [SerializeField, Min(1)] float edgeBrakeAcceleration = 32f;
    [SerializeField, Min(.5f)] float edgeMargin = 2f;
    Rigidbody body;
    SmoothFirstPersonController observer, rider;
    PlayerSkateShoes drive;
    Collider[] riderColliders;
    Collider shape;
    readonly RaycastHit[] obstacles = new RaycastHit[24];
    PhysicsMaterial rollingMaterial;
    bool captured, airborne;
    float panicUntil, collisionRestoreAt;
    Vector3 fleeDirection, launchDirection;
    Vector3 lastSafeGround;
    bool hasSafeGround;
    public int EdgeRecoveries { get; private set; }
    public bool Captured => captured;
    public bool Ridden => rider != null;
    public bool Airborne => airborne;
    public override bool CanBeCarried => false;
    public override string InteractionPrompt => captured ? "[LMB] Stand on gecko" : "[LMB] Catch gecko (get close)";

    protected override void InitializeCreature()
    {
        SetCreatureName("Giant Skate Gecko");
        body = GetComponent<Rigidbody>(); shape = GetComponent<Collider>();
        body.mass = 8f; body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.linearDamping = 0;
        rollingMaterial = new PhysicsMaterial("Gecko gliding belly") { dynamicFriction = .015f, staticFriction = .015f, frictionCombine = PhysicsMaterialCombine.Minimum };
        shape.sharedMaterial = rollingMaterial;
        var capsule = GetComponent<CapsuleCollider>(); capsule.direction = 2; capsule.height = 2.8f; capsule.radius = .24f;
        if (transform.Find("Gecko model") == null) SkateGeckoVisuals.Build(transform);
    }
    protected override void TickCreature(float dt)
    {
        if (observer == null || observer.gameObject.scene != gameObject.scene)
            foreach (var player in FindObjectsByType<SmoothFirstPersonController>(FindObjectsSortMode.None))
                if (player.gameObject.scene == gameObject.scene) { observer = player; break; }
    }
    bool Ground(out RaycastHit hit)
    {
        hit = default;
        int count = gameObject.scene.GetPhysicsScene().Raycast(body.position + Vector3.up * .25f,
            Vector3.down, obstacles, .65f, groundMask, QueryTriggerInteraction.Ignore);
        if (count == obstacles.Length) return false;
        float closest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var candidate = obstacles[i];
            if (candidate.distance >= closest || candidate.normal.y < .65f ||
                candidate.collider.attachedRigidbody == body || Physics.GetIgnoreCollision(shape, candidate.collider)) continue;
            closest = candidate.distance; hit = candidate;
        }
        return hit.collider != null;
    }

    float EdgeLookahead(float speed) => speed * speed / (2f * Mathf.Max(1, edgeBrakeAcceleration)) + speed * .25f + edgeMargin + 1.4f;

    // Sample the whole braking corridor, not just one point beyond a gap. The two sides
    // keep the body inside corners as well as protecting its centre at a straight edge.
    bool SupportedCorridor(Vector3 direction, float distance)
    {
        if (direction.sqrMagnitude < .01f) return true;
        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, direction) * .4f;
        var physics = gameObject.scene.GetPhysicsScene();
        float height = body.position.y;
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / 1.5f));
        for (int i = 1; i <= steps; i++)
        {
            Vector3 point = body.position + direction * (distance * i / steps);
            point.y = height + 1.5f;
            for (int lane = -1; lane <= 1; lane++)
            {
                if (!physics.Raycast(point + side * lane, Vector3.down, out var floor, 3f, groundMask, QueryTriggerInteraction.Ignore) || floor.normal.y < .65f) return false;
                if (lane == 0) height = floor.point.y;
            }
        }
        return true;
    }

    void RememberSafeGround(RaycastHit ground)
    {
        if (SupportedCorridor(Vector3.forward, edgeMargin) && SupportedCorridor(Vector3.back, edgeMargin) &&
            SupportedCorridor(Vector3.left, edgeMargin) && SupportedCorridor(Vector3.right, edgeMargin))
        { lastSafeGround = ground.point; hasSafeGround = true; }
    }

    public bool Sees(SmoothFirstPersonController player)
    {
        if (player == null || player.gameObject.scene != gameObject.scene) return false;
        Vector3 start = body.position + Vector3.up * .35f, delta = player.transform.position - start;
        if (delta.magnitude > sightRange) return false;
        return !gameObject.scene.GetPhysicsScene().Raycast(start, delta.normalized, out var hit, delta.magnitude, ~0, QueryTriggerInteraction.Ignore)
            || hit.collider.GetComponentInParent<SmoothFirstPersonController>() == player;
    }
    void FixedUpdate()
    {
        if (!SimulationEnabled || IsDead || IsCarried || IsBubbleCaptured) { if (Ridden) Dismount(); return; }
        if (Ridden)
        {
            if (drive == null || !drive.CanDrive) { Dismount(); return; }
            if (!airborne) FollowRider();
            return;
        }
        RestoreCollisionsWhenClear();
        if (!Ground(out var ground))
        {
            // Only recover a wild animal when there is no terrain anywhere below it.
            // Captured, ridden and detached boards retain their normal airborne physics.
            if (!captured && hasSafeGround && !gameObject.scene.GetPhysicsScene().Raycast(body.position + Vector3.up * 10,
                Vector3.down, out _, 1000, groundMask, QueryTriggerInteraction.Ignore))
            {
                body.position = lastSafeGround + Vector3.up * .26f; body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero; fleeDirection = Vector3.zero; EdgeRecoveries++;
            }
            return;
        }
        if (!captured) RememberSafeGround(ground);
        Vector3 velocity = Vector3.ProjectOnPlane(body.linearVelocity, ground.normal);
        if (!captured && Sees(observer))
        {
            fleeDirection = Vector3.ProjectOnPlane(body.position - observer.transform.position, ground.normal).normalized;
            panicUntil = Time.time + 2f;
        }
        if (!captured && Time.time < panicUntil)
        {
            Vector3 direction = fleeDirection;
            // Turn away from trees and cliffs instead of running directly into them.
            bool Clear(Vector3 d)
            {
                if (d.sqrMagnitude < .01f) return false;
                var physics = gameObject.scene.GetPhysicsScene();
                int count = physics.SphereCast(body.position + Vector3.up * .4f, .25f, d, obstacles, 2f, ~0, QueryTriggerInteraction.Ignore);
                if (count == obstacles.Length) return false;
                for (int i = 0; i < count; i++)
                    if (obstacles[i].collider.attachedRigidbody != body && obstacles[i].normal.y < .65f) return false;
                return SupportedCorridor(d, EdgeLookahead(Mathf.Max(velocity.magnitude, fleeSpeed)));
            }
            if (!Clear(direction))
            {
                bool found = false;
                foreach (float angle in new[] { 45f, -45f, 90f, -90f, 135f, -135f, 180f })
                { var candidate = Quaternion.AngleAxis(angle, Vector3.up) * direction; if (Clear(candidate)) { direction = candidate; found = true; break; } }
                if (!found) direction = Vector3.zero;
            }
            fleeDirection = direction;
            velocity = Vector3.MoveTowards(velocity, direction * fleeSpeed, fleeAcceleration * Time.fixedDeltaTime);
        }
        else velocity = Vector3.MoveTowards(velocity, Vector3.zero, .55f * Time.fixedDeltaTime);
        // A safe desired heading is insufficient while inertia still points off the edge.
        if (!captured && !SupportedCorridor(velocity, EdgeLookahead(velocity.magnitude)))
            velocity = Vector3.MoveTowards(Vector3.ProjectOnPlane(body.linearVelocity, ground.normal), Vector3.zero,
                edgeBrakeAcceleration * Time.fixedDeltaTime);
        // Keep the normal component once. Adding world-down velocity to an inclined
        // tangent counted the same falling velocity twice on rounded silk/steep ground.
        body.linearVelocity = velocity + ground.normal * Mathf.Min(0, Vector3.Dot(body.linearVelocity, ground.normal));
        if (velocity.sqrMagnitude > .3f) body.MoveRotation(Quaternion.LookRotation(velocity.normalized, Vector3.up));
    }
    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        var player = interaction.Source != null ? interaction.Source.GetComponentInParent<SmoothFirstPersonController>() : null;
        // The camera/interactor can be a sibling of the player's physics body.
        if (player == null && interaction.Source != null)
            player = interaction.Source.GetComponentInParent<PlayerCreatureInteractor>()?.PlayerController;
        if (TryCatch(player)) LandmarkToast.Show("Gecko caught! Click again to ride. WASD steer, reverse input brakes, K steps off. Sharp turns in the air separate you.");
        else if (captured) TryMount(player);
    }
    public bool TryCatch(SmoothFirstPersonController player)
    {
        var web = GetComponent<WebCapture>();
        if (captured || !Available(player, web != null && web.HoldsActiveCreature) || !Sees(player)) return false;
        if (web != null && web.IsCaptured)
        {
            web.ReleaseForRescue();
        }
        captured = true; panicUntil = 0;
        body.linearVelocity = Vector3.zero;
        return true;
    }
    bool Available(SmoothFirstPersonController player, bool heldByWeb = false) => !IsDead && (SimulationEnabled || heldByWeb) && !IsBubbleCaptured && !IsCarried &&
        player != null && player.isActiveAndEnabled && player.gameObject.scene == gameObject.scene && !player.Restricted && !player.BubbleMovementBlocked &&
        !player.Climbing && !player.WallRunning && !BoneRide.Busy && !BoneAwakeningCutscene.Playing &&
        Vector3.Distance(shape.ClosestPoint(player.transform.position), player.transform.position) <= catchDistance;
    public bool TryMount(SmoothFirstPersonController player)
    {
        if (!captured || Ridden || !Available(player) || !Sees(player) || !player.Grounded || !Ground(out _)) return false;
        rider = player;
        drive = player.GetComponent<PlayerSkateShoes>(); if (drive == null) drive = player.gameObject.AddComponent<PlayerSkateShoes>();
        riderColliders = player.GetComponentsInChildren<Collider>();
        foreach (var c in riderColliders) if (c != null) Physics.IgnoreCollision(shape, c, true);
        airborne = false; body.linearVelocity = Vector3.zero; body.isKinematic = true;
        player.Body.position += Vector3.up * RideHeight;
        drive.MountGecko(this); FollowRider();
        return true;
    }
    void FollowRider()
    {
        // The rider remains the movement controller's single physics owner on the ground.
        float bottom = rider.GetComponent<Collider>().bounds.min.y;
        body.position = new Vector3(rider.transform.position.x, bottom - .14f, rider.transform.position.z);
        Vector3 facing = Vector3.ProjectOnPlane(rider.Body.linearVelocity, Vector3.up);
        if (facing.sqrMagnitude < .1f) facing = rider.Orientation != null ? rider.Orientation.forward : rider.transform.forward;
        body.rotation = Quaternion.LookRotation(facing, Vector3.up);
    }
    public bool UpdateRide(Vector3 input, bool supported, float dt)
    {
        if (!Ridden) return false;
        if (!supported && !airborne)
        {
            FollowRider(); airborne = true; body.isKinematic = false;
            body.linearVelocity = rider.Body.linearVelocity;
            Vector3 momentum = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            // Tiny contact corrections at mount time have no meaningful travel direction.
            // Normalizing them could turn a forward push into a false sharp airborne turn.
            launchDirection = input.sqrMagnitude > .1f ? input.normalized : momentum.sqrMagnitude > .1f
                ? momentum.normalized : (rider.Orientation != null ? rider.Orientation.forward : rider.transform.forward);
        }
        if (airborne)
        {
            if (supported)
            {
                if (Vector3.ProjectOnPlane(body.position - rider.Body.position, Vector3.up).magnitude > .9f)
                { Dismount(); return false; }
                airborne = false; body.linearVelocity = Vector3.zero; body.isKinematic = true; FollowRider();
                return true;
            }
            if (Vector3.ProjectOnPlane(body.position - rider.Body.position, Vector3.up).magnitude > 1.2f)
            { Dismount(); return false; }
            if (input.sqrMagnitude > .1f && launchDirection.sqrMagnitude > .1f && Vector3.Angle(input, launchDirection) >= airborneTurnAngle)
            { Dismount(); return false; }
        }
        return true;
    }
    public void Dismount()
    {
        if (rider == null) return;
        var momentum = airborne ? body.linearVelocity : rider.Body.linearVelocity;
        body.isKinematic = false; body.linearVelocity = momentum;
        if (airborne) rider.Fling(rider.Body.linearVelocity, .5f);
        rider = null; airborne = false; collisionRestoreAt = Time.time + .3f;
        var old = drive; drive = null; if (old != null) old.ReleaseGecko();
    }
    void RestoreCollisionsWhenClear()
    {
        if (riderColliders == null || Time.time < collisionRestoreAt) return;
        foreach (var c in riderColliders)
            if (c != null && Physics.ComputePenetration(shape, shape.transform.position, shape.transform.rotation, c, c.transform.position, c.transform.rotation, out _, out _)) return;
        foreach (var c in riderColliders) if (c != null) Physics.IgnoreCollision(shape, c, false);
        riderColliders = null;
    }
    protected override void OnDisable() { Dismount(); if (riderColliders != null) foreach (var c in riderColliders) if (c != null) Physics.IgnoreCollision(shape, c, false); base.OnDisable(); }
    protected override void OnCaptureStarted() { Dismount(); base.OnCaptureStarted(); }
    public override void Die() { Dismount(); base.Die(); }
    void OnDestroy() { if (rollingMaterial != null) Destroy(rollingMaterial); }
}
