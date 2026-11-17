using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One square tile of the wildflower meadow, drawn with GPU instancing (no GameObjects, no colliders) and grown
/// from a seed on the terrain. Tiles sit side by side over the flower field; all their patterns (drifts, colour
/// bands, the meadow edge) come from world-space noise, so neighbouring tiles join without seams.
/// One flower family (daisies in three builds) fills sweeping drifts, low at the drift edges and tall in the cores.
/// Colour bands walk through the palette and shift towards the last band (ice) near a chosen point (the bone
/// mother); black paths run between drifts, and the meadow fades raggedly into the plain lily field at its edge.
/// Flowers grow round the lilies (the lilies are real, pickable bubble flowers), never on top of them.
/// Far tiles draw half, then a quarter, of their flowers; tiles grow one per frame so loading never spikes.
/// </summary>
[ExecuteAlways]
public sealed class FloraPatch : MonoBehaviour
{
    [Tooltip("Half the tile's side (metres); the tile covers a square round this object.")]
    [Min(4f)] [SerializeField] private float halfSize = 16f;
    [SerializeField] private int seed = 7;
    [Tooltip("Flowers per square metre inside a drift.")]
    [Min(0.5f)] [SerializeField] private float density = 5f;
    [Tooltip("Share of the ground left as open black paths between drifts (0 = carpet, 0.6 = sparse islands).")]
    [Range(0f, 0.8f)] [SerializeField] private float openGround = 0.2f;
    [Tooltip("Size of the drifts (metres across, roughly).")]
    [Min(2f)] [SerializeField] private float driftSize = 9f;
    [Tooltip("Flower size multiplier.")]
    [Min(0.2f)] [SerializeField] private float scale = 1.3f;
    [Header("Meadow shape (world x, z)")]
    [Tooltip("Centre and radii of the oval meadow; flowers thin out raggedly towards its rim.")]
    [SerializeField] private Vector2 meadowCentre = new Vector2(-55f, 45f);
    [SerializeField] private Vector2 meadowRadii = new Vector2(95f, 85f);
    [Tooltip("Colour bands shift towards the last band near this point.")]
    [SerializeField] private Vector2 coolToward = new Vector2(2f, 161f);
    [Min(10f)] [SerializeField] private float coolReach = 190f;
    [Header("Materials")]
    [Tooltip("Petal colours; colour bands sweep through these in order.")]
    [SerializeField] private Material[] tones = new Material[0];
    [Tooltip("The last tone is a rare accent: it never forms its own band and only a few flowers take it.")]
    [SerializeField] private bool rareLastTone;
    [SerializeField] private Material stem;
    [Tooltip("Flower centres.")]
    [SerializeField] private Material centre;
    [Tooltip("Not drawn beyond this camera distance; beyond a third of it half the flowers, beyond two thirds a quarter.")]
    [Min(10f)] [SerializeField] private float drawDistance = 85f;

    private struct Plant { public Vector2 at; public int tone, variant; public float height, yaw, reach; }
    private readonly List<Plant> plants = new List<Plant>();
    private sealed class Layer
    {
        public Mesh mesh; public int submesh; public Material material;
        public readonly List<Matrix4x4>[] lists = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
        public Matrix4x4[][] levels;   // all, half, quarter
    }
    private readonly List<Layer> layers = new List<Layer>();
    private Bounds bounds;
    private bool grown;
    private ProceduralWorld world;
    private float nextWorldSearch;

    // Shared flower meshes (one set for every tile).
    private static Mesh[] shapes;
    private static int lastGrowFrame = -1;
    private static bool growFreely;

    private static Mesh[] Shapes()
    {
        if (shapes == null || shapes[0] == null)
            // Short stems (heads sit low over the ground), so they never read like the tall-stemmed bubble lilies.
            shapes = new[] { FloraShapes.Daisy(12, stemHeight: 0.12f), FloraShapes.Daisy(18, 0.3f, 0.07f, 0.12f, 0.08f, 0.08f), FloraShapes.Daisy(8, 0.42f, 0.16f, 0.3f, 0.11f, 0.15f) };
        return shapes;
    }

    /// <summary>Grows every tile now, ignoring the one-per-frame budget (for review captures).</summary>
    public static void GrowAll()
    {
        growFreely = true;
        try { foreach (var tile in FindObjectsByType<FloraPatch>(FindObjectsSortMode.None)) tile.TryGrow(); }
        finally { growFreely = false; }
    }

    private void OnEnable() => RenderPipelineManager.beginCameraRendering += Draw;
    private void OnDisable() { RenderPipelineManager.beginCameraRendering -= Draw; Release(); }
    private void OnValidate() => Release();
    private void Release() { layers.Clear(); plants.Clear(); grown = false; }

    // ---------------- Layout ----------------

    private float Noise(Vector2 p, float offset, float freq) => Mathf.PerlinNoise(p.x * freq + 71.3f + offset, p.y * freq + 23.9f + offset * 0.7f);

    private void Layout(List<Vector2> lilies)
    {
        plants.Clear();
        var random = new System.Random(seed);
        float R() => (float)random.NextDouble();
        Vector2 middle = new Vector2(transform.position.x, transform.position.z);
        float f = 1f / driftSize;

        // Spatial hash of what is already standing (lilies first) so nothing overlaps.
        var heads = new Dictionary<Vector2Int, List<Vector3>>();   // x, z, reach
        Vector2Int CellOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));
        void Mark(Vector2 p, float reach)
        {
            var cell = CellOf(p);
            if (!heads.TryGetValue(cell, out var list)) heads[cell] = list = new List<Vector3>();
            list.Add(new Vector3(p.x, p.y, reach));
        }
        bool Free(Vector2 at, float reach)
        {
            var cell = CellOf(at);
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                if (!heads.TryGetValue(cell + new Vector2Int(dx, dz), out var list)) continue;
                foreach (Vector3 h in list)
                {
                    float gap = h.z + reach, ddx = h.x - at.x, ddz = h.y - at.y;
                    if (ddx * ddx + ddz * ddz < gap * gap) return false;
                }
            }
            return true;
        }
        foreach (Vector2 lily in lilies) Mark(lily, 0.38f);

        int toneCount = Mathf.Max(1, tones.Length);
        int bands = rareLastTone && toneCount > 1 ? toneCount - 1 : toneCount;
        int count = Mathf.RoundToInt(density * 4f * halfSize * halfSize);
        for (int n = 0; n < count; n++)
        {
            Vector2 at = middle + new Vector2(R() * 2f - 1f, R() * 2f - 1f) * halfSize;
            // Fill: drift noise minus the open-ground share, fading raggedly out at the meadow rim.
            float fill = Noise(at, 0f, f) + (Noise(at, 40f, f * 3f) - 0.5f) * 0.25f;
            float rim = new Vector2((at.x - meadowCentre.x) / meadowRadii.x, (at.y - meadowCentre.y) / meadowRadii.y).magnitude
                        + (Noise(at, 90f, 0.06f) - 0.5f) * 0.3f;
            fill -= Mathf.SmoothStep(0f, 1f, (rim - 0.72f) / 0.28f) * 1.2f;
            float core = (fill - openGround) / Mathf.Max(0.05f, 1f - openGround);
            if (core <= 0f || R() > Mathf.Clamp01(core * 3f)) continue;

            // Daisies in three builds; low at drift edges, tall in the cores.
            float species = Noise(at, 17f, f * 0.8f);
            float tallness = Mathf.Clamp01(core * 1.3f + (R() - 0.5f) * 0.5f);
            float h = Mathf.Lerp(0.55f, 1.15f, tallness) * scale;   // overall size (head and stem together)
            int variant = species < 0.33f ? 0 : species < 0.66f ? 1 : 2;
            if (R() < 0.3f) variant = random.Next(3);
            float reach = (variant == 2 ? 0.2f : 0.16f) * scale * Mathf.Clamp(h / scale, 0.6f, 1.2f);
            if (!Free(at, reach)) continue;

            // Colour bands from slow noise, shifted towards the cool end near coolToward.
            float cool = 1f - Mathf.Clamp01(Vector2.Distance(at, coolToward) / coolReach);
            // Big single-colour drifts (like real meadows and most stylised games: one hue per patch, never confetti);
            // only drift edges mix with the neighbouring colour, and a very few flowers take the accent.
            float band = Noise(at, 63f, f * 0.3f) * 0.7f + cool * 0.7f - 0.15f;
            float scaled = Mathf.Clamp(band * bands, 0f, bands - 0.001f);
            int tone = Mathf.FloorToInt(scaled);
            float within = scaled - tone;
            if (within > 0.8f && R() < (within - 0.8f) * 2.5f) tone = Mathf.Min(tone + 1, bands - 1);
            if (rareLastTone && toneCount > 1 && R() < 0.025f) tone = toneCount - 1;
            Mark(at, reach);
            plants.Add(new Plant { at = at, tone = tone, height = h, yaw = R() * 360f, reach = reach, variant = variant });
        }
    }

    // ---------------- Growing onto the terrain ----------------

    private bool TryGrow()
    {
        if (grown) return true;
        if (!growFreely && Application.isPlaying && Time.frameCount == lastGrowFrame) return false;
        if (world == null)
        {
            if (!growFreely && Time.realtimeSinceStartup < nextWorldSearch) return false;
            nextWorldSearch = Time.realtimeSinceStartup + 1f;
            world = FindFirstObjectByType<ProceduralWorld>();
        }
        if (world == null || !world.LayoutReady || tones.Length == 0 || stem == null) return false;
        lastGrowFrame = Time.frameCount;
        var timer = System.Diagnostics.Stopwatch.StartNew();

        var lilies = new List<Vector2>();
        var area = new Rect(transform.position.x - halfSize - 1f, transform.position.z - halfSize - 1f, halfSize * 2f + 2f, halfSize * 2f + 2f);
        foreach (var field in FindObjectsByType<LilyField>(FindObjectsSortMode.None)) field.CollectPositions(area, lilies);
        Layout(lilies);
        if (plants.Count == 0) { layers.Clear(); grown = true; GrowMilliseconds = 0f; return true; }   // outside the meadow

        layers.Clear();
        Mesh[] meshes = Shapes();
        var byKey = new Dictionary<(Mesh, int, Material), Layer>();
        void Add(Mesh mesh, int submesh, Material material, Matrix4x4 m, int level)
        {
            if (material == null || submesh >= mesh.subMeshCount) return;
            if (!byKey.TryGetValue((mesh, submesh, material), out Layer layer))
                byKey[(mesh, submesh, material)] = layer = new Layer { mesh = mesh, submesh = submesh, material = material };
            for (int l = 0; l <= level; l++) layer.lists[l].Add(m);
        }

        BuildHeights();
        bounds = new Bounds(transform.position, Vector3.one);
        bool first = true;
        for (int i = 0; i < plants.Count; i++)
        {
            Plant p = plants[i];
            Vector3 ground = Ground(p.at, out Vector3 normal);
            // Stems lean a little with the slope and a little at random, never stiffly upright.
            float leanX = Mathf.Sin(p.yaw * 1.7f) * 6f, leanZ = Mathf.Cos(p.yaw * 2.3f) * 6f;
            Quaternion lean = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, normal), 0.35f) * Quaternion.Euler(leanX, 0f, leanZ);
            var m = Matrix4x4.TRS(ground - Vector3.up * 0.01f, lean * Quaternion.Euler(0f, p.yaw, 0f), Vector3.one * p.height);
            int level = i % 4 == 0 ? 2 : i % 2 == 0 ? 1 : 0;   // which distance levels still draw this flower
            Mesh mesh = meshes[p.variant];
            Add(mesh, 0, stem, m, level);
            Add(mesh, 1, tones[Mathf.Clamp(p.tone, 0, tones.Length - 1)], m, level);
            Add(mesh, 2, centre != null ? centre : stem, m, level);
            if (first) { bounds = new Bounds(ground, Vector3.zero); first = false; }
            bounds.Encapsulate(ground + Vector3.up * p.height);
            bounds.Encapsulate(ground);
        }
        layers.AddRange(byKey.Values);
        foreach (Layer layer in layers) layer.levels = new[] { layer.lists[0].ToArray(), layer.lists[1].ToArray(), layer.lists[2].ToArray() };
        bounds.Expand(1f);
        heights = null;
        grown = true;
        GrowMilliseconds = (float)timer.Elapsed.TotalMilliseconds;
        return true;
    }

    // Terrain heights sampled once on a 0.5 m grid (searching down from just above the tile, so ground under a
    // sky island or overhang is found, not its top), then read bilinearly per flower.
    private float[,] heights;
    private Vector2 heightOrigin;
    private const float HeightStep = 0.5f;
    private static readonly RaycastHit[] rayHits = new RaycastHit[8];

    private void BuildHeights()
    {
        int n = Mathf.CeilToInt(halfSize * 2f / HeightStep) + 4;
        heights = new float[n, n];
        heightOrigin = new Vector2(transform.position.x, transform.position.z) - Vector2.one * (n - 1) * HeightStep * 0.5f;
        float from = transform.position.y + 30f;
        int mask = 1 << world.TerrainLayer;
        // Ray casts against the terrain chunk colliders are fast; the density-field search (TryFindSurfaceBelow)
        // took seconds per tile, so it is only the fallback where no chunk collider is hit.
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            float wx = heightOrigin.x + x * HeightStep, wz = heightOrigin.y + z * HeightStep;
            // Highest terrain hit below the ceiling (ground on slopes above the tile centre, never the sky island).
            int hits = Physics.RaycastNonAlloc(new Vector3(wx, from, wz), Vector3.down, rayHits, 120f, mask, QueryTriggerInteraction.Ignore);
            float best = float.MinValue;
            for (int k = 0; k < hits; k++) best = Mathf.Max(best, rayHits[k].point.y);
            heights[x, z] = best > float.MinValue ? best : WorldMapTerrain.Height(wx, wz);
        }
    }

    private float Height(float wx, float wz)
    {
        int n = heights.GetLength(0);
        float gx = Mathf.Clamp((wx - heightOrigin.x) / HeightStep, 0f, n - 1.001f), gz = Mathf.Clamp((wz - heightOrigin.y) / HeightStep, 0f, n - 1.001f);
        int x = (int)gx, z = (int)gz; float u = gx - x, v = gz - z;
        return Mathf.Lerp(Mathf.Lerp(heights[x, z], heights[x + 1, z], u), Mathf.Lerp(heights[x, z + 1], heights[x + 1, z + 1], u), v);
    }

    private Vector3 Ground(Vector2 at, out Vector3 normal)
    {
        const float e = 0.5f;
        normal = new Vector3(Height(at.x - e, at.y) - Height(at.x + e, at.y), 2f * e, Height(at.x, at.y - e) - Height(at.x, at.y + e)).normalized;
        return new Vector3(at.x, Height(at.x, at.y), at.y);
    }

    /// <summary>One line for review tools.</summary>
    public string Describe()
    {
        int instances = 0; foreach (Layer l in layers) instances += l.levels != null ? l.levels[0].Length : 0;
        return $"{name}: grown={grown} flowers={plants.Count} batches={layers.Count} instances={instances}";
    }

    public int FlowerCount => plants.Count;
    public bool Grown => grown;
    /// <summary>How long this tile took to grow (layout, terrain heights, instance lists).</summary>
    public float GrowMilliseconds { get; private set; }

    // ---------------- Drawing ----------------

    private void Draw(ScriptableRenderContext context, Camera camera)
    {
        if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection) return;
        // Every tile grows as soon as the terrain is ready (one tile a frame, during loading), never later
        // while the player walks, so there is no hitch on the way.
        Vector3 eye = camera.transform.position;
        if (!grown && !TryGrow()) return;
        float distance = Mathf.Sqrt(bounds.SqrDistance(eye));
        if (distance > drawDistance) return;
        int level = distance > drawDistance * 0.66f ? 2 : distance > drawDistance * 0.33f ? 1 : 0;
        int layer = gameObject.layer;
        if ((camera.cullingMask & (1 << layer)) == 0 && camera.cullingMask != 0)
            for (int l = 0; l < 32; l++) if ((camera.cullingMask & (1 << l)) != 0) { layer = l; break; }
        foreach (Layer each in layers)
        {
            Matrix4x4[] instances = each.levels[level];
            if (instances.Length == 0) continue;
            var settings = new RenderParams(each.material)
            { camera = camera, worldBounds = bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, layer = layer };
            for (int start = 0; start < instances.Length; start += 1000)
                Graphics.RenderMeshInstanced(settings, each.mesh, each.submesh, instances, Mathf.Min(1000, instances.Length - start), start);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.8f, 0.4f, 1f, 0.6f);
        Gizmos.DrawWireCube(transform.position, new Vector3(halfSize * 2f, 2f, halfSize * 2f));
    }
}
