using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Grows procedural decorations on the ProceduralWorld chunks as they are built:
/// stone shards, arches and cairns plus soft breathing polyps, sacs and creepers on the surface,
/// and colonies of dim hard-shelled tentacles on the floors, walls and ceilings of the inner world.
///
/// Pieces gather in patches and clusters so the land has bare and crowded areas.
/// All pieces of one chunk are merged into one mesh per material, parented to the chunk.
/// In Play Mode the placement runs on a worker thread and colliders are baked off the main thread,
/// so new chunks never stall a frame; finished chunks are turned into objects a few per frame.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(ProceduralWorld))]
public sealed class WorldDecorator : MonoBehaviour
{
    public enum Group
    {
        Stone,
        Organic,
        Inner,
        InnerGlow,
    }

    [System.Serializable]
    public sealed class Kind
    {
        public string name;
        public DecorShapes.Shape shape;
        public Group group;
        [Tooltip("Grows in the inner world instead of on the surface.")]
        public bool inner;
        [Min(0f)] public float clustersPer1000m2 = 0.5f;
        public Vector2Int membersPerCluster = new Vector2Int(1, 3);
        [Min(0f)] public float clusterRadius = 4f;
        public Vector2 scaleRange = new Vector2(0.8f, 1.4f);
        [Tooltip("Range of ground normal.y this kind grows on (1 = flat floor, -1 = ceiling).")]
        public Vector2 normalYRange = new Vector2(0.6f, 1f);
        [Tooltip("0 = grows straight up (or down from ceilings), 1 = grows along the ground normal.")]
        [Range(0f, 1f)] public float alignToNormal = 0.3f;
        [Tooltip("How far the base is pushed into the ground, as a fraction of the scale.")]
        public float sink = 0.1f;
        [Tooltip("Size in metres of the patches this kind gathers in.")]
        [Min(1f)] public float patchSize = 60f;
        [Tooltip("Share of the land the patches cover.")]
        [Range(0f, 1f)] public float patchCoverage = 0.5f;
        [Tooltip("Every piece of a cluster has the same shape and a similar size.")]
        public bool uniformClusters;
    }

    [Header("Materials")]
    [SerializeField] private Material stoneMaterial;
    [SerializeField] private Material organicMaterial;
    [SerializeField] private Material innerMaterial;
    [Tooltip("Faintly glowing inner growths: crystals and sacs.")]
    [SerializeField] private Material innerGlowMaterial;

    [Header("Placement")]
    [SerializeField] private int seed = 7;
    [Range(1, 16)] [SerializeField] private int variantsPerShape = 8;
    [Tooltip("No surface decorations this close to the spawn point.")]
    [Min(0f)] [SerializeField] private float spawnClearRadius = 6f;
    [Range(0f, 1f)] [SerializeField] private float density = 0.5f;
    [Range(0.2f, 1f)] [SerializeField] private float clusterFullness = 0.6f;
    [SerializeField] private List<Kind> kinds = DefaultKinds();

    [Header("Physics")]
    [SerializeField] private bool stoneColliders = true;
    [Tooltip("Collision for inner growths and crystals, merged per chunk.")]
    [SerializeField] private bool innerColliders = true;
    [SerializeField] private bool organicColliders = true;
    [Tooltip("7 = Ground, so the player can stand and jump on stones.")]
    [SerializeField] private int decorLayer = 7;

    [Header("Loading")]
    [Tooltip("Decorated chunks turned into objects per frame.")]
    [Range(1, 16)] [SerializeField] private int chunksPerFrame = 2;
    [Range(0.5f, 8f)] [SerializeField] private float updateBudgetMilliseconds = 2.5f;
    public float LastUpdateMilliseconds { get; private set; }

    [Header("Runtime State (Read Only)")]
    [SerializeField] private int piecesPlaced;
    [SerializeField] private int chunksWaiting;

    private sealed class Buffer
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<int> triangles = new List<int>();
        public readonly List<(Bounds bounds, int start, int count)> pieces = new List<(Bounds, int, int)>();
    }

    /// <summary>World-space triangles of one chunk sub-mesh, for area-weighted sampling.</summary>
    internal sealed class Ground
    {
        public Vector3[] a, b, c, normal, centre;
        public float[] cumulativeArea;
        public float totalArea;
        public int Count => a.Length;
    }

    /// <summary>Everything a worker needs to decorate one chunk; no Unity objects inside.</summary>
    private sealed class Job
    {
        public GameObject chunk;
        public Matrix4x4 localToWorld, worldToLocal;
        public Vector3[] vertices, normals;
        public int[][] triangles;
        public int randomSeed;
        public Vector3 spawn;
        public Kind[] kinds;
        public RealmDecorLayout.Snapshot layout;
        public Dictionary<Group, Buffer> result;
        public int pieces;
        public int version;
    }

    /// <summary>A decor mesh waiting for its collider to finish baking on a worker.</summary>
    private sealed class PendingCollider
    {
        public GameObject decor;
        public Mesh mesh;
        public Task bake;
        public bool retired;
        public System.Exception error;
    }
    private sealed class Upload
    {
        public GameObject chunk;
        public int version;
        public Group group;
        public Buffer buffer;
    }

    // Same options as the default MeshCollider, so the baked data is reused when assigned.
    private const MeshColliderCookingOptions Cooking = MeshColliderCookingOptions.CookForFasterSimulation |
        MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices |
        MeshColliderCookingOptions.UseFastMidphase;

    private readonly ConcurrentQueue<Job> finishedJobs = new ConcurrentQueue<Job>();
    private readonly List<PendingCollider> pendingColliders = new List<PendingCollider>();
    private readonly Dictionary<DecorShapes.Shape, DecorMesh[]> variants = new Dictionary<DecorShapes.Shape, DecorMesh[]>();
    private readonly List<(GameObject chunk, Mesh mesh)> built = new List<(GameObject, Mesh)>();
    private readonly Queue<Upload> uploads = new Queue<Upload>();
    private readonly Dictionary<GameObject, int> chunkVersions = new Dictionary<GameObject, int>();
    private Bounds[] boneClearance;
    private ProceduralWorld world;
    private RealmDecorLayout.Snapshot layout;
    private bool layoutFinal;
    private readonly Queue<GameObject> layoutRefresh = new Queue<GameObject>();

    private void Reset() => kinds = DefaultKinds();

    private void OnEnable()
    {
        world = GetComponent<ProceduralWorld>();
        world.ChunkCreated += Decorate;
        world.ChunkUpdated += Redecorate;
    }

    private void OnDisable()
    {
        foreach (PendingCollider pending in pendingColliders) pending.bake?.Wait();
        foreach (PendingCollider pending in pendingColliders) if (pending.retired) DestroySafe(pending.mesh);
        if (world != null) world.ChunkCreated -= Decorate;
        if (world != null) world.ChunkUpdated -= Redecorate;
        foreach (var entry in built)
        {
            if (entry.chunk != null)
                for (int i = entry.chunk.transform.childCount - 1; i >= 0; i--)
                {
                    GameObject child = entry.chunk.transform.GetChild(i).gameObject;
                    if (child.name.StartsWith("Decor ")) DestroySafe(child);
                }
            DestroySafe(entry.mesh);
        }
        built.Clear();
        uploads.Clear();
        chunkVersions.Clear();
        boneClearance = null;
        layoutFinal = false;
        layout = default;
        layoutRefresh.Clear();
        variants.Clear();
        while (finishedJobs.TryDequeue(out _)) { }
        pendingColliders.Clear();
        chunksWaiting = 0;
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        bool Budget() => (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 /
            System.Diagnostics.Stopwatch.Frequency < updateBudgetMilliseconds;
        if (!layoutFinal)
        {
            var candidate = RealmDecorLayout.Capture(world);
            var kiln = FindFirstObjectByType<BoneKiln>();
            if ((candidate.active || FindFirstObjectByType<HangingSpire>() == null) && (kiln == null || kiln.Root != null))
            {
                layout = candidate;
                layoutFinal = true;
                // Chunks can arrive before the tower. Replace those early jobs once district centres exist.
                foreach (GameObject chunk in new List<GameObject>(chunkVersions.Keys))
                    if (chunk != null) layoutRefresh.Enqueue(chunk);
            }
        }
        for (int i = 0; i < chunksPerFrame && layoutRefresh.Count > 0 && Budget(); i++)
        {
            GameObject chunk = layoutRefresh.Dequeue();
            if (chunk != null && chunk.TryGetComponent<MeshFilter>(out var filter)) Redecorate(chunk, filter.sharedMesh, default);
        }
        for (int i = 0; i < chunksPerFrame && Budget() && finishedJobs.TryDequeue(out Job job); i++)
        {
            chunksWaiting--;
            if (job.chunk == null || !chunkVersions.TryGetValue(job.chunk, out int version) || version != job.version) continue;
            piecesPlaced += job.pieces;
            foreach (var pair in job.result)
                if (pair.Value.vertices.Count > 0)
                    uploads.Enqueue(new Upload { chunk = job.chunk, version = job.version, group = pair.Key, buffer = pair.Value });
        }
        // Attach colliders once their meshes are baked; assigning a baked mesh is cheap.
        int attached = 0;
        for (int i = pendingColliders.Count - 1; i >= 0 && attached < chunksPerFrame && Budget(); i--)
        {
            PendingCollider pending = pendingColliders[i];
            if (!pending.bake.IsCompleted) continue;
            pendingColliders.RemoveAt(i);
            attached++;
            if (pending.retired || pending.decor == null || pending.error != null)
            {
                if (pending.error != null) Debug.LogException(pending.error, this);
                DestroySafe(pending.decor); DestroySafe(pending.mesh);
                continue;
            }
            if (pending.mesh == null) continue;
            var meshCollider = pending.decor.AddComponent<MeshCollider>();
            meshCollider.cookingOptions = Cooking;
            meshCollider.sharedMesh = pending.mesh;
            pending.decor.GetComponent<MeshRenderer>().enabled = true;
        }
        for (int i = 0; i < chunksPerFrame && uploads.Count > 0 && Budget(); i++)
        {
            Upload upload = uploads.Dequeue();
            if (upload.chunk != null && chunkVersions.TryGetValue(upload.chunk, out int version) && version == upload.version)
                CreateDecorObject(upload.chunk, upload.group, upload.buffer);
        }
        // Late landmark placement already queues chunk replacement above. Never mutate and re-cook
        // every published collider in a single frame just to clear the resting skeleton.
        LastUpdateMilliseconds = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 /
            System.Diagnostics.Stopwatch.Frequency);
    }

    public void SetMaterials(Material stone, Material organic, Material innerGrowth, Material innerGlow)
    {
        innerGlowMaterial = innerGlow;
        stoneMaterial = stone;
        organicMaterial = organic;
        innerMaterial = innerGrowth;
    }

    // ---------------- Decorating one chunk ----------------

    private void Redecorate(GameObject chunk, Mesh mesh, Bounds changed)
    {
        // A sculpted/rebuilt terrain chunk needs new placements; old decorations otherwise hang or get buried.
        for (int i = pendingColliders.Count - 1; i >= 0; i--)
        {
            PendingCollider pending = pendingColliders[i];
            if (pending.decor == null || pending.decor.transform.parent != chunk.transform) continue;
            // Leave the native mesh alive until its worker finishes, without waiting on gameplay.
            pending.retired = true;
        }
        for (int i = chunk.transform.childCount - 1; i >= 0; i--)
            if (chunk.transform.GetChild(i).name.StartsWith("Decor ")) DestroySafe(chunk.transform.GetChild(i).gameObject);
        for (int i = built.Count - 1; i >= 0; i--)
        {
            if (built[i].chunk != chunk) continue;
            RetireMesh(built[i].mesh);
            built.RemoveAt(i);
        }
        Decorate(chunk, mesh);
    }

    private void Decorate(GameObject chunk, Mesh chunkMesh)
    {
        PruneDestroyedChunks();
        if (chunkMesh == null)
        {
            chunkVersions.TryGetValue(chunk, out int previous);
            chunkVersions[chunk] = previous + 1;
            return;
        }
        chunkVersions.TryGetValue(chunk, out int version);
        chunkVersions[chunk] = ++version;

        Vector3 key = chunk.transform.localPosition;
        var job = new Job
        {
            chunk = chunk,
            localToWorld = chunk.transform.localToWorldMatrix,
            worldToLocal = chunk.transform.worldToLocalMatrix,
            vertices = chunkMesh.vertices,
            normals = chunkMesh.normals,
            triangles = new int[Mathf.Min(2, chunkMesh.subMeshCount)][],
            randomSeed = unchecked(seed * 73856093 ^ Mathf.RoundToInt(key.x) * 19349663 ^ Mathf.RoundToInt(key.y) * 83492791 ^ Mathf.RoundToInt(key.z) * 2971215),
            spawn = transform.position,
            kinds = kinds.ToArray(),
            layout = layout,
            version = version,
        };
        for (int subMesh = 0; subMesh < job.triangles.Length; subMesh++) job.triangles[subMesh] = chunkMesh.GetTriangles(subMesh);
        // Shapes are built here on the main thread; workers only read them.
        foreach (Kind kind in job.kinds) GetVariants(kind.shape);

        if (!Application.isPlaying)
        {
            Compute(job);
            piecesPlaced += job.pieces;
            foreach (var pair in job.result)
                if (pair.Value.vertices.Count > 0)
                    CreateDecorObject(chunk, pair.Key, pair.Value);
            return;
        }
        chunksWaiting++;
        Task.Run(() =>
        {
            try { Compute(job); }
            catch (System.Exception error) { Debug.LogException(error); job.result = new Dictionary<Group, Buffer>(); }
            finishedJobs.Enqueue(job);
        });
    }

    /// <summary>Places every piece for one chunk. Pure math: safe on a worker thread.</summary>
    private void Compute(Job job)
    {
        var random = new System.Random(job.randomSeed);
        var occupied = new List<Bounds>();
        job.result = new Dictionary<Group, Buffer>();
        for (int subMesh = 0; subMesh < job.triangles.Length; subMesh++)
        {
            Ground ground = BuildGround(job.localToWorld, job.vertices, job.normals, job.triangles[subMesh]);
            if (ground.totalArea <= 0f) continue;
            for (int k = 0; k < job.kinds.Length; k++)
            {
                Kind kind = job.kinds[k];
                if (kind.inner != (subMesh == 1)) continue;
                float expected = ground.totalArea / 1000f * kind.clustersPer1000m2 * density;
                int clusters = Mathf.FloorToInt(expected) + (random.NextDouble() < expected % 1f ? 1 : 0);
                for (int i = 0; i < clusters; i++)
                    job.pieces += GrowCluster(kind, k, ground, random, GetBuffer(job.result, kind.group), job.worldToLocal, job.spawn, occupied, job.layout);
            }
        }
    }

    /// <summary>Grows one cluster and returns how many pieces it placed.</summary>
    private int GrowCluster(Kind kind, int kindIndex, Ground ground, System.Random random, Buffer buffer, Matrix4x4 worldToChunk, Vector3 spawn, List<Bounds> occupied, RealmDecorLayout.Snapshot district)
    {
        int triangle = PickTriangle(ground, random);
        Vector3 centre = PointOn(ground, triangle, random);
        Vector3 normal = ground.normal[triangle];
        if (normal.y < kind.normalYRange.x || normal.y > kind.normalYRange.y) return 0;

        if (!kind.inner && new Vector2(centre.x - spawn.x, centre.z - spawn.z).magnitude < spawnClearRadius) return 0;
        if (random.NextDouble() >= district.Weight(kind.shape, kind.inner, centre)) return 0;

        // Patches: each kind has its own noise field, so kinds settle in different areas.
        float patch = PatchNoise(centre.x / kind.patchSize + kindIndex * 37.1f + seed * 0.37f,
                                 (centre.z + centre.y * 0.5f) / kind.patchSize - kindIndex * 21.7f);
        float threshold = 1f - kind.patchCoverage;
        if (random.NextDouble() > Mathf.SmoothStep(0f, 1f, (patch - threshold + 0.12f) / 0.24f)) return 0;

        // Nearby triangles that face a similar way, for the other members of the cluster.
        var nearby = new List<int>();
        float radiusSqr = kind.clusterRadius * kind.clusterRadius;
        for (int t = 0; t < ground.Count; t++)
        {
            Vector3 n = ground.normal[t];
            if (n.y < kind.normalYRange.x || n.y > kind.normalYRange.y) continue;
            if ((ground.centre[t] - centre).sqrMagnitude > radiusSqr) continue;
            if (Vector3.Dot(n, normal) < 0.5f) continue;
            nearby.Add(t);
        }

        DecorMesh[] shapes = GetVariants(kind.shape);
        int sharedVariant = random.Next(shapes.Length);
        float sharedScale = Range(random, kind.scaleRange.x, kind.scaleRange.y);
        int members = random.Next(Mathf.Max(1, kind.membersPerCluster.x), Mathf.Max(kind.membersPerCluster.x, kind.membersPerCluster.y) + 1);
        members = Mathf.Max(1, Mathf.CeilToInt(members * clusterFullness));
        int placed = 0;

        for (int m = 0; m < members; m++)
        {
            Vector3 point = centre, pointNormal = normal;
            if (m > 0)
            {
                if (nearby.Count == 0) break;
                int t = nearby[random.Next(nearby.Count)];
                point = PointOn(ground, t, random);
                pointNormal = ground.normal[t];
            }
            DecorMesh shape = kind.uniformClusters ? shapes[sharedVariant] : shapes[random.Next(shapes.Length)];
            if (district.Weight(kind.shape, kind.inner, point) < 0.2f) continue;
            float scale = kind.uniformClusters ? sharedScale * Range(random, 0.88f, 1.12f) : Range(random, kind.scaleRange.x, kind.scaleRange.y);
            if (Place(kind, shape, buffer, point, pointNormal, scale, random, worldToChunk, occupied)) placed++;
        }
        return placed;
    }

    private bool Place(Kind kind, DecorMesh shape, Buffer buffer, Vector3 point, Vector3 normal, float scale, System.Random random, Matrix4x4 worldToChunk, List<Bounds> occupied)
    {
        // Floors grow up, ceilings hang down, then lean towards the ground normal.
        Vector3 reference = normal.y >= 0f ? Vector3.up : Vector3.down;
        Vector3 direction = Vector3.Slerp(reference, normal, kind.alignToNormal).normalized;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, direction) * Quaternion.Euler(0f, Range(random, 0f, 360f), 0f);
        Vector3 position = point - direction * kind.sink * scale;
        if (!kind.inner && normal.y > 0.45f)
        {
            // The centre alone is insufficient on a slope: lift the lowest part of the footprint out of its ground plane.
            float correction = 0f;
            foreach (Vector3 vertex in shape.vertices)
                correction = Mathf.Max(correction, Vector3.Dot(point - (position + rotation * (vertex * scale)), normal) - 0.03f * scale);
            if (correction > 0.75f * scale) return false; // Too steep for this shape; leave this patch clear.
            position += normal * correction;
        }
        Matrix4x4 toWorld = Matrix4x4.TRS(position, rotation, Vector3.one * scale);
        Bounds footprint = new Bounds(toWorld.MultiplyPoint3x4(shape.vertices[0]), Vector3.zero);
        foreach (Vector3 vertex in shape.vertices) footprint.Encapsulate(toWorld.MultiplyPoint3x4(vertex));
        if (!kind.inner)
        {
            foreach (Bounds other in occupied) if (other.Intersects(footprint)) return false;
            occupied.Add(footprint);
        }
        Matrix4x4 toChunk = worldToChunk * Matrix4x4.TRS(position, rotation, Vector3.one * scale);
        float pieceRandom = (float)random.NextDouble();

        int start = buffer.vertices.Count;
        for (int i = 0; i < shape.vertices.Count; i++)
        {
            buffer.vertices.Add(toChunk.MultiplyPoint3x4(shape.vertices[i]));
            buffer.normals.Add(toChunk.MultiplyVector(shape.normals[i]).normalized);
            buffer.uvs.Add(new Vector2(shape.lengths[i], pieceRandom));
        }
        int triangleStart = buffer.triangles.Count;
        foreach (int index in shape.triangles) buffer.triangles.Add(start + index);
        buffer.pieces.Add((footprint, triangleStart, buffer.triangles.Count - triangleStart));
        return true;
    }

    private void CreateDecorObject(GameObject chunk, Group group, Buffer buffer)
    {
        CullOccupied(buffer, LandmarkBounds());
        if (buffer.triangles.Count == 0) return;
        var mesh = new Mesh
        {
            name = $"Decor {group} {chunk.name}",
            hideFlags = HideFlags.DontSave,
            indexFormat = buffer.vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };
        mesh.SetVertices(buffer.vertices);
        mesh.SetNormals(buffer.normals);
        mesh.SetUVs(0, buffer.uvs);
        mesh.SetTriangles(buffer.triangles, 0);
        mesh.RecalculateBounds();
        // Room for the sway and breathing done in the shader.
        Bounds bounds = mesh.bounds;
        bounds.Expand(2f);
        mesh.bounds = bounds;

        var decor = new GameObject($"Decor {group}", typeof(MeshFilter), typeof(MeshRenderer))
        {
            hideFlags = HideFlags.DontSave,
            layer = decorLayer,
        };
        decor.transform.SetParent(chunk.transform, false);
        decor.GetComponent<MeshFilter>().sharedMesh = mesh;
        var meshRenderer = decor.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = group == Group.Stone ? stoneMaterial : group == Group.Organic ? organicMaterial
            : group == Group.InnerGlow && innerGlowMaterial != null ? innerGlowMaterial : innerMaterial;
        // The inner world has no sun, so its growths never need to cast shadows.
        if (group == Group.Inner || group == Group.InnerGlow) meshRenderer.shadowCastingMode = ShadowCastingMode.Off;

        if ((group == Group.Stone && stoneColliders) || (group == Group.Organic && organicColliders)
            || ((group == Group.Inner || group == Group.InnerGlow) && innerColliders))
        {
            if (!Application.isPlaying) decor.AddComponent<MeshCollider>().sharedMesh = mesh;
            else
            {
                // Cook the collider on a worker; Update attaches it when ready.
                var id = mesh.GetEntityId();
                meshRenderer.enabled = false; // Do not show a solid object until it can actually be stood on.
                var pending = new PendingCollider { decor = decor, mesh = mesh };
                pending.bake = Task.Run(() =>
                {
                    try { Physics.BakeMesh(id, false, Cooking); }
                    catch (System.Exception error) { pending.error = error; }
                });
                pendingColliders.Add(pending);
            }
        }

        built.Add((chunk, mesh));
    }

    private Bounds[] LandmarkBounds()
    {
        if (boneClearance != null) return boneClearance;
        var reserved = new List<Bounds>();
        BoneKiln kiln = FindFirstObjectByType<BoneKiln>();
        if (kiln != null && kiln.Root != null)
            foreach (Renderer renderer in kiln.Root.GetComponentsInChildren<Renderer>())
            {
                if (renderer.GetComponentInParent<BoneChild>() != null || renderer.GetComponentInParent<Creature>() != null) continue;
                Bounds bounds = renderer.bounds;
                bounds.Expand(0.3f);
                reserved.Add(bounds);
            }
        // Save its resting footprint once; a flying mother must not erase decorations along its flight path.
        if (reserved.Count > 0) boneClearance = reserved.ToArray();
        return boneClearance ?? System.Array.Empty<Bounds>();
    }

    private static bool CullOccupied(Buffer buffer, Bounds[] reserved)
    {
        bool changed = false;
        for (int p = buffer.pieces.Count - 1; p >= 0; p--)
        {
            var piece = buffer.pieces[p];
            bool intersects = false;
            foreach (Bounds obstacle in reserved) if (obstacle.Intersects(piece.bounds)) { intersects = true; break; }
            if (!intersects) continue;
            buffer.triangles.RemoveRange(piece.start, piece.count);
            buffer.pieces.RemoveAt(p);
            for (int later = p; later < buffer.pieces.Count; later++)
            {
                var shifted = buffer.pieces[later];
                buffer.pieces[later] = (shifted.bounds, shifted.start - piece.count, shifted.count);
            }
            changed = true;
        }
        return changed;
    }

    // ---------------- Helpers ----------------

    internal static Ground BuildGround(Matrix4x4 chunk, Vector3[] vertices, Vector3[] normals, int[] triangles)
    {
        int count = triangles.Length / 3;
        var ground = new Ground
        {
            a = new Vector3[count], b = new Vector3[count], c = new Vector3[count],
            normal = new Vector3[count], centre = new Vector3[count], cumulativeArea = new float[count],
        };
        float total = 0f;
        for (int i = 0; i < count; i++)
        {
            int ia = triangles[i * 3], ib = triangles[i * 3 + 1], ic = triangles[i * 3 + 2];
            Vector3 a = chunk.MultiplyPoint3x4(vertices[ia]), b = chunk.MultiplyPoint3x4(vertices[ib]), c = chunk.MultiplyPoint3x4(vertices[ic]);
            ground.a[i] = a;
            ground.b[i] = b;
            ground.c[i] = c;
            ground.centre[i] = (a + b + c) / 3f;
            ground.normal[i] = chunk.MultiplyVector(normals[ia] + normals[ib] + normals[ic]).normalized;
            total += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            ground.cumulativeArea[i] = total;
        }
        ground.totalArea = total;
        return ground;
    }

    internal static int PickTriangle(Ground ground, System.Random random)
    {
        float target = (float)random.NextDouble() * ground.totalArea;
        int index = System.Array.BinarySearch(ground.cumulativeArea, target);
        if (index < 0) index = ~index;
        return Mathf.Clamp(index, 0, ground.Count - 1);
    }

    internal static Vector3 PointOn(Ground ground, int triangle, System.Random random)
    {
        float u = (float)random.NextDouble(), v = (float)random.NextDouble();
        if (u + v > 1f)
        {
            u = 1f - u;
            v = 1f - v;
        }
        return ground.a[triangle] + (ground.b[triangle] - ground.a[triangle]) * u + (ground.c[triangle] - ground.a[triangle]) * v;
    }

    private DecorMesh[] GetVariants(DecorShapes.Shape shape)
    {
        if (variants.TryGetValue(shape, out DecorMesh[] list)) return list;
        list = new DecorMesh[variantsPerShape];
        for (int i = 0; i < list.Length; i++)
            list[i] = DecorShapes.Build(shape, seed * 7919 + i * 104729 + (int)shape * 1299709);
        variants.Add(shape, list);
        return list;
    }

    private static Buffer GetBuffer(Dictionary<Group, Buffer> buffers, Group group)
    {
        if (!buffers.TryGetValue(group, out Buffer buffer))
        {
            buffer = new Buffer();
            buffers.Add(group, buffer);
        }
        return buffer;
    }

    private void PruneDestroyedChunks()
    {
        for (int i = built.Count - 1; i >= 0; i--)
        {
            if (built[i].chunk != null) continue;
            RetireMesh(built[i].mesh);
            built.RemoveAt(i);
        }
        if (built.Count == 0) piecesPlaced = 0;
    }

    private static void DestroySafe(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    private void RetireMesh(Mesh mesh)
    {
        foreach (PendingCollider pending in pendingColliders)
            if (pending.mesh == mesh) { pending.retired = true; return; }
        DestroySafe(mesh);
    }

    private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

    /// <summary>Smooth value noise 0..1 (thread safe, unlike Mathf.PerlinNoise).</summary>
    private static float PatchNoise(float x, float y)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Lattice(ix, iy), b = Lattice(ix + 1, iy), c = Lattice(ix, iy + 1), d = Lattice(ix + 1, iy + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    private static float Lattice(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }

    [ContextMenu("Reset Kinds To Defaults")]
    public void ResetKinds() => kinds = DefaultKinds();

    private static Kind NewKind(string name, DecorShapes.Shape shape, Group group, float clusters, int minMembers, int maxMembers,
        float radius, float minScale, float maxScale, float minNormalY, float align, float sink, float patchSize, float coverage)
    {
        return new Kind
        {
            name = name, shape = shape, group = group, inner = group == Group.Inner || group == Group.InnerGlow,
            clustersPer1000m2 = clusters, membersPerCluster = new Vector2Int(minMembers, maxMembers), clusterRadius = radius,
            scaleRange = new Vector2(minScale, maxScale), normalYRange = new Vector2(minNormalY, 1f),
            alignToNormal = align, sink = sink, patchSize = patchSize, patchCoverage = coverage,
            uniformClusters = group == Group.Inner || group == Group.InnerGlow,
        };
    }

    private static List<Kind> DefaultKinds() => new List<Kind>
    {
        // Surface, inorganic and abstract.
        NewKind("Shards", DecorShapes.Shape.Shards, Group.Stone, 0.9f, 1, 3, 6f, 0.8f, 1.8f, 0.5f, 0.35f, 0.05f, 70f, 0.6f),
        NewKind("Arches", DecorShapes.Shape.Arch, Group.Stone, 0.25f, 1, 2, 8f, 0.8f, 1.6f, 0.7f, 0.5f, 0f, 90f, 0.65f),
        NewKind("Cairns", DecorShapes.Shape.Cairn, Group.Stone, 0.45f, 1, 4, 5f, 0.6f, 1.3f, 0.7f, 0.2f, 0.05f, 50f, 0.55f),
        NewKind("Cages", DecorShapes.Shape.Cage, Group.Stone, 0.3f, 1, 3, 7f, 0.8f, 1.8f, 0.65f, 0.15f, 0f, 60f, 0.55f),
        NewKind("Totems", DecorShapes.Shape.Totem, Group.Stone, 0.3f, 1, 4, 6f, 0.8f, 1.5f, 0.7f, 0.1f, 0f, 55f, 0.55f),
        NewKind("Halos", DecorShapes.Shape.Halo, Group.Stone, 0.14f, 1, 2, 10f, 0.8f, 1.6f, 0.6f, 0f, 0f, 80f, 0.6f),

        // Surface, organic.
        NewKind("Polyps", DecorShapes.Shape.Polyps, Group.Organic, 1.4f, 2, 7, 4f, 0.7f, 1.6f, 0.6f, 0.2f, 0.03f, 45f, 0.6f),
        NewKind("Bladders", DecorShapes.Shape.Bladders, Group.Organic, 1f, 2, 6, 3f, 0.6f, 1.4f, 0.55f, 0.7f, 0.05f, 35f, 0.55f),
        NewKind("Creepers", DecorShapes.Shape.Creepers, Group.Organic, 1f, 1, 4, 4f, 0.8f, 1.6f, 0.45f, 1f, 0f, 40f, 0.6f),
        NewKind("Needles", DecorShapes.Shape.Needles, Group.Organic, 1.1f, 2, 6, 3.5f, 0.6f, 1.5f, 0.5f, 0.6f, 0.02f, 30f, 0.55f),
        NewKind("Ribbons", DecorShapes.Shape.Ribbon, Group.Organic, 0.5f, 1, 4, 4f, 0.8f, 1.5f, 0.65f, 0.15f, 0.02f, 50f, 0.5f),
        NewKind("Mushrooms", DecorShapes.Shape.Mushrooms, Group.Organic, 1.2f, 2, 6, 3.5f, 0.7f, 1.6f, 0.55f, 0.3f, 0.02f, 35f, 0.55f),
        NewKind("Fronds", DecorShapes.Shape.Fronds, Group.Organic, 0.9f, 1, 4, 4f, 0.7f, 1.4f, 0.6f, 0.4f, 0.02f, 40f, 0.55f),
        NewKind("Pebbles", DecorShapes.Shape.Pebbles, Group.Stone, 1.5f, 1, 3, 5f, 0.7f, 1.6f, 0.5f, 0.9f, 0.02f, 30f, 0.65f),
        NewKind("Obelisks", DecorShapes.Shape.Obelisk, Group.Stone, 0.08f, 1, 2, 12f, 0.8f, 1.4f, 0.75f, 0.05f, 0f, 120f, 0.6f),

        // Inner world: colonies of identical hard-shelled tentacles, carpets of short spikes, hanging needle beards.
        NewKind("Shell Tentacles", DecorShapes.Shape.ShellTentacle, Group.Inner, 1.6f, 8, 22, 3.5f, 1.4f, 3.4f, -1f, 0.85f, 0.05f, 40f, 0.65f),
        NewKind("Shell Carpets", DecorShapes.Shape.ShellTentacle, Group.Inner, 0.7f, 12, 26, 2.2f, 0.45f, 0.8f, -1f, 0.95f, 0.03f, 30f, 0.55f),
        NewKind("Needle Beards", DecorShapes.Shape.Needles, Group.Inner, 0.6f, 3, 8, 3f, 0.8f, 1.6f, -1f, 0.9f, 0.02f, 35f, 0.55f),
        NewKind("Crystals", DecorShapes.Shape.Shards, Group.InnerGlow, 0.7f, 2, 6, 3f, 0.5f, 1.3f, -1f, 0.8f, 0.05f, 45f, 0.55f),
        NewKind("Hanging Veils", DecorShapes.Shape.Ribbon, Group.Inner, 0.5f, 2, 5, 3f, 0.9f, 1.8f, -1f, 0.95f, 0.02f, 40f, 0.5f),
        NewKind("Glow Sacs", DecorShapes.Shape.Bladders, Group.InnerGlow, 0.6f, 1, 4, 3f, 0.5f, 1.1f, -1f, 0.8f, 0.05f, 35f, 0.55f),
        NewKind("Cave Pebbles", DecorShapes.Shape.Pebbles, Group.Inner, 1.2f, 1, 3, 5f, 0.7f, 1.5f, 0.5f, 0.9f, 0.02f, 30f, 0.6f),
    };
}
