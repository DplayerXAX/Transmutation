using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A long torn scar in the ground through the bone mother's hole, as if the earth split when she dived in.
/// The trench itself is carved into the saved terrain by Tools > Capstone > Landmarks > Bone Mother > Carve Ground Scar
/// (Editor/BoneScarSetup.cs), which also fills in the line, widths and depths below. In Play mode this component
/// dresses it: an ember seam glowing along the floor, inner-world growths on its walls and down the shaft, scar tissue
/// along the lips, and vents deep in the crack that let fire bubbles float up out of it (in place of her surface nests).
/// Sits on the BoneKiln object. BoneKiln asks it where the scar is so she stays where she was before it was carved.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class BoneScar : MonoBehaviour
{
    [Header("Dressing (Play mode)")]
    [Tooltip("Off = the carved trench stays, but nothing is added to it (no glow, growths or vents).")]
    public bool dress = true;
    [Tooltip("Fire bubbles rise from vents deep in the crack, and her nests on the open ground are switched off.")]
    public bool bubblesFromCrack = true;
    [Range(0, 8)] public int vents = 4;
    [Range(0, 200)] public int wallGrowths = 70;
    [Range(0, 80)] public int shaftGrowths = 36;
    [Range(0, 80)] public int lipGrowths = 30;
    [ColorUsage(false, true)] public Color seamColour = new Color(2.6f, 0.75f, 0.18f);
    [Tooltip("Inner-world decor prefabs for the walls, floor and shaft (filled in by the carve menu).")]
    public GameObject[] wallDecor = new GameObject[0];
    [Tooltip("Surface decor prefabs that grow along the torn lips (filled in by the carve menu).")]
    public GameObject[] lipDecor = new GameObject[0];

    [Header("Carved Shape (set by the carve menu)")]
    [SerializeField] private int sinkhole = -1;
    [SerializeField] private Vector3 heading;
    [SerializeField] private Vector3 mouth;
    [SerializeField] private int seed = 11;
    [Tooltip("Centre line of the scar, world space, from one end to the other.")]
    [SerializeField] private Vector3[] line = new Vector3[0];
    [SerializeField] private float[] halfWidth = new float[0];
    [SerializeField] private float[] depth = new float[0];
    [Tooltip("Ground height at each line point before carving (the lips).")]
    [SerializeField] private float[] lipHeight = new float[0];
    [SerializeField] private Vector3[] ventPoints = new Vector3[0];

    // What the carve changed in the saved terrain, so the remove menu can put it back exactly.
    [SerializeField, HideInInspector] private Vector3Int[] touched = new Vector3Int[0];
    [SerializeField, HideInInspector] private float[] before = new float[0];

    public bool Carved => line != null && line.Length > 1;
    public int Sinkhole => sinkhole;
    public IReadOnlyList<Vector3> Line => line;
    public IReadOnlyList<Vector3> VentPoints => ventPoints;
    public Transform Dressing { get; private set; }
    public int GrowthsPlaced { get; private set; }

    // ---------------- Registry (edit and play mode) ----------------

    private static readonly List<BoneScar> active = new List<BoneScar>();

    private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
    private void OnDisable() => active.Remove(this);

    /// <summary>Whether a point (looked at from above) lies in a carved scar, or within margin of its lips.</summary>
    public static bool Covers(Vector3 point, float margin)
    {
        foreach (BoneScar scar in active)
            if (scar != null && scar.Carved && scar.Distance(point, out float width) < width + margin) return true;
        return false;
    }

    /// <summary>The way the mother faced when her scar was carved, so she keeps facing it.</summary>
    public static bool TryHeading(int sinkholeIndex, out Vector3 facing)
    {
        foreach (BoneScar scar in active)
            if (scar != null && scar.Carved && scar.sinkhole == sinkholeIndex && scar.heading.sqrMagnitude > 0.5f)
            { facing = scar.heading; return true; }
        facing = Vector3.forward;
        return false;
    }

    /// <summary>Flat distance from the centre line, and the half width there.</summary>
    public float Distance(Vector3 point, out float width)
    {
        float best = float.MaxValue;
        width = 0f;
        var p = new Vector2(point.x, point.z);
        for (int i = 1; i < line.Length; i++)
        {
            var a = new Vector2(line[i - 1].x, line[i - 1].z);
            var b = new Vector2(line[i].x, line[i].z);
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            float d = Vector2.Distance(p, a + ab * t);
            if (d < best) { best = d; width = Mathf.Lerp(halfWidth[i - 1], halfWidth[i], t); }
        }
        return best;
    }

    // ---------------- Set by the carve menu ----------------

    public void SetShape(int sinkholeIndex, Vector3 mouthPoint, Vector3 facing, int shapeSeed, Vector3[] centre, float[] widths,
        float[] depths, float[] lips, Vector3[] ventSpots)
    {
        sinkhole = sinkholeIndex;
        mouth = mouthPoint;
        heading = facing;
        seed = shapeSeed;
        line = centre;
        halfWidth = widths;
        depth = depths;
        lipHeight = lips;
        ventPoints = ventSpots;
    }

    public void SetBackup(Vector3Int[] samples, float[] values)
    {
        touched = samples;
        before = values;
    }

    public void GetBackup(out Vector3Int[] samples, out float[] values)
    {
        samples = touched;
        values = before;
    }

    public void ClearShape()
    {
        line = new Vector3[0];
        halfWidth = depth = lipHeight = new float[0];
        ventPoints = new Vector3[0];
        touched = new Vector3Int[0];
        before = new float[0];
        sinkhole = -1;
    }

    // ---------------- Play mode dressing ----------------

    private IEnumerator Start()
    {
        if (!Application.isPlaying || !Carved || !dress) yield break;
        var kiln = GetComponent<BoneKiln>();
        ProceduralWorld world = FindFirstObjectByType<ProceduralWorld>();
        if (world == null) yield break;
        float reach = 0f;
        foreach (Vector3 p in line) reach = Mathf.Max(reach, Vector3.Distance(p, mouth));
        // Wait for her and for the ground round the whole scar.
        while (kiln != null && (kiln.Root == null || kiln.SinkholeIndex < 0)) yield return null;
        float waited = 0f;
        while (!world.IsRegionReady(mouth, reach + 4f) && waited < 30f) { waited += Time.deltaTime; yield return null; }
        yield return null;
        Physics.SyncTransforms();
        Build(world, kiln);
    }

    private void Build(ProceduralWorld world, BoneKiln kiln)
    {
        var root = new GameObject("Bone Scar (runtime)").transform;
        root.SetParent(transform, true);
        Dressing = root;
        var random = new System.Random(seed);
        int terrain = 1 << world.TerrainLayer;

        // Highest terrain under a point, or NaN when nothing is found within reach (over the open shaft).
        float Floor(Vector3 p, float top, float reach)
        {
            float best = float.NaN;
            foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(p.x, top + 2f, p.z), Vector3.down, reach + 2f, terrain, QueryTriggerInteraction.Ignore))
                if (world.OwnsCollider(hit.collider) && (float.IsNaN(best) || hit.point.y > best)) best = hit.point.y;
            return best;
        }

        // 0. Flowers that ended up over the trench (spawned at runtime, or placed before it was carved) drop onto its ground.
        foreach (MonoBehaviour flower in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (!(flower is BubbleFlower || flower is FireFlower || flower is NectarFlower)) continue;
            Vector3 at = flower.transform.position;
            if (Distance(at, out float w) > w + 8f) continue;
            float below = Floor(at, at.y + 0.5f, 40f);
            if (float.IsNaN(below) || at.y - below < 0.3f) continue;
            Vector3 drop = Vector3.down * (at.y - below - 0.05f);
            flower.transform.position += drop;
            if (flower.TryGetComponent(out Rigidbody body)) body.position += drop;
        }

        // 1. The ember seam: a glowing line along the bottom of the crack, and a faint one along each lip.
        var floor = new Vector3[line.Length];
        var open = new bool[line.Length];
        for (int i = 0; i < line.Length; i++)
        {
            floor[i] = line[i];
            float y = Floor(line[i], lipHeight[i], depth[i] + 3f);
            // Cut through the crust: the seam glows on the inner world's floor far below.
            if (float.IsNaN(y)) y = Floor(line[i], lipHeight[i] - depth[i], 90f);
            open[i] = float.IsNaN(y);
            floor[i].y = (open[i] ? lipHeight[i] - depth[i] : y) + 0.12f;
        }
        Material seam = GlowMaterial(seamColour, "Bone scar seam");
        Broken(root, "Ember Seam", floor, open, seam, i => Mathf.Lerp(0.25f, 1.1f, Profile(i)));
        Material lipGlow = GlowMaterial(seamColour * 0.35f, "Bone scar lips");
        for (int side = -1; side <= 1; side += 2)
        {
            var lip = new Vector3[line.Length];
            var lipOpen = new bool[line.Length];
            for (int i = 0; i < line.Length; i++)
            {
                Vector3 across = Across(i) * side * halfWidth[i] * 1.02f;
                lip[i] = line[i] + across;
                float y = Floor(lip[i], lipHeight[i] + 3f, 8f);
                lipOpen[i] = float.IsNaN(y);
                lip[i].y = (lipOpen[i] ? lipHeight[i] : y) + 0.06f;
            }
            Broken(root, side < 0 ? "Lip Glow L" : "Lip Glow R", lip, lipOpen, lipGlow, i => 0.12f + 0.12f * Profile(i));
        }
        // A warm light low in the crack, either side of the hole.
        for (int k = 0; k < 2; k++)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(line.Length * (k == 0 ? 0.3f : 0.7f)), 0, line.Length - 1);
            var light = new GameObject("Crack Light").AddComponent<Light>();
            light.transform.SetParent(root, true);
            light.transform.position = floor[i] + Vector3.up * 1.5f;
            light.type = LightType.Point;
            light.color = new Color(1f, 0.45f, 0.15f);
            light.range = 14f;
            light.intensity = 2.2f;
            light.shadows = LightShadows.None;
        }

        // 2. Vents deep in the crack. Fire bubbles float up out of it and over to her face; her open-ground nests rest.
        if (bubblesFromCrack && kiln != null && kiln.Root != null)
        {
            Transform kilnRoot = kiln.Root;
            Vector3 keepFrom = mouth + heading * 30f;
            var surfaceNests = new List<KilnBubbleNest>();
            foreach (KilnBubbleNest nest in kilnRoot.GetComponentsInChildren<KilnBubbleNest>(true))
            {
                if (nest.name.Contains("Heart")) { keepFrom = nest.keepFrom == Vector3.zero ? nest.transform.position + Vector3.up : nest.keepFrom; continue; }
                surfaceNests.Add(nest);
            }
            Vector3 gather = surfaceNests.Count > 0 ? surfaceNests[0].drawTo : mouth - heading * 15f;
            Material bone = null;
            foreach (Renderer r in kilnRoot.GetComponentsInChildren<Renderer>()) { bone = r.sharedMaterial; break; }
            int count = Mathf.Min(vents, ventPoints.Length);
            for (int v = 0; v < count; v++) MakeVent(root, ventPoints[v], v);
            ventGather = gather;
            ventKeepFrom = keepFrom;
            if (count > 0) foreach (KilnBubbleNest nest in surfaceNests) nest.gameObject.SetActive(false);
        }

        // 3. Growths (kept off the vents): on the walls and floor of the crack, down the shaft, and scar tissue along the lips.
        if (wallDecor.Length > 0)
        {
            for (int n = 0; n < wallGrowths * 3 && GrowthsPlaced < wallGrowths; n++)
            {
                int i = random.Next(line.Length);
                if (Profile(i) < 0.1f) continue;
                float y = Mathf.Lerp(Mathf.Max(floor[i].y + 0.5f, lipHeight[i] - depth[i] + 2f), lipHeight[i] - 0.3f, (float)random.NextDouble());
                Vector3 from = new Vector3(line[i].x, y, line[i].z);
                Vector3 dir = Across(i) * (random.Next(2) == 0 ? -1f : 1f) + Vector3.down * ((float)random.NextDouble() * 1.4f - 0.4f)
                              + Along(i) * ((float)random.NextDouble() - 0.5f);
                Grow(world, root, from, dir.normalized, halfWidth[i] * 2.5f + 2f, wallDecor, random, 0.55f, 1.2f);
            }
            int shaft = 0;
            Vector3 top = world.SinkholeCentre(sinkhole, mouth.y, out float holeRadius);
            world.ColumnHeights(top, out _, out float ceiling, out float inner);
            for (int n = 0; n < shaftGrowths * 3 && shaft < shaftGrowths; n++)
            {
                float y = Mathf.Lerp(mouth.y - 2f, ceiling - 2f, (float)random.NextDouble());
                Vector3 from = world.SinkholeCentre(sinkhole, y, out _);
                float a = (float)random.NextDouble() * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), (float)random.NextDouble() * 0.6f - 0.3f, Mathf.Sin(a));
                if (Grow(world, root, from, dir.normalized, holeRadius * 2.2f + 4f, wallDecor, random, 0.7f, 1.5f)) shaft++;
            }
            // A bright bed on the inner floor under the hole, where the fire seems to come from.
            for (int n = 0; n < 14; n++)
            {
                float a = n * 2.39996f;
                Vector3 from = world.SinkholeCentre(sinkhole, inner + 3f, out _) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (1f + n * 0.7f);
                Grow(world, root, from, Vector3.down, 8f, wallDecor, random, 0.8f, 1.6f);
            }
        }
        if (lipDecor.Length > 0)
            for (int n = 0, made = 0; n < lipGrowths * 3 && made < lipGrowths; n++)
            {
                int i = random.Next(line.Length);
                float side = random.Next(2) == 0 ? -1f : 1f;
                Vector3 at = line[i] + Across(i) * side * (halfWidth[i] + 0.4f + (float)random.NextDouble() * 1.6f);
                if (Grow(world, root, new Vector3(at.x, lipHeight[i] + 4f, at.z), Vector3.down, 9f, lipDecor, random, 0.6f, 1.1f, flatOnly: true)) made++;
            }

    }

    // ---------------- Vents: fire bubbles rising from the underground up through the crack ----------------

    [Header("Vents")]
    [Min(2f)] public float ventInterval = 7f;
    [Range(1, 6)] public int bubblesPerVent = 3;
    [Tooltip("Rising speed of a bubble in the crack (m/s).")]
    [Min(0.5f)] public float riseSpeed = 5f;
    [Min(4f)] public float bubbleLifetime = 40f;

    private sealed class Vent
    {
        public Vector3 point, lineXZ;
        public float lip, timer;
        public readonly List<(FireBubble bubble, float born)> alive = new List<(FireBubble, float)>();
    }
    private readonly List<Vent> ventList = new List<Vent>();
    private Vector3 ventGather, ventKeepFrom;
    private FireBubble bubblePrefab;

    /// <summary>For tests: bubbles from the vents still free, and how many have come up out of the crack.</summary>
    public int VentBubbles { get { int n = 0; foreach (Vent v in ventList) n += v.alive.Count; return n; } }
    public int VentBubblesOut { get; private set; }
    public int VentBubblesReleased { get; private set; }

    private void MakeVent(Transform parent, Vector3 point, int index)
    {
        int i = NearestIndex(point);
        // A glowing split in the inner floor with a light: where the fire comes from.
        var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        glow.name = "Crack Vent";
        Destroy(glow.GetComponent<Collider>());
        glow.transform.SetParent(parent, true);
        glow.transform.SetPositionAndRotation(point + Vector3.up * 0.1f, Quaternion.LookRotation(Along(i), Vector3.up));
        glow.transform.localScale = new Vector3(1.1f, 0.35f, 2.6f);
        glow.GetComponent<Renderer>().sharedMaterial = GlowMaterial(seamColour * 1.4f, "Bone scar vent");
        var light = new GameObject("Vent Light").AddComponent<Light>();
        light.transform.SetParent(glow.transform, true);
        light.transform.position = point + Vector3.up * 2f;
        light.type = LightType.Point;
        light.color = new Color(1f, 0.5f, 0.18f);
        light.range = 12f;
        light.intensity = 3f;
        light.shadows = LightShadows.None;
        ventList.Add(new Vent { point = point, lineXZ = new Vector3(line[i].x, 0f, line[i].z), lip = lipHeight[i], timer = 1f + index * 1.3f });
    }

    private int NearestIndex(Vector3 point)
    {
        int best = 0;
        float d = float.MaxValue;
        for (int i = 0; i < line.Length; i++)
        {
            float e = new Vector2(line[i].x - point.x, line[i].z - point.z).sqrMagnitude;
            if (e < d) { d = e; best = i; }
        }
        return best;
    }

    private FireBubble ResolveBubble()
    {
        if (bubblePrefab != null) return bubblePrefab;
        foreach (FireFlower flower in FindObjectsByType<FireFlower>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (flower.BubblePrefab != null) return bubblePrefab = flower.BubblePrefab;
        return bubblePrefab = FindFirstObjectByType<FireBubble>();
    }

    private void Update()
    {
        if (!Application.isPlaying || ventList.Count == 0) return;
        foreach (Vent vent in ventList)
        {
            for (int k = vent.alive.Count - 1; k >= 0; k--)
            {
                FireBubble b = vent.alive[k].bubble;
                if (b == null) { vent.alive.RemoveAt(k); continue; }
                // Taken by the player, a child or her bones: no longer the vent's to keep.
                if (b.IsCarried || b.IsLeashed || b.IsBoundToBone || BoneChild.IsCradled(b)) { vent.alive.RemoveAt(k); continue; }
                if (Time.time - vent.alive[k].born > bubbleLifetime) { Destroy(b.gameObject); vent.alive.RemoveAt(k); }
            }
            vent.timer -= Time.deltaTime;
            if (vent.timer > 0f || vent.alive.Count >= bubblesPerVent) continue;
            vent.timer = ventInterval;
            FireBubble prefab = ResolveBubble();
            if (prefab == null) continue;
            FireBubble bubble = Instantiate(prefab, vent.point + Vector3.up * 1.2f, Quaternion.identity);
            bubble.name = "Fire Bubble (crack vent)";
            bubble.gameObject.SetActive(true);
            // Carried up the crack by the vent (its own flight would stall under the crust); the vent pops it in time.
            bubble.LifetimeLimited = false;
            if (bubble.TryGetComponent(out Rigidbody body)) body.isKinematic = true;
            vent.alive.Add((bubble, Time.time));
        }
    }

    private void FixedUpdate()
    {
        int outside = 0;
        foreach (Vent vent in ventList)
            foreach (var (bubble, born) in vent.alive)
            {
                if (bubble == null || bubble.IsCarried || bubble.IsBoundToBone || BoneChild.IsCradled(bubble)) continue;
                var rb = bubble.GetComponent<Rigidbody>();
                if (rb == null) continue;
                Vector3 p = rb.position;
                if (rb.isKinematic)
                {
                    // Rising up the middle of the crack, swaying a little; set free once above the lips.
                    if (p.y >= vent.lip + 2f) { rb.isKinematic = false; rb.linearVelocity = Vector3.up * 1.5f; VentBubblesReleased++; continue; }
                    Vector3 toLine = new Vector3(vent.lineXZ.x - p.x, 0f, vent.lineXZ.z - p.z);
                    float age = Time.time - born;
                    Vector3 sway = new Vector3(Mathf.Sin(age * 1.3f + born), 0f, Mathf.Cos(age * 1.1f + born)) * 0.4f;
                    rb.MovePosition(p + (toLine * 0.8f + sway + Vector3.up * riseSpeed) * Time.fixedDeltaTime);
                    continue;
                }
                outside++;
                // Out of the crack: drift over to her face and wander there, away from her heart (as her nests did).
                float wander = Time.time * 0.3f + born;
                Vector3 goal = ventGather + new Vector3(Mathf.Sin(wander) * 6f, 3.5f + Mathf.Sin(wander * 1.7f), Mathf.Cos(wander * 0.8f) * 6f);
                Vector3 force = Vector3.ClampMagnitude((goal - p) * 0.6f - rb.linearVelocity * 0.4f, 5f);
                Vector3 push = p - ventKeepFrom;
                push.y = 0f;
                if (push.magnitude < 9f) force += push.normalized * 7.5f;
                force.y = Mathf.Clamp((goal.y - p.y) * 0.6f - rb.linearVelocity.y * 0.5f, -2f, 2f);
                rb.AddForce(force, ForceMode.Acceleration);
            }
        VentBubblesOut = outside;
    }

    /// <summary>One growth where a ray from inside the crack meets the terrain.</summary>
    private bool Grow(ProceduralWorld world, Transform parent, Vector3 from, Vector3 dir, float distance, GameObject[] prefabs,
        System.Random random, float minScale, float maxScale, bool flatOnly = false)
    {
        RaycastHit best = default;
        float nearest = float.MaxValue;
        foreach (RaycastHit hit in Physics.RaycastAll(from, dir, distance, 1 << world.TerrainLayer, QueryTriggerInteraction.Ignore))
            if (world.OwnsCollider(hit.collider) && hit.distance < nearest) { nearest = hit.distance; best = hit; }
        if (best.collider == null || (flatOnly && best.normal.y < 0.6f)) return false;
        foreach (Vector3 vent in ventPoints) if ((best.point - vent).sqrMagnitude < 3.2f * 3.2f) return false;
        GameObject prefab = prefabs[random.Next(prefabs.Length)];
        if (prefab == null) return false;
        // Growths stand along the surface they hold on to: up from floors, out of walls, down from overhangs.
        Vector3 up = Vector3.Slerp(Vector3.up, best.normal, flatOnly ? 0.3f : 0.85f).normalized;
        if (best.normal.y < -0.3f) up = best.normal;
        Quaternion rotation = Quaternion.AngleAxis((float)random.NextDouble() * 360f, up) * Quaternion.FromToRotation(Vector3.up, up);
        GameObject made = Instantiate(prefab, best.point - up * 0.08f, rotation, parent);
        made.transform.localScale = prefab.transform.localScale * Mathf.Lerp(minScale, maxScale, (float)random.NextDouble());
        GrowthsPlaced++;
        return true;
    }

    // 0 at the ends of the scar, 1 at the hole.
    private float Profile(int i)
    {
        float u = line.Length > 1 ? i / (float)(line.Length - 1) * 2f - 1f : 0f;
        return 1f - Mathf.Abs(u);
    }

    private Vector3 Along(int i)
    {
        Vector3 d = line[Mathf.Min(i + 1, line.Length - 1)] - line[Mathf.Max(i - 1, 0)];
        d.y = 0f;
        return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.right;
    }

    private Vector3 Across(int i) => Vector3.Cross(Vector3.up, Along(i));

    /// <summary>Glowing lines through the points, broken wherever a point is open (over the shaft).</summary>
    private static void Broken(Transform parent, string name, Vector3[] points, bool[] open, Material material, System.Func<int, float> width)
    {
        for (int start = 0; start < points.Length;)
        {
            while (start < points.Length && open[start]) start++;
            int end = start;
            while (end < points.Length && !open[end]) end++;
            if (end - start >= 2)
            {
                var part = new Vector3[end - start];
                System.Array.Copy(points, start, part, 0, part.Length);
                int offset = start;
                GlowLine(parent, name, part, material, i => width(i + offset));
            }
            start = end;
        }
    }

    private static void GlowLine(Transform parent, string name, Vector3[] points, Material material, System.Func<int, float> width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = points.Length;
        lr.SetPositions(points);
        lr.sharedMaterial = material;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.numCornerVertices = 2;
        lr.alignment = LineAlignment.View;
        var curve = new AnimationCurve();
        for (int i = 0; i < points.Length; i++) curve.AddKey(points.Length > 1 ? i / (float)(points.Length - 1) : 0f, width(i));
        lr.widthCurve = curve;
        lr.widthMultiplier = 1f;
    }

    private static Material GlowMaterial(Color colour, string name)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
        var material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", colour);
        material.SetColor("_Color", colour);
        return material;
    }

    private void OnDrawGizmosSelected()
    {
        if (!Carved) return;
        Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.9f);
        for (int i = 1; i < line.Length; i++) Gizmos.DrawLine(line[i - 1], line[i]);
        foreach (Vector3 v in ventPoints) Gizmos.DrawWireSphere(v, 0.8f);
    }
}
