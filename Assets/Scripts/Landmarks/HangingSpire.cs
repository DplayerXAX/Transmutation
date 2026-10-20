using System.Collections.Generic;
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
    [SerializeField] private Material glazeMaterial;

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

    [Header("Glaze")]
    [Tooltip("Smooth glaze that bare hands slide down. One full ring round the outside, between these shares " +
             "of the tower's height; below it scattered patches that get denser going up. Inside only patches. " +
             "Ways past the ring: a tentacle creature on the back, the shaft draft once fire warms the hearth, " +
             "the bounce mushroom on the balcony below it, or climbing inside. x >= y turns all glaze off.")]
    [SerializeField] private Vector2 glazeRing = new Vector2(0.72f, 0.775f);
    [Tooltip("Share of the wall covered by patches just under the ring.")]
    [Range(0f, 1f)] [SerializeField] private float glazePatches = 0.6f;
    [Tooltip("Bounce mushroom placed on the balcony under the band (the landmark menu uses the prefab).")]
    [SerializeField] private GameObject bounceShroomPrefab;
    [Tooltip("Heat (summed over fire creatures on the hearth inside the door) for the shaft draft to lift at full strength.")]
    [Min(1f)] [SerializeField] private float heatForLift = 25f;
    [Tooltip("Upward speed of the heated draft, m/s.")]
    [Min(0f)] [SerializeField] private float liftSpeed = 9f;

    [Header("Climb In Three Stages")]
    [Tooltip("Two shorter towers beside the spire, linked by ribbon bridges: climb the first, cross to the second, " +
             "climb it, cross to a landing on the spire and climb the last stretch. The spire can still be climbed whole.")]
    [SerializeField] private bool stages = true;
    [Tooltip("Tops of the first and second towers, as shares of the spire's height.")]
    [SerializeField] private Vector2 stageTops = new Vector2(0.32f, 0.62f);
    [Tooltip("Each stage is at least this much climbing (metres), wherever the ground around the spire is high.")]
    [Min(4f)] [SerializeField] private float minStageClimb = 18f;
    [Tooltip("Climb on the second tower, from its landing to its top (metres).")]
    [Min(4f)] [SerializeField] private float secondStageClimb = 24f;
    [Tooltip("Climb on the spire from its landing to the crown (metres); the spire grows taller if it needs to.")]
    [Min(8f)] [SerializeField] private float finalStageClimb = 22f;
    [Tooltip("How far the floating towers hang on below their landings (metres).")]
    [Min(0f)] [SerializeField] private float hangBelow = 9f;
    [Tooltip("How much higher each bridge's far end is than its near end (metres).")]
    [Min(0f)] [SerializeField] private float bridgeRise = 4f;
    [Min(1f)] [SerializeField] private float bridgeWidth = 3f;
    [Tooltip("Off: no ribbon bridges between the towers; the player has to find a way across (a tentacle arm shot " +
             "at the next tower and reeled in, a cord, a slingshot). The landings stay where the bridges arrived.")]
    [SerializeField] private bool bridges = false;
    [Tooltip("On: bare hands can climb the towers (they only creep down). Off: bare hands slide straight off; only " +
             "sticky hands (a carried tentacle creature) hold, and ledge tops still stop a slide.")]
    [SerializeField] private bool climbAlone = false;
    [Tooltip("Scene object whose mesh and material the bridges use (empty = the object named Plane).")]
    [SerializeField] private GameObject bridgeTemplate;

    [Header("Structure")]
    [Tooltip("The spire is a stack of drums about this tall (metres): each its own polygon (square, hexagon...), " +
             "turned and shifted against the one below, with a glowing seam between them. 0 = one smooth wall.")]
    [Min(0f)] [SerializeField] private float drumLength = 12f;
    [Tooltip("Drum height on the two stage towers (0 = smooth).")]
    [Min(0f)] [SerializeField] private float stageDrumLength = 8f;

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
        UpdateHeat();
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
            roughness = drumLength > 0f ? 0.08f : 0.3f,
            drumLength = drumLength,
            glazePatchDensity = glazePatches,
            // Balcony round to the side of the door, so players walking round the tower pass under it.
            balconyAngle = doorAngle + Mathf.PI * 0.6f, // the glaze tentacle in SpawnCreatures uses the same angle
        };
        upper.glazeBottom = upper.height * glazeRing.x;
        upper.glazeTop = upper.height * glazeRing.y;
        upper.SnapGlaze();
        if (stages)
        {
            PlanStages(basePoint, upper, doorAngle);
            float landing = stageTopsPlanned.y + bridgeRise;
            // Tall enough for the last stretch, with the glaze ring above the landing.
            upper.height = Mathf.Max(upper.height, landing + finalStageClimb);
            float ringHeight = upper.height * (glazeRing.y - glazeRing.x);
            upper.glazeBottom = Mathf.Max(upper.height * glazeRing.x, landing + 3f);
            upper.glazeTop = Mathf.Min(upper.glazeBottom + ringHeight, upper.height - 6f);
            upper.SnapGlaze();
            // The spire floats, hanging on a little way below the landing its bridge arrives at.
            upper.cutBelow = landing - 2.5f - hangBelow;
        }
        upperShape = upper;
        ringBottom = upper.glazeBottom;
        ringTop = upper.glazeTop;
        Mesh upperMesh = LandmarkShapes.Spire(upper, random, ledgeCount, beam: true, flipY: false, out Vector3 beamCentre);
        // A resting place on the crown rim, facing out over the world (RestSpotSystem).
        {
            float a = doorAngle + Mathf.PI;
            Vector3 axisTop = upper.Axis(upper.height);
            float rim = (upper.Inner(a, upper.height) + upper.Outer(a, upper.height)) * 0.5f;
            Vector3 outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            crownSeat = basePoint + new Vector3(axisTop.x, upper.height, axisTop.z) + outward * rim;
            crownFacing = outward;
        }
        GameObject spireObject = Part("Spire", root, upperMesh, spireMaterial, collide: true, shadows: true);
        spireRenderer = spireObject.GetComponent<Renderer>();
        SpireStructure.Build(spireObject.transform, upper, spireMaterial);
        // The whole tower is smooth: bare hands creep down it unless they keep climbing or rest on a ledge.
        Slippery(spireObject);
        if (upper.HasGlaze)
        {
            if (bounceShroomPrefab != null)
            {
                Vector3 spot = basePoint + LandmarkShapes.BalconyTop(upper) + Vector3.up * 0.6f;
                GameObject shroom = Instantiate(bounceShroomPrefab, spot, Quaternion.identity, root);
                shroom.name = "Balcony Bounce Shroom";
                shroom.AddComponent<ReturnHome>();
            }
        }
        // Hearth: a slab inside the door, over the shaft. Fire set down on it warms the tower and the draft lifts.
        {
            Vector3 inward = -new Vector3(Mathf.Cos(doorAngle), 0f, Mathf.Sin(doorAngle));
            Vector3 axis0 = upper.Axis(0f);
            float wallIn = upper.Inner(doorAngle, 0f);
            Vector3 from = axis0 - inward * (wallIn + 0.4f) + Vector3.down * 0.6f;
            Vector3 to = axis0 - inward * (wallIn - 3.6f) + Vector3.down * 0.6f;
            Part("Hearth", root, LandmarkShapes.Slab(from, to, 4f, 0.6f, 0.3f), spireMaterial, collide: true, shadows: true);
            heatProbe = basePoint + (from + to) * 0.5f + Vector3.up * 0.8f;
            heatRadius = 3f;
        }

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
        sign.SetActive(signY > upper.cutBelow + 1f);
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

        if (stages) BuildStages(basePoint, upper, doorAngle);
        LandmarkDecor.Spire(root, basePoint, upper, stageTowers, spireMaterial, glyphMaterial, seed);
        // Crystal blades, a drifting shard field, ink guide lines, ground etchings and rising motes.
        SpireEnvironment.Build(root, basePoint, upper, stageTowers, spireMaterial, glyphMaterial, seed);
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
        ShaftDraft draft = MakeDraft(name, centre, axis, length, radius);
        if (name == "Tower Draft") towerDraft = draft;
    }

    private ShaftDraft MakeDraft(string name, Vector3 centre, Vector3 axis, float length, float radius)
    {
        var go = new GameObject(name) { layer = 2 };
        go.transform.SetParent(root, false);
        go.transform.SetPositionAndRotation(centre, Quaternion.FromToRotation(Vector3.up, axis));
        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.isTrigger = true;
        capsule.direction = 1;
        capsule.radius = Mathf.Max(0.5f, radius);
        capsule.height = Mathf.Max(length, capsule.radius * 2f);
        return go.AddComponent<ShaftDraft>();
    }

    // ---- Heat in the base: fire creatures brought inside the tower make the shaft draft lift ----
    private ShaftDraft towerDraft;
    private float ringBottom, ringTop;
    private SpireShape upperShape;

    /// <summary>
    /// For tests: the lowest window below the glaze ring. sill = middle of its bottom edge on the inner
    /// (or outer) face; radial points out of the tower.
    /// </summary>
    public bool FindWindow(bool inside, out Vector3 sill, out Vector3 radial)
    {
        sill = radial = default;
        if (upperShape == null || root == null) return false;
        for (int j = 0; j < upperShape.Rings; j += 2)
        for (int k = 0; k < upperShape.segments; k++)
        {
            float y = upperShape.RingY(j);
            if (y < 14f || y < upperShape.cutBelow + 3f || !upperShape.WindowCell(j, k) || upperShape.WindowCell(j - 1, k)) continue;
            if (upperShape.HasGlaze && y > upperShape.glazeBottom - 3f) continue;
            float angle = (k + 0.5f) * Mathf.PI * 2f / upperShape.segments;
            radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float r = inside ? upperShape.Inner(angle, y) : upperShape.Outer(angle, y);
            Vector3 axis = upperShape.Axis(y);
            sill = root.position + new Vector3(axis.x, y, axis.z) + radial * r;
            return true;
        }
        return false;
    }

    /// <summary>Heights (above the base) of the full glaze ring, for tests.</summary>
    public Vector2 GlazeRing => new Vector2(ringBottom, ringTop);
    private Renderer spireRenderer;
    private MaterialPropertyBlock glowBlock;
    private Vector3 heatProbe;
    private float heatRadius;
    private float nextHeatCheck;
    private float heatTarget;
    private readonly Collider[] heatHits = new Collider[32];
    private readonly System.Collections.Generic.HashSet<Creature> heatCounted = new System.Collections.Generic.HashSet<Creature>();

    /// <summary>How warm the tower's base is (0..1). Public for tests and other scripts.</summary>
    public float Warmth { get; private set; }

    /// <summary>Where heat is measured: just above the middle of the hearth.</summary>
    public Vector3 HeatProbe => heatProbe;

    private void UpdateHeat()
    {
        if (towerDraft == null) return;
        if (Time.time >= nextHeatCheck)
        {
            nextHeatCheck = Time.time + 0.5f;
            float heat = 0f;
            heatCounted.Clear();
            int count = Physics.OverlapSphereNonAlloc(heatProbe, heatRadius, heatHits, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Creature creature = heatHits[i].GetComponentInParent<Creature>();
                if (creature != null && heatCounted.Add(creature)) heat += Mathf.Max(0f, creature.Heat);
            }
            heatTarget = Mathf.Clamp01(heat / heatForLift);
        }
        // Warms up over a few seconds, cools more slowly.
        Warmth = Mathf.MoveTowards(Warmth, heatTarget, Time.deltaTime * (heatTarget > Warmth ? 0.3f : 0.1f));
        towerDraft.lift = Warmth > 0.15f ? liftSpeed * Warmth : 0f;

        // The tower's script glows warm while the draft runs.
        if (spireRenderer != null && spireMaterial != null && spireMaterial.HasProperty("_GlowColor"))
        {
            if (glowBlock == null) glowBlock = new MaterialPropertyBlock();
            spireRenderer.GetPropertyBlock(glowBlock);
            Color cool = spireMaterial.GetColor("_GlowColor");
            float strength = spireMaterial.GetFloat("_GlowStrength");
            glowBlock.SetColor("_GlowColor", Color.Lerp(cool, new Color(1f, 0.45f, 0.15f), Warmth));
            glowBlock.SetFloat("_GlowStrength", strength * (1f + 1.5f * Warmth));
            spireRenderer.SetPropertyBlock(glowBlock);
        }
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
                float low = Mathf.Max(6f, CutHeight + 3f);
                crawler.minHeight = Mathf.Max(crawler.minHeight, CutHeight + 2f);
                crawler.Place(90f - angle, Mathf.Lerp(low, towerHeight * 0.7f, (float)random.NextDouble()));
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

        // One more lives on the glaze ring, by the balcony: the creature that lets the hands stick there.
        if (ringTop > ringBottom)
        {
            TentacleCreature bandCopy = CopyCreature(tentacleTemplate, "Spire Tentacle (glaze)");
            var bandCrawler = bandCopy.gameObject.AddComponent<SpireCrawler>();
            bandCrawler.wall = wall;
            bandCrawler.axisBase = basePoint;
            bandCrawler.minHeight = ringBottom - 1f;
            bandCrawler.maxHeight = ringTop + 0.5f;
            float balcony = (doorAngle + Mathf.PI * 0.6f) * Mathf.Rad2Deg;
            bandCrawler.Place(90f - balcony - 12f, ringBottom + 1.5f);
            bandCopy.gameObject.SetActive(true);
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
        copy.InitializeNewIndividual();
        return copy;
    }

    // ---------------- Three-stage climb ----------------

    private readonly List<Transform> stageTowers = new List<Transform>();
    private readonly List<SpireShape> stageShapes = new List<SpireShape>();
    /// <summary>The two smaller towers of the staged climb (for tests).</summary>
    public IReadOnlyList<Transform> StageTowers => stageTowers;

    /// <summary>
    /// Two shorter towers stand beside the spire. The first rises to stageTops.x of the spire's height, the second
    /// to stageTops.y. A ribbon bridge runs from the first one's rim up to a landing on the second, and another from
    /// the second's rim up to a landing on the spire itself, so the climb comes in three shorter stretches.
    /// </summary>
    // Planned before building: where the two stage towers stand, their ground and their tops (above the spire base).
    private Vector3[] stageCentres;
    private Vector2 stageGrounds, stageTopsPlanned;

    /// <summary>Height above the spire's base below which the spire is cut away (floating); very low when not cut.</summary>
    public float CutHeight => upperShape != null ? upperShape.cutBelow : float.MinValue;

    private void PlanStages(Vector3 basePoint, SpireShape upper, float doorAngle)
    {
        float distance = upper.Outer(doorAngle, 0f) + 15f;
        // Round from the door, so the first tower is met on the way in; the next a little further round.
        float[] angles = { doorAngle + 0.6f, doorAngle + 1.45f };
        stageCentres = new Vector3[2];
        float previousTop = 0f;
        for (int i = 0; i < 2; i++)
        {
            Vector3 dir = new Vector3(Mathf.Cos(angles[i]), 0f, Mathf.Sin(angles[i]));
            stageCentres[i] = basePoint + dir * distance;
            float ground = StageGround(stageCentres[i]) - basePoint.y;
            // Tops: shares of the spire's height, but always a real climb, the second clearly above where
            // the first bridge lands on it, and both below the spire's glaze ring.
            float top = upper.height * (i == 0 ? stageTops.x : stageTops.y);
            top = Mathf.Max(top, i == 0 ? ground + minStageClimb : previousTop + bridgeRise + secondStageClimb);
            if (i == 0) { stageGrounds.x = ground; stageTopsPlanned.x = top; }
            else { stageGrounds.y = ground; stageTopsPlanned.y = top; }
            previousTop = top;
        }
    }

    private void BuildStages(Vector3 basePoint, SpireShape upper, float doorAngle)
    {
        GameObject template = bridgeTemplate != null ? bridgeTemplate : GameObject.Find("Plane");
        Mesh bridgeMesh = template != null && template.GetComponent<MeshFilter>() != null ? template.GetComponent<MeshFilter>().sharedMesh : null;
        Material bridgeMaterial = template != null && template.GetComponent<Renderer>() != null ? template.GetComponent<Renderer>().sharedMaterial : spireMaterial;
        if (bridgeMesh == null)
        {
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            bridgeMesh = plane.GetComponent<MeshFilter>().sharedMesh;
            Destroy(plane);
        }
        var random = new System.Random(seed * 31 + 7);
        signBaseY = basePoint.y;
        signHeight = upper.height;
        Vector3 previousTop = Vector3.zero, previousCentre = Vector3.zero;
        float previousRim = 0f;
        for (int i = 0; i < 2; i++)
        {
            Vector3 centre = stageCentres[i];
            float ground = i == 0 ? stageGrounds.x : stageGrounds.y;
            float top = i == 0 ? stageTopsPlanned.x : stageTopsPlanned.y;
            var shape = new SpireShape
            {
                height = Mathf.Max(8f, top - ground),
                bury = 6f,
                innerRadius = 3f,
                topScale = 0.85f,
                wallBase = 2.4f,
                wallTop = 2f, // a rim wide enough to stand on
                flare = 2.5f,
                lean = 1f,
                crown = 2.5f,
                doorHalfAngle = 0f,
                windows = 0f,
                noiseSeed = 21.7f + i * 9.3f + seed,
                glazePatchDensity = 0.08f,
            };
            // The second tower floats: cut away below the landing the first bridge arrives at.
            if (i == 1) shape.cutBelow = stageTopsPlanned.x + bridgeRise - ground - 2.5f - hangBelow;
            // A slip band part of the way round, half way up the climbable stretch (the gap in it is the way past).
            float from = Mathf.Max(0f, shape.cutBelow + (i == 1 ? hangBelow + 2.5f : 0f));
            shape.roughness = stageDrumLength > 0f ? 0.08f : 0.3f;
            shape.drumLength = stageDrumLength;
            Mesh mesh = LandmarkShapes.Spire(shape, random, 3, beam: false, flipY: false, out _);
            GameObject tower = Part($"Stage Tower {i + 1}", root, mesh, spireMaterial, collide: true, shadows: true);
            tower.transform.position = new Vector3(centre.x, basePoint.y + ground, centre.z);
            Slippery(tower);
            SpireStructure.Build(tower.transform, shape, spireMaterial);
            stageShapes.Add(shape);
            if (i == 0)
            {
                // At the foot of the first tower, facing the way in: the crossbar sits low.
                Vector3 inDir = new Vector3(Mathf.Cos(doorAngle), 0f, Mathf.Sin(doorAngle));
                float facing = Mathf.Atan2(inDir.z, inDir.x);
                Vector3 axis = shape.Axis(2.5f);
                Vector3 wall = tower.transform.position + new Vector3(axis.x, 0f, axis.z) + inDir * shape.Outer(facing, 2.5f);
                HeightSign(wall, inDir, tower.transform.position.y + 2.5f);
            }
            stageTowers.Add(tower.transform);
            Debug.Log($"Hanging Spire stage tower {i + 1}: ground {ground:0.0} m, top {top:0.0} m above the spire base ({shape.height:0.0} m tall).");
            Vector3 towerTop = tower.transform.position + shape.Axis(shape.height);
            float rimRadius = (shape.Inner(0f, shape.height) + shape.Outer(0f, shape.height)) * 0.5f;

            // The bridge from the previous tower's rim up to a landing on this one.
            if (i > 0) Bridge(previousCentre, previousTop, previousRim, tower.transform.position, shape, bridgeMesh, bridgeMaterial);
            previousTop = towerTop;
            previousCentre = new Vector3(towerTop.x, 0f, towerTop.z);
            previousRim = rimRadius;
        }
        // Last bridge: from the second tower to a landing on the spire.
        if (stageTowers.Count == 2)
        {
            Vector3 from = previousTop;
            Vector3 toward = new Vector3(basePoint.x - from.x, 0f, basePoint.z - from.z).normalized;
            float landingY = from.y - basePoint.y + bridgeRise;
            float angle = Mathf.Atan2(-toward.z, -toward.x);
            Vector3 axis = upper.Axis(landingY);
            float outer = upper.Outer(angle, landingY);
            Vector3 wallPoint = basePoint + new Vector3(axis.x, landingY, axis.z) + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * outer;
            Landing(wallPoint, -toward, landingY + basePoint.y);
            Vector3 start = new Vector3(previousCentre.x, from.y, previousCentre.z) + toward * previousRim;
            if (bridges) Ribbon(start, wallPoint - toward * 2.2f + Vector3.up * 0.05f, bridgeMesh, bridgeMaterial);
        }
    }

    /// <summary>Ribbon bridge from a tower's rim to a landing on the next tower's wall.</summary>
    private void Bridge(Vector3 fromCentre, Vector3 fromTop, float fromRim, Vector3 toBase, SpireShape toShape, Mesh mesh, Material material)
    {
        Vector3 toward = new Vector3(toBase.x - fromCentre.x, 0f, toBase.z - fromCentre.z).normalized;
        float landingY = fromTop.y + bridgeRise - toBase.y;
        float angle = Mathf.Atan2(-toward.z, -toward.x);
        Vector3 axis = toShape.Axis(landingY);
        float outer = toShape.Outer(angle, landingY);
        Vector3 wallPoint = toBase + new Vector3(axis.x, landingY, axis.z) + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * outer;
        Landing(wallPoint, -toward, fromTop.y + bridgeRise);
        Vector3 start = new Vector3(fromCentre.x, fromTop.y, fromCentre.z) + toward * fromRim;
        if (bridges) Ribbon(start, wallPoint - toward * 2.2f + Vector3.up * 0.05f, mesh, material);
    }

    private readonly List<(Vector3 top, Vector3 inward)> landings = new List<(Vector3, Vector3)>();
    /// <summary>Where the bridges arrive (top of each landing, next to the wall) and the direction into the wall (for tests).</summary>
    public IReadOnlyList<(Vector3 top, Vector3 inward)> Landings => landings;

    // For the height signs: the spire's base and full height.
    private float signBaseY, signHeight = 1f;

    /// <summary>
    /// The spire's sign carved on a wall, with its crossbar set to how high up this spot is on the spire:
    /// low on the first tower it sits near the foot of the sign, by the crown it has climbed to the point.
    /// </summary>
    private void HeightSign(Vector3 wallPoint, Vector3 outward, float worldY)
    {
        if (glyphMaterial == null) return;
        float t = Mathf.Clamp01((worldY - signBaseY) / Mathf.Max(1f, signHeight));
        const float size = 1.8f;
        Mesh mesh = LandmarkShapes.Glyph(LandmarkGlyphs.SpireAtHeight(t), size, 0.12f);
        GameObject sign = Part("Height Sign", root, mesh, glyphMaterial, collide: false, shadows: false);
        sign.transform.position = new Vector3(wallPoint.x, worldY, wallPoint.z) + outward * 0.2f;
        sign.transform.rotation = Quaternion.LookRotation(-outward, Vector3.up);
    }

    [Tooltip("How fast bare hands creep down the towers (share of the full slide speed); keep climbing or rest on a ledge.")]
    [Range(0f, 1f)] [SerializeField] private float towerSlip = 0.3f;

    private void Slippery(GameObject tower)
    {
        var slip = tower.AddComponent<SlipperySurface>();
        slip.speedScale = climbAlone ? towerSlip : 1f;
        slip.accelerate = !climbAlone;
    }

    /// <summary>A stone slab sticking out of a wall at a height, its top at topY: where a bridge arrives.</summary>
    private void Landing(Vector3 wallPoint, Vector3 outward, float topY)
    {
        // A height sign on the wall just above the landing, where someone arriving over the bridge sees it.
        HeightSign(wallPoint, outward, topY + 1.2f);
        landings.Add((new Vector3(wallPoint.x, topY, wallPoint.z) + outward * 0.45f, -outward));
        Vector3 from = new Vector3(wallPoint.x, topY - 0.7f, wallPoint.z) - outward * 1.2f;
        Vector3 to = new Vector3(wallPoint.x, topY - 0.7f, wallPoint.z) + outward * 3f;
        GameObject slab = Part("Landing", root, LandmarkShapes.Slab(root.InverseTransformPoint(from), root.InverseTransformPoint(to), bridgeWidth + 1f, 0.7f, 0.4f), spireMaterial, collide: true, shadows: true);
        slab.transform.localPosition = Vector3.zero;
    }

    /// <summary>A walkable ribbon (the scene's plane) laid straight from a to b.</summary>
    private void Ribbon(Vector3 a, Vector3 b, Mesh mesh, Material material)
    {
        Debug.Log($"Hanging Spire bridge from {a} to {b} ({Vector3.Distance(a, b):0.0} m, rising {b.y - a.y:0.0} m).");
        Vector3 span = b - a;
        var go = new GameObject("Bridge") { layer = 7 };
        go.transform.SetParent(root, true);
        go.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.LookRotation(span.normalized, Vector3.up));
        // The built-in plane is 10 x 10 m in its x/z plane.
        Bounds bounds = mesh.bounds;
        go.transform.localScale = new Vector3(bridgeWidth / Mathf.Max(0.01f, bounds.size.x), 1f, (span.magnitude + 1f) / Mathf.Max(0.01f, bounds.size.z));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    private float StageGround(Vector3 point)
    {
        world.ColumnHeights(point, out float surface, out _, out _);
        return Physics.Raycast(new Vector3(point.x, surface + 100f, point.z), Vector3.down, out RaycastHit hit, 220f, 1 << 7, QueryTriggerInteraction.Ignore) ? hit.point.y : surface;
    }

    private Vector3 crownSeat, crownFacing;

    /// <summary>A spot on the crown rim to rest at, and the way it faces (out over the world).</summary>
    public bool CrownSeat(out Vector3 position, out Vector3 facing)
    {
        position = crownSeat;
        facing = crownFacing;
        return built && crownFacing != Vector3.zero;
    }

    /// <summary>True once the tower has been built (it waits for the world).</summary>
    public bool Built => built;
    /// <summary>The sinkhole the tower stands on (-1 before it is built).</summary>
    public int SinkholeIndex => sinkholeIndex;

    public void SetTentacleTemplate(TentacleCreature template) => tentacleTemplate = template;

    public void SetGlaze(Material material, GameObject bounceShroom)
    {
        glazeMaterial = material;
        bounceShroomPrefab = bounceShroom;
    }

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
