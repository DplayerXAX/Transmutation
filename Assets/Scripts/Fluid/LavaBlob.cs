using UnityEngine;

/// <summary>
/// One pooled blob of lava, owned and stepped by LavaFluid. A small physics sphere that only collides with the
/// ground, walls and other lava (creatures and the player pass through it; it burns them instead). Hot, it is
/// runny and slides downhill, slumping flat where it stops; as it cools it thickens, sets into a crust that
/// stays put on the surface for a while, then crumbles away.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class LavaBlob : MonoBehaviour
{
    private Rigidbody body;
    private SphereCollider sphere;
    private Transform visual;
    private Renderer visualRenderer;
    private MaterialPropertyBlock block;
    private LavaFluid fluid;
    private float age, heat, slump, grounded;
    private Vector3 previous;
    private bool set;

    public Rigidbody Body => body;
    public Vector3 Position => body.position;
    /// <summary>1 just spat out .. 0 cooled to crust.</summary>
    public float Temperature => heat;
    /// <summary>Still runny: it flows and pulls on its neighbours.</summary>
    public bool Fluid => !set && heat > fluid.SetBelow;
    public bool Active { get; private set; }
    private static readonly int HeatId = Shader.PropertyToID("_Heat");

    internal void Initialize(LavaFluid owner, Mesh mesh, Material material, PhysicsMaterial friction)
    {
        fluid = owner;
        body = GetComponent<Rigidbody>();
        sphere = GetComponent<SphereCollider>();
        body.mass = 0.25f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.maxDepenetrationVelocity = 3f;
        body.constraints = RigidbodyConstraints.FreezeRotation; // it slides, it does not roll
        sphere.radius = 0.5f;
        sphere.sharedMaterial = friction;
        // Lava meets only the ground, walls and other lava; everything else it burns as it passes through.
        sphere.includeLayers = owner.CollisionLayers;
        sphere.excludeLayers = ~owner.CollisionLayers;
        sphere.layerOverridePriority = 100;
        var look = new GameObject("Lava Look");
        look.layer = gameObject.layer;
        look.transform.SetParent(transform, false);
        look.AddComponent<MeshFilter>().sharedMesh = mesh;
        visualRenderer = look.AddComponent<MeshRenderer>();
        visualRenderer.sharedMaterial = material;
        visualRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        visual = look.transform;
        block = new MaterialPropertyBlock();
    }

    internal void Launch(Vector3 position, Vector3 velocity, float startHeat)
    {
        age = 0f; slump = 0f; grounded = 0f; set = false;
        heat = Mathf.Clamp01(startHeat);
        transform.localScale = Vector3.one * fluid.BlobRadius * 2f;
        transform.SetPositionAndRotation(position, Quaternion.identity);
        body.position = position;
        previous = position;
        body.isKinematic = false;
        body.useGravity = true;
        body.linearVelocity = velocity;
        body.angularVelocity = Vector3.zero;
        gameObject.SetActive(true);
        Active = true;
        body.WakeUp();
        UpdateLook(0f);
    }

    /// <summary>One physics step. Returns false when it has crumbled away and should go back to the pool.</summary>
    internal bool Simulate(float dt)
    {
        if (!Active) return false;
        age += dt;
        heat = Mathf.Max(0f, heat - dt / fluid.CoolTime);
        if (age >= fluid.Lifetime) return false;

        if (!set)
        {
            // Never through rock: fast lava can slip between terrain chunks, so a step that crossed a surface
            // is put back on it, sliding along it.
            Vector3 now = body.position;
            if (Physics.Linecast(previous, now, out RaycastHit hit, fluid.CollisionLayers, QueryTriggerInteraction.Ignore))
            {
                now = hit.point + hit.normal * fluid.BlobRadius * 1.05f;
                body.position = now;
                transform.position = now;
                body.linearVelocity = Vector3.ProjectOnPlane(body.linearVelocity, hit.normal) * 0.5f;
            }
            previous = now;
            // Thicker as it cools: runny when hot, sluggish near setting.
            body.linearDamping = Mathf.Lerp(fluid.Viscosity.y, fluid.Viscosity.x, heat);
            bool slow = body.linearVelocity.sqrMagnitude < 0.04f;
            grounded = slow ? grounded + dt : 0f;
            if (heat <= fluid.SetBelow && grounded > 0.3f) Set();
        }
        float settle = Mathf.Clamp01(grounded * 2f);
        slump = Mathf.MoveTowards(slump, set ? 1f : settle, dt * 1.5f);
        UpdateLook(dt);
        return true;
    }

    /// <summary>Sets into crust: it stops moving and stays where it is until it crumbles.</summary>
    private void Set()
    {
        set = true;
        body.linearVelocity = Vector3.zero;
        body.isKinematic = true;
    }

    private void UpdateLook(float dt)
    {
        // A puddle where it rests (wider and flat), a round gob while it flies; it crumbles away at the end.
        float crumble = Mathf.Clamp01((fluid.Lifetime - age) / 2f);
        float wide = Mathf.Lerp(1.35f, 2.1f, slump) * crumble;
        float tall = Mathf.Lerp(1.25f, 0.55f, slump) * crumble;
        visual.localScale = new Vector3(wide, tall, wide);
        visual.localPosition = Vector3.down * (0.5f - tall * 0.5f) * slump;
        visual.rotation = Quaternion.identity;
        block.SetFloat(HeatId, heat);
        visualRenderer.SetPropertyBlock(block);
    }

    /// <summary>Gives up its heat (to a fire snail drinking it) and goes back to the pool. Returns 0..1 heat it had.</summary>
    internal float Absorb()
    {
        float had = heat;
        heat = 0f;
        Recycle();
        return had;
    }

    internal void Recycle()
    {
        Active = false;
        if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        gameObject.SetActive(false);
    }
}
