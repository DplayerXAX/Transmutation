using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The Ore Colossus's weak point: a giant ore mass grown onto its belly. Only erosive water wears it away,
/// and like the terrain it wears away where the water touches: the ore is a density volume (positive = solid)
/// that every contact carves, remeshed with marching cubes, with its collider following so water runs into
/// the hollows. Integrity still counts the total erosion taken; thresholds and breaking are reported to the
/// colossus, which acts on them in its own Tick.
/// Not a Creature: hits on it resolve to the colossus, which is invulnerable.
/// </summary>
[DisallowMultipleComponent]
public sealed class OreHeart : MonoBehaviour
{
    [Tooltip("Erosion the ore takes before it shatters.")]
    [Min(0.1f)] [SerializeField] private float maxIntegrity = 10f;
    [Tooltip("Shares of integrity left at which the colossus staggers.")]
    [SerializeField] private float[] staggerThresholds = { 0.75f, 0.5f, 0.25f };

    [Header("Carving")]
    [Tooltip("Radius of the hollow one contact carves, in metres.")]
    [Min(0.1f)] [SerializeField] private float carveRadius = 0.8f;
    [Tooltip("Metres of ore removed at the centre of a contact per unit of erosion.")]
    [Min(0f)] [SerializeField] private float carveDepth = 0.5f;
    [Tooltip("Shortest time between remeshes, and between collider updates, in seconds.")]
    [Min(0f)] [SerializeField] private float remeshInterval = 0.05f;
    [Min(0f)] [SerializeField] private float colliderInterval = 0.2f;
    [Tooltip("How fast the acid glow on fresh cuts fades, per second.")]
    [Min(0f)] [SerializeField] private float fizzFade = 1.2f;

    [Header("Runtime (Read Only)")]
    [SerializeField] private float integrity;
    [SerializeField] private bool broken;
    [SerializeField] private float carved;

    private static readonly Vector3Int[] CornerOffsets =
    {
        new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 1), new Vector3Int(0, 0, 1),
        new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0), new Vector3Int(1, 1, 1), new Vector3Int(0, 1, 1),
    };
    private static readonly int[,] EdgeConnections =
    {
        { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 }, { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
    };

    private OreColossus owner;
    private ErosionTarget target;
    private MeshCollider surface;
    private Renderer crystal;
    private Mesh mesh;
    private Material corrodingMaterial;
    private float flash, wakeTimer, remeshTimer, colliderTimer;
    private int nextThreshold;
    private ParticleSystem chips;
    private MaterialPropertyBlock block;
    private Color baseEmission = Color.black, baseColor = Color.white;
    private static readonly Collider[] nearby = new Collider[32];
    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    // Density grid in local space; sample (x, y, z) sits at origin + (x, y, z) * cell.
    private int nx, ny, nz;
    private float cell;
    private Vector3 origin;
    private float[] density, etched, fizz;
    private bool meshDirty, colliderDirty, fizzing;
    // Each vertex lies on a grid edge: its two samples and blend, so colours can refresh without remeshing.
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector3> normals = new List<Vector3>();
    private readonly List<int> triangles = new List<int>();
    private readonly List<int> edgeA = new List<int>(), edgeB = new List<int>();
    private readonly List<float> edgeT = new List<float>();
    private Color[] colors = new Color[0];

    public float Integrity => integrity;
    public float MaxIntegrity => maxIntegrity;
    public float Integrity01 => maxIntegrity > 0f ? Mathf.Clamp01(integrity / maxIntegrity) : 0f;
    public bool Broken => broken;
    public ErosionTarget Target => target;
    public Collider Collider => surface;
    /// <summary>Total density carved out by water so far (metres of depth summed over samples).</summary>
    public float Carved => carved;
    /// <summary>The current ore surface, in local space.</summary>
    public Mesh SurfaceMesh => mesh;
    /// <summary>Raised with the amount taken each time water wears the ore.</summary>
    public event System.Action<OreHeart, float> Eroded;

    /// <summary>Builds the ore below its parent (the belly), extending down by size.y. The object must still be inactive.</summary>
    internal void Initialize(OreColossus colossus, Vector3 size, float cellSize, float integrityMax, float sensitivity,
        int seed, Material heartMaterial)
    {
        owner = colossus;
        maxIntegrity = integrityMax; integrity = maxIntegrity; nextThreshold = 0; broken = false; carved = 0f;
        if (heartMaterial != null)
        {
            if (heartMaterial.HasProperty(EmissionId)) baseEmission = heartMaterial.GetColor(EmissionId);
            if (heartMaterial.HasProperty(BaseColorId)) baseColor = heartMaterial.GetColor(BaseColorId);
        }
        block = new MaterialPropertyBlock();

        Fill(size, Mathf.Max(0.08f, cellSize), seed);
        mesh = new Mesh { name = "Ore Heart (carved)", hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
        mesh.MarkDynamic();
        Remesh();
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        crystal = gameObject.AddComponent<MeshRenderer>();
        crystal.shadowCastingMode = ShadowCastingMode.On;
        Shader corrodingShader = Shader.Find("Capstone/Creature/CorrodingCrystal");
        if (corrodingShader != null)
        {
            corrodingMaterial = new Material(corrodingShader) { name = "Ore Heart Corroding (runtime)", hideFlags = HideFlags.DontSave };
            corrodingMaterial.SetColor(BaseColorId, baseColor);
            corrodingMaterial.SetColor(EmissionId, baseEmission);
        }
        crystal.sharedMaterial = corrodingMaterial != null ? corrodingMaterial : heartMaterial;
        // A concave collider is fine here: the colossus body is kinematic, and water measures overlap with
        // ComputePenetration, which handles concave meshes (the terrain is the same).
        surface = gameObject.AddComponent<MeshCollider>();
        surface.sharedMesh = mesh;
        target = gameObject.AddComponent<ErosionTarget>();
        target.Configure(new Collider[] { surface }, sensitivity, 1f, 0.9f, 0.15f);
        target.Eroded += OnTargetEroded;

        var particles = new GameObject("Heart Chips").transform;
        particles.SetParent(transform, false);
        chips = particles.gameObject.AddComponent<ParticleSystem>();
        var main = chips.main; main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 128; main.startLifetime = 0.9f; main.startSize = 0.1f; main.startSpeed = 1.2f; main.gravityModifier = 1f;
        main.startColor = baseEmission.maxColorComponent > 0.01f ? (Color)baseEmission : new Color(0.55f, 0.9f, 0.85f);
        var emission = chips.emission; emission.enabled = false;
        var shape = chips.shape; shape.enabled = false;
        var particleRenderer = chips.GetComponent<ParticleSystemRenderer>();
        particleRenderer.sharedMaterial = heartMaterial;
        particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    // ---------------- Shape ----------------

    private struct Spike { public Vector3 start, axis, u, v; public float length, radius; }

    /// <summary>A rough ore core hanging from the top, bristling with big crystals that point down and out.</summary>
    private void Fill(Vector3 size, float cellSize, int seed)
    {
        cell = cellSize;
        // A metre of room each side for crystals leaning out past the core.
        nx = Mathf.CeilToInt((size.x + 2f) / cell) + 3;
        ny = Mathf.CeilToInt(size.y / cell) + 3;
        nz = Mathf.CeilToInt((size.z + 2f) / cell) + 3;
        // Top samples sit a little above the parent so the seam stays hidden in the belly.
        origin = new Vector3(-(nx - 1) * cell * 0.5f, 0.35f - (ny - 1) * cell, -(nz - 1) * cell * 0.5f);
        density = new float[nx * ny * nz];
        etched = new float[density.Length];
        fizz = new float[density.Length];

        var random = new System.Random(seed);
        float Range(float a, float b) => a + (float)random.NextDouble() * (b - a);
        var coreCentre = new Vector3(0f, -size.y * 0.2f, 0f);
        var coreRadii = new Vector3(size.x * 0.34f, size.y * 0.21f, size.z * 0.36f);
        var spikes = new List<Spike>();
        void AddSpike(Vector3 direction, float length, float radius)
        {
            Vector3 axis = direction.normalized;
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            float twist = Range(0f, Mathf.PI);
            u = Quaternion.AngleAxis(twist * Mathf.Rad2Deg, axis) * u;
            spikes.Add(new Spike { start = coreCentre + Vector3.Scale(axis, coreRadii) * 0.2f, axis = axis, u = u,
                v = Vector3.Cross(axis, u), length = length, radius = radius });
        }
        float lowest = size.y - 0.35f - (-coreCentre.y);
        AddSpike(Vector3.down + new Vector3(Range(-0.1f, 0.1f), 0f, Range(-0.1f, 0.1f)), lowest, size.x * 0.15f);
        for (int i = 0; i < 9; i++)
        {
            float angle = (i + Range(-0.3f, 0.3f)) * Mathf.PI * 2f / 9f;
            var outward = new Vector3(Mathf.Cos(angle) * size.x, 0f, Mathf.Sin(angle) * size.z).normalized;
            Vector3 direction = outward * Range(0.3f, 0.7f) + Vector3.down;
            AddSpike(direction, lowest * Range(0.55f, 0.85f), size.x * Range(0.08f, 0.12f));
        }

        for (int z = 0; z < nz; z++) for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
        {
            int i = Index(x, y, z);
            if (x == 0 || y == 0 || z == 0 || x == nx - 1 || y == ny - 1 || z == nz - 1) { density[i] = -1f; continue; }
            Vector3 p = origin + new Vector3(x, y, z) * cell;
            float d = Ellipsoid(p - coreCentre, coreRadii) + (Hash(p * 1.7f) - 0.5f) * 0.12f;
            foreach (Spike spike in spikes) d = SmoothMin(d, Crystal(p, spike), 0.12f);
            density[i] = Mathf.Clamp(-d, -1f, 4f);
        }
    }

    private int Index(int x, int y, int z) => (z * ny + y) * nx + x;

    private static float Ellipsoid(Vector3 p, Vector3 r)
    {
        float k0 = new Vector3(p.x / r.x, p.y / r.y, p.z / r.z).magnitude;
        float k1 = new Vector3(p.x / (r.x * r.x), p.y / (r.y * r.y), p.z / (r.z * r.z)).magnitude;
        return k1 > 1e-6f ? k0 * (k0 - 1f) / k1 : -Mathf.Min(r.x, Mathf.Min(r.y, r.z));
    }

    /// <summary>Hexagonal prism along the axis with a pyramid tip (approximate distance).</summary>
    private static float Crystal(Vector3 p, Spike s)
    {
        Vector3 q = p - s.start;
        float h = Vector3.Dot(q, s.axis);
        float a = Mathf.Abs(Vector3.Dot(q, s.u)), b = Mathf.Abs(Vector3.Dot(q, s.v));
        float shaft = s.length * 0.65f;
        float radius = h < shaft ? s.radius : s.radius * Mathf.Max(0f, (s.length - h) / (s.length - shaft));
        float hex = Mathf.Max(a * 0.866f + b * 0.5f, b) - radius;
        return Mathf.Max(hex, Mathf.Max(-h, h - s.length));
    }

    private static float SmoothMin(float a, float b, float k)
    {
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
        return Mathf.Lerp(b, a, h) - k * h * (1f - h);
    }

    private static float Hash(Vector3 p)
    {
        float h = Mathf.Sin(Vector3.Dot(new Vector3(Mathf.Floor(p.x), Mathf.Floor(p.y), Mathf.Floor(p.z)), new Vector3(127.1f, 311.7f, 74.7f))) * 43758.5453f;
        return h - Mathf.Floor(h);
    }

    // ---------------- Carving and meshing ----------------

    /// <summary>Carves a hollow at a world point, as water wearing it by amount would.</summary>
    internal void Carve(Vector3 worldPoint, float amount)
    {
        if (density == null || amount <= 0f) return;
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        float depth = amount * carveDepth, r = carveRadius;
        Vector3Int min = Vector3Int.Max(Vector3Int.one, Vector3Int.FloorToInt((local - Vector3.one * r - origin) / cell));
        Vector3Int max = Vector3Int.Min(new Vector3Int(nx - 2, ny - 2, nz - 2), Vector3Int.CeilToInt((local + Vector3.one * r - origin) / cell));
        for (int z = min.z; z <= max.z; z++) for (int y = min.y; y <= max.y; y++) for (int x = min.x; x <= max.x; x++)
        {
            float t = Vector3.Distance(local, origin + new Vector3(x, y, z) * cell) / r;
            if (t >= 1f) continue;
            float w = 1f - t * t; w *= w;
            int i = Index(x, y, z);
            float before = density[i];
            density[i] = Mathf.Max(-1f, before - depth * w);
            carved += before - density[i];
            etched[i] = Mathf.Min(1f, etched[i] + depth * w * 2.5f);
            fizz[i] = Mathf.Min(1f, fizz[i] + amount * 8f * w);
        }
        meshDirty = colliderDirty = fizzing = true;
    }

    private void Remesh()
    {
        vertices.Clear(); normals.Clear(); edgeA.Clear(); edgeB.Clear(); edgeT.Clear(); triangles.Clear();
        var corner = new int[8];
        var at = new Vector3[12];
        var outward = new Vector3[12];
        var ea = new int[12]; var eb = new int[12]; var et = new float[12];
        for (int z = 0; z < nz - 1; z++) for (int y = 0; y < ny - 1; y++) for (int x = 0; x < nx - 1; x++)
        {
            int cube = 0;
            for (int c = 0; c < 8; c++)
            {
                Vector3Int o = CornerOffsets[c];
                corner[c] = Index(x + o.x, y + o.y, z + o.z);
                if (density[corner[c]] > 0f) cube |= 1 << c;
            }
            int mask = MarchingCubesTables.EdgeTable[cube];
            if (mask == 0) continue;
            for (int e = 0; e < 12; e++)
            {
                if ((mask & (1 << e)) == 0) continue;
                int ca = EdgeConnections[e, 0], cb = EdgeConnections[e, 1];
                int a = corner[ca], b = corner[cb];
                float da = density[a], db = density[b];
                float t = Mathf.Abs(da - db) < 1e-6f ? 0.5f : Mathf.Clamp01(da / (da - db));
                Vector3 pa = origin + (Vector3)(new Vector3Int(x, y, z) + CornerOffsets[ca]) * cell;
                Vector3 pb = origin + (Vector3)(new Vector3Int(x, y, z) + CornerOffsets[cb]) * cell;
                at[e] = Vector3.Lerp(pa, pb, t);
                outward[e] = da > 0f ? pb - pa : pa - pb;
                ea[e] = a; eb[e] = b; et[e] = t;
            }
            for (int k = 0; MarchingCubesTables.TriTable[cube, k] != -1; k += 3)
            {
                int e0 = MarchingCubesTables.TriTable[cube, k], e1 = MarchingCubesTables.TriTable[cube, k + 1], e2 = MarchingCubesTables.TriTable[cube, k + 2];
                Vector3 normal = Vector3.Cross(at[e1] - at[e0], at[e2] - at[e0]);
                if (normal.sqrMagnitude < 1e-12f) continue;
                if (Vector3.Dot(normal, outward[e0] + outward[e1] + outward[e2]) < 0f) { int swap = e1; e1 = e2; e2 = swap; normal = -normal; }
                normal.Normalize();
                // Each triangle has its own vertices: flat facets, like the rest of the colossus.
                AddVertex(at[e0], normal, ea[e0], eb[e0], et[e0]);
                AddVertex(at[e1], normal, ea[e1], eb[e1], et[e1]);
                AddVertex(at[e2], normal, ea[e2], eb[e2], et[e2]);
            }
        }
        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        if (colors.Length != vertices.Count) colors = new Color[vertices.Count];
        RefreshColors();
        meshDirty = false;
    }

    private void AddVertex(Vector3 position, Vector3 normal, int a, int b, float t)
    {
        triangles.Add(vertices.Count);
        vertices.Add(position); normals.Add(normal);
        edgeA.Add(a); edgeB.Add(b); edgeT.Add(t);
    }

    /// <summary>Vertex colour for the CorrodingCrystal shader: r = how eaten, g = fresh-cut fizz.</summary>
    private void RefreshColors()
    {
        for (int v = 0; v < colors.Length; v++)
        {
            int a = edgeA[v], b = edgeB[v]; float t = edgeT[v];
            colors[v] = new Color(Mathf.Lerp(etched[a], etched[b], t), Mathf.Lerp(fizz[a], fizz[b], t), 0f, 1f);
        }
        mesh.colors = colors;
    }

    private void LateUpdate()
    {
        if (mesh == null || broken) return;
        float deltaTime = Time.deltaTime;
        remeshTimer -= deltaTime; colliderTimer -= deltaTime;
        bool refresh = false;
        if (fizzing)
        {
            float fade = fizzFade * deltaTime;
            bool any = false;
            for (int i = 0; i < fizz.Length; i++)
                if (fizz[i] > 0f) { fizz[i] = Mathf.Max(0f, fizz[i] - fade); any |= fizz[i] > 0f; }
            fizzing = any;
            refresh = true;
        }
        if (meshDirty && remeshTimer <= 0f) { remeshTimer = remeshInterval; Remesh(); }
        else if (refresh) RefreshColors();
        if (colliderDirty && !meshDirty && colliderTimer <= 0f) UpdateCollider();
    }

    private void UpdateCollider()
    {
        colliderTimer = colliderInterval;
        colliderDirty = false;
        // Reassigning makes PhysX cook the new shape.
        surface.sharedMesh = null;
        surface.sharedMesh = mesh;
        Physics.SyncTransforms();
    }

    /// <summary>Applies pending carving to the mesh and collider at once (validation and debug).</summary>
    internal void Rebuild()
    {
        if (mesh == null) return;
        Remesh();
        UpdateCollider();
    }

    // ---------------- Erosion, glow and breaking ----------------

    private void OnTargetEroded(ErosionTarget source, Vector3 point, float amount)
    {
        if (broken || amount <= 0f) return;
        integrity = Mathf.Max(0f, integrity - amount);
        flash = Mathf.Min(1f, flash + amount * 2f + 0.15f);
        Carve(point, amount);
        if (chips != null)
        {
            Vector3 normal = (point - transform.position).sqrMagnitude > 1e-6f ? (point - transform.position).normalized : Vector3.down;
            var emit = new ParticleSystem.EmitParams { position = point, velocity = normal * 1.2f + Random.insideUnitSphere * 0.5f };
            if (!chips.isPlaying) chips.Play();
            chips.Emit(emit, Mathf.Clamp(Mathf.CeilToInt(amount * 12f), 1, 6));
        }
        Eroded?.Invoke(this, amount);
        // Erosion arrives inside the fluid system's physics loop: only report, the colossus acts in its Tick.
        while (nextThreshold < staggerThresholds.Length && Integrity01 <= staggerThresholds[nextThreshold])
        {
            nextThreshold++;
            if (integrity > 0f && owner != null) owner.OnHeartThreshold();
        }
        if (integrity <= 0f && owner != null) owner.OnHeartBroken();
    }

    /// <summary>One frame of glow, and keeps resting water awake against the moving ore.</summary>
    internal void Tick(float deltaTime)
    {
        if (broken || deltaTime <= 0f) return;
        flash = Mathf.MoveTowards(flash, 0f, deltaTime * 3f);
        UpdateGlow();
        // Teleported kinematic contacts do not wake sleeping bodies; water resting on the ore must keep eroding.
        wakeTimer -= deltaTime;
        if (wakeTimer <= 0f && crystal != null)
        {
            wakeTimer = 0.1f;
            Bounds bounds = crystal.bounds;
            int count = gameObject.scene.GetPhysicsScene().OverlapBox(bounds.center, bounds.extents + Vector3.one * 0.6f, nearby,
                Quaternion.identity, 1 << 4, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) if (nearby[i].attachedRigidbody != null && nearby[i].attachedRigidbody.IsSleeping()) nearby[i].attachedRigidbody.WakeUp();
        }
    }

    /// <summary>Debug and validation: wears the ore down to nothing.</summary>
    internal void ForceBreak()
    {
        if (broken) return;
        integrity = 0f;
        nextThreshold = staggerThresholds.Length;
        if (owner != null) owner.OnHeartBroken();
    }

    private void UpdateGlow()
    {
        if (crystal == null) return;
        float health = Integrity01;
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * Mathf.Lerp(6f, 1.5f, health));
        crystal.GetPropertyBlock(block);
        block.SetColor(EmissionId, baseEmission * (Mathf.Lerp(0.25f, 1f, health) * pulse + flash * 2.5f));
        block.SetColor(BaseColorId, Color.Lerp(baseColor * 0.35f, baseColor, health));
        crystal.SetPropertyBlock(block);
    }

    /// <summary>Bursts into shards scattered through what is left of the ore; it stops taking water.</summary>
    internal void Shatter(Mesh shardMesh, Material shardMaterial)
    {
        if (broken) return;
        broken = true;
        integrity = 0f;
        Bounds bounds = mesh != null ? mesh.bounds : new Bounds(Vector3.zero, Vector3.one);
        Vector3 centre = transform.TransformPoint(bounds.center);
        var random = new System.Random(GetInstanceID());
        for (int i = 0; i < 16; i++)
        {
            var local = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f);
            Vector3 at = transform.TransformPoint(bounds.center + Vector3.Scale(local, bounds.size) * 0.7f);
            Vector3 dir = (at - centre).sqrMagnitude > 1e-4f ? (at - centre).normalized : Random.onUnitSphere;
            var shard = new GameObject("Heart Shard");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(shard, gameObject.scene);
            shard.transform.SetPositionAndRotation(at, Quaternion.LookRotation(dir));
            shard.transform.localScale = Vector3.one * (0.9f + (float)random.NextDouble() * 1.1f);
            shard.AddComponent<MeshFilter>().sharedMesh = shardMesh;
            shard.AddComponent<MeshRenderer>().sharedMaterial = shardMaterial;
            var collider = shard.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.4f, 0.4f, 0.8f);
            collider.excludeLayers = 1 << 6;
            var body = shard.AddComponent<Rigidbody>();
            body.mass = 1.5f;
            body.linearVelocity = dir * (2f + (float)random.NextDouble() * 3f);
            body.angularVelocity = Random.insideUnitSphere * 4f;
            Destroy(shard, 8f);
        }
        if (chips != null)
        {
            chips.transform.SetParent(null, true);
            chips.transform.position = centre;
            if (!chips.isPlaying) chips.Play();
            chips.Emit(80);
            Destroy(chips.gameObject, 2f);
        }
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (target != null) target.Eroded -= OnTargetEroded;
        if (mesh != null) Destroy(mesh);
        if (corrodingMaterial != null) Destroy(corrodingMaterial);
    }
}
