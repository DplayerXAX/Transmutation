using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Fire Snail: the heat loop's cousin of the Cistern Snail. A slow snail with a cracked basalt shell that
/// stores Heat. Left to itself it crawls to the nearest fire source (vents included) and sits in its radiation
/// until the shell is full, or slurps up hot lava lying about. The cracks glow brighter the fuller it is.
/// Carried, left click spits a gob of lava where the player aims, paid for with its Heat; tapped on the ground,
/// it wells lava up around itself. Lava (LavaFluid) runs and pools on the ground, burns creatures it touches
/// and feeds fire creatures; it sets into crust and stays a while before it crumbles.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class FireSnail : Creature
{
    [Header("Heat Store")]
    [Tooltip("Heat the shell holds.")]
    [Min(1f)] [SerializeField] private float capacity = 400f;
    [Tooltip("Hot lava this close to the shell is drunk, in metres.")]
    [Min(0.2f)] [SerializeField] private float absorbRadius = 0.9f;
    [Tooltip("Heat gained from drinking one blob of molten lava (scaled by how hot it still is).")]
    [Min(0f)] [SerializeField] private float heatPerDrunkBlob = 6f;
    [Tooltip("A fire source or hot lava within this distance draws it over to feed, in metres.")]
    [Min(1f)] [SerializeField] private float seekRadius = 30f;
    [Tooltip("How close it settles to a fire source to bask (well inside its radiation).")]
    [Min(0.5f)] [SerializeField] private float baskDistance = 3.5f;

    [Header("Lava")]
    [SerializeField] private Material lavaMaterial;
    [Tooltip("Heat one blob of spat lava costs.")]
    [Min(0.1f)] [SerializeField] private float heatPerBlob = 8f;
    [Tooltip("Blobs spat by one click while carried.")]
    [Min(1)] [SerializeField] private int spitBurst = 6;
    [Min(0f)] [SerializeField] private float spitCooldown = 0.35f;
    [Min(0.5f)] [SerializeField] private float spitSpeed = 9f;
    [Range(0f, 0.5f)] [SerializeField] private float spitSpread = 0.035f;
    [Tooltip("Blobs welled up around it when tapped on the ground.")]
    [Min(1)] [SerializeField] private int spillBurst = 10;
    [Tooltip("Seconds after spitting before it drinks lava again, so it does not swallow its own.")]
    [Min(0f)] [SerializeField] private float drinkPause = 4f;

    [Header("Movement")]
    [Min(0.05f)] [SerializeField] private float crawlSpeed = 0.7f;
    [Tooltip("Degrees per second.")]
    [Min(1f)] [SerializeField] private float turnSpeed = 90f;

    [Header("Look")]
    [SerializeField] private Material shellMaterial;
    [SerializeField] private Material bodyMaterial;
    [ColorUsage(false, true)] [SerializeField] private Color heatGlow = new Color(2.4f, 0.6f, 0.08f);

    private Rigidbody body;
    private LavaFluid lava;
    private Transform shell;
    private readonly List<Renderer> glowing = new List<Renderer>();
    private MaterialPropertyBlock block;
    private Material runtimeShell, runtimeBody;
    private readonly List<Mesh> meshes = new List<Mesh>();
    private float nextUse, drinkAfter, absorbTimer, seekTimer, wanderTimer, gulp, crawlPhase;
    private Vector3 goal;
    private bool hasGoal;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    public float Capacity => capacity;
    public float Fill01 => Mathf.Clamp01(Heat / capacity);
    public int BlobsLeft => Mathf.FloorToInt(Heat / heatPerBlob);
    /// <summary>Where lava is drunk and spat from: the shell's opening.</summary>
    public Vector3 ShellCentre => shell != null ? shell.position : transform.position + Vector3.up * 0.6f;

    public override bool CanUseWhileCarried => !IsDead;
    public override string InteractionPrompt => IsDead || BlobsLeft == 0 ? null : IsCarried ? "Spit lava" : "Tap the shell: well up lava";
    public override string CarriedPrompt => IsDead ? null : BlobsLeft > 0 ?
        $"Left click: spit lava ({Mathf.FloorToInt(Heat)}/{Mathf.FloorToInt(capacity)} heat)" : "Cold: set it down by a fire to fill it";

    /// <summary>The shell takes heat only up to its capacity.</summary>
    public override void AddHeat(float amount) => base.AddHeat(Mathf.Min(amount, capacity - Heat));

    protected override void InitializeCreature()
    {
        SetCreatureName("Fire Snail");
        SetFoodAndDiet(FoodTag.None, FoodTag.None);
        SetResolvedPolarity(CreaturePolarity.None);
        body = GetComponent<Rigidbody>();
        body.constraints = RigidbodyConstraints.FreezeRotation;
        if (GetComponent<Collider>() == null)
        {
            var collider = gameObject.AddComponent<SphereCollider>();
            collider.radius = 0.45f;
            collider.center = Vector3.up * 0.45f;
        }
        if (transform.Find("Visual") == null) BuildVisual();
        shell = transform.Find("Visual/Shell");
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
            if (renderer.name == "Shell" || renderer.name == "Crown") glowing.Add(renderer);
        block = new MaterialPropertyBlock();
        if (Heat > capacity) SetHeat(capacity);
        wanderTimer = Random.Range(0f, 3f);
    }

    // ---------------- Look ----------------

    private void BuildVisual()
    {
        Material shellLook = shellMaterial;
        if (shellLook == null) shellLook = runtimeShell = OreColossus.MakeMaterial("Fire Snail Shell (runtime)", new Color(0.13f, 0.09f, 0.08f), heatGlow * 0.05f);
        Material bodyLook = bodyMaterial;
        if (bodyLook == null) bodyLook = runtimeBody = OreColossus.MakeMaterial("Fire Snail Body (runtime)", new Color(0.32f, 0.12f, 0.08f), Color.black);
        int seed = GetInstanceID() & 0xffff;
        var visual = new GameObject("Visual").transform;
        visual.SetParent(transform, false);
        // A low foot along +Z, a lumpy basalt shell with a crown of ember shards, two eye stalks at the front.
        Part("Foot", visual, OreShapes.Limb(1.5f, 0.32f, 0.16f, 7, seed, "Fire Snail Foot"), bodyLook,
            new Vector3(0f, 0.2f, -0.65f), Quaternion.identity);
        Transform shellPart = Part("Shell", visual, OreShapes.Boulder(new Vector3(0.95f, 0.9f, 1f), seed + 1, "Fire Snail Shell", 0.2f), shellLook,
            new Vector3(0f, 0.72f, -0.15f), Quaternion.identity);
        Part("Crown", shellPart, OreShapes.ShardCluster(5, 0.5f, seed + 2, "Fire Snail Crown"), shellLook,
            new Vector3(0f, 0.32f, -0.05f), Quaternion.identity);
        for (int s = -1; s <= 1; s += 2)
            Part(s < 0 ? "Eye L" : "Eye R", visual, OreShapes.Limb(0.45f, 0.05f, 0.035f, 5, seed + 3 + s, "Fire Snail Eye"), bodyLook,
                new Vector3(s * 0.12f, 0.35f, 0.7f), Quaternion.LookRotation(new Vector3(s * 0.35f, 1f, 0.6f)));
    }

    private Transform Part(string name, Transform parent, Mesh mesh, Material material, Vector3 position, Quaternion rotation)
    {
        meshes.Add(mesh);
        var go = new GameObject(name);
        go.layer = gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.SetLocalPositionAndRotation(position, rotation);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        var materials = new Material[mesh.subMeshCount];
        for (int i = 0; i < materials.Length; i++) materials[i] = material;
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        return go.transform;
    }

    private void UpdateLook(float deltaTime)
    {
        gulp = Mathf.MoveTowards(gulp, 0f, deltaTime * 3f);
        if (shell != null) shell.localScale = Vector3.one * (1f + 0.1f * Fill01 + 0.07f * gulp);
        // The cracks smoulder: a slow, uneven flicker, brighter the more heat is stored.
        float flicker = 0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 1.7f, GetInstanceID() * 0.01f);
        Color glow = heatGlow * ((0.03f + 0.95f * Fill01 * Fill01) * flicker + gulp * 0.7f);
        foreach (Renderer renderer in glowing)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(block);
            block.SetColor(EmissionId, glow);
            renderer.SetPropertyBlock(block);
        }
    }

    // ---------------- Behaviour ----------------

    protected override void TickCreature(float deltaTime)
    {
        if (!IsCarried)
        {
            absorbTimer -= deltaTime;
            if (absorbTimer <= 0f) { absorbTimer = 0.25f; DrinkLava(); }
            Crawl(deltaTime);
        }
        UpdateLook(deltaTime);
    }

    private LavaFluid Lava => lava != null ? lava : lava = LavaFluid.For(gameObject.scene, lavaMaterial);

    /// <summary>Drinks hot lava within reach of the shell, as much as there is room for. Returns heat gained.</summary>
    public float DrinkLava()
    {
        if (Time.time < drinkAfter || Heat >= capacity - heatPerDrunkBlob * 0.5f) return 0f;
        int room = Mathf.CeilToInt((capacity - Heat) / Mathf.Max(0.1f, heatPerDrunkBlob));
        float drunk = Lava.Absorb(ShellCentre, absorbRadius, room) * heatPerDrunkBlob;
        if (drunk <= 0f) return 0f;
        AddHeat(drunk);
        gulp = 1f;
        return drunk;
    }

    private void Crawl(float deltaTime)
    {
        if (body == null || body.isKinematic || IsBubbleCaptured) return;
        seekTimer -= deltaTime;
        if (seekTimer <= 0f) { seekTimer = 0.5f; ChooseGoal(); }
        Vector3 to = goal - transform.position; to.y = 0f;
        if (!hasGoal || to.magnitude < 0.4f) { SetCreatureHorizontalVelocity(Vector3.zero); return; }
        Vector3 direction = to.normalized;
        Quaternion facing = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction, Vector3.up), turnSpeed * deltaTime);
        FaceCreature(facing);
        // It only moves the way it faces, and slows to turn.
        float ahead = Mathf.Clamp01(Vector3.Dot(transform.forward, direction));
        crawlPhase += deltaTime * 3f;
        float pulse = 0.75f + 0.25f * Mathf.Sin(crawlPhase);
        SetCreatureHorizontalVelocity(transform.forward * crawlSpeed * ahead * pulse);
    }

    private void ChooseGoal()
    {
        Vector3 here = transform.position;
        bool hungry = Heat < capacity * 0.95f;
        if (hungry && Time.time >= drinkAfter && Lava.NearestHot(here, seekRadius * 0.5f, out Vector3 molten))
        { goal = molten; hasGoal = true; return; }
        if (hungry && NearestFire(here, out Vector3 fire))
        {
            // Bask beside it, not in it.
            Vector3 away = here - fire; away.y = 0f;
            goal = fire + (away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward) * baskDistance;
            hasGoal = true;
            return;
        }
        wanderTimer -= 0.5f;
        if (wanderTimer > 0f && hasGoal) return;
        wanderTimer = Random.Range(6f, 10f);
        Vector2 step = Random.insideUnitCircle * 4f;
        goal = here + new Vector3(step.x, 0f, step.y);
        hasGoal = true;
    }

    private bool NearestFire(Vector3 here, out Vector3 point)
    {
        point = here;
        float best = seekRadius * seekRadius;
        bool found = false;
        foreach (FireSource source in FindObjectsByType<FireSource>(FindObjectsSortMode.None))
        {
            if (source == null || source.IsDead || source.IsCarried || source.Heat <= 0f || source.gameObject.scene != gameObject.scene) continue;
            float distance = (source.transform.position - here).sqrMagnitude;
            if (distance >= best) continue;
            best = distance; point = source.transform.position; found = true;
        }
        return found;
    }

    // ---------------- Lava ----------------

    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (IsDead || !SimulationEnabled || IsConsumptionPending || Time.time < nextUse || BlobsLeft == 0) return;
        if (IsCarried)
        {
            Vector3 aim = interaction.Direction.sqrMagnitude > 1e-6f ? interaction.Direction.normalized : transform.forward;
            Release(aim, spitBurst, spitSpeed, spitSpread);
        }
        else Release(Vector3.up, spillBurst, 3.5f, 0.8f);
    }

    /// <summary>Sends up to count blobs of lava out of the shell along direction, paid in Heat. Returns how many.</summary>
    public int Release(Vector3 direction, int count, float speed, float spread)
    {
        LavaFluid fluid = Lava;
        direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.up;
        // Out of the shell's mouth, clear of the shell and of the player holding it.
        Vector3 mouth = ShellCentre + direction * 0.75f;
        int sent = 0;
        for (int i = 0; i < count && Heat >= heatPerBlob; i++)
        {
            Vector3 heading = (direction + Random.insideUnitSphere * spread).normalized;
            Vector3 at = mouth + Random.insideUnitSphere * 0.1f;
            // Not out into rock (held against a wall): there it would fall through the ground.
            if (Physics.CheckSphere(at, fluid.BlobRadius, (1 << 7) | (1 << 8), QueryTriggerInteraction.Ignore)) continue;
            if (!fluid.Spawn(at, heading * speed * Random.Range(0.94f, 1.02f))) break;
            SpendHeat(heatPerBlob);
            sent++;
        }
        if (sent == 0) return 0;
        nextUse = Time.time + spitCooldown;
        drinkAfter = Time.time + drinkPause;
        gulp = 0.6f;
        return sent;
    }

    private void OnDestroy()
    {
        foreach (Mesh mesh in meshes) if (mesh != null) Destroy(mesh);
        if (runtimeShell != null) Destroy(runtimeShell);
        if (runtimeBody != null) Destroy(runtimeBody);
    }
}
