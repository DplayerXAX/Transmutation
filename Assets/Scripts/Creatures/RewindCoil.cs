using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Rewind Coil: a small blue glass ammonite with bone ribs, a ring of twelve "ticks" round its whorl and a few
/// feelers out of its mouth. It remembers where it has been.
///
/// LMB (held or on the ground) marks a moment: it sheds a ghost copy of its shell on the spot (the echo) and from then
/// on pays out a thin blue thread along the way it travels, while its ticks count the seconds.
/// LMB again while carried rewinds: the player is reeled back along the thread, with lines of light streaming past along the thread, to the echo,
/// the view turning back to where it looked; the thread winds into the shell and the echo is taken back.
/// LMB again on the ground calls only the coil itself home along its thread.
///
/// Each rewind spends one of the glowing beads in its chambers. Beads regrow slowly by themselves, and at once when
/// it drinks the heat of a fire bubble or fire source that touches it (heat freezes into time).
/// Everything is built in code.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class RewindCoil : Creature
{
    [Header("Size")]
    [Tooltip("World size of one shell unit (the shell is about 1.9 units across).")]
    [Min(0.05f)] [SerializeField] private float size = 0.3f;

    [Header("Beads (rewinds)")]
    [Range(1, 5)] [SerializeField] private int maxBeads = 3;
    [Range(0, 5)] [SerializeField] private int startBeads = 3;
    [Tooltip("Seconds for one bead to regrow by itself.")]
    [Min(1f)] [SerializeField] private float beadRegrowSeconds = 45f;
    [Tooltip("Heat drunk from fire that makes one bead.")]
    [Min(0.1f)] [SerializeField] private float heatPerBead = 10f;
    [Tooltip("Fire bubbles and fire sources this close to the shell give it their heat.")]
    [Min(0.05f)] [SerializeField] private float absorbReach = 0.6f;
    [Min(0f)] [SerializeField] private float absorbPerSecond = 6f;

    [Header("Thread")]
    [Tooltip("Distance travelled between two remembered points (m).")]
    [Min(0.05f)] [SerializeField] private float sampleSpacing = 0.3f;
    [Tooltip("Above this many points the path is thinned (every other point dropped), so it never runs out.")]
    [Min(64)] [SerializeField] private int maxSamples = 3000;
    [Tooltip("A jump bigger than this in one frame (a respawn) breaks the thread.")]
    [Min(1f)] [SerializeField] private float breakJump = 12f;
    [Tooltip("The thread is drawn this far below the remembered body centre.")]
    [SerializeField] private float threadDrop = 0.45f;

    [Header("Rewind")]
    [Tooltip("Average reel speed (m/s); short threads still take the minimum time, long ones at most the maximum.")]
    [Min(1f)] [SerializeField] private float rewindSpeed = 16f;
    [SerializeField] private Vector2 rewindSeconds = new Vector2(0.9f, 5f);
    [Tooltip("Height of the player's body centre over a point the coil itself remembered on the ground.")]
    [SerializeField] private float playerLift = 1f;

    private struct Sample { public Vector3 position; public float yaw, pitch; }

    private Rigidbody body;
    private SphereCollider bodyCollider;
    private Transform look, shell, echo;
    private Mesh shellMesh, ribMesh;
    private readonly List<Mesh> meshes = new List<Mesh>();
    private Material glassMaterial, boneMaterial, beadMaterial, tickMaterial, tickLitMaterial, ghostMaterial, ghostBoneMaterial, lineMaterial;
    private readonly List<Renderer> beads = new List<Renderer>();
    private readonly List<Material> beadMaterials = new List<Material>();
    private readonly List<Renderer> ticks = new List<Renderer>();
    private readonly List<Transform> feelers = new List<Transform>();
    private readonly List<Quaternion> feelerRotations = new List<Quaternion>();
    private Vector3 mouthLocal, mouthTangent;
    private LineRenderer thread;

    private readonly List<Sample> samples = new List<Sample>();
    private readonly List<float> lengths = new List<float>();
    private Vector3 lastRecorded;
    private float recordedSeconds, shownHand, echoAlpha, warmFlash, dryShudder, cooldown;
    private float beadCharge;
    private bool recording, rewinding, rewindPlayer;
    private float rewindTime, rewindDuration, rewindFrom;
    private SmoothFirstPersonController controller;
    private Rigidbody playerBody;
    private bool savedKinematic, savedRestricted, coilWasKinematic;
    private readonly Collider[] hits = new Collider[128];
    private readonly HashSet<Creature> seen = new HashSet<Creature>();

    private sealed class Streak { public LineRenderer line; public float angle, radius, phase, speed, length, twist; }
    private readonly List<Streak> streaks = new List<Streak>();
    private const int StreakCount = 30, StreakPoints = 12;
    private readonly Vector3[] streakPoints = new Vector3[StreakPoints];

    public bool Recording => recording;
    public bool Rewinding => rewinding;
    public int Beads => Mathf.FloorToInt(beadCharge + 0.0001f);
    /// <summary>Length of the thread paid out so far (m).</summary>
    public float ThreadLength => lengths.Count > 0 ? lengths[lengths.Count - 1] : 0f;
    /// <summary>Where the mark was made (remembered body centre), for tests.</summary>
    public Vector3 MarkPosition => samples.Count > 0 ? samples[0].position : transform.position;

    public override bool CanUseWhileCarried => !IsDead;
    public override bool CanBeCaptured => !rewinding;
    public override string CarriedPrompt => IsDead || rewinding ? null : !recording ? "[LMB] Mark" : Beads >= 1 ? "[LMB] Rewind" : null;
    public override string InteractionPrompt => IsDead || IsCarried || rewinding ? null : recording ? "[LMB] Rewind" : "[LMB] Mark";

    // ---------------- Building ----------------

    protected override void InitializeCreature()
    {
        SetCreatureName("Rewind Coil");
        body = GetComponent<Rigidbody>();
        body.mass = 0.6f;
        body.linearDamping = 0.6f;
        body.angularDamping = 2f;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        bodyCollider = GetComponent<SphereCollider>();
        if (bodyCollider == null) bodyCollider = gameObject.AddComponent<SphereCollider>();
        if (look == null) Build();
        if (Application.isPlaying) beadCharge = Mathf.Min(startBeads, maxBeads);
    }

    // Shell spiral: radius a * e^(b * theta), from theta0 to theta1, in the xy plane.
    // Not a true shell: the last whorl peels away from the coil, the whole coil leans out of its plane, the tube
    // swells and pinches into beads of chambers and the mouth flares into a bell.
    private const float SpiralA = 0.055f, SpiralB = 0.19f, Theta0 = 0.35f * Mathf.PI, Theta1 = 4f * Mathf.PI;
    private static float BaseR(float theta) => SpiralA * Mathf.Exp(SpiralB * theta);
    private static float Uncoil(float theta) { float u = Mathf.Clamp01((theta - 3.1f * Mathf.PI) / (0.9f * Mathf.PI)); return u * u; }
    private static float SpiralR(float theta) => BaseR(theta) * (1f + 0.75f * Uncoil(theta));
    private static float TubeR(float theta)
    {
        float flare = Mathf.Clamp01((theta - (Theta1 - 0.7f)) / 0.7f);
        return BaseR(theta) * 0.55f * (1f + 0.24f * Mathf.Sin(theta * 3.3f)) * (1f + 1.1f * flare * flare);
    }
    private static Vector3 SpiralPoint(float theta) =>
        new Vector3(Mathf.Cos(theta), Mathf.Sin(theta), 0f) * SpiralR(theta)
        + Vector3.forward * BaseR(theta) * (0.35f * Mathf.Sin(theta * 0.9f) + 0.9f * Uncoil(theta));

    private void Build()
    {
        Shader comb = Shader.Find("Capstone/Landmarks/BoneComb");
        Shader marble = Shader.Find("Capstone/Landmarks/BoneMarble");
        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        Shader sprites = Shader.Find("Sprites/Default") ?? unlit;

        glassMaterial = new Material(comb != null ? comb : lit) { name = "Coil glass" };
        glassMaterial.SetColor("_Body", new Color(0.08f, 0.35f, 0.95f, 0.25f));
        glassMaterial.SetColor("_Rim", new Color(0.55f, 0.95f, 1f, 1f));
        glassMaterial.SetColor("_Core", new Color(0.75f, 0.95f, 1f, 1f));
        glassMaterial.SetFloat("_CombRows", 48f);
        glassMaterial.SetColor("_GlowColor", new Color(0.2f, 0.75f, 1f, 1f));
        boneMaterial = new Material(marble != null ? marble : lit) { name = "Coil bone" };
        if (marble != null)
        {
            boneMaterial.SetColor("_ColorA", new Color(0.9f, 0.93f, 0.97f));
            boneMaterial.SetColor("_ColorB", new Color(0.03f, 0.04f, 0.08f));
            boneMaterial.SetColor("_ColorMid", new Color(0.35f, 0.5f, 0.75f));
            boneMaterial.SetFloat("_Scale", 2.2f);
            boneMaterial.SetFloat("_Flow", 0f);
            boneMaterial.SetColor("_GlowColor", new Color(0.2f, 0.7f, 1f));
        }
        beadMaterial = new Material(unlit) { name = "Coil bead" };
        tickMaterial = new Material(unlit) { name = "Coil tick" };
        SetColor(tickMaterial, new Color(0.05f, 0.12f, 0.3f));
        tickLitMaterial = new Material(unlit) { name = "Coil tick lit" };
        SetColor(tickLitMaterial, new Color(0.6f, 2.2f, 3.2f));
        ghostMaterial = new Material(sprites) { name = "Coil echo" };
        ghostBoneMaterial = new Material(sprites) { name = "Coil echo bone" };
        lineMaterial = new Material(sprites) { name = "Coil thread" };

        look = new GameObject("Coil Look").transform;
        look.SetParent(transform, false);
        shell = new GameObject("Shell").transform;
        shell.SetParent(look, false);

        shellMesh = BuildShellMesh();
        ribMesh = BuildRibMesh();
        Part("Glass", shellMesh, glassMaterial, shell);
        Part("Ribs", ribMesh, boneMaterial, shell);

        // Mouth of the shell, and its opening direction.
        mouthLocal = SpiralPoint(Theta1);
        mouthTangent = (SpiralPoint(Theta1 + 0.01f) - SpiralPoint(Theta1 - 0.01f)).normalized;
        // Opening forward and a little down (+x here becomes the creature's +z).
        Quaternion align = Quaternion.Euler(0f, -90f, 0f) * Quaternion.FromToRotation(mouthTangent, new Vector3(1f, -0.6f, 0f).normalized);
        shell.localRotation = align;
        shell.localScale = Vector3.one * size;
        Bounds b = shellMesh.bounds;
        Vector3 centre = align * (b.center * size);
        look.localPosition = -centre;
        float radius = Mathf.Max(b.extents.x, b.extents.y) * size * 0.85f;
        bodyCollider.radius = radius;
        bodyCollider.center = Vector3.zero;

        // Beads in three inner chambers, seen through the glass.
        float[] chambers = { 2.3f * Mathf.PI, 2.95f * Mathf.PI, 3.5f * Mathf.PI, 1.8f * Mathf.PI, 3.85f * Mathf.PI };
        for (int i = 0; i < maxBeads; i++)
        {
            var bead = VisualPrimitive.Create(PrimitiveType.Sphere);
            bead.name = "Bead " + i;
            bead.transform.SetParent(shell, false);
            float theta = chambers[i % chambers.Length];
            bead.transform.localPosition = SpiralPoint(theta);
            bead.transform.localScale = Vector3.one * TubeR(theta) * 1.05f;
            var r = bead.GetComponent<Renderer>();
            var m = new Material(beadMaterial) { name = "Coil bead " + i };
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beads.Add(r);
            beadMaterials.Add(m);
        }

        // Twelve ticks round the whorl, a clock face that counts while it remembers.
        float tickRadius = BaseR(Theta1) * 1.55f + 0.16f;
        Transform clock = new GameObject("Clock").transform;
        clock.SetParent(shell, false);
        clock.localRotation = Quaternion.Euler(22f, -14f, 0f);
        for (int i = 0; i < 12; i++)
        {
            var tick = VisualPrimitive.Create(PrimitiveType.Sphere);
            tick.name = "Tick " + i;
            tick.transform.SetParent(clock, false);
            float a = Mathf.PI * 0.5f - i / 12f * Mathf.PI * 2f;
            tick.transform.localPosition = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * tickRadius;
            tick.transform.localScale = Vector3.one * (i % 3 == 0 ? 0.1f : 0.065f);
            var r = tick.GetComponent<Renderer>();
            r.sharedMaterial = tickMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ticks.Add(r);
        }

        // Feelers out of the mouth.
        Mesh feeler = Lathe(new[] { new Vector2(0.06f, 0f), new Vector2(0.045f, 0.4f), new Vector2(0.025f, 0.8f), new Vector2(0f, 1f) }, 6);
        Vector3 radial = new Vector3(Mathf.Cos(Theta1), Mathf.Sin(Theta1), 0f);
        for (int i = 0; i < 4; i++)
        {
            float a = (i + 0.5f) / 4f * Mathf.PI * 2f;
            Vector3 offset = (radial * Mathf.Cos(a) + Vector3.forward * Mathf.Sin(a)) * TubeR(Theta1) * 0.45f;
            Transform f = Part("Feeler " + i, feeler, boneMaterial, shell);
            f.localPosition = mouthLocal + offset;
            f.localScale = new Vector3(0.6f, 0.6f, 0.55f + 0.12f * (i % 2));
            f.localRotation = Quaternion.LookRotation(mouthTangent + offset.normalized * 0.5f, Vector3.forward);
            feelers.Add(f);
            feelerRotations.Add(f.localRotation);
        }

        var lineGo = new GameObject("Thread");
        lineGo.transform.SetParent(transform, false);
        thread = lineGo.AddComponent<LineRenderer>();
        thread.useWorldSpace = true;
        thread.sharedMaterial = lineMaterial;
        thread.widthMultiplier = 0.035f;
        thread.numCapVertices = 2;
        thread.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        thread.receiveShadows = false;
        thread.positionCount = 0;
        thread.enabled = false;
    }

    private static void SetColor(Material material, Color color)
    {
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
    }

    private Transform Part(string name, Mesh mesh, Material material, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    /// <summary>A tube swept along the log spiral, growing wider towards the open mouth.</summary>
    private Mesh BuildShellMesh()
    {
        const int steps = 150, sides = 14;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
        for (int i = 0; i <= steps; i++)
        {
            float theta = Mathf.Lerp(Theta0, Theta1, i / (float)steps);
            Vector3 c = SpiralPoint(theta);
            Vector3 u = new Vector3(Mathf.Cos(theta), Mathf.Sin(theta), 0f);
            float tr = TubeR(theta);
            for (int s = 0; s <= sides; s++)
            {
                float phi = s / (float)sides * Mathf.PI * 2f;
                float lobe = 1f + 0.18f * Mathf.Cos(3f * phi + theta * 1.7f);
                v.Add(c + (u * Mathf.Cos(phi) + Vector3.forward * Mathf.Sin(phi) * 0.8f) * tr * lobe);
                uv.Add(new Vector2(i / (float)steps, s / (float)sides));
            }
        }
        int row = sides + 1;
        for (int i = 0; i < steps; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = i * row + s, b = a + 1, c = a + row, d = c + 1;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
        return Finish("Coil shell", v, uv, tris);
    }

    /// <summary>Bone septa round the tube every half radian, and a thick lip at the mouth.</summary>
    private Mesh BuildRibMesh()
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tris = new List<int>();
        for (float theta = Theta0 + 0.5f; theta < Theta1 - 0.1f; theta += 0.5f)
            AddRing(v, uv, tris, theta, 1.03f, 0.07f);
        AddRing(v, uv, tris, Theta1, 1.06f, 0.14f);
        return Finish("Coil ribs", v, uv, tris);
    }

    private static void AddRing(List<Vector3> v, List<Vector2> uv, List<int> tris, float theta, float scale, float thickness)
    {
        const int segments = 18, tube = 5;
        Vector3 c = SpiralPoint(theta);
        Vector3 u = new Vector3(Mathf.Cos(theta), Mathf.Sin(theta), 0f);
        Vector3 t = (SpiralPoint(theta + 0.01f) - SpiralPoint(theta - 0.01f)).normalized;
        float tr = TubeR(theta) * scale, th = TubeR(theta) * thickness;
        int start = v.Count;
        for (int s = 0; s <= segments; s++)
        {
            float phi = s / (float)segments * Mathf.PI * 2f;
            Vector3 dir = u * Mathf.Cos(phi) + Vector3.forward * Mathf.Sin(phi) * 0.8f;
            Vector3 centre = c + dir * tr;
            Vector3 n = dir.normalized;
            for (int k = 0; k <= tube; k++)
            {
                float b = k / (float)tube * Mathf.PI * 2f;
                v.Add(centre + (n * Mathf.Cos(b) + t * Mathf.Sin(b)) * th);
                uv.Add(new Vector2(s / (float)segments, k / (float)tube));
            }
        }
        int row = tube + 1;
        for (int s = 0; s < segments; s++)
            for (int k = 0; k < tube; k++)
            {
                int a = start + s * row + k, b = a + 1, c2 = a + row, d = c2 + 1;
                tris.AddRange(new[] { a, c2, b, b, c2, d });
            }
    }

    private Mesh Lathe(Vector2[] profile, int sides)
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
        return Finish("Coil feeler", v, uv, tris);
    }

    private Mesh Finish(string name, List<Vector3> v, List<Vector2> uv, List<int> tris)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        meshes.Add(mesh);
        return mesh;
    }

    // ---------------- Living ----------------

    protected override void TickCreature(float deltaTime)
    {
        cooldown = Mathf.Max(0f, cooldown - deltaTime);
        if (beadCharge < maxBeads) beadCharge = Mathf.Min(maxBeads, beadCharge + deltaTime / beadRegrowSeconds);
        if (rewinding) StepRewind(deltaTime);
        else if (recording)
        {
            Record(deltaTime);
            // Left behind and tied to the player: the middle mouse button rewinds from anywhere.
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (recording && !IsCarried && tied == this && tetherBody != null && mouse != null && mouse.middleButton.wasPressedThisFrame
                && (Application.isFocused || Application.isBatchMode) && !MeditationController.BlocksPlayerInput) Rewind();
        }
        Restless(deltaTime);
        Animate(deltaTime);
        DrawThread();
        UpdateStreaks();
    }

    // ---------------- Habit ----------------

    [Header("Habit")]
    [Tooltip("Left alone on the ground, it now and then marks its own spot, darts about in a few hops and reels itself back.")]
    [SerializeField] private bool restless = true;
    [Tooltip("Seconds of rest between two fits (random in this range).")]
    [SerializeField] private Vector2 restSeconds = new Vector2(9f, 22f);
    [Tooltip("Hops in one fit (random in this range) and their speed (m/s, random in this range).")]
    [SerializeField] private Vector2Int fitHops = new Vector2Int(3, 5);
    [SerializeField] private Vector2 hopSpeed = new Vector2(4f, 6.5f);
    [Tooltip("It only does this with the camera this near (m).")]
    [Min(1f)] [SerializeField] private float restlessRange = 60f;

    private bool selfFit;
    private float restLeft = -1f, nextHop, fitTime;
    private int hopsLeft;
    private Vector3 lastHop;
    /// <summary>True while it is darting about on its own thread (for tests).</summary>
    public bool Fitting => selfFit;

    protected override void OnCarryStarted()
    {
        // Picked up in the middle of its own fit: the fit ends, its thread is dropped.
        if (selfFit && recording && !rewinding) StopRecording();
    }

    private bool OnGround()
    {
        int count = Physics.RaycastNonAlloc(transform.position, Vector3.down, rayHits, bodyCollider.radius + 0.2f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (!rayHits[i].collider.transform.IsChildOf(transform)) return true;
        return false;
    }

    /// <summary>Its own use of the thread: mark, a few quick hops this way and that, then back along the thread.</summary>
    private void Restless(float deltaTime)
    {
        if (!restless || IsCarried || rewinding || !groundFound || body.isKinematic) return;
        if (restLeft < 0f) restLeft = Random.Range(restSeconds.x, restSeconds.y) * Random.Range(0.3f, 1f);
        if (!selfFit)
        {
            if (recording) return; // the player's thread comes first
            restLeft -= deltaTime;
            if (restLeft > 0f) return;
            Camera view = Camera.main;
            if (view == null || (view.transform.position - transform.position).sqrMagnitude > restlessRange * restlessRange || !OnGround())
            { restLeft = 2f; return; }
            Mark();
            if (!recording) { restLeft = 1f; return; }
            selfFit = true;
            hopsLeft = Random.Range(fitHops.x, fitHops.y + 1);
            nextHop = 0.45f; // a moment of stillness while the echo forms
            fitTime = 0f;
            lastHop = Vector3.zero;
            return;
        }
        fitTime += deltaTime;
        nextHop -= deltaTime;
        // Turn towards the way it is going, a little behind the motion.
        Vector3 flat = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        if (flat.sqrMagnitude > 0.4f)
            FaceCreature(Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat.normalized, Vector3.up), 1f - Mathf.Exp(-deltaTime * 5f)));
        if (fitTime > 9f) { Rewind(); return; }
        if (nextHop > 0f || !OnGround()) return;
        if (hopsLeft <= 0) { Rewind(); return; }
        // Each hop turns sharply away from the last, so it zigzags instead of running off in a line.
        Vector3 direction = lastHop == Vector3.zero
            ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward
            : Quaternion.Euler(0f, (Random.value < 0.5f ? -1f : 1f) * Random.Range(40f, 140f), 0f) * lastHop;
        lastHop = direction;
        body.linearVelocity = direction * Random.Range(hopSpeed.x, hopSpeed.y) + Vector3.up * Random.Range(2.4f, 3.6f);
        hopsLeft--;
        nextHop = Random.Range(0.3f, 0.6f);
    }

    private void FixedUpdate()
    {
        if (!SimulationEnabled || body == null) return;
        HoldUntilGround();
        Absorb(Time.fixedDeltaTime);
    }

    // Placed in a scene, it hangs still until the generated ground under it exists (works inside caves too,
    // unlike a ray from high above), then drops. Left to SettleWhenGroundReady when that is attached.
    private bool groundFound;
    private void HoldUntilGround()
    {
        if (groundFound) return;
        if (IsCarried || rewinding || TryGetComponent<SettleWhenGroundReady>(out _)) { groundFound = true; return; }
        bool found = false;
        int count = Physics.RaycastNonAlloc(transform.position, Vector3.down, rayHits, 6f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count && !found; i++) found = !rayHits[i].collider.transform.IsChildOf(transform);
        body.isKinematic = !found;
        groundFound = found;
    }
    private readonly RaycastHit[] rayHits = new RaycastHit[16];

    /// <summary>Drinks the heat of fire touching the shell; the heat freezes into beads.</summary>
    private void Absorb(float deltaTime)
    {
        if (beadCharge >= maxBeads) return;
        int count = ReflectionPhysics.OverlapSphereNonAlloc(transform.position, bodyCollider.radius + absorbReach, hits, ~0, QueryTriggerInteraction.Collide);
        seen.Clear();
        for (int i = 0; i < count; i++)
        {
            Creature other = hits[i].GetComponentInParent<Creature>();
            if (other == null || other == this || !seen.Add(other) || other.Heat <= 0f || other.IsDead) continue;
            bool hot = other is FireBubble fire && !fire.IsBoundToBone && !BoneChild.IsCradled(fire) && !fire.IsCarried || other is FireSource;
            if (!hot) continue;
            float take = Mathf.Min(other.Heat, absorbPerSecond * deltaTime);
            other.SetHeat(other.Heat - take);
            beadCharge = Mathf.Min(maxBeads, beadCharge + take / heatPerBead);
            warmFlash = 1f;
        }
    }

    // The player the thread is tied to (whoever made the mark). Tied, the thread follows the player whether the
    // coil is held or left behind; only one coil is tied to the player at a time.
    private static RewindCoil tied;
    private SmoothFirstPersonController tetherController;
    private Rigidbody tetherBody;
    public bool TiedToPlayer => recording && tetherBody != null;

    /// <summary>The body centre the thread remembers: the tied player's, else its own (lifted) on the ground.</summary>
    private Vector3 RememberedPoint() => tetherBody != null ? tetherBody.position : transform.position + Vector3.up * playerLift;

    /// <summary>Pops the bubble holding this object (a leashed bubble just lets go), so a rewind is never fought over.</summary>
    private static void FreeFromBubble(GameObject target)
    {
        if (target == null || !target.TryGetComponent<BubbleCapture>(out var capture) || !capture.IsCaptured) return;
        capture.OwningBubble.Pop(false);
        if (capture.IsCaptured) capture.Release();
    }

    private void CurrentLook(out float yaw, out float pitch)
    {
        var player = tetherController;
        if (player != null) { yaw = player.LookYaw; pitch = player.LookPitch; return; }
        Sample last = samples.Count > 0 ? samples[samples.Count - 1] : default;
        yaw = last.yaw; pitch = last.pitch;
    }

    private void AddSample(Vector3 point)
    {
        CurrentLook(out float yaw, out float pitch);
        float length = samples.Count > 0 ? lengths[lengths.Count - 1] + Vector3.Distance(samples[samples.Count - 1].position, point) : 0f;
        samples.Add(new Sample { position = point, yaw = yaw, pitch = pitch });
        lengths.Add(length);
        lastRecorded = point;
        if (samples.Count > maxSamples) Thin();
    }

    // Drops every other point (keeping both ends) and recounts the lengths.
    private void Thin()
    {
        var kept = new List<Sample>();
        for (int i = 0; i < samples.Count; i++) if (i % 2 == 0 || i == samples.Count - 1) kept.Add(samples[i]);
        samples.Clear(); samples.AddRange(kept);
        lengths.Clear();
        for (int i = 0; i < samples.Count; i++)
            lengths.Add(i == 0 ? 0f : lengths[i - 1] + Vector3.Distance(samples[i - 1].position, samples[i].position));
    }

    private void Record(float deltaTime)
    {
        recordedSeconds += deltaTime;
        Vector3 point = RememberedPoint();
        float moved = Vector3.Distance(point, lastRecorded);
        if (moved > breakJump) { StopRecording(); return; }
        if (moved >= sampleSpacing) AddSample(point);
    }

    /// <summary>Marks this moment: sheds the echo here and starts paying out the thread.</summary>
    public void Mark(GameObject source = null)
    {
        if (IsDead || rewinding || cooldown > 0f) return;
        samples.Clear(); lengths.Clear();
        // Tie the thread to the player who touched it (or who holds it).
        tetherController = source != null ? source.GetComponentInParent<SmoothFirstPersonController>() : null;
        if (tetherController == null && IsCarried && Carrier != null) tetherController = Carrier.PlayerController;
        tetherBody = tetherController != null ? tetherController.Body : null;
        if (tetherBody != null)
        {
            if (tied != null && tied != this) tied.StopRecording();
            tied = this;
        }
        recordedSeconds = 0f;
        shownHand = 0f;
        AddSample(RememberedPoint());
        recording = true;
        PlaceEcho();
        cooldown = 0.35f;
    }

    private void StopRecording()
    {
        recording = false;
        if (selfFit) { selfFit = false; restLeft = Random.Range(restSeconds.x, restSeconds.y); }
        if (tied == this) tied = null;
        tetherController = null; tetherBody = null;
        samples.Clear(); lengths.Clear();
    }

    /// <summary>Rewinds along the thread: the player when carried (and a bead is left), otherwise the coil alone.</summary>
    public bool Rewind()
    {
        if (IsDead || rewinding || !recording || cooldown > 0f) return false;
        AddFinalSample();
        if (tetherBody != null && tetherController != null)
        {
            if (Beads < 1) { dryShudder = 0.6f; return false; }
            playerBody = tetherBody;
            controller = tetherController;
            if (BoneRide.Busy || controller.FishingInputOwner != null ||
                (RestSpotSystem.Instance != null && RestSpotSystem.Instance.Sitting)) { dryShudder = 0.6f; return false; }
            // Rewinding from inside a bubble bursts out of it first.
            FreeFromBubble(playerBody.gameObject);
            controller.GetComponent<AdvancedFirstPersonTraversal>()?.StopForBubbleCapture();
            foreach (HandClimber climber in FindObjectsByType<HandClimber>(FindObjectsSortMode.None))
                if (climber.Controls(controller)) climber.StopForBubbleCapture();
            savedKinematic = playerBody.isKinematic;
            savedRestricted = controller.Restricted;
            if (!playerBody.isKinematic) { playerBody.linearVelocity = Vector3.zero; playerBody.angularVelocity = Vector3.zero; }
            playerBody.isKinematic = true;
            controller.SetRestricted(true);
            rewindPlayer = true;
            beadCharge -= 1f;
        }
        else
        {
            coilWasKinematic = body.isKinematic;
            body.isKinematic = true;
            rewindPlayer = false;
        }
        rewinding = true;
        rewindTime = 0f;
        rewindFrom = ThreadLength;
        rewindDuration = Mathf.Clamp(rewindFrom / rewindSpeed, rewindSeconds.x, rewindSeconds.y);
        cooldown = 0.35f;
        return true;
    }

    // Adds the very last point so the rewind starts exactly where we are.
    private void AddFinalSample()
    {
        Vector3 point = RememberedPoint();
        float moved = Vector3.Distance(point, lastRecorded);
        if (samples.Count > 0 && moved > 0.02f && moved < breakJump) AddSample(point);
    }

    /// <summary>Point and look along the thread at distance s from the mark.</summary>
    private Sample Along(float s, out Vector3 tangent)
    {
        tangent = Vector3.forward;
        if (samples.Count == 0) return default;
        if (samples.Count == 1 || s <= 0f) { tangent = samples.Count > 1 ? samples[1].position - samples[0].position : tangent; return samples[0]; }
        int hi = lengths.BinarySearch(s);
        if (hi < 0) hi = ~hi;
        hi = Mathf.Clamp(hi, 1, samples.Count - 1);
        int lo = hi - 1;
        float span = Mathf.Max(1e-5f, lengths[hi] - lengths[lo]);
        float t = Mathf.Clamp01((s - lengths[lo]) / span);
        Sample a = samples[lo], b = samples[hi];
        tangent = b.position - a.position;
        return new Sample
        {
            position = Vector3.Lerp(a.position, b.position, t),
            yaw = Mathf.LerpAngle(a.yaw, b.yaw, t),
            pitch = Mathf.LerpAngle(a.pitch, b.pitch, t)
        };
    }

    private float HeadDistance => rewinding ? rewindFrom * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(rewindTime / rewindDuration))) : ThreadLength;

    private void StepRewind(float deltaTime)
    {
        if (rewindPlayer ? playerBody == null || controller == null : IsCarried) { EndRewind(); return; }
        // A bubble met on the way back must not keep the player (its capture would fight the rewind and leave
        // the body frozen afterwards): it pops.
        if (rewindPlayer) FreeFromBubble(playerBody.gameObject);
        rewindTime += deltaTime;
        float s = HeadDistance;
        Sample at = Along(s, out Vector3 tangent);
        if (rewindPlayer)
        {
            playerBody.position = at.position;
            playerBody.transform.position = at.position;
            controller.SuppressJumpForCurrentFrame();
            // The view turns back the way it looked then, a little behind (never snapping).
            float follow = 1f - Mathf.Exp(-deltaTime * 6f);
            controller.SetLook(Mathf.LerpAngle(controller.LookYaw, at.yaw, follow), Mathf.LerpAngle(controller.LookPitch, at.pitch, follow));
        }
        else
        {
            Vector3 target = at.position - Vector3.up * playerLift;
            body.position = target;
            transform.position = target;
        }
        if (rewindTime >= rewindDuration) EndRewind();
    }

    private void EndRewind()
    {
        if (!rewinding) return;
        rewinding = false;
        if (rewindPlayer)
        {
            if (playerBody != null)
            {
                FreeFromBubble(playerBody.gameObject);
                if (samples.Count > 0)
                {
                    Vector3 home = FreeSpot(samples[0].position);
                    playerBody.position = home; playerBody.transform.position = home;
                }
                playerBody.isKinematic = savedKinematic;
                if (!playerBody.isKinematic) { playerBody.linearVelocity = Vector3.zero; playerBody.angularVelocity = Vector3.zero; }
            }
            if (controller != null)
            {
                controller.SetRestricted(savedRestricted);
                if (samples.Count > 0) controller.SetLook(samples[0].yaw, samples[0].pitch);
            }
        }
        else if (!IsCarried)
        {
            body.isKinematic = coilWasKinematic;
            if (!body.isKinematic) body.linearVelocity = Vector3.zero;
        }
        playerBody = null; controller = null;
        StopRecording();
        cooldown = 0.5f;
    }

    /// <summary>The player's body at point, lifted out of anything solid it would stand in.</summary>
    private Vector3 FreeSpot(Vector3 point)
    {
        Collider capsule = playerBody.GetComponent<Collider>();
        Vector3 extents = capsule != null ? capsule.bounds.extents : new Vector3(0.35f, 0.95f, 0.35f);
        Vector3 offset = capsule != null ? capsule.bounds.center - playerBody.position : Vector3.zero;
        float radius = Mathf.Max(0.1f, Mathf.Min(extents.x, extents.z) - 0.03f);
        Vector3 half = Vector3.up * Mathf.Max(0f, extents.y - radius - 0.03f);
        for (int step = 0; step < 12; step++)
        {
            Vector3 c = point + offset + Vector3.up * (step * 0.3f);
            int count = Physics.OverlapCapsuleNonAlloc(c + half, c - half, radius, hits, ~0, QueryTriggerInteraction.Ignore);
            bool blocked = false;
            for (int i = 0; i < count && !blocked; i++)
                blocked = hits[i].attachedRigidbody != playerBody && !hits[i].transform.IsChildOf(playerBody.transform) && hits[i].GetComponentInParent<Creature>() == null;
            if (!blocked) return point + Vector3.up * (step * 0.3f);
        }
        return point;
    }

    // ---------------- Echo, thread and speed lines ----------------

    private void PlaceEcho()
    {
        if (echo == null)
        {
            echo = new GameObject("Rewind Coil Echo").transform;
            Part("Echo glass", shellMesh, ghostMaterial, echo);
            Part("Echo ribs", ribMesh, ghostBoneMaterial, echo);
        }
        echo.SetParent(null, true);
        echo.position = shell.position;
        echo.rotation = shell.rotation;
        echo.localScale = shell.lossyScale;
        echo.gameObject.SetActive(true);
        echoAlpha = 0f;
    }

    private void DrawThread()
    {
        if (thread == null) return;
        bool show = (recording || rewinding) && samples.Count > 0;
        if (!show) { if (thread.enabled) thread.enabled = false; return; }
        float head = HeadDistance;
        int end = samples.Count - 1;
        while (end > 0 && lengths[end] > head) end--;
        int stride = Mathf.Max(1, (end + 1) / 240);
        var points = new List<Vector3>(end / stride + 4);
        if (echo != null && echo.gameObject.activeSelf) points.Add(echo.position);
        Vector3 drop = Vector3.up * threadDrop;
        for (int i = 0; i <= end; i += stride) points.Add(samples[i].position - drop);
        if (rewinding) points.Add(Along(head, out _).position - drop);
        if (IsCarried || tetherBody == null) points.Add(shell.TransformPoint(mouthLocal));
        else
        {
            // Left behind: the shell still holds the far end, the near end stays on the player's hip.
            points.Insert(0, shell.TransformPoint(mouthLocal));
            if (!rewinding) points.Add(tetherBody.position - drop);
        }
        thread.positionCount = points.Count;
        thread.SetPositions(points.ToArray());
        thread.enabled = true;
        // A brighter pulse runs along it from the echo towards the shell (and back while rewinding).
        float pulse = Mathf.Repeat(Time.time * (rewinding ? -1.6f : 0.35f), 1f);
        var gradient = new Gradient();
        Color dim = new Color(0.25f, 0.6f, 1f), bright = new Color(0.85f, 1f, 1f);
        gradient.SetKeys(
            new[] { new GradientColorKey(bright, 0f), new GradientColorKey(dim, Mathf.Max(0.01f, pulse - 0.08f)),
                    new GradientColorKey(bright, pulse), new GradientColorKey(dim, Mathf.Min(0.99f, pulse + 0.08f)), new GradientColorKey(dim, 1f) },
            new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(rewinding ? 0.95f : 0.55f, 0.5f), new GradientAlphaKey(0.9f, 1f) });
        thread.colorGradient = gradient;
        thread.widthMultiplier = rewinding ? 0.05f : 0.03f;
    }

    // Speed lines while the player is reeled back: thin streaks that run along the thread on orbits round it,
    // streaming past the player and drawing together towards the mark ahead.
    private void UpdateStreaks()
    {
        bool show = rewinding && rewindPlayer && samples.Count > 1;
        if (!show)
        {
            foreach (Streak streak in streaks) if (streak.line.enabled) streak.line.enabled = false;
            return;
        }
        if (streaks.Count == 0)
            for (int i = 0; i < StreakCount; i++)
            {
                var go = new GameObject("Rewind Streak");
                go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = StreakPoints;
                line.sharedMaterial = lineMaterial;
                line.numCapVertices = 2;
                line.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.2f);
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                float h = Mathf.Repeat(i * 0.618034f, 1f); // spread evenly without a visible pattern
                streaks.Add(new Streak
                {
                    line = line, angle = i * 137.5f * Mathf.Deg2Rad, radius = Mathf.Lerp(0.6f, 1.9f, h),
                    phase = Mathf.Repeat(i * 0.377f, 1f), speed = Mathf.Lerp(0.9f, 1.7f, Mathf.Repeat(i * 0.271f, 1f)),
                    length = Mathf.Lerp(4f, 9f, Mathf.Repeat(i * 0.731f, 1f)), twist = (i % 2 == 0 ? 1f : -1f) * Mathf.Lerp(0.05f, 0.14f, h)
                });
            }
        float head = HeadDistance;
        float progress = Mathf.Clamp01(rewindTime / rewindDuration);
        float strength = Mathf.Clamp01(Mathf.Sin(progress * Mathf.PI) * 2.2f); // in and out, never popping
        const float behind = 4f, ahead = 16f;
        foreach (Streak streak in streaks)
        {
            // The streak's front runs from behind the player to far ahead (towards the mark), over and over.
            float run = Mathf.Repeat(streak.phase + Time.time * streak.speed, 1f);
            float front = behind - run * (behind + ahead);
            for (int k = 0; k < StreakPoints; k++)
            {
                float rel = front + streak.length * k / (StreakPoints - 1f); // metres behind the player (negative = ahead)
                Sample at = Along(Mathf.Clamp(head + rel, 0f, rewindFrom), out Vector3 tangent);
                if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.forward;
                tangent.Normalize();
                Vector3 u = Vector3.Cross(tangent, Mathf.Abs(tangent.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                Vector3 w = Vector3.Cross(tangent, u);
                float a = streak.angle + rel * streak.twist + Time.time * 0.35f;
                // Orbits narrow towards the mark, so the lines gather ahead.
                float r = streak.radius * Mathf.Lerp(1f, 0.3f, Mathf.Clamp01(-rel / ahead));
                streakPoints[k] = at.position + (u * Mathf.Cos(a) + w * Mathf.Sin(a)) * r;
            }
            streak.line.SetPositions(streakPoints);
            float edge = Mathf.Clamp01(Mathf.Sin(run * Mathf.PI) * 3f);
            float alpha = strength * edge;
            Color tip = new Color(0.9f, 1f, 1f, alpha), tail = new Color(0.2f, 0.5f, 1f, 0f);
            streak.line.startColor = tip;
            streak.line.endColor = tail;
            streak.line.widthMultiplier = 0.05f;
            streak.line.enabled = alpha > 0.01f;
        }
    }

    // ---------------- Look ----------------

    private void Animate(float deltaTime)
    {
        if (look == null) return;
        float time = Time.time;
        warmFlash = Mathf.MoveTowards(warmFlash, 0f, deltaTime * 1.5f);
        dryShudder = Mathf.MoveTowards(dryShudder, 0f, deltaTime);

        // A slow sway; it trembles while reeling and shudders when it has no bead to give.
        float sway = Mathf.Sin(time * 0.7f + GetInstanceID() * 0.1f) * 4f;
        float tremble = (rewinding ? 2.5f : 0f) * Mathf.Sin(time * 40f) + dryShudder * 6f * Mathf.Sin(time * 50f);
        look.localRotation = Quaternion.Euler(sway * 0.4f, 0f, sway + tremble);

        // Clock hand: one tick a second while it remembers, running back while it rewinds, resting at the top otherwise.
        float hand = rewinding ? recordedSeconds * Mathf.Clamp01(HeadDistance / Mathf.Max(0.01f, rewindFrom)) : recording ? Mathf.Floor(recordedSeconds) : 0f;
        shownHand = Mathf.Lerp(shownHand, hand, 1f - Mathf.Exp(-deltaTime * (rewinding ? 14f : 5f)));
        float handIndex = Mathf.Repeat(shownHand, 12f);
        for (int i = 0; i < ticks.Count; i++)
        {
            float d = Mathf.Abs(Mathf.DeltaAngle(i * 30f, handIndex * 30f)) / 30f;
            bool lit = (recording || rewinding) && d < 0.5f;
            Renderer r = ticks[i];
            Material want = lit ? tickLitMaterial : tickMaterial;
            if (r.sharedMaterial != want) r.sharedMaterial = want;
        }

        // Beads glow by how full each one is.
        for (int i = 0; i < beads.Count; i++)
        {
            float fill = Mathf.Clamp01(beadCharge - i);
            Color c = fill >= 1f ? new Color(0.5f, 1.8f, 3f) * (0.85f + 0.15f * Mathf.Sin(time * 2f + i)) : Color.Lerp(new Color(0.02f, 0.04f, 0.1f), new Color(0.15f, 0.4f, 0.8f), fill);
            if (warmFlash > 0f && fill < 1f) c = Color.Lerp(c, new Color(3f, 1.2f, 0.3f), warmFlash * 0.7f);
            SetColor(beadMaterials[i], c);
        }
        float glow = 0.35f + 0.9f * (rewinding ? 1f : recording ? 0.5f : 0f) + warmFlash;
        glassMaterial.SetColor("_GlowColor", Color.Lerp(new Color(0.2f, 0.75f, 1f), new Color(1f, 0.5f, 0.15f), warmFlash));
        glassMaterial.SetFloat("_GlowStrength", glow);
        boneMaterial.SetFloat("_GlowStrength", glow * 0.4f);

        // Feelers wave slowly (each a little behind the last) and curl in while it reels.
        for (int i = 0; i < feelers.Count; i++)
        {
            float phase = time * 1.1f - i * 0.7f;
            Vector3 dir = mouthTangent + new Vector3(Mathf.Cos(Theta1), Mathf.Sin(Theta1), 0f) * (0.35f * Mathf.Sin(phase) - (rewinding ? 0.9f : 0f))
                          + Vector3.forward * (0.45f * Mathf.Cos(phase * 0.8f + i * 1.6f) * (i % 2 == 0 ? 1f : -1f));
            Quaternion target = Quaternion.LookRotation(dir.normalized, Vector3.forward);
            feelerRotations[i] = Quaternion.Slerp(feelerRotations[i], target, 1f - Mathf.Exp(-deltaTime * 3f));
            feelers[i].localRotation = feelerRotations[i];
        }

        // The echo fades in, turns slowly and is drawn back into the shell at the end of a rewind.
        if (echo != null && echo.gameObject.activeSelf)
        {
            bool alive = recording || rewinding;
            echoAlpha = Mathf.MoveTowards(echoAlpha, alive ? 1f : 0f, deltaTime * (alive ? 1.5f : 3f));
            echo.Rotate(Vector3.up, deltaTime * 12f, Space.World);
            if (!alive) echo.position = Vector3.Lerp(echo.position, shell.position, 1f - Mathf.Exp(-deltaTime * 8f));
            float pulse = 0.75f + 0.25f * Mathf.Sin(time * 2.4f);
            ghostMaterial.color = new Color(0.35f, 0.75f, 1f, 0.35f * echoAlpha * pulse);
            ghostBoneMaterial.color = new Color(0.8f, 0.95f, 1f, 0.6f * echoAlpha * pulse);
            if (!alive && echoAlpha <= 0f) echo.gameObject.SetActive(false);
        }
    }

    // ---------------- Being touched ----------------

    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (IsDead || rewinding) return;
        // Caught in a bubble itself, it cannot think (simulation is off): it frees itself first.
        FreeFromBubble(gameObject);
        // Touched in the middle of its own fit: it drops its own thread and takes the player's mark.
        if (selfFit && recording) { StopRecording(); cooldown = 0f; }
        if (!recording) Mark(interaction.Source);
        else Rewind();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        EndRewind();
    }

    private void OnDrawGizmos()
    {
        if (Application.isPlaying) return;
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, size * 0.9f);
    }

    private void OnDestroy()
    {
        if (echo != null) Destroy(echo.gameObject);
        foreach (Mesh m in meshes) if (m != null) Destroy(m);
        foreach (Material m in beadMaterials) if (m != null) Destroy(m);
        foreach (Material m in new[] { glassMaterial, boneMaterial, beadMaterial, tickMaterial, tickLitMaterial, ghostMaterial, ghostBoneMaterial, lineMaterial })
            if (m != null) Destroy(m);
    }
}
