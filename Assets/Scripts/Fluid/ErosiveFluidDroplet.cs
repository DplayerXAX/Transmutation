using UnityEngine;

/// <summary>
/// A pooled erosive water droplet. As a Creature it can be carried, petted, and heated by the
/// player and other creatures; enough Heat evaporates it. The fluid system owns its lifetime.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class ErosiveFluidDroplet : Creature
{
    // isTarget is explicit: a destroyed ErosionTarget compares equal to null.
    private struct Touch { public Collider collider; public ErosionTarget target; public bool isTarget; public Vector3 point, normal; public float step; }
    private readonly Touch[] touches = new Touch[4];
    private static readonly Collider[] overlaps = new Collider[8];
    private static readonly Collider[] creatureOverlaps = new Collider[16];
    private int touchCount;
    private Vector3 lastPosition;
    private ErosiveFluidSystem fluid;
    private ErosiveFluidSource source;
    private Rigidbody body;
    private SphereCollider sphere;
    private Renderer visual;
    private MaterialPropertyBlock properties;
    private float elapsed, probeTimer;
    private float drain, drainTouched = float.NegativeInfinity;
    private Vector3 drainPoint;
    private bool evaporated, wasCarried, splashed;
    private Touch effectContact;
    public Rigidbody Body => body;
    public SphereCollider Collider => sphere;
    public Vector3 Position => body.position;
    /// <summary>Seconds since launch, not counting time spent carried or paused.</summary>
    public float Elapsed => elapsed;
    public int TerrainContactCount { get { int count = 0; for (int i = 0; i < touchCount; i++) if (!touches[i].isTarget) count++; return count; } }
    /// <summary>Contacts with non-terrain erosion targets (see ErosionTarget).</summary>
    public int TargetContactCount => touchCount - TerrainContactCount;
    public bool Evaporated => evaporated;
    private static readonly int VelocityId = Shader.PropertyToID("_FlowVelocity");
    private static readonly int ContactId = Shader.PropertyToID("_ContactPoint");
    private static readonly int NormalId = Shader.PropertyToID("_ContactNormal");
    private static readonly int StrengthId = Shader.PropertyToID("_ErosionStrength");
    private static readonly int DrainId = Shader.PropertyToID("_LifeDrain");
    private static readonly int DrainPointId = Shader.PropertyToID("_DrainPoint");
    /// <summary>0..1 glow of the creature-contact feedback; rises on touch and fades after.</summary>
    public float LifeDrain => drain;
    public override bool HeldInView => true;
    protected override void InitializeCreature() => SetCreatureName("Water Droplet");
    protected override void TickCreature(float deltaTime) { }
    internal void Initialize(ErosiveFluidSystem system, Material material, PhysicsMaterial friction)
    {
        fluid = system; body = GetComponent<Rigidbody>(); sphere = GetComponent<SphereCollider>();
        visual = GetComponent<Renderer>(); visual.sharedMaterial = material;
        properties = new MaterialPropertyBlock();
        body.mass = 0.05f; body.useGravity = true; body.linearDamping = 0.05f; body.angularDamping = 0.05f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.maxDepenetrationVelocity = 4f;
        sphere.sharedMaterial = friction;
        // Local overrides preserve the project's collision matrix and existing actor layers.
        sphere.excludeLayers = ~system.CollisionLayers; sphere.layerOverridePriority = 100;
        visual.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
    internal void Launch(ErosiveFluidSource emitter, Vector3 position, Vector3 velocity)
    {
        source = emitter; elapsed = probeTimer = drain = 0f; touchCount = 0; effectContact = default;
        drainTouched = float.NegativeInfinity;
        evaporated = wasCarried = splashed = false;
        transform.localScale = Vector3.one * emitter.Radius * 2f;
        body.position = position; transform.position = position; lastPosition = position;
        body.linearVelocity = velocity; body.angularVelocity = Vector3.zero;
        gameObject.SetActive(true); body.WakeUp();
        SetHeat(0f);
        fluid.IgnoreActors(sphere);
        properties.Clear(); visual.SetPropertyBlock(properties);
    }
    internal bool Simulate(float dt)
    {
        if (IsDead || evaporated || splashed || source == null || !fluid.InBounds(body.position)) return false;
        // The carrier restores player collision on release; water must not push the player.
        if (wasCarried && !IsCarried) fluid.IgnoreActors(sphere);
        wasCarried = IsCarried;
        if (!SimulationEnabled) return true;
        if (!IsCarried) elapsed += dt;
        if (elapsed >= source.Lifetime) return false;
        if (Heat > 0f) SpendHeat(source.HeatLossPerSecond * dt);
        // A body held in view is placed directly, so its motion comes from position change.
        bool placed = IsCarried && body.isKinematic;
        Vector3 position = placed ? transform.position : body.position;
        Vector3 velocity = placed ? (position - lastPosition) / dt : body.linearVelocity;
        lastPosition = position;
        // Writing even an unchanged velocity wakes a resting Rigidbody. Clamp only an overspeed
        // body so sleeping droplets can retain their low-cost contact erosion.
        if (!placed && velocity.sqrMagnitude > fluid.MaximumSpeed * fluid.MaximumSpeed)
            body.linearVelocity = velocity = velocity.normalized * fluid.MaximumSpeed;
        probeTimer -= dt;
        bool validate = probeTimer <= 0f;
        if (validate) probeTimer = fluid.SampleInterval;
        if (validate && fluid.AgesCreatures) ProbeCreatures(position);
        if (placed) ProbeHeldContacts(position);
        else for (int i = touchCount - 1; i >= 0; i--)
        {
            Touch touch = touches[i];
            bool valid = touch.collider != null && touch.collider.enabled &&
                (touch.isTarget ? ErosionTarget.TryGet(touch.collider, out _) : fluid.CanErode(touch.collider));
            if (valid && validate)
            {
                // Raycast from the centre avoids sphere-cast overlap ambiguity, including sleeping bodies.
                valid = touch.collider.Raycast(new Ray(body.position, -touch.normal), out RaycastHit hit, source.Radius + 0.06f);
                if (valid) { touch.point = hit.point; touch.normal = hit.normal; touches[i] = touch;
                    if (!touch.isTarget) { effectContact = touch; effectContact.step = Time.fixedTime; } }
            }
            if (!valid || (!body.IsSleeping() && Time.fixedTime - touch.step > dt * 2.1f))
            { touches[i] = touches[--touchCount]; }
        }
        float activity = 0f;
        for (int i = 0; i < touchCount; i++)
        {
            float speed = Vector3.ProjectOnPlane(velocity, touches[i].normal).magnitude;
            float strength = source.Rate * fluid.World.Erodibility * source.MotionMultiplier(speed);
            activity += strength;
            float amount = strength * dt / touchCount;
            if (touches[i].isTarget) touches[i].target.Erode(touches[i].point, amount);
            else fluid.Accumulate(touches[i].point, source.ErosionRadius, amount);
        }
        properties.SetVector(VelocityId, velocity);
        properties.SetFloat(StrengthId, activity);
        properties.SetVector(ContactId, touchCount > 0 ? touches[0].point : position);
        properties.SetVector(NormalId, touchCount > 0 ? touches[0].normal : Vector3.zero);
        // Quick rise on touch, slower fade so a brief brush still reads.
        bool touching = Time.fixedTime - drainTouched <= fluid.SampleInterval * 1.5f;
        drain = Mathf.MoveTowards(drain, touching ? 1f : 0f, dt * (touching ? 8f : 2.5f));
        properties.SetFloat(DrainId, drain);
        properties.SetVector(DrainPointId, drain > 0f ? drainPoint : position);
        visual.SetPropertyBlock(properties);
        return true;
    }
    // A kinematic body gets no contacts from static terrain; overlap with it stands in for touching.
    private void ProbeHeldContacts(Vector3 position)
    {
        touchCount = 0;
        int count = Physics.OverlapSphereNonAlloc(position, source.Radius, overlaps, 1 << fluid.World.TerrainLayer, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count && touchCount < touches.Length; i++)
        {
            Collider terrain = overlaps[i];
            if (!fluid.CanErode(terrain) || !Physics.ComputePenetration(sphere, position, transform.rotation,
                terrain, terrain.transform.position, terrain.transform.rotation, out Vector3 normal, out float depth)) continue;
            touches[touchCount] = new Touch { collider = terrain, point = position - normal * Mathf.Max(0f, source.Radius - depth), normal = normal, step = Time.fixedTime };
            effectContact = touches[touchCount++];
        }
        if (ErosionTarget.Count == 0) return;
        // Erosion targets can be on any layer under a moving Creature; this scene's physics keeps tests local.
        count = gameObject.scene.GetPhysicsScene().OverlapSphere(position, source.Radius, overlaps, ErosionTarget.SurfaceLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count && touchCount < touches.Length; i++)
        {
            Collider surface = overlaps[i];
            if (!ErosionTarget.TryGet(surface, out ErosionTarget target) || HasTouch(surface) ||
                !Physics.ComputePenetration(sphere, position, transform.rotation, surface, surface.transform.position,
                    surface.transform.rotation, out Vector3 normal, out float depth)) continue;
            touches[touchCount++] = new Touch { collider = surface, target = target, isTarget = true,
                point = position - normal * Mathf.Max(0f, source.Radius - depth), normal = normal, step = Time.fixedTime };
        }
    }
    private bool HasTouch(Collider collider)
    {
        for (int i = 0; i < touchCount; i++) if (touches[i].collider == collider) return true;
        return false;
    }
    // Creature bodies ignore droplet collisions, so touching is an overlap with a thin margin.
    private void ProbeCreatures(Vector3 position)
    {
        int mask = Physics.AllLayers & ~(1 << gameObject.layer) & ~(1 << fluid.World.TerrainLayer);
        int count = Physics.OverlapSphereNonAlloc(position, source.Radius + 0.05f, creatureOverlaps, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Creature creature = CreatureSpatialQuery.Owner(creatureOverlaps[i]);
            if (creature == null || creature == this || creature is ErosiveFluidDroplet || creature.IsDead || creature.Invulnerable) continue;
            Collider other = creatureOverlaps[i];
            // ClosestPoint only supports primitives and convex meshes.
            drainPoint = other is MeshCollider mesh && !mesh.convex ? other.bounds.ClosestPoint(position) : other.ClosestPoint(position);
            drainTouched = Time.fixedTime;
            fluid.Soak(creature, drainPoint);
        }
    }
    protected override void OnHeatChanged()
    {
        if (evaporated || source == null || IsDead || Heat < source.EvaporationHeat) return;
        evaporated = true;
        fluid.Evaporate(body.position);
    }
    /// <summary>A thrown droplet bursts on an erosion target instead of landing beside it; the pool then recycles it.</summary>
    internal bool TrySplash(RaycastHit hit, Vector3 velocity)
    {
        if (fluid == null || source == null || IsDead || evaporated || splashed || !ErosionTarget.TryGet(hit.collider, out ErosionTarget target)) return false;
        target.Splash(hit.point, velocity.magnitude);
        splashed = true;
        fluid.Evaporate(hit.point);
        return true;
    }
    internal void Invalidate(Bounds bounds)
    { if (bounds.SqrDistance(body.position) < 1f) { touchCount = 0; probeTimer = 0f; body.WakeUp(); } }
    internal bool TryGetEffectContact(out Vector3 point, out Vector3 normal)
    {
        point = effectContact.point; normal = effectContact.normal;
        return effectContact.collider != null && Time.fixedTime - effectContact.step <= 1f && fluid.CanErode(effectContact.collider);
    }
    internal void Recycle()
    {
        touchCount = 0; source = null;
        // Disabling releases any carrier, web, or bubble before the body is reset.
        gameObject.SetActive(false);
        body.linearVelocity = body.angularVelocity = Vector3.zero;
    }
    private void OnCollisionEnter(Collision collision) => ReadContact(collision);
    private void OnCollisionStay(Collision collision) => ReadContact(collision);
    private void OnCollisionExit(Collision collision)
    { for (int i = touchCount - 1; i >= 0; i--) if (touches[i].collider == collision.collider) touches[i] = touches[--touchCount]; }
    private void ReadContact(Collision collision)
    {
        if (fluid == null || collision.contactCount == 0) return;
        bool terrain = fluid.CanErode(collision.collider);
        ErosionTarget target = null;
        if (!terrain && !ErosionTarget.TryGet(collision.collider, out target)) return;
        ContactPoint contact = collision.GetContact(0);
        int index = -1;
        for (int i = 0; i < touchCount; i++) if (touches[i].collider == collision.collider) { index = i; break; }
        if (index < 0) { if (touchCount == touches.Length) return; index = touchCount++; }
        touches[index] = new Touch { collider = collision.collider, target = target, isTarget = !terrain,
            point = contact.point, normal = contact.normal, step = Time.fixedTime };
        if (terrain) effectContact = touches[index];
    }
}
