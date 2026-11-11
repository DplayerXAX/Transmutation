using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Bellows: a glassy bladder held in bone hoops, with a flared bone spout at the front and four thin legs.
/// It soaks up heat from fire nearby (fire bubbles that drift against it, fire sources) and swells with it; the hoops
/// spread apart like the folds of a bellows and an ember brightens inside.
/// Bumped (by the player, a thrown creature, anything moving) or poked with LMB, it slowly sighs the heat out: a
/// stream of air leaves the spout and pushes what is in front of it, harder the fuller it is and the harder it was hit.
/// Too much heat and it lifts off the ground and hovers; venting while it floats drives it backwards like a balloon.
/// Carried, LMB puffs along the view (and the push back can carry the player). Everything is built in code.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class BellowsCreature : Creature
{
    [Header("Heat")]
    [Tooltip("Heat that fills it completely (charge 1).")]
    [Min(1f)] [SerializeField] private float heatToFull = 30f;
    [Tooltip("It cannot hold more than this many fills; the rest is lost.")]
    [Min(1f)] [SerializeField] private float maxCharge = 2f;
    [Tooltip("Hot creatures (fire bubbles, fire sources) this close give their heat to it.")]
    [Min(0.1f)] [SerializeField] private float absorbReach = 1.2f;
    [Min(0f)] [SerializeField] private float absorbPerSecond = 8f;
    [Tooltip("Heat lost per second by itself.")]
    [Min(0f)] [SerializeField] private float coolPerSecond = 0.25f;
    [Tooltip("Charge it starts with, so a fresh one can be tried at once (0 = empty until fed with fire).")]
    [Range(0f, 1f)] [SerializeField] private float startCharge = 0.6f;
    [Tooltip("Charge at which it starts to float.")]
    [Min(0.5f)] [SerializeField] private float floatAbove = 1.25f;

    [Header("Size")]
    [Min(0.1f)] [SerializeField] private float emptyRadius = 0.45f;
    [Min(0.1f)] [SerializeField] private float fullRadius = 1.05f;

    [Header("Air Stream")]
    [Tooltip("Push (m/s² for a 1 kg body) at the spout, full and squeezed normally. Heavier bodies move less.")]
    [Min(0f)] [SerializeField] private float gustForce = 40f;
    [Min(1f)] [SerializeField] private float gustRange = 9f;
    [Range(5f, 60f)] [SerializeField] private float gustAngle = 24f;
    [Tooltip("Charge sighed out per second when squeezed normally.")]
    [Min(0.05f)] [SerializeField] private float ventPerSecond = 0.4f;
    [Tooltip("Closing speed of a bump that makes it vent (m/s). A harder bump vents faster and pushes harder.")]
    [Min(0.1f)] [SerializeField] private float bumpSpeed = 1.5f;
    [Tooltip("Seconds one LMB puff lasts while carried.")]
    [Min(0.1f)] [SerializeField] private float carriedPuff = 0.6f;

    [Header("Floating")]
    [Tooltip("Hover height above the ground at the float threshold, and how much higher per extra fill.")]
    [SerializeField] private Vector2 hoverHeight = new Vector2(1.5f, 9f);

    private Rigidbody body;
    private SphereCollider bodyCollider;
    private Transform look, membrane, spout, core;
    private readonly List<Transform> hoops = new List<Transform>();
    private readonly List<Transform> legs = new List<Transform>();
    private readonly List<Transform> spines = new List<Transform>();
    private Material membraneMaterial, boneMaterial, coreMaterial, airMaterial;
    private readonly List<Mesh> meshes = new List<Mesh>();

    private float shownRadius;
    private float ventLeft, ventIntensity;
    private Vector3 ventDirection;
    private bool ventAlongView;
    private float nextRing, wheeze;
    private readonly Collider[] hits = new Collider[256];
    private readonly HashSet<Rigidbody> pushed = new HashSet<Rigidbody>();

    private sealed class Ring { public LineRenderer line; public Vector3 origin, direction; public float age, life, speed; }
    private readonly List<Ring> rings = new List<Ring>();

    /// <summary>Stored heat in fills (0 empty, 1 full, above 1 swollen).</summary>
    public float Charge => Heat / heatToFull;
    public bool Venting => ventLeft > 0f;
    public bool Floating => Charge >= floatAbove && !IsCarried;
    public Vector3 Spout => spout != null ? spout.position : transform.position + transform.forward * Radius;
    public float Radius => Mathf.Lerp(emptyRadius, fullRadius, Mathf.Clamp01(Charge)) * (1f + 0.12f * Mathf.Clamp01(Charge - 1f));
    /// <summary>For tests: the push it gave on the last physics step (m/s², strongest body).</summary>
    public float LastPush { get; private set; }
    public string LastPushed { get; private set; }

    public override bool CanUseWhileCarried => !IsDead;
    public override string CarriedPrompt => IsDead ? null : "[LMB] Puff";
    public override string InteractionPrompt => IsDead || IsCarried ? null : "[LMB] Squeeze";

    // ---------------- Building ----------------

    protected override void InitializeCreature()
    {
        SetCreatureName("Bellows");
        body = GetComponent<Rigidbody>();
        body.mass = 1.5f;
        body.linearDamping = 0.4f;
        body.angularDamping = 2f;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        bodyCollider = GetComponent<SphereCollider>();
        if (bodyCollider == null) bodyCollider = gameObject.AddComponent<SphereCollider>();
        if (look == null) Build();
        if (Application.isPlaying && Heat <= 0f && startCharge > 0f) AddHeat(startCharge * heatToFull);
        shownRadius = Radius;
        ApplySize(0f);
    }

    private void Build()
    {
        Shader comb = Shader.Find("Capstone/Landmarks/BoneComb");
        Shader marble = Shader.Find("Capstone/Landmarks/BoneMarble");
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        membraneMaterial = new Material(comb != null ? comb : lit) { name = "Bellows membrane" };
        membraneMaterial.SetColor("_Body", new Color(0.42f, 0.22f, 0.85f, 0.2f));
        membraneMaterial.SetColor("_Rim", new Color(0.85f, 0.75f, 1f, 1f));
        membraneMaterial.SetColor("_Core", new Color(1f, 0.8f, 0.95f, 1f));
        membraneMaterial.SetFloat("_CombRows", 60f);
        membraneMaterial.SetColor("_GlowColor", new Color(1f, 0.45f, 0.12f, 1f));
        boneMaterial = new Material(marble != null ? marble : lit) { name = "Bellows bone" };
        if (marble != null)
        {
            boneMaterial.SetColor("_ColorA", new Color(0.92f, 0.9f, 0.86f));
            boneMaterial.SetColor("_ColorB", new Color(0.05f, 0.05f, 0.08f));
            boneMaterial.SetColor("_ColorMid", new Color(0.55f, 0.5f, 0.62f));
            boneMaterial.SetFloat("_Scale", 1.6f);
            boneMaterial.SetFloat("_Flow", 0f);
            boneMaterial.SetColor("_GlowColor", new Color(1f, 0.45f, 0.1f));
        }
        coreMaterial = new Material(unlit) { name = "Bellows ember" };
        Shader sprites = Shader.Find("Sprites/Default") ?? unlit;
        airMaterial = new Material(sprites) { name = "Bellows air" };

        look = new GameObject("Bellows Look").transform;
        look.SetParent(transform, false);

        membrane = Part("Membrane", Lathe(MembraneProfile(), 28, capped: false), membraneMaterial);
        spout = Part("Spout", Lathe(SpoutProfile(), 20, capped: false), boneMaterial);
        for (int i = 0; i < 5; i++) hoops.Add(Part("Hoop " + i, Torus(1f, 0.045f, 32, 6), boneMaterial));
        // A ridge of short bone spines along the back, so it reads as a creature, not a balloon.
        Mesh spine = Lathe(new[] { new Vector2(0.05f, 0f), new Vector2(0.035f, 0.5f), new Vector2(0.0f, 1f) }, 6, capped: true);
        for (int i = 0; i < 4; i++)
        {
            spines.Add(Part("Spine " + i, spine, boneMaterial));
        }
        Mesh leg = Lathe(new[] { new Vector2(0.06f, 0f), new Vector2(0.05f, 0.45f), new Vector2(0.03f, 0.85f), new Vector2(0.0f, 1f) }, 6, capped: true);
        for (int i = 0; i < 4; i++)
        {
            Transform l = Part("Leg " + i, leg, boneMaterial);
            legs.Add(l);
        }
        var ember = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ember.name = "Ember";
        Destroy(ember.GetComponent<Collider>());
        ember.transform.SetParent(look, false);
        ember.GetComponent<Renderer>().sharedMaterial = coreMaterial;
        core = ember.transform;
        if (Application.isPlaying) HeatHaze.Attach(this);
    }

    private Transform Part(string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name);
        go.transform.SetParent(look, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    // A gourd along +z: round at the back, swelling to its widest behind the middle, pinched into a neck at the front.
    private static Vector2[] MembraneProfile()
    {
        var p = new List<Vector2>();
        for (int i = 0; i <= 16; i++)
        {
            float t = i / 16f;
            float z = Mathf.Lerp(-1f, 0.95f, t);
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z)) * (1f - 0.12f * Mathf.Max(0f, z));
            if (z > 0.55f) r = Mathf.Lerp(r, 0.3f, Mathf.SmoothStep(0f, 1f, (z - 0.55f) / 0.4f));
            p.Add(new Vector2(Mathf.Max(0.02f, r), z));
        }
        return p.ToArray();
    }

    // A flared trumpet of bone.
    private static Vector2[] SpoutProfile() => new[]
    {
        new Vector2(0.3f, 0f), new Vector2(0.24f, 0.18f), new Vector2(0.22f, 0.32f), new Vector2(0.3f, 0.46f),
        new Vector2(0.48f, 0.56f), new Vector2(0.56f, 0.58f), new Vector2(0.5f, 0.54f), new Vector2(0.27f, 0.42f),
        new Vector2(0.18f, 0.2f), new Vector2(0.22f, 0.02f)
    };

    /// <summary>Spins a (radius, z) profile round the z axis.</summary>
    private Mesh Lathe(Vector2[] profile, int sides, bool capped)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
        for (int i = 0; i < profile.Length; i++)
            for (int s = 0; s <= sides; s++)
            {
                float a = s / (float)sides * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(a) * profile[i].x, Mathf.Sin(a) * profile[i].x, profile[i].y));
                uv.Add(new Vector2(i / (float)(profile.Length - 1), s / (float)sides));
            }
        int row = sides + 1;
        for (int i = 0; i < profile.Length - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = i * row + s, b = a + 1, c = a + row, d = c + 1;
                tris.AddRange(new[] { a, b, c, b, d, c });
            }
        var mesh = new Mesh { name = "Bellows lathe" };
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        meshes.Add(mesh);
        return mesh;
    }

    private Mesh Torus(float radius, float thickness, int sides, int tube)
    {
        var v = new List<Vector3>(); var tris = new List<int>();
        for (int s = 0; s <= sides; s++)
        {
            float a = s / (float)sides * Mathf.PI * 2f;
            var centre = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
            for (int t = 0; t <= tube; t++)
            {
                float b = t / (float)tube * Mathf.PI * 2f;
                v.Add(centre + (centre.normalized * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b)) * thickness);
            }
        }
        int row = tube + 1;
        for (int s = 0; s < sides; s++)
            for (int t = 0; t < tube; t++)
            {
                int a = s * row + t, b = a + 1, c = a + row, d = c + 1;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
        var mesh = new Mesh { name = "Bellows hoop" };
        mesh.SetVertices(v); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        meshes.Add(mesh);
        return mesh;
    }

    // ---------------- Living ----------------

    protected override void TickCreature(float deltaTime)
    {
        // Carried: holding the left button keeps the stream going along the view (a click is a short puff).
        if (IsCarried && Venting && ventAlongView && UnityEngine.InputSystem.Mouse.current != null &&
            UnityEngine.InputSystem.Mouse.current.leftButton.isPressed && Camera.main != null)
        {
            ventLeft = Mathf.Max(ventLeft, 0.15f);
            ventDirection = Camera.main.transform.forward;
        }
        if (!Venting && Heat > 0f) SpendHeat(coolPerSecond * deltaTime);
        ApplySize(deltaTime);
        UpdateRings(deltaTime);
    }

    /// <summary>Size, hoops, legs and the ember follow the stored heat; it breathes a little.</summary>
    private void ApplySize(float deltaTime)
    {
        if (look == null) return;
        float charge = Charge;
        shownRadius = deltaTime <= 0f ? Radius : Mathf.MoveTowards(shownRadius, Radius, deltaTime * (Venting ? 0.9f : 0.5f));
        float r = shownRadius;
        float breath = 1f + 0.035f * Mathf.Sin(Time.time * (1.4f + charge)) * (Venting ? 0f : 1f);
        // Venting: the bladder shudders and squeezes in from the sides.
        wheeze = Mathf.MoveTowards(wheeze, 0f, deltaTime);
        float flutter = Venting ? 0.05f * Mathf.Sin(Time.time * 38f) * ventIntensity : 0.09f * wheeze * Mathf.Sin(Time.time * 45f);
        membrane.localScale = new Vector3(r * (breath + flutter), r * (breath - flutter), r * 1.15f);
        membrane.localPosition = Vector3.zero;
        float spread = Mathf.Lerp(0.7f, 1.05f, Mathf.Clamp01(charge));
        for (int i = 0; i < hoops.Count; i++)
        {
            float z = Mathf.Lerp(-0.62f, 0.5f, i / (hoops.Count - 1f)) * spread;
            float ring = Mathf.Sqrt(Mathf.Max(0.05f, 1f - z * z)) * (1f - 0.12f * Mathf.Max(0f, z));
            hoops[i].localPosition = new Vector3(0f, 0f, z * r * 1.15f);
            hoops[i].localScale = Vector3.one * (ring * r * breath + 0.02f);
            hoops[i].localRotation = Quaternion.Euler(0f, 0f, i * 23f);
        }
        // Spines stand on the top of the bladder (its outline at that point), leaning back like a crest.
        for (int i = 0; i < spines.Count; i++)
        {
            float z = Mathf.Lerp(-0.5f, 0.35f, i / Mathf.Max(1f, spines.Count - 1f));
            float top = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z)) * (1f - 0.12f * Mathf.Max(0f, z)) * r * (breath - flutter);
            spines[i].localPosition = new Vector3(0f, top - 0.03f * r, z * r * 1.15f);
            spines[i].localRotation = Quaternion.Euler(-90f + 25f + (i - 1.5f) * 10f, 0f, 0f);
            spines[i].localScale = Vector3.one * r * (0.75f + 0.18f * Mathf.Sin(i * 1.7f));
        }
        spout.localPosition = new Vector3(0f, 0f, r * 1.15f * 0.88f);
        spout.localScale = Vector3.one * Mathf.Max(0.6f, r * 1.1f) * (Venting ? 1f + 0.08f * Mathf.Sin(Time.time * 30f) : 1f);
        core.localScale = Vector3.one * r * (0.35f + 0.12f * Mathf.Clamp01(charge - 1f));
        Color ember = Color.Lerp(new Color(0.18f, 0.1f, 0.35f), new Color(3.2f, 1.1f, 0.25f), Mathf.Clamp01(charge));
        if (charge > 1f) ember = Color.Lerp(ember, new Color(4f, 3f, 2.2f), Mathf.Clamp01((charge - 1f) / (maxCharge - 1f)) * (0.6f + 0.4f * Mathf.Sin(Time.time * 6f)));
        coreMaterial.SetColor("_BaseColor", ember);
        coreMaterial.SetColor("_Color", ember);
        membraneMaterial.SetFloat("_GlowStrength", Mathf.Clamp01(charge) * 1.6f);
        boneMaterial.SetFloat("_GlowStrength", Mathf.Clamp01(charge) * 0.8f);
        // Legs reach down to the ground; floating, they hang and curl in.
        bool floating = Floating;
        for (int i = 0; i < legs.Count; i++)
        {
            float a = (i + 0.5f) / legs.Count * 360f;
            Vector3 hip = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 0.45f) * r;
            hip.y = -0.55f * r;
            legs[i].localPosition = hip;
            float splay = floating ? 8f + 6f * Mathf.Sin(Time.time * 2f + i) : 32f;
            legs[i].localRotation = Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(90f - splay, 0f, 0f);
            float length = floating ? r * 0.8f : r * 0.62f;
            legs[i].localScale = new Vector3(1f, 1f, length);
        }
        bodyCollider.radius = r;
        bodyCollider.center = Vector3.zero;
    }

    private bool wasCarried;

    private void FixedUpdate()
    {
        if (!SimulationEnabled || body == null) return;
        if (wasCarried && !IsCarried) PushOutOfGeometry();
        wasCarried = IsCarried;
        Absorb(Time.fixedDeltaTime);
        if (Heat > heatToFull * maxCharge) SetHeat(heatToFull * maxCharge);
        Float();
        LastPush = 0f;
        if (Venting) Vent(Time.fixedDeltaTime);
    }

    /// <summary>Takes heat from hot creatures touching it (or very nearly).</summary>
    private void Absorb(float deltaTime)
    {
        if (Charge >= maxCharge) return;
        int count = ReflectionPhysics.OverlapSphereNonAlloc(transform.position, Radius + absorbReach, hits, ~0, QueryTriggerInteraction.Collide);
        var seen = new HashSet<Creature>();
        for (int i = 0; i < count; i++)
        {
            Creature other = hits[i].GetComponentInParent<Creature>();
            if (other == null || other == this || !seen.Add(other) || other.Heat <= 0f || other.IsDead) continue;
            bool hot = other is FireBubble fire && !fire.IsBoundToBone && !BoneChild.IsCradled(fire) && !fire.IsCarried || other is FireSource;
            if (!hot) continue;
            float take = Mathf.Min(other.Heat, absorbPerSecond * deltaTime);
            other.SetHeat(other.Heat - take);
            AddHeat(take);
        }
    }

    /// <summary>Over-full, it lifts off and hovers; the fuller, the higher.</summary>
    private void Float()
    {
        if (IsCarried || body.isKinematic) return;
        if (!Floating) { body.useGravity = true; body.linearDamping = 0.4f; return; }
        body.useGravity = false;
        body.linearDamping = 1.2f; // the air holds a floating bladder back
        float ground = transform.position.y - 50f;
        if (ReflectionPhysics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore) && hit.rigidbody != body)
            ground = hit.point.y;
        float target = ground + hoverHeight.x + hoverHeight.y * (Charge - floatAbove) / Mathf.Max(0.01f, maxCharge - floatAbove) + Radius;
        float lift = (target - transform.position.y) * 1.6f - body.linearVelocity.y * 1.4f;
        body.AddForce(Vector3.up * Mathf.Clamp(lift, -6f, 6f), ForceMode.Acceleration);
        // A slow drift, so it does not hang perfectly still.
        body.AddForce(new Vector3(Mathf.Sin(Time.time * 0.31f + GetInstanceID()), 0f, Mathf.Cos(Time.time * 0.23f)) * 0.25f, ForceMode.Acceleration);
    }

    // ---------------- Venting ----------------

    /// <summary>Starts (or strengthens) a sigh. intensity 1 is a normal squeeze; seconds &lt;= 0 means until empty.</summary>
    public void Squeeze(float intensity, float seconds, Vector3 direction, bool alongView)
    {
        if (IsDead) return;
        if (Charge < 0.03f)
        {
            // Empty: a dry wheeze, a shudder and one faint ring, so a squeeze is never met with nothing.
            wheeze = 0.5f;
            Vector3 dir = alongView && direction.sqrMagnitude > 0.01f ? direction.normalized : transform.forward;
            EmitRing(Spout + dir * 0.3f, dir, gustRange * 0.35f, gustForce * 0.15f);
            return;
        }
        ventIntensity = Mathf.Max(Venting ? ventIntensity : 0f, Mathf.Clamp(intensity, 0.3f, 2f));
        ventLeft = Mathf.Max(ventLeft, seconds > 0f ? seconds : 999f);
        ventDirection = direction;
        ventAlongView = alongView;
    }

    private void Vent(float deltaTime)
    {
        float charge = Charge;
        if (charge < 0.02f) { ventLeft = 0f; return; }
        ventLeft -= deltaTime;
        float released = Mathf.Min(charge, ventPerSecond * ventIntensity * deltaTime);
        SpendHeat(released * heatToFull);
        Vector3 forward = ventAlongView && ventDirection.sqrMagnitude > 0.01f ? ventDirection.normalized : transform.forward;
        // The fuller it is and the harder it was squeezed, the stronger the stream.
        float strength = gustForce * Mathf.Clamp(charge, 0.15f, 1.3f) * Mathf.Sqrt(ventIntensity);
        Vector3 mouth = Spout + forward * 0.3f;
        float range = gustRange * Mathf.Lerp(0.6f, 1.1f, Mathf.Clamp01(charge));
        float spread = Mathf.Tan(gustAngle * Mathf.Deg2Rad);
        int count = ReflectionPhysics.OverlapCapsuleNonAlloc(mouth, mouth + forward * range, range * spread, hits, ~0, QueryTriggerInteraction.Collide);
        pushed.Clear();
        var bubbles = new HashSet<TrappingBubble>();
        for (int i = 0; i < count; i++)
        {
            Collider c = hits[i];
            if (c.transform.IsChildOf(transform)) continue;
            Vector3 to = c.ClosestPoint(mouth) - mouth;
            if (to.sqrMagnitude < 1e-4f) to = c.bounds.center - mouth;
            float along = Vector3.Dot(to, forward);
            if (along < -0.3f || along > range) continue;
            float width = Mathf.Max(0.4f, Mathf.Max(0f, along) * spread + 0.4f);
            float side = (to - forward * along).magnitude;
            if (side > width) continue;
            float falloff = (1f - Mathf.Clamp01(along / range)) * (1f - 0.6f * side / width);
            TrappingBubble bubble = c.GetComponentInParent<TrappingBubble>();
            if (bubble != null)
            {
                if (bubbles.Add(bubble)) bubble.Tug(forward * strength * falloff * 0.15f);
                continue;
            }
            Rigidbody other = c.attachedRigidbody;
            if (other == null || other == body || other.isKinematic || !pushed.Add(other)) continue;
            float push = strength * falloff / Mathf.Sqrt(Mathf.Max(0.2f, other.mass));
            // Another Bellows is mostly air: it catches the stream like a sail.
            if (other.GetComponent<BellowsCreature>() != null) push *= 2.2f;
            var player = other.GetComponent<SmoothFirstPersonController>();
            if (player != null) player.Fling(other.linearVelocity + forward * push * deltaTime, 0.2f);
            else other.AddForce(forward * push, ForceMode.Acceleration);
            if (push > LastPush) LastPushed = other.name;
            LastPush = Mathf.Max(LastPush, push);
        }
        // Push back: a grounded bellows digs in its legs, a floating one is driven backwards, a carried one shoves the player.
        if (IsCarried)
        {
            Rigidbody carrierBody = Carrier != null ? Carrier.PlayerBody : null;
            var controller = carrierBody != null ? carrierBody.GetComponent<SmoothFirstPersonController>() : null;
            if (controller != null && !carrierBody.isKinematic)
                controller.Fling(carrierBody.linearVelocity - forward * strength * 0.3f * deltaTime, 0.25f);
        }
        else body.AddForce(-forward * strength * (Floating ? 0.18f : 0.1f), ForceMode.Acceleration);

        if (Time.time >= nextRing)
        {
            nextRing = Time.time + Mathf.Lerp(0.16f, 0.06f, Mathf.Clamp01(charge * ventIntensity));
            EmitRing(mouth, forward, range, strength);
        }
        if (ventLeft <= 0f) ventIntensity = 0f;
    }

    // Rings of air: loops of dotted light leaving the spout, widening and fading as they go.
    private void EmitRing(Vector3 origin, Vector3 direction, float range, float strength)
    {
        Ring ring = null;
        foreach (Ring r in rings) if (r.age >= r.life) { ring = r; break; }
        if (ring == null)
        {
            if (rings.Count >= 24) return;
            var go = new GameObject("Air Ring");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.positionCount = 20;
            line.sharedMaterial = airMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            ring = new Ring { line = line };
            rings.Add(ring);
        }
        ring.origin = origin;
        ring.direction = direction;
        ring.age = 0f;
        ring.speed = Mathf.Lerp(4f, 12f, Mathf.Clamp01(strength / gustForce));
        ring.life = range / ring.speed;
        ring.line.enabled = true;
    }

    private void UpdateRings(float deltaTime)
    {
        foreach (Ring ring in rings)
        {
            if (ring.age >= ring.life) { if (ring.line.enabled) ring.line.enabled = false; continue; }
            ring.age += deltaTime;
            float t = Mathf.Clamp01(ring.age / ring.life);
            Vector3 centre = ring.origin + ring.direction * ring.speed * ring.age;
            float radius = 0.25f + t * gustRange * Mathf.Tan(gustAngle * Mathf.Deg2Rad);
            Vector3 u = Vector3.Cross(ring.direction, Mathf.Abs(ring.direction.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 w = Vector3.Cross(ring.direction, u);
            for (int k = 0; k < 20; k++)
            {
                float a = k / 20f * Mathf.PI * 2f;
                float wobble = 1f + 0.08f * Mathf.Sin(a * 3f + ring.age * 9f);
                ring.line.SetPosition(k, centre + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * radius * wobble);
            }
            float alpha = (1f - t) * 0.7f;
            Color c = Color.Lerp(new Color(1f, 0.75f, 0.5f), new Color(0.75f, 0.85f, 1f), t);
            c.a = alpha;
            ring.line.startColor = ring.line.endColor = c;
            ring.line.widthMultiplier = Mathf.Lerp(0.06f, 0.02f, t);
        }
    }

    /// <summary>Just let go: if it overlaps rock or anything solid, lift it out onto the ground or back towards the player.</summary>
    private void PushOutOfGeometry()
    {
        float r = Radius;
        int count = ReflectionPhysics.OverlapSphereNonAlloc(transform.position, r * 0.95f, hits, ~0, QueryTriggerInteraction.Ignore);
        bool blocked = false;
        for (int i = 0; i < count && !blocked; i++)
            blocked = !hits[i].transform.IsChildOf(transform) && hits[i].attachedRigidbody != body &&
                      (hits[i].attachedRigidbody == null || hits[i].attachedRigidbody.isKinematic) && hits[i].GetComponentInParent<SmoothFirstPersonController>() == null;
        if (!blocked) return;
        Vector3 at = transform.position;
        // Up onto whatever it went into.
        if (ReflectionPhysics.SphereCast(at + Vector3.up * (r * 2f + 3f), r * 0.9f, Vector3.down, out RaycastHit top, r * 2f + 6f, ~0, QueryTriggerInteraction.Ignore)
            && !top.transform.IsChildOf(transform))
            at = top.point + top.normal * (r + 0.05f);
        else at += Vector3.up * (r * 2f);
        body.position = at;
        transform.position = at;
        body.linearVelocity = Vector3.zero;
    }

    // ---------------- Being touched ----------------

    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (IsDead) return;
        if (IsCarried) Squeeze(1f, carriedPuff, interaction.Direction, alongView: true);
        else Squeeze(1f, 0f, transform.forward, alongView: false);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (IsCarried || collision.rigidbody == null) return; // the ground and other still things do not squeeze it
        float speed = collision.relativeVelocity.magnitude;
        if (speed < bumpSpeed) return;
        Squeeze(Mathf.Clamp(speed / 5f, 0.4f, 1.8f), 0f, transform.forward, alongView: false);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        ventLeft = 0f;
    }

    private void OnDrawGizmos()
    {
        // Built only in Play mode; show where it stands in the Scene view.
        if (Application.isPlaying) return;
        Gizmos.color = new Color(0.7f, 0.5f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, emptyRadius);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * (emptyRadius + 0.8f));
    }

    private void OnDestroy()
    {
        foreach (Mesh m in meshes) if (m != null) Destroy(m);
        if (membraneMaterial != null) Destroy(membraneMaterial);
        if (boneMaterial != null) Destroy(boneMaterial);
        if (coreMaterial != null) Destroy(coreMaterial);
        if (airMaterial != null) Destroy(airMaterial);
    }
}
