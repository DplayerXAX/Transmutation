using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// One of the colossus's young: a short spine of vertebrae with three pairs of ribs and a small skull, every
/// bone wrapped in a glassy skin. It crawls in slow loops round its mother, now and then curling up to rest.
///   - Habits that hint at what fire does to it: it notices fire bubbles drifting nearby and goes after them,
///     its ribs fluttering faster the closer one is, rearing its head up to reach one floating just above it.
///   - Heat: two middle ribs embrace a caught fire bubble, which stays bound and keeps its fire alive.
///     The body warms to amber; the other ribs beat like wings and it rises and drifts in slow loops.
///   - Riding: [C] climbs on its back (and off again).
///   - Reins: with a tentacle creature on the back, climbing on sends two of its arms into the child's spine
///     (or shoot them in yourself). With reins it flies forward: hold left/right mouse to turn left/right,
///     W to climb and S to sink. Without reins it ignores the rider; without heat it cannot fly at all.
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

    public float Warmth { get; private set; }
    public bool Ridden => BoneRide.Mount == (object)this;
    public bool Reined => BoneRide.Reins(transform) >= 2;
    public Transform Head => beads.Count > 0 ? beads[0] : transform;
    public Vector3 Chest => beads.Count > 3 ? beads[3].position : transform.position;

    private readonly List<Transform> beads = new List<Transform>();      // one per vertebra (bone + skin), head first
    private readonly List<Transform[]> ribs = new List<Transform[]>();   // [left, right] on vertebrae 2..4
    private readonly List<float> ribBeat = new List<float>();
    private readonly List<Vector3> trail = new List<Vector3>();
    private Vector3 head, headDir, rest;
    private float restYaw, loopAngle;
    private Creature cradled;
    private readonly MeshFilter[,] cradleFilters = new MeshFilter[2, 2]; // side, bone/skin
    private readonly Mesh[,] openRibMeshes = new Mesh[2, 2];
    private readonly List<Mesh> cradleMeshes = new List<Mesh>();
    private Vector3 cradleLocal;
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
        foreach (BoneChild c in All) if (c != null && c.cradled == creature) return true;
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
                    if (i == 3)
                    {
                        cradleFilters[s, 0] = ribBone;
                        cradleFilters[s, 1] = ribSkin;
                        openRibMeshes[s, 0] = ribBone.sharedMesh;
                        openRibMeshes[s, 1] = ribSkin.sharedMesh;
                    }
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
        foreach (Mesh mesh in cradleMeshes) if (mesh != null) Destroy(mesh);
    }

    // ---------------- Update ----------------

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || beads.Count == 0) return;
        UpdateHeat();
        bool flying = Warmth > 0.35f;
        if (flying) { resting = false; crawlTimer = Mathf.Max(crawlTimer, 8f); rearUp = 0f; }
        else if (resting && chasing == null) Lively = Mathf.MoveTowards(Lively, 0f, dt);
        if (Ridden) { if (BoneRide.CanToggle && BoneRide.PressedC()) BoneRide.GetOff(); }
        else if (BoneRide.Mount == null && BoneRide.CanToggle && BoneRide.Near(beads[2].position, 2f + size, transform) && BoneRide.PressedC())
            BoneRide.GetOn(this, transform, beads[2], ReinPoints());

        if (flying)
        {
            Fly(dt);
            PlaceAlongTrail(dt);
        }
        else if (!resting || LookForFire())
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
                if (Physics.Raycast(rest + Vector3.up * 30f, Vector3.down, out RaycastHit floor, 80f, Ground, QueryTriggerInteraction.Ignore)
                    && !floor.collider.transform.IsChildOf(transform))
                    rest.y = floor.point.y;
                // Each vertebra lies on the ground under it, on a slope too.
                for (int i = 0; i < beads.Count; i++)
                {
                    CurledPose(i, 0f, out Vector3 p, out _);
                    lift[i] = Physics.Raycast(new Vector3(p.x, rest.y + 30f, p.z), Vector3.down, out RaycastHit under, 80f, Ground, QueryTriggerInteraction.Ignore)
                        && !under.collider.transform.IsChildOf(transform) ? Mathf.Max(0f, under.point.y - rest.y) : 0f;
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
        if (trail.Count == 0) trail.Add(head);
        bool fire = LookForFire();
        float distance = fire ? Vector3.Distance(chasing.transform.position, beads[0].position) : float.MaxValue;
        Lively = Mathf.MoveTowards(Lively, fire ? Mathf.Clamp01(1.3f - distance / noticeRange) : 0f, dt * 0.8f);
        Vector3 target;
        if (fire)
        {
            target = chasing.transform.position;
            resting = false;
            crawlTimer = Mathf.Max(crawlTimer, 6f);
        }
        else
        {
            // A point a little further round the circle about its mother.
            Vector3 from = head - mother;
            from.y = 0f;
            float angle = Mathf.Atan2(from.x, from.z) * Mathf.Rad2Deg + 25f * orbitSense;
            target = mother + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * orbitRadius;
            crawlTimer -= dt;
            if (crawlTimer <= 0f)
            {
                // Settles down to rest where it is.
                resting = true;
                restTimer = Random.Range(6f, 12f);
                rest = new Vector3(beads[3].position.x, rest.y, beads[3].position.z);
                restYaw = Mathf.Atan2(headDir.x, headDir.z) * Mathf.Rad2Deg;
                nextGroundCheck = 0f;
            }
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
        if (Physics.Raycast(head + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 20f, Ground, QueryTriggerInteraction.Ignore) && !ground.collider.transform.IsChildOf(transform))
            head.y = Mathf.Lerp(head.y, ground.point.y + 0.3f * size, 1f - Mathf.Exp(-8f * dt));
        // Rears up towards a bubble floating above its head.
        float reach = fire ? Mathf.Clamp(chasing.transform.position.y - head.y - 0.3f, 0f, 3.4f) * Mathf.Clamp01(2.5f - flat.magnitude / 2f) : 0f;
        rearUp = Mathf.Lerp(rearUp, reach, 1f - Mathf.Exp(-2.5f * dt));
        if ((head - trail[trail.Count - 1]).sqrMagnitude > 0.01f) trail.Add(head);
        if (trail.Count > 200) trail.RemoveRange(0, trail.Count - 200);
    }

    private IEnumerable<Transform> ReinPoints() { yield return beads[1]; yield return beads[3]; }

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
        else
        {
            // Unsteered: slow loops over the spot where it lay, at hover height.
            loopAngle += dt * 12f;
            Vector3 centre = rest + Vector3.up * hoverHeight * Warmth;
            Vector3 aim = centre + Quaternion.Euler(0f, loopAngle, 0f) * Vector3.forward * 3.5f;
            desired = (aim - head).normalized;
            speed = 2.2f;
        }
        // Lift fades with the heat; too little heat and it sinks.
        if (Warmth < 0.6f) desired += Vector3.down * (0.6f - Warmth) * 2f;
        if (desired.sqrMagnitude < 1e-4f) desired = headDir * 0.01f;
        headDir = Vector3.RotateTowards(headDir, desired.normalized, 1.6f * dt, 0f).normalized;
        Vector3 step = headDir * (speed * Mathf.Clamp01(desired.magnitude) * dt);
        // Never into rock: stop at it and slide along.
        if (Physics.SphereCast(head, 0.45f, step.normalized, out RaycastHit block, step.magnitude + 0.3f, Ground, QueryTriggerInteraction.Ignore)
            && !block.collider.transform.IsChildOf(transform))
            step = Vector3.ProjectOnPlane(step, block.normal);
        head += step;
        float floor = GroundBelow(head) + 1.2f;
        if (head.y < floor) head.y = Mathf.Lerp(head.y, floor, 1f - Mathf.Exp(-6f * dt));
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
            float target = flying ? 15f + 40f * Mathf.Sin(Time.time * 4.2f - r * 0.7f)
                : -55f + Lively * (35f + 18f * Mathf.Sin(Time.time * (6f + 4f * Lively) - r * 0.9f));
            // The middle pair cups the bubble permanently; the other pairs remain free to flap.
            float progress = CatchProgress;
            bool catching = cradled != null && r == 1;
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

    /// <summary>A warm creature set down in the ribcage is cradled there; its heat is the child's lift.</summary>
    private void UpdateHeat()
    {
        if (cradled != null && (cradled.IsCarried || !cradled.isActiveAndEnabled)) cradled = null;
        if (cradled == null && cradleMeshes.Count > 0) RestoreRibs();
        if (cradled == null && Time.time >= nextHeatCheck)
        {
            // Checked every frame: a fire bubble let go nearby drifts off quickly, and the ribs snatch it as it passes.
            // The head snatches too, when it rears up after a bubble floating overhead.
            for (int part = 0; part < 2 && cradled == null; part++)
            {
                int count = Physics.OverlapSphereNonAlloc(part == 0 ? Chest : beads[0].position, 1.3f * size, hits, ~0, QueryTriggerInteraction.Collide);
                Collider[] nearby = hits;
                // Bone, flora and terrain colliders can fill the small buffer before the bubble is returned.
                if (count == hits.Length)
                {
                    nearby = Physics.OverlapSphere(part == 0 ? Chest : beads[0].position, 1.3f * size, ~0, QueryTriggerInteraction.Collide);
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
        heat = cradled != null ? Mathf.Max(0f, cradled.Heat) : 0f;
        // Fire bubbles are fire even when their shared Heat value is zero (as on the authored prefab).
        // A bound bubble is a lasting source of lift, rather than a one-off Heat transfer.
        if (cradled is FireBubble) heat = Mathf.Max(heat, heatForLift);
        Warmth = Mathf.MoveTowards(Warmth, Mathf.Clamp01(heat / heatForLift), Time.deltaTime * (heat > 0f ? 0.5f : 0.12f));
        UpdateWarmColors();
    }

    private void UpdateWarmColors()
    {
        if (bodyRenderers == null) return;
        warmBlock ??= new MaterialPropertyBlock();
        foreach (Renderer renderer in bodyRenderers)
        {
            if (renderer == null || renderer.sharedMaterial == null) continue;
            Material material = renderer.sharedMaterial;
            renderer.GetPropertyBlock(warmBlock);
            for (int i = 0; i < WarmProperties.Length; i++)
            {
                string property = WarmProperties[i];
                if (!material.HasProperty(property)) continue;
                Color cool = material.GetColor(property), warm = WarmColors[i];
                warm.a = cool.a; // Keep the skin transparent.
                warmBlock.SetColor(property, Color.Lerp(cool, warm, Warmth));
            }
            if (material.HasProperty("_GlowStrength"))
                warmBlock.SetFloat("_GlowStrength", material.GetFloat("_GlowStrength") + Warmth * 1.5f);
            renderer.SetPropertyBlock(warmBlock);
        }
    }

    // ---------------- Catching ----------------

    private const float CatchTime = 1.1f;
    private float catchStart = -10f;
    private Vector3 catchFrom;

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
        creature != null && cradled == null && isActiveAndEnabled && creature.isActiveAndEnabled && !creature.IsDead &&
        !creature.IsCarried && !creature.IsBubbleCaptured && !(creature is TentacleCreature) &&
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
        if (creature is FireBubble alreadyBound && alreadyBound.IsBoundToBone) return false;
        // Measure while its colliders are still enabled, before the binding takes physics ownership.
        PrepareCradle(creature);
        if (creature is FireBubble bubble && !bubble.BindToBone(this)) { RestoreRibs(); return false; }
        cradled = creature;
        catchStart = Time.time;
        catchFrom = creature.transform.position;
        chasing = null;
        // Held still in the ribs it must not count as resting on the ground (a fire bubble would take root).
        if (creature.GetComponent<Rigidbody>() is Rigidbody held) held.isKinematic = true;
        return true;
    }

    /// <summary>Shape two ribs around the bubble's actual size, leaving it visible between the bones.</summary>
    private void PrepareCradle(Creature creature)
    {
        if (ribs.Count < 2) return;
        RestoreRibs();
        float radius = 0.35f;
        if (CreatureBounds.TryMeasure(creature.transform, out Bounds bounds))
            radius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
        radius = Mathf.Max(0.1f, radius / size) + 0.06f;
        cradleLocal = new Vector3(0f, -radius - 0.16f, 0f);
        for (int s = 0; s < 2; s++)
        {
            float side = s == 0 ? -1f : 1f;
            Transform hinge = ribs[1][s];
            var points = new List<Vector3> { Vector3.zero };
            for (int k = 0; k <= 14; k++)
            {
                float t = k / 14f;
                float angle = Mathf.Lerp(50f, -100f, t) * Mathf.Deg2Rad;
                Vector3 point = cradleLocal + new Vector3(side * Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
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
                    for (int v = 0; v < vertices.Length; v++) vertices[v] -= cradleFilters[s, layer].transform.localPosition;
                    mesh.vertices = vertices;
                    mesh.RecalculateBounds();
                }
                cradleMeshes.Add(mesh);
                cradleFilters[s, layer].sharedMesh = mesh;
            }
        }
    }

    private void RestoreRibs()
    {
        for (int s = 0; s < 2; s++)
            for (int layer = 0; layer < 2; layer++)
                if (cradleFilters[s, layer] != null) cradleFilters[s, layer].sharedMesh = openRibMeshes[s, layer];
        foreach (Mesh mesh in cradleMeshes) if (mesh != null) Destroy(mesh);
        cradleMeshes.Clear();
    }

    private void LateUpdate()
    {
        // The skin swells once as the ribs close round a catch.
        float progress = CatchProgress;
        float swell = progress >= 0f ? Mathf.Sin(Mathf.Clamp01((progress - 0.45f) / 0.9f) * Mathf.PI) * 0.14f : 0f;
        for (int i = 1; i < beads.Count && i < 6; i++) beads[i].localScale = Vector3.one * (size * (1f + swell * (1f - Mathf.Abs(i - 3) * 0.25f)));
        // The cradled creature rides in the ribcage (drawn in from where it touched, at the start of a catch).
        if (cradled == null) return;
        Vector3 at = beads[3].TransformPoint(cradleLocal);
        if (progress >= 0f && progress < CatchTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - 0.15f) / (CatchTime - 0.25f)));
            at = Vector3.Lerp(catchFrom, at, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.4f);
        }
        cradled.transform.position = at;
        var rb = cradled.GetComponent<Rigidbody>();
        if (rb != null) { rb.position = at; if (!rb.isKinematic) rb.linearVelocity = Vector3.zero; }
    }

    private float GroundBelow(Vector3 point)
    {
        return Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 200f, Ground, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform)
            ? hit.point.y : point.y - 50f;
    }

    /// <summary>For tests: put a creature straight into the ribcage.</summary>
    public void Cradle(Creature creature) => StartCatch(creature);

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
    /// <summary>For automated checks: -1 pulls left, +1 pulls right; null reads the mouse.</summary>
    public static float? ScriptedTurn;
    public static float ScriptedRise;
    public static bool ScriptedToggle;
    private static int toggledFrame = -1;
    private static SmoothFirstPersonController controller;
    private static Rigidbody body;
    private static bool wasKinematic;
    private static Vector3 heading;

    public static bool LeftReinHeld => Application.isFocused && Mouse.current != null && Mouse.current.leftButton.isPressed;
    public static bool RightReinHeld => Application.isFocused && Mouse.current != null && Mouse.current.rightButton.isPressed;

    /// <summary>Forward flight with independent rein steering and W/S altitude; looking remains free.</summary>
    public static Vector3 Steer(float deltaTime)
    {
        float turn = ScriptedTurn ?? ((RightReinHeld ? 1f : 0f) - (LeftReinHeld ? 1f : 0f));
        heading = Quaternion.AngleAxis(Mathf.Clamp(turn, -1f, 1f) * 45f * deltaTime, Vector3.up) * heading;
        Keyboard keys = Keyboard.current;
        float rise = ScriptedRise;
        if (Application.isFocused && keys != null)
            rise += (keys.wKey.isPressed ? 1f : 0f) - (keys.sKey.isPressed ? 1f : 0f);
        return (heading + Vector3.up * Mathf.Clamp(rise, -1f, 1f) * 0.8f).normalized;
    }

    /// <summary>One getting on or off per frame (so the same key press does not do both).</summary>
    public static bool CanToggle => toggledFrame != Time.frameCount;

    public static bool PressedC()
    {
        if (ScriptedToggle) { ScriptedToggle = false; return true; }
        return Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame;
    }

    /// <summary>True when the player stands within radius of point and no resting place is closer (C sits there).</summary>
    public static bool Near(Vector3 point, float radius, Transform self)
    {
        if (!FindPlayer()) return false;
        float d = Vector3.Distance(body.position, point);
        if (d > radius) return false;
        if (RestSpotSystem.Instance != null)
            foreach (RestSpot spot in RestSpotSystem.Instance.Spots)
                if (spot != null && Vector3.Distance(spot.Seat, body.position) < Mathf.Min(d, 2.6f)) return false;
        return true;
    }

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
        heading = Vector3.ProjectOnPlane(seat.forward, Vector3.up).normalized;
        if (heading.sqrMagnitude < 1e-4f) heading = Vector3.forward;
        wasKinematic = body.isKinematic;
        body.linearVelocity = Vector3.zero;
        body.isKinematic = true;
        controller.SetRestricted(true);
        // With a tentacle creature on the back, two of its arms whip into the spine: the reins.
        TentacleTether tether = TentacleTether.Instance;
        if (tether != null && Reins(root) < 2)
            foreach (Transform point in reinPoints)
                if (Reins(root) < 2) tether.FireAt(point.position, point);
    }

    public static void GetOff()
    {
        if (Mount == null || !FindPlayer()) return;
        toggledFrame = Time.frameCount;
        Mount = null;
        MountRoot = null;
        ScriptedTurn = null;
        ScriptedRise = 0f;
        body.position += Vector3.up * 0.6f;
        body.isKinematic = wasKinematic;
        controller.SetRestricted(false);
        // The reins let go.
        TentacleTether.Instance?.LetGoAll();
    }

    /// <summary>Keeps the rider sitting on the given point (called by the mount every frame).</summary>
    public static void Seat(Vector3 point)
    {
        if (!FindPlayer()) return;
        Vector3 at = point + Vector3.up * 1f;
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
