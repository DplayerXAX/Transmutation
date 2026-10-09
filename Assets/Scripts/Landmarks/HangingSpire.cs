using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Landmark: a huge hollow spire built around one of the ProceduralWorld sinkholes, and its
/// upside-down twin hanging from the inner world's ceiling under the same hole.
///
/// Outside: ledges spiral up the wall; the whole wall is steep enough for HandClimber.
/// Top: a beam spans the crown with a seed above its middle.
/// Inside: the hollow core is the sinkhole shaft. Falling in is slowed by an updraft and drops the
/// player out of the hanging twin's mouth, onto an inscription in the inner world.
/// The inscription's glyph also stands over the door, and appears on a stele once learned.
/// </summary>
[DisallowMultipleComponent]
public sealed class HangingSpire : MonoBehaviour
{
    [SerializeField] private ProceduralWorld world;

    [Header("Materials")]
    [SerializeField] private Material spireMaterial;
    [SerializeField] private Material innerSpireMaterial;
    [SerializeField] private Material glyphMaterial;
    [SerializeField] private Material seedMaterial;

    [Header("Seed Model")]
    [Tooltip("An object in the scene to copy as the crown seed (the menu uses the one named Colorful). " +
             "The copy keeps its materials and parts; the original stays where it is.")]
    [SerializeField] private GameObject seedSource;
    [Tooltip("Used when there is no Seed Source: a single mesh for the crown seed. Empty = the built-in pod shape.")]
    [SerializeField] private Mesh seedModel;
    [SerializeField] private Material seedModelMaterial;
    [Tooltip("Turns the model upright (the Cylinder needs -90 on X).")]
    [SerializeField] private Vector3 seedModelRotation = new Vector3(-90f, 0f, 0f);
    [Tooltip("Height of the seed at the crown (metres).")]
    [Min(0.5f)] [SerializeField] private float crownSeedSize = 8f;
    [Tooltip("How high above the crown teeth it floats (seen from the ground) until the player climbs up.")]
    [Min(0f)] [SerializeField] private float crownSeedFloat = 5f;

    [Header("Placement")]
    [Tooltip("Picks the sinkhole whose distance from the spawn is closest to this.")]
    [Min(0f)] [SerializeField] private float preferredDistance = 85f;
    [Tooltip("Use this sinkhole index instead (-1 = choose automatically).")]
    [SerializeField] private int sinkholeOverride = -1;

    [Header("Shape")]
    [SerializeField] private int seed = 11;
    [Min(10f)] [SerializeField] private float height = 72f;
    [Range(0, 24)] [SerializeField] private int ledgeCount = 11;
    [Range(0f, 1f)] [SerializeField] private float windows = 0.22f;
    [Tooltip("Length of the hanging twin as a share of the inner world's height there.")]
    [Range(0.2f, 0.9f)] [SerializeField] private float hangingLength = 0.6f;
    [SerializeField] private bool halo = true;

    [Header("Rewards")]
    [SerializeField] private string seedId = "spire_seed";
    [SerializeField] private string glyphId = "spire";

    [Header("Creatures")]
    [Tooltip("A tentacle creature in the scene to copy. Copies crawl over the tower and wander round its base.")]
    [SerializeField] private TentacleCreature tentacleTemplate;
    [Range(0, 8)] [SerializeField] private int wallCrawlers = 3;
    [Range(0, 8)] [SerializeField] private int groundCrawlers = 2;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool built;
    [SerializeField] private int sinkholeIndex = -1;

    private Transform root;
    private Transform haloTransform;
    private float haloSpin;

    private void Update()
    {
        if (!built)
        {
            if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
            if (world != null && world.LayoutReady && world.SinkholeCount > 0) Build();
            return;
        }

        if (haloTransform != null)
        {
            haloSpin += Time.deltaTime * 2.5f;
            haloTransform.localRotation = Quaternion.Euler(9f + Mathf.Sin(Time.time * 0.11f) * 4f, haloSpin, 6f);
        }
    }

    private void OnDisable()
    {
        if (root != null) Destroy(root.gameObject);
        root = null;
        built = false;
    }

    private void Build()
    {
        built = true;
        var random = new System.Random(seed);
        sinkholeIndex = sinkholeOverride >= 0 && sinkholeOverride < world.SinkholeCount ? sinkholeOverride : PickSinkhole();

        // Ground level around the hole: the lowest rim point, so the base is buried everywhere.
        Vector3 approx = world.SinkholeCentre(sinkholeIndex, world.transform.position.y, out float holeRadius);
        world.ColumnHeights(approx, out float surfaceAtCentre, out _, out _);
        Vector3 top = world.SinkholeCentre(sinkholeIndex, surfaceAtCentre, out _);
        float innerRadius = holeRadius * 1.3f + 1.2f;
        float rimLow = float.MaxValue, rimHigh = float.MinValue, ceilingHigh = float.MinValue, groundLow = float.MaxValue, groundHigh = float.MinValue;
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI / 8f;
            Vector3 p = top + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (innerRadius + 2f);
            world.ColumnHeights(p, out float s, out float c, out float g);
            rimLow = Mathf.Min(rimLow, s);
            rimHigh = Mathf.Max(rimHigh, s);
            ceilingHigh = Mathf.Max(ceilingHigh, c);
            groundLow = Mathf.Min(groundLow, g);
            groundHigh = Mathf.Max(groundHigh, g);
        }
        float groundY = rimLow;
        Vector3 basePoint = world.SinkholeCentre(sinkholeIndex, groundY, out _);

        root = new GameObject("Hanging Spire (runtime)").transform;
        root.SetParent(transform, false);
        root.position = basePoint;

        // The door faces the spawn, so the glyph above it is seen on the way in.
        Vector3 toSpawn = world.transform.position - basePoint;
        float doorAngle = Mathf.Atan2(toSpawn.z, toSpawn.x);

        // ---- Upper spire ----
        var upper = new SpireShape
        {
            height = height + (rimHigh - rimLow) * 0.5f,
            bury = 9f,
            innerRadius = innerRadius,
            wallBase = 2.6f,
            wallTop = 2.2f, // wide enough to stand on after climbing over the top
            flare = 5f + (rimHigh - rimLow) * 0.3f,
            lean = 3.5f,
            crown = 7f,
            doorAngle = doorAngle,
            doorHalfAngle = 0.2f,
            doorHeight = 5.5f + (rimHigh - rimLow) * 0.3f,
            windows = windows,
            noiseSeed = 3.3f + seed * 1.71f,
        };
        Mesh upperMesh = LandmarkShapes.Spire(upper, random, ledgeCount, beam: true, flipY: false, out Vector3 beamCentre);
        GameObject spireObject = Part("Spire", root, upperMesh, spireMaterial, collide: true, shadows: true);

        // ---- Hanging twin in the inner world ----
        Vector3 ceilingPoint = world.SinkholeCentre(sinkholeIndex, ceilingHigh, out _);
        float innerHeight = Mathf.Max(6f, ceilingHigh - groundLow);
        // Keep the mouth well above the highest inner ground nearby.
        float hangLength = Mathf.Min(innerHeight * hangingLength, ceilingHigh - groundHigh - 8f);
        var lower = new SpireShape
        {
            height = Mathf.Max(6f, hangLength),
            bury = 6f,
            innerRadius = innerRadius * 0.9f,
            topScale = 0.6f,
            wallBase = 2f,
            wallTop = 0.9f,
            flare = 4f,
            lean = 2f,
            crown = 3f,
            doorHalfAngle = 0f,
            windows = 0f,
            noiseSeed = 9.1f + seed * 0.83f,
        };
        Mesh lowerMesh = LandmarkShapes.Spire(lower, random, 0, beam: false, flipY: true, out _);
        GameObject hanging = Part("Hanging Twin", root, lowerMesh, innerSpireMaterial != null ? innerSpireMaterial : spireMaterial, collide: true, shadows: false);
        hanging.transform.position = ceilingPoint;
        Vector3 tip = lower.Axis(lower.height);
        Vector3 mouth = ceilingPoint + new Vector3(tip.x, -lower.height, tip.z);

        // ---- Halo above the crown ----
        if (halo)
        {
            GameObject ring = Part("Halo", root, LandmarkShapes.Halo(innerRadius * 1.9f, 0.35f, seed), spireMaterial, collide: false, shadows: false);
            haloTransform = ring.transform;
            haloTransform.localPosition = upper.Axis(upper.height) + Vector3.up * (upper.crown + 9f);
        }

        // ---- Updrafts: inside the tower, and down the slanted shaft to the hanging mouth ----
        // Stops below the beam, so walking on the beam feels normal.
        Draft("Tower Draft", basePoint + upper.Axis((upper.height - 6f) * 0.5f),
            Vector3.up, upper.height - 6f, innerRadius * 0.85f);
        Vector3 shaftTop = basePoint;
        Vector3 shaftBottom = mouth;
        Draft("Shaft Draft", (shaftTop + shaftBottom) * 0.5f, (shaftTop - shaftBottom).normalized,
            Vector3.Distance(shaftTop, shaftBottom) + 2f, holeRadius * 0.9f);

        // ---- Rewards ----
        Mesh doorGlyph = BuildGlyph(1.8f, 0.12f);
        GameObject sign = Part("Door Glyph", root, doorGlyph, glyphMaterial, collide: false, shadows: false);
        float signY = upper.doorHeight + 2.2f;
        Vector3 doorDirection = new Vector3(Mathf.Cos(doorAngle), 0f, Mathf.Sin(doorAngle));
        Vector3 signAxis = upper.Axis(signY);
        float signRadius = upper.Outer(doorAngle, signY) + 0.25f;
        sign.transform.localPosition = new Vector3(signAxis.x, signY, signAxis.z) + doorDirection * signRadius - Vector3.up * 0.9f;
        sign.transform.localRotation = Quaternion.LookRotation(-doorDirection, Vector3.up);

        if (!string.IsNullOrEmpty(seedId))
        {
            if (seedSource != null)
            {
                // A copy of the scene object, floating high over the crown and cracking apart now and then.
                LandmarkPickup seedPickup = Pickup("Seed", basePoint + beamCentre + Vector3.up * (crownSeedSize * 0.5f + 0.4f), LandmarkPickup.Kind.Seed);
                seedPickup.id = seedId;
                seedPickup.hoverHeight = 0f;
                seedPickup.triggerRadius = crownSeedSize * 0.5f + 1f;
                Transform body = CopySeed(seedPickup.transform);
                var cracking = new GameObject("Seed Eruption").AddComponent<SeedEruption>();
                cracking.transform.SetParent(root, false);
                cracking.Init(seedPickup, body, crownSeedSize, upper.crown + crownSeedFloat + crownSeedSize * 0.5f);
            }
            else if (seedModel != null && seedModelMaterial != null)
            {
                // A big seed floating high over the crown and cracking apart now and then.
                LandmarkPickup seedPickup = Pickup("Seed", basePoint + beamCentre + Vector3.up * (crownSeedSize * 0.5f + 0.4f), LandmarkPickup.Kind.Seed);
                seedPickup.id = seedId;
                seedPickup.hoverHeight = 0f;
                seedPickup.triggerRadius = crownSeedSize * 0.5f + 1f;
                Quaternion upright = Quaternion.Euler(seedModelRotation);
                GameObject body = Part("Seed Body", seedPickup.transform, seedModel, seedModelMaterial, collide: false, shadows: true);
                Vector3 extent = SeedEruption.UprightSize(seedModel, upright);
                float scale = crownSeedSize / Mathf.Max(1e-5f, extent.y);
                body.transform.localRotation = upright;
                body.transform.localScale = Vector3.one * scale;
                body.transform.localPosition = -(upright * seedModel.bounds.center) * scale;

                var cracking = new GameObject("Seed Eruption").AddComponent<SeedEruption>();
                cracking.transform.SetParent(root, false);
                cracking.Init(seedPickup, body.transform, crownSeedSize, upper.crown + crownSeedFloat + crownSeedSize * 0.5f);
            }
            else
            {
                LandmarkPickup seedPickup = Pickup("Seed", basePoint + beamCentre + Vector3.up * 1.2f, LandmarkPickup.Kind.Seed);
                seedPickup.id = seedId;
                GameObject pod = Part("Pod", seedPickup.transform, LandmarkShapes.Seed(seed), seedMaterial, collide: false, shadows: false);
                // Twice life size so it reads from the rim, two metres away.
                pod.transform.localPosition = Vector3.down * 0.5f;
                pod.transform.localScale = Vector3.one * 2f;
            }
        }

        if (!string.IsNullOrEmpty(glyphId))
        {
            LandmarkPickup inscription = Pickup("Inscription", mouth + Vector3.down * 0.5f, LandmarkPickup.Kind.Inscription);
            inscription.glyphId = glyphId;
            inscription.snapToGround = true;
            inscription.hoverHeight = 1.4f;
            Mesh small = BuildGlyph(0.9f, 0.07f);
            GameObject mark = Part("Glyph", inscription.transform, small, glyphMaterial, collide: false, shadows: false);
            mark.transform.localPosition = Vector3.down * 0.6f;
        }

        SpawnCreatures(spireObject.GetComponent<Collider>(), basePoint, upper.height, doorAngle, random);
        Debug.Log($"Hanging Spire built on sinkhole {sinkholeIndex} at {basePoint}.");
    }

    private int PickSinkhole()
    {
        int best = 0;
        float bestScore = float.MaxValue;
        Vector3 spawn = world.transform.position;
        for (int i = 0; i < world.SinkholeCount; i++)
        {
            Vector3 centre = world.SinkholeCentre(i, spawn.y, out _);
            float distance = new Vector2(centre.x - spawn.x, centre.z - spawn.z).magnitude;
            // The first hole is the guaranteed one right in front of the spawn; keep it free.
            float score = Mathf.Abs(distance - preferredDistance) + (i == 0 ? 1000f : 0f);
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
    }

    private Mesh BuildGlyph(float size, float width)
    {
        Mesh mesh = LandmarkShapes.Glyph(LandmarkGlyphs.Get(glyphId), size, width);
        // Centre the glyph on its pivot.
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++) vertices[i] -= new Vector3(0f, 0.7f * size, 0f);
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
        return mesh;
    }

    private void Draft(string name, Vector3 centre, Vector3 axis, float length, float radius)
    {
        var go = new GameObject(name) { layer = 2 };
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(centre, Quaternion.FromToRotation(Vector3.up, axis));
        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.isTrigger = true;
        capsule.direction = 1;
        capsule.radius = Mathf.Max(0.5f, radius);
        capsule.height = Mathf.Max(length, capsule.radius * 2f);
        go.AddComponent<ShaftDraft>();
    }

    private LandmarkPickup Pickup(string name, Vector3 position, LandmarkPickup.Kind kind)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.position = position;
        var pickup = go.AddComponent<LandmarkPickup>();
        pickup.kind = kind;
        return pickup;
    }

    private static GameObject Part(string name, Transform parent, Mesh mesh, Material material, bool collide, bool shadows)
    {
        var go = new GameObject(name) { layer = collide ? 7 : 0 };
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        if (collide) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    /// <summary>Copies of the template tentacle creature: some crawling on the tower walls, some round its base.</summary>
    private void SpawnCreatures(Collider wall, Vector3 basePoint, float towerHeight, float doorAngle, System.Random random)
    {
        if (tentacleTemplate == null || wall == null) return;
        int total = wallCrawlers + groundCrawlers;
        for (int i = 0; i < total; i++)
        {
            // Spread round the tower, away from the door so the entrance stays clear.
            float angle = doorAngle * Mathf.Rad2Deg + 60f + i * (240f / Mathf.Max(1, total)) + (float)random.NextDouble() * 20f;
            TentacleCreature copy = CopyCreature(tentacleTemplate, $"Spire Tentacle {i}");
            if (i < wallCrawlers)
            {
                var crawler = copy.gameObject.AddComponent<SpireCrawler>();
                crawler.wall = wall;
                crawler.axisBase = basePoint;
                crawler.maxHeight = towerHeight - 6f;
                // Unity angles: 0 = +z. The tower's door angle is measured from +x.
                crawler.Place(90f - angle, Mathf.Lerp(6f, towerHeight * 0.7f, (float)random.NextDouble()));
            }
            else
            {
                Vector3 outward = Quaternion.Euler(0f, 90f - angle, 0f) * Vector3.forward;
                Vector3 start = basePoint + outward * 16f + Vector3.up * 20f;
                if (Physics.Raycast(start, Vector3.down, out RaycastHit ground, 60f, 1 << 7, QueryTriggerInteraction.Ignore))
                    start = ground.point + Vector3.up;
                copy.transform.position = start;
            }
            copy.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// A fresh creature with the template's settings. Built inactive so it makes its own body
    /// (instead of copying the template's already built one) once activated.
    /// </summary>
    private TentacleCreature CopyCreature(TentacleCreature template, string name)
    {
        var go = new GameObject(name) { layer = template.gameObject.layer };
        go.SetActive(false);
        go.transform.SetParent(root, false);
        go.transform.localScale = template.transform.localScale;
        var templateCollider = template.GetComponent<SphereCollider>();
        if (templateCollider != null)
        {
            var collider = go.AddComponent<SphereCollider>();
            collider.radius = templateCollider.radius;
            collider.center = templateCollider.center;
            collider.isTrigger = templateCollider.isTrigger;
        }
        var copy = go.AddComponent<TentacleCreature>();
        // Same settings, but each copy grows its own body shape.
        string settings = System.Text.RegularExpressions.Regex.Replace(JsonUtility.ToJson(template), @"""seed"":-?\d+", $"\"seed\":{name.GetHashCode() & 0xffff}");
        JsonUtility.FromJsonOverwrite(settings, copy);
        return copy;
    }

    public void SetTentacleTemplate(TentacleCreature template) => tentacleTemplate = template;

    /// <summary>
    /// Copies seedSource under the pickup, keeps its rotation, scales it to crownSeedSize tall and centres it.
    /// Its colliders and rigidbodies are removed.
    /// </summary>
    private Transform CopySeed(Transform parent)
    {
        var holder = new GameObject("Seed Body").transform;
        holder.SetParent(parent, false);
        GameObject copy = Instantiate(seedSource, holder);
        copy.name = seedSource.name;
        copy.SetActive(true);
        copy.transform.SetPositionAndRotation(holder.position, seedSource.transform.rotation);
        copy.transform.localScale = seedSource.transform.lossyScale;
        foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (Rigidbody r in copy.GetComponentsInChildren<Rigidbody>(true)) Destroy(r);

        Bounds bounds = new Bounds(holder.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (any) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; any = true; }
        }

        float scale = crownSeedSize / Mathf.Max(1e-4f, bounds.size.y);
        Vector3 offset = bounds.center - holder.position;
        holder.localScale = Vector3.one * scale;
        holder.position -= offset * scale;
        return holder;
    }

    /// <summary>Scene object copied as the crown seed (null = use the single mesh below, or the built-in pod).</summary>
    public void SetSeedSource(GameObject source) => seedSource = source;

    /// <summary>Model used for the crown seed (null mesh = the built-in pod).</summary>
    public void SetSeedModel(Mesh mesh, Material material, Vector3 uprightEuler)
    {
        seedModel = mesh;
        seedModelMaterial = material;
        seedModelRotation = uprightEuler;
    }

    public void SetMaterials(Material spire, Material innerSpire, Material glyph, Material seedPod)
    {
        spireMaterial = spire;
        innerSpireMaterial = innerSpire;
        glyphMaterial = glyph;
        seedMaterial = seedPod;
    }
}
