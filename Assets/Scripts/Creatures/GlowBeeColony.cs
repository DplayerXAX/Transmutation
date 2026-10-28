using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Scene-owned generation of concealed hives, nectar patches and a bounded bee population.</summary>
public sealed class GlowBeeColony : MonoBehaviour
{
    [Header("Population")]
    [Range(1, 6)] [SerializeField] private int nestCount = 3;
    [Range(1, 8)] [SerializeField] private int beesPerNest = 4;
    [Range(2, 12)] [SerializeField] private int flowersPerNest = 5;
    [SerializeField] private int seed = 814;
    [Header("Placement")]
    [Tooltip("Optional authored ground positions. When nonempty, these replace rock/trunk discovery.")]
    [SerializeField] private Transform[] nestAnchors = new Transform[0];
    [SerializeField] private LayerMask groundLayers = 1 << 7;
    [SerializeField] private LayerMask sceneryLayers = (1 << 0) | (1 << 7);
    [Min(2f)] [SerializeField] private float nestSeparation = 9f;
    [Header("Predator")]
    [SerializeField] private SkySpider spiderPrefab;
    [SerializeField] private bool spawnSpider = true;
    private readonly List<GlowBeeNest> nests = new List<GlowBeeNest>();
    private readonly List<NectarFlower> flowers = new List<NectarFlower>();
    private readonly List<Creature> creatures = new List<Creature>();
    private readonly List<Vector3> positions = new List<Vector3>();
    private readonly List<SkySpider> spiders = new List<SkySpider>();
    private readonly List<Material> materials = new List<Material>();
    private readonly List<Vector3> sites = new List<Vector3>();
    private Material shell, dark, wing, glow, petal, stem;
    private Light sun;
    private float nextPredatorRefresh;
    private float nextPlayerRefresh;
    public PlayerHoneyEffects PlayerEffects { get; private set; }
    private System.Random random;
    public int SceneryLayers => sceneryLayers.value;
    public IReadOnlyList<GlowBeeNest> Nests => nests;
    public IReadOnlyList<NectarFlower> Flowers => flowers;

    private void Start()
    {
        random = new System.Random(seed);
        foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (light.gameObject.scene == gameObject.scene && light.type == LightType.Directional && light.isActiveAndEnabled)
            { sun = light; break; }
        shell = MakeMaterial("Glow bee amber shell", new Color(0.36f, 0.22f, 0.065f));
        dark = MakeMaterial("Glow bee dark bands", new Color(0.055f, 0.08f, 0.065f));
        wing = MakeMaterial("Glow bee pearl wings", new Color(0.65f, 0.86f, 0.75f));
        glow = MakeMaterial("Glow bee luminous nectar", new Color(0.55f, 1f, 0.23f), true);
        petal = MakeMaterial("Nectar flower violet petals", new Color(0.57f, 0.29f, 0.72f));
        stem = MakeMaterial("Nectar flower stem", new Color(0.1f, 0.22f, 0.12f));
        Physics.SyncTransforms();
        if (nestAnchors.Length > 0) FindAnchoredSites();
        else FindConcealedSites();
        foreach (Vector3 site in sites) BuildNest(site);
        if (nests.Count == 0)
            Debug.LogWarning("Glow bees: no valid nest sites. Check anchors, ground layers, or rock/trunk cover.", this);
        if (spawnSpider && spiderPrefab != null && nests.Count > 0) SpawnPredator();
    }

    private float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

    private void FindAnchoredSites()
    {
        foreach (Transform anchor in nestAnchors)
        {
            if (sites.Count >= nestCount) break;
            if (anchor == null || anchor.gameObject.scene != gameObject.scene ||
                !TryGround(anchor.position, out Vector3 ground, 1.25f)) continue;
            if (gameObject.scene.GetPhysicsScene().OverlapSphere(ground + Vector3.up * 0.55f, 0.52f,
                anchorClearance, sceneryLayers, QueryTriggerInteraction.Ignore) > 0)
            { Debug.LogWarning("Glow bees: nest anchor is obstructed: " + anchor.name, anchor); continue; }
            bool crowded = false;
            foreach (Vector3 site in sites) if (Vector3.Distance(site, ground) < nestSeparation) { crowded = true; break; }
            if (!crowded) sites.Add(ground);
        }
    }
    private readonly Collider[] anchorClearance = new Collider[1];

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.7f, 1f, 0.25f);
        foreach (Transform anchor in nestAnchors)
            if (anchor != null) Gizmos.DrawWireSphere(anchor.position + Vector3.up * 0.5f, 0.5f);
    }

    private void Update()
    {
        if (Time.time < nextPlayerRefresh) return;
        nextPlayerRefresh = Time.time + 1f;
        if (PlayerEffects != null && PlayerEffects.isActiveAndEnabled) return;
        foreach (var carrier in FindObjectsByType<PlayerCreatureCarrier>(FindObjectsSortMode.None))
            if (carrier.gameObject.scene == gameObject.scene)
            { PlayerEffects = PlayerHoneyEffects.Ensure(carrier); break; }
    }

    private void FindConcealedSites()
    {
        var candidates = new List<(Vector3 point, float score)>();
        foreach (Collider cover in FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (cover.gameObject.scene != gameObject.scene || cover.isTrigger || !cover.enabled ||
                cover.attachedRigidbody != null ||
                (!cover.name.Contains("Rock") && !cover.name.Contains("Trunk"))) continue;
            Bounds bounds = cover.bounds;
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6f;
                Vector3 point = bounds.center + new Vector3(Mathf.Cos(angle) * (bounds.extents.x + 0.9f),
                    0f, Mathf.Sin(angle) * (bounds.extents.z + 0.9f));
                if (!TryGround(point, out Vector3 ground) ||
                    Physics.CheckSphere(ground + Vector3.up * 0.55f, 0.52f, sceneryLayers, QueryTriggerInteraction.Ignore)) continue;
                Vector3 center = ground + Vector3.up * 0.6f;
                float score = 0f;
                if (sun != null && Physics.Raycast(center, -sun.transform.forward, 30f, sceneryLayers, QueryTriggerInteraction.Ignore)) score += 8f;
                if (Physics.Raycast(center, Vector3.up, 6f, sceneryLayers, QueryTriggerInteraction.Ignore)) score += 4f;
                for (int direction = 0; direction < 8; direction++)
                    if (Physics.Raycast(center, Quaternion.Euler(0f, direction * 45f, 0f) * Vector3.forward,
                            3f, sceneryLayers, QueryTriggerInteraction.Ignore)) score += 1f;
                candidates.Add((ground, score + Range(0f, 0.2f)));
            }
        }
        candidates.Sort((a, b) => b.score.CompareTo(a.score));
        foreach (var candidate in candidates)
        {
            bool crowded = false;
            foreach (Vector3 site in sites)
                if (Vector3.Distance(site, candidate.point) < nestSeparation) { crowded = true; break; }
            if (crowded) continue;
            sites.Add(candidate.point);
            if (sites.Count >= nestCount) break;
        }
    }

    private bool TryGround(Vector3 near, out Vector3 point, float height = 30f)
    {
        point = default;
        if (!gameObject.scene.GetPhysicsScene().Raycast(near + Vector3.up * height, Vector3.down,
                out RaycastHit hit, height + 35f, groundLayers, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.8f) return false;
        point = hit.point;
        return true;
    }

    private GameObject Root(string label, Vector3 position, Transform parent = null)
    {
        var root = new GameObject(label);
        SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        root.transform.SetParent(parent != null ? parent : transform, true);
        root.transform.position = position;
        return root;
    }

    private void BuildNest(Vector3 ground)
    {
        GameObject root = Root("Hidden Glow Bee Nest " + (nests.Count + 1), ground + Vector3.up * 0.5f);
        var nest = root.AddComponent<GlowBeeNest>();
        nest.Configure(this);
        nests.Add(nest);
        Part(root.transform, "Weathered hive", Vector3.zero, new Vector3(0.95f, 0.85f, 0.8f), shell);
        Part(root.transform, "Dark entrance", new Vector3(0f, 0.08f, 0.36f), new Vector3(0.34f, 0.28f, 0.14f), dark);
        for (int i = 0; i < 4; i++)
            Part(root.transform, "Wax ridge", new Vector3(0f, -0.27f + i * 0.17f, 0f),
                new Vector3(0.99f, 0.09f, 0.84f), shell);
        int firstFlower = flowers.Count;
        for (int i = 0; i < flowersPerNest; i++) BuildFlowerPatch(ground);
        // Do not create a colony that has no accessible food source.
        if (flowers.Count == firstFlower)
            Debug.LogWarning("Glow bee nest has no clear flower placement nearby.", nest);
        for (int i = 0; i < beesPerNest; i++)
        {
            GameObject beeRoot = Root("Glow Bee " + (i + 1), nest.Entrance + Vector3.up * (0.2f + i * 0.2f));
            var bee = beeRoot.AddComponent<GlowBee>();
            bee.Configure(this, nest, i * 0.7f);
            Part(beeRoot.transform, "Thorax", Vector3.zero, new Vector3(0.19f, 0.19f, 0.25f), shell);
            Renderer abdomen = Part(beeRoot.transform, "Firefly abdomen", new Vector3(0f, 0f, -0.18f),
                new Vector3(0.23f, 0.2f, 0.25f), glow);
            Part(beeRoot.transform, "Head", new Vector3(0f, 0.02f, 0.17f), Vector3.one * 0.16f, dark);
            Part(beeRoot.transform, "Left wing", new Vector3(-0.16f, 0.08f, 0f), new Vector3(0.32f, 0.025f, 0.16f), wing);
            Part(beeRoot.transform, "Right wing", new Vector3(0.16f, 0.08f, 0f), new Vector3(0.32f, 0.025f, 0.16f), wing);
            abdomen.gameObject.AddComponent<GlowBeeLight>().Configure(abdomen, new Color(0.6f, 1f, 0.25f), 1.5f);
        }
    }

    private void BuildFlowerPatch(Vector3 center)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            float angle = Range(0f, Mathf.PI * 2f);
            Vector3 near = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Range(3f, 7f);
            if (!TryGround(near, out Vector3 point, nestAnchors.Length > 0 ? 1.25f : 30f) || Physics.CheckSphere(point + Vector3.up * 0.5f,
                    0.42f, sceneryLayers, QueryTriggerInteraction.Ignore)) continue;
            bool crowded = false;
            foreach (NectarFlower other in flowers)
                if (Vector3.Distance(other.transform.position, point) < 0.9f) { crowded = true; break; }
            if (crowded) continue;
            GameObject root = Root("Nectar Flower " + (flowers.Count + 1), point);
            flowers.Add(root.AddComponent<NectarFlower>());
            Part(root.transform, "Stem", Vector3.up * 0.3f, new Vector3(0.055f, 0.65f, 0.055f), stem);
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 0.4f;
                Part(root.transform, "Petal", new Vector3(Mathf.Cos(a) * 0.17f, 0.65f, Mathf.Sin(a) * 0.17f),
                    new Vector3(0.24f, 0.09f, 0.24f), petal);
            }
            Part(root.transform, "Nectar center", Vector3.up * 0.69f, Vector3.one * 0.15f, glow);
            return;
        }
    }

    internal NectarFlower FindFlower(Vector3 position)
    {
        NectarFlower best = null;
        float distance = float.PositiveInfinity;
        foreach (NectarFlower flower in flowers)
        {
            if (flower == null || !flower.isActiveAndEnabled || !flower.HasNectar) continue;
            float candidate = (flower.SippingPoint - position).sqrMagnitude;
            if (candidate >= distance) continue;
            best = flower;
            distance = candidate;
        }
        return best;
    }

    internal bool TryFindPredator(Vector3 position, float range, out Vector3 threat)
    {
        threat = default;
        if (Time.time >= nextPredatorRefresh)
        {
            nextPredatorRefresh = Time.time + 1f;
            CreatureRegistry.SnapshotCombat(gameObject.scene, creatures, positions);
            spiders.Clear();
            foreach (Creature creature in creatures) if (creature is SkySpider spider) spiders.Add(spider);
        }
        bool found = false;
        float closest = range * range;
        foreach (SkySpider spider in spiders)
        {
            if (spider == null || !spider.isActiveAndEnabled || spider.IsDead || spider.IsCarried || spider.IsBubbleCaptured) continue;
            float distance = (spider.transform.position - position).sqrMagnitude;
            if (distance > closest) continue;
            closest = distance;
            threat = spider.transform.position;
            found = true;
        }
        return found;
    }

    internal GlowHoney CreateHoney(GlowBeeNest nest, int slot)
    {
        if (nest == null || nest.IsDead) return null;
        Vector3 local = default;
        GlowHoney[] existing = nest.GetComponentsInChildren<GlowHoney>();
        bool free = false;
        for (int i = 0; i < 4; i++)
        {
            float angle = (slot + i) * Mathf.PI * 0.5f;
            local = new Vector3(Mathf.Cos(angle) * 0.35f, 0.48f, Mathf.Sin(angle) * 0.35f);
            free = true;
            foreach (GlowHoney other in existing)
                if (!other.IsCarried && (other.transform.localPosition - local).sqrMagnitude < 0.04f)
                { free = false; break; }
            if (free) break;
        }
        if (!free) return null;
        GameObject root = Root("Glowing Honey", nest.transform.TransformPoint(local), nest.transform);
        var honey = root.AddComponent<GlowHoney>();
        var body = root.GetComponent<Rigidbody>();
        // Held by the wax shelf until picked up; the standard carrier restores dynamic physics on drop.
        body.useGravity = true;
        body.isKinematic = true;
        Renderer bead = Part(root.transform, "Luminous honey drop", Vector3.zero, new Vector3(0.32f, 0.38f, 0.32f), glow);
        bead.gameObject.AddComponent<GlowBeeLight>().Configure(bead, new Color(0.7f, 1f, 0.24f), 2.5f);
        return honey;
    }

    private void SpawnPredator()
    {
        // A web at the edge of a foraging patch makes the predator relevant without covering the hive.
        Vector3 position = flowers.Count > 0 ? flowers[0].SippingPoint + Vector3.right * 4f : nests[0].Entrance + Vector3.right * 7f;
        SkySpider spider = Instantiate(spiderPrefab, position, Quaternion.identity, transform);
        spider.name = "Glow Bee Predator - Sky Spider";
    }

    private Material MakeMaterial(string label, Color color, bool emission = false)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = label };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", emission ? 0.65f : 0.25f);
        if (emission) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 1.5f); }
        materials.Add(material);
        return material;
    }

    private static Renderer Part(Transform parent, string label, Vector3 position, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        part.name = label;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        var collider = part.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        var renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    private void OnDestroy()
    {
        foreach (Material material in materials) if (material != null) Destroy(material);
    }
}
