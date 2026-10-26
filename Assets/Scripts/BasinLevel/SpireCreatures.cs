using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The basin level's creatures, placed round the Hanging Spire's two stage towers (Scene_Daniel): a goo pool
/// with the compost creature that turns corpses into more goo, bubble flowers by each tower, a sky spider on the
/// ground (its webs are walkable), and a bird flock overhead. Waits for the spire and the ground to exist.
/// Goo, flowers and compost grow back at home when lost (RegrowingSlots). Added by
/// Tools > Capstone > Basin Level > Add Basin Creatures Round The Spire.
/// </summary>
[DisallowMultipleComponent]
public sealed class SpireCreatures : MonoBehaviour
{
    [Header("Creatures (templates or prefabs)")]
    [SerializeField] private GooCreature goo;
    [SerializeField] private BubbleFlower bubbleFlower;
    [SerializeField] private SkySpider spider;
    [SerializeField] private CompostCreature compost;
    [SerializeField] private BirdCreature bird;
    [SerializeField] private Material inkMaterial;

    [Header("Layout")]
    [Tooltip("Goo pool: half way between the two stage towers, pushed out from the spire by this much (m).")]
    [SerializeField] private float poolOut = 6f;
    [Min(1f)] [SerializeField] private float poolRadius = 5.5f;
    [Range(0, 12)] [SerializeField] private int gooCount = 6;
    [Tooltip("Bubble flowers per stage tower, this far out from it (m).")]
    [Range(0, 4)] [SerializeField] private int flowersPerTower = 2;
    [SerializeField] private float flowerDistance = 11f;
    [SerializeField] private float spiderDistance = 14f;
    [Range(0, 40)] [SerializeField] private int birdCount = 16;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool placed;

    private readonly RegrowingSlots slots = new RegrowingSlots();
    private ProceduralWorld world;
    private HangingSpire spire;
    private SkySpider spiderCopy;
    private Transform root;
    private float waited;

    public void Setup(GooCreature gooTemplate, BubbleFlower flowerTemplate, SkySpider spiderTemplate, CompostCreature compostPrefab,
        BirdCreature birdPrefab, Material ink)
    {
        goo = gooTemplate;
        bubbleFlower = flowerTemplate;
        spider = spiderTemplate;
        compost = compostPrefab;
        bird = birdPrefab;
        inkMaterial = ink;
    }

    private void Update()
    {
        if (placed) { slots.Tick(Time.deltaTime); return; }
        if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
        if (spire == null) spire = FindFirstObjectByType<HangingSpire>();
        if (world == null || spire == null || !spire.Built || spire.StageTowers.Count < 2) return;
        waited += Time.deltaTime;
        // Creatures need the ground under them.
        if (!world.GenerationComplete && waited < 20f) return;
        Place();
    }

    private void Place()
    {
        placed = true;
        root = new GameObject("Spire Creatures (runtime)").transform;
        Vector3 centre = world.SinkholeCentre(spire.SinkholeIndex, spire.StageTowers[0].position.y, out _);
        Vector3 towerA = spire.StageTowers[0].position, towerB = spire.StageTowers[1].position;
        Vector3 outA = Flat(towerA - centre).normalized, outB = Flat(towerB - centre).normalized;

        // Goo pool and the compost creature between the two towers.
        Vector3 between = (towerA + towerB) * 0.5f;
        Vector3 outMid = Flat(between - centre).normalized;
        Vector3 pool = SurfaceAt(between + outMid * poolOut);
        Ring(pool + Vector3.up * 0.06f, poolRadius);
        for (int i = 0; i < gooCount && goo != null; i++)
        {
            float a = i / (float)Mathf.Max(1, gooCount) * Mathf.PI * 2f;
            float r = poolRadius * (i % 2 == 0 ? 0.45f : 0.75f);
            slots.Add(goo.gameObject, SurfaceAt(pool + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r), 12f);
        }
        if (compost != null) slots.Add(compost.gameObject, SurfaceAt(pool + outMid * (poolRadius + 4f)), 20f);

        // Bubble flowers out from each tower, either side.
        foreach ((Vector3 tower, Vector3 outward) in new[] { (towerA, outA), (towerB, outB) })
            for (int f = 0; f < flowersPerTower && bubbleFlower != null; f++)
            {
                float turn = flowersPerTower == 1 ? 0f : Mathf.Lerp(-40f, 40f, f / (float)(flowersPerTower - 1));
                Vector3 dir = Quaternion.Euler(0f, turn, 0f) * outward;
                slots.Add(bubbleFlower.gameObject, SurfaceAt(tower + dir * flowerDistance), 15f);
            }

        // The spider low over the ground beyond the second tower; its first web is spun there.
        if (spider != null)
        {
            Vector3 side = Vector3.Cross(Vector3.up, outB);
            spiderCopy = Instantiate(spider, SurfaceAt(towerB + outB * spiderDistance + side * 5f) + Vector3.up * 2.5f, Quaternion.identity);
            spiderCopy.name = "Spire Spider";
            spiderCopy.gameObject.SetActive(true);
        }

        // Birds circling above the towers (their flock manager is made by the first bird).
        if (bird != null && birdCount > 0)
        {
            var area = new GameObject("Spire Birds");
            area.transform.SetParent(root, false);
            area.transform.position = SurfaceAt(between) + Vector3.up * 16f;
            var spawn = area.AddComponent<BirdSpawnArea>();
            spawn.Configure(bird, birdCount, 6f, 4242);
            spawn.SpawnOnce();
        }
        Debug.Log($"Spire Creatures placed: goo pool at {pool}, {slots.Count} regrowing creatures.", this);
    }

    /// <summary>The built ground at a point (overhangs included); the analytic surface if none.</summary>
    private Vector3 SurfaceAt(Vector3 point)
    {
        world.ColumnHeights(point, out float surface, out _, out _);
        float top = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(point.x, surface + 30f, point.z), Vector3.down, 60f, 1 << 7, QueryTriggerInteraction.Ignore))
            if (world.OwnsCollider(hit.collider)) top = Mathf.Max(top, hit.point.y);
        return new Vector3(point.x, float.IsNegativeInfinity(top) ? surface : top, point.z);
    }

    private void Ring(Vector3 centre, float radius)
    {
        if (inkMaterial == null) return;
        var mesh = new FacetMesh(centre.y - 0.1f, centre.y + 0.1f, 0.2f);
        const int count = 40;
        for (int i = 0; i < count; i++)
        {
            float a0 = i / (float)count * Mathf.PI * 2f, a1 = (i + 1) / (float)count * Mathf.PI * 2f;
            var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
            var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            mesh.Quad(centre + d0 * (radius - 0.1f), centre + d1 * (radius - 0.1f), centre + d1 * (radius + 0.1f), centre + d0 * (radius + 0.1f), Vector3.up);
        }
        GameObject ring = RealmSites.Part("Goo Pool Ring", root, mesh.Build("Goo Pool Ring"), inkMaterial, 0, false);
        ring.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
        if (spiderCopy != null) Destroy(spiderCopy.gameObject);
    }
}
