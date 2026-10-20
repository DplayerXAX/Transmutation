using UnityEngine;

/// <summary>
/// Bubble Well: a tall round well near the Hanging Spire with smooth walls no hand can grip.
/// On a plinth in the middle sits a bubble flower, closed up with cold. No single creature gets you up:
///   1. outside the well a fire source keeps a fire flower warm, and the flower blooms fire bubbles,
///   2. catch a fire bubble and carry it to the cold bubble flower,
///   3. fire close by wakes the bubble flower; it starts blowing bubbles straight up the shaft
///      (a fire bubble left there falls, turns into a fire flower and keeps it awake for good),
///   4. step into a rising bubble; press Space near the top to burst out onto the lid, where a seed waits.
/// Put it on any object in the scene; it builds itself once the world and the spire exist.
/// </summary>
[DisallowMultipleComponent]
public sealed class BubbleWell : MonoBehaviour
{
    [Header("Prefabs And Materials")]
    [SerializeField] private BubbleFlower flowerPrefab;
    [SerializeField] private FireSource fireSourcePrefab;
    [SerializeField] private FireFlower fireFlowerPrefab;
    [SerializeField] private Material stoneMaterial;
    [SerializeField] private Material inkMaterial;
    [SerializeField] private Material seedMaterial;

    [Header("Layout")]
    [SerializeField] private int seed = 33;
    [Tooltip("Distance range from the spire's foot.")]
    [SerializeField] private Vector2 distanceFromSpire = new Vector2(40f, 80f);
    [Min(2f)] [SerializeField] private float innerRadius = 3.6f;
    [Min(0.2f)] [SerializeField] private float wallThickness = 0.8f;
    [Tooltip("Height of the rim above the well floor. A bubble rises 6-10 m and bursting adds about 4 m.")]
    [Min(3f)] [SerializeField] private float height = 8.5f;
    [Tooltip("Radius of the opening in the lid; wider than a bubble with the player inside (about 1.3 m).")]
    [Min(1f)] [SerializeField] private float openingRadius = 2.3f;
    [Min(0.5f)] [SerializeField] private float plinthHeight = 1.4f;
    [Min(0.8f)] [SerializeField] private float doorWidth = 1.8f;
    [Min(1.5f)] [SerializeField] private float doorHeight = 2.8f;

    [Header("Cold Flower")]
    [Tooltip("Fire creatures (fire bubbles, fire flowers, fire sources) this close warm the bubble flower.")]
    [Min(0.5f)] [SerializeField] private float warmRadius = 3f;
    [Tooltip("Warmth gained per second while fire is close, and lost per second without it.")]
    [Min(0f)] [SerializeField] private float warmRate = 8f;
    [Min(0f)] [SerializeField] private float coolRate = 1.5f;
    [Tooltip("Above this warmth the flower blows bubbles.")]
    [Min(0f)] [SerializeField] private float wakeWarmth = 10f;
    [Min(1f)] [SerializeField] private float maxWarmth = 40f;
    [Tooltip("Distance from the well to the fire garden outside.")]
    [Min(4f)] [SerializeField] private float gardenDistance = 11f;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool built;
    [SerializeField] private float warmth;
    [SerializeField] private bool flowerAwake;

    private const string SeedId = "well_seed";
    private ProceduralWorld world;
    private Transform root;
    private BubbleFlower flower;
    private float waited;

    public void Setup(BubbleFlower bubbleFlower, FireSource fireSource, FireFlower fireFlower, Material stone, Material ink, Material seedPod)
    {
        flowerPrefab = bubbleFlower;
        fireSourcePrefab = fireSource;
        fireFlowerPrefab = fireFlower;
        stoneMaterial = stone;
        inkMaterial = ink;
        seedMaterial = seedPod;
    }

    private float fireScan;
    private bool fireNear;
    private readonly GameObject[] gardenParts = new GameObject[2];

    private void Update()
    {
        if (!built)
        {
            if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
            waited += Time.deltaTime;
            if (RealmSites.ReadyToPlace(world, waited)) Build();
            return;
        }
        if (flower == null) return;

        // Warmth: any fire creature near the flower, wherever the flower has been carried.
        fireScan -= Time.deltaTime;
        if (fireScan <= 0f)
        {
            fireScan = 0.4f;
            fireNear = FireWithin(flower.transform.position, warmRadius);
        }
        warmth = Mathf.Clamp(warmth + (fireNear ? warmRate : -coolRate) * Time.deltaTime, 0f, maxWarmth);
        bool awake = warmth >= wakeWarmth || (flowerAwake && warmth > wakeWarmth * 0.5f);
        if (awake != flowerAwake || flower.SimulationEnabled != awake)
        {
            flowerAwake = awake;
            // Asleep: the flower blows nothing. It can still be carried.
            flower.SetSimulationEnabled(awake);
        }
    }

    private static bool FireWithin(Vector3 point, float radius)
    {
        float r2 = radius * radius;
        foreach (FireBubble fire in FindObjectsByType<FireBubble>(FindObjectsSortMode.None))
            if ((fire.transform.position - point).sqrMagnitude < r2) return true;
        foreach (FireFlower fire in FindObjectsByType<FireFlower>(FindObjectsSortMode.None))
            if ((fire.transform.position - point).sqrMagnitude < r2) return true;
        foreach (FireSource fire in FindObjectsByType<FireSource>(FindObjectsSortMode.None))
            if ((fire.transform.position - point).sqrMagnitude < r2) return true;
        return false;
    }

    private void Build()
    {
        built = true;
        float outer = innerRadius + wallThickness;
        if (!RealmSites.FindSpot(world, outer + 2f, distanceFromSpire.x, distanceFromSpire.y, seed, out Vector3 centre, 2.5f))
        {
            Debug.LogWarning("Bubble Well: no free spot near the spire.", this);
            return;
        }
        var random = new System.Random(seed);

        // Floor at the highest ground inside so no terrain pokes through; the wall is sunk below the lowest.
        RealmSites.SurfaceRange(world, centre, outer, out float low, out float high);
        float floorY = high;
        float rimY = floorY + height;
        float bottomY = low - 3f;

        root = new GameObject("Bubble Well").transform;
        root.position = new Vector3(centre.x, floorY, centre.z);
        // The door faces the spire, the way players come from.
        Vector3 toSpire = RealmSites.Flat(RealmSites.AreaCentre(world) - centre).normalized;
        float doorAngle = Mathf.Atan2(toSpire.z, toSpire.x);

        Wall(centre, bottomY, rimY, doorAngle, random);
        Lid(centre, rimY);
        Disc("Floor", new Vector3(centre.x, floorY + 0.05f, centre.z), innerRadius + 0.2f, 0.4f, stoneMaterial, 7);
        Disc("Plinth", new Vector3(centre.x, floorY + plinthHeight, centre.z), 0.9f, plinthHeight + 0.3f, stoneMaterial, 7);
        InkCircle(new Vector3(centre.x, floorY + 0.12f, centre.z), innerRadius - 0.6f);
        Ramp(centre, toSpire, floorY);

        // The seed sits on the lid, across from the door, so it is seen on the way up.
        Vector3 back = -toSpire;
        RealmSites.Seed(SeedId, new Vector3(centre.x, rimY + 1f, centre.z) + back * ((openingRadius + outer) * 0.5f), seedMaterial, root);

        if (flowerPrefab != null)
        {
            flower = Instantiate(flowerPrefab, new Vector3(centre.x, floorY + plinthHeight + 0.35f, centre.z), Quaternion.identity);
            flower.name = "Well Bubble Flower";
            flower.SetSimulationEnabled(false);
        }
        FireGarden(centre, toSpire, random);
    }

    /// <summary>
    /// Outside the well, off to one side of the door: a fire source keeping a fire flower warm,
    /// so it blooms fire bubbles now and then. Ringed in ink so it reads as a place.
    /// </summary>
    private void FireGarden(Vector3 centre, Vector3 toSpire, System.Random random)
    {
        float turn = (random.NextDouble() < 0.5 ? -1f : 1f) * 50f;
        Vector3 direction = Quaternion.Euler(0f, turn, 0f) * toSpire;
        Vector3 spot = centre + direction * gardenDistance;
        spot.y = RealmSites.Surface(world, spot);
        InkCircle(spot + Vector3.up * 0.06f, 2.4f);
        if (fireSourcePrefab != null)
        {
            FireSource source = Instantiate(fireSourcePrefab, spot + Vector3.up * 0.6f, Quaternion.identity);
            source.name = "Well Fire Source";
            gardenParts[0] = source.gameObject;
        }
        if (fireFlowerPrefab != null)
        {
            Vector3 flowerSpot = spot + Vector3.Cross(Vector3.up, direction) * 1.4f;
            flowerSpot.y = RealmSites.Surface(world, flowerSpot);
            FireFlower fireFlower = Instantiate(fireFlowerPrefab, flowerSpot, Quaternion.identity);
            fireFlower.name = "Well Fire Flower";
            gardenParts[1] = fireFlower.gameObject;
        }
    }

    /// <summary>The round wall, with a doorway at the bottom. Default layer: hands cannot grip it.</summary>
    private void Wall(Vector3 centre, float bottomY, float topY, float doorAngle, System.Random random)
    {
        const int segments = 30;
        float outer = innerRadius + wallThickness;
        float doorHalf = doorWidth * 0.5f / innerRadius;
        var mesh = new FacetMesh(bottomY, topY, (float)random.NextDouble());
        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * Mathf.PI * 2f, a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
            float mid = Mathf.DeltaAngle((a0 + a1) * 0.5f * Mathf.Rad2Deg, doorAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            // Segments in the doorway start above the lintel.
            float from = Mathf.Abs(mid) < doorHalf ? Mathf.Max(bottomY, root.position.y + doorHeight) : bottomY;
            var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
            var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Vector3 c0 = new Vector3(centre.x, from, centre.z), c1 = new Vector3(centre.x, topY, centre.z);
            Vector3 i00 = c0 + d0 * innerRadius, i01 = c0 + d1 * innerRadius, i10 = c1 + d0 * innerRadius, i11 = c1 + d1 * innerRadius;
            Vector3 o00 = c0 + d0 * outer, o01 = c0 + d1 * outer, o10 = c1 + d0 * outer, o11 = c1 + d1 * outer;
            Vector3 outward = (d0 + d1) * 0.5f;
            mesh.Quad(o00, o01, o11, o10, outward);
            mesh.Quad(i00, i01, i11, i10, -outward);
            mesh.Quad(i00, i01, o01, o00, Vector3.down);
            // Door jambs.
            bool doorHere = Mathf.Abs(mid) < doorHalf;
            float prevMid = Mathf.DeltaAngle((a0 - Mathf.PI / segments) * Mathf.Rad2Deg, doorAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float nextMid = Mathf.DeltaAngle((a1 + Mathf.PI / segments) * Mathf.Rad2Deg, doorAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            if (!doorHere && Mathf.Abs(nextMid) < doorHalf)
                mesh.Quad(new Vector3(centre.x, bottomY, centre.z) + d1 * innerRadius, new Vector3(centre.x, bottomY, centre.z) + d1 * outer,
                    o11, i11, new Vector3(-d1.z, 0f, d1.x));
            if (!doorHere && Mathf.Abs(prevMid) < doorHalf)
                mesh.Quad(new Vector3(centre.x, bottomY, centre.z) + d0 * innerRadius, new Vector3(centre.x, bottomY, centre.z) + d0 * outer,
                    o10, i10, new Vector3(d0.z, 0f, -d0.x));
        }
        RealmSites.Part("Well Wall", root, mesh.Build("Well Wall"), stoneMaterial, 0, true);
    }

    /// <summary>A stone ring over the top with an opening in the middle; walkable (Ground layer).</summary>
    private void Lid(Vector3 centre, float rimY)
    {
        const int segments = 30;
        float outer = innerRadius + wallThickness + 0.4f;
        const float thickness = 0.5f;
        var mesh = new FacetMesh(rimY - thickness, rimY, 0.4f);
        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * Mathf.PI * 2f, a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
            var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
            var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            Vector3 top = new Vector3(centre.x, rimY, centre.z), bottom = new Vector3(centre.x, rimY - thickness, centre.z);
            mesh.Quad(top + d0 * openingRadius, top + d1 * openingRadius, top + d1 * outer, top + d0 * outer, Vector3.up);
            mesh.Quad(bottom + d0 * openingRadius, bottom + d1 * openingRadius, bottom + d1 * outer, bottom + d0 * outer, Vector3.down);
            mesh.Quad(bottom + d0 * outer, bottom + d1 * outer, top + d1 * outer, top + d0 * outer, (d0 + d1) * 0.5f);
            mesh.Quad(bottom + d0 * openingRadius, bottom + d1 * openingRadius, top + d1 * openingRadius, top + d0 * openingRadius, -(d0 + d1) * 0.5f);
        }
        RealmSites.Part("Well Lid", root, mesh.Build("Well Lid"), stoneMaterial, 7, true);
    }

    /// <summary>A flat-topped round slab whose top is at the given point.</summary>
    private void Disc(string name, Vector3 topCentre, float radius, float depth, Material material, int layer)
    {
        const int segments = 20;
        var mesh = new FacetMesh(topCentre.y - depth, topCentre.y, 0.3f);
        Vector3 bottomCentre = topCentre - Vector3.up * depth;
        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * Mathf.PI * 2f, a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
            var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
            var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
            mesh.Triangle(topCentre, topCentre + d0, topCentre + d1, Vector3.up);
            mesh.Quad(bottomCentre + d0, bottomCentre + d1, topCentre + d1, topCentre + d0, d0 + d1);
        }
        RealmSites.Part(name, root, mesh.Build(name), material, layer, true);
    }

    /// <summary>A sloped stone step from the ground outside the door up to the well floor.</summary>
    private void Ramp(Vector3 centre, Vector3 outward, float floorY)
    {
        Vector3 side = Vector3.Cross(Vector3.up, outward).normalized * (doorWidth * 0.5f);
        Vector3 inner = new Vector3(centre.x, floorY + 0.05f, centre.z) + outward * (innerRadius - 0.2f);
        Vector3 outerFoot = centre + outward * (innerRadius + wallThickness + 3.5f);
        outerFoot.y = Mathf.Min(RealmSites.Surface(world, outerFoot), floorY) - 0.05f;
        float bottom = Mathf.Min(outerFoot.y, floorY) - 1.5f;
        var mesh = new FacetMesh(bottom, floorY, 0.6f);
        Vector3 a = inner - side, b = inner + side, c = outerFoot + side, d = outerFoot - side;
        mesh.Quad(a, b, c, d, Vector3.up);
        Vector3 ab = new Vector3(a.x, bottom, a.z), bb = new Vector3(b.x, bottom, b.z), cb = new Vector3(c.x, bottom, c.z), db = new Vector3(d.x, bottom, d.z);
        mesh.Quad(b, c, cb, bb, side);
        mesh.Quad(a, d, db, ab, -side);
        mesh.Quad(c, d, db, cb, outward);
        RealmSites.Part("Well Ramp", root, mesh.Build("Well Ramp"), stoneMaterial, 7, true);
    }

    /// <summary>A thin ink circle on the floor round the plinth: where the bubbles rise.</summary>
    private void InkCircle(Vector3 centre, float radius)
    {
        const int count = 40;
        var mesh = new FacetMesh(centre.y - 0.1f, centre.y + 0.1f, 0.2f);
        for (int i = 0; i < count; i++)
        {
            float a0 = i / (float)count * Mathf.PI * 2f, a1 = (i + 1) / (float)count * Mathf.PI * 2f;
            var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
            var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            mesh.Quad(centre + d0 * (radius - 0.08f), centre + d1 * (radius - 0.08f), centre + d1 * (radius + 0.08f), centre + d0 * (radius + 0.08f), Vector3.up);
        }
        RealmSites.Part("Ink Circle", root, mesh.Build("Ink Circle"), inkMaterial, 0, false);
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
        if (flower != null) Destroy(flower.gameObject);
        foreach (GameObject part in gardenParts) if (part != null) Destroy(part);
    }
}
