using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Grows procedural decorations on the ProceduralWorld chunks as they are built:
/// stone shards, arches and cairns plus soft breathing polyps, sacs and creepers on the surface,
/// and colonies of dim hard-shelled tentacles on the floors, walls and ceilings of the inner world.
///
/// Pieces gather in patches and clusters so the land has bare and crowded areas.
/// All pieces of one chunk are merged into one mesh per material, parented to the chunk.
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

    [Header("Placement")]
    [SerializeField] private int seed = 7;
    [Range(1, 16)] [SerializeField] private int variantsPerShape = 6;
    [Tooltip("No surface decorations this close to the spawn point.")]
    [Min(0f)] [SerializeField] private float spawnClearRadius = 6f;
    [SerializeField] private List<Kind> kinds = DefaultKinds();

    [Header("Physics")]
    [SerializeField] private bool stoneColliders = true;
    [Tooltip("Solid tentacles. Off by default: many small colliders slow chunk loading.")]
    [SerializeField] private bool innerColliders;
    [Tooltip("7 = Ground, so the player can stand and jump on stones.")]
    [SerializeField] private int decorLayer = 7;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private int piecesPlaced;

    private sealed class Buffer
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<int> triangles = new List<int>();
    }

    /// <summary>World-space triangles of one chunk sub-mesh, for area-weighted sampling.</summary>
    private sealed class Ground
    {
        public Vector3[] a, b, c, normal, centre;
        public float[] cumulativeArea;
        public float totalArea;
        public int Count => a.Length;
    }

    private readonly Dictionary<DecorShapes.Shape, DecorMesh[]> variants = new Dictionary<DecorShapes.Shape, DecorMesh[]>();
    private readonly List<(GameObject chunk, Mesh mesh)> built = new List<(GameObject, Mesh)>();
    private ProceduralWorld world;

    private void Reset() => kinds = DefaultKinds();

    private void OnEnable()
    {
        world = GetComponent<ProceduralWorld>();
        world.ChunkCreated += Decorate;
    }

    private void OnDisable()
    {
        if (world != null) world.ChunkCreated -= Decorate;
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
        variants.Clear();
    }

    public void SetMaterials(Material stone, Material organic, Material innerGrowth)
    {
        stoneMaterial = stone;
        organicMaterial = organic;
        innerMaterial = innerGrowth;
    }

    // ---------------- Decorating one chunk ----------------

    private void Decorate(GameObject chunk, Mesh chunkMesh)
    {
        PruneDestroyedChunks();

        Vector3 key = chunk.transform.localPosition;
        var random = new System.Random(unchecked(seed * 73856093 ^ Mathf.RoundToInt(key.x) * 19349663 ^ Mathf.RoundToInt(key.y) * 83492791 ^ Mathf.RoundToInt(key.z) * 2971215));
        var buffers = new Dictionary<Group, Buffer>();
        Matrix4x4 worldToChunk = chunk.transform.worldToLocalMatrix;

        Vector3[] vertices = chunkMesh.vertices;
        Vector3[] normals = chunkMesh.normals;
        for (int subMesh = 0; subMesh < Mathf.Min(2, chunkMesh.subMeshCount); subMesh++)
        {
            Ground ground = BuildGround(chunk.transform, vertices, normals, chunkMesh.GetTriangles(subMesh));
            if (ground.totalArea <= 0f) continue;
            for (int k = 0; k < kinds.Count; k++)
            {
                Kind kind = kinds[k];
                if (kind.inner != (subMesh == 1)) continue;
                float expected = ground.totalArea / 1000f * kind.clustersPer1000m2;
                int clusters = Mathf.FloorToInt(expected) + (random.NextDouble() < expected % 1f ? 1 : 0);
                for (int i = 0; i < clusters; i++)
                    GrowCluster(kind, k, ground, random, GetBuffer(buffers, kind.group), worldToChunk);
            }
        }

        foreach (var pair in buffers)
            if (pair.Value.vertices.Count > 0)
                CreateDecorObject(chunk, pair.Key, pair.Value);
    }

    private void GrowCluster(Kind kind, int kindIndex, Ground ground, System.Random random, Buffer buffer, Matrix4x4 worldToChunk)
    {
        int triangle = PickTriangle(ground, random);
        Vector3 centre = PointOn(ground, triangle, random);
        Vector3 normal = ground.normal[triangle];
        if (normal.y < kind.normalYRange.x || normal.y > kind.normalYRange.y) return;

        if (!kind.inner)
        {
            Vector3 spawn = transform.position;
            if (new Vector2(centre.x - spawn.x, centre.z - spawn.z).magnitude < spawnClearRadius) return;
        }

        // Patches: each kind has its own noise field, so kinds settle in different areas.
        float patch = Mathf.PerlinNoise(centre.x / kind.patchSize + kindIndex * 37.1f + seed * 0.37f,
                                        (centre.z + centre.y * 0.5f) / kind.patchSize - kindIndex * 21.7f);
        float threshold = 1f - kind.patchCoverage;
        if (random.NextDouble() > Mathf.SmoothStep(0f, 1f, (patch - threshold + 0.12f) / 0.24f)) return;

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
            float scale = kind.uniformClusters ? sharedScale * Range(random, 0.88f, 1.12f) : Range(random, kind.scaleRange.x, kind.scaleRange.y);
            Place(kind, shape, buffer, point, pointNormal, scale, random, worldToChunk);
        }
    }

    private void Place(Kind kind, DecorMesh shape, Buffer buffer, Vector3 point, Vector3 normal, float scale, System.Random random, Matrix4x4 worldToChunk)
    {
        // Floors grow up, ceilings hang down, then lean towards the ground normal.
        Vector3 reference = normal.y >= 0f ? Vector3.up : Vector3.down;
        Vector3 direction = Vector3.Slerp(reference, normal, kind.alignToNormal).normalized;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, direction) * Quaternion.Euler(0f, Range(random, 0f, 360f), 0f);
        Vector3 position = point - direction * kind.sink * scale;
        Matrix4x4 toChunk = worldToChunk * Matrix4x4.TRS(position, rotation, Vector3.one * scale);
        float pieceRandom = (float)random.NextDouble();

        int start = buffer.vertices.Count;
        for (int i = 0; i < shape.vertices.Count; i++)
        {
            buffer.vertices.Add(toChunk.MultiplyPoint3x4(shape.vertices[i]));
            buffer.normals.Add(toChunk.MultiplyVector(shape.normals[i]).normalized);
            buffer.uvs.Add(new Vector2(shape.lengths[i], pieceRandom));
        }
        foreach (int index in shape.triangles) buffer.triangles.Add(start + index);
        piecesPlaced++;
    }

    private void CreateDecorObject(GameObject chunk, Group group, Buffer buffer)
    {
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
        meshRenderer.sharedMaterial = group == Group.Stone ? stoneMaterial : group == Group.Organic ? organicMaterial : innerMaterial;
        // The inner world has no sun, so its growths never need to cast shadows.
        if (group == Group.Inner) meshRenderer.shadowCastingMode = ShadowCastingMode.Off;

        if ((group == Group.Stone && stoneColliders) || (group == Group.Inner && innerColliders))
            decor.AddComponent<MeshCollider>().sharedMesh = mesh;

        built.Add((chunk, mesh));
    }

    // ---------------- Helpers ----------------

    private static Ground BuildGround(Transform chunk, Vector3[] vertices, Vector3[] normals, int[] triangles)
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
            Vector3 a = chunk.TransformPoint(vertices[ia]), b = chunk.TransformPoint(vertices[ib]), c = chunk.TransformPoint(vertices[ic]);
            ground.a[i] = a;
            ground.b[i] = b;
            ground.c[i] = c;
            ground.centre[i] = (a + b + c) / 3f;
            ground.normal[i] = chunk.TransformDirection(normals[ia] + normals[ib] + normals[ic]).normalized;
            total += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            ground.cumulativeArea[i] = total;
        }
        ground.totalArea = total;
        return ground;
    }

    private static int PickTriangle(Ground ground, System.Random random)
    {
        float target = (float)random.NextDouble() * ground.totalArea;
        int index = System.Array.BinarySearch(ground.cumulativeArea, target);
        if (index < 0) index = ~index;
        return Mathf.Clamp(index, 0, ground.Count - 1);
    }

    private static Vector3 PointOn(Ground ground, int triangle, System.Random random)
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
            DestroySafe(built[i].mesh);
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

    private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

    private static List<Kind> DefaultKinds() => new List<Kind>
    {
        // Surface, inorganic.
        new Kind { name = "Shards", shape = DecorShapes.Shape.Shards, group = Group.Stone, clustersPer1000m2 = 0.5f,
            membersPerCluster = new Vector2Int(1, 2), clusterRadius = 6f, scaleRange = new Vector2(0.8f, 1.7f),
            normalYRange = new Vector2(0.5f, 1f), alignToNormal = 0.35f, sink = 0.05f, patchSize = 70f, patchCoverage = 0.45f },
        new Kind { name = "Arches", shape = DecorShapes.Shape.Arch, group = Group.Stone, clustersPer1000m2 = 0.12f,
            membersPerCluster = new Vector2Int(1, 1), clusterRadius = 0f, scaleRange = new Vector2(0.8f, 1.5f),
            normalYRange = new Vector2(0.75f, 1f), alignToNormal = 0.5f, sink = 0f, patchSize = 90f, patchCoverage = 0.6f },
        new Kind { name = "Cairns", shape = DecorShapes.Shape.Cairn, group = Group.Stone, clustersPer1000m2 = 0.25f,
            membersPerCluster = new Vector2Int(1, 3), clusterRadius = 5f, scaleRange = new Vector2(0.6f, 1.2f),
            normalYRange = new Vector2(0.7f, 1f), alignToNormal = 0.2f, sink = 0.05f, patchSize = 50f, patchCoverage = 0.4f },

        // Surface, organic.
        new Kind { name = "Polyps", shape = DecorShapes.Shape.Polyps, group = Group.Organic, clustersPer1000m2 = 0.8f,
            membersPerCluster = new Vector2Int(2, 6), clusterRadius = 4f, scaleRange = new Vector2(0.7f, 1.5f),
            normalYRange = new Vector2(0.6f, 1f), alignToNormal = 0.2f, sink = 0.03f, patchSize = 45f, patchCoverage = 0.45f },
        new Kind { name = "Bladders", shape = DecorShapes.Shape.Bladders, group = Group.Organic, clustersPer1000m2 = 0.6f,
            membersPerCluster = new Vector2Int(2, 5), clusterRadius = 3f, scaleRange = new Vector2(0.6f, 1.3f),
            normalYRange = new Vector2(0.55f, 1f), alignToNormal = 0.7f, sink = 0.05f, patchSize = 35f, patchCoverage = 0.4f },
        new Kind { name = "Creepers", shape = DecorShapes.Shape.Creepers, group = Group.Organic, clustersPer1000m2 = 0.6f,
            membersPerCluster = new Vector2Int(1, 3), clusterRadius = 4f, scaleRange = new Vector2(0.8f, 1.5f),
            normalYRange = new Vector2(0.5f, 1f), alignToNormal = 1f, sink = 0f, patchSize = 40f, patchCoverage = 0.5f },

        // Inner world: colonies of identical hard-shelled tentacles, and low carpets of short spikes.
        new Kind { name = "Shell Tentacles", shape = DecorShapes.Shape.ShellTentacle, group = Group.Inner, inner = true,
            clustersPer1000m2 = 1f, membersPerCluster = new Vector2Int(8, 20), clusterRadius = 3.5f, scaleRange = new Vector2(1.4f, 3.2f),
            normalYRange = new Vector2(-1f, 1f), alignToNormal = 0.85f, sink = 0.05f, patchSize = 40f, patchCoverage = 0.6f, uniformClusters = true },
        new Kind { name = "Shell Carpets", shape = DecorShapes.Shape.ShellTentacle, group = Group.Inner, inner = true,
            clustersPer1000m2 = 0.35f, membersPerCluster = new Vector2Int(12, 24), clusterRadius = 2.2f, scaleRange = new Vector2(0.45f, 0.8f),
            normalYRange = new Vector2(-1f, 1f), alignToNormal = 0.95f, sink = 0.03f, patchSize = 30f, patchCoverage = 0.5f, uniformClusters = true },
    };
}
