using UnityEngine;

/// <summary>
/// One small floating bead of water. On its own it only hangs in the air and drifts; a
/// DropletChoir gives it a goal point each frame and it eases there on a critically damped
/// spring, so its motion is always slow and smooth (never snapping or jittering). Each droplet
/// has its own unhurriedness, so a choir moves like a flock rather than a grid.
/// It can be picked up like any creature; once let go it floats back to its choir.
/// Built entirely in code: a sphere bead, a kinematic body and a collider that touches nothing
/// (it is still found by the pickup ray).
/// </summary>
[DisallowMultipleComponent]
public sealed class GlyphDroplet : Creature
{
    [Header("Droplet")]
    [Tooltip("Bead diameter in metres.")]
    [Min(0.02f)] [SerializeField] private float diameter = 0.13f;
    [Tooltip("Seconds the spring takes to close most of the gap to its goal. Larger = lazier.")]
    [Min(0.1f)] [SerializeField] private float smoothTime = 1.4f;
    [Tooltip("Top speed in metres per second, so far goals are reached by gliding, not rushing.")]
    [Min(0.1f)] [SerializeField] private float maxSpeed = 2.4f;
    [Tooltip("How much the bead stretches along its motion (teardrop) at 1 m/s.")]
    [Range(0f, 1f)] [SerializeField] private float stretch = 0.22f;
    [SerializeField] private Material material;

    private Transform bead;
    private Renderer beadRenderer;
    private MaterialPropertyBlock block;
    private Vector3 velocity;
    private Vector3 goal;
    private bool hasGoal, wasHeld;
    private float glow, shownGlow = -1f;
    private Quaternion beadTurn = Quaternion.identity;
    private static Mesh sphereMesh;
    private static Material fallbackMaterial;
    private static readonly int GlowId = Shader.PropertyToID("_Glow");

    /// <summary>The choir this droplet sings in (null when it is free).</summary>
    public DropletChoir Choir { get; private set; }
    public Vector3 Velocity => velocity;
    public float Diameter => diameter;
    /// <summary>True while the player holds it or a bubble has it: it does not follow its goal then.</summary>
    public bool Held => IsCarried || IsBubbleCaptured || CreatureThrower.IsFlying(this);
    public override bool HeldInView => true;
    /// <summary>A summoned title's creatures cannot be taken out of it.</summary>
    public override bool CanBeCarried => Choir == null || !Choir.IsSummoned;

    protected override void InitializeCreature()
    {
        SetCreatureName("Glyph Droplet");
        BuildBody();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        velocity = Vector3.zero;
        goal = transform.position;
    }

    /// <summary>Called by the choir when it adopts this droplet.</summary>
    internal void Configure(DropletChoir choir, Material beadMaterial, float beadDiameter, float lazy, float topSpeed)
    {
        Choir = choir;
        diameter = beadDiameter;
        smoothTime = lazy;
        maxSpeed = topSpeed;
        if (beadMaterial != null) material = beadMaterial;
        BuildBody();
        // Title creatures ride the camera: they must never catch the player's aim or pickup rays.
        if (choir != null && choir.IsSummoned && TryGetComponent<SphereCollider>(out var sphere)) sphere.enabled = false;
    }

    /// <summary>0..1: how fully the droplet has condensed out of the air (a summoned choir fades in and out).</summary>
    public float Presence { get; private set; } = 1f;
    public float Radius => diameter * 0.5f * Presence;
    internal void SetPresence(float value) => Presence = Mathf.Clamp01(value);
    public float Glow => glow;

    /// <summary>Hides the bead when a DropletSurface draws this droplet as part of a merged body.</summary>
    internal void SetBeadVisible(bool visible)
    {
        if (beadRenderer != null && beadRenderer.enabled != visible) beadRenderer.enabled = visible;
    }

    /// <summary>Where the droplet should be heading now, and how brightly it glows (0..1).</summary>
    internal void SetGoal(Vector3 point, float glowAmount)
    {
        goal = point;
        hasGoal = true;
        glow = glowAmount;
    }

    protected override void TickCreature(float deltaTime)
    {
        if (Held)
        {
            // Hands and bubbles own the movement; forget any momentum so release starts calm.
            velocity = Vector3.zero;
            wasHeld = true;
            UpdateBead(deltaTime);
            return;
        }
        if (wasHeld)
        {
            wasHeld = false;
            goal = transform.position;
        }
        if (!hasGoal) goal = transform.position;
        Vector3 next = Vector3.SmoothDamp(transform.position, goal, ref velocity, smoothTime, maxSpeed, deltaTime);
        MoveCreature(next - transform.position);
        UpdateBead(deltaTime);
    }

    private void UpdateBead(float deltaTime)
    {
        if (bead == null) return;
        // Stretch smoothly with speed; at rest it is a round bead, so its facing never matters.
        float speed = velocity.magnitude;
        float s = 1f + Mathf.Min(0.6f, speed * stretch);
        if (speed > 0.02f)
            beadTurn = Quaternion.Slerp(beadTurn, Quaternion.LookRotation(velocity / speed), 1f - Mathf.Exp(-deltaTime * 4f));
        bead.rotation = beadTurn;
        float size = diameter * Presence;
        bead.localScale = new Vector3(size / Mathf.Sqrt(s), size / Mathf.Sqrt(s), size * s);
        if (beadRenderer != null && Mathf.Abs(glow - shownGlow) > 0.01f)
        {
            shownGlow = glow;
            block ??= new MaterialPropertyBlock();
            block.SetFloat(GlowId, glow);
            beadRenderer.SetPropertyBlock(block);
        }
    }

    private void BuildBody()
    {
        if (!TryGetComponent<Rigidbody>(out var body)) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;
        if (!TryGetComponent<SphereCollider>(out var sphere)) sphere = gameObject.AddComponent<SphereCollider>();
        // Big enough to aim at, but it never pushes or is pushed by anything.
        sphere.radius = Mathf.Max(0.12f, diameter * 0.9f);
        sphere.excludeLayers = ~0;
        sphere.layerOverridePriority = 100;

        if (bead == null)
        {
            Transform existing = transform.Find("Bead");
            bead = existing != null ? existing : new GameObject("Bead").transform;
            bead.SetParent(transform, false);
        }
        if (sphereMesh == null)
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphereMesh = probe.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Destroy(probe); else DestroyImmediate(probe);
        }
        if (!bead.TryGetComponent<MeshFilter>(out var filter)) filter = bead.gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = sphereMesh;
        if (!bead.TryGetComponent(out beadRenderer)) beadRenderer = bead.gameObject.AddComponent<MeshRenderer>();
        beadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beadRenderer.receiveShadows = false;
        if (material == null)
        {
            if (fallbackMaterial == null)
            {
                Shader shader = Shader.Find("Capstone/Creature/GoldenGlyph");
                if (shader != null) fallbackMaterial = new Material(shader) { name = "Glyph droplet (runtime)", enableInstancing = true };
            }
            material = fallbackMaterial;
        }
        if (material != null) beadRenderer.sharedMaterial = material;
        bead.localScale = Vector3.one * diameter;
        shownGlow = -1f;
    }
}
