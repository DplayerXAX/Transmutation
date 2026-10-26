using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Basin test level: two towers in a round basin ringed by mountains, and the creatures that offer
/// several ways onto the tall tower's top, where the seed is. Layout positions are metres from the
/// ProceduralWorld's spawn (x = east, y = north).
///   Tower A: short, climbable with goo on the hands (all hand climbing spends goo). Flat top.
///   Tower B: tall and floating (nothing below cutHeight), flat top with the seed.
///   Sky spider: lives on the ground between the towers (its own SkySpider / SkyWeb behaviour). Its web is
///     solid (SkyWeb walkable strands): carried up and set down in the gap, it spins a web to cross on.
/// The compost creature turns corpses (the spider's leftovers, bird bones) into new goo.
/// Routes: goo + tentacle (climb A, tether across); fire + bone child + tentacle (fly);
/// goo + spider (climb A with the spider, drop it in the gap, walk or climb its web to B).
/// Goo, fuzz, tentacle creatures and bubble flowers grow back at home when lost (eaten, used up).
/// </summary>
[DisallowMultipleComponent]
public sealed class BasinLevel : MonoBehaviour
{
    [SerializeField] private ProceduralWorld world;

    [Header("Creatures")]
    [SerializeField] private GooCreature gooPrefab;
    [SerializeField] private BubbleFlower bubbleFlowerPrefab;
    [SerializeField] private SkySpider spiderPrefab;
    [SerializeField] private FireSource fireSourcePrefab;
    [SerializeField] private FireFlower fireFlowerPrefab;
    [SerializeField] private CompostCreature compostPrefab;
    [Tooltip("Goo and bubble flower can be prefabs or scene templates (the setup menu makes templates: goo the birds leave alone, flowers that fire less often).")]
    [SerializeField] private TentacleCreature tentacleTemplate;
    [SerializeField] private FuzzCreature fuzzTemplate;

    [Header("Materials")]
    [SerializeField] private Material towerMaterial;
    [SerializeField] private Material boneMaterial;
    [SerializeField] private Material silkMaterial;
    [SerializeField] private Material inkMaterial;
    [SerializeField] private Material seedMaterial;

    [Header("Layout (metres from spawn: x east, y north)")]
    [SerializeField] private Vector2 towerA = new Vector2(-15f, 30f);
    [SerializeField] private Vector2 towerB = new Vector2(15f, 36f);
    [SerializeField] private Vector2 bubbleShelf = new Vector2(-28f, 22f);
    [SerializeField] private Vector2 spiderHome = new Vector2(8f, 26f);
    [Tooltip("More bubble flowers round the two towers.")]
    [SerializeField] private Vector2[] towerFlowers =
    {
        new Vector2(-9f, 22f), new Vector2(-25f, 31f), new Vector2(26f, 37f), new Vector2(14f, 47f),
    };
    [SerializeField] private Vector2 fireGarden = new Vector2(24f, 26f);
    [SerializeField] private Vector2 gooPool = new Vector2(-4f, 27f);
    [SerializeField] private Vector2 fuzzStart = new Vector2(0f, 16f);
    [SerializeField] private Vector2 compostHome = new Vector2(2f, 37f);
    [Tooltip("Where tentacle creatures live (each grows back there).")]
    [SerializeField] private Vector2[] tentacleSpots =
    {
        new Vector2(-11f, 22f), new Vector2(6f, 24f), new Vector2(-24f, 38f), new Vector2(18f, 22f), new Vector2(-2f, 44f),
    };
    [Range(0, 12)] [SerializeField] private int gooCount = 7;
    [Tooltip("Radius of the goo pool (the ring on the ground, and how far the blobs spread).")]
    [Min(1f)] [SerializeField] private float gooPoolRadius = 6.5f;
    [Range(0, 8)] [SerializeField] private int fuzzCount = 4;
    [Range(0, 4)] [SerializeField] private int boneChildren = 2;

    [Header("Towers")]
    [Tooltip("Height of tower A's top above the ground under it.")]
    [Min(8f)] [SerializeField] private float towerAHeight = 20f;
    [Tooltip("Height of tower B's top above the ground under it.")]
    [Min(10f)] [SerializeField] private float towerBHeight = 28f;
    [Tooltip("Tower B is cut away below this height (it floats).")]
    [Min(0f)] [SerializeField] private float towerBCut = 10f;
    [Tooltip("Off: goo-coated hands climb the towers. On: bare hands slide off (only a carried tentacle creature grips).")]
    [SerializeField] private bool slipperyTowers;
    [SerializeField] private int seed = 41;

    [Tooltip("Radius of the loop the bone children crawl round the fire garden.")]
    [Min(8f)] [SerializeField] private float boneLoop = 8f;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool built;
    [SerializeField] private bool lifeSpawned;

    private const string SeedId = "basin_seed";

    private Transform root;
    private SkySpider spider;
    private Vector3 origin, topA, topB;
    private SpireShape shapeA, shapeB;
    private Transform towerATransform, towerBTransform;
    private float lifeWait;

    private readonly RegrowingSlots slots = new RegrowingSlots();

    public Vector3 TopA => topA;
    public Vector3 TopB => topB;

    public void Setup(ProceduralWorld proceduralWorld, GooCreature goo, BubbleFlower flower, SkySpider skySpider, FireSource source,
        FireFlower fireFlower, CompostCreature compost, TentacleCreature tentacle, FuzzCreature fuzz)
    {
        world = proceduralWorld;
        gooPrefab = goo;
        bubbleFlowerPrefab = flower;
        spiderPrefab = skySpider;
        fireSourcePrefab = source;
        fireFlowerPrefab = fireFlower;
        compostPrefab = compost;
        tentacleTemplate = tentacle;
        fuzzTemplate = fuzz;
    }

    public void SetMaterials(Material tower, Material bone, Material silk, Material ink, Material seedPod)
    {
        towerMaterial = tower;
        boneMaterial = bone;
        silkMaterial = silk;
        inkMaterial = ink;
        seedMaterial = seedPod;
    }

    private void Update()
    {
        if (!built)
        {
            if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
            if (world != null && world.LayoutReady) Build();
            return;
        }
        if (root == null) return;

        // Creatures wait for the ground (bone children walk on it; bodies would fall through).
        if (!lifeSpawned)
        {
            lifeWait += Time.deltaTime;
            if (world.GenerationComplete || lifeWait > 20f) SpawnLife();
        }
        else slots.Tick(Time.deltaTime);
    }

    // ---------------- Building ----------------

    private void Build()
    {
        built = true;
        origin = world.transform.position;
        var random = new System.Random(seed);
        root = new GameObject("Basin Level (runtime)").transform;
        root.SetParent(transform, false);
        root.position = Vector3.zero;

        towerATransform = Tower("Tower A", towerA, towerAHeight, 0f, 3, 31.7f + seed, random, out shapeA, out topA);
        towerBTransform = Tower("Tower B", towerB, towerBHeight, towerBCut, 2, 47.3f + seed, random, out shapeB, out topB);

        RealmSites.Seed(SeedId, topB + Vector3.up * 1.2f, seedMaterial, root);

        Ring(Ground(Local(fireGarden)) + Vector3.up * 0.06f, 2.4f, "Garden Ring");
        Ring(Ground(Local(gooPool)) + Vector3.up * 0.06f, gooPoolRadius, "Goo Ring");
        Ring(Ground(Local(bubbleShelf)) + Vector3.up * 0.06f, 1.8f, "Flower Ring");
        Debug.Log($"Basin Level built: tower A top {topA.y - origin.y:0.0} m, tower B top {topB.y - origin.y:0.0} m, " +
                  $"gap {Flat(topB - topA).magnitude:0.0} m.", this);
    }

    private Transform Tower(string name, Vector2 at, float height, float cut, int ledges, float noise, System.Random random, out SpireShape shape, out Vector3 top)
    {
        Vector3 centre = Local(at);
        float ground = LowestGround(centre, 6f);
        shape = new SpireShape
        {
            height = height,
            bury = 6f,
            innerRadius = 2.6f,
            topScale = 0.9f,
            wallBase = 2.4f,
            wallTop = 2.2f,
            flare = 2.5f,
            lean = 0.8f,
            crown = 0.6f,
            doorHalfAngle = 0f,
            windows = 0f,
            noiseSeed = noise,
            glazePatchDensity = 0.08f,
            roughness = 0.08f,
            drumLength = 7f,
        };
        if (cut > 0f) shape.cutBelow = cut;
        Mesh mesh = LandmarkShapes.Spire(shape, random, ledges, beam: false, flipY: false, out _);
        GameObject tower = Part(name, root, mesh, towerMaterial, 7, true, true);
        tower.transform.position = new Vector3(centre.x, ground, centre.z);
        if (slipperyTowers)
        {
            // Bare hands slide straight off; a carried tentacle creature never slips.
            var slip = tower.AddComponent<SlipperySurface>();
            slip.speedScale = 1f;
            slip.accelerate = true;
        }
        SpireStructure.Build(tower.transform, shape, towerMaterial);
        Lid(tower.transform, shape);
        top = tower.transform.position + shape.Axis(shape.height);
        return tower.transform;
    }

    /// <summary>A flat stone floor over the hollow shaft at the top, so the top is a platform.</summary>
    private void Lid(Transform tower, SpireShape shape)
    {
        float y = shape.height - 0.05f;
        Vector3 centre = shape.Axis(y);
        var mesh = new FacetMesh(y - 0.3f, y, 0.3f);
        const int sides = 28;
        for (int i = 0; i < sides; i++)
        {
            float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
            Vector3 p0 = centre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * (shape.Inner(a0, y) + 0.6f);
            Vector3 p1 = centre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * (shape.Inner(a1, y) + 0.6f);
            mesh.Triangle(centre, p0, p1, Vector3.up);
            mesh.Triangle(centre - Vector3.up * 0.3f, p0 - Vector3.up * 0.3f, p1 - Vector3.up * 0.3f, Vector3.down);
        }
        GameObject lid = Part("Top Floor", tower, mesh.Build("Top Floor"), towerMaterial, 7, true, false);
        lid.transform.localPosition = Vector3.zero;
    }

    private void Ring(Vector3 centre, float radius, string name)
    {
        var mesh = new FacetMesh(centre.y - 0.1f, centre.y + 0.1f, 0.2f);
        const int count = 40;
        for (int i = 0; i < count; i++)
        {
            float a0 = i / (float)count * Mathf.PI * 2f, a1 = (i + 1) / (float)count * Mathf.PI * 2f;
            var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
            var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            mesh.Quad(centre + d0 * (radius - 0.1f), centre + d1 * (radius - 0.1f), centre + d1 * (radius + 0.1f), centre + d0 * (radius + 0.1f), Vector3.up);
        }
        Part(name, root, mesh.Build(name), inkMaterial, 0, false, false);
    }

    // ---------------- Creatures ----------------

    private void SpawnLife()
    {
        lifeSpawned = true;
        var random = new System.Random(seed * 7 + 3);

        // Fire garden: the source keeps the flower blooming fire bubbles; bone children loop round it.
        Vector3 garden = Ground(Local(fireGarden));
        // Flower first: its marching-cubes volume is what the fire source's own glow is drawn into.
        if (fireFlowerPrefab != null) Instantiate(fireFlowerPrefab, SurfaceAt(garden + Vector3.right * 1.4f), Quaternion.identity).name = "Basin Fire Flower";
        if (fireSourcePrefab != null) Instantiate(fireSourcePrefab, SurfaceAt(garden) + Vector3.up * 0.6f, Quaternion.identity).name = "Basin Fire Source";
        Shader comb = Shader.Find("Capstone/Landmarks/BoneComb");
        Material skin = comb != null ? new Material(comb) { name = "Basin Bone Ribs (runtime)" } : boneMaterial;
        for (int c = 0; c < boneChildren; c++)
        {
            float angle = (c / (float)Mathf.Max(1, boneChildren) + (float)random.NextDouble() * 0.1f) * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 spot = Ground(garden + dir * boneLoop);
            BoneChild.Spawn(root, spot, Quaternion.LookRotation(-dir).eulerAngles.y, seed * 31 + c, boneMaterial, skin, garden);
        }

        // The spider starts low over the ground and spins its first web there.
        if (spiderPrefab != null)
        {
            spider = Instantiate(spiderPrefab, Ground(Local(spiderHome)) + Vector3.up * 2.5f, Quaternion.identity);
            spider.name = "Basin Spider";
        }

        foreach (Vector2 spot in tentacleSpots)
            if (tentacleTemplate != null) AddSlot(tentacleTemplate.gameObject, Ground(Local(spot)), 8f);
        if (bubbleFlowerPrefab != null)
        {
            AddSlot(bubbleFlowerPrefab.gameObject, Ground(Local(bubbleShelf)), 15f);
            foreach (Vector2 spot in towerFlowers) AddSlot(bubbleFlowerPrefab.gameObject, Ground(Local(spot)), 15f);
        }
        if (compostPrefab != null) AddSlot(compostPrefab.gameObject, Ground(Local(compostHome)), 20f);
        Vector3 pool = Local(gooPool);
        for (int i = 0; i < gooCount && gooPrefab != null; i++)
            AddSlot(gooPrefab.gameObject, Ground(pool + Around(i, gooCount, gooPoolRadius * (i % 2 == 0 ? 0.45f : 0.75f))), 12f);
        Vector3 fuzz = Local(fuzzStart);
        for (int i = 0; i < fuzzCount && fuzzTemplate != null; i++)
            AddSlot(fuzzTemplate.gameObject, Ground(fuzz + Around(i, fuzzCount, 1.8f)), 20f);
    }

    private void AddSlot(GameObject template, Vector3 home, float delay) => slots.Add(template, home, delay);

    // ---------------- Helpers ----------------

    private Vector3 Local(Vector2 at) => origin + new Vector3(at.x, 0f, at.y);

    private Vector3 Ground(Vector3 point)
    {
        world.ColumnHeights(point, out float surface, out _, out _);
        return new Vector3(point.x, surface, point.z);
    }

    /// <summary>The built ground at a point (the generated mesh, overhangs included); the analytic surface if none.</summary>
    private Vector3 SurfaceAt(Vector3 point)
    {
        Vector3 rough = Ground(point);
        float top = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(rough + Vector3.up * 30f, Vector3.down, 60f, 1 << 7, QueryTriggerInteraction.Ignore))
            if (world.OwnsCollider(hit.collider)) top = Mathf.Max(top, hit.point.y);
        return float.IsNegativeInfinity(top) ? rough : new Vector3(rough.x, top, rough.z);
    }

    private float LowestGround(Vector3 centre, float radius)
    {
        float low = Ground(centre).y;
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * Mathf.PI * 2f;
            low = Mathf.Min(low, Ground(centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius).y);
        }
        return low;
    }

    private static Vector3 Around(int i, int count, float radius)
    {
        float a = i / (float)Mathf.Max(1, count) * Mathf.PI * 2f;
        return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static GameObject Part(string name, Transform parent, Mesh mesh, Material material, int layer, bool collide, bool shadows)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { layer = layer };
        go.transform.SetParent(parent, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        if (collide) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
        if (spider != null) Destroy(spider.gameObject);
    }
}
