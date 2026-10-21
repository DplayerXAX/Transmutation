using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Web Gorge: a tall smooth mesa near the Hanging Spire that no hand can climb, with a sky spider's den
/// at the foot of the gorge in front of it and a steady wind blowing from the den up onto the mesa.
/// No single creature gets you up; it takes a chain of them:
///   1. lead fuzz creatures here (they follow you),
///   2. carry the bubble flower from the den and shoot a bubble at a fuzz,
///   3. the gorge wind lifts light bubbles (never one with you inside: too heavy) up to a web sail on the
///      mesa top, which bursts the bubble and drops the fuzz there,
///   4. the spider senses prey above it and runs up after it, laying a silk ramp from its den to the top.
/// Walk up the ramp to the seed. The ramp stays for later visits.
/// Put it on any object in the scene; it builds itself once the world and the spire exist.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(500)] // after the bubbles have set their own velocity
public sealed class WebGorge : MonoBehaviour
{
    [Header("Prefabs And Materials")]
    [SerializeField] private SkySpider spiderPrefab;
    [SerializeField] private BubbleFlower flowerPrefab;
    [SerializeField] private Material rockMaterial;
    [SerializeField] private Material silkMaterial;
    [SerializeField] private Material inkMaterial;
    [SerializeField] private Material seedMaterial;

    [Header("Layout")]
    [SerializeField] private int seed = 21;
    [Tooltip("Distance range from the spire's foot.")]
    [SerializeField] private Vector2 distanceFromSpire = new Vector2(45f, 85f);
    [Min(2f)] [SerializeField] private float mesaRadius = 6f;
    [Tooltip("Height of the mesa top above the highest ground under it.")]
    [Min(4f)] [SerializeField] private float mesaHeight = 15f;
    [Tooltip("Horizontal distance from the spider's den to the mesa's edge; long enough for a gentle ramp.")]
    [Min(6f)] [SerializeField] private float denDistance = 30f;

    [Header("Wind")]
    [Tooltip("How fast the wind pushes light bubbles towards the mesa (m/s).")]
    [Min(0f)] [SerializeField] private float windSpeed = 2.6f;
    [Tooltip("How fast it lifts them up to the sail (m/s).")]
    [Min(0f)] [SerializeField] private float liftSpeed = 1.8f;
    [Tooltip("Half width of the wind corridor (metres).")]
    [Min(1f)] [SerializeField] private float windWidth = 7f;

    [Header("Ramp")]
    [Tooltip("Seconds the spider takes to run up and lay the ramp.")]
    [Min(0.5f)] [SerializeField] private float growTime = 7f;
    [Min(0.4f)] [SerializeField] private float rampWidth = 1.6f;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool built;
    [SerializeField] private bool rampDone;
    [SerializeField] private float rampGrowth;
    [SerializeField] private int bubblesInWind;

    private const string SavedKey = "Realm.WebGorge.Ramp";
    private const string SeedId = "gorge_seed";

    private ProceduralWorld world;
    private Transform root;
    private SkySpider spider;
    private BubbleFlower flower;
    private Vector3 mesaTop, denPoint, rampFoot, rampTop, sail, across;
    private readonly List<GameObject> segments = new List<GameObject>();
    private readonly List<Transform> streaks = new List<Transform>();
    private TrappingBubble[] bubbles = new TrappingBubble[0];
    private float bubbleScan, preyScan, waited;

    public bool RampDone => rampDone;

    public void Setup(SkySpider spiderTemplate, BubbleFlower flowerTemplate, Material rock, Material silk, Material ink, Material seedPod)
    {
        spiderPrefab = spiderTemplate;
        flowerPrefab = flowerTemplate;
        rockMaterial = rock;
        silkMaterial = silk;
        inkMaterial = ink;
        seedMaterial = seedPod;
    }

    private void Update()
    {
        if (!built)
        {
            if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
            waited += Time.deltaTime;
            if (RealmSites.ReadyToPlace(world, waited)) Build();
            return;
        }
        if (root == null) return;
        AnimateWind();
        if (rampDone) return;

        if (rampGrowth > 0f)
        {
            Grow(rampGrowth + Time.deltaTime / growTime);
            return;
        }
        // The spider hunts as soon as a fuzz creature sits on the mesa top.
        preyScan -= Time.deltaTime;
        if (preyScan > 0f) return;
        preyScan = 0.3f;
        foreach (FuzzCreature fuzz in FindObjectsByType<FuzzCreature>(FindObjectsSortMode.None))
        {
            Vector3 p = fuzz.transform.position;
            if (fuzz.IsCarried || fuzz.IsBubbleCaptured) continue;
            if (RealmSites.Flat(p - mesaTop).magnitude < mesaRadius && Mathf.Abs(p.y - mesaTop.y) < 2.5f)
            {
                if (spider != null) spider.SetSimulationEnabled(false);
                Grow(0.001f);
                break;
            }
        }
    }

    // ---------------- Wind ----------------

    private void FixedUpdate()
    {
        if (root == null) return;
        bubbleScan -= Time.fixedDeltaTime;
        if (bubbleScan <= 0f)
        {
            bubbleScan = 0.25f;
            bubbles = FindObjectsByType<TrappingBubble>(FindObjectsSortMode.None);
        }
        bubblesInWind = 0;
        float length = RealmSites.Flat(sail - denPoint).magnitude + 4f;
        foreach (TrappingBubble bubble in bubbles)
        {
            if (bubble == null || bubble.IsPopped) continue;
            // A bubble with the player inside is far too heavy for the wind.
            if (bubble.IsOccupied && bubble.Occupant.GetComponent<SmoothFirstPersonController>() != null) continue;
            Vector3 offset = bubble.transform.position - denPoint;
            float along = Vector3.Dot(RealmSites.Flat(offset), across);
            float aside = Vector3.Cross(across, RealmSites.Flat(offset)).y;
            if (along < -6f || along > length || Mathf.Abs(aside) > windWidth) continue;
            if (offset.y < -2f || bubble.transform.position.y > sail.y + 6f) continue;

            Rigidbody body = bubble.GetComponent<Rigidbody>();
            // Steer towards the corridor's centre line, forward, and up to the sail's height.
            Vector3 push = across * windSpeed - Vector3.Cross(Vector3.up, across) * (aside * 0.4f);
            float lift = Mathf.Clamp((sail.y + 1f - bubble.transform.position.y) * 0.6f, -0.5f, liftSpeed);
            body.position += (push + Vector3.up * lift) * Time.fixedDeltaTime;
            bubblesInWind++;
        }
    }

    /// <summary>Short silk wisps drifting along the corridor, so the wind can be seen.</summary>
    private void AnimateWind()
    {
        if (streaks.Count == 0) return;
        float length = RealmSites.Flat(sail - denPoint).magnitude;
        for (int i = 0; i < streaks.Count; i++)
        {
            float t = Mathf.Repeat(Time.time * windSpeed / length + i / (float)streaks.Count, 1f);
            float aside = Mathf.Sin(i * 2.39f) * windWidth * 0.6f;
            Vector3 p = Vector3.Lerp(denPoint, sail, t) + Vector3.Cross(Vector3.up, across) * aside;
            p.y = Mathf.Lerp(denPoint.y + 2f, sail.y + 1f, Mathf.SmoothStep(0f, 1f, t)) + Mathf.Sin(i * 1.7f) * 1.5f;
            streaks[i].position = p;
            streaks[i].localScale = Vector3.one * Mathf.Sin(t * Mathf.PI);
        }
    }

    // ---------------- Building ----------------

    private void Build()
    {
        built = true;
        float footprint = (denDistance + mesaRadius * 2f) * 0.5f + 4f;
        if (!RealmSites.FindSpot(world, footprint, distanceFromSpire.x, distanceFromSpire.y, seed, out Vector3 centre))
        {
            Debug.LogWarning("Web Gorge: no free spot near the spire.", this);
            return;
        }

        var random = new System.Random(seed);
        float angle = (float)random.NextDouble() * Mathf.PI * 2f;
        across = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        // The den faces the spire, so players arrive at the den first.
        Vector3 toSpire = RealmSites.Flat(RealmSites.AreaCentre(world) - centre);
        if (Vector3.Dot(across, toSpire) > 0f) across = -across;
        float half = (denDistance + mesaRadius) * 0.5f;
        Vector3 mesaBase = centre + across * half;
        Vector3 denBase = centre - across * half;

        root = new GameObject("Web Gorge").transform;
        root.position = centre;

        RealmSites.SurfaceRange(world, mesaBase, mesaRadius, out float mesaLow, out float mesaHigh);
        mesaTop = new Vector3(mesaBase.x, mesaHigh + mesaHeight, mesaBase.z);
        denPoint = new Vector3(denBase.x, RealmSites.Surface(world, denBase), denBase.z);
        Mesa(mesaBase, mesaLow - 3f, mesaTop.y, random);

        rampTop = mesaTop - across * (mesaRadius * 0.82f);
        rampFoot = denPoint + across * 1.5f;
        sail = mesaTop - across * (mesaRadius * 0.35f) + Vector3.up * 2f;

        Sail();
        InkRing(mesaTop + Vector3.up * 0.03f, 2.6f);
        InkRing(denPoint + Vector3.up * 0.05f, 3.2f);
        RealmSites.Seed(SeedId, mesaTop + Vector3.up * 1f + across * (mesaRadius * 0.4f), seedMaterial, root);
        WindStreaks();

        if (spiderPrefab != null)
        {
            spider = Instantiate(spiderPrefab, denPoint + Vector3.up * 1.5f, Quaternion.identity);
            spider.name = "Gorge Spider";
        }
        if (flowerPrefab != null)
        {
            Vector3 beside = denPoint + Vector3.Cross(Vector3.up, across) * 3.5f;
            beside.y = RealmSites.Surface(world, beside) + 0.4f;
            flower = Instantiate(flowerPrefab, beside, Quaternion.identity);
            flower.name = "Gorge Bubble Flower";
            // Placed before the ground there may be built: held still until it exists (it fell out of the world).
            flower.gameObject.AddComponent<SettleWhenGroundReady>();
        }

        BuildRampSegments();
        if (PlayerPrefs.GetInt(SavedKey, 0) == 1) Grow(1f);
    }

    /// <summary>An irregular, slightly tapering stone mesa with a flat top; Default layer, so it cannot be climbed.</summary>
    private void Mesa(Vector3 basePoint, float bottomY, float topY, System.Random random)
    {
        const int sides = 11;
        var mesh = new FacetMesh(bottomY, topY, (float)random.NextDouble());
        var lower = new Vector3[sides];
        var upper = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = (i + (float)random.NextDouble() * 0.4f) / sides * Mathf.PI * 2f;
            float r = mesaRadius * Mathf.Lerp(0.88f, 1.12f, (float)random.NextDouble());
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            lower[i] = new Vector3(basePoint.x, bottomY, basePoint.z) + dir * r * 1.12f;
            upper[i] = new Vector3(basePoint.x, topY, basePoint.z) + dir * r;
        }
        Vector3 axisTop = new Vector3(basePoint.x, topY, basePoint.z);
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            mesh.Quad(lower[i], lower[j], upper[j], upper[i], RealmSites.Flat((lower[i] + lower[j]) * 0.5f - basePoint));
            mesh.Triangle(axisTop, upper[i], upper[j], Vector3.up);
        }
        RealmSites.Part("Mesa", root, mesh.Build("Mesa"), rockMaterial, 0, true);
    }

    /// <summary>
    /// A web stretched between two posts on the mesa top, facing the wind. Bubbles that hit it burst,
    /// and whatever was inside drops onto the mesa.
    /// </summary>
    private void Sail()
    {
        Vector3 side = Vector3.Cross(Vector3.up, across).normalized;
        const float width = 6f, height = 4.5f;
        Vector3 baseCentre = new Vector3(sail.x, mesaTop.y, sail.z);
        var mesh = new FacetMesh(mesaTop.y, mesaTop.y + height, 0.8f);
        Vector3 left = baseCentre - side * width * 0.5f, right = baseCentre + side * width * 0.5f;
        mesh.Tube(left, left + Vector3.up * height, 0.12f, 6);
        mesh.Tube(right, right + Vector3.up * height, 0.12f, 6);
        for (int i = 0; i <= 6; i++)
        {
            float h = 0.6f + i / 6f * (height - 0.8f);
            float droop = Mathf.Sin(i * 1.3f) * 0.25f;
            mesh.Tube(left + Vector3.up * h, baseCentre + Vector3.up * (h - 0.5f + droop), 0.03f, 4);
            mesh.Tube(baseCentre + Vector3.up * (h - 0.5f + droop), right + Vector3.up * h, 0.03f, 4);
        }
        for (int i = 1; i < 6; i++)
        {
            Vector3 x = Vector3.Lerp(left, right, i / 6f);
            mesh.Tube(x + Vector3.up * 0.5f, x + Vector3.up * (height - 0.3f), 0.025f, 4);
        }
        GameObject part = RealmSites.Part("Web Sail", root, mesh.Build("Web Sail"), silkMaterial, 0, false);
        // The catching surface: a thin wall the bubbles bump into.
        var catcher = new GameObject("Sail Catcher", typeof(BoxCollider));
        catcher.transform.SetParent(part.transform, false);
        catcher.transform.SetPositionAndRotation(baseCentre + Vector3.up * height * 0.55f, Quaternion.LookRotation(across, Vector3.up));
        catcher.GetComponent<BoxCollider>().size = new Vector3(width, height * 0.9f, 0.3f);
    }

    private void InkRing(Vector3 centre, float radius)
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
        RealmSites.Part("Ink Ring", root, mesh.Build("Ink Ring"), inkMaterial, 0, false);
    }

    private void WindStreaks()
    {
        var mesh = new FacetMesh(0f, 1f, 0.5f);
        mesh.Tube(-across * 0.8f, across * 0.8f, 0.018f, 4);
        Mesh shared = mesh.Build("Wind Streak");
        for (int i = 0; i < 14; i++)
            streaks.Add(RealmSites.Part("Wind " + i, root, shared, silkMaterial, 2, false).transform);
    }

    /// <summary>
    /// The silk ramp from the den up to the mesa edge, cut into pieces that appear one after another
    /// behind the running spider. Each piece has its own walkable deck (Ground layer).
    /// </summary>
    private void BuildRampSegments()
    {
        const int pieces = 36;
        const int strands = 5;
        Vector3 side = Vector3.Cross(Vector3.up, (rampTop - rampFoot).normalized).normalized;
        float span = Vector3.Distance(rampFoot, rampTop);
        Vector3 Point(float t, float lateral)
        {
            Vector3 p = Vector3.Lerp(rampFoot, rampTop, t) + side * lateral;
            p.y -= Mathf.Sin(t * Mathf.PI) * span * 0.03f;
            return p;
        }

        for (int i = 0; i < pieces; i++)
        {
            float t0 = i / (float)pieces, t1 = (i + 1) / (float)pieces;
            var mesh = new FacetMesh(rampFoot.y, rampTop.y, 0.7f);
            for (int s = 0; s < strands; s++)
            {
                float lateral = Mathf.Lerp(-rampWidth * 0.5f, rampWidth * 0.5f, s / (float)(strands - 1));
                mesh.Tube(Point(t0, lateral), Point(t1, lateral), 0.03f, 4);
            }
            mesh.Tube(Point(t0, -rampWidth * 0.5f), Point(t0, rampWidth * 0.5f), 0.022f, 4);
            mesh.Tube(Point(t0, -rampWidth * 0.6f) + Vector3.up * 0.8f, Point(t1, -rampWidth * 0.6f) + Vector3.up * 0.8f, 0.02f, 4);
            mesh.Tube(Point(t0, rampWidth * 0.6f) + Vector3.up * 0.8f, Point(t1, rampWidth * 0.6f) + Vector3.up * 0.8f, 0.02f, 4);

            GameObject piece = RealmSites.Part("Ramp " + i, root, mesh.Build("Ramp " + i), silkMaterial, 7, false);
            Vector3 a = Point(t0, 0f), b = Point(t1, 0f);
            var deck = new GameObject("Deck", typeof(BoxCollider)) { layer = 7 };
            deck.transform.SetParent(piece.transform, false);
            deck.transform.SetPositionAndRotation((a + b) * 0.5f - Vector3.up * 0.06f, Quaternion.LookRotation(b - a, Vector3.up));
            deck.GetComponent<BoxCollider>().size = new Vector3(rampWidth, 0.12f, Vector3.Distance(a, b) + 0.05f);
            piece.SetActive(false);
            segments.Add(piece);
        }
    }

    private void Grow(float growth)
    {
        rampGrowth = Mathf.Clamp01(growth);
        int visible = Mathf.CeilToInt(rampGrowth * segments.Count);
        for (int i = 0; i < segments.Count; i++) segments[i].SetActive(i < visible);
        // The spider runs ahead of the silk it lays.
        if (spider != null && !rampDone) spider.transform.position = Vector3.Lerp(rampFoot, rampTop, rampGrowth) + Vector3.up * 0.4f;
        if (rampGrowth >= 1f)
        {
            rampDone = true;
            if (spider != null) spider.SetSimulationEnabled(true);
            PlayerPrefs.SetInt(SavedKey, 1);
            PlayerPrefs.Save();
        }
    }

    [ContextMenu("Forget Ramp")]
    private void ForgetRamp()
    {
        PlayerPrefs.DeleteKey(SavedKey);
        rampDone = false;
        rampGrowth = 0f;
        foreach (GameObject piece in segments) if (piece != null) piece.SetActive(false);
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
        if (spider != null) Destroy(spider.gameObject);
        if (flower != null) Destroy(flower.gameObject);
    }
}
