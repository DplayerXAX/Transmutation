using UnityEngine;

public sealed class SeaAngelCreature : Creature
{
    [SerializeField] bool materialized;
    [SerializeField] float bodyLength = .7f;
    [SerializeField] int appearanceSeed = 1;
    [SerializeField] Transform visualRoot, leftFin, rightFin;
    [SerializeField] float swimSpeed = .5f, approachSpeed = .85f, turningSpeed = 110f;
    public float BodyLength => bodyLength;
    public float RequiredPull => Mathf.Lerp(12, 28, Mathf.InverseLerp(.4f, 1f, bodyLength));
    public Vector3 MouthPosition => transform.position + transform.forward * bodyLength * .53f;
    public float SwimSpeed => currentSpeed;
    public bool Materialized => materialized;
    public FishingPoleCreature ReservedBy { get; private set; }
    public bool CaughtRetrieval { get; private set; }
    public override bool IsInPhysicalWorld => materialized;
    public override bool CanBeCarried => materialized;
    public override bool CanBeCaptured => materialized;
    public override bool CanBeConsumed => materialized;
    Vector3 home;
    float clock, freeAt, flapPhase;
    float currentSpeed;
    float struggleStroke;
    Material[] ownedMaterials;
    public void Configure(float length, int seed) { bodyLength = length; appearanceSeed = seed; }
    protected override void InitializeCreature()
    {
        home = transform.position; clock = appearanceSeed * .13f;
        SetMovementPhysics(!materialized, false);
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = materialized;
        if (visualRoot == null) visualRoot = transform.Find("Visual");
        leftFin = visualRoot.Find("Left Wing"); rightFin = visualRoot.Find("Right Wing");
        visualRoot.localScale = Vector3.one * bodyLength;
        // The authored mesh has its head along +Y. Swimming uses root +Z as forward.
        visualRoot.localRotation = Quaternion.Euler(90, 0, 0);
        if (TryGetComponent<CapsuleCollider>(out var capsule)) { capsule.direction = 2; capsule.height = bodyLength * 1.2f; capsule.radius = bodyLength * .16f; }
        var rng = new System.Random(appearanceSeed);
        float hue = (float)rng.NextDouble();
        Color outer = Color.HSVToRGB(hue, .65f, 1); outer.a = .32f;
        Color inner = Color.HSVToRGB(Mathf.Repeat(hue + .25f + (float)rng.NextDouble() * .4f, 1), .85f, 1); inner.a = .78f;
        var renderers = visualRoot.GetComponentsInChildren<Renderer>(); ownedMaterials = new Material[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            var m = renderers[i].material; ownedMaterials[i] = m;
            Color color = renderers[i].name.Contains("Core") ? inner : outer;
            m.SetColor("_BaseColor", color); m.SetColor("_EmissionColor", color * .35f);
        }
    }
    public bool TryReserve(FishingPoleCreature pole)
    {
        if (materialized || IsDead || !SimulationEnabled || ReservedBy != null || Time.time < freeAt) return false;
        ReservedBy = pole; return true;
    }
    public void ReleaseReservation(FishingPoleCreature pole)
    {
        if (ReservedBy != pole) return;
        ReservedBy = null; struggleStroke = 0; home = transform.position; freeAt = Time.time + 1;
    }
    /// <summary>Turn before accelerating; the fish's own movement always follows its nose.</summary>
    public void SwimToward(Vector3 point, float dt, float speed, float stopDistance = .05f, float acceleration = 1.8f, float turnRate = 0)
    {
        if (dt <= 0) return;
        Vector3 delta = point - transform.position;
        float distance = delta.magnitude;
        if (distance > .001f) TurnToward(delta / distance, dt, turnRate > 0 ? turnRate : turningSpeed);
        float alignment = distance > .001f ? Vector3.Dot(transform.forward, delta / distance) : 0;
        float desiredSpeed = speed * Mathf.InverseLerp(.25f, .9f, alignment) * Mathf.Clamp01((distance - stopDistance) / .4f);
        currentSpeed = Mathf.MoveTowards(currentSpeed, desiredSpeed, dt * acceleration);
        float step = Mathf.Min(currentSpeed * dt, Mathf.Max(0, distance - stopDistance));
        if (!materialized) transform.position += transform.forward * step;
        else SetCreatureVelocity(transform.forward * (dt > 0 ? step / dt : 0));
    }
    public void SetStruggleAnticipation(Vector3 direction, float amount, float dt)
    {
        struggleStroke = amount;
        if (amount > 0 && direction.sqrMagnitude > .001f)
            TurnToward((transform.forward + direction * .5f).normalized, dt, 110);
    }
    void TurnToward(Vector3 direction, float dt, float degreesPerSecond)
    {
        Vector3 up = Vector3.ProjectOnPlane(Vector3.up, direction).normalized;
        if (up.sqrMagnitude < .01f) up = Vector3.ProjectOnPlane(transform.up, direction).normalized;
        float bank = -Vector3.Dot(Vector3.Cross(transform.forward, direction), Vector3.up) * 28f;
        Quaternion target = Quaternion.LookRotation(direction, up) * Quaternion.Euler(0, 0, bank);
        FaceCreature(Quaternion.RotateTowards(transform.rotation, target, degreesPerSecond * dt));
    }
    public void NoticeBait(Vector3 bait, float dt) { SwimToward(bait, dt, 0, bodyLength * .53f); }
    public bool ApproachBait(Vector3 bait, float dt)
    {
        SwimToward(bait, dt, approachSpeed, bodyLength * .53f);
        Vector3 toBait = bait - transform.position;
        return toBait.magnitude <= bodyLength * .53f + .12f &&
            (toBait.sqrMagnitude < .0001f || Vector3.Dot(transform.forward, toBait.normalized) > .9f);
    }
    public bool BeginCaughtRetrieval(FishingPoleCreature pole)
    {
        if (ReservedBy != pole || IsDead || materialized) return false;
        CaughtRetrieval = true; materialized = true;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = true;
        Die();
        if (!IsDead)
        {
            CaughtRetrieval = materialized = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            return false;
        }
        currentSpeed = 0; struggleStroke = 0;
        var body = GetComponent<Rigidbody>();
        body.linearVelocity = body.angularVelocity = Vector3.zero;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        if (leftFin != null) leftFin.localRotation = Quaternion.identity;
        if (rightFin != null) rightFin.localRotation = Quaternion.identity;
        return true;
    }
    public void EndCaughtRetrieval() { CaughtRetrieval = false; }
    protected override void TickCreature(float dt)
    {
        clock += dt;
        flapPhase += dt * (2.2f + Mathf.Min(currentSpeed, 3) + struggleStroke * 4);
        float flap = (24 + struggleStroke * 12) * Mathf.Sin(flapPhase);
        if (leftFin != null) leftFin.localRotation = Quaternion.Euler(0, flap, 0);
        if (rightFin != null) rightFin.localRotation = Quaternion.Euler(0, -flap, 0);
        if (ReservedBy != null || IsCarried || IsBubbleCaptured) return;
        Vector3 goal = home + new Vector3(Mathf.Sin(clock * .17f) * 2.2f, Mathf.Sin(clock * .23f + 1) * .8f, Mathf.Cos(clock * .17f) * 2.2f);
        if (!materialized) SwimToward(goal, dt, swimSpeed);
        else if (Time.time > freeAt)
        {
            // Do not seize movement from the shared scripted throw flight.
            var rb = GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic && rb.linearVelocity.magnitude < 2f)
                SwimToward(goal, dt, swimSpeed);
            else home = transform.position;
        }
    }
    protected override void OnCarryStarted() { if (ReservedBy != null) ReservedBy.CancelFishing(); home = transform.position; }
    protected override void OnCaptureStarted() { if (ReservedBy != null) ReservedBy.CancelFishing(); base.OnCaptureStarted(); }
    public override void PrepareForThrow(PlayerCreatureCarrier carrier) { freeAt = Time.time + 3; home = transform.position; }
    protected override void OnDisable() { if (ReservedBy != null) ReservedBy.CancelFishing(); base.OnDisable(); }
    protected override void OnDeath() { if (!CaughtRetrieval && ReservedBy != null) ReservedBy.CancelFishing(); base.OnDeath(); }
    void OnDestroy() { if (ownedMaterials != null) foreach (var m in ownedMaterials) if (m != null) Destroy(m); }
}

