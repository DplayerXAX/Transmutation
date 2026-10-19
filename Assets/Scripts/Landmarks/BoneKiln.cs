using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Landmark: the Bone Kiln, a colossus that dived head first into a sinkhole and fell asleep there. Its neck
/// runs down the shaft, its skull hangs below the ceiling of the inner world (a seed rests under it), its back
/// arches over the mouth of the hole and its tail sinks into the ground. Flowers, leaves and vines grow on it.
///
/// Asleep, its ribs come down side by side round the mouth and fence it in. A slow ripple runs along them,
/// each rib following the one before a little late, so a gap rolls along the cage: time it to slip in and
/// drop down the shaft. A rib never comes down on a fuzzy creature (or the player) in its gap; a fuzzy
/// creature caught there is pinned and props it open until the player pets it or picks it up.
///
/// Fire set in the kiln heart (a bowl of bone beside the tail) warms the bones; the ribs lift, and once it is
/// fully warm the colossus wakes: it pulls its skull up out of the cave, its spine wrings itself into a helix
/// (every vertebra turned further round, its ribs turning with it) and it roams the whole map, crawling over
/// the ground and taking to the air, its ribs rippling like legs and fins. The mouth of the cave is left open.
/// </summary>
[DefaultExecutionOrder(-40)] // moves before the player's camera follows a rider
public sealed class BoneKiln : MonoBehaviour
{
    [SerializeField] private ProceduralWorld world;

    [Header("Materials")]
    [SerializeField] private Material boneMaterial;
    [SerializeField] private Material inkMaterial;
    [SerializeField] private Material glyphMaterial;
    [SerializeField] private Material seedMaterial;

    [Header("Placement")]
    [Tooltip("Preferred distance of the sinkhole from the spawn.")]
    [Min(20f)] [SerializeField] private float preferredDistance = 80f;
    [Tooltip("Use this sinkhole index instead (-1 = choose automatically).")]
    [SerializeField] private int sinkholeOverride = -1;

    [Header("Shape")]
    [SerializeField] private int seed = 5;
    [Tooltip("Gap between ribs along the spine, metres. Small enough that resting ribs fence the mouth in.")]
    [Range(1.2f, 3f)] [SerializeField] private float ribSpacing = 1.6f;
    [Tooltip("Extra back-and-forth wringing of the spine on top of the helix (degrees).")]
    [Range(0f, 60f)] [SerializeField] private float spineTwist = 15f;
    [Tooltip("How much of the bones carry flowers (0..1).")]
    [Range(0f, 1f)] [SerializeField] private float flowers = 0.5f;

    [Header("Ribs")]
    [Tooltip("Seconds for the ripple to pass any one rib (it rises and settles in this time).")]
    [Min(1f)] [SerializeField] private float wavePeriod = 18f;
    [Tooltip("Rib pairs between one crest of the ripple and the next. Each pair lags a little behind the one before.")]
    [Min(2f)] [SerializeField] private float waveLength = 16f;
    [Tooltip("Share of the ripple during which a rib is lifted (higher = wider gaps).")]
    [Range(0.1f, 0.9f)] [SerializeField] private float waveOpenShare = 0.25f;
    [Tooltip("Heat (summed over creatures in the kiln heart) that warms the bones fully.")]
    [Min(1f)] [SerializeField] private float heatToWarm = 25f;

    [Header("Waking")]
    [Tooltip("Seconds fully warm before it wakes.")]
    [Min(0f)] [SerializeField] private float wakeAfter = 2.5f;
    [Tooltip("Degrees each metre of spine is turned further round (the helix of the ribcage), asleep and awake.")]
    [Range(0f, 30f)] [SerializeField] private float helixTwist = 9f;
    [Tooltip("Awake: how fast the helix rolls along the body, degrees per second.")]
    [SerializeField] private float helixRoll = 30f;
    [Min(0.5f)] [SerializeField] private float crawlSpeed = 6f;
    [Min(0.5f)] [SerializeField] private float flySpeed = 10f;
    [Tooltip("Height of the head above the ground while crawling and while flying.")]
    [SerializeField] private Vector2 travelHeights = new Vector2(3f, 24f);
    [Tooltip("Seconds between switching from crawling to flying and back (random in this range).")]
    [SerializeField] private Vector2 modeTime = new Vector2(25f, 45f);

    [Header("Children")]
    [Tooltip("Her young, curled up round the mouth of the hole (BoneChild). Warm one with fire to ride it.")]
    [Range(0, 6)] [SerializeField] private int children = 3;

    [Header("Nearby Tentacles")]
    [Tooltip("Creature settings to copy for the tentacles wandering near the children and bubble nests.")]
    [SerializeField] private TentacleCreature tentacleTemplate;
    [Range(0, 6)] [SerializeField] private int nearbyTentacles = 3;

    [Header("Rewards")]
    [SerializeField] private string seedId = "kiln_seed";
    [SerializeField] private string glyphId = "kiln";

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool built;
    [SerializeField] private int sinkholeIndex = -1;
    [Range(0f, 1f)] [SerializeField] private float warmth;
    [SerializeField] private bool awake;
    [SerializeField] private bool flying;

    private sealed class Station
    {
        public float s;
        public Transform transform;
    }

    private Transform root;
    private readonly List<Station> stations = new List<Station>();
    private readonly List<BoneRib> ribs = new List<BoneRib>();
    private readonly List<Renderer> boneRenderers = new List<Renderer>();
    private Transform skull;
    private Material floraMaterial;
    private Material ribMaterial;
    private MaterialPropertyBlock glowBlock;
    private Vector3 heartPoint;
    private float heatTarget, nextHeatCheck, warmTimer;
    private readonly Collider[] heatHits = new Collider[32];
    private readonly HashSet<Creature> heatCounted = new HashSet<Creature>();
    private const int GroundMask = 1 << 7;

    // The sleeping shape (root space) and, once awake, the trail the head has left (world space, head last).
    private List<Vector3> path;
    private float pathLength;
    private readonly List<Vector3> trail = new List<Vector3>();
    private readonly List<float> trailDistance = new List<float>();
    private Vector3 head, headDir, waypoint, mouth;
    private float awakeBlend, modeTimer, wakeTime;
    private bool emerging;

    /// <summary>0..1: how warm the bones are.</summary>
    public float Warmth => warmth;
    public bool Awake => awake;
    public bool Flying => flying;
    public Vector3 Head => awake ? head : (skull != null ? skull.position : transform.position);
    /// <summary>World position of the kiln heart (where heat is measured).</summary>
    public Vector3 HeartPoint => heartPoint;
    /// <summary>Average lift of the ribs (0 all down .. 1 all up), and how many are up right now.</summary>
    public Vector2 Lift
    {
        get
        {
            float sum = 0f; int up = 0;
            foreach (BoneRib rib in ribs) { sum += rib.open; if (rib.open > 0.6f) up++; }
            return new Vector2(ribs.Count > 0 ? sum / ribs.Count : 0f, up);
        }
    }
    public Transform Root => root;
    public IReadOnlyList<BoneRib> Ribs => ribs;

    private void Update()
    {
        if (!built)
        {
            if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
            // Wait for the terrain itself, and for the spire to claim its sinkhole first.
            HangingSpire spire = FindFirstObjectByType<HangingSpire>();
            bool spireReady = spire == null || !spire.isActiveAndEnabled || spire.Built;
            if (world != null && world.LayoutReady && world.SinkholeCount > 1 && spireReady &&
                Physics.Raycast(world.transform.position + Vector3.up * 200f, Vector3.down, 600f, GroundMask, QueryTriggerInteraction.Ignore))
                Build(spire != null ? spire.SinkholeIndex : -1);
            return;
        }
        if (root == null) return;
        UpdateHeat();

        if (!awake)
        {
            warmTimer = warmth > 0.97f ? warmTimer + Time.deltaTime : 0f;
            if (warmTimer > wakeAfter) Wake();
        }

        // One smooth ripple runs along the ribcage: every rib follows the one before it a little later, like
        // a centipede's legs. Awake, it runs faster and fuller (legs while crawling, fins while flying).
        int count = ribs.Count;
        float period = awake ? (flying ? 6f : 8f) : wavePeriod;
        float share = awake ? 0.6f : waveOpenShare;
        float time = Time.time * Mathf.PI * 2f / period;
        for (int i = 0; i < count; i++)
        {
            float pair = i / 2;
            float side = (i % 2) * (awake && !flying ? Mathf.PI : 0.6f);
            float wave = 0.5f + 0.5f * Mathf.Sin(time - pair * Mathf.PI * 2f / waveLength - side);
            float target = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - share * 1.4f, 1f, wave));
            ribs[i].Drive(awake ? target : Mathf.Max(target, warmth), Time.deltaTime);
        }

        if (awake)
        {
            // Ridden: [C] by the skull climbs on (and off). With tentacle reins in her spine she goes where the rider looks.
            if (Ridden) { if (BoneRide.CanToggle && BoneRide.PressedC()) { BoneRide.GetOff(); PickWaypoint(); } }
            else if (BoneRide.Mount == null && BoneRide.CanToggle && skull != null && BoneRide.Near(skull.position, 6f, root) && BoneRide.PressedC())
                BoneRide.GetOn(this, root, skull, ReinPoints());
            MoveHead(Time.deltaTime);
            PlaceBody();
            if (Ridden && skull != null) BoneRide.Seat(skull.position + skull.up * 2.6f);
        }
    }

    /// <summary>True while the player rides her.</summary>
    public bool Ridden => BoneRide.Mount == (object)this;
    public bool Reined => BoneRide.Reins(root) >= 2;
    public Transform Skull => skull;

    /// <summary>Two vertebrae just behind the skull, for the reins.</summary>
    private IEnumerable<Transform> ReinPoints()
    {
        var near = new List<Station>(stations);
        near.Sort((x, y) => Vector3.Distance(x.transform.position, skull.position).CompareTo(Vector3.Distance(y.transform.position, skull.position)));
        if (near.Count > 1) yield return near[1].transform;
        if (near.Count > 3) yield return near[3].transform;
    }

    // ---------------- Building ----------------

    private void Build(int spireHole)
    {
        built = true;
        var random = new System.Random(seed);
        sinkholeIndex = sinkholeOverride >= 0 && sinkholeOverride < world.SinkholeCount ? sinkholeOverride : PickSinkhole(spireHole);
        if (sinkholeIndex < 0)
        {
            Debug.LogWarning("Bone Kiln: no free sinkhole inside the map.");
            return;
        }

        // The mouth: the lowest ground round the hole is the rim.
        Vector3 approx = world.SinkholeCentre(sinkholeIndex, world.transform.position.y, out float holeRadius);
        world.ColumnHeights(approx, out float surfaceAtCentre, out float ceilingAtCentre, out float innerGroundAtCentre);
        Vector3 mouthApprox = world.SinkholeCentre(sinkholeIndex, surfaceAtCentre, out _);
        float rimY = float.MaxValue;
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI / 6f;
            rimY = Mathf.Min(rimY, GroundHeight(mouthApprox + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (holeRadius + 2.5f)));
        }
        mouth = world.SinkholeCentre(sinkholeIndex, rimY, out _);
        float R = holeRadius + 0.5f;
        Vector3 heading = PickHeading(mouth, R);

        root = new GameObject("Bone Kiln (runtime)").transform;
        root.SetParent(transform, false);
        root.SetPositionAndRotation(mouth, Quaternion.LookRotation(heading, Vector3.up));
        floraMaterial = MakeFloraMaterial();
        Shader comb = Shader.Find("Capstone/Landmarks/BoneComb");
        ribMaterial = comb != null ? new Material(comb) { name = "Bone Kiln Ribs (runtime)" } : null;

        // ---- Spine: a smooth curve through key points (local space, y = 0 at the rim, +z towards the tail) ----
        Vector3 Shaft(float y)
        {
            Vector3 local = root.InverseTransformPoint(world.SinkholeCentre(sinkholeIndex, rimY + y, out _));
            return new Vector3(local.x, y, local.z);
        }
        float ceiling = ceilingAtCentre - rimY;
        float innerGround = innerGroundAtCentre - rimY;
        float Ground(float z) => Mathf.Abs(z) < R ? 0f : LocalGround(new Vector3(0f, 0f, z));
        var keys = new List<Vector3>
        {
            Shaft(ceiling - 6f) + Vector3.back * (R * 0.25f),
            Shaft(ceiling + 3f) + Vector3.back * (R * 0.55f),
            Shaft(-6f) + Vector3.back * (R * 0.65f),
            new Vector3(0f, 1.8f, -R * 0.8f),
            new Vector3(0f, 6.5f, -R * 0.25f),
            new Vector3(0f, 7.2f, R * 0.4f),
            new Vector3(0f, Ground(R + 3f) + 6f, R + 3f),
            new Vector3(0f, Ground(R + 11f) + 4f, R + 11f),
            new Vector3(0f, Ground(R + 19f) + 1f, R + 19f),
            new Vector3(0f, Ground(R + 26f) - 4f, R + 26f),
        };
        path = CatmullRom(keys, 0.5f);
        var lengths = new float[path.Count];
        for (int i = 1; i < path.Count; i++) lengths[i] = lengths[i - 1] + Vector3.Distance(path[i - 1], path[i]);
        pathLength = lengths[path.Count - 1];
        float total = pathLength;
        Vector3 At(float s)
        {
            s = Mathf.Clamp(s, 0f, total);
            int i = System.Array.BinarySearch(lengths, s);
            if (i >= 0) return path[i];
            i = ~i;
            if (i <= 0) return path[0];
            if (i >= path.Count) return path[path.Count - 1];
            return Vector3.Lerp(path[i - 1], path[i], Mathf.InverseLerp(lengths[i - 1], lengths[i], s));
        }
        // Thick over the mouth, thinner down the neck and out to the tail.
        float sMid = 0f;
        { float best = float.MaxValue; for (int i = 0; i < path.Count; i++) { float d = Mathf.Abs(path[i].z); if (path[i].y > 3f && d < best) { best = d; sMid = lengths[i]; } } }
        float Radius(float s) => s < sMid ? Mathf.Lerp(1.3f, 2.3f, Mathf.SmoothStep(0f, 1f, s / Mathf.Max(1f, sMid))) : Mathf.Lerp(2.3f, 0.5f, (s - sMid) / Mathf.Max(1f, total - sMid));

        // Where the ribs go: from the far rim of the mouth to where the tail sinks.
        var ribStations = new List<float>();
        for (float s = sMid - R * 1.2f; s < total; s += ribSpacing)
        {
            Vector3 c = At(s);
            if (c.z < -R * 0.8f) continue;
            if (c.y - Ground(c.z) < 2.2f) { if (c.z > R) break; continue; }
            ribStations.Add(s);
        }
        // Vertebrae: one under every rib pair, and spaced by their size elsewhere.
        var spineStations = new List<float>();
        {
            float s = 0f;
            int next = 0;
            while (s < total)
            {
                if (next < ribStations.Count && s >= ribStations[next] - ribSpacing * 0.5f)
                {
                    s = ribStations[next++];
                    spineStations.Add(s);
                    s += ribSpacing;
                    continue;
                }
                spineStations.Add(s);
                s += Radius(s) * 1.65f;
            }
        }

        // ---- Vertebrae: each its own bone, so the spine can bend and twist as a chain ----
        int order = 0;
        for (int v = 0; v < spineStations.Count; v++)
        {
            float s = spineStations[v];
            Vector3 tangent = (At(s + 0.5f) - At(s - 0.5f)).normalized;
            Quaternion frame = Quaternion.LookRotation(tangent, SleepingUp(s, tangent));
            float r = Radius(s);
            bool ribbed = ribStations.Contains(s);
            var builder = new BoneShapes.Builder();
            BoneShapes.Vertebra(builder, Vector3.zero, Vector3.forward, Vector3.up, r, (float)random.NextDouble(), ribbed ? ribSpacing * 0.55f : -1f);
            GameObject vertebra = Part($"Vertebra {v}", root, builder.ToMesh("Vertebra"), boneMaterial, layer: 0, convex: false);
            vertebra.transform.localPosition = At(s);
            vertebra.transform.localRotation = frame;
            boneRenderers.Add(vertebra.GetComponent<Renderer>());
            stations.Add(new Station { s = s, transform = vertebra.transform });

            // Flowers on the back, now and then a vine wound round the bone.
            var flora = new BoneFlora(seed * 977 + v);
            if (!ribbed && flora.Chance() < flowers) flora.Vine(Vector3.back * r * 0.8f, Vector3.forward * r * 0.8f, r * 1.05f, 2f);
            if (!ribbed && flora.Chance() < flowers * 0.6f) flora.Cluster(Vector3.up * (r * 1.4f), Vector3.up, Mathf.Lerp(0.5f, 1f, r / 2.3f));
            flora.Attach(vertebra.transform, floraMaterial);

            if (!ribbed) continue;
            for (int side = -1; side <= 1; side += 2)
                ribs.Add(MakeRib(vertebra.transform, At(s), frame, r, side, order++, At(s).z, R, Ground(At(s).z), random));
        }

        // ---- Skull: hanging head down from the ceiling of the inner world ----
        Vector3 skullAt = At(0f);
        GameObject skullObject = Part("Skull", root, BoneShapes.Skull(new Vector3(4.5f, 3.6f, 7.5f), (float)random.NextDouble()), boneMaterial, layer: 0, convex: false);
        skull = skullObject.transform;
        skull.localPosition = skullAt + Vector3.down * 5f;
        skull.localRotation = Quaternion.LookRotation(new Vector3(0f, -0.9f, -0.35f), Vector3.forward);
        boneRenderers.Add(skullObject.GetComponent<Renderer>());
        for (int s = -1; s <= 1; s += 2)
        {
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(eye.GetComponent<Collider>());
            eye.name = "Eye Socket";
            eye.transform.SetParent(skull, false);
            eye.transform.localPosition = new Vector3(s * 2.3f, 2.1f, 1.6f);
            eye.transform.localScale = new Vector3(1.6f, 1.2f, 1.9f);
            eye.GetComponent<Renderer>().sharedMaterial = inkMaterial != null ? inkMaterial : boneMaterial;
        }
        {
            // A crown of blooms on the brow.
            var flora = new BoneFlora(seed * 13);
            for (int i = 0; i < 4; i++)
                flora.Cluster(new Vector3((i - 1.5f) * 1.6f, 3.4f, -1.5f + i * 0.6f), Vector3.up, 1.1f);
            flora.Attach(skull, floraMaterial);
        }

        // ---- Kiln heart: a bowl of fused bone on the ground beside the tail ----
        Vector3 heartLocal = new Vector3(10f, 0f, R + 14f);
        heartLocal.y = LocalGround(heartLocal);
        GameObject heart = Part("Kiln Heart", root, BoneShapes.Kiln(1.8f, (float)random.NextDouble()), boneMaterial, layer: 0, convex: false);
        heart.transform.localPosition = heartLocal;
        boneRenderers.Add(heart.GetComponent<Renderer>());
        heartPoint = root.TransformPoint(heartLocal + Vector3.up * 0.8f);

        // ---- Rewards: the glyph inside the cage at the lip of the hole, the seed below the skull ----
        if (!string.IsNullOrEmpty(glyphId))
        {
            Vector3 glyphLocal = new Vector3(0f, 0f, R + 2.5f);
            glyphLocal.y = LocalGround(glyphLocal) + 1.4f;
            LandmarkPickup inscription = Pickup("Inscription", root.TransformPoint(glyphLocal), LandmarkPickup.Kind.Inscription);
            inscription.glyphId = glyphId;
            inscription.hoverHeight = 1.4f;
            GameObject mark = Part("Glyph", inscription.transform, LandmarkShapes.Glyph(LandmarkGlyphs.Get(glyphId), 0.9f, 0.07f), glyphMaterial, layer: 0, convex: false, collide: false);
            mark.transform.localPosition = Vector3.down * 0.6f;
        }
        if (!string.IsNullOrEmpty(seedId))
        {
            // On the floor of the inner world, under the skull.
            Vector3 below = root.TransformPoint(new Vector3(skullAt.x, innerGround + 1.5f, skullAt.z));
            LandmarkPickup seedPickup = Pickup("Seed", below, LandmarkPickup.Kind.Seed);
            seedPickup.id = seedId;
            seedPickup.snapToGround = true;
            seedPickup.hoverHeight = 1.3f;
            GameObject pod = Part("Pod", seedPickup.transform, LandmarkShapes.Seed(seed), seedMaterial, layer: 0, convex: false, collide: false);
            pod.transform.localPosition = Vector3.down * 0.5f;
            pod.transform.localScale = Vector3.one * 1.6f;
        }

        // ---- A slow draft down the shaft, so the drop to the skull is a glide, not a fall ----
        {
            Vector3 top = world.SinkholeCentre(sinkholeIndex, rimY - 1f, out _);
            Vector3 bottom = world.SinkholeCentre(sinkholeIndex, innerGroundAtCentre + 1f, out _);
            var draft = new GameObject("Shaft Draft") { layer = 2 };
            draft.transform.SetParent(root, true);
            draft.transform.SetPositionAndRotation((top + bottom) * 0.5f, Quaternion.FromToRotation(Vector3.up, (top - bottom).normalized));
            var capsule = draft.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.radius = Mathf.Max(1f, holeRadius * 0.9f);
            capsule.height = Mathf.Max((top - bottom).magnitude, capsule.radius * 2f);
            draft.AddComponent<ShaftDraft>();
        }

        // Bone shards, loose vertebrae, glassy rib tips and flora scattered round the mouth.
        LandmarkDecor.Kiln(root, mouth, holeRadius, GroundHeight, boneMaterial, ribMaterial, floraMaterial, seed);

        // Her young, curled up round the mouth, away from where her tail lies.
        for (int c = 0; c < children; c++)
        {
            float angle = 120f + c * (120f / Mathf.Max(1, children - 1)) + (float)random.NextDouble() * 15f;
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * heading;
            Vector3 spot = mouth + dir * (R + 10f + (float)random.NextDouble() * 4f);
            spot.y = GroundHeight(spot);
            BoneChild.Spawn(root, spot, Quaternion.LookRotation(-dir).eulerAngles.y, seed * 31 + c, boneMaterial, ribMaterial != null ? ribMaterial : boneMaterial, mouth);
        }
        // Three little nests on the children's side that let fire bubbles float up; the bubbles drift over to her.
        if (children > 0)
            for (int n = 0; n < 3; n++)
            {
                Vector3 dir = Quaternion.Euler(0f, 130f + n * 50f, 0f) * heading;
                Vector3 spot = mouth + dir * (R + 16f);
                spot.y = GroundHeight(spot);
                // They gather in front of her face, just outside the fence of her ribs (so the children are not under them).
                Vector3 gather = mouth - heading * (R + 9f);
                gather.y = GroundHeight(gather);
                KilnBubbleNest.Build(root, spot, boneMaterial, seed * 17 + n, gather, heartPoint);
            }

        SpawnNearbyTentacles(heading, R);

        // Bones become climbable ground only now, so the ground probes above never hit them.
        foreach (Station station in stations) station.transform.gameObject.layer = 7;
        skull.gameObject.layer = 7;
        heart.layer = 7;
        Debug.Log($"Bone Kiln built over sinkhole {sinkholeIndex} at {mouth} with {stations.Count} vertebrae and {ribs.Count} ribs.");
    }

    /// <summary>"Up" for a vertebra with no twist: world up, or (on a vertical stretch) the fallback.</summary>
    private static Vector3 PlainUp(Vector3 tangent, Vector3 fallback)
    {
        Vector3 up = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.9f ? fallback : Vector3.up;
        return Vector3.ProjectOnPlane(up, tangent).normalized;
    }

    /// <summary>Each vertebra turned further round than the last (a helix), wrung a little back and forth on top.</summary>
    private float Twist(float s) => helixTwist * s + spineTwist * Mathf.Sin(s * 0.085f + seed);

    private Vector3 SleepingUp(float s, Vector3 tangent)
    {
        return Quaternion.AngleAxis(Twist(s), tangent) * PlainUp(tangent, Vector3.forward);
    }

    /// <summary>
    /// A rib on a vertebra (frame: z along the spine, y its twisted "up"): a flat curved band that leaves the
    /// vertebra sideways, rounds its shoulder, sweeps back towards the tail and goes down into the ground
    /// beyond the edge of the hole. It hangs from its vertebra, so it turns and travels with it.
    /// </summary>
    private BoneRib MakeRib(Transform vertebra, Vector3 centre, Quaternion frame, float spineRadius, int side, int order, float z, float R, float groundUnder, System.Random random)
    {
        Vector3 attachLocal = Vector3.right * (side * spineRadius * 0.75f);
        Vector3 attach = centre + frame * attachLocal;
        // A curved hook in its vertebra's frame: out to the side, round, and curling back in underneath,
        // sweeping towards the tail. Bigger on bigger vertebrae.
        float length = 3.2f * spineRadius + 3f;
        var joints = new Vector3[4];
        joints[0] = Vector3.zero;
        joints[1] = new Vector3(side * length * 0.45f, 0.3f, -0.3f);
        joints[2] = new Vector3(side * length * 0.8f, -length * 0.35f, -0.8f);
        joints[3] = new Vector3(side * length * 0.62f, -length * 0.78f, -1.3f);
        // A smooth curve through the joints, cut into the three moving pieces.
        var keys = new List<Vector3>(joints);
        List<Vector3> SegmentCurve(int k)
        {
            Vector3 p0 = keys[Mathf.Max(0, k - 1)], p1 = keys[k], p2 = keys[k + 1], p3 = keys[Mathf.Min(3, k + 2)];
            var points = new List<Vector3>();
            const int n = 10;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n, t2 = t * t, t3 = t2 * t;
                points.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3) - p1);
            }
            return points;
        }

        var ribObject = new GameObject($"Rib {order}");
        ribObject.transform.SetParent(vertebra, false);
        ribObject.transform.localPosition = attachLocal;
        ribObject.transform.localRotation = Quaternion.identity;
        BoneRib rib = ribObject.AddComponent<BoneRib>();
        rib.side = side;
        rib.phase = order / 2;
        rib.segments = new Transform[3];
        Transform parent = ribObject.transform;
        float part = (float)random.NextDouble();
        var rest = new List<Vector3>();
        for (int k = 0; k < 3; k++)
        {
            Vector3 from = joints[k], to = joints[k + 1];
            // Flat band, wide along the spine, thinning towards the tip.
            float w0 = Mathf.Lerp(1.1f, 0.55f, k / 3f), w1 = Mathf.Lerp(1.1f, 0.55f, (k + 1) / 3f);
            Mesh band = BoneShapes.Ribbon(SegmentCurve(k), w0, w1, 0.42f, Vector3.forward, part + k * 0.1f, k / 3f, (k + 1) / 3f);
            // Ribs glow like a comb jelly (glassy, bright-edged, rainbow sparkles running along them).
            GameObject segment = Part($"Segment {k}", parent, band, ribMaterial != null ? ribMaterial : boneMaterial, layer: 0, convex: true);
            if (ribMaterial != null) segment.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            segment.transform.localPosition = k == 0 ? Vector3.zero : joints[k] - joints[k - 1];
            boneRenderers.Add(segment.GetComponent<Renderer>());
            rib.segments[k] = segment.transform;
            parent = segment.transform;
            rest.Add(root.TransformPoint(attach + frame * ((from + to) * 0.5f)));
            if (k > 0) rest.Add(root.TransformPoint(attach + frame * from));

            // Growth on the rib: vines round the upper pieces, a bloom where the rib bends.
            var growth = new BoneFlora(seed * 104729 + order * 3 + k);
            if (k < 2 && growth.Chance() < flowers * 0.8f) growth.Vine(Vector3.zero, to - from, Mathf.Lerp(0.42f, 0.32f, k / 2f), 2f + k);
            if (growth.Chance() < flowers * 0.5f) growth.Cluster((to - from) * (0.3f + 0.4f * growth.Chance()), Vector3.right * side, 0.5f);
            growth.Attach(segment.transform, floraMaterial);
        }
        rib.restPoints = rest.ToArray();
        rib.owner = root;
        rib.tipLocal = joints[3] - joints[2];
        return rib;
    }

    private static List<Vector3> CatmullRom(List<Vector3> keys, float step)
    {
        var points = new List<Vector3>();
        for (int i = 0; i < keys.Count - 1; i++)
        {
            Vector3 p0 = keys[Mathf.Max(0, i - 1)], p1 = keys[i], p2 = keys[i + 1], p3 = keys[Mathf.Min(keys.Count - 1, i + 2)];
            int n = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(p1, p2) / step));
            for (int k = 0; k < n; k++)
            {
                float t = k / (float)n, t2 = t * t, t3 = t2 * t;
                points.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
            }
        }
        points.Add(keys[keys.Count - 1]);
        return points;
    }

    private static Material MakeFloraMaterial()
    {
        Shader shader = Shader.Find("Capstone/Landmarks/Flora");
        return shader != null ? new Material(shader) { name = "Bone Kiln Flora (runtime)" } : null;
    }

    // ---------------- Waking and roaming ----------------

    /// <summary>Wakes the colossus: from now on its head leads it out of the cave and all over the map.</summary>
    public void Wake()
    {
        if (awake || root == null) return;
        awake = true;
        emerging = true;
        wakeTime = Time.time;
        // The sleeping spine is the trail it has already "walked": the head (skull end) is its newest point.
        trail.Clear();
        for (int i = path.Count - 1; i >= 0; i--) trail.Add(root.TransformPoint(path[i]));
        head = trail[trail.Count - 1];
        headDir = (root.TransformPoint(path[0]) - root.TransformPoint(path[2])).normalized;
        waypoint = mouth + Vector3.up * 12f;
        modeTimer = Random.Range(modeTime.x, modeTime.y);
        // It no longer bars the way, and nothing should be swept along by it.
        foreach (Collider c in root.GetComponentsInChildren<Collider>())
            if (!c.isTrigger && c.gameObject.name != "Kiln Heart") c.enabled = false;
        foreach (BoneRib rib in ribs) rib.free = true;
        Debug.Log("Bone Kiln: the colossus wakes.");
    }

    private void MoveHead(float deltaTime)
    {
        awakeBlend = Mathf.Clamp01((Time.time - wakeTime) / 6f);
        float t = Time.time;
        Vector3 desired;
        float speed;
        if (emerging)
        {
            // Up out of the shaft, slowly, then off over the ground.
            desired = (waypoint - head).normalized;
            speed = crawlSpeed * 0.7f;
            if ((waypoint - head).magnitude < 3f) { emerging = false; PickWaypoint(); }
        }
        else if (Ridden && Reined)
        {
            desired = BoneRide.Steer(deltaTime);
            speed = flySpeed;
            flying = head.y > GroundHeight(head) + 6f;
        }
        else
        {
            modeTimer -= deltaTime;
            if (modeTimer <= 0f)
            {
                flying = !flying;
                modeTimer = Random.Range(modeTime.x, modeTime.y);
            }
            Vector3 flat = waypoint - head;
            flat.y = 0f;
            if (flat.magnitude < 12f) { PickWaypoint(); flat = waypoint - head; flat.y = 0f; }
            float ground = GroundHeight(head + headDir * 3f);
            float targetY = ground + (flying ? travelHeights.y + 6f * Mathf.Sin(t * 0.3f) : travelHeights.x);
            desired = flat.normalized + Vector3.up * Mathf.Clamp((targetY - head.y) * 0.15f, -0.8f, 0.8f);
            desired.Normalize();
            speed = flying ? flySpeed : crawlSpeed;
        }
        headDir = Vector3.RotateTowards(headDir, desired, (emerging ? 1.6f : 0.9f) * deltaTime, 0f).normalized;
        // Serpentine: sideways weave on the ground, up-and-down swells in the air.
        Vector3 weaveAxis = flying ? Vector3.Cross(headDir, Vector3.up) : Vector3.up;
        if (weaveAxis.sqrMagnitude < 0.25f) weaveAxis = Vector3.right;
        Vector3 move = Quaternion.AngleAxis((flying ? 14f : 24f) * Mathf.Sin(t * (flying ? 0.9f : 1.6f)), weaveAxis.normalized) * headDir;
        head += move * (speed * deltaTime);
        // Never through the ground while crawling.
        if (!emerging && !flying)
        {
            float floor = GroundHeight(head) + 1.5f;
            if (head.y < floor) head.y = Mathf.Lerp(head.y, floor, 1f - Mathf.Exp(-6f * deltaTime));
        }

        if ((head - trail[trail.Count - 1]).sqrMagnitude > 0.16f) trail.Add(head);
        // Forget the trail beyond the tail.
        float kept = 0f;
        for (int i = trail.Count - 1; i > 0; i--)
        {
            kept += Vector3.Distance(trail[i], trail[i - 1]);
            if (kept > pathLength + 6f) { trail.RemoveRange(0, i - 1); break; }
        }
    }

    private void PickWaypoint()
    {
        Vector3 centre = world.transform.position;
        Vector2 half = world.WorldSize * 0.5f * 0.62f;
        for (int i = 0; i < 12; i++)
        {
            Vector3 p = centre + new Vector3(Random.Range(-half.x, half.x), 0f, Random.Range(-half.y, half.y));
            // Prefer somewhere a good way off, so it crosses the map.
            if ((p - head).magnitude > 60f || i == 11) { waypoint = p; return; }
        }
    }

    /// <summary>Lays every vertebra along the head's trail, wrung into a rolling helix.</summary>
    private void PlaceBody()
    {
        // Distances back from the head along the trail.
        trailDistance.Clear();
        trailDistance.Add(0f);
        Vector3 previous = head;
        for (int i = trail.Count - 1; i >= 0; i--)
        {
            trailDistance.Add(trailDistance[trailDistance.Count - 1] + Vector3.Distance(previous, trail[i]));
            previous = trail[i];
        }
        Vector3 Behind(float d)
        {
            if (d <= 0f) return head - headDir * d;
            for (int i = 1; i < trailDistance.Count; i++)
            {
                if (trailDistance[i] < d) continue;
                Vector3 a = i == 1 ? head : trail[trail.Count - i + 1];
                Vector3 b = trail[trail.Count - i];
                return Vector3.Lerp(a, b, Mathf.InverseLerp(trailDistance[i - 1], trailDistance[i], d));
            }
            return trail[0];
        }

        float roll = Time.time * helixRoll;
        float follow = awakeBlend < 1f ? 1f - Mathf.Exp(-4f * Time.deltaTime) : 1f;
        foreach (Station station in stations)
        {
            Vector3 p = Behind(station.s);
            Vector3 tangent = Behind(station.s + 0.6f) - Behind(station.s - 0.6f);
            if (tangent.sqrMagnitude < 1e-6f) continue;
            tangent.Normalize();
            Vector3 plainUp = PlainUp(tangent, station.transform.up);
            // Awake, the helix also rolls slowly along the body.
            Vector3 up = Quaternion.AngleAxis(Twist(station.s) + roll * awakeBlend, tangent) * plainUp;
            Quaternion target = Quaternion.LookRotation(tangent, up);
            // Ease into the new pose over the first seconds, so waking is a stir, not a snap.
            station.transform.SetPositionAndRotation(Vector3.Lerp(station.transform.position, p, follow), Quaternion.Slerp(station.transform.rotation, target, follow));
        }
        if (skull != null)
        {
            Quaternion look = Quaternion.LookRotation(headDir, Mathf.Abs(headDir.y) > 0.95f ? Vector3.forward : Vector3.up);
            Vector3 at = head + headDir * 4f;
            float ease = 1f - Mathf.Exp(-3f * Time.deltaTime);
            skull.SetPositionAndRotation(Vector3.Lerp(skull.position, at, ease), Quaternion.Slerp(skull.rotation, look, ease));
        }
    }

    // ---------------- Placement ----------------

    /// <summary>A sinkhole other than the spire's, well inside the map, nearest the preferred distance from the spawn.</summary>
    private int PickSinkhole(int spireHole)
    {
        Vector3 spawn = world.transform.position;
        Vector3 spireMouth = spireHole >= 0 ? world.SinkholeCentre(spireHole, spawn.y, out _) : Vector3.positiveInfinity;
        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < world.SinkholeCount; i++)
        {
            if (i == spireHole) continue;
            Vector3 hole = world.SinkholeCentre(i, spawn.y, out _);
            if (!InsideMap(hole, 0.62f)) continue;
            if (spireHole >= 0 && new Vector2(hole.x - spireMouth.x, hole.z - spireMouth.z).magnitude < 55f) continue;
            float distance = new Vector2(hole.x - spawn.x, hole.z - spawn.z).magnitude;
            float score = Mathf.Abs(distance - preferredDistance);
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    /// <summary>Direction of the tail from the mouth: inside the map, clear of other sinkholes, on the most even ground.</summary>
    private Vector3 PickHeading(Vector3 mouthPoint, float R)
    {
        Vector3 best = Vector3.forward;
        float bestScore = float.MaxValue;
        for (int h = 0; h < 16; h++)
        {
            Vector3 dir = Quaternion.Euler(0f, h * 22.5f + seed * 11f, 0f) * Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            float score = 0f;
            float last = mouthPoint.y;
            for (int k = 1; k <= 6; k++)
            {
                Vector3 p = mouthPoint + dir * (R + k * 5f);
                if (!InsideMap(p, 0.75f) || !InsideMap(p + right * 15f, 0.75f) || !InsideMap(p - right * 15f, 0.75f) || NearOtherSinkhole(p, 14f)) { score = float.MaxValue; break; }
                float g = GroundHeight(p);
                score += Mathf.Abs(g - last) + Mathf.Abs(GroundHeight(p + right * 10f) - g) * 0.5f + Mathf.Abs(GroundHeight(p - right * 10f) - g) * 0.5f;
                last = g;
            }
            if (score < bestScore) { bestScore = score; best = dir; }
        }
        return best;
    }

    private bool NearOtherSinkhole(Vector3 point, float clearance)
    {
        for (int i = 0; i < world.SinkholeCount; i++)
        {
            if (i == sinkholeIndex) continue;
            Vector3 hole = world.SinkholeCentre(i, point.y, out float radius);
            if (new Vector2(hole.x - point.x, hole.z - point.z).magnitude < clearance + radius) return true;
        }
        return false;
    }

    /// <summary>True when a point is inside the given share of the map's half size (the rim cliffs start at about 0.82).</summary>
    private bool InsideMap(Vector3 point, float share)
    {
        Vector3 local = point - world.transform.position;
        Vector2 half = world.WorldSize * 0.5f;
        return Mathf.Abs(local.x) < half.x * share && Mathf.Abs(local.z) < half.y * share;
    }

    /// <summary>Ground height (local y) under a local point.</summary>
    private float LocalGround(Vector3 local)
    {
        return GroundHeight(root.TransformPoint(new Vector3(local.x, 0f, local.z))) - root.position.y;
    }

    /// <summary>Top surface height at a world point: the generated terrain, or its base height before it exists.</summary>
    private float GroundHeight(Vector3 worldPoint)
    {
        world.ColumnHeights(worldPoint, out float surface, out _, out _);
        Vector3 from = new Vector3(worldPoint.x, surface + 120f, worldPoint.z);
        return Physics.Raycast(from, Vector3.down, out RaycastHit hit, 260f, GroundMask, QueryTriggerInteraction.Ignore) ? hit.point.y : surface;
    }

    // ---------------- Heat ----------------

    private void UpdateHeat()
    {
        if (Time.time >= nextHeatCheck)
        {
            nextHeatCheck = Time.time + 0.5f;
            float heat = 0f;
            heatCounted.Clear();
            int count = Physics.OverlapSphereNonAlloc(heartPoint, 2.6f, heatHits, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Creature creature = heatHits[i].GetComponentInParent<Creature>();
                if (creature != null && heatCounted.Add(creature)) heat += Mathf.Max(0f, creature.Heat);
            }
            heatTarget = Mathf.Clamp01(heat / heatToWarm);
        }
        // Warms over a few seconds; cools slowly, so there is time to get through.
        warmth = Mathf.MoveTowards(warmth, heatTarget, Time.deltaTime * (heatTarget > warmth ? 0.25f : 0.04f));

        if (boneMaterial == null || !boneMaterial.HasProperty("_GlowColor")) return;
        if (glowBlock == null) glowBlock = new MaterialPropertyBlock();
        Color cool = boneMaterial.GetColor("_GlowColor");
        float strength = boneMaterial.GetFloat("_GlowStrength");
        float glow = awake ? 0.5f : warmth;
        glowBlock.SetColor("_GlowColor", Color.Lerp(cool, new Color(1f, 0.42f, 0.12f), glow));
        glowBlock.SetFloat("_GlowStrength", strength * (1f + 4f * glow));
        foreach (Renderer r in boneRenderers) if (r != null) r.SetPropertyBlock(glowBlock);
    }

    // ---------------- Helpers ----------------

    private void SpawnNearbyTentacles(Vector3 heading, float radius)
    {
        if (nearbyTentacles <= 0) return;
        if (tentacleTemplate == null)
            foreach (TentacleCreature creature in FindObjectsByType<TentacleCreature>(FindObjectsSortMode.None))
                if (!creature.IsCarried && !creature.IsDead && creature.GetComponent<SpireCrawler>() == null)
                { tentacleTemplate = creature; break; }
        if (tentacleTemplate == null) return;

        string settings = JsonUtility.ToJson(tentacleTemplate);
        for (int i = 0; i < nearbyTentacles; i++)
        {
            float angle = nearbyTentacles == 1 ? 180f : Mathf.Lerp(105f, 255f, i / (float)(nearbyTentacles - 1));
            Vector3 spot = mouth + Quaternion.Euler(0f, angle, 0f) * heading * (radius + 21f);
            spot.y = GroundHeight(spot) + 1f;
            // Activate only after placement/settings, so it builds its own body and wanders around this spot.
            var go = new GameObject($"Kiln Tentacle {i + 1}") { layer = tentacleTemplate.gameObject.layer };
            go.SetActive(false);
            go.transform.SetParent(root, false);
            go.transform.position = spot;
            go.transform.localScale = tentacleTemplate.transform.localScale;
            var collider = go.AddComponent<SphereCollider>();
            SphereCollider sourceCollider = tentacleTemplate.GetComponent<SphereCollider>();
            collider.radius = sourceCollider != null ? sourceCollider.radius : 0.45f;
            collider.center = sourceCollider != null ? sourceCollider.center : Vector3.zero;
            var copy = go.AddComponent<TentacleCreature>();
            JsonUtility.FromJsonOverwrite(settings, copy);
            JsonUtility.FromJsonOverwrite($"{{\"seed\":{seed * 53 + i},\"wanderRadius\":3,\"simulationEnabled\":true}}", copy);
            go.SetActive(true);
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

    private static GameObject Part(string name, Transform parent, Mesh mesh, Material material, int layer, bool convex, bool collide = true)
    {
        var go = new GameObject(name) { layer = layer };
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        if (collide)
        {
            var collider = go.AddComponent<MeshCollider>();
            collider.convex = convex;
            collider.sharedMesh = mesh;
        }
        return go;
    }

    public void SetMaterials(Material bone, Material ink, Material glyph, Material seedPod)
    {
        boneMaterial = bone;
        inkMaterial = ink;
        glyphMaterial = glyph;
        seedMaterial = seedPod;
    }

    public void SetTentacleTemplate(TentacleCreature template) => tentacleTemplate = template;
}
