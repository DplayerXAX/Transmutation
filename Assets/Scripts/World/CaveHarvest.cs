using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Grows pickable fruit and flowers in the inner world as ProceduralWorld chunks are built.
/// Fruit bunches hang on thin stalks from the cave ceiling; flowers stand on floors and walls.
/// Each fruit / flower is its own object with a CaveHarvestItem, so the player can pick it with E.
/// Stalks stay behind and are merged into one mesh per chunk. Only grows in Play Mode.
/// Placement runs on a worker thread; the items are then created a few per frame so loading never stalls.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ProceduralWorld))]
public sealed class CaveHarvest : MonoBehaviour
{
    [Header("Materials")]
    [SerializeField] private Material fruitMaterial;
    [SerializeField] private Material flowerMaterial;
    [SerializeField] private Material stalkMaterial;

    [Header("Fruit (ceilings)")]
    [Min(0f)] [SerializeField] private float fruitClustersPer1000m2 = 0.5f;
    [SerializeField] private Vector2Int fruitsPerCluster = new Vector2Int(2, 5);
    [SerializeField] private Vector2 stalkLength = new Vector2(0.5f, 1.6f);

    [Header("Flowers (floors and walls)")]
    [Min(0f)] [SerializeField] private float flowerClustersPer1000m2 = 0.6f;
    [SerializeField] private Vector2Int flowersPerCluster = new Vector2Int(3, 8);

    [Header("Placement")]
    [Min(0f)] [SerializeField] private float clusterRadius = 2.5f;
    [SerializeField] private int seed = 11;
    [Range(1, 12)] [SerializeField] private int variants = 6;

    [Header("Loading")]
    [Tooltip("Fruit / flower objects created per frame.")]
    [Range(1, 64)] [SerializeField] private int itemsPerFrame = 12;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private int itemsGrown;
    [SerializeField] private int itemsWaiting;

    /// <summary>One fruit or flower to create, decided on a worker.</summary>
    private struct Spawn
    {
        public bool fruit;
        public int variant;
        public Vector3 position;
        public Quaternion rotation;
        public float scale;
    }

    private sealed class Job
    {
        public GameObject chunk;
        public Matrix4x4 localToWorld, worldToLocal;
        public Vector3[] vertices, normals;
        public int[] triangles;
        public int randomSeed;
        public readonly List<Spawn> spawns = new List<Spawn>();
        public readonly List<Matrix4x4> stalks = new List<Matrix4x4>();
    }

    private readonly ConcurrentQueue<Job> finishedJobs = new ConcurrentQueue<Job>();
    private readonly Queue<(Transform chunk, Spawn spawn)> spawnQueue = new Queue<(Transform, Spawn)>();
    private float[] flowerHeadHeights;

    private ProceduralWorld world;
    private DecorMesh stalkShape;
    private Mesh[] fruitMeshes, flowerMeshes;
    private readonly List<Mesh> stalkMeshes = new List<Mesh>();

    public void SetMaterials(Material fruit, Material flower, Material stalk)
    {
        fruitMaterial = fruit;
        flowerMaterial = flower;
        stalkMaterial = stalk;
    }

    private void OnEnable()
    {
        world = GetComponent<ProceduralWorld>();
        world.ChunkCreated += Grow;
    }

    private void OnDisable()
    {
        if (world != null) world.ChunkCreated -= Grow;
    }

    private void OnDestroy()
    {
        foreach (Mesh mesh in stalkMeshes) if (mesh != null) Destroy(mesh);
        foreach (Mesh mesh in shared.Values) if (mesh != null) Destroy(mesh);
    }

    private void Grow(GameObject chunk, Mesh chunkMesh)
    {
        if (!Application.isPlaying || chunkMesh.subMeshCount < 2) return;
        BuildShapes();

        Vector3 key = chunk.transform.localPosition;
        var job = new Job
        {
            chunk = chunk,
            localToWorld = chunk.transform.localToWorldMatrix,
            worldToLocal = chunk.transform.worldToLocalMatrix,
            vertices = chunkMesh.vertices,
            normals = chunkMesh.normals,
            triangles = chunkMesh.GetTriangles(1),
            randomSeed = unchecked(seed * 92821 ^ Mathf.RoundToInt(key.x) * 19349663 ^ Mathf.RoundToInt(key.y) * 83492791 ^ Mathf.RoundToInt(key.z) * 2971215),
        };
        Task.Run(() =>
        {
            try { Compute(job); }
            catch (System.Exception error) { Debug.LogException(error); job.spawns.Clear(); job.stalks.Clear(); }
            finishedJobs.Enqueue(job);
        });
    }

    private void Update()
    {
        // Stalks for a whole chunk are one mesh; its fruit and flowers join the spawn queue.
        while (finishedJobs.TryDequeue(out Job job))
        {
            if (job.chunk == null) continue;
            if (job.stalks.Count > 0) CreateStalks(job.chunk, job.stalks);
            foreach (Spawn spawn in job.spawns) spawnQueue.Enqueue((job.chunk.transform, spawn));
        }
        for (int i = 0; i < itemsPerFrame && spawnQueue.Count > 0; i++)
        {
            var (chunk, spawn) = spawnQueue.Dequeue();
            if (chunk == null) continue;
            if (spawn.fruit)
                CreateItem("Cave Fruit", fruitMeshes[spawn.variant], fruitMaterial, spawn.position, spawn.rotation, spawn.scale,
                    new Vector3(0f, -0.18f, 0f), 0.16f, chunk);
            else
                CreateItem("Cave Flower", flowerMeshes[spawn.variant], flowerMaterial, spawn.position, spawn.rotation, spawn.scale,
                    new Vector3(0f, flowerHeadHeights[spawn.variant] * 0.75f, 0f), 0.13f, chunk);
        }
        itemsWaiting = spawnQueue.Count;
    }

    /// <summary>Decides every fruit, flower and stalk for one chunk. Pure math: safe on a worker thread.</summary>
    private void Compute(Job job)
    {
        WorldDecorator.Ground ground = WorldDecorator.BuildGround(job.localToWorld, job.vertices, job.normals, job.triangles);
        if (ground.totalArea <= 0f) return;
        var random = new System.Random(job.randomSeed);

        int fruitClusters = Count(ground.totalArea * fruitClustersPer1000m2 / 1000f, random);
        for (int i = 0; i < fruitClusters; i++) GrowCluster(ground, random, job, fruit: true);

        int flowerClusters = Count(ground.totalArea * flowerClustersPer1000m2 / 1000f, random);
        for (int i = 0; i < flowerClusters; i++) GrowCluster(ground, random, job, fruit: false);
    }

    private void GrowCluster(WorldDecorator.Ground ground, System.Random random, Job job, bool fruit)
    {
        int triangle = WorldDecorator.PickTriangle(ground, random);
        Vector3 centre = WorldDecorator.PointOn(ground, triangle, random);
        Vector3 normal = ground.normal[triangle];
        // Fruit only under ceilings, flowers on anything that is not a ceiling.
        if (fruit ? normal.y > -0.5f : normal.y < -0.3f) return;

        var nearby = new List<int>();
        for (int t = 0; t < ground.Count; t++)
        {
            if ((ground.centre[t] - centre).sqrMagnitude > clusterRadius * clusterRadius) continue;
            if (Vector3.Dot(ground.normal[t], normal) < 0.6f) continue;
            nearby.Add(t);
        }

        Vector2Int range = fruit ? fruitsPerCluster : flowersPerCluster;
        int members = random.Next(Mathf.Max(1, range.x), Mathf.Max(range.x, range.y) + 1);
        for (int m = 0; m < members; m++)
        {
            Vector3 point = centre, pointNormal = normal;
            if (m > 0 && nearby.Count > 0)
            {
                int t = nearby[random.Next(nearby.Count)];
                point = WorldDecorator.PointOn(ground, t, random);
                pointNormal = ground.normal[t];
            }
            if (fruit) GrowFruit(point, random, job);
            else GrowFlower(point, pointNormal, random, job);
        }
    }

    private void GrowFruit(Vector3 ceilingPoint, System.Random random, Job job)
    {
        float length = Range(random, stalkLength.x, stalkLength.y);
        // The stalk mesh grows along +y; turn it upside down so it hangs from the ceiling.
        Vector3 top = ceilingPoint + Vector3.up * 0.05f;
        Quaternion hang = Quaternion.Euler(180f + Range(random, -8f, 8f), Range(random, 0f, 360f), 0f);
        job.stalks.Add(job.worldToLocal * Matrix4x4.TRS(top, hang, Vector3.one * length));

        job.spawns.Add(new Spawn
        {
            fruit = true,
            variant = random.Next(variants),
            position = top + hang * Vector3.up * length,
            rotation = Quaternion.Euler(0f, Range(random, 0f, 360f), 0f),
            scale = Range(random, 0.85f, 1.3f),
        });
    }

    private void GrowFlower(Vector3 point, Vector3 normal, System.Random random, Job job)
    {
        Vector3 up = Vector3.Slerp(Vector3.up, normal, 0.6f).normalized;
        job.spawns.Add(new Spawn
        {
            fruit = false,
            rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, Range(random, 0f, 360f), 0f),
            variant = random.Next(variants),
            scale = Range(random, 0.9f, 1.5f),
            position = point - up * 0.02f,
        });
    }

    private void CreateItem(string itemName, Mesh mesh, Material material, Vector3 position, Quaternion rotation, float scale,
        Vector3 colliderCentre, float colliderRadius, Transform chunk)
    {
        var item = new GameObject(itemName, typeof(MeshFilter), typeof(MeshRenderer), typeof(SphereCollider), typeof(Rigidbody))
        {
            hideFlags = HideFlags.DontSave,
        };
        item.transform.SetParent(chunk, true);
        item.transform.SetPositionAndRotation(position, rotation);
        item.transform.localScale = Vector3.one * scale;
        item.GetComponent<MeshFilter>().sharedMesh = mesh;
        var itemRenderer = item.GetComponent<MeshRenderer>();
        itemRenderer.sharedMaterial = material;
        itemRenderer.shadowCastingMode = ShadowCastingMode.Off;

        var sphere = item.GetComponent<SphereCollider>();
        sphere.center = colliderCentre;
        sphere.radius = colliderRadius;

        var body = item.GetComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.mass = 0.2f;

        item.AddComponent<CaveHarvestItem>().SetDisplayName(itemName);
        itemsGrown++;
    }

    private void CreateStalks(GameObject chunk, List<Matrix4x4> stalkMatrices)
    {
        Mesh stalk = ToMesh(stalkShape, "Stalk");
        var stalks = new CombineInstance[stalkMatrices.Count];
        for (int i = 0; i < stalks.Length; i++) stalks[i] = new CombineInstance { mesh = stalk, transform = stalkMatrices[i] };
        var mesh = new Mesh { name = "Harvest Stalks " + chunk.name, hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(stalks, true, true);
        mesh.RecalculateBounds();
        stalkMeshes.Add(mesh);

        var stalkObject = new GameObject("Decor Harvest Stalks", typeof(MeshFilter), typeof(MeshRenderer)) { hideFlags = HideFlags.DontSave };
        stalkObject.transform.SetParent(chunk.transform, false);
        stalkObject.GetComponent<MeshFilter>().sharedMesh = mesh;
        var stalkRenderer = stalkObject.GetComponent<MeshRenderer>();
        stalkRenderer.sharedMaterial = stalkMaterial;
        stalkRenderer.shadowCastingMode = ShadowCastingMode.Off;
    }

    // ---------------- Shapes ----------------

    private readonly Dictionary<string, Mesh> shared = new Dictionary<string, Mesh>();

    private void BuildShapes()
    {
        if (fruitMeshes != null) return;
        stalkShape = DecorShapes.Build(DecorShapes.Shape.HangingStalk, seed * 31);
        fruitMeshes = new Mesh[variants];
        flowerMeshes = new Mesh[variants];
        for (int i = 0; i < variants; i++)
        {
            fruitMeshes[i] = ToMesh(DecorShapes.Build(DecorShapes.Shape.Fruit, seed * 7919 + i * 104729), "Cave Fruit " + i);
            flowerMeshes[i] = ToMesh(DecorShapes.Build(DecorShapes.Shape.Flower, seed * 6151 + i * 1299709), "Cave Flower " + i);
        }
        flowerHeadHeights = new float[variants];
        for (int i = 0; i < variants; i++) flowerHeadHeights[i] = flowerMeshes[i].bounds.max.y;
    }

    /// <summary>Unity mesh for a shape; uv.y gets a fixed random per variant so pieces differ in the shader.</summary>
    private Mesh ToMesh(DecorMesh shape, string meshName)
    {
        if (shared.TryGetValue(meshName, out Mesh cached)) return cached;
        var uvs = new List<Vector2>(shape.lengths.Count);
        float pieceRandom = (meshName.GetHashCode() & 1023) / 1023f;
        foreach (float length in shape.lengths) uvs.Add(new Vector2(length, pieceRandom));
        var mesh = new Mesh { name = meshName, hideFlags = HideFlags.DontSave };
        mesh.SetVertices(shape.vertices);
        mesh.SetNormals(shape.normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(shape.triangles, 0);
        mesh.RecalculateBounds();
        shared.Add(meshName, mesh);
        return mesh;
    }

    private static int Count(float expected, System.Random random) =>
        Mathf.FloorToInt(expected) + (random.NextDouble() < expected % 1f ? 1 : 0);

    private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
}
