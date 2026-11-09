using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Cistern Snail: a slow snail with a hollow crystal shell that drinks erosive water. Left to itself it crawls
/// to free droplets nearby and sucks them in, up to its capacity; with nothing to drink it trails after the
/// nearest Ore Colossus at a safe distance. Carried, left click sprays the stored water where the player aims,
/// so a shellful can be brought up a knelt colossus's tail ramp to its ore. Tapped on the ground, it spills a
/// burst around itself. Stored water ages it, the faster the fuller it is, except inside a Geode Larva's aura
/// (StillnessField): beside a larva the snail is a safe cistern.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class CisternSnail : Creature
{
    [Header("Cistern")]
    [Tooltip("Droplets the shell holds.")]
    [Min(1)] [SerializeField] private int capacity = 30;
    [Tooltip("Free droplets this close to the shell are drunk, in metres.")]
    [Min(0.2f)] [SerializeField] private float absorbRadius = 1.2f;
    [Tooltip("Free droplets within this distance draw it over to drink, in metres.")]
    [Min(1f)] [SerializeField] private float seekRadius = 14f;
    [Tooltip("Lifetime spent per second with a full shell (scaled by how full it is), on top of natural ageing.")]
    [Min(0f)] [SerializeField] private float fullAgeCost = 2f;

    [Header("Spray")]
    [Tooltip("Droplets sprayed by one click while carried.")]
    [Min(1)] [SerializeField] private int sprayBurst = 5;
    [Min(0f)] [SerializeField] private float sprayCooldown = 0.3f;
    [Min(0.5f)] [SerializeField] private float spraySpeed = 9f;
    [Range(0f, 0.5f)] [SerializeField] private float spraySpread = 0.08f;
    [Tooltip("Droplets spilled around it when tapped on the ground.")]
    [Min(1)] [SerializeField] private int spillBurst = 8;
    [Tooltip("Seconds after spraying or spilling before it drinks again, so it does not swallow its own spray.")]
    [Min(0f)] [SerializeField] private float drinkPause = 3f;

    [Header("Movement")]
    [Min(0.05f)] [SerializeField] private float crawlSpeed = 0.8f;
    [Tooltip("Degrees per second.")]
    [Min(1f)] [SerializeField] private float turnSpeed = 90f;
    [Tooltip("An Ore Colossus within this distance is followed when there is nothing to drink.")]
    [Min(1f)] [SerializeField] private float followRadius = 45f;
    [Tooltip("How close it trails a colossus, clear of its feet.")]
    [Min(2f)] [SerializeField] private float followDistance = 11f;

    [Header("Look")]
    [SerializeField] private Material shellMaterial;
    [SerializeField] private Material bodyMaterial;
    [ColorUsage(false, true)] [SerializeField] private Color waterGlow = new Color(0.35f, 1.4f, 1.6f);

    [Header("Runtime (Read Only)")]
    [SerializeField] private int stored;

    private Rigidbody body;
    private ErosiveFluidSystem fluid;
    private Transform shell;
    private Renderer shellRenderer;
    private MaterialPropertyBlock block;
    private Material runtimeShell, runtimeBody;
    private readonly List<Mesh> meshes = new List<Mesh>();
    private float nextUse, drinkAfter, absorbTimer, seekTimer, wanderTimer, gulp, crawlPhase;
    private Vector3 goal;
    private bool hasGoal;
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    public int Stored => stored;
    public int Capacity => capacity;
    public float Fill01 => capacity > 0 ? Mathf.Clamp01((float)stored / capacity) : 0f;
    /// <summary>Where water is drunk and sprayed from: the shell's opening.</summary>
    public Vector3 ShellCentre => shell != null ? shell.position : transform.position + Vector3.up * 0.6f;
    /// <summary>Raised after water leaves the shell: (snail, droplets sprayed or spilled).</summary>
    public event System.Action<CisternSnail, int> Released;

    public override bool CanUseWhileCarried => !IsDead;
    public override string InteractionPrompt => IsDead || stored == 0 ? null : IsCarried ? "Spray water" : "Tap the shell: spill water";
    public override string CarriedPrompt => IsDead ? null : stored > 0 ?
        $"Left click: spray water ({stored}/{capacity})" : "Empty: set it down near water to drink";

    protected override void InitializeCreature()
    {
        SetCreatureName("Cistern Snail");
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
        shellRenderer = shell != null ? shell.GetComponent<Renderer>() : null;
        block = new MaterialPropertyBlock();
        stored = Mathf.Clamp(stored, 0, capacity);
        wanderTimer = Random.Range(0f, 3f);
    }

    // ---------------- Look ----------------

    private void BuildVisual()
    {
        Material shellLook = shellMaterial;
        if (shellLook == null) shellLook = runtimeShell = OreColossus.MakeMaterial("Cistern Shell (runtime)", new Color(0.55f, 0.85f, 0.9f), waterGlow * 0.1f);
        Material bodyLook = bodyMaterial;
        if (bodyLook == null) bodyLook = runtimeBody = OreColossus.MakeMaterial("Cistern Snail Body (runtime)", new Color(0.42f, 0.4f, 0.38f), Color.black);
        int seed = GetInstanceID() & 0xffff;
        var visual = new GameObject("Visual").transform;
        visual.SetParent(transform, false);
        // A low foot along +Z, the shell riding on its back and two eye stalks at the front.
        Part("Foot", visual, OreShapes.Limb(1.5f, 0.32f, 0.16f, 7, seed, "Cistern Foot"), bodyLook,
            new Vector3(0f, 0.2f, -0.65f), Quaternion.identity);
        Transform shellPart = Part("Shell", visual, OreShapes.Boulder(new Vector3(0.95f, 0.85f, 1f), seed + 1, "Cistern Shell", 0.12f), shellLook,
            new Vector3(0f, 0.7f, -0.15f), Quaternion.identity);
        Part("Crown", shellPart, OreShapes.ShardCluster(4, 0.55f, seed + 2, "Cistern Crown"), shellLook,
            new Vector3(0f, 0.3f, -0.05f), Quaternion.identity);
        for (int s = -1; s <= 1; s += 2)
            Part(s < 0 ? "Eye L" : "Eye R", visual, OreShapes.Limb(0.45f, 0.05f, 0.035f, 5, seed + 3 + s, "Cistern Eye"), bodyLook,
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
        if (shell != null) shell.localScale = Vector3.one * (1f + 0.12f * Fill01 + 0.06f * gulp);
        if (shellRenderer == null) return;
        float shimmer = 0.9f + 0.1f * Mathf.Sin(Time.time * 2.5f + GetInstanceID());
        shellRenderer.GetPropertyBlock(block);
        block.SetColor(EmissionId, waterGlow * ((0.08f + 0.9f * Fill01) * shimmer + gulp * 0.6f));
        shellRenderer.SetPropertyBlock(block);
    }

    // ---------------- Behaviour ----------------

    protected override void TickCreature(float deltaTime)
    {
        if (fluid == null || fluid.gameObject.scene != gameObject.scene) fluid = FindFluid();
        if (!IsCarried)
        {
            absorbTimer -= deltaTime;
            if (absorbTimer <= 0f) { absorbTimer = 0.1f; Drink(); }
            Crawl(deltaTime);
        }
        // The water it carries wears its life away, unless time stands still around it.
        if (stored > 0 && fullAgeCost > 0f && !StillnessField.Holds(this))
            SetRemainingAge(Age - fullAgeCost * Fill01 * deltaTime);
        UpdateLook(deltaTime);
    }

    private ErosiveFluidSystem FindFluid()
    {
        foreach (ErosiveFluidSystem candidate in FindObjectsByType<ErosiveFluidSystem>(FindObjectsSortMode.None))
            if (candidate.isActiveAndEnabled && candidate.gameObject.scene == gameObject.scene) return candidate;
        return null;
    }

    /// <summary>Drinks every free droplet within reach of the shell, up to capacity. Returns how many.</summary>
    public int Drink()
    {
        if (fluid == null || stored >= capacity || Time.time < drinkAfter) return 0;
        Vector3 mouth = ShellCentre;
        float reach = absorbRadius * absorbRadius;
        int drunk = 0;
        IReadOnlyList<ErosiveFluidDroplet> droplets = fluid.Droplets;
        for (int i = 0; i < droplets.Count && stored < capacity; i++)
        {
            ErosiveFluidDroplet droplet = droplets[i];
            if (droplet == null || (droplet.Position - mouth).sqrMagnitude > reach || !droplet.TryAbsorb()) continue;
            stored++; drunk++;
        }
        if (drunk > 0) gulp = 1f;
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
        if (stored < capacity && Time.time >= drinkAfter && NearestDroplet(here, out Vector3 water)) { goal = water; hasGoal = true; return; }
        OreColossus colossus = NearestColossus(here);
        if (colossus != null)
        {
            Vector3 away = here - colossus.transform.position; away.y = 0f;
            if (away.magnitude > followDistance * 1.3f)
            {
                goal = colossus.transform.position + (away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward) * followDistance;
                hasGoal = true;
                return;
            }
        }
        wanderTimer -= 0.5f;
        if (wanderTimer > 0f && hasGoal) return;
        wanderTimer = Random.Range(6f, 10f);
        Vector2 step = Random.insideUnitCircle * 5f;
        goal = here + new Vector3(step.x, 0f, step.y);
        hasGoal = true;
    }

    private bool NearestDroplet(Vector3 here, out Vector3 point)
    {
        point = here;
        if (fluid == null) return false;
        float best = seekRadius * seekRadius;
        bool found = false;
        foreach (ErosiveFluidDroplet droplet in fluid.Droplets)
        {
            if (droplet == null || droplet.IsCarried || !droplet.isActiveAndEnabled) continue;
            float distance = (droplet.Position - here).sqrMagnitude;
            if (distance >= best) continue;
            best = distance; point = droplet.Position; found = true;
        }
        return found;
    }

    private OreColossus NearestColossus(Vector3 here)
    {
        OreColossus nearest = null;
        float best = followRadius * followRadius;
        foreach (OreColossus colossus in OreColossus.All)
        {
            if (colossus == null || !colossus.Built || colossus.gameObject.scene != gameObject.scene) continue;
            float distance = (colossus.transform.position - here).sqrMagnitude;
            if (distance < best) { best = distance; nearest = colossus; }
        }
        return nearest;
    }

    // ---------------- Releasing water ----------------

    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (IsDead || !SimulationEnabled || IsConsumptionPending || Time.time < nextUse || stored == 0) return;
        if (IsCarried)
        {
            Vector3 aim = interaction.Direction.sqrMagnitude > 1e-6f ? interaction.Direction.normalized : transform.forward;
            Release(aim, sprayBurst, spraySpeed, spraySpread);
        }
        else Release(Vector3.up, spillBurst, 4f, 0.7f);
    }

    /// <summary>Sends up to count stored droplets out of the shell along direction. Returns how many left it.</summary>
    public int Release(Vector3 direction, int count, float speed, float spread)
    {
        if (fluid == null) fluid = FindFluid();
        ErosiveFluidSource source = fluid != null ? fluid.GetComponentInChildren<ErosiveFluidSource>() : null;
        if (source == null || stored == 0) return 0;
        direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.up;
        Vector3 mouth = ShellCentre + direction * 0.55f;
        int sent = 0;
        for (int i = 0; i < count && stored > 0; i++)
        {
            Vector3 heading = (direction + Random.insideUnitSphere * spread).normalized;
            Vector3 at = mouth + Random.insideUnitSphere * 0.12f;
            if (!fluid.Spawn(source, at, heading * speed * Random.Range(0.85f, 1.05f))) break;
            stored--; sent++;
        }
        if (sent == 0) return 0;
        nextUse = Time.time + sprayCooldown;
        drinkAfter = Time.time + drinkPause;
        gulp = 0.6f;
        Released?.Invoke(this, sent);
        return sent;
    }

    /// <summary>Debug and validation: sets the shell's contents directly.</summary>
    public void SetStored(int value) => stored = Mathf.Clamp(value, 0, capacity);

    private void OnDestroy()
    {
        foreach (Mesh mesh in meshes) if (mesh != null) Destroy(mesh);
        if (runtimeShell != null) Destroy(runtimeShell);
        if (runtimeBody != null) Destroy(runtimeBody);
    }
}
