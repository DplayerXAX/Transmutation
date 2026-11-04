using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>The portable organ of Recycle. Colony and detached resource banks never overlap.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(RecycleFlowerVisual))]
public sealed class RecycleFlower : Creature
{
    [SerializeField] CompostCreature seedPrefab;
    [SerializeField] GooCreature gooPrefab;
    [Min(1f)] [SerializeField] float detachedLifetime = 120f;
    [Min(0.1f)] [SerializeField] float bloomSeconds = 5f;
    [Min(0.1f)] [SerializeField] float aimRange = 5f;
    [Min(0.1f)] [SerializeField] float clickCooldown = 0.4f;
    [SerializeField, HideInInspector] bool detachedEver;
    [SerializeField, HideInInspector] long storedCharges;
    CompostCreature colony;
    SurfaceAnchor attachment;
    readonly RecycleDigestion digestion = new RecycleDigestion();
    RecycleFlowerVisual visual;
    float bloom, productionTime, nextClick;
    bool producing;
    public CompostCreature Colony => colony;
    public bool IsAttached => colony != null && !detachedEver;
    public bool SeedReserved { get; internal set; }
    public long StoredCharges => IsAttached ? colony.StoredCharges : storedCharges;
    public float Bloom => bloom;
    public bool IsProducing => producing;
    public float ProductionProgress => producing ? Mathf.Clamp01(productionTime / (IsAttached ? colony.ProductionSeconds : 7f)) : 0f;
    public CompostCreature SeedPrefab => seedPrefab;
    public override bool CanBeCarried => !SeedReserved && bloom >= 0.95f;
    public override bool CanBeCaptured => !IsAttached && !SeedReserved;
    public override bool CanBeConsumed => !SeedReserved;
    public override bool CanUseWhileCarried => !IsDead && !IsAttached;
    public override bool HeldInView => true;
    public override string CarriedPrompt => IsDead ? null : "[LMB] Recycle corpse / jolt creature";

    protected override void InitializeCreature()
    {
        SetCreatureName("Recycle Flower");
        SetFoodAndDiet(FoodTag.Plant, FoodTag.Everything);
        visual = GetComponent<RecycleFlowerVisual>();
        ConfigureLifespan(detachedEver ? Mathf.Min(Age, detachedLifetime) : detachedLifetime, detachedEver ? 1f : 0f);
        SetMovementPhysics(IsAttached, !IsAttached);
        if (IsAttached) GetComponent<Rigidbody>().constraints = RigidbodyConstraints.None;
        if (visual != null) visual.SetState(bloom, 1f, 0f);
        if (!TryGetComponent<RecycleGermination>(out _)) gameObject.AddComponent<RecycleGermination>();
    }
    public void ConfigureAttached(CompostCreature owner, SurfaceAnchor site)
    {
        colony = owner; attachment = site; detachedEver = false; storedCharges = 0; bloom = 0f;
        if (owner != null) gooPrefab = owner.GooPrefab;
    }
    internal void AdvanceAttached(float dt)
    {
        if (!IsAttached || IsDead || !SimulationEnabled || !colony.SimulationEnabled) return;
        if (!attachment.Resolve(out _, out _)) { Detach(); return; }
        bloom = Mathf.Min(1f, bloom + dt / bloomSeconds);
        AdvanceOutput(dt);
        visual.SetState(bloom, 1f, ProductionProgress);
    }
    protected override void TickCreature(float dt)
    {
        if (IsAttached) return;
        bloom = Mathf.Min(1f, bloom + dt / bloomSeconds);
        long added = digestion.Advance(this, dt, 5f, storedCharges);
        storedCharges = RecycleDigestion.SaturatingAdd(storedCharges, added);
        AdvanceOutput(dt);
        visual.SetState(bloom, Mathf.Clamp01(Age / detachedLifetime), ProductionProgress);
    }
    void AdvanceOutput(float dt)
    {
        if (bloom < 0.99f || gooPrefab == null) return;
        int cost = IsAttached ? colony.GooCost : 10;
        if (!producing)
        {
            bool reserved;
            if (IsAttached) reserved = colony.ReserveGoo();
            else { reserved = storedCharges >= cost; if (reserved) storedCharges -= cost; }
            if (!reserved) return;
            producing = true; productionTime = 0f;
        }
        productionTime = Mathf.Min(IsAttached ? colony.ProductionSeconds : 7f, productionTime + dt);
        if (productionTime < (IsAttached ? colony.ProductionSeconds : 7f)) return;
        if (!TryDropGoo()) return;
        producing = false; productionTime = 0f;
    }
    bool TryDropGoo()
    {
        Vector3 muzzle = visual != null ? visual.MouthPosition : transform.position + transform.up * 0.8f;
        // A small side offset lets drops fall out of the flower instead of resting in its own capsule.
        Vector3 position = muzzle + transform.forward * 0.35f;
        int count = gameObject.scene.GetPhysicsScene().OverlapSphere(position, 0.26f, outputOverlaps, ~0, QueryTriggerInteraction.Ignore);
        if (count == outputOverlaps.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Collider solid = outputOverlaps[i];
            if (solid.GetComponentInParent<Creature>() == this) continue;
            if (IsCarried && Carrier != null && solid.GetComponentInParent<SmoothFirstPersonController>() == Carrier.PlayerController) continue;
            return false;
        }
        GooCreature goo = Instantiate(gooPrefab, position, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(goo.gameObject, gameObject.scene);
        var body = goo.GetComponent<Rigidbody>(); body.isKinematic = false; body.useGravity = true;
        body.linearVelocity = Vector3.down * 0.2f;
        return true;
    }
    protected override void OnCarryStarted() => Detach();
    readonly Collider[] outputOverlaps = new Collider[32];
    public void Detach()
    {
        if (detachedEver) return;
        CompostCreature previous = colony;
        if (producing && previous != null) previous.RefundGoo();
        producing = false; productionTime = 0f; storedCharges = 0;
        colony = null; detachedEver = true;
        transform.SetParent(null, true);
        ConfigureLifespan(detachedLifetime, 1f);
        SetMovementPhysics(false, true);
        if (previous != null) previous.FlowerDetached(this);
    }
    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (!IsCarried || IsAttached || IsDead || !SimulationEnabled || Time.time < nextClick || Carrier == null) return;
        Camera camera = Carrier.CarryCamera;
        if (camera == null) return;
        Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        Creature target = CreatureAim.Find(gameObject.scene.GetPhysicsScene(), ray, aimRange, ~0, Carrier, out RaycastHit hit);
        if (target == null || target == this || target.IsCarried || target.IsBubbleCaptured || target.IsConsumptionPending ||
            (target.TryGetComponent<CompostAbsorption>(out var claimed) && claimed.HasClaim)) return;
        // Camera selection cannot reach around the obstruction between the physical flower and its target.
        Vector3 mouth = visual != null ? visual.MouthPosition : transform.position;
        if (CreatureSpatialQuery.FirstSolid(gameObject.scene, mouth, hit.point, this,
            Carrier.PlayerController != null ? Carrier.PlayerController.gameObject : null, ref sightHits, out var blocker) &&
            CreatureSpatialQuery.Owner(blocker.collider) != target) return;
        nextClick = Time.time + clickCooldown;
        TryFeedOrJolt(target, hit.point);
    }
    RaycastHit[] sightHits = new RaycastHit[32];
    internal bool TryFeedOrJolt(Creature target, Vector3 point)
    {
        if (target == null || target.IsCarried || target.IsBubbleCaptured || target.IsConsumptionPending) return false;
        if (target.IsDead)
        {
            bool began = digestion.Begin(this, target, point);
            if (began) visual.Jolt();
            return began;
        }
        if (target.TryGetComponent<Rigidbody>(out var body) && !body.isKinematic)
            body.AddForce(Vector3.up * 1.1f, ForceMode.VelocityChange);
        visual.Jolt();
        return false;
    }
    protected override void OnDeath()
    {
        digestion.Release();
        if (IsAttached) Detach();
        producing = false; storedCharges = 0;
        if (visual != null) visual.SetState(bloom, 0f, 0f);
        if (TryGetComponent<RecycleGermination>(out var seed)) seed.CheckRestingContact();
    }
    protected override void OnDisable() { digestion.Release(); base.OnDisable(); }
}
