using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Recycle: an immovable terrain colony. Flowers are independently portable child creatures.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(RecycleNetworkGeometry))]
public sealed class CompostCreature : Creature
{
    [Header("Recycle")]
    [SerializeField] GooCreature gooPrefab;
    [SerializeField] RecycleFlower flowerPrefab;
    [SerializeField] int seed = 7081;
    [Min(0.01f)] [SerializeField] float absorptionSeconds = 5f;
    [Min(1)] [SerializeField] int fullnessThreshold = 50;
    [Min(1)] [SerializeField] int gooCost = 10;
    [Min(0.01f)] [SerializeField] float productionSeconds = 7f;
    [Min(1)] [SerializeField] int flowerCost = 5;
    [Min(0.1f)] [SerializeField] float flowerGrowthSeconds = 5f;
    [Min(1)] [SerializeField] int maximumFlowers = 8;
    [Min(1f)] [SerializeField] float metresPerFlower = 8f;
    [Min(1f)] [SerializeField] float startingStrands = 8f;
    [SerializeField] bool starterFlower = true;
    // Retained serialized identity for old scene overrides and the explicit migration tool.
    [SerializeField, HideInInspector] BoxCollider back;
    [SerializeField, HideInInspector] float walkSpeed, roamRadius;
    [SerializeField, HideInInspector] long storedCharges;
    readonly RecycleDigestion digestion = new RecycleDigestion();
    readonly List<RecycleFlower> flowers = new List<RecycleFlower>();
    readonly HashSet<Creature> scanned = new HashSet<Creature>();
    Collider[] overlaps = new Collider[64];
    RecycleNetworkGeometry geometry;
    SurfaceAnchor seedAnchor, flowerSite;
    ProceduralWorld world;
    bool seedConfigured, starterIssued, growingFlower;
    float flowerTime, scanAfter, retryAfter;
    Vector3 rootedPosition;
    Quaternion rootedRotation;

    public override bool CanBeCarried => false;
    public override bool CanBeCaptured => false;
    protected override bool KeepCorpseStationary => true;
    public long StoredCharges => storedCharges;
    public bool IsFull => storedCharges >= fullnessThreshold;
    public int AbsorbingBodyCount => digestion.Count;
    public IReadOnlyList<RecycleFlower> Flowers => flowers;
    public RecycleNetworkGeometry Geometry => geometry;
    public GooCreature GooPrefab => gooPrefab;
    public float DigestionSeconds => absorptionSeconds;
    public float ProductionSeconds => productionSeconds;
    public int GooCost => gooCost;
    public bool IsProducing { get { foreach (var f in flowers) if (f != null && f.IsProducing) return true; return false; } }
    public float ProductionProgress { get { float p = 0f; foreach (var f in flowers) if (f != null) p = Mathf.Max(p, f.ProductionProgress); return p; } }
    // Compatibility for existing scene/setup code; these no longer describe a physical platform.
    public BoxCollider Back => back;
    public CompostRideSurface RideSurface => GetComponent<CompostRideSurface>();
    public Collider[] BodyColliders { get; private set; } = Array.Empty<Collider>();
    public float BackTopWS => transform.position.y;

    protected override void InitializeCreature()
    {
        SetCreatureName("Recycle");
        ConfigureLifespan(Mathf.Max(1f, Age), 0f);
        Rigidbody body = GetComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        rootedPosition = transform.position; rootedRotation = transform.rotation;
        geometry = GetComponent<RecycleNetworkGeometry>();
        if (geometry == null) geometry = gameObject.AddComponent<RecycleNetworkGeometry>();
        // Old prefab/scene overrides cannot leave an invisible rideable deck over the new colony.
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
            if (CreatureBounds.BelongsTo(renderer.transform, transform)) renderer.enabled = false;
        foreach (Collider collider in GetComponentsInChildren<Collider>())
            if (CreatureBounds.BelongsTo(collider.transform, transform)) collider.enabled = false;
        if (RideSurface != null) RideSurface.enabled = false;
    }

    public static long ChargesForNutrition(float nutrition)
    {
        if (float.IsNaN(nutrition) || nutrition < 20f) return 1;
        double value = Math.Floor(nutrition / 20d);
        return value >= long.MaxValue ? long.MaxValue : Math.Max(1L, (long)value);
    }
    public void InitializeSeed(SurfaceAnchor anchor)
    {
        seedAnchor = anchor; seedConfigured = true; starterFlower = false; startingStrands = 4f;
        storedCharges = 0; starterIssued = false;
        InitializeNewIndividual();
    }

    protected override void TickCreature(float deltaTime) => AdvanceProcessing(deltaTime);
    internal void AdvanceProcessing(float dt)
    {
        if (!SimulationEnabled || !isActiveAndEnabled || dt <= 0f) return;
        if (geometry == null) return;
        if (!geometry.Ready)
        {
            if (Time.time < retryAfter) return;
            retryAfter = Time.time + 0.5f;
            if (!seedConfigured)
            {
                if (!SurfaceGeometry.Probe(gameObject.scene, transform.position + Vector3.up * 0.5f, Vector3.down, 2.5f, true, out seedAnchor)) return;
                seedConfigured = true;
            }
            if (!seedAnchor.Resolve(out _, out _)) return;
            world = seedAnchor.Support.GetComponent<ProceduralWorldChunk>().Owner;
            geometry.Initialize(seedAnchor, seed, startingStrands);
        }
        if (world != null && !world.IsRegionReady(rootedPosition, 1f)) return;
        geometry.Advance(dt);
        long added = digestion.Advance(this, dt, absorptionSeconds, storedCharges, fullnessThreshold);
        if (added > 0) { storedCharges = RecycleDigestion.SaturatingAdd(storedCharges, added); geometry.AddGrowth(added); }
        flowers.RemoveAll(f => f == null || !f.IsAttached || f.Colony != this);
        if (starterFlower && !starterIssued && geometry.EstablishedLength >= startingStrands - 0.1f &&
            geometry.TryFlowerSite(flowers, out var starterSite))
        { if (CreateFlower(starterSite)) starterIssued = true; }

        if (growingFlower)
        {
            flowerTime += dt;
            if (!flowerSite.Resolve(out _, out _))
            { storedCharges = RecycleDigestion.SaturatingAdd(storedCharges, flowerCost); growingFlower = false; }
            else if (flowerTime >= flowerGrowthSeconds && CreateFlower(flowerSite)) growingFlower = false;
        }
        if (!growingFlower && flowers.Count < FlowerSlots && storedCharges >= flowerCost && flowerPrefab != null &&
            geometry.TryFlowerSite(flowers, out flowerSite))
        { storedCharges -= flowerCost; growingFlower = true; flowerTime = 0f; }
        for (int i = flowers.Count - 1; i >= 0; i--) if (i < flowers.Count && flowers[i] != null) flowers[i].AdvanceAttached(dt);
        if (Time.time >= scanAfter)
        { scanAfter = Time.time + 0.25f; Scan(); }
    }
    int FlowerSlots => Mathf.Clamp(Mathf.FloorToInt((geometry.EstablishedLength + 0.01f) / metresPerFlower), 0, maximumFlowers);
    internal bool ReserveGoo()
    {
        bool needsFlower = flowers.Count < FlowerSlots && flowerPrefab != null && geometry.TryFlowerSite(flowers, out _);
        if (!SimulationEnabled || growingFlower || needsFlower || storedCharges < gooCost) return false;
        storedCharges -= gooCost; return true;
    }
    internal void RefundGoo() => storedCharges = RecycleDigestion.SaturatingAdd(storedCharges, gooCost);
    internal void FlowerDetached(RecycleFlower flower) => flowers.Remove(flower);

    bool CreateFlower(SurfaceAnchor site)
    {
        if (flowerPrefab == null || !site.Resolve(out Vector3 p, out Vector3 n)) return false;
        var staging = new GameObject("Recycle flower staging"); staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, gameObject.scene);
        try
        {
            RecycleFlower flower = Instantiate(flowerPrefab, p + n * 0.012f, Quaternion.FromToRotation(Vector3.up, n), staging.transform);
            flower.ConfigureAttached(this, site);
            flower.transform.SetParent(transform, true);
            flowers.Add(flower); return true;
        }
        finally { Destroy(staging); }
    }

    void Scan()
    {
        if (!geometry.Ready) return;
        // Broad phase is bounded by the actual rendered colony; exact strand contact follows it.
        Bounds bounds = geometry.SurfaceBounds; bounds.Expand(0.25f);
        int count;
        do
        {
            count = gameObject.scene.GetPhysicsScene().OverlapBox(bounds.center, bounds.extents, overlaps, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            if (count < overlaps.Length) break;
            Array.Resize(ref overlaps, overlaps.Length * 2);
        } while (overlaps.Length < 4096);
        scanned.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider solid = overlaps[i]; Creature target = solid.GetComponentInParent<Creature>();
            if (target == null || scanned.Contains(target)) continue;
            if (TryBeginAbsorption(target, solid)) scanned.Add(target);
        }
    }
    internal bool TryBeginAbsorption(Creature target, Collider contactCollider)
    {
        if (!SimulationEnabled || target == this || !CompostAbsorption.Eligible(target) || geometry == null ||
            !geometry.TryContact(contactCollider, out Vector3 point)) return false;
        return digestion.Begin(this, target, point);
    }
    void LateUpdate()
    {
        // The root is an ecological location, not a physics object that another system can push.
        if (Application.isPlaying) transform.SetPositionAndRotation(rootedPosition, rootedRotation);
    }
    protected override void OnEnable() { base.OnEnable(); ProceduralWorld.AnyTerrainChanged += TerrainChanged; }
    void TerrainChanged(ProceduralWorld changed, Bounds bounds)
    {
        if (changed != world || geometry == null) return;
        SurfaceGeometry.Invalidate(changed, bounds); geometry.Invalidate(bounds);
    }
    protected override void OnDeath()
    {
        digestion.Release(); storedCharges = 0; growingFlower = false;
        var released = flowers.ToArray(); foreach (var flower in released) if (flower != null) flower.Detach();
    }
    protected override void OnDisable()
    { ProceduralWorld.AnyTerrainChanged -= TerrainChanged; digestion.Release(); base.OnDisable(); }
}
