using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// One of the colossus's young: a short spine of vertebrae with three pairs of ribs and a small skull, every
/// bone wrapped in a glassy skin. It crawls in slow loops round its mother, now and then curling up to rest.
///   - Habits that hint at what fire does to it: it notices fire bubbles drifting nearby and goes after them,
///     its ribs fluttering faster the closer one is, rearing its head up to reach one floating just above it.
    ///   - Heat: up to three rib pairs embrace fire bubbles in reserve; flying spends them oldest first.
///     The body warms to amber; the other ribs beat like wings and it rises and drifts in slow loops.
///   - Riding: [RMB] gets on or off. A carried tentacle can grapple onto the back.
///   - Reins: with a tentacle creature on the back, climbing on sends two of its arms into the child's spine
///     (or shoot them in yourself). W/S changes altitude, A/D moves sideways, Space/Ctrl moves forward/back.
///     Without reins it ignores the rider; without heat it cannot fly at all.
/// The mother (BoneKiln) is ridden the same way, once she is awake.
/// Built by BoneKiln.
/// </summary>
[DefaultExecutionOrder(-40)] // moves before the player's camera follows the player
public sealed class BoneChild : MonoBehaviour
{
    public static readonly List<BoneChild> All = new List<BoneChild>();

    [Min(3)] public int vertebrae = 7;
    [Min(0.2f)] public float spacing = 0.55f;
    [Tooltip("Scales the whole child (bones, skin, the curl and the gaps between vertebrae).")]
    [Min(0.3f)] public float size = 1.7f;
    [Tooltip("Heat (summed over creatures cradled in the ribs) for full lift.")]
    [Min(0.5f)] public float heatForLift = 6f;
    [Min(0.5f)] public float flySpeed = 7f;
    [Min(0.5f)] public float climbSpeed = 3.5f;
    [Tooltip("Height it hovers at when warm and not steered (above the ground).")]
    [Min(0.5f)] public float hoverHeight = 2.5f;
    [Tooltip("Heat spent each second while airborne.")]
    [Min(0f)] public float flightHeatPerSecond = 1.35f;
    [Tooltip("Extra heat spent per metre of actual upward travel.")]
    [Min(0f)] public float climbHeatPerMetre = 0.54f;
    [Range(1, 3)] public int bubbleCapacity = 3;

    public float Warmth { get; private set; }
    public float FlightStrength => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.8f, Warmth));
    public bool Flying { get; private set; }
    public float FuelHeat
    {
        get { float total = 0f; foreach (StoredFuel fuel in storedFuel) if (fuel.creature is FireBubble fire && !fire.IsDead) total += fire.Heat; return total; }
    }
    public int StoredBubbleCount => storedFuel.Count;
    public bool Ridden => BoneRide.Mount == (object)this;
    public bool Reined => BoneRide.Reins(transform) >= 2;
    public Transform Head => beads.Count > 0 ? beads[0] : transform;
    public Vector3 Chest => beads.Count > 3 ? beads[3].position : transform.position;
    public Vector3 RidePoint => beads.Count > 2 ? beads[2].position : Head.position;
    public Vector3 RiderPosition => RidePoint + (beads.Count > 2 ? beads[2].up : Vector3.up) * (0.55f * size) + Vector3.up;

    public bool TryGetRideSeat(out Transform seat)
    {
        seat = beads.Count > 2 && isActiveAndEnabled ? beads[2] : null;
        return seat != null;
    }

    public bool MountAt(Transform seat)
    {
        if (!TryGetRideSeat(out Transform ownSeat) || seat != ownSeat || BoneAwakeningCutscene.Playing) return false;
        BoneRide.GetOn(this, transform, seat, ReinPoints());
        return Ridden;
    }

    private readonly List<Transform> beads = new List<Transform>();      // one per vertebra (bone + skin), head first
    private readonly List<Transform[]> ribs = new List<Transform[]>();   // [left, right] on vertebrae 2..4
    private readonly List<float> ribBeat = new List<float>();
    private readonly List<Vector3> trail = new List<Vector3>();
    private Vector3 head, headDir, rest;
    private float restYaw, loopAngle;
    private sealed class StoredFuel
    {
        public Creature creature;
        public int rib;
        public Vector3 local, from;
        public float start;
        public readonly List<Mesh> meshes = new List<Mesh>();
    }
    private readonly List<StoredFuel> storedFuel = new List<StoredFuel>();
    private Creature cradled => storedFuel.Count > 0 ? storedFuel[0].creature : null;
    private readonly MeshFilter[,,] cradleFilters = new MeshFilter[3, 2, 2]; // rib pair, side, bone/skin
    private readonly Mesh[,,] openRibMeshes = new Mesh[3, 2, 2];
    private Renderer[] bodyRenderers;
    private MaterialPropertyBlock warmBlock;
    private static readonly string[] WarmProperties = { "_ColorA", "_ColorB", "_ColorMid", "_Body", "_Rim", "_Core", "_GlowColor", "_FringeB", "_FringeC" };
    private static readonly Color[] WarmColors = {
        new Color(1f, 0.85f, 0.56f), new Color(0.12f, 0.035f, 0.02f), new Color(0.78f, 0.29f, 0.07f),
        new Color(0.85f, 0.22f, 0.035f), new Color(1f, 0.62f, 0.16f), new Color(1f, 0.85f, 0.42f),
        new Color(1f, 0.34f, 0.045f), new Color(1f, 0.55f, 0.12f), new Color(1f, 0.24f, 0.06f)
    };
    private readonly Collider[] hits = new Collider[16];
    private float nextHeatCheck, heat, nextGroundCheck;
    private float[] lift;

    // ---- Habits while cold ----
    [Tooltip("Crawling speed round its mother, and after a fire bubble.")]
    public Vector2 crawlSpeeds = new Vector2(1.1f, 2.6f);
    [Tooltip("Fire bubbles nearer than this catch its attention.")]
    [Min(1f)] public float noticeRange = 16f;
    private Vector3 mother;
    private float orbitRadius = 20f, orbitSense = 1f, restTimer, crawlTimer, rearUp, nextLook;
    private bool resting = true;
    private Creature chasing;
    /// <summary>0..1: how stirred up it is by fire nearby (ribs flutter, it hurries).</summary>
    public float Lively { get; private set; }
    /// <summary>The fire bubble it is going after, if any (for tests).</summary>
    public Creature Chasing => chasing;
    /// <summary>True while it rests curled up.</summary>
    public bool Resting => resting;

    /// <summary>True when any child holds this creature in its ribs.</summary>
    public static bool IsCradled(Creature creature)
    {
        if (creature is FireBubble bubble && bubble.IsBoundToBone) return true;
        foreach (BoneChild c in All) if (c != null) foreach (StoredFuel fuel in c.storedFuel) if (fuel.creature == creature) return true;
        return false;
    }
    private const int Ground = 1 << 7;

    // ---------------- Building ----------------

    /// <summary>Makes a curled child lying at position (on the ground), facing yaw.</summary>
    public static BoneChild Spawn(Transform parent, Vector3 position, float yaw, int seed, Material bone, Material skin, Vector3 mother)
    {
        var go = new GameObject("Bone Child");
        go.transform.SetParent(parent, true);
        go.transform.position = position;
        var child = go.AddComponent<BoneChild>();
        child.mother = mother;
        child.orbitRadius = Mathf.Max(8f, new Vector2(position.x - mother.x, position.z - mother.z).magnitude);
        child.orbitSense = (seed & 1) == 0 ? 1f : -1f;
        child.restTimer = 3f + (seed % 7);
        child.Build(seed, bone, skin, yaw);
        return child;
    }

    private void Build(int seed, Material bone, Material skin, float yaw)
    {
        var random = new System.Random(seed);
        rest = transform.position;
        restYaw = yaw;
        loopAngle = (float)random.NextDouble() * 360f;
        for (int i = 0; i < vertebrae; i++)
        {
            // Thick in the middle, thin at the tail; the first one carries the skull.
            float t = i / (float)(vertebrae - 1);
            float radius = Mathf.Lerp(0.2f, 0.09f, t) * (1f + 0.4f * Mathf.Sin(t * Mathf.PI));
            var bead = new GameObject(i == 0 ? "Head" : $"Vertebra {i}") { layer = 7 };
            bead.transform.SetParent(transform, false);
            var b = new BoneShapes.Builder();
            BoneShapes.Vertebra(b, Vector3.zero, Vector3.forward, Vector3.up, radius, (float)random.NextDouble(), spacing * 0.42f);
            AddMesh(bead.transform, b.ToMesh("Child Vertebra"), bone, Vector3.zero, Vector3.one);
            if (i == 0) AddMesh(bead.transform, BoneShapes.Skull(new Vector3(0.32f, 0.26f, 0.5f), (float)random.NextDouble()), bone, new Vector3(0f, 0.05f, 0.35f), Vector3.one);
            // The skin: a soft glassy bead round each vertebra, overlapping the next.
            Vector3 skinSize = new Vector3(radius * 3.2f, radius * 2.8f, spacing * 1.35f) * (i == 0 ? 1.5f : 1f);
            AddMesh(bead.transform, SphereMesh(), skin, i == 0 ? new Vector3(0f, 0.05f, 0.25f) : Vector3.zero, skinSize);
            var collider = bead.AddComponent<SphereCollider>();
            collider.radius = Mathf.Max(0.18f, radius * 1.4f);
            bead.transform.localScale = Vector3.one * size;
            beads.Add(bead.transform);

            if (i >= 2 && i <= 4)
            {
                // A pair of ribs that fold down along the body when cold and beat like wings when warm.
                var pair = new Transform[2];
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1f : 1f;
                    var hinge = new GameObject(side < 0 ? "Rib L" : "Rib R").transform;
                    hinge.SetParent(bead.transform, false);
                    hinge.localPosition = new Vector3(side * radius * 0.8f, 0.05f, 0f);
                    float length = 1.3f - (i - 2) * 0.2f;
                    var points = new List<Vector3>();
                    for (int k = 0; k <= 8; k++)
                    {
                        float u = k / 8f;
                        points.Add(new Vector3(side * Mathf.Sin(u * 1.9f) * length, Mathf.Sin(u * 2.6f) * 0.22f - u * u * 0.15f, -u * 0.35f));
                    }
                    MeshFilter ribBone = AddMesh(hinge, BoneShapes.Ribbon(points, 0.11f, 0.05f, 0.05f, Vector3.forward, (float)random.NextDouble()), bone, Vector3.zero, Vector3.one);
                    // A wing of skin stretched along the rib.
                    MeshFilter ribSkin = AddMesh(hinge, BoneShapes.Ribbon(points, 0.42f, 0.16f, 0.025f, Vector3.forward, 0.5f), skin, new Vector3(0f, 0f, -0.08f), Vector3.one);
                    cradleFilters[i - 2, s, 0] = ribBone;
                    cradleFilters[i - 2, s, 1] = ribSkin;
                    openRibMeshes[i - 2, s, 0] = ribBone.sharedMesh;
                    openRibMeshes[i - 2, s, 1] = ribSkin.sharedMesh;
                    pair[s] = hinge;
                }
                ribs.Add(pair);
                ribBeat.Add(0f);
            }
        }
        // Curled up where it lies.
        spacing *= size;
        lift = new float[beads.Count];
        PlaceCurled(1f);
        head = beads[0].position;
        headDir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        bodyRenderers = GetComponentsInChildren<Renderer>();
        All.Add(this);
    }

    private static Mesh sphere;
    private static Mesh SphereMesh()
    {
        if (sphere != null) return sphere;
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere = temp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(temp);
        return sphere;
    }

    private static MeshFilter AddMesh(Transform parent, Mesh mesh, Material material, Vector3 at, Vector3 scale)
    {
        var go = new GameObject("Part");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = at;
        go.transform.localScale = scale;
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.On;
        return filter;
    }

    private void OnDestroy()
    {
        All.Remove(this);
        foreach (StoredFuel fuel in storedFuel) foreach (Mesh mesh in fuel.meshes) if (mesh != null) Destroy(mesh);
    }

    // ---------------- Update ----------------

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || beads.Count == 0) return;
        UpdateHeat();
        // Keep the flight movement until it has settled, even after the last heat is spent.
        bool flying = Warmth > 0.35f || (Flying && head.y > GroundBelow(head) + 1.3f);
        Flying = flying;
        if (flying) { resting = false; crawlTimer = Mathf.Max(crawlTimer, 8f); rearUp = 0f; }
        else if (resting && chasing == null) Lively = Mathf.MoveTowards(Lively, 0f, dt);
        if (flying)
        {
            Fly(dt);
            PlaceAlongTrail(dt);
        }
        else if (!resting)
        {
            Crawl(dt);
            PlaceAlongTrail(dt);
        }
        else
        {
            restTimer -= dt;
            if (restTimer <= 0f) { resting = false; crawlTimer = Random.Range(15f, 28f); }
            // The ground under it may only have been built after it was placed: settle onto it (never buried).
            if (Time.time >= nextGroundCheck)
            {
                nextGroundCheck = Time.time + 0.5f;
                rest.y = GroundBelow(rest);
                // Each vertebra lies on the ground under it, on a slope too.
                for (int i = 0; i < beads.Count; i++)
                {
                    CurledPose(i, 0f, out Vector3 p, out _);
                    lift[i] = Mathf.Max(0f, GroundBelow(p) - rest.y);
                }
            }
            // Cold: it sinks back to where it lay and curls up.
            float curl = 1f - Mathf.Exp(-0.8f * dt);
            PlaceCurled(curl);
            head = beads[0].position;
            headDir = Vector3.ProjectOnPlane(beads[0].forward, Vector3.up).normalized;
            trail.Clear();
        }
        BeatRibs(dt, flying);
        if (Ridden) BoneRide.Seat(beads[2].position + beads[2].up * (0.55f * size));
    }

    /// <summary>Finds the nearest free fire bubble in range (checked twice a second). True if one is being chased.</summary>
    private bool LookForFire()
    {
        if (resting && restTimer > 0f) { chasing = null; return false; }
        if (Time.time >= nextLook)
        {
            nextLook = Time.time + 0.5f;
            chasing = null;
            float best = noticeRange;
            foreach (FireBubble bubble in FindObjectsByType<FireBubble>(FindObjectsSortMode.None))
            {
                if (bubble == null || bubble.IsCarried || IsCradled(bubble)) continue;
                float d = Vector3.Distance(bubble.transform.position, beads[0].position);
                if (d < best) { best = d; chasing = bubble; }
            }
        }
        if (chasing != null && (!chasing.isActiveAndEnabled || chasing.IsCarried)) chasing = null;
        return chasing != null;
    }

    /// <summary>
    /// Cold crawling along the ground: in a slow loop round its mother, or after a fire bubble, rearing its head
    /// up towards one floating just above. Every so often it stops and curls up to rest where it is.
    /// </summary>
    private void Crawl(float dt)
    {
        // A nearby bubble no longer keeps it chasing forever. Rest is committed until the nap finishes.
        crawlTimer -= dt;
        if (crawlTimer <= 0f)
        {
            BeginRest(Random.Range(6f, 12f));
            return;
        }
        if (trail.Count == 0) trail.Add(head);
        bool fire = LookForFire();
        float distance = fire ? Vector3.Distance(chasing.transform.position, beads[0].position) : float.MaxValue;
        Lively = Mathf.MoveTowards(Lively, fire ? Mathf.Clamp01(1.3f - distance / noticeRange) : 0f, dt * 0.8f);
        Vector3 target;
        if (fire)
        {
            target = chasing.transform.position;
            resting = false;
        }
        else
        {
            // A point a little further round the circle about its mother.
            Vector3 from = head - mother;
            from.y = 0f;
            float angle = Mathf.Atan2(from.x, from.z) * Mathf.Rad2Deg + 25f * orbitSense;
            target = mother + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * orbitRadius;
        }
        Vector3 flat = Vector3.ProjectOnPlane(target - head, Vector3.up);
        if (flat.sqrMagnitude > 0.04f)
            headDir = Vector3.RotateTowards(headDir, flat.normalized, (fire ? 1.4f : 0.6f) * dt, 0f).normalized;
        // A slow side-to-side weave, like something feeling its way.
        Vector3 weave = Quaternion.Euler(0f, 18f * Mathf.Sin(Time.time * (1.2f + Lively)), 0f) * headDir;
        float speed = Mathf.Lerp(crawlSpeeds.x, crawlSpeeds.y, Lively) * (fire && flat.magnitude < 1.2f ? 0.2f : 1f);
        Vector3 step = weave * (speed * dt);
        if (Physics.SphereCast(head + Vector3.up * 0.3f, 0.35f * size, step.normalized, out RaycastHit block, step.magnitude + 0.3f, Ground, QueryTriggerInteraction.Ignore)
            && !block.collider.transform.IsChildOf(transform) && block.normal.y < 0.5f)
            step = Vector3.ProjectOnPlane(step, block.normal);
        head += step;
        float groundY = GroundBelow(head);
        head.y = Mathf.Max(groundY + 0.3f * size, Mathf.Lerp(head.y, groundY + 0.3f * size, 1f - Mathf.Exp(-8f * dt)));
        // Rears up towards a bubble floating above its head.
        float reach = fire ? Mathf.Clamp(chasing.transform.position.y - head.y - 0.3f, 0f, 3.4f) * Mathf.Clamp01(2.5f - flat.magnitude / 2f) : 0f;
        rearUp = Mathf.Lerp(rearUp, reach, 1f - Mathf.Exp(-2.5f * dt));
        if ((head - trail[trail.Count - 1]).sqrMagnitude > 0.01f) trail.Add(head);
        if (trail.Count > 200) trail.RemoveRange(0, trail.Count - 200);
    }

    private IEnumerable<Transform> ReinPoints() { yield return beads[1]; yield return beads[3]; }

    private void BeginRest(float seconds)
    {
        resting = true;
        restTimer = seconds;
        chasing = null;
        rearUp = 0f;
        rest = new Vector3(beads[3].position.x, rest.y, beads[3].position.z);
        restYaw = Mathf.Atan2(headDir.x, headDir.z) * Mathf.Rad2Deg;
        nextGroundCheck = 0f;
        nextLook = 0f;
    }

    private void Fly(float dt)
    {
        Vector3 desired;
        float speed;
        float ground = GroundBelow(head);
        if (Ridden && Reined)
        {
            desired = BoneRide.Steer(dt);
            speed = flySpeed;
        }
        else if (LookForFire())
        {
            // Unsteered but lured: it flies after a free fire bubble (one shot from a carried fire source leads it).
            desired = (chasing.transform.position - head).normalized;
            speed = flySpeed * 0.8f;
        }
        else
        {
            // Unsteered: slow loops over the spot where it lay, at hover height.
            loopAngle += dt * 12f;
            Vector3 centre = rest + Vector3.up * hoverHeight * Warmth;
            Vector3 aim = centre + Quaternion.Euler(0f, loopAngle, 0f) * Vector3.forward * 3.5f;
            desired = (aim - head).normalized;
            speed = 2.2f;
        }
        float strength = FlightStrength;
        desired.y = Mathf.Min(desired.y, 0f) + Mathf.Max(desired.y, 0f) * strength;
        desired += Vector3.down * ((1f - strength) * 1.5f);
        speed *= Mathf.Lerp(0.35f, 1f, strength);
        if (desired.sqrMagnitude > 1e-4f)
            headDir = Vector3.RotateTowards(headDir, desired.normalized, 1.6f * dt, 0f).normalized;
        Vector3 step = (Ridden && Reined ? Vector3.ClampMagnitude(desired, 1f)
            : headDir * Mathf.Clamp01(desired.magnitude)) * (speed * dt);
        step.y -= (1f - strength) * 2.5f * dt;
        // Never into rock: stop at it and slide along.
        if (Physics.SphereCast(head, 0.45f, step.normalized, out RaycastHit block, step.magnitude + 0.3f, Ground, QueryTriggerInteraction.Ignore)
            && !block.collider.transform.IsChildOf(transform))
            step = Vector3.ProjectOnPlane(step, block.normal);
        float previousY = head.y;
        head += step;
        float floor = GroundBelow(head) + 1.2f;
        if (head.y < floor) head.y = Mathf.Lerp(head.y, floor, 1f - Mathf.Exp(-6f * dt));
        SpendFlightHeat(dt, Mathf.Max(0f, head.y - previousY));
        if (trail.Count == 0 || (head - trail[trail.Count - 1]).sqrMagnitude > 0.01f) trail.Add(head);
        if (trail.Count > 200) trail.RemoveRange(0, trail.Count - 200);
    }

    /// <summary>Each vertebra eases (a little later than the one before) to its place along the head's trail.</summary>
    private void PlaceAlongTrail(float dt)
    {
        float behind = 0f;
        int t = trail.Count - 1;
        Vector3 previous = head;
        for (int i = 0; i < beads.Count; i++)
        {
            float want = i * spacing;
            Vector3 p = previous;
            while (t > 0 && behind < want)
            {
                float seg = Vector3.Distance(trail[t], trail[t - 1]);
                if (behind + seg >= want) { p = Vector3.Lerp(trail[t], trail[t - 1], (want - behind) / Mathf.Max(1e-4f, seg)); break; }
                behind += seg;
                t--;
                p = trail[t];
            }
            if (t == 0 && behind < want) p = (trail.Count > 0 ? trail[0] : head) - headDir * (want - behind);
            float follow = 1f - Mathf.Exp(-(9f - i * 0.8f) * dt);
            // The front end rears up (only while crawling), easing off towards the middle of the body.
            Vector3 lifted = p + Vector3.up * (rearUp * Mathf.Clamp01(1f - i / 3.5f));
            Vector3 at = Vector3.Lerp(beads[i].position, lifted, follow);
            at.y = Mathf.Max(at.y, GroundBelow(at) + 0.3f * size);
            Vector3 along = i == 0 ? headDir : (beads[i - 1].position - at);
            if (along.sqrMagnitude > 1e-5f)
                beads[i].SetPositionAndRotation(at, Quaternion.Slerp(beads[i].rotation, Quaternion.LookRotation(along.normalized, Vector3.up), follow));
            else beads[i].position = at;
            previous = p;
        }
    }

    /// <summary>The curled pose: the spine round in a C on the ground, the head tucked towards the tail.</summary>
    private void CurledPose(int i, float breath, out Vector3 position, out Quaternion rotation)
    {
        float radius = 0.95f * size;
        float angle = (restYaw + 200f - i * 42f) * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
        position = rest + offset + Vector3.up * (0.28f * size + (lift != null && i < lift.Length ? lift[i] : 0f) + breath * 0.04f * Mathf.Sin(Time.time * 0.8f + i * 0.5f));
        Vector3 tangent = new Vector3(Mathf.Cos(angle), 0f, -Mathf.Sin(angle));
        rotation = Quaternion.LookRotation(tangent, Vector3.up);
    }

    private void PlaceCurled(float blend)
    {
        for (int i = 0; i < beads.Count; i++)
        {
            CurledPose(i, 1f, out Vector3 p, out Quaternion r);
            beads[i].SetPositionAndRotation(Vector3.Lerp(beads[i].position, p, blend), Quaternion.Slerp(beads[i].rotation, r, blend));
        }
    }

    /// <summary>Cold, the ribs fold down along the body. Warm, they beat, each pair a little after the one in front.</summary>
    private void BeatRibs(float dt, bool flying)
    {
        for (int r = 0; r < ribs.Count; r++)
        {
            // Warm: full wingbeats. Stirred up by fire nearby: a quick flutter that grows the closer it is.
            float target = flying ? Mathf.Lerp(-55f, 15f + 40f * Mathf.Sin(Time.time * 4.2f - r * 0.7f), FlightStrength)
                : -55f + Lively * (35f + 18f * Mathf.Sin(Time.time * (6f + 4f * Lively) - r * 0.9f));
            // Each occupied pair cups its own bubble; empty pairs remain free to flap.
            StoredFuel held = storedFuel.Find(fuel => fuel.rib == r);
            float progress = held != null ? Time.time - held.start : -1f;
            bool catching = held != null;
            if (catching) target = progress >= 0f && progress < 0.35f ? 65f : 0f;
            ribBeat[r] = Mathf.Lerp(ribBeat[r], target, 1f - Mathf.Exp(-(catching ? 10f : flying || Lively > 0.05f ? 6f : 1.5f) * dt));
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                ribs[r][s].localRotation = Quaternion.Euler(0f, 0f, ribBeat[r] * side);
            }
        }
    }

    // ---------------- Heat ----------------

    private void SpendFlightHeat(float deltaTime, float climbedMetres)
    {
        float remaining = Mathf.Max(0f, deltaTime) * flightHeatPerSecond + Mathf.Max(0f, climbedMetres) * climbHeatPerMetre;
        foreach (StoredFuel fuel in storedFuel)
        {
            if (fuel.creature is FireBubble fire && !fire.IsDead) remaining -= fire.BurnBoneHeat(remaining);
            if (remaining <= 0f) break;
        }
        PruneFuel();
    }

    /// <summary>A warm creature set down in the ribcage is cradled there; its heat is the child's lift.</summary>
    private void UpdateHeat()
    {
        PruneFuel();
        if (storedFuel.Count < bubbleCapacity && Time.time >= nextHeatCheck)
        {
            // Checked every frame: a fire bubble let go nearby drifts off quickly, and the ribs snatch it as it passes.
            // The head snatches too, when it rears up after a bubble floating overhead.
            for (int part = 0; part < 2 && storedFuel.Count < bubbleCapacity; part++)
            {
                int count = Physics.OverlapSphereNonAlloc(part == 0 ? Chest : Head.position, 1.3f * size, hits, ~0, QueryTriggerInteraction.Collide);
                Collider[] nearby = hits;
                // Bone, flora and terrain colliders can fill the small buffer before the bubble is returned.
                if (count == hits.Length)
                {
                    nearby = Physics.OverlapSphere(part == 0 ? Chest : Head.position, 1.3f * size, ~0, QueryTriggerInteraction.Collide);
                    count = nearby.Length;
                }
                for (int i = 0; i < count; i++)
                {
                    Creature c = nearby[i].GetComponentInParent<Creature>();
                    if (Catch(c)) break;
                }
            }
            // Held still in the ribs it must not count as resting on the ground (a fire bubble would take root).
            if (cradled != null && cradled.GetComponent<Rigidbody>() is Rigidbody held) held.isKinematic = true;
        }
        heat = 0f;
        foreach (StoredFuel fuel in storedFuel) if (fuel.creature != null) heat += Mathf.Max(0f, fuel.creature.Heat);
        UpdateWarmth(Time.deltaTime);
    }

    private void UpdateWarmth(float deltaTime)
    {
        Warmth = Mathf.MoveTowards(Warmth, Mathf.Clamp01(heat / heatForLift), Mathf.Max(0f, deltaTime) * (heat > 0f ? 0.5f : 0.12f));
        UpdateWarmColors();
    }

    private void UpdateWarmColors()
    {
        if (bodyRenderers == null) return;
        warmBlock ??= new MaterialPropertyBlock();
        foreach (Renderer renderer in bodyRenderers)
            ApplyWarmColors(renderer, Warmth, warmBlock);
    }

    internal static void ApplyWarmColors(Renderer renderer, float warmth, MaterialPropertyBlock block)
    {
        if (renderer == null || renderer.sharedMaterial == null) return;
        Material material = renderer.sharedMaterial;
        renderer.GetPropertyBlock(block);
        for (int i = 0; i < WarmProperties.Length; i++)
        {
            string property = WarmProperties[i];
            if (!material.HasProperty(property)) continue;
            Color cool = material.GetColor(property), warm = WarmColors[i];
            warm.a = cool.a; // Keep the skin transparent.
            block.SetColor(property, Color.Lerp(cool, warm, warmth));
        }
        if (material.HasProperty("_GlowStrength"))
            block.SetFloat("_GlowStrength", material.GetFloat("_GlowStrength") + warmth * 1.5f);
        renderer.SetPropertyBlock(block);
    }

    // ---------------- Catching ----------------

    private const float CatchTime = 1.1f;
    private float catchStart = -10f;

    /// <summary>Seconds into the catch (0 .. CatchTime), or -1 when not catching.</summary>
    private float CatchProgress => Time.time - catchStart < CatchTime + 0.6f ? Time.time - catchStart : -1f;

    /// <summary>
    /// A warm creature touching it (thrown at it, or drifting into it) is caught: the ribs fling open, it is drawn
    /// in to the chest, the ribs close round it and the skin swells once, like a breath in. True if it was caught.
    /// </summary>
    public bool Catch(Creature creature)
    {
        if (!CanCatch(creature)) return false;
        return StartCatch(creature);
    }

    private bool CanCatch(Creature creature) =>
        creature != null && storedFuel.Count < Mathf.Clamp(bubbleCapacity, 1, 3) && isActiveAndEnabled && creature.isActiveAndEnabled && !creature.IsDead &&
        !creature.IsCarried && !creature.IsBubbleCaptured && !(creature is TentacleCreature) &&
        // Only small loose bodies: never a fire source or fire flower (fixtures), a web, or anything unportable.
        !(creature is FireSource) && !(creature is FireFlower) && creature.CanBeCarried && creature.GetComponent<Rigidbody>() != null &&
        (creature is FireBubble || creature.Heat > 0.5f) && !IsCradled(creature);

    /// <summary>Catch a throw as it crosses the head/chest's reach, including the visible ribs between colliders.</summary>
    public static bool CatchAlongPath(Creature creature, Vector3 from, Vector3 to)
    {
        BoneChild nearest = null;
        float earliest = float.PositiveInfinity;
        Vector3 step = to - from;
        float lengthSquared = step.sqrMagnitude;
        foreach (BoneChild child in All)
        {
            if (child == null || !child.CanCatch(creature)) continue;
            for (int part = 0; part < 2; part++)
            {
                Vector3 centre = part == 0 ? child.Chest : child.Head.position;
                Vector3 offset = from - centre;
                float radius = 1.3f * child.size;
                float outside = offset.sqrMagnitude - radius * radius;
                float fraction = 0f;
                if (outside > 0f)
                {
                    if (lengthSquared < 1e-8f) continue;
                    float along = Vector3.Dot(offset, step);
                    float discriminant = along * along - lengthSquared * outside;
                    if (discriminant < 0f) continue;
                    fraction = (-along - Mathf.Sqrt(discriminant)) / lengthSquared;
                    if (fraction < 0f || fraction > 1f) continue;
                }
                if (fraction >= earliest) continue;
                Vector3 point = from + step * fraction;
                // A nearby ribcage must not snatch the throw through terrain or another bone creature.
                if (Physics.Linecast(point, centre, out RaycastHit block, Ground, QueryTriggerInteraction.Ignore) &&
                    !block.collider.transform.IsChildOf(child.transform)) continue;
                earliest = fraction;
                nearest = child;
            }
        }
        return nearest != null && nearest.Catch(creature);
    }

    private bool StartCatch(Creature creature)
    {
        if (!CanCatch(creature)) return false;
        int slot = 1;
        if (storedFuel.Exists(fuel => fuel.rib == slot)) slot = 0;
        if (storedFuel.Exists(fuel => fuel.rib == slot)) slot = 2;
        var heldFuel = new StoredFuel { creature = creature, rib = slot, from = creature.transform.position, start = Time.time };
        // Measure while its colliders are still enabled, before the binding takes physics ownership.
        PrepareCradle(heldFuel);
        if (creature is FireBubble bubble && !bubble.BindToBone(this)) { RestoreCradle(heldFuel); return false; }
        storedFuel.Add(heldFuel);
        catchStart = Time.time;
        chasing = null;
        // Held still in the ribs it must not count as resting on the ground (a fire bubble would take root).
        if (creature.GetComponent<Rigidbody>() is Rigidbody held) held.isKinematic = true;
        return true;
    }

    /// <summary>Shape two ribs around the bubble's actual size, leaving it visible between the bones.</summary>
    private void PrepareCradle(StoredFuel heldFuel)
    {
        float radius = 0.35f;
        if (CreatureBounds.TryMeasure(heldFuel.creature.transform, out Bounds bounds))
            radius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
        radius = Mathf.Max(0.1f, radius / size) + 0.06f;
        heldFuel.local = new Vector3(0f, -radius - 0.16f, 0f);
        if (ribs.Count <= heldFuel.rib) return;
        for (int s = 0; s < 2; s++)
        {
            float side = s == 0 ? -1f : 1f;
            Transform hinge = ribs[heldFuel.rib][s];
            var points = new List<Vector3> { Vector3.zero };
            for (int k = 0; k <= 14; k++)
            {
                float t = k / 14f;
                float angle = Mathf.Lerp(50f, -100f, t) * Mathf.Deg2Rad;
                Vector3 point = heldFuel.local + new Vector3(side * Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                points.Add(point - hinge.localPosition);
            }
            for (int layer = 0; layer < 2; layer++)
            {
                Mesh mesh = BoneShapes.Ribbon(points, layer == 0 ? 0.12f : 0.2f, 0.04f,
                    layer == 0 ? 0.055f : 0.025f, Vector3.forward, 0.5f);
                // Mesh vertices are relative to the hinge; the skin originally sits slightly behind it.
                if (layer == 1)
                {
                    Vector3[] vertices = mesh.vertices;
                    for (int v = 0; v < vertices.Length; v++) vertices[v] -= cradleFilters[heldFuel.rib, s, layer].transform.localPosition;
                    mesh.vertices = vertices;
                    mesh.RecalculateBounds();
                }
                heldFuel.meshes.Add(mesh);
                cradleFilters[heldFuel.rib, s, layer].sharedMesh = mesh;
            }
        }
    }

    private void RestoreCradle(StoredFuel fuel)
    {
        for (int s = 0; s < 2; s++)
            for (int layer = 0; layer < 2; layer++)
                if (cradleFilters[fuel.rib, s, layer] != null) cradleFilters[fuel.rib, s, layer].sharedMesh = openRibMeshes[fuel.rib, s, layer];
        foreach (Mesh mesh in fuel.meshes) if (mesh != null) Destroy(mesh);
        fuel.meshes.Clear();
    }

    private void PruneFuel()
    {
        for (int i = storedFuel.Count - 1; i >= 0; i--)
        {
            Creature creature = storedFuel[i].creature;
            if (creature != null && !creature.IsDead && !creature.IsCarried && creature.isActiveAndEnabled) continue;
            RestoreCradle(storedFuel[i]);
            storedFuel.RemoveAt(i);
        }
    }

    private void LateUpdate()
    {
        // The skin swells once as the ribs close round a catch.
        float progress = CatchProgress;
        float swell = progress >= 0f ? Mathf.Sin(Mathf.Clamp01((progress - 0.45f) / 0.9f) * Mathf.PI) * 0.14f : 0f;
        for (int i = 1; i < beads.Count && i < 6; i++) beads[i].localScale = Vector3.one * (size * (1f + swell * (1f - Mathf.Abs(i - 3) * 0.25f)));
        // The cradled creature rides in the ribcage (drawn in from where it touched, at the start of a catch).
        foreach (StoredFuel fuel in storedFuel)
        {
            if (fuel.creature == null) continue;
            progress = Time.time - fuel.start;
            Transform anchor = beads.Count > fuel.rib + 2 ? beads[fuel.rib + 2] : transform;
            Vector3 at = anchor.TransformPoint(fuel.local);
            if (progress >= 0f && progress < CatchTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - 0.15f) / (CatchTime - 0.25f)));
                at = Vector3.Lerp(fuel.from, at, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.4f);
            }
            fuel.creature.transform.position = at;
            var rb = fuel.creature.GetComponent<Rigidbody>();
            if (rb != null) { rb.position = at; if (!rb.isKinematic) rb.linearVelocity = Vector3.zero; }
        }
    }

    private float GroundBelow(Vector3 point)
    {
        float best = float.PositiveInfinity, height = rest.y;
        foreach (RaycastHit hit in Physics.RaycastAll(point + Vector3.up * 3f, Vector3.down, 200f, Ground, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<BoneChild>() != null
                || hit.collider.GetComponentInParent<Creature>() != null || hit.distance >= best) continue;
            best = hit.distance;
            height = hit.point.y;
        }
        return height;
    }

    /// <summary>For tests: put a creature straight into the ribcage.</summary>
    public void Cradle(Creature creature) => Catch(creature);

    /// <summary>For tests: the creature held in its ribs, if any.</summary>
    public Creature Cradled => cradled;
}

/// <summary>
/// Riding a bone child or the colossus: the player sits on its back (moved with it, the view still free), and
/// a carried tentacle creature sends two arms into its spine as reins.
/// </summary>
public static class BoneRide
{
    /// <summary>What is being ridden (a BoneChild or a BoneKiln), or null.</summary>
    public static object Mount { get; private set; }
    public static Transform MountRoot { get; private set; }
    /// <summary>For automated checks: x strafes, y moves forward/back; null reads A/D and Space/Ctrl.</summary>
    public static Vector2? ScriptedMove;
    public static float ScriptedRise;
    public static bool ScriptedToggle;
    private static int toggledFrame = -1;
    private static SmoothFirstPersonController controller;
    private static Rigidbody body;
    private static bool wasKinematic;
    private static Vector3 heading;
    private static bool wasRestricted, waitForMountRelease;
    private static Vector2 moveInput;
    private static float riseInput;
    private static Vector3 seatVelocity, lastSeat;
    private static bool hasSeatSample;
    private static Component approachMount;
    private static Transform approachSeat, approachRoot;
    private static PlayerCreatureCarrier approachCarrier;
    private static bool approachKinematic, approachRestricted, landing, approachNeedsTentacle;
    private static float approachSpeed, approachTime, blockedTime;
    private static BoneKiln nearbyMother;
    private static readonly RaycastHit[] boardingHits = new RaycastHit[64];
    private static readonly Collider[] boardingOverlaps = new Collider[64];
    public static bool Approaching => approachSeat != null;
    public static bool Busy => Mount != null || approachCarrier != null;

    public static bool LeftReinHeld => moveInput.x < -0.05f || Mathf.Abs(moveInput.y) > 0.05f;
    public static bool RightReinHeld => moveInput.x > 0.05f || Mathf.Abs(moveInput.y) > 0.05f;

    /// <summary>W/S controls altitude, A/D strafes, Space/Ctrl moves forward/back relative to view.</summary>
    public static Vector3 Steer(float deltaTime)
    {
        Keyboard keys = Keyboard.current;
        Vector2 input = ScriptedMove ?? Vector2.zero;
        float rise = ScriptedRise;
        if ((Application.isFocused || Application.isBatchMode) && keys != null)
        {
            if (ScriptedMove == null) input = new Vector2((keys.dKey.isPressed ? 1f : 0f) - (keys.aKey.isPressed ? 1f : 0f),
                (keys.spaceKey.isPressed ? 1f : 0f) - (keys.leftCtrlKey.isPressed || keys.rightCtrlKey.isPressed ? 1f : 0f));
            rise += (keys.wKey.isPressed ? 1f : 0f) - (keys.sKey.isPressed ? 1f : 0f);
        }
        moveInput = Vector2.MoveTowards(moveInput, Vector2.ClampMagnitude(input, 1f), 8f * deltaTime);
        riseInput = Mathf.MoveTowards(riseInput, Mathf.Clamp(rise, -1f, 1f), 8f * deltaTime);
        Camera view = controller != null && controller.PlayerCamera != null ? controller.PlayerCamera : Camera.main;
        Vector3 forward = view != null ? Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized : heading;
        if (forward.sqrMagnitude < 0.001f) forward = heading;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        return Vector3.ClampMagnitude(forward * moveInput.y + right * moveInput.x + Vector3.up * riseInput * 0.8f, 1f);
    }

    /// <summary>One getting on or off per frame (so the same key press does not do both).</summary>
    public static bool CanToggle => !BoneAwakeningCutscene.Playing && toggledFrame != Time.frameCount;

    public static bool PressedC()
    {
        if (ScriptedToggle) { ScriptedToggle = false; return true; }
        return Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
    }

    /// <summary>
    /// Mount input is resolved before the carrier can interpret RMB as dropping its creature.
    /// Left click near a mount gets on. Right click gets off; so does left click when the hand holds nothing
    /// with its own left-click use (a tentacle aims, a fire source shoots).
    /// </summary>
    public static bool HandleInput(PlayerCreatureCarrier carrier)
    {
        Mouse mouse = Mouse.current;
        bool rightPress = mouse != null && mouse.rightButton.wasPressedThisFrame;
        bool leftPress = mouse != null && mouse.leftButton.wasPressedThisFrame;
        Creature held = carrier != null ? carrier.CarriedCreature : null;
        bool leftIsBusy = held != null && (held is TentacleCreature || held.CanUseWhileCarried);
        if (approachCarrier != null)
        {
            if (rightPress || (Keyboard.current != null && (Keyboard.current.cKey.wasPressedThisFrame || Keyboard.current.rKey.wasPressedThisFrame)))
                CancelApproach();
            return true;
        }
        if (Mount != null)
        {
            if (Mount is Component mounted && (mounted == null || !mounted.gameObject.activeInHierarchy))
            { GetOff(); return true; }
            bool canDismount = !waitForMountRelease;
            if (mouse == null || (!mouse.rightButton.isPressed && !mouse.leftButton.isPressed)) waitForMountRelease = false;
            if (CanToggle && canDismount && (rightPress || (leftPress && !leftIsBusy) || ScriptedToggle)) { ScriptedToggle = false; GetOff(true); }
            return true;
        }
        if (toggledFrame == Time.frameCount) return true;
        if (!CanToggle || (!leftPress && !ScriptedToggle) || (!Application.isFocused && !Application.isBatchMode)) return false;
        if (!TryNearby(out Component mount, out Transform seat)) return false;
        ScriptedToggle = false;
        return BeginApproach(mount, seat, carrier);
    }

    public static bool TryNearby(out Component mount, out Transform seat)
    {
        mount = null; seat = null;
        if (Busy || BoneAwakeningCutscene.Playing || !FindPlayer()) return false;
        float nearest = float.PositiveInfinity;
        if (nearbyMother == null) nearbyMother = Object.FindFirstObjectByType<BoneKiln>();
        if (nearbyMother != null && nearbyMother.TryGetRideSeat(out Transform motherSeat))
        { mount = nearbyMother; seat = motherSeat; nearest = PlayerDistance(seat.position); }
        foreach (BoneChild child in BoneChild.All)
            if (child != null && child.TryGetRideSeat(out Transform childSeat) && Near(child.RidePoint, 2f + child.size, child.transform))
            {
                float distance = PlayerDistance(childSeat.position);
                if (distance < nearest) { mount = child; seat = childSeat; nearest = distance; }
            }
        return mount != null;
    }

    public static bool TryGrappleTarget(Transform anchor, Vector3 hit, out Component mount, out Transform seat)
    {
        mount = null; seat = null;
        if (anchor == null || approachCarrier != null || BoneAwakeningCutscene.Playing) return false;
        BoneChild child = anchor.GetComponentInParent<BoneChild>();
        if (child != null && Mount != (object)child && child.TryGetRideSeat(out seat)) { mount = child; return true; }
        BoneKiln mother = anchor.GetComponentInParent<BoneKiln>();
        if (mother != null && Mount != (object)mother && mother.TryGrappleSeat(hit, out seat)) { mount = mother; return true; }
        return false;
    }

    private static Vector3 RiderPosition(Component mount, Transform seat) => mount is BoneChild child
        ? child.RiderPosition : ((BoneKiln)mount).RiderPosition(seat);

    /// <summary>One short controlled pull; free look stays enabled, and all motion is swept against scenery.</summary>
    public static bool BeginApproach(Component mount, Transform seat, PlayerCreatureCarrier carrier, bool needsTentacle = false, bool transfer = false)
    {
        if (Busy || mount == null || seat == null || carrier == null || !FindPlayer() || (controller.BubbleMovementBlocked && !transfer)
            || BoneAwakeningCutscene.Playing || (RestSpotSystem.Instance != null && RestSpotSystem.Instance.Sitting)
            || !controller.enabled || (!CanToggle && !transfer)) return false;
        if (!(mount is BoneChild) && !(mount is BoneKiln)) return false;
        Transform root = mount is BoneKiln mother ? mother.RideRoot : mount.transform;
        if (root == null) return false;
        Vector3 goal = RiderPosition(mount, seat);
        if (Vector3.Distance(body.position, goal) > 34f) return false;
        float incomingSpeed = !body.isKinematic ? Mathf.Clamp(Vector3.Dot(body.linearVelocity, (goal - body.position).normalized), 0f, 12f) : 0f;
        controller.GetComponent<AdvancedFirstPersonTraversal>()?.StopForBubbleCapture();
        foreach (HandClimber climber in Object.FindObjectsByType<HandClimber>(FindObjectsSortMode.None))
            if (climber.Controls(controller)) climber.StopForBubbleCapture();
        approachKinematic = body.isKinematic; approachRestricted = controller.Restricted;
        if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        body.isKinematic = true; controller.SetRestricted(true);
        approachMount = mount; approachSeat = seat; approachRoot = root; approachCarrier = carrier;
        approachNeedsTentacle = needsTentacle;
        approachSpeed = incomingSpeed; approachTime = blockedTime = 0f;
        landing = Vector3.Distance(body.position, goal) < 3f;
        toggledFrame = Time.frameCount;
        waitForMountRelease = true;
        return true;
    }

    private static bool BoardingObstacle(Collider collider) => collider != null && !collider.isTrigger
        && collider.attachedRigidbody != body && !collider.transform.IsChildOf(body.transform)
        && !collider.transform.IsChildOf(approachRoot)
        && (approachCarrier.CarriedCreature == null || collider.GetComponentInParent<Creature>() != approachCarrier.CarriedCreature);

    private static Vector3 SweepBoarding(Vector3 from, Vector3 to)
    {
        Collider capsule = body.GetComponent<Collider>();
        Vector3 centre = from, extents = new Vector3(0.35f, 0.95f, 0.35f);
        if (capsule != null)
        { centre += capsule.bounds.center - body.transform.position; extents = capsule.bounds.extents; }
        float radius = Mathf.Max(0.1f, Mathf.Min(extents.x, extents.z) - 0.025f);
        Vector3 half = Vector3.up * Mathf.Max(0f, extents.y - 0.025f - radius);
        Vector3 delta = to - from;
        float distance = delta.magnitude;
        if (distance < 0.00001f) return from;
        int count = Physics.CapsuleCastNonAlloc(centre + half, centre - half, radius, delta / distance,
            boardingHits, distance + 0.03f, ~0, QueryTriggerInteraction.Ignore);
        if (count == boardingHits.Length) return from;
        float travel = distance;
        for (int i = 0; i < count; i++)
            if (BoardingObstacle(boardingHits[i].collider)) travel = Mathf.Min(travel, Mathf.Max(0f, boardingHits[i].distance - 0.03f));
        Vector3 step = delta / distance * travel;
        count = Physics.OverlapCapsuleNonAlloc(centre + half + step, centre - half + step, radius,
            boardingOverlaps, ~0, QueryTriggerInteraction.Ignore);
        if (count == boardingOverlaps.Length) return from;
        for (int i = 0; i < count; i++) if (BoardingObstacle(boardingOverlaps[i])) return from;
        return from + step;
    }

    public static void StepApproach(float dt)
    {
        if (approachCarrier == null || dt <= 0f) return;
        if (approachMount == null || approachSeat == null || !approachMount.gameObject.activeInHierarchy
            || (approachMount is Behaviour behaviour && !behaviour.isActiveAndEnabled) || !FindPlayer()
            || (approachNeedsTentacle && (!approachCarrier.CarriesTentacle || !approachCarrier.CarriedCreature.isActiveAndEnabled))
            || (approachMount is BoneKiln approachingMother && !approachingMother.TryGrappleSeat(approachSeat.position, out _))
            || BoneAwakeningCutscene.Playing || (controller.GetComponent<BubbleCapture>() is BubbleCapture capture && capture.BlocksPlayerMovement) || !controller.enabled)
        { CancelApproach(); return; }
        approachTime += dt;
        if (approachTime > 6f) { CancelApproach(); return; }
        Vector3 seat = RiderPosition(approachMount, approachSeat);
        Vector3 goal = seat + (landing ? Vector3.zero : Vector3.up * 1.2f);
        if (!landing && Vector3.Distance(body.position, goal) < 0.6f) { landing = true; goal = seat; }
        float distance = Vector3.Distance(body.position, goal);
        approachSpeed = Mathf.MoveTowards(approachSpeed, Mathf.Min(12f, distance * 5f), 28f * dt);
        Vector3 next = SweepBoarding(body.position, Vector3.MoveTowards(body.position, goal, approachSpeed * dt));
        blockedTime = (next - body.position).sqrMagnitude < 0.00001f && distance > 0.15f ? blockedTime + dt : 0f;
        if (blockedTime > 0.3f) { CancelApproach(); return; }
        body.position = next; body.transform.position = next;
        controller.SuppressJumpForCurrentFrame();
        if (landing && Vector3.Distance(next, seat) < 0.12f)
        {
            Component mount = approachMount; Transform mountSeat = approachSeat;
            EndApproach();
            if (mount is BoneChild child) child.MountAt(mountSeat);
            else if (mount is BoneKiln mother) mother.MountAt(mountSeat);
            if (Mount == null) TentacleTether.Instance?.LetGoAll();
        }
    }

    private static void EndApproach()
    {
        approachMount = null; approachSeat = approachRoot = null; approachCarrier = null;
        if (body != null) body.isKinematic = approachKinematic;
        if (controller != null) controller.SetRestricted(approachRestricted);
        toggledFrame = Time.frameCount;
    }

    public static void CancelApproach()
    {
        if (approachCarrier == null) return;
        EndApproach();
        TentacleTether.Instance?.LetGoAll();
    }

    /// <summary>True when the player stands within radius of a visible seat.</summary>
    public static bool Near(Vector3 point, float radius, Transform self)
    {
        if (!FindPlayer()) return false;
        float d = Vector3.Distance(body.position, point);
        if (d > radius) return false;
        foreach (RaycastHit hit in Physics.RaycastAll(body.position, (point - body.position).normalized, d, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.collider.transform.IsChildOf(self) && hit.collider.GetComponentInParent<SmoothFirstPersonController>() == null
                && hit.collider.GetComponentInParent<Creature>() == null) return false;
        return true;
    }

    public static float PlayerDistance(Vector3 point) => FindPlayer() ? Vector3.Distance(body.position, point) : float.PositiveInfinity;

    /// <summary>How many arms of the carried tentacle creature are stuck into root.</summary>
    public static int Reins(Transform root)
    {
        var carrier = Object.FindFirstObjectByType<PlayerCreatureCarrier>();
        return carrier != null && carrier.CarriedCreature is TentacleCreature t ? t.PinnedInto(root) : 0;
    }

    public static void GetOn(object mount, Transform root, Transform seat, IEnumerable<Transform> reinPoints)
    {
        if (!FindPlayer() || Mount != null) return;
        // Release traversal ownership before the mount takes over the player's body.
        controller.GetComponent<AdvancedFirstPersonTraversal>()?.StopForBubbleCapture();
        foreach (HandClimber climber in Object.FindObjectsByType<HandClimber>(FindObjectsSortMode.None))
            if (climber.Controls(controller)) climber.StopForBubbleCapture();
        toggledFrame = Time.frameCount;
        Mount = mount;
        MountRoot = root;
        heading = Vector3.ProjectOnPlane(mount is BoneKiln mother ? mother.RideForward : seat.forward, Vector3.up).normalized;
        if (heading.sqrMagnitude < 1e-4f) heading = Vector3.forward;
        wasKinematic = body.isKinematic;
        wasRestricted = controller.Restricted;
        moveInput = Vector2.zero; riseInput = 0f;
        hasSeatSample = false; seatVelocity = Vector3.zero;
        if (!body.isKinematic) body.linearVelocity = Vector3.zero;
        body.isKinematic = true;
        controller.SetRestricted(true);
        // With a tentacle creature on the back, two of its arms whip into the spine: the reins.
        TentacleTether tether = TentacleTether.Instance;
        if (tether != null && Reins(root) < 2)
            foreach (Transform point in reinPoints)
                if (Reins(root) < 2) tether.FireAt(point.position, point);
    }

    public static void GetOff(bool keepMomentum = false)
    {
        if (Mount == null || !FindPlayer()) return;
        toggledFrame = Time.frameCount;
        Mount = null;
        MountRoot = null;
        ScriptedMove = null;
        ScriptedRise = 0f;
        moveInput = Vector2.zero; riseInput = 0f;
        body.position += Vector3.up * 0.6f;
        body.transform.position = body.position;
        body.isKinematic = wasKinematic;
        controller.SetRestricted(wasRestricted);
        if (keepMomentum && !body.isKinematic)
        {
            body.linearVelocity = seatVelocity;
            controller.Fling(seatVelocity, 0.35f);
        }
        // The reins let go.
        TentacleTether.Instance?.LetGoAll();
    }

    /// <summary>Keeps the rider sitting on the given point (called by the mount every frame).</summary>
    public static void Seat(Vector3 point)
    {
        if (!FindPlayer()) return;
        Vector3 at = point + Vector3.up * 1f;
        if (hasSeatSample && Time.deltaTime > 0f) seatVelocity = Vector3.ClampMagnitude((at - lastSeat) / Time.deltaTime, 18f);
        lastSeat = at; hasSeatSample = true;
        body.position = at;
        body.transform.position = at;
        controller.SuppressJumpForCurrentFrame();
    }

    private static bool FindPlayer()
    {
        if (controller == null)
        {
            controller = Object.FindFirstObjectByType<SmoothFirstPersonController>();
            body = controller != null ? controller.GetComponent<Rigidbody>() : null;
        }
        return body != null;
    }
}
