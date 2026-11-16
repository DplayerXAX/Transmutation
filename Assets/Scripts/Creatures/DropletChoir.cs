using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A loose cloud of GlyphDroplets that, every so often and of its own accord, gathers in the air
/// and writes a glyph, holds it, then lets it go again.
///
/// Drift: each droplet wanders inside the cloud along its own slow noise path.
/// Gather: the glyph's strokes are sampled into points; each point, in writing order, takes the
/// nearest free droplet, and that droplet sets off after a delay proportional to how far along the
/// pen path its point lies, so the glyph appears to be written stroke by stroke. Every droplet
/// travels on a gentle arc and eases in on its own spring; nothing ever snaps.
/// Hold: the glyph hangs and glows faintly, a slow shimmer running along the strokes, and turns
/// lazily to face the viewer. Disperse: droplets leave in reverse order and drift away.
///
/// Glyphs come from the Glyph Source (any component implementing IDropletGlyphSource, for the
/// planet's own script later) and otherwise from the built-in DropletGlyphs test font and figures.
/// Other code can call Show(key) / Release().
/// </summary>
[DisallowMultipleComponent]
public sealed class DropletChoir : MonoBehaviour
{
    public enum Phase { Drift, Gather, Hold, Disperse }

    [Header("Droplets")]
    [Min(1)] [SerializeField] private int dropletCount = 150;
    [Min(0.02f)] [SerializeField] private float dropletDiameter = 0.12f;
    [Tooltip("Each droplet's spring time is picked in this range: some hurry, some dawdle.")]
    [SerializeField] private Vector2 smoothTimeRange = new Vector2(0.9f, 1.8f);
    [Min(0.1f)] [SerializeField] private float maxSpeed = 2.2f;
    [SerializeField] private Material dropletMaterial;

    [Header("Glyphs")]
    [Tooltip("Shown in turn. Text uses the test font; '@circle', '@spiral', '@wave', '@star', '@eye' are figures; \\n starts a new line.")]
    [SerializeField] private string[] glyphs = { "WATER", "@spiral", "HELLO", "@eye", "DRIFT", "@star" };
    [Tooltip("Optional component implementing IDropletGlyphSource (e.g. the planet glyph language). Asked first.")]
    [SerializeField] private MonoBehaviour glyphSource;
    [Tooltip("Height of one letter (a 6-unit glyph cell) in metres.")]
    [Min(0.2f)] [SerializeField] private float glyphHeight = 1.6f;
    [Tooltip("Glyphs wider than this are scaled down to fit.")]
    [Min(0.5f)] [SerializeField] private float maxGlyphWidth = 9f;
    [Tooltip("Distance between neighbouring droplets along a stroke, in metres.")]
    [Min(0.03f)] [SerializeField] private float spacing = 0.16f;
    [Tooltip("The glyph's centre, and the cloud's, above this object.")]
    [SerializeField] private float hoverHeight = 2.6f;
    [SerializeField] private bool faceViewer = true;
    [Min(0.1f)] [SerializeField] private float turnSmoothTime = 3f;

    [Header("Cloud")]
    [SerializeField] private Vector3 cloudExtents = new Vector3(3.5f, 1.2f, 3.5f);
    [Tooltip("How quickly each droplet's wander path changes (noise cycles per second).")]
    [Min(0f)] [SerializeField] private float wanderRate = 0.045f;

    [Header("Merging")]
    [Tooltip("Draw the droplets as one liquid surface: close droplets reach out with a neck and flow together.")]
    [SerializeField] private bool mergeSurface = true;
    [Tooltip("Reach of each droplet's pull, in droplet radii. Two droplets bridge when closer than about 1.15 x this x radius.")]
    [Range(1.3f, 4f)] [SerializeField] private float influence = 2.6f;
    [Tooltip("Surface grid step in metres (smaller = finer, slower).")]
    [Range(0.008f, 0.06f)] [SerializeField] private float surfaceCell = 0.034f;
    [Tooltip("Surface tension: drifting droplets that touch pull together and cling for a while before the drift tears them apart.")]
    [Range(0f, 0.9f)] [SerializeField] private float stickiness = 0.55f;
    [Tooltip("Distance, in droplet diameters, within which droplets cling.")]
    [Min(0.5f)] [SerializeField] private float clingReach = 2.2f;

    [Header("Timing (seconds)")]
    [SerializeField] private Vector2 driftTimeRange = new Vector2(12f, 20f);
    [Tooltip("The pen's time from first stroke to last: the spread of the droplets' starting delays.")]
    [Min(0f)] [SerializeField] private float writeDuration = 5f;
    [Tooltip("How long one droplet takes to glide from the cloud to its place.")]
    [Min(0.2f)] [SerializeField] private float travelTime = 2.4f;
    [Min(0f)] [SerializeField] private float holdTime = 9f;
    [Tooltip("Gather sooner when the player stands within this distance (0 = never).")]
    [Min(0f)] [SerializeField] private float noticeRadius = 10f;
    [Tooltip("Shortest drift before a gathering when the player is near.")]
    [Min(0f)] [SerializeField] private float noticedDriftTime = 5f;

    private struct Singer
    {
        public GlyphDroplet droplet;
        public Vector3 seed;
        public float weight, order, arc, side;
        public Vector2 slot;
        public bool inGlyph;
    }

    private Singer[] singers = new Singer[0];
    private readonly List<Vector2[]> strokes = new List<Vector2[]>();
    private readonly List<Vector2> points = new List<Vector2>();
    private readonly List<float> orders = new List<float>();
    private Phase phase = Phase.Drift;
    private float phaseTime, driftDuration, yaw, yawVelocity, formAmount;
    private int nextGlyph;
    private Transform viewer;
    private Transform player;
    private Vector3[] positions = new Vector3[0];
    private Vector3[] velocities = new Vector3[0];
    private float[] radii = new float[0];
    private float[] glowValues = new float[0];
    private DropletSurface surface;
    private GameObject surfaceObject;
    private Material surfaceMaterial;
    /// <summary>Vertices in the merged surface this frame (0 when merging is off).</summary>
    public float LastSurfaceMilliseconds { get; private set; }
    public int SurfaceVertexCount => surface != null ? surface.VertexCount : 0;

    public event System.Action<DropletChoir, string> GlyphFormed;
    public Phase CurrentPhase => phase;
    public Material DropletMaterial => dropletMaterial;
    public string CurrentGlyph { get; private set; }
    public int DropletCount => singers.Length;
    /// <summary>Droplets placed in the current glyph.</summary>
    public int GlyphDropletCount { get; private set; }
    public Vector3 Anchor => viewLocked ? transform.position : transform.position + Vector3.up * hoverHeight;
    /// <summary>The glyph plane: turned to face the viewer, or the camera's own rotation for a screen title.</summary>
    public Quaternion GlyphRotation => viewLocked ? transform.rotation : Quaternion.Euler(0f, yaw, 0f);

    private void Start()
    {
        viewer = titleView != null ? titleView : Camera.main != null ? Camera.main.transform : null;
        var carrier = FindFirstObjectByType<PlayerCreatureCarrier>();
        if (carrier != null) player = carrier.transform;
        yaw = FacingYaw(transform.eulerAngles.y);
        Spawn();
        // The first gathering comes a little sooner than the later ones.
        driftDuration = Random.Range(driftTimeRange.x, driftTimeRange.y) * 0.5f;
        if (ephemeral && !Show(ephemeralKey)) vanishing = true;
    }

    // ---------------- Summoned (one-off) choirs ----------------

    private bool ephemeral, vanishing;
    private string ephemeralKey;
    private float appearTime, vanishTime;

    /// <summary>True for a choir made by Summon: it writes one glyph, then evaporates and removes itself.</summary>
    public bool IsSummoned => ephemeral;

    /// <summary>
    /// Makes a one-off choir standing on groundPoint: its droplets condense out of the air all around,
    /// write the glyph once, hold it, scatter, and evaporate; then the choir removes itself.
    /// </summary>
    public static DropletChoir Summon(Vector3 groundPoint, string key, MonoBehaviour source, Material material,
        int count = 160, float height = 1.4f, float hover = 2.4f, float hold = 9f)
    {
        var go = new GameObject("Summoned Choir (" + key + ")");
        go.transform.position = groundPoint;
        go.SetActive(false); // configure before Start runs
        var choir = go.AddComponent<DropletChoir>();
        choir.ephemeral = true;
        choir.ephemeralKey = key;
        choir.glyphSource = source;
        choir.dropletMaterial = material;
        choir.dropletCount = count;
        choir.glyphHeight = height;
        choir.hoverHeight = hover;
        choir.holdTime = hold;
        choir.noticeRadius = 0f;
        choir.writeDuration = 4f;
        choir.cloudExtents = new Vector3(6f, 2.2f, 6f);
        go.SetActive(true);
        return choir;
    }

    private bool viewLocked;
    private Transform titleView;
    /// <summary>
    /// A region title: a one-off choir that hangs in the upper middle of the view (distance metres ahead,
    /// raise metres above eye height), follows the view lazily, and is dense enough that its creatures
    /// flow together into solid strokes. Creature count comes from the glyph's stroke length.
    /// </summary>
    public static DropletChoir SummonTitle(Transform view, string key, MonoBehaviour source, Material material,
        float height = 1f, float distance = 4.5f, float hold = 6f, float raise = 1.15f)
    {
        const float titleSpacing = 0.045f;
        var strokes = new List<Vector2[]>();
        bool found = source is IDropletGlyphSource glyphs && glyphs.TryGetStrokes(key, strokes) && strokes.Count > 0;
        if (!found) DropletGlyphs.TryGetStrokes(key, strokes);
        float length = 0f;
        foreach (Vector2[] stroke in strokes)
        {
            length += 1f;
            for (int i = 1; i < stroke.Length; i++) length += Vector2.Distance(stroke[i - 1], stroke[i]);
        }
        int needed = Mathf.CeilToInt(length * (height / 6f) / titleSpacing);
        // Fixed in the camera's own space, like a title on the screen, but made of real creatures.
        var go = new GameObject("Summoned Title (" + key + ")");
        go.SetActive(false);
        go.transform.SetParent(view, false);
        go.transform.localPosition = new Vector3(0f, raise, distance);
        go.transform.localRotation = Quaternion.identity;
        var choir = go.AddComponent<DropletChoir>();
        choir.ephemeral = true;
        choir.viewLocked = true;
        choir.titleView = view;
        choir.ephemeralKey = key;
        choir.glyphSource = source;
        choir.dropletMaterial = material;
        choir.dropletCount = Mathf.Clamp(Mathf.CeilToInt(needed * 1.1f) + 12, 30, 600);
        choir.dropletDiameter = 0.06f;
        choir.spacing = titleSpacing;
        choir.glyphHeight = height;
        choir.hoverHeight = 0f;
        choir.holdTime = hold;
        choir.noticeRadius = 0f;
        choir.writeDuration = 2.4f;
        choir.travelTime = 1.5f;
        choir.maxSpeed = 7f;
        choir.smoothTimeRange = new Vector2(0.35f, 0.7f);
        choir.turnSmoothTime = 0.5f;
        choir.cloudExtents = new Vector3(1.8f, 0.7f, 0.5f);
        choir.stickiness = 0.3f;
        go.SetActive(true);
        return choir;
    }

    /// <summary>Fades summoned droplets in (staggered, condensing out of the air) and out again.</summary>
    private void UpdatePresence(float dt)
    {
        appearTime += dt;
        if (vanishing) vanishTime += dt;
        for (int i = 0; i < singers.Length; i++)
        {
            if (singers[i].droplet == null) continue;
            float delay = Mathf.Repeat(singers[i].seed.y, 1f) * 1.6f;
            float presence = Mathf.Clamp01((appearTime - delay) / 1.2f) * (1f - Mathf.Clamp01((vanishTime - delay) / 1.2f));
            // Creatures the glyph did not need swirl a moment, then dissolve, leaving the title clear.
            if (!singers[i].inGlyph) presence *= 1f - Mathf.Clamp01((appearTime - 3f - delay) / 1.5f);
            singers[i].droplet.SetPresence(presence * presence * (3f - 2f * presence));
        }
        if (vanishing && vanishTime > 3f) Destroy(gameObject);
    }

    private void Spawn()
    {
        var root = new GameObject("Droplets").transform;
        root.SetParent(transform, false);
        singers = new Singer[dropletCount];
        for (int i = 0; i < dropletCount; i++)
        {
            var go = new GameObject("Glyph Droplet " + i);
            go.transform.SetParent(root, false);
            Singer singer = default;
            singer.seed = new Vector3(Random.value * 100f, Random.value * 100f, Random.value * 100f);
            go.transform.position = DriftPoint(ref singer, 0f);
            singer.droplet = go.AddComponent<GlyphDroplet>();
            singer.droplet.Configure(this, dropletMaterial, dropletDiameter * Random.Range(0.8f, 1.2f),
                Random.Range(smoothTimeRange.x, smoothTimeRange.y), maxSpeed);
            if (ephemeral) singer.droplet.SetPresence(0f);
            singer.arc = Random.Range(0.25f, 0.9f);
            singer.side = Random.Range(-0.6f, 0.6f);
            singers[i] = singer;
        }
    }

    /// <summary>Gathers now into the given glyph (a test-font text, an '@figure', or a key the Glyph Source knows).</summary>
    public bool Show(string key)
    {
        if (!Resolve(key)) return false;
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
        foreach (Vector2[] stroke in strokes)
            foreach (Vector2 p in stroke) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
        float scale = glyphHeight / 6f;
        if ((max.x - min.x) * scale > maxGlyphWidth) scale = maxGlyphWidth / (max.x - min.x);
        Vector2 centre = (min + max) * 0.5f;

        // Droplets in someone's hand cannot answer; the rest share the glyph.
        int free = 0;
        for (int i = 0; i < singers.Length; i++)
        {
            singers[i].inGlyph = false;
            if (singers[i].droplet != null && !singers[i].droplet.Held) free++;
        }
        DropletGlyphs.Sample(strokes, spacing / scale, free, points, orders);
        if (points.Count == 0) return false;

        if (faceViewer) yaw = FacingYaw(yaw);
        yawVelocity = 0f;
        Quaternion rotation = GlyphRotation;
        Vector3 anchor = Anchor;
        // Each point, in pen order, calls the nearest droplet still free.
        for (int p = 0; p < points.Count; p++)
        {
            Vector2 local = (points[p] - centre) * scale;
            Vector3 world = anchor + rotation * new Vector3(local.x, local.y, 0f);
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < singers.Length; i++)
            {
                if (singers[i].inGlyph || singers[i].droplet == null || singers[i].droplet.Held) continue;
                float d = (singers[i].droplet.transform.position - world).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            if (best < 0) break;
            singers[best].inGlyph = true;
            singers[best].slot = local;
            singers[best].order = orders[p];
        }
        GlyphDropletCount = points.Count;
        CurrentGlyph = key;
        SetPhase(Phase.Gather);
        return true;
    }

    /// <summary>Lets the current glyph go: droplets leave in reverse writing order.</summary>
    public void Release()
    {
        if (phase == Phase.Gather || phase == Phase.Hold) SetPhase(Phase.Disperse);
    }

    private bool Resolve(string key)
    {
        strokes.Clear();
        if (glyphSource is IDropletGlyphSource source && source.TryGetStrokes(key, strokes) && strokes.Count > 0) return true;
        return DropletGlyphs.TryGetStrokes(key, strokes);
    }

    private void SetPhase(Phase next)
    {
        phase = next;
        phaseTime = 0f;
        if (next == Phase.Drift)
        {
            driftDuration = Random.Range(driftTimeRange.x, driftTimeRange.y);
            for (int i = 0; i < singers.Length; i++) singers[i].inGlyph = false;
            CurrentGlyph = null;
            GlyphDropletCount = 0;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || singers.Length == 0) return;
        phaseTime += dt;
        StepPhase();
        if (ephemeral) UpdatePresence(dt);

        bool forming = phase == Phase.Gather || phase == Phase.Hold;
        formAmount = Mathf.MoveTowards(formAmount, forming ? 1f : 0f, dt / Mathf.Max(0.1f, travelTime));
        // A screen title is a child of the camera and needs no turning; others face the viewer slowly.
        if (forming && !viewLocked && faceViewer && viewer != null)
            yaw = Mathf.SmoothDampAngle(yaw, FacingYaw(yaw), ref yawVelocity, turnSmoothTime, 40f, dt);

        Quaternion rotation = GlyphRotation;
        Vector3 anchor = Anchor;
        Vector3 normal = rotation * Vector3.forward;
        float t = Time.time;
        if (positions.Length != singers.Length) AllocateBuffers();
        for (int i = 0; i < singers.Length; i++)
            if (singers[i].droplet != null) positions[i] = singers[i].droplet.transform.position;
        float clingDistance = clingReach * dropletDiameter;
        for (int i = 0; i < singers.Length; i++)
        {
            ref Singer s = ref singers[i];
            if (s.droplet == null) continue;
            // Weight 0 = in the cloud, 1 = in its place in the glyph. It changes at a steady rate once
            // the droplet's turn comes, and is eased below, so the start and finish are both gentle.
            if (s.inGlyph && forming)
            {
                if (phaseTime >= s.order * writeDuration || phase == Phase.Hold) s.weight = Mathf.Min(1f, s.weight + dt / travelTime);
            }
            else if (phase != Phase.Disperse || phaseTime >= (1f - s.order) * writeDuration * 0.6f)
                s.weight = Mathf.Max(0f, s.weight - dt / travelTime);

            float e = s.weight * s.weight * (3f - 2f * s.weight);
            Vector3 drift = DriftPoint(ref s, t);
            // While a glyph hangs, the spare droplets keep to the back so the strokes stay readable.
            if (!s.inGlyph) drift -= normal * (formAmount * 1.2f);
            // Surface tension: a drifting droplet's goal leans toward any droplet it touches, so touching
            // droplets cling and move as one until their own wandering pulls hard enough to tear them apart.
            if (stickiness > 0f && e < 0.99f) drift += Cling(i, clingDistance) * (1f - e);
            Vector3 place = anchor + rotation * new Vector3(s.slot.x, s.slot.y, 0f)
                + normal * (Mathf.Sin(t * 0.7f + s.seed.x) * 0.015f);
            float lift = Mathf.Sin(e * Mathf.PI);
            Vector3 goal = Vector3.LerpUnclamped(drift, place, e)
                + Vector3.up * (lift * s.arc) + rotation * Vector3.right * (lift * s.side);
            // A faint glow that a slow shimmer carries along the strokes, in writing order.
            float shimmer = phase == Phase.Hold ? 0.15f * (0.5f + 0.5f * Mathf.Sin(t * 1.3f - s.order * 9f)) : 0f;
            s.droplet.SetGoal(goal, e * (0.55f + shimmer));
        }
    }

    private Vector3 Cling(int index, float reach)
    {
        Vector3 me = positions[index], pull = Vector3.zero;
        float reach2 = reach * reach;
        for (int j = 0; j < positions.Length; j++)
        {
            if (j == index) continue;
            Vector3 offset = positions[j] - me;
            float d2 = offset.sqrMagnitude;
            if (d2 >= reach2 || d2 < 1e-8f) continue;
            pull += offset * (stickiness * (1f - Mathf.Sqrt(d2) / reach));
        }
        return Vector3.ClampMagnitude(pull, reach);
    }

    private void AllocateBuffers()
    {
        int n = singers.Length;
        positions = new Vector3[n];
        radii = new float[n];
        velocities = new Vector3[n];
        glowValues = new float[n];
    }

    private void LateUpdate()
    {
        if (!mergeSurface || singers.Length == 0)
        {
            if (surfaceObject != null) surfaceObject.SetActive(false);
            for (int i = 0; i < singers.Length; i++) if (singers[i].droplet != null) singers[i].droplet.SetBeadVisible(true);
            return;
        }
        if (positions.Length != singers.Length) AllocateBuffers();
        if (surface == null)
        {
            surface = new DropletSurface();
            surfaceObject = new GameObject(name + " Surface");
            // Built in this object's own space, so it moves with it (a screen title rides the camera with no lag).
            surfaceObject.transform.SetParent(transform, false);
            surfaceObject.AddComponent<MeshFilter>().sharedMesh = surface.Mesh;
            var renderer = surfaceObject.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Material source = dropletMaterial;
            if (source == null)
            {
                Shader shader = Shader.Find("Capstone/Creature/GoldenGlyph");
                if (shader != null) source = new Material(shader);
            }
            if (source != null)
            {
                surfaceMaterial = new Material(source) { name = "Droplet surface" };
                surfaceMaterial.SetFloat("_VertexGlow", 1f);
                renderer.sharedMaterial = surfaceMaterial;
            }
        }
        surfaceObject.SetActive(true);
        // Droplets in a hand or a bubble are drawn as their own bead and leave the body.
        int count = 0;
        for (int i = 0; i < singers.Length; i++)
        {
            GlyphDroplet d = singers[i].droplet;
            if (d == null) continue;
            bool alone = d.Held;
            d.SetBeadVisible(alone);
            if (alone || d.Radius < 0.002f) continue;
            positions[count] = transform.InverseTransformPoint(d.transform.position);
            radii[count] = d.Radius;
            velocities[count] = transform.InverseTransformDirection(d.Velocity);
            glowValues[count] = d.Glow;
            count++;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        surface.Rebuild(positions, radii, velocities, glowValues, count, influence, surfaceCell, 0.22f);
        LastSurfaceMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
    }

    private void OnDestroy()
    {
        surface?.Dispose();
        surface = null;
        if (surfaceObject != null) Destroy(surfaceObject);
        if (surfaceMaterial != null) Destroy(surfaceMaterial);
    }

    private void StepPhase()
    {
        switch (phase)
        {
            case Phase.Drift:
                if (ephemeral) { vanishing = true; break; }
                bool noticed = player != null && noticeRadius > 0f &&
                    (player.position - transform.position).sqrMagnitude < noticeRadius * noticeRadius;
                if (phaseTime >= driftDuration || (noticed && phaseTime >= noticedDriftTime))
                {
                    // Try each glyph once, so one unknown key cannot stall the choir.
                    for (int tries = 0; tries < Mathf.Max(1, glyphs.Length); tries++)
                    {
                        string key = glyphs.Length > 0 ? glyphs[nextGlyph++ % glyphs.Length] : "@circle";
                        if (Show(key)) break;
                    }
                    if (phase == Phase.Drift) phaseTime = 0f;
                }
                break;
            case Phase.Gather:
                if (phaseTime >= writeDuration + travelTime + 0.5f)
                {
                    SetPhase(Phase.Hold);
                    GlyphFormed?.Invoke(this, CurrentGlyph);
                }
                break;
            case Phase.Hold:
                if (phaseTime >= holdTime) SetPhase(Phase.Disperse);
                break;
            case Phase.Disperse:
                if (phaseTime >= writeDuration * 0.6f + travelTime + 0.1f) SetPhase(Phase.Drift);
                break;
        }
    }

    /// <summary>A droplet's own slow wander inside the cloud.</summary>
    private Vector3 DriftPoint(ref Singer s, float time)
    {
        float k = time * wanderRate;
        var n = new Vector3(
            Mathf.PerlinNoise(s.seed.x + k, s.seed.y) * 2f - 1f,
            Mathf.PerlinNoise(s.seed.y + k, s.seed.z) * 2f - 1f,
            Mathf.PerlinNoise(s.seed.z + k, s.seed.x) * 2f - 1f);
        // Perlin rarely leaves +-0.6; stretch so the cloud fills its extents.
        n *= 1.6f;
        Vector3 offset = Vector3.Scale(n, cloudExtents);
        return Anchor + (viewLocked ? transform.rotation * offset : offset);
    }

    private float FacingYaw(float fallback)
    {
        if (viewer == null) return fallback;
        Vector3 away = Anchor - viewer.position;
        away.y = 0f;
        return away.sqrMagnitude < 0.01f ? fallback : Quaternion.LookRotation(away).eulerAngles.y;
    }

    /// <summary>Mean distance from each glyph droplet to its place (test and debugging aid).</summary>
    public float MeanPlaceError()
    {
        Quaternion rotation = GlyphRotation;
        Vector3 anchor = Anchor;
        float sum = 0f;
        int count = 0;
        foreach (Singer s in singers)
        {
            if (!s.inGlyph || s.droplet == null || s.droplet.Held) continue;
            sum += Vector3.Distance(s.droplet.transform.position, anchor + rotation * new Vector3(s.slot.x, s.slot.y, 0f));
            count++;
        }
        return count == 0 ? 0f : sum / count;
    }

    /// <summary>The droplets, for tests and other systems.</summary>
    public GlyphDroplet GetDroplet(int index) => singers[index].droplet;

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0.9f, 1f, 0.5f);
        Gizmos.DrawWireCube(Anchor, cloudExtents * 2f);
    }
}
