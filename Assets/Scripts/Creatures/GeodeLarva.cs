using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Geode Larva: a crystal grub that grows inside an Ore Colossus's geode and is released, once, when the
/// colossus kneels. Carried, left click pours its stored life into the creature under the crosshair
/// (adds remaining Age) - the opposite of the TimeSpider, and an answer to water's ageing. Its store
/// refills while it rests near any Ore Colossus. Water, blasts and stomps cannot hurt it.
/// Whenever it is not carried it radiates a stillness aura: creatures inside do not age (StillnessField).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class GeodeLarva : Creature, StillnessField.ISource
{
    [Header("Life Store")]
    [Tooltip("Most life (seconds of Age) it can hold.")]
    [Min(1f)] [SerializeField] private float capacity = 60f;
    [Tooltip("Life given by one click.")]
    [Min(0f)] [SerializeField] private float transferAmount = 15f;
    [Min(0f)] [SerializeField] private float cooldown = 0.75f;
    [Tooltip("Reach of the infusion from the player's eyes, in metres.")]
    [Min(0.5f)] [SerializeField] private float infuseRange = 5f;
    [Tooltip("An infusion never raises a creature's remaining Age above this.")]
    [Min(1f)] [SerializeField] private float maxTargetAge = 600f;

    [Header("Recharge")]
    [Tooltip("Life regained per second while resting near an Ore Colossus.")]
    [Min(0f)] [SerializeField] private float rechargeRate = 1.5f;
    [Min(1f)] [SerializeField] private float rechargeRadius = 14f;
    [Tooltip("Slower than this (m/s) counts as resting.")]
    [Min(0f)] [SerializeField] private float restSpeed = 0.25f;

    [Header("Stillness Aura")]
    [Tooltip("Radius of the aura, in metres; creatures inside do not age while the larva is not carried.")]
    [Min(0.5f)] [SerializeField] private float auraRadius = 6f;
    [Tooltip("Seconds between pulses rippling out to the edge of the aura.")]
    [Min(0.2f)] [SerializeField] private float auraPulseInterval = 2.4f;
    [ColorUsage(false, true)] [SerializeField] private Color auraColor = new Color(0.45f, 1.3f, 1.05f);

    [Header("Look")]
    [SerializeField] private Material glowMaterial;
    [Tooltip("Glow left when the store is empty (share of full).")]
    [Range(0f, 1f)] [SerializeField] private float dimFloor = 0.12f;

    [Header("Runtime (Read Only)")]
    [SerializeField] private float reserve = 60f;
    [SerializeField] private bool nested;

    private Rigidbody body;
    private Renderer[] glowRenderers = new Renderer[0];
    private MaterialPropertyBlock block;
    private Color baseEmission = new Color(0.4f, 1.6f, 1.2f);
    private OreColossus home;
    private float nextUse, flash, beamAge = 1f;
    private LineRenderer beam;
    private Material beamMaterial, runtimeMaterial;
    private Mesh runtimeMesh;
    private Transform auraRoot;
    private Renderer[] auraShells = new Renderer[0];
    private Material auraMaterial;
    private MaterialPropertyBlock auraBlock;
    private float auraBlend;
    private static readonly int AuraFadeId = Shader.PropertyToID("_Fade");
    private static readonly int AuraColorId = Shader.PropertyToID("_Color");
    private static readonly RaycastHit[] hits = new RaycastHit[24];
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    public float Reserve => reserve;
    public float Capacity => capacity;
    public float Reserve01 => Mathf.Clamp01(reserve / capacity);
    public float TransferAmount => transferAmount;
    public bool Nested => nested;
    public OreColossus Home => home;
    public int Infusions { get; private set; }
    public float AuraRadius => auraRadius;
    /// <summary>The aura is up whenever the larva lies free (nested or dropped), not while carried.</summary>
    public bool StillnessActive => !IsDead && !IsCarried && isActiveAndEnabled;
    public Vector3 StillnessCentre => transform.position;
    public float StillnessRadius => auraRadius;
    public UnityEngine.SceneManagement.Scene StillnessScene => gameObject.scene;
    /// <summary>Raised after life is given: (receiver, amount).</summary>
    public event System.Action<Creature, float> Infused;

    public override bool CanUseWhileCarried => !IsDead;
    public override bool Invulnerable => true;
    public override bool CanBeConsumed => false;
    public override string InteractionPrompt => IsDead || !IsCarried || reserve < 1f ? null : "Infuse life";
    public override string CarriedPrompt => IsDead ? null : reserve >= 1f ?
        $"Left click: infuse life ({reserve:0}/{capacity:0})" : "Drained: let it rest near an Ore Colossus";

    protected override void OnEnable() { base.OnEnable(); StillnessField.Register(this); }
    protected override void OnDisable() { StillnessField.Unregister(this); if (auraRoot != null) auraRoot.gameObject.SetActive(false); base.OnDisable(); }

    protected override void InitializeCreature()
    {
        SetCreatureName("Geode Larva");
        SetFoodAndDiet(FoodTag.None, FoodTag.None);
        SetResolvedPolarity(CreaturePolarity.None);
        body = GetComponent<Rigidbody>();
        if (GetComponent<Collider>() == null) gameObject.AddComponent<SphereCollider>().radius = 0.3f;
        if (transform.Find("Visual") == null) BuildVisual();
        glowRenderers = GetComponentsInChildren<Renderer>();
        Material glow = glowRenderers.Length > 0 ? glowRenderers[0].sharedMaterial : null;
        if (glow != null && glow.HasProperty(EmissionId) && glow.GetColor(EmissionId).maxColorComponent > 0.01f) baseEmission = glow.GetColor(EmissionId);
        block = new MaterialPropertyBlock();
        reserve = Mathf.Clamp(reserve, 0f, capacity);
    }

    private void BuildVisual()
    {
        Material material = glowMaterial;
        if (material == null)
        {
            runtimeMaterial = OreColossus.MakeMaterial("Geode Larva (runtime)", new Color(0.55f, 0.95f, 0.85f), baseEmission);
            material = runtimeMaterial;
        }
        runtimeMesh = OreShapes.Larva(GetInstanceID() & 0xffff);
        var visual = new GameObject("Visual");
        visual.transform.SetParent(transform, false);
        visual.layer = gameObject.layer;
        visual.AddComponent<MeshFilter>().sharedMesh = runtimeMesh;
        var renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = runtimeMesh.subMeshCount > 1 ? new[] { material, material } : new[] { material };
        renderer.shadowCastingMode = ShadowCastingMode.On;
    }

    /// <summary>Settles the larva in a colossus's opened geode; it stays put until picked up.</summary>
    internal void Nest(OreColossus colossus, Vector3 at, Quaternion rotation)
    {
        home = colossus;
        nested = true;
        transform.SetPositionAndRotation(at, rotation);
        if (body == null) body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = at; body.rotation = rotation;
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        }
        SetMovementPhysics(true, false);
    }

    protected override void OnCarryStarted()
    {
        if (!nested) return;
        nested = false;
        SetMovementPhysics(false, true);
    }

    protected override void TickCreature(float deltaTime)
    {
        if (!IsCarried && reserve < capacity && NearColossus() && (body == null || body.isKinematic || body.linearVelocity.magnitude < restSpeed))
            reserve = Mathf.Min(capacity, reserve + rechargeRate * deltaTime);
        // Lost below the world: back to its geode.
        if (!IsCarried && home != null && transform.position.y < home.transform.position.y - 150f && home.CradlePoint(out Vector3 cradle))
            Nest(home, cradle, transform.rotation);
        flash = Mathf.MoveTowards(flash, 0f, deltaTime * 2.5f);
        UpdateGlow();
        UpdateBeam(deltaTime);
        UpdateAura(deltaTime);
    }

    // ---------------- Stillness aura ----------------

    /// <summary>A faint shell at the aura's edge and two pulses rippling out to it, kept in world space.</summary>
    private void BuildAura()
    {
        Shader shader = Shader.Find("Capstone/Creature/StillnessAura");
        if (shader == null) return;
        auraMaterial = new Material(shader) { name = "Stillness Aura (runtime)", hideFlags = HideFlags.DontSave };
        auraMaterial.SetColor(AuraColorId, auraColor);
        auraBlock = new MaterialPropertyBlock();
        var probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Mesh sphere = probe.GetComponent<MeshFilter>().sharedMesh;
        Destroy(probe);
        auraRoot = new GameObject("Stillness Aura") { layer = 2 }.transform;
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(auraRoot.gameObject, gameObject.scene);
        auraShells = new Renderer[3];
        for (int i = 0; i < auraShells.Length; i++)
        {
            var shell = new GameObject(i == 0 ? "Edge" : "Pulse " + i) { layer = 2 };
            shell.transform.SetParent(auraRoot, false);
            shell.AddComponent<MeshFilter>().sharedMesh = sphere;
            var renderer = shell.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = auraMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            auraShells[i] = renderer;
        }
    }

    private void UpdateAura(float deltaTime)
    {
        auraBlend = Mathf.MoveTowards(auraBlend, StillnessActive ? 1f : 0f, deltaTime * 2f);
        if (auraRoot == null)
        {
            if (auraBlend <= 0f) return;
            BuildAura();
            if (auraRoot == null) return;
        }
        bool visible = auraBlend > 0f;
        if (auraRoot.gameObject.activeSelf != visible) auraRoot.gameObject.SetActive(visible);
        if (!visible) return;
        auraRoot.SetPositionAndRotation(transform.position, Quaternion.identity);
        float diameter = auraRadius * 2f;
        float breathe = 0.85f + 0.15f * Mathf.Sin(Time.time * 1.3f);
        SetShell(auraShells[0], diameter, 0.35f * breathe * auraBlend);
        for (int k = 1; k < auraShells.Length; k++)
        {
            float t = Mathf.Repeat(Time.time / auraPulseInterval + (k - 1f) / (auraShells.Length - 1f), 1f);
            float grow = 1f - (1f - t) * (1f - t);
            SetShell(auraShells[k], diameter * Mathf.Max(0.05f, grow), Mathf.Pow(1f - t, 1.5f) * 0.8f * auraBlend);
        }
    }

    private void SetShell(Renderer shell, float diameter, float fade)
    {
        shell.transform.localScale = Vector3.one * diameter;
        shell.GetPropertyBlock(auraBlock);
        auraBlock.SetFloat(AuraFadeId, fade);
        shell.SetPropertyBlock(auraBlock);
    }

    private bool NearColossus()
    {
        foreach (OreColossus colossus in OreColossus.All)
            if (colossus != null && colossus.gameObject.scene == gameObject.scene &&
                (colossus.transform.position - transform.position).sqrMagnitude <= rechargeRadius * rechargeRadius) return true;
        return false;
    }

    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (!IsCarried || !SimulationEnabled || IsConsumptionPending) return;
        if (reserve < 0.01f) { flash = 0.3f; return; }
        if (TryFindTarget(interaction, out Creature target)) TryInfuse(target);
    }

    /// <summary>Whether life can be poured into this creature at all.</summary>
    public bool CanInfuse(Creature target) => target != null && target != this && !target.IsDead && !target.IsConsumptionPending &&
        !(target is ErosiveFluidDroplet) && !target.Invulnerable && target.isActiveAndEnabled;

    /// <summary>Gives up to Transfer Amount of stored life to the target. Respects the cooldown.</summary>
    internal bool TryInfuse(Creature target)
    {
        if (IsDead || Time.time < nextUse || !CanInfuse(target)) return false;
        float amount = Mathf.Min(transferAmount, reserve, maxTargetAge - target.Age);
        if (amount <= 0.01f) return false;
        Vector3 to = target.AimPosition;
        // Spend before the receiver's callbacks run.
        reserve -= amount;
        nextUse = Time.time + cooldown;
        target.SetRemainingAge(target.Age + amount);
        Infusions++;
        flash = 1f;
        ShowBeam(to);
        Infused?.Invoke(target, amount);
        return true;
    }

    private bool TryFindTarget(CreatureInteraction interaction, out Creature target)
    {
        target = null;
        Camera eye = Carrier != null && Carrier.PlayerController != null && Carrier.PlayerController.PlayerCamera != null ?
            Carrier.PlayerController.PlayerCamera : Camera.main;
        Vector3 direction = interaction.Direction.sqrMagnitude > 1e-6f ? interaction.Direction.normalized : (eye != null ? eye.transform.forward : transform.forward);
        Vector3 origin = eye != null ? eye.transform.position : AimPosition;
        int count = gameObject.scene.GetPhysicsScene().Raycast(origin, direction, hits, infuseRange + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, 0, count, HitComparer.Instance);
        Rigidbody player = Carrier != null ? Carrier.PlayerBody : null;
        for (int i = 0; i < count; i++)
        {
            Collider collider = hits[i].collider;
            if (collider == null || collider.transform.IsChildOf(transform) || (player != null && collider.attachedRigidbody == player)) continue;
            Creature creature = CreatureSpatialQuery.Owner(collider);
            if (creature is ErosiveFluidDroplet) continue; // look through water
            if (creature == null || !CanInfuse(creature)) return false;
            target = creature;
            return true;
        }
        return false;
    }

    private sealed class HitComparer : System.Collections.Generic.IComparer<RaycastHit>
    {
        public static readonly HitComparer Instance = new HitComparer();
        public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
    }

    private void UpdateGlow()
    {
        if (block == null) return;
        float pulse = 0.9f + 0.1f * Mathf.Sin(Time.time * 2.2f + GetInstanceID());
        Color emission = baseEmission * (Mathf.Lerp(dimFloor, 1f, Reserve01) * pulse + flash * 1.5f);
        foreach (Renderer renderer in glowRenderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(block);
            block.SetColor(EmissionId, emission);
            renderer.SetPropertyBlock(block);
        }
    }

    private void ShowBeam(Vector3 to)
    {
        if (beam == null)
        {
            var go = new GameObject("Infusion Beam");
            go.transform.SetParent(transform, false);
            beam = go.AddComponent<LineRenderer>();
            beam.positionCount = 2;
            beam.useWorldSpace = true;
            beam.shadowCastingMode = ShadowCastingMode.Off;
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit != null)
            {
                beamMaterial = new Material(unlit) { name = "Infusion Beam (runtime)" };
                beamMaterial.SetColor("_BaseColor", new Color(0.65f, 1f, 0.85f));
                beam.sharedMaterial = beamMaterial;
            }
        }
        beam.SetPosition(0, AimPosition);
        beam.SetPosition(1, to);
        beamAge = 0f;
        beam.enabled = true;
    }

    private void UpdateBeam(float deltaTime)
    {
        if (beam == null || !beam.enabled) return;
        beamAge += deltaTime;
        beam.SetPosition(0, AimPosition);
        float fade = 1f - beamAge / 0.4f;
        beam.widthMultiplier = 0.06f * Mathf.Max(0f, fade);
        if (fade <= 0f) beam.enabled = false;
    }

    private void OnDestroy()
    {
        if (auraRoot != null) Destroy(auraRoot.gameObject);
        if (auraMaterial != null) Destroy(auraMaterial);
        if (beamMaterial != null) Destroy(beamMaterial);
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
        if (runtimeMesh != null) Destroy(runtimeMesh);
    }
}
