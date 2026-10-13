using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class ErosiveFluidDroplet : MonoBehaviour
{
    private struct Touch { public Collider collider; public Vector3 point, normal; public float step; }
    private readonly Touch[] touches = new Touch[4];
    private int touchCount;
    private ErosiveFluidSystem fluid;
    private ErosiveFluidSource source;
    private Rigidbody body;
    private SphereCollider sphere;
    private Renderer visual;
    private MaterialPropertyBlock properties;
    private float age, probeTimer;
    private Touch effectContact;
    public Rigidbody Body => body;
    public SphereCollider Collider => sphere;
    public Vector3 Position => body.position;
    public float Age => age;
    public int TerrainContactCount => touchCount;
    private static readonly int VelocityId = Shader.PropertyToID("_FlowVelocity");
    private static readonly int ContactId = Shader.PropertyToID("_ContactPoint");
    private static readonly int NormalId = Shader.PropertyToID("_ContactNormal");
    private static readonly int StrengthId = Shader.PropertyToID("_ErosionStrength");
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
        source = emitter; age = probeTimer = 0f; touchCount = 0; effectContact = default;
        transform.localScale = Vector3.one * emitter.Radius * 2f;
        body.position = position; transform.position = position;
        body.linearVelocity = velocity; body.angularVelocity = Vector3.zero;
        gameObject.SetActive(true); body.WakeUp();
        fluid.IgnoreActors(sphere);
        properties.Clear(); visual.SetPropertyBlock(properties);
    }
    internal bool Simulate(float dt)
    {
        age += dt;
        if (source == null || age >= source.Lifetime || !fluid.InBounds(body.position)) return false;
        // Writing even an unchanged velocity wakes a resting Rigidbody. Clamp only an overspeed
        // body so sleeping droplets can retain their low-cost contact erosion.
        Vector3 velocity = body.linearVelocity;
        if (velocity.sqrMagnitude > fluid.MaximumSpeed * fluid.MaximumSpeed)
            body.linearVelocity = velocity.normalized * fluid.MaximumSpeed;
        probeTimer -= dt;
        bool validate = probeTimer <= 0f;
        if (validate) probeTimer = fluid.SampleInterval;
        for (int i = touchCount - 1; i >= 0; i--)
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
            float speed = Vector3.ProjectOnPlane(body.linearVelocity, touches[i].normal).magnitude;
            float strength = source.Rate * fluid.World.Erodibility * source.MotionMultiplier(speed);
            activity += strength;
            fluid.Accumulate(touches[i].point, source.ErosionRadius, strength * dt / touchCount);
        }
        properties.SetVector(VelocityId, body.linearVelocity);
        properties.SetFloat(StrengthId, activity);
        properties.SetVector(ContactId, touchCount > 0 ? touches[0].point : body.position);
        properties.SetVector(NormalId, touchCount > 0 ? touches[0].normal : Vector3.zero);
        visual.SetPropertyBlock(properties);
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
    { touchCount = 0; source = null; body.linearVelocity = body.angularVelocity = Vector3.zero; gameObject.SetActive(false); }
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
