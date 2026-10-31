using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Ore Colossus: a huge, slow, peaceful creature of rock and ore that roams the map on four long legs,
/// carrying a geode shrine on its back (after Elden Ring's Walking Mausoleum). Its feet shove aside
/// whatever they land beside. Under its belly grows a giant ore mass (OreHeart); only erosive water wears
/// it away, carving it wherever it touches. When the heart breaks the colossus kneels for good: its tail lowers into a ramp,
/// its back and legs become ground and climbable rock (static "Remains" colliders), and the geode opens
/// to release one Geode Larva. It ignores blasts, stomps and water ageing (Invulnerable) and is never an
/// AI target (polarity None). It builds its body itself when placed, like the Bone Kiln.
/// </summary>
[DefaultExecutionOrder(-40)] // moves before the player and camera react to it
[RequireComponent(typeof(Rigidbody))]
public sealed class OreColossus : Creature
{
    public enum Phase { Dormant, Walking, Resting, Staggering, Settling, Settled }

    [Header("Placement")]
    [Tooltip("Wait for this scene's ProceduralWorld and place itself on it. Off, or with no world: roam around where it stands.")]
    [SerializeField] private bool placeInWorld = true;
    [SerializeField] private ProceduralWorld world;
    [Tooltip("Optional authored home. Keeps the existing world-aware feet and erosion behavior.")]
    [SerializeField] private Transform roamingAnchor;
    [Min(5f)] [SerializeField] private float authoredRoamRadius = 24f;
    [Tooltip("Preferred distance of the starting spot from the world's spawn (its origin).")]
    [Min(10f)] [SerializeField] private float preferredDistance = 70f;
    [Tooltip("Without a world: wander within this distance of the authored position.")]
    [Min(5f)] [SerializeField] private float roamRadius = 30f;
    [Tooltip("Ground for its feet when there is no world.")]
    [SerializeField] private LayerMask groundLayers = 1 << 7;
    [SerializeField] private int seed = 11;

    [Header("Materials")]
    [SerializeField] private Material rockMaterial;
    [SerializeField] private Material oreMaterial;
    [SerializeField] private Material heartMaterial;
    [SerializeField] private Material geodeMaterial;
    [Tooltip("Released from the geode when it kneels. Empty: a default larva is made.")]
    [SerializeField] private GeodeLarva larvaPrefab;

    [Header("Body")]
    [SerializeField] private Vector3 bodySize = new Vector3(6.5f, 3.6f, 9f);
    [Tooltip("Height of the body centre above the mean height of its feet while walking.")]
    [Min(4f)] [SerializeField] private float walkHeight = 9.8f;
    [Tooltip("How far the body sinks while resting; the ore under the belly then nearly touches the ground.")]
    [Min(0f)] [SerializeField] private float restDrop = 2.6f;

    [Header("Legs")]
    [Tooltip("Hip positions on the body (x to each side, z fore and aft).")]
    [SerializeField] private Vector2 hipOffset = new Vector2(2.6f, 3.2f);
    [Tooltip("Resting foot positions around the body centre on the ground (x, z).")]
    [SerializeField] private Vector2 footOffset = new Vector2(5.6f, 4.6f);
    [Min(1f)] [SerializeField] private float upperLength = 6.6f;
    [Min(1f)] [SerializeField] private float lowerLength = 6.2f;
    [Min(0.2f)] [SerializeField] private float legRadius = 0.75f;
    [Min(0.3f)] [SerializeField] private float footRadius = 0.9f;

    [Header("Gait")]
    [Min(0.1f)] [SerializeField] private float walkSpeed = 1.5f;
    [Tooltip("Degrees per second.")]
    [Min(1f)] [SerializeField] private float turnSpeed = 12f;
    [Min(0.2f)] [SerializeField] private float stepDuration = 0.9f;
    [Min(0.1f)] [SerializeField] private float stepHeight = 1.6f;
    [Tooltip("A foot steps when it is this far from where it would rest.")]
    [Min(0.3f)] [SerializeField] private float stepThreshold = 1.6f;
    [Tooltip("Seconds of rest at a waypoint (min, max).")]
    [SerializeField] private Vector2 restTime = new Vector2(6f, 10f);
    [Range(0f, 1f)] [SerializeField] private float restChance = 0.5f;
    [Tooltip("Steepest ground a foot may land on, in degrees.")]
    [Range(20f, 75f)] [SerializeField] private float maxFootSlope = 60f;

    [Header("Stomp")]
    [Min(0.5f)] [SerializeField] private float stompRadius = 5.5f;
    [Tooltip("Speed the player is thrown at by a foot landing beside them (m/s).")]
    [Min(0f)] [SerializeField] private float stompFling = 10f;
    [Tooltip("Speed other loose bodies are pushed at (m/s).")]
    [Min(0f)] [SerializeField] private float stompPush = 6f;
    [Tooltip("Damage to small creatures (2.2 m or less) beside a landing foot, falling off with distance. Water is never hurt.")]
    [Min(0f)] [SerializeField] private float stompDamage = 20f;

    [Header("Heart")]
    [Tooltip("Size of the ore mass under the belly (width, height down from the belly, length).")]
    [SerializeField] private Vector3 heartSize = new Vector3(4.4f, 5.8f, 5.4f);
    [Tooltip("Spacing of the ore's density grid; smaller is finer carving and costlier remeshing.")]
    [Range(0.12f, 0.5f)] [SerializeField] private float heartCellSize = 0.25f;
    [Tooltip("Erosion the heart takes before it breaks.")]
    [Min(0.1f)] [SerializeField] private float heartIntegrity = 10f;
    [Tooltip("Multiplier on the erosion water does to the heart (capped at 1 per second).")]
    [Min(0f)] [SerializeField] private float heartSensitivity = 4f;
    [Tooltip("Seconds it staggers each time the heart cracks past a quarter.")]
    [Min(0f)] [SerializeField] private float staggerTime = 3.5f;

    [Header("Kneeling")]
    [Tooltip("Gap between the belly and the ground once it kneels.")]
    [Min(0.2f)] [SerializeField] private float settledClearance = 0.8f;
    [Range(2, 6)] [SerializeField] private int tailPlates = 4;
    [Min(1f)] [SerializeField] private float tailPlateLength = 3f;
    [Min(1f)] [SerializeField] private float tailWidth = 2.6f;
    [Tooltip("Steepest the tail ramp may lie (degrees).")]
    [Range(10f, 40f)] [SerializeField] private float maxRampSlope = 28f;
    [Tooltip("Seconds to lower the body and tail, then to open the geode.")]
    [SerializeField] private Vector2 kneelTimes = new Vector2(4f, 1f);

    [Header("Runtime (Read Only)")]
    [SerializeField] private Phase phase;
    [SerializeField] private bool built;
    [SerializeField] private bool larvaReleased;
    [SerializeField] private Vector3 waypoint;

    private sealed class Leg
    {
        public Vector3 hipLocal, restLocal;
        public Transform upper, lower, foot;
        public Vector3 foothold, from, to, drawn, knee, reached;
        public float swing, plantedAt, side;
        public bool swinging, preflung;
        public int pair;
    }

    private static readonly List<OreColossus> all = new List<OreColossus>();
    private static readonly RaycastHit[] rayHits = new RaycastHit[16];
    private static readonly Collider[] overlaps = new Collider[96];
    private readonly List<Leg> legs = new List<Leg>();
    private readonly List<Transform> plates = new List<Transform>();
    private readonly List<Transform> geodeHinges = new List<Transform>();
    private readonly List<MeshFilter> geodeHalves = new List<MeshFilter>();
    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<Material> runtimeMaterials = new List<Material>();
    private readonly HashSet<Collider> legColliders = new HashSet<Collider>();
    private readonly HashSet<Rigidbody> shoved = new HashSet<Rigidbody>();
    private readonly HashSet<Creature> hurt = new HashSet<Creature>();
    private Rigidbody body;
    private Transform bodyTransform, heartAnchor, tailRoot, cradle;
    private OreHeart heart;
    private Mesh shardMesh;
    private Material rock, ore, heartGlow, geode;
    private GameObject remains;
    private GeodeLarva larva;
    private SmoothFirstPersonController player;
    private System.Random random;
    private Vector3 home, velocity, pendingSpot, pendingHeading;
    private float clock, yaw, speed, rootGroundY, bodyY, bodyYVelocity, bob, pitch, roll;
    private float phaseTime, phaseLength, nextPathCheck, nextRepick, nextPlayerSearch, nextBellyCheck, placeTimer, placeWait;
    private bool spotChosen, heartBroken, settleReady, subscribed;
    private int pendingStaggers;
    private float settleStart;
    private Vector3 settleFrom, settleTo;
    private Quaternion settleRotationFrom, settleRotationTo;
    private Vector3[] plateFrom, plateTo;
    private Quaternion[] plateRotationFrom, plateRotationTo;

    /// <summary>Every enabled colossus (the Geode Larva recharges near one).</summary>
    public static IReadOnlyList<OreColossus> All => all;
    public Phase CurrentPhase => phase;
    public bool Built => built;
    public bool Settled => phase == Phase.Settled;
    public bool Wandering => phase == Phase.Walking || phase == Phase.Resting || phase == Phase.Staggering;
    public OreHeart Heart => heart;
    public Transform Body => bodyTransform;
    public GeodeLarva ReleasedLarva => larva;
    public GameObject Remains => remains;
    public float Speed => speed;
    public Vector3 Waypoint => waypoint;
    /// <summary>Raised once when it has knelt for good and the geode is open.</summary>
    public event System.Action<OreColossus> SettledDown;
    /// <summary>Raised where each foot lands.</summary>
    public event System.Action<Vector3> Stomped;

    public override bool CanBeCarried => false;
    public override bool CanBeConsumed => false;
    public override bool Invulnerable => true;
    protected override bool KeepCorpseStationary => true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => all.Clear();

    protected override void OnEnable() { base.OnEnable(); if (!all.Contains(this)) all.Add(this); }
    protected override void OnDisable() { all.Remove(this); base.OnDisable(); }

    protected override void InitializeCreature()
    {
        SetCreatureName("Ore Colossus");
        SetFoodAndDiet(FoodTag.None, FoodTag.None);
        SetResolvedPolarity(CreaturePolarity.None);
        body = GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;
        // Leg shapes are re-posed every frame; fixed mass properties avoid recomputing them.
        body.automaticCenterOfMass = false;
        body.centerOfMass = Vector3.up * walkHeight;
        body.automaticInertiaTensor = false;
        body.inertiaTensor = Vector3.one;
        home = transform.position;
        random = new System.Random(seed);
        if (!placeInWorld) world = null;
        else if (world == null) world = FindWorld();
        // Until it is placed it has no body; park it far below so nothing notices an empty creature.
        if (world != null && !built) transform.position = home + Vector3.down * 2000f;
    }

    private ProceduralWorld FindWorld()
    {
        foreach (ProceduralWorld candidate in FindObjectsByType<ProceduralWorld>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) return candidate;
        return null;
    }

    /// <summary>Runs one creature update (validation drives it with a fixed step).</summary>
    internal void Advance(float deltaTime) => TickCreature(deltaTime);

    protected override void TickCreature(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        clock += deltaTime;
        if (phase == Phase.Dormant) { TickDormant(deltaTime); return; }
        if (phase == Phase.Settled) return;
        phaseTime += deltaTime;
        if (heartBroken && phase != Phase.Settling) BeginSettling();
        else if (pendingStaggers > 0 && (phase == Phase.Walking || phase == Phase.Resting)) BeginStagger();

        switch (phase)
        {
            case Phase.Walking: TickWalking(deltaTime); break;
            case Phase.Resting: if (phaseTime >= phaseLength) { Begin(Phase.Walking, 0f); PickWaypoint(false); } break;
            case Phase.Staggering:
                if (phaseTime >= phaseLength)
                {
                    if (pendingStaggers > 0) BeginStagger();
                    else Begin(Phase.Walking, 0f);
                }
                break;
        }
        if (phase != Phase.Walking) speed = Mathf.MoveTowards(speed, 0f, deltaTime * 2f);
        Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        velocity = forward * speed;
        MoveRoot(deltaTime, forward);
        UpdateGait(deltaTime);
        if (phase == Phase.Settling) TickSettling(deltaTime);
        if (phase == Phase.Settled) return;
        PoseBody(deltaTime, false);
        PoseLegs();
        PoseTail();
        UpdateHeart(deltaTime);
    }

    private void Begin(Phase next, float length) { phase = next; phaseTime = 0f; phaseLength = length; }

    // ---------------- Placement ----------------

    private void TickDormant(float deltaTime)
    {
        placeTimer -= deltaTime;
        if (placeTimer > 0f) return;
        placeTimer = 0.5f;
        if (world == null) { BuildAt(home, transform.forward); return; }
        if (!world.LayoutReady) return;
        if (!spotChosen)
        {
            if (roamingAnchor != null)
            {
                pendingSpot = roamingAnchor.position;
                pendingHeading = roamingAnchor.forward;
                spotChosen = true;
            }
            else
            {
            Vector3 centre = world.transform.position;
            if (!gameObject.scene.GetPhysicsScene().Raycast(centre + Vector3.up * 200f, Vector3.down, out _, 600f, 1 << world.TerrainLayer, QueryTriggerInteraction.Ignore)) return;
            if (!PickSpot(out pendingSpot, out pendingHeading))
            {
                Debug.LogWarning("Ore Colossus: no flat open ground found near the preferred distance; starting near the spawn instead.", this);
                pendingSpot = centre + new Vector3(preferredDistance * 0.5f, 0f, 0f);
                pendingHeading = Vector3.forward;
            }
            spotChosen = true;
            }
            placeWait = 0f;
        }
        placeWait += 0.5f;
        // Feet need real terrain colliders; after a while fall back to the generator's column heights.
        if (world.IsRegionReady(pendingSpot, 25f) || placeWait > 20f) BuildAt(pendingSpot, pendingHeading);
    }

    private bool PickSpot(out Vector3 spot, out Vector3 heading)
    {
        Vector3 spawn = world.transform.position;
        float[] rings = { 0f, 12f, -12f, 24f, -24f, 40f };
        float offset = (float)random.NextDouble() * 24f;
        foreach (float ring in rings)
        {
            float distance = Mathf.Max(20f, preferredDistance + ring);
            for (int i = 0; i < 24; i++)
            {
                float angle = (i + offset) * Mathf.PI * 2f / 24f;
                Vector3 p = spawn + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                if (!InsideMap(p, 0.6f) || NearSinkhole(p, 18f) || !Flat(p, 8f, 3f) || NearOtherColossus(p, 60f)) continue;
                world.ColumnHeights(p, out float surface, out _, out _);
                spot = new Vector3(p.x, surface, p.z);
                heading = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                return true;
            }
        }
        spot = spawn; heading = Vector3.forward;
        return false;
    }

    private bool Flat(Vector3 p, float reach, float tolerance)
    {
        float low = float.MaxValue, high = float.MinValue;
        Vector3[] offsets = { Vector3.zero, Vector3.right * reach, Vector3.left * reach, Vector3.forward * reach, Vector3.back * reach };
        foreach (Vector3 offset in offsets)
        {
            world.ColumnHeights(p + offset, out float surface, out _, out _);
            low = Mathf.Min(low, surface); high = Mathf.Max(high, surface);
        }
        return high - low <= tolerance;
    }

    private bool InsideMap(Vector3 point, float share)
    {
        Vector3 local = point - world.transform.position;
        Vector2 half = world.WorldSize * 0.5f;
        return Mathf.Abs(local.x) < half.x * share && Mathf.Abs(local.z) < half.y * share;
    }

    private bool NearSinkhole(Vector3 point, float margin)
    {
        for (int i = 0; i < world.SinkholeCount; i++)
        {
            Vector3 centre = world.SinkholeCentre(i, point.y, out float radius);
            if (new Vector2(centre.x - point.x, centre.z - point.z).magnitude < radius + margin) return true;
        }
        return false;
    }

    private bool NearOtherColossus(Vector3 point, float distance)
    {
        foreach (OreColossus other in all)
            if (other != this && other != null && other.built && (other.transform.position - point).sqrMagnitude < distance * distance) return true;
        return false;
    }

    // ---------------- Building ----------------

    private void BuildAt(Vector3 ground, Vector3 heading)
    {
        if (built) return;
        built = true;
        heading.y = 0f;
        if (heading.sqrMagnitude < 1e-4f) heading = Vector3.forward;
        yaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
        if (GroundAt(ground, out Vector3 point, out _)) ground = point;
        transform.SetPositionAndRotation(ground, Quaternion.Euler(0f, yaw, 0f));
        rootGroundY = ground.y;
        home = ground;
        EnsureMaterials();
        BuildBody();
        BuildLegs();
        BuildTail();
        BuildHeart();
        foreach (Leg leg in legs)
        {
            Vector3 rest = transform.TransformPoint(leg.restLocal);
            leg.foothold = leg.drawn = GroundAt(rest, out Vector3 p, out _) ? p : rest;
            leg.plantedAt = clock;
        }
        bodyY = MeanFootY() + walkHeight;
        PoseBody(0f, true);
        PoseLegs();
        PoseTail();
        if (world != null && !subscribed) { world.TerrainChanged += OnWorldEroded; subscribed = true; }
        PickWaypoint(false);
        Begin(Phase.Walking, 0f);
    }

    /// <summary>A URP Lit material made at runtime (used when no authored material is set).</summary>
    public static Material MakeMaterial(string name, Color color, Color emission)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var material = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (emission.maxColorComponent > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return material;
    }

    private void EnsureMaterials()
    {
        rock = rockMaterial != null ? rockMaterial : Keep(MakeMaterial("Ore Colossus Rock (runtime)", new Color(0.32f, 0.29f, 0.27f), Color.black));
        ore = oreMaterial != null ? oreMaterial : Keep(MakeMaterial("Ore Colossus Ore (runtime)", new Color(0.3f, 0.75f, 0.7f), new Color(0.2f, 0.9f, 0.75f)));
        heartGlow = heartMaterial != null ? heartMaterial : Keep(MakeMaterial("Ore Colossus Heart (runtime)", new Color(0.6f, 0.95f, 1f), new Color(0.6f, 2.2f, 2.4f)));
        geode = geodeMaterial != null ? geodeMaterial : Keep(MakeMaterial("Ore Colossus Geode (runtime)", new Color(0.62f, 0.45f, 0.85f), new Color(0.5f, 0.3f, 1.1f)));
    }

    private Material Keep(Material material) { runtimeMaterials.Add(material); return material; }

    private Mesh Track(Mesh mesh) { if (mesh != null && !meshes.Contains(mesh)) meshes.Add(mesh); return mesh; }

    private static Transform Node(string name, Transform parent, Vector3 localPosition)
    {
        var node = new GameObject(name).transform;
        node.SetParent(parent, false);
        node.localPosition = localPosition;
        return node;
    }

    private GameObject Part(string name, Transform parent, Mesh mesh, Material first, Material second = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = Track(mesh);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = mesh.subMeshCount > 1 ? new[] { first, second != null ? second : first } : new[] { first };
        renderer.shadowCastingMode = ShadowCastingMode.On;
        return go;
    }

    private void BuildBody()
    {
        float halfY = bodySize.y * 0.5f;
        bodyTransform = Node("Body", transform, Vector3.up * walkHeight);
        bodyTransform.gameObject.AddComponent<BoxCollider>().size = bodySize;
        Part("Hull", bodyTransform, OreShapes.Boulder(new Vector3(bodySize.x * 1.08f, bodySize.y, bodySize.z * 1.06f), seed), rock)
            .transform.localPosition = Vector3.down * 0.3f;
        float deckLength = bodySize.z * 0.96f;
        Part("Deck", bodyTransform, OreShapes.Slab(deckLength, bodySize.x * 0.94f, 0.7f, seed + 1), rock)
            .transform.localPosition = new Vector3(0f, halfY, -deckLength * 0.5f);
        // Ore veins breaking out of the flanks and the rear of the back.
        Vector3[] spots = { new Vector3(-0.47f, 0.05f, -0.25f), new Vector3(0.47f, 0.05f, -0.25f), new Vector3(-0.45f, -0.2f, 0.2f),
            new Vector3(0.45f, -0.2f, 0.2f), new Vector3(-0.36f, 0.5f, -0.4f), new Vector3(0.36f, 0.5f, -0.4f) };
        for (int i = 0; i < spots.Length; i++)
        {
            Vector3 local = Vector3.Scale(spots[i], bodySize);
            Vector3 outward = new Vector3(Mathf.Sign(local.x), i >= 4 ? 1.4f : 0.25f, 0f).normalized;
            GameObject shards = Part("Ore Vein " + i, bodyTransform, OreShapes.ShardCluster(5, 1.7f, seed + 3 + i), ore);
            shards.transform.localPosition = local;
            shards.transform.localRotation = Quaternion.FromToRotation(Vector3.up, outward);
        }

        // The geode shrine on the back: two halves that swing open on hinges at their outer lower edges.
        const float geodeRadius = 2f;
        var geodeCentre = new Vector3(0f, halfY + 0.9f, 0.6f);
        cradle = Node("Cradle", bodyTransform, new Vector3(0f, halfY + 0.02f, 0.6f));
        for (int k = 0; k < 6; k++)
        {
            float a = k * Mathf.PI / 3f;
            GameObject crystals = Part("Cradle Crystal " + k, cradle, OreShapes.ShardCluster(2, 0.7f, seed + 40 + k), geode);
            crystals.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.95f;
        }
        for (int s = -1; s <= 1; s += 2)
        {
            var hingePoint = new Vector3(s * geodeRadius * 0.8f, halfY, geodeCentre.z);
            Transform hinge = Node(s < 0 ? "Geode Hinge L" : "Geode Hinge R", bodyTransform, hingePoint);
            GameObject half = Part(s < 0 ? "Geode L" : "Geode R", hinge, OreShapes.GeodeHalf(geodeRadius, 0.35f, s, seed + 50 + s), rock, geode);
            half.transform.localPosition = geodeCentre - hingePoint;
            geodeHinges.Add(hinge);
            geodeHalves.Add(half.GetComponent<MeshFilter>());
        }
        // Slightly inside the hull, so the ore's top seam stays hidden.
        heartAnchor = Node("Heart Anchor", bodyTransform, new Vector3(0f, -halfY + 0.35f, 0.3f));
        tailRoot = Node("Tail Root", bodyTransform, new Vector3(0f, halfY, -bodySize.z * 0.5f + 0.2f));
    }

    private void BuildLegs()
    {
        string[] names = { "FL", "FR", "BL", "BR" };
        float[] sx = { -1f, 1f, -1f, 1f };
        float[] sz = { 1f, 1f, -1f, -1f };
        int[] pairs = { 0, 1, 1, 0 }; // diagonal pairs
        for (int i = 0; i < 4; i++)
        {
            var leg = new Leg
            {
                hipLocal = new Vector3(sx[i] * hipOffset.x, -bodySize.y * 0.28f, sz[i] * hipOffset.y),
                restLocal = new Vector3(sx[i] * footOffset.x, 0f, sz[i] * footOffset.y),
                side = sx[i], pair = pairs[i],
            };
            Transform root = Node("Leg " + names[i], transform, Vector3.zero);
            GameObject upper = Part("Upper", root, OreShapes.Limb(upperLength, legRadius * 1.1f, legRadius * 0.85f, 7, seed + 10 + i), rock);
            legColliders.Add(Capsule(upper, upperLength, legRadius * 0.9f));
            GameObject knee = Part("Knee Ore", upper.transform, OreShapes.ShardCluster(4, 1.2f, seed + 14 + i), ore);
            knee.transform.localPosition = new Vector3(0f, legRadius * 0.4f, upperLength);
            GameObject lower = Part("Lower", root, OreShapes.Limb(lowerLength, legRadius * 0.85f, legRadius * 0.6f, 7, seed + 20 + i), rock);
            legColliders.Add(Capsule(lower, lowerLength, legRadius * 0.7f));
            GameObject foot = Part("Foot", root, OreShapes.Boulder(new Vector3(footRadius * 2.3f, footRadius * 1.2f, footRadius * 2.3f), seed + 30 + i), rock);
            var pad = foot.AddComponent<SphereCollider>();
            pad.radius = footRadius * 0.85f;
            pad.center = Vector3.up * footRadius * 0.15f;
            legColliders.Add(pad);
            leg.upper = upper.transform; leg.lower = lower.transform; leg.foot = foot.transform;
            legs.Add(leg);
        }
    }

    private static Collider Capsule(GameObject part, float length, float radius)
    {
        var capsule = part.AddComponent<CapsuleCollider>();
        capsule.direction = 2;
        capsule.radius = radius;
        capsule.height = Mathf.Max(length, radius * 2f);
        capsule.center = new Vector3(0f, 0f, length * 0.5f);
        return capsule;
    }

    private void BuildTail()
    {
        for (int i = 0; i < tailPlates; i++)
        {
            float width = tailWidth * (1f - 0.08f * i);
            GameObject plate = Part("Tail Plate " + i, transform, OreShapes.Slab(tailPlateLength, width, 0.5f, seed + 60 + i), rock);
            var box = plate.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.25f, tailPlateLength * 0.5f);
            box.size = new Vector3(width * 0.95f, 0.5f, tailPlateLength);
            plates.Add(plate.transform);
        }
    }

    private void BuildHeart()
    {
        var go = new GameObject("Heart Ore");
        go.SetActive(false);
        go.transform.SetParent(heartAnchor, false);
        heart = go.AddComponent<OreHeart>();
        heart.Initialize(this, heartSize, heartCellSize, heartIntegrity, heartSensitivity, seed + 71, heartGlow);
        shardMesh = Track(OreShapes.ShardCluster(1, 0.9f, seed + 72, "Heart Shard"));
        go.SetActive(true);
    }

    // ---------------- Walking ----------------

    private void TickWalking(float deltaTime)
    {
        Vector3 flat = waypoint - transform.position; flat.y = 0f;
        float targetYaw = flat.sqrMagnitude > 0.01f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : yaw;
        float difference = Mathf.Abs(Mathf.DeltaAngle(yaw, targetYaw));
        // Standing nearly still it turns faster, stepping round on the spot.
        yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, turnSpeed * (speed < 0.3f ? 2f : 1f) * deltaTime);
        // Slow down to turn, and when the feet are lagging far behind.
        float desired = walkSpeed * Mathf.Clamp01(1f - (difference - 25f) / 50f);
        float lag = MaxLag(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * speed * stepDuration);
        // Feet that cannot keep up (no foothold ahead) hold the body back instead of being dragged.
        if (lag > stepThreshold * 3f) desired = 0f;
        else if (lag > stepThreshold * 2.2f) desired *= 0.4f;
        speed = Mathf.MoveTowards(speed, desired, deltaTime * 0.8f);

        if (flat.magnitude < 6f)
        {
            if (random.NextDouble() < restChance) Begin(Phase.Resting, Mathf.Lerp(restTime.x, restTime.y, (float)random.NextDouble()));
            else PickWaypoint(false);
            return;
        }
        if (clock < nextPathCheck) return;
        nextPathCheck = clock + 0.5f;
        Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 ahead = transform.position + forward * 9f;
        bool clear = GroundAt(ahead, out Vector3 point, out Vector3 normal) && Mathf.Abs(point.y - transform.position.y) < 4.5f &&
            normal.y >= Mathf.Cos(maxFootSlope * Mathf.Deg2Rad);
        if (world != null) clear &= InsideMap(ahead, 0.68f) && !NearSinkhole(ahead, 8f);
        if (!clear) Repick();
    }

    private void PickWaypoint(bool avoidAhead)
    {
        Vector3 position = transform.position;
        Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        for (int i = 0; i < 24; i++)
        {
            bool last = i == 23;
            Vector3 p;
            if (roamingAnchor != null)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                float distance = Mathf.Sqrt((float)random.NextDouble()) * authoredRoamRadius;
                Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                p = roamingAnchor.position + new Vector3(offset.x, 0f, offset.y);
                if (!last && Flat2(p - position) < 8f) continue;
            }
            else if (world != null)
            {
                Vector2 half = world.WorldSize * 0.5f * 0.62f;
                p = world.transform.position + new Vector3(Mathf.Lerp(-half.x, half.x, (float)random.NextDouble()), 0f, Mathf.Lerp(-half.y, half.y, (float)random.NextDouble()));
                if (!last && (NearSinkhole(p, 14f) || Flat2(p - position) < 40f)) continue;
            }
            else
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f, distance = Mathf.Sqrt((float)random.NextDouble()) * roamRadius;
                p = home + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                if (!last && Flat2(p - position) < roamRadius * 0.4f) continue;
            }
            Vector3 to = p - position; to.y = 0f;
            if (!last && avoidAhead && to.sqrMagnitude > 0.01f && Vector3.Dot(to.normalized, forward) > 0.3f) continue;
            waypoint = new Vector3(p.x, position.y, p.z);
            return;
        }
    }

    private static float Flat2(Vector3 v) => new Vector2(v.x, v.z).magnitude;

    private void MoveRoot(float deltaTime, Vector3 forward)
    {
        Vector3 position = transform.position + forward * speed * deltaTime;
        if (GroundAt(position, out Vector3 ground, out _)) rootGroundY = Mathf.Lerp(rootGroundY, ground.y, 1f - Mathf.Exp(-4f * deltaTime));
        position.y = rootGroundY;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
    }

    // ---------------- Gait ----------------

    private void UpdateGait(float deltaTime)
    {
        int swingingPair = -1;
        foreach (Leg leg in legs)
        {
            if (!leg.swinging) continue;
            leg.swing += deltaTime / stepDuration;
            if (leg.swing >= 0.85f && !leg.preflung)
            {
                leg.preflung = true;
                // Clear the player out from under the landing foot before it arrives.
                Shove(leg.to, footRadius + 1.2f, stompFling * 0.6f, 0f, 0f);
            }
            if (leg.swing >= 1f) { Plant(leg); continue; }
            float t = Mathf.SmoothStep(0f, 1f, leg.swing);
            leg.drawn = Vector3.Lerp(leg.from, leg.to, t) + Vector3.up * stepHeight * Mathf.Sin(Mathf.PI * leg.swing);
            swingingPair = leg.pair;
        }
        if (swingingPair >= 0 || !Wandering) return;
        Vector3 lead = velocity * stepDuration;
        float threshold = speed > 0.05f ? stepThreshold : 0.5f;
        int best = -1; float worst = threshold;
        for (int pair = 0; pair < 2; pair++)
        {
            float lag = 0f; bool settled = true;
            foreach (Leg leg in legs)
            {
                if (leg.pair != pair) continue;
                if (clock - leg.plantedAt < 0.25f) settled = false;
                lag = Mathf.Max(lag, Flat2(leg.foothold - Home(leg, lead)));
            }
            if (settled && lag > worst) { worst = lag; best = pair; }
        }
        if (best >= 0 && !TryBeginPairStep(best, lead) && phase == Phase.Walking) Repick();
    }

    /// <summary>Gives up on the current waypoint for one not straight ahead, at most every few seconds.</summary>
    private void Repick()
    {
        if (clock < nextRepick) return;
        nextRepick = clock + 3f;
        PickWaypoint(true);
    }

    private Vector3 Home(Leg leg, Vector3 lead) => transform.TransformPoint(leg.restLocal) + lead;

    private float MaxLag(Vector3 lead)
    {
        float lag = 0f;
        foreach (Leg leg in legs) if (!leg.swinging) lag = Mathf.Max(lag, Flat2(leg.foothold - Home(leg, lead)));
        return lag;
    }

    private readonly Vector3[] stepTargets = new Vector3[4];

    private bool TryBeginPairStep(int pair, Vector3 lead)
    {
        for (int i = 0; i < legs.Count; i++)
            if (legs[i].pair == pair && !FindFoothold(legs[i], Home(legs[i], lead), out stepTargets[i])) return false;
        for (int i = 0; i < legs.Count; i++)
        {
            Leg leg = legs[i];
            if (leg.pair != pair) continue;
            leg.from = leg.foothold; leg.to = stepTargets[i];
            leg.swing = 0f; leg.swinging = true; leg.preflung = false;
        }
        return true;
    }

    private bool FindFoothold(Leg leg, Vector3 home, out Vector3 target)
    {
        Vector3 hip = bodyTransform.TransformPoint(leg.hipLocal) + velocity * stepDuration;
        float reach = (upperLength + lowerLength) * 0.97f;
        float minNormal = Mathf.Cos(maxFootSlope * Mathf.Deg2Rad);
        // Rough ground: try the full step, then shorter ones, each also a little to either side.
        float[] shares = { 1f, 0.75f, 0.5f, 0.3f };
        float[] sideways = { 0f, 1.2f, -1.2f };
        foreach (float share in shares)
            foreach (float offset in sideways)
            {
                Vector3 candidate = Vector3.Lerp(leg.foothold, home, share) + transform.right * offset;
                if (!GroundAt(candidate, out Vector3 point, out Vector3 normal) || normal.y < minNormal) continue;
                if ((point - hip).magnitude > reach) continue;
                target = point;
                return true;
            }
        target = leg.foothold;
        return false;
    }

    private void Plant(Leg leg)
    {
        leg.swinging = false;
        leg.swing = 1f;
        leg.foothold = leg.drawn = leg.to;
        leg.plantedAt = clock;
        bob -= 0.12f;
        Shove(leg.to, stompRadius, stompFling, stompPush, stompDamage);
        Stomped?.Invoke(leg.to);
    }

    private float MeanFootY()
    {
        float sum = 0f;
        foreach (Leg leg in legs) sum += leg.swinging ? leg.to.y : leg.foothold.y;
        return legs.Count > 0 ? sum / legs.Count : transform.position.y;
    }

    private void OnWorldEroded(Bounds bounds)
    {
        if (!Wandering) return;
        bounds.Expand(3f);
        foreach (Leg leg in legs)
            if (!leg.swinging && bounds.Contains(leg.foothold) && GroundAt(leg.foothold, out Vector3 point, out _))
                leg.foothold = leg.drawn = point;
    }

    // ---------------- Pose ----------------

    private void PoseBody(float deltaTime, bool snap)
    {
        if (phase == Phase.Settling && settleReady) return;
        float drop = 0f;
        if (phase == Phase.Resting)
            drop = restDrop * Mathf.SmoothStep(0f, 1f, Mathf.Min(phaseTime, phaseLength - phaseTime) / 1.5f);
        else if (phase == Phase.Staggering && phaseLength > 0f)
        {
            float t = phaseTime / phaseLength;
            drop = 1.4f * (t < 0.12f ? t / 0.12f : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.12f) / 0.88f));
        }
        float target = Mathf.Max(MeanFootY() + walkHeight - drop, rootGroundY + bodySize.y * 0.5f + 1f);
        bodyY = snap ? target : Mathf.SmoothDamp(bodyY, target, ref bodyYVelocity, phase == Phase.Staggering ? 0.12f : 0.35f);
        bob = snap ? 0f : Mathf.Lerp(bob, 0f, 1f - Mathf.Exp(-5f * deltaTime));

        float front = 0f, back = 0f, left = 0f, right = 0f;
        foreach (Leg leg in legs)
        {
            float y = leg.swinging ? leg.to.y : leg.foothold.y;
            if (leg.restLocal.z > 0f) front += y; else back += y;
            if (leg.side < 0f) left += y; else right += y;
        }
        float targetPitch = Mathf.Clamp(Mathf.Atan2((back - front) * 0.5f, footOffset.y * 2f) * Mathf.Rad2Deg, -8f, 8f);
        float targetRoll = Mathf.Clamp(Mathf.Atan2((right - left) * 0.5f, footOffset.x * 2f) * Mathf.Rad2Deg, -8f, 8f);
        if (phase == Phase.Staggering && phaseLength > 0f) targetRoll += Mathf.Sin(clock * 14f) * 2.5f * (1f - phaseTime / phaseLength);
        float blend = snap ? 1f : 1f - Mathf.Exp(-3f * deltaTime);
        pitch = Mathf.Lerp(pitch, targetPitch, blend);
        roll = Mathf.Lerp(roll, targetRoll, blend);
        bodyTransform.SetPositionAndRotation(new Vector3(transform.position.x, bodyY + bob, transform.position.z),
            transform.rotation * Quaternion.Euler(pitch, 0f, roll));
    }

    private void PoseLegs()
    {
        foreach (Leg leg in legs)
        {
            Vector3 hip = bodyTransform.TransformPoint(leg.hipLocal);
            Vector3 outward = hip - bodyTransform.position; outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : transform.right * leg.side;
            SolveTwoBone(hip, leg.drawn, upperLength, lowerLength, (outward + Vector3.up * 0.35f).normalized,
                out leg.knee, out leg.reached, out Vector3 bend);
            leg.upper.SetPositionAndRotation(hip, Quaternion.LookRotation(leg.knee - hip, bend));
            leg.lower.SetPositionAndRotation(leg.knee, Quaternion.LookRotation(leg.reached - leg.knee, bend));
            leg.foot.SetPositionAndRotation(leg.reached + Vector3.up * footRadius * 0.45f, transform.rotation);
        }
    }

    /// <summary>Two-bone reach from hip to foot; the knee bends towards pole. Segment lengths are exact.</summary>
    internal static void SolveTwoBone(Vector3 hip, Vector3 foot, float upper, float lower, Vector3 pole,
        out Vector3 knee, out Vector3 reached, out Vector3 bend)
    {
        Vector3 toFoot = foot - hip;
        float distance = toFoot.magnitude;
        Vector3 dir = distance > 1e-4f ? toFoot / distance : Vector3.down;
        distance = Mathf.Clamp(distance, Mathf.Abs(upper - lower) + 1e-3f, upper + lower - 1e-3f);
        float cosA = Mathf.Clamp((upper * upper + distance * distance - lower * lower) / (2f * upper * distance), -1f, 1f);
        bend = Vector3.ProjectOnPlane(pole, dir);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.up, dir);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.right, dir);
        bend.Normalize();
        knee = hip + dir * upper * cosA + bend * upper * Mathf.Sqrt(1f - cosA * cosA);
        reached = hip + dir * distance;
    }

    private void PoseTail()
    {
        if (phase == Phase.Settling && settleReady) return;
        Vector3 p = tailRoot.position;
        Quaternion rotation = bodyTransform.rotation;
        for (int i = 0; i < plates.Count; i++)
        {
            float pitchDown = 30f - 18f * i;
            float sway = Mathf.Sin(clock * 0.7f + i * 0.8f) * 5f * (i + 1) / plates.Count;
            Quaternion plateRotation = rotation * Quaternion.Euler(0f, 180f + sway, 0f) * Quaternion.Euler(pitchDown, 0f, 0f);
            plates[i].SetPositionAndRotation(p, plateRotation);
            p += plateRotation * Vector3.forward * (tailPlateLength - 0.35f);
        }
    }

    private void UpdateHeart(float deltaTime)
    {
        if (heart != null && !heart.Broken) heart.Tick(deltaTime);
    }

    // ---------------- Heart, stagger and kneeling ----------------

    /// <summary>The heart cracked past a threshold (called from inside the fluid system's step; acted on in Tick).</summary>
    internal void OnHeartThreshold() => pendingStaggers++;

    /// <summary>The heart is worn through (acted on in Tick).</summary>
    internal void OnHeartBroken() => heartBroken = true;

    [ContextMenu("Shatter Heart (debug)")]
    public void DebugShatter() { if (heart != null && !heart.Broken) heart.ForceBreak(); }

    private void BeginStagger()
    {
        // Thresholds reached during a stagger wait their turn; consume only the one starting now.
        pendingStaggers--;
        Begin(Phase.Staggering, staggerTime);
        speed = 0f;
        Vector3 at = heart != null ? heart.transform.position : transform.position;
        Shove(new Vector3(at.x, rootGroundY, at.z), 5f, stompFling * 0.6f, stompPush * 0.5f, 0f);
    }

    private void BeginSettling()
    {
        pendingStaggers = 0;
        Begin(Phase.Settling, 0f);
        settleReady = false;
        speed = 0f;
        Vector3 at = heart != null ? heart.transform.position : transform.position;
        if (heart != null) heart.Shatter(shardMesh, heartGlow);
        Shove(new Vector3(at.x, rootGroundY, at.z), 6f, stompFling * 0.8f, stompPush, 0f);
    }

    private void TickSettling(float deltaTime)
    {
        bool anySwinging = false;
        foreach (Leg leg in legs) anySwinging |= leg.swinging;
        if (!settleReady)
        {
            if (phaseTime < 0.6f || (anySwinging && phaseTime < 1.5f)) return;
            foreach (Leg leg in legs) if (leg.swinging) { leg.drawn = leg.to; Plant(leg); }
            ComputeSettleTargets();
            settleReady = true;
            settleStart = phaseTime;
        }
        float t = phaseTime - settleStart;
        float lower = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.1f, kneelTimes.x));
        bodyTransform.SetPositionAndRotation(Vector3.Lerp(settleFrom, settleTo, lower), Quaternion.Slerp(settleRotationFrom, settleRotationTo, lower));
        for (int i = 0; i < plates.Count; i++)
            plates[i].SetPositionAndRotation(Vector3.Lerp(plateFrom[i], plateTo[i], lower), Quaternion.Slerp(plateRotationFrom[i], plateRotationTo[i], lower));
        float open = Mathf.SmoothStep(0f, 1f, (t - kneelTimes.x) / Mathf.Max(0.1f, kneelTimes.y));
        for (int i = 0; i < geodeHinges.Count; i++)
            geodeHinges[i].localRotation = Quaternion.Euler(0f, 0f, (i == 0 ? 1f : -1f) * 110f * open);
        if (lower < 1f && clock >= nextBellyCheck) { nextBellyCheck = clock + 0.2f; ClearUnderBelly(); }
        if (t >= kneelTimes.x + kneelTimes.y + 0.2f) FinishSettling();
    }

    private void ComputeSettleTargets()
    {
        settleFrom = bodyTransform.position;
        settleRotationFrom = bodyTransform.rotation;
        float halfX = bodySize.x * 0.5f, halfY = bodySize.y * 0.5f, halfZ = bodySize.z * 0.5f;
        var heights = new float[4];
        var corners = new Vector3[4];
        float front = 0f, back = 0f, left = 0f, right = 0f, sum = 0f;
        for (int i = 0; i < 4; i++)
        {
            float sx = i % 2 == 0 ? -1f : 1f, sz = i < 2 ? 1f : -1f;
            corners[i] = new Vector3(sx * halfX, -halfY, sz * halfZ);
            Vector3 under = transform.TransformPoint(new Vector3(sx * halfX, 0f, sz * halfZ));
            heights[i] = GroundAt(under, out Vector3 point, out _) ? point.y : rootGroundY;
            sum += heights[i];
            if (sz > 0f) front += heights[i]; else back += heights[i];
            if (sx < 0f) left += heights[i]; else right += heights[i];
        }
        float settledPitch = Mathf.Clamp(Mathf.Atan2((back - front) * 0.5f, bodySize.z) * Mathf.Rad2Deg, -8f, 8f);
        float settledRoll = Mathf.Clamp(Mathf.Atan2((right - left) * 0.5f, bodySize.x) * Mathf.Rad2Deg, -8f, 8f);
        settleRotationTo = transform.rotation * Quaternion.Euler(settledPitch, 0f, settledRoll);
        float centreY = sum * 0.25f + settledClearance + halfY;
        // No belly corner may end up in the ground.
        for (int i = 0; i < 4; i++)
        {
            float cornerY = centreY + (settleRotationTo * corners[i]).y;
            if (cornerY < heights[i] + 0.3f) centreY += heights[i] + 0.3f - cornerY;
        }
        settleTo = new Vector3(transform.position.x, centreY, transform.position.z);

        // The tail lies down as a straight ramp from the rear edge of the back to the ground.
        int count = plates.Count;
        plateFrom = new Vector3[count]; plateTo = new Vector3[count];
        plateRotationFrom = new Quaternion[count]; plateRotationTo = new Quaternion[count];
        for (int i = 0; i < count; i++) { plateFrom[i] = plates[i].position; plateRotationFrom[i] = plates[i].rotation; }
        Vector3 start = settleTo + settleRotationTo * tailRoot.localPosition;
        Vector3 backward = -(settleRotationTo * Vector3.forward); backward.y = 0f;
        backward = backward.sqrMagnitude > 1e-4f ? backward.normalized : -transform.forward;
        float total = count * tailPlateLength;
        float bestDistance = 3f, bestSlope = float.MaxValue, bestGround = start.y - 2f;
        for (float d = 3f; d <= total; d += 0.25f)
        {
            Vector3 probe = start + backward * d;
            float ground = (GroundAt(probe, out Vector3 point, out _) ? point.y : rootGroundY) - 0.3f;
            float drop = start.y - ground;
            // Ground height can rise again beyond a trench, making a later landing reachable.
            if (Mathf.Sqrt(d * d + drop * drop) > total) continue;
            float slope = Mathf.Abs(Mathf.Atan2(drop, d) * Mathf.Rad2Deg);
            if (slope < bestSlope) { bestSlope = slope; bestDistance = d; bestGround = ground; }
            if (slope <= maxRampSlope) { bestDistance = d; bestGround = ground; break; }
        }
        Vector3 end = start + backward * bestDistance; end.y = bestGround;
        Vector3 ramp = end - start;
        float length = ramp.magnitude;
        Vector3 dir = length > 1e-3f ? ramp / length : backward;
        Quaternion lie = Quaternion.LookRotation(dir, Vector3.up);
        float spacing = count > 1 ? Mathf.Max(0f, length - tailPlateLength) / (count - 1) : 0f;
        for (int i = 0; i < count; i++)
        {
            // Each later plate sits a hair lower so overlapping tops do not flicker.
            plateTo[i] = start + dir * spacing * i + lie * Vector3.down * (0.02f * i);
            plateRotationTo[i] = lie;
        }
    }

    private void ClearUnderBelly()
    {
        FindPlayer();
        if (player == null) return;
        Vector3 local = bodyTransform.InverseTransformPoint(player.transform.position);
        if (Mathf.Abs(local.x) > bodySize.x * 0.5f + 0.6f || Mathf.Abs(local.z) > bodySize.z * 0.5f + 0.6f || local.y > -bodySize.y * 0.5f + 0.5f) return;
        Vector3 side = bodyTransform.right * (local.x >= 0f ? 1f : -1f);
        player.Fling(side * 8f + Vector3.up * 3f);
    }

    private void FindPlayer()
    {
        if (player != null || clock < nextPlayerSearch) return;
        nextPlayerSearch = clock + 2f;
        foreach (SmoothFirstPersonController candidate in FindObjectsByType<SmoothFirstPersonController>(FindObjectsSortMode.None))
            if (candidate.gameObject.scene == gameObject.scene) { player = candidate; return; }
    }

    private void FinishSettling()
    {
        bodyTransform.SetPositionAndRotation(settleTo, settleRotationTo);
        for (int i = 0; i < plates.Count; i++) plates[i].SetPositionAndRotation(plateTo[i], plateRotationTo[i]);
        for (int i = 0; i < geodeHinges.Count; i++) geodeHinges[i].localRotation = Quaternion.Euler(0f, 0f, (i == 0 ? 1f : -1f) * 110f);
        PoseLegs();
        ConvertCollidersToRemains();
        ReleaseLarva();
        phase = Phase.Settled;
        speed = 0f;
        velocity = Vector3.zero;
        if (!Application.isBatchMode) LandmarkToast.Show("The colossus kneels. Its geode opens.");
        SettledDown?.Invoke(this);
    }

    /// <summary>
    /// Kneeling, it becomes ground: its colliders move to a static object that is not a Creature, so the
    /// player can walk, climb and look around on it like terrain (systems treat creature colliders specially).
    /// </summary>
    private void ConvertCollidersToRemains()
    {
        remains = new GameObject("Ore Colossus Remains");
        SceneManager.MoveGameObjectToScene(remains, gameObject.scene);
        foreach (Collider source in GetComponentsInChildren<Collider>(true))
        {
            if (source == null || source.isTrigger || (heart != null && source == heart.Collider)) continue;
            CopyCollider(source, remains.transform, legColliders.Contains(source) ? 8 : 7);
            source.enabled = false;
            Destroy(source);
        }
        foreach (MeshFilter half in geodeHalves)
        {
            var go = new GameObject(half.name) { layer = 7 };
            go.transform.SetParent(remains.transform, false);
            go.transform.SetPositionAndRotation(half.transform.position, half.transform.rotation);
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = half.sharedMesh;
            collider.convex = true;
        }
        var bed = new GameObject("Cradle") { layer = 7 };
        bed.transform.SetParent(remains.transform, false);
        bed.transform.SetPositionAndRotation(cradle.position, cradle.rotation);
        var box = bed.AddComponent<BoxCollider>();
        box.center = Vector3.up * 0.1f;
        box.size = new Vector3(1.4f, 0.2f, 1.4f);
    }

    private static void CopyCollider(Collider source, Transform parent, int layer)
    {
        var go = new GameObject(source.name) { layer = layer };
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        go.transform.localScale = source.transform.lossyScale;
        Collider copy = null;
        switch (source)
        {
            case BoxCollider box: { var c = go.AddComponent<BoxCollider>(); c.center = box.center; c.size = box.size; copy = c; break; }
            case SphereCollider sphere: { var c = go.AddComponent<SphereCollider>(); c.center = sphere.center; c.radius = sphere.radius; copy = c; break; }
            case CapsuleCollider capsule:
            {
                var c = go.AddComponent<CapsuleCollider>();
                c.center = capsule.center; c.radius = capsule.radius; c.height = capsule.height; c.direction = capsule.direction; copy = c; break;
            }
            case MeshCollider mesh: { var c = go.AddComponent<MeshCollider>(); c.sharedMesh = mesh.sharedMesh; c.convex = mesh.convex; copy = c; break; }
        }
        if (copy != null) copy.sharedMaterial = source.sharedMaterial;
    }

    /// <summary>Where the larva rests in the opened geode.</summary>
    public bool CradlePoint(out Vector3 point)
    {
        point = cradle != null ? cradle.position + cradle.up * 0.45f : transform.position;
        return cradle != null;
    }

    private void ReleaseLarva()
    {
        if (larvaReleased || !CradlePoint(out Vector3 at)) return;
        larvaReleased = true;
        Quaternion rotation = Quaternion.Euler(0f, yaw + 90f, 0f);
        // Spawned inactive in this scene, then unparented, so it never belongs to the colossus.
        var staging = new GameObject("Geode Larva Staging");
        staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, gameObject.scene);
        bool made = larvaPrefab == null;
        if (!made) larva = Instantiate(larvaPrefab, at, rotation, staging.transform);
        else
        {
            var go = new GameObject("Geode Larva");
            go.transform.SetParent(staging.transform, false);
            go.transform.SetPositionAndRotation(at, rotation);
            Rigidbody larvaBody = go.AddComponent<Rigidbody>();
            larvaBody.mass = 0.6f;
            larvaBody.interpolation = RigidbodyInterpolation.Interpolate;
            larvaBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            go.AddComponent<SphereCollider>().radius = 0.3f;
            larva = go.AddComponent<GeodeLarva>();
        }
        larva.name = "Geode Larva";
        larva.transform.SetParent(null, true);
        Destroy(staging);
        // A default larva cannot have its lifespan authored; give it the same long life as the prefab.
        if (made) larva.SetRemainingAge(100000f);
        larva.Nest(this, at, rotation);
    }

    // ---------------- Shoving ----------------

    /// <summary>
    /// Pushes everything beside a point outward: the player is flung, loose bodies pushed, and small
    /// creatures (not water, not carried) damaged. Only things within 2.5 m of the point's height count.
    /// </summary>
    private void Shove(Vector3 point, float radius, float fling, float push, float damage)
    {
        int count = gameObject.scene.GetPhysicsScene().OverlapSphere(point + Vector3.up, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
        shoved.Clear(); hurt.Clear();
        for (int i = 0; i < count; i++)
        {
            Collider collider = overlaps[i];
            if (collider == null || collider.transform.IsChildOf(transform)) continue;
            Rigidbody rb = collider.attachedRigidbody;
            Creature creature = CreatureSpatialQuery.Owner(collider);
            if (creature == this || (rb == null && creature == null)) continue;
            // ClosestPoint only supports primitives and convex meshes.
            Vector3 closest = collider is BoxCollider || collider is SphereCollider || collider is CapsuleCollider ||
                (collider is MeshCollider mesh && mesh.convex) ? collider.ClosestPoint(point) : collider.bounds.ClosestPoint(point);
            if (Mathf.Abs(closest.y - point.y) > 2.5f) continue;
            Vector3 flat = closest - point; flat.y = 0f;
            float distance = flat.magnitude;
            if (distance > radius) continue;
            float falloff = 1f - distance / radius;
            Vector3 outward = distance > 0.05f ? flat / distance : Vector3.zero;
            if (outward == Vector3.zero && rb != null) { outward = rb.position - point; outward.y = 0f; outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : transform.forward; }
            if (rb != null && shoved.Add(rb))
            {
                var controller = rb.GetComponent<SmoothFirstPersonController>();
                if (controller != null)
                {
                    if (fling > 0f) controller.Fling(rb.linearVelocity * 0.3f + (outward + Vector3.up * 0.45f) * fling * Mathf.Max(0.35f, falloff));
                }
                else if (!rb.isKinematic && push > 0f)
                {
                    rb.AddForce((outward + Vector3.up * 0.5f) * push * falloff, ForceMode.VelocityChange);
                    if (rb.TryGetComponent<AsuraMotor>(out var motor)) motor.AcceptPush();
                }
            }
            if (damage > 0f && creature != null && hurt.Add(creature) && !(creature is ErosiveFluidDroplet) && !creature.IsCarried && Size(creature) <= 2.2f)
                creature.ApplyDamage(damage * falloff, this);
        }
    }

    private static float Size(Creature creature)
    {
        Bounds bounds = default; bool any = false;
        foreach (Collider collider in creature.GetComponentsInChildren<Collider>())
        {
            if (collider.isTrigger) continue;
            if (!any) { bounds = collider.bounds; any = true; } else bounds.Encapsulate(collider.bounds);
        }
        return any ? Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) : 0f;
    }

    // ---------------- Ground ----------------

    private float GroundY(Vector3 point) => GroundAt(point, out Vector3 ground, out _) ? ground.y : rootGroundY;

    /// <summary>Top ground under a point: the world's own terrain, or Ground Layers without a world (never itself).</summary>
    private bool GroundAt(Vector3 p, out Vector3 point, out Vector3 normal)
    {
        float top = p.y + 40f; int mask = groundLayers;
        float surface = 0f;
        if (world != null)
        {
            world.ColumnHeights(p, out surface, out _, out _);
            top = surface + 40f; mask = 1 << world.TerrainLayer;
        }
        int count = gameObject.scene.GetPhysicsScene().Raycast(new Vector3(p.x, top, p.z), Vector3.down, rayHits, 120f, mask, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        point = p; normal = Vector3.up;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = rayHits[i];
            if (hit.collider == null || hit.distance >= best) continue;
            if (world != null ? !world.OwnsCollider(hit.collider) :
                hit.collider.transform.IsChildOf(transform) || (remains != null && hit.collider.transform.IsChildOf(remains.transform))) continue;
            best = hit.distance; point = hit.point; normal = hit.normal;
        }
        if (best < float.PositiveInfinity) return true;
        if (world == null) return false;
        point = new Vector3(p.x, surface, p.z);
        return true;
    }

    // ---------------- Inspection (read only; used by validation) ----------------

    public int LegCount => legs.Count;
    public float UpperLength => upperLength;
    public float LowerLength => lowerLength;
    public float MaxRampSlope => maxRampSlope;
    public int SwingingPairCount
    {
        get
        {
            bool a = false, b = false;
            foreach (Leg leg in legs) if (leg.swinging) { if (leg.pair == 0) a = true; else b = true; }
            return (a ? 1 : 0) + (b ? 1 : 0);
        }
    }

    public void GetLeg(int index, out Vector3 hip, out Vector3 knee, out Vector3 foot, out bool swinging)
    {
        Leg leg = legs[index];
        hip = bodyTransform.TransformPoint(leg.hipLocal); knee = leg.knee; foot = leg.reached; swinging = leg.swinging;
    }

    public Transform TailPlate(int index) => plates[index];
    public int TailPlateCount => plates.Count;
    public Transform CradleTransform => cradle;

    /// <summary>A stomp at a point, as if a foot landed there.</summary>
    internal void ForceStomp(Vector3 point) => Shove(point, stompRadius, stompFling, stompPush, stompDamage);

    private void OnDestroy()
    {
        if (subscribed && world != null) world.TerrainChanged -= OnWorldEroded;
        foreach (Mesh mesh in meshes) if (mesh != null) Destroy(mesh);
        foreach (Material material in runtimeMaterials) if (material != null) Destroy(material);
        if (remains != null) Destroy(remains);
    }
}
