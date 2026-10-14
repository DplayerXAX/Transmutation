using UnityEngine;

/// <summary>
/// A pooled erosive water droplet. As a Creature it can be carried, petted, and heated by the
/// player and other creatures; enough Heat evaporates it. The fluid system owns its lifetime.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class ErosiveFluidDroplet : Creature
{
    private struct Touch { public Collider collider; public Vector3 point, normal; public float step; }
    private readonly Touch[] touches = new Touch[4];
    private static readonly Collider[] overlaps = new Collider[8];
    private int touchCount;
    private Vector3 lastPosition;
    private ErosiveFluidSystem fluid;
    private ErosiveFluidSource source;
    private Rigidbody body;
    private SphereCollider sphere;
    private Renderer visual;
    private MaterialPropertyBlock properties;
    private float elapsed, probeTimer;
    private bool evaporated, wasCarried;
    private Touch effectContact;
    public Rigidbody Body => body;
    public SphereCollider Collider => sphere;
    public Vector3 Position => body.position;
    /// <summary>Seconds since launch, not counting time spent carried or paused.</summary>
    public float Elapsed => elapsed;
    public int TerrainContactCount => touchCount;
    public bool Evaporated => evaporated;
    private static readonly int VelocityId = Shader.PropertyToID("_FlowVelocity");
    private static readonly int ContactId = Shader.PropertyToID("_ContactPoint");
    private static readonly int NormalId = Shader.PropertyToID("_ContactNormal");
    private static readonly int StrengthId = Shader.PropertyToID("_ErosionStrength");
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
        source = emitter; elapsed = probeTimer = 0f; touchCount = 0; effectContact = default;
        evaporated = wasCarried = false;
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
        if (IsDead || evaporated || source == null || !fluid.InBounds(body.position)) return false;
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
        if (placed) ProbeHeldContacts(position);
        else for (int i = touchCount - 1; i >= 0; i--)
        {
            Touch touch = touches[i];
            bool valid = touch.collider != null && touch.collider.enabled && fluid.CanErode(touch.collider);
            if (valid && validate)
            {
                // Raycast from the centre avoids sphere-cast overlap ambiguity, including sleeping bodies.
                valid = touch.collider.Raycast(new Ray(body.position, -touch.normal), out RaycastHit hit, source.Radius + 0.06f);
                if (valid) { touch.point = hit.point; touch.normal = hit.normal; touches[i] = touch;
                    effectContact = touch; effectContact.step = Time.fixedTime; }
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
            fluid.Accumulate(touches[i].point, source.ErosionRadius, strength * dt / touchCount);
        }
        properties.SetVector(VelocityId, velocity);
        properties.SetFloat(StrengthId, activity);
        properties.SetVector(ContactId, touchCount > 0 ? touches[0].point : position);
        properties.SetVector(NormalId, touchCount > 0 ? touches[0].normal : Vector3.zero);
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
    }
    protected override void OnHeatChanged()
    {
        if (evaporated || source == null || IsDead || Heat < source.EvaporationHeat) return;
        evaporated = true;
        fluid.Evaporate(body.position);
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
        if (fluid == null || !fluid.CanErode(collision.collider) || collision.contactCount == 0) return;
        ContactPoint contact = collision.GetContact(0);
        int index = -1;
        for (int i = 0; i < touchCount; i++) if (touches[i].collider == collision.collider) { index = i; break; }
        if (index < 0) { if (touchCount == touches.Length) return; index = touchCount++; }
        touches[index] = new Touch { collider = collision.collider, point = contact.point, normal = contact.normal, step = Time.fixedTime };
        effectContact = touches[index];
    }
}
