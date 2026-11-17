using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A field of bubble lilies drawn with GPU instancing from a baked point list, where every flower can be picked
/// up: the lily under the crosshair (within pickup reach) is swapped for a real BubbleFlower from a small pool, so
/// the normal pickup prompt and pickup work, and only one or two real flowers exist at a time. A flower that is
/// carried off stays a real creature and leaves a gap; the others turn back into instances when not aimed at.
/// Point data: 6 floats per flower (x, y, z, yaw degrees, scale, tone) in a .bytes asset.
/// </summary>
[ExecuteAlways]
public sealed class LilyField : MonoBehaviour
{
    [SerializeField] private TextAsset data;
    [Tooltip("Low-poly lily: one submesh coloured by the tone materials' baked strips, or four submeshes (stem, outer petals, inner petals, bud).")]
    [SerializeField] private Mesh mesh;
    [SerializeField] private Material stem, inner, bud;
    [Tooltip("Material per tone (purple, violet, ice): the whole lily for a one-submesh mesh, else the outer petals.")]
    [SerializeField] private Material[] outer = new Material[0];
    [SerializeField] private BubbleFlower livePrefab;
    [Tooltip("Outer petal material per tone for the real flower that replaces the lily the player aims at.")]
    [SerializeField] private Material[] liveOuter = new Material[0];
    [Min(1)] [SerializeField] private int livePool = 32;
    [Min(4f)] [SerializeField] private float tileSize = 24f;
    [Header("Stem Height")]
    [Tooltip("Height of the lily's stem in the mesh (metres at scale 1).")]
    [Min(0.05f)] [SerializeField] private float stemLength = 0.5f;
    [Tooltip("Average share of the stem sunk into the ground (0 = full stems as modelled).")]
    [Range(0f, 0.9f)] [SerializeField] private float stemShorter = 0.35f;
    [Tooltip("Random spread of the stem height from flower to flower, as a share of the stem.")]
    [Range(0f, 0.6f)] [SerializeField] private float stemVariation = 0.3f;
    [Tooltip("Tiles further than this from the camera draw only every third lily.")]
    [Min(10f)] [SerializeField] private float thinBeyond = 70f;

    /// <summary>Hides the instanced lilies (for example while sculpting in the Scene view).</summary>
    public static bool Hidden;

    private const int Stride = 6, Batch = 1000;
    private static readonly Matrix4x4 Gone = Matrix4x4.Scale(Vector3.zero);
    private sealed class Tile
    {
        public Bounds bounds;
        public readonly List<int> ids = new List<int>();
        public Matrix4x4[] all;
        public Matrix4x4[][] tones;
        public Matrix4x4[][] far;   // every third lily, for distant tiles
    }
    private float[] points;
    private int count;
    private Matrix4x4[] matrices;
    private int[] tileOf, allIndex, toneIndex, farIndex;
    private readonly List<Tile> tiles = new List<Tile>();
    private readonly Dictionary<Vector2Int, List<int>> grid = new Dictionary<Vector2Int, List<int>>();
    private readonly Dictionary<int, BubbleFlower> live = new Dictionary<int, BubbleFlower>();
    private readonly HashSet<int> taken = new HashSet<int>();
    private readonly Stack<BubbleFlower> pool = new Stack<BubbleFlower>();
    private readonly Dictionary<BubbleFlower, int> toneOf = new Dictionary<BubbleFlower, int>();
    private int created;

    public int Count => count;

    private void OnEnable()
    {
        Load();
        RenderPipelineManager.beginCameraRendering += Draw;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= Draw;
        foreach (var flower in live.Values) if (flower != null) Destroy(flower.gameObject);
        live.Clear();
        while (pool.Count > 0) { var flower = pool.Pop(); if (flower != null) Destroy(flower.gameObject); }
    }

    private new static void Destroy(Object o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }

    /// <summary>Re-reads the point data (after the editor rebakes it).</summary>
    public void Load()
    {
        tiles.Clear(); grid.Clear(); count = 0;
        if (data == null || mesh == null) return;
        byte[] bytes = data.bytes;
        points = new float[bytes.Length / 4];
        System.Buffer.BlockCopy(bytes, 0, points, 0, points.Length * 4);
        count = points.Length / Stride;
        matrices = new Matrix4x4[count];
        tileOf = new int[count]; allIndex = new int[count]; toneIndex = new int[count]; farIndex = new int[count];
        var byKey = new Dictionary<Vector2Int, Tile>();
        Bounds local = mesh.bounds;
        for (int i = 0; i < count; i++)
        {
            Vector3 at = Position(i);
            matrices[i] = Matrix4x4.TRS(at + Vector3.down * Sink(i), Quaternion.Euler(0f, points[i * Stride + 3], 0f), Vector3.one * points[i * Stride + 4]);
            var key = new Vector2Int(Mathf.FloorToInt(at.x / tileSize), Mathf.FloorToInt(at.z / tileSize));
            if (!byKey.TryGetValue(key, out Tile tile))
            {
                tile = new Tile { bounds = new Bounds(at, Vector3.zero) };
                byKey[key] = tile; tiles.Add(tile);
            }
            tile.ids.Add(i);
            tile.bounds.Encapsulate(new Bounds(at + local.center, local.size * 1.6f));
            var cell = new Vector2Int(Mathf.FloorToInt(at.x / 4f), Mathf.FloorToInt(at.z / 4f));
            if (!grid.TryGetValue(cell, out var list)) grid[cell] = list = new List<int>();
            list.Add(i);
        }
        int tones = Mathf.Max(1, outer.Length);
        for (int t = 0; t < tiles.Count; t++)
        {
            Tile tile = tiles[t];
            tile.all = new Matrix4x4[tile.ids.Count];
            var perTone = new List<Matrix4x4>[tones];
            var farTone = new List<Matrix4x4>[tones];
            for (int k = 0; k < tones; k++) { perTone[k] = new List<Matrix4x4>(); farTone[k] = new List<Matrix4x4>(); }
            for (int n = 0; n < tile.ids.Count; n++)
            {
                int i = tile.ids[n], tone = Tone(i, tones);
                tileOf[i] = t; allIndex[i] = n; toneIndex[i] = perTone[tone].Count; farIndex[i] = -1;
                tile.all[n] = matrices[i]; perTone[tone].Add(matrices[i]);
                if (n % 3 == 0) { farIndex[i] = farTone[tone].Count; farTone[tone].Add(matrices[i]); }
            }
            tile.tones = new Matrix4x4[tones][];
            tile.far = new Matrix4x4[tones][];
            for (int k = 0; k < tones; k++) { tile.tones[k] = perTone[k].ToArray(); tile.far[k] = farTone[k].ToArray(); }
        }
    }

    /// <summary>How far a flower is sunk so its stem looks shorter (the head keeps its size). Varies per flower.</summary>
    private float Sink(int i)
    {
        float r = Mathf.Repeat(Mathf.Sin(i * 12.9898f + 78.233f) * 43758.5453f, 1f);
        float kept = Mathf.Clamp(1f - stemShorter + (r * 2f - 1f) * stemVariation, 0.1f, 1f);
        return stemLength * (1f - kept) * points[i * Stride + 4];
    }

    /// <summary>The area the flowers cover (world space), for things placed over the field.</summary>
    public bool TryFieldBounds(out Bounds bounds)
    {
        bounds = default;
        if (count == 0) Load();
        if (count == 0) return false;
        bounds = new Bounds(Position(0), Vector3.zero);
        for (int i = 1; i < count; i++) bounds.Encapsulate(Position(i));
        return true;
    }

    /// <summary>Adds the (x, z) of every lily inside an area, so other plants (FloraPatch) can grow round them.</summary>
    public void CollectPositions(Rect area, List<Vector2> into)
    {
        if (count == 0) Load();
        for (int cz = Mathf.FloorToInt(area.yMin / 4f); cz <= Mathf.FloorToInt(area.yMax / 4f); cz++)
        for (int cx = Mathf.FloorToInt(area.xMin / 4f); cx <= Mathf.FloorToInt(area.xMax / 4f); cx++)
        {
            if (!grid.TryGetValue(new Vector2Int(cx, cz), out var cell)) continue;
            foreach (int i in cell)
            {
                var p = new Vector2(points[i * Stride], points[i * Stride + 2]);
                if (area.Contains(p)) into.Add(p);
            }
        }
    }

    private Vector3 Position(int i) => new Vector3(points[i * Stride], points[i * Stride + 1], points[i * Stride + 2]);
    private int Tone(int i, int tones) => Mathf.Clamp(Mathf.RoundToInt(points[i * Stride + 5]), 0, tones - 1);

    private void Show(int i, bool visible)
    {
        Tile tile = tiles[tileOf[i]];
        Matrix4x4 m = visible ? matrices[i] : Gone;
        tile.all[allIndex[i]] = m;
        int tone = Tone(i, tile.tones.Length);
        tile.tones[tone][toneIndex[i]] = m;
        if (farIndex[i] >= 0) tile.far[tone][farIndex[i]] = m;
    }

    private void Draw(ScriptableRenderContext context, Camera camera)
    {
        if (Hidden || count == 0 || mesh == null) return;
        if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection) return;
        Vector3 eye = camera.transform.position;
        bool single = mesh.subMeshCount == 1;
        foreach (Tile tile in tiles)
        {
            bool thin = tile.bounds.SqrDistance(eye) > thinBeyond * thinBeyond;
            var byTone = thin ? tile.far : tile.tones;
            if (!single)
            {
                Submit(camera, tile, stem, 0, tile.all);
                if (inner != null) Submit(camera, tile, inner, 2, tile.all);
                if (bud != null) Submit(camera, tile, bud, 3, tile.all);
            }
            for (int k = 0; k < byTone.Length && k < outer.Length; k++)
                if (outer[k] != null && byTone[k].Length > 0) Submit(camera, tile, outer[k], single ? 0 : 1, byTone[k]);
        }
    }

    // Draw on this object's layer, or on a layer the camera does see when it is culled (review captures).
    private int DrawLayer(Camera camera)
    {
        int mask = camera.cullingMask;
        if ((mask & (1 << gameObject.layer)) != 0 || mask == 0) return gameObject.layer;
        for (int layer = 0; layer < 32; layer++) if ((mask & (1 << layer)) != 0) return layer;
        return gameObject.layer;
    }

    private void Submit(Camera camera, Tile tile, Material material, int submesh, Matrix4x4[] instances)
    {
        if (submesh >= mesh.subMeshCount) return;
        var settings = new RenderParams(material)
        {
            camera = camera, worldBounds = tile.bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, layer = DrawLayer(camera),
        };
        for (int start = 0; start < instances.Length; start += Batch)
            Graphics.RenderMeshInstanced(settings, mesh, submesh, instances, Mathf.Min(Batch, instances.Length - start), start);
    }

    // ---------------- Real flowers only where the player aims ----------------

    // A lily turns into a real, pickable bubble flower only while the crosshair rests on it within pickup reach
    // (the same centre-of-view ray the carrier uses), so the pickup prompt and pickup work as for any creature,
    // while only one or two real flowers exist at a time instead of a ring of them round the player.
    // Real flowers make no ambient bubbles here (ground emission is off), so nothing needs them earlier.
    private const int AimPool = 6;
    private const float AimReach = 5.5f, KeepSeconds = 1.5f;
    private readonly Dictionary<int, float> lastAimed = new Dictionary<int, float>();
    private readonly List<int> release = new List<int>();
    private Camera eye;

    private void Update()
    {
        if (!Application.isPlaying || livePrefab == null || count == 0) return;
        int poolSize = Mathf.Min(livePool, AimPool);
        // Fill the small pool one flower a frame from the start.
        if (created < poolSize) { var spare = Create(); if (spare != null) pool.Push(spare); }
        if (eye == null || !eye.isActiveAndEnabled) eye = Camera.main;
        if (eye == null) return;

        Ray ray = eye.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        int aimed = AimedLily(ray);
        if (aimed >= 0 && !taken.Contains(aimed))
        {
            lastAimed[aimed] = Time.time;
            if (!live.ContainsKey(aimed))
            {
                BubbleFlower flower = Take();
                if (flower != null) { Place(flower, aimed, Position(aimed)); live[aimed] = flower; Show(aimed, false); }
            }
        }

        // Flowers carried off stay real and leave a gap; the rest turn back into lilies once not aimed at for a moment.
        release.Clear();
        foreach (var pair in live)
        {
            BubbleFlower flower = pair.Value;
            if (flower == null || flower.IsCarried || (flower.transform.position - Position(pair.Key)).sqrMagnitude > 0.25f) { taken.Add(pair.Key); release.Add(pair.Key); continue; }
            if (!lastAimed.TryGetValue(pair.Key, out float at) || Time.time - at > KeepSeconds) release.Add(pair.Key);
        }
        foreach (int i in release)
        {
            BubbleFlower flower = live[i];
            live.Remove(i);
            lastAimed.Remove(i);
            if (taken.Contains(i))
            {
                // Picked up: show the whole stem again.
                Transform look = flower != null ? flower.transform.Find("Bubble lily") : null;
                if (look != null) look.localPosition = Vector3.zero;
                continue;
            }
            flower.gameObject.SetActive(false);
            pool.Push(flower);
            Show(i, true);
        }
    }

    // The lily whose head is nearest the view ray (within a forgiving radius) and within reach, or -1.
    private int AimedLily(Ray ray)
    {
        Vector3 a = ray.origin, b = ray.origin + ray.direction * AimReach;
        int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / 4f) - 1, x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / 4f) + 1;
        int z0 = Mathf.FloorToInt(Mathf.Min(a.z, b.z) / 4f) - 1, z1 = Mathf.FloorToInt(Mathf.Max(a.z, b.z) / 4f) + 1;
        float headUp = mesh != null ? mesh.bounds.center.y + mesh.bounds.extents.y * 0.5f : 0.8f;
        int best = -1; float bestScore = float.MaxValue;
        for (int cz = z0; cz <= z1; cz++)
        for (int cx = x0; cx <= x1; cx++)
        {
            if (!grid.TryGetValue(new Vector2Int(cx, cz), out var cell)) continue;
            foreach (int i in cell)
            {
                if (taken.Contains(i)) continue;
                float size = points[i * Stride + 4];
                Vector3 baseAt = Position(i), head = baseAt + Vector3.up * (headUp * size - Sink(i));
                // Test both the head and the middle of the stem, so aiming anywhere on the flower works.
                foreach (Vector3 p in new[] { head, (head + baseAt) * 0.5f })
                {
                    float along = Vector3.Dot(p - a, ray.direction);
                    if (along < 0.3f || along > AimReach) continue;
                    float off = (p - (a + ray.direction * along)).magnitude;
                    if (off > 0.45f * size) continue;
                    float score = off + along * 0.05f;
                    if (score < bestScore) { bestScore = score; best = i; }
                }
            }
        }
        return best;
    }

    private BubbleFlower Take() => pool.Count > 0 ? pool.Pop() : Create();

    private BubbleFlower Create()
    {
        if (created >= livePool) return null;
        created++;
        BubbleFlower flower = Instantiate(livePrefab, transform);
        flower.name = "Field bubble lily (live)";
        flower.ConfigureGroundEmission(false, new Vector2(10f, 20f));
        // Walk-through: it rests on the ground but does not bump the player or its neighbours.
        foreach (var collider in flower.GetComponentsInChildren<Collider>()) collider.excludeLayers = ~(1 << 7);
        flower.gameObject.SetActive(false);
        return flower;
    }

    private void Place(BubbleFlower flower, int i, Vector3 home)
    {
        Transform t = flower.transform;
        t.SetPositionAndRotation(home, Quaternion.Euler(0f, points[i * Stride + 3], 0f));
        t.localScale = Vector3.one * points[i * Stride + 4];
        if (t.TryGetComponent<Rigidbody>(out var body)) { body.position = home; body.rotation = t.rotation; body.linearVelocity = Vector3.zero; }
        int tone = Tone(i, Mathf.Max(1, outer.Length));
        var look = t.Find("Bubble lily");
        // The live flower stays on the ground (its body and collider); only the drawn lily sinks to match.
        if (look != null) look.localPosition = Vector3.down * (Sink(i) / Mathf.Max(0.01f, points[i * Stride + 4]));
        // Swapping materials allocates, so only when this pooled flower last showed another tone.
        if (look != null && tone < liveOuter.Length && liveOuter[tone] != null && (!toneOf.TryGetValue(flower, out int shown) || shown != tone))
        {
            toneOf[flower] = tone;
            var renderer = look.GetComponent<MeshRenderer>();
            var materials = renderer.sharedMaterials;
            if (materials.Length > 1) { materials[1] = liveOuter[tone]; renderer.sharedMaterials = materials; }
        }
        flower.gameObject.SetActive(true);
    }
}
