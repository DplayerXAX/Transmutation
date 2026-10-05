using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Fully procedural replacement for the painted Unity Terrain, built from a density field with
/// chunked Marching Cubes so it can hold overhangs, caves and a second world underneath.
///
/// Layout (heights relative to this transform, h = surface height at a column):
///   Surface         y = h                         walked on from above
///   Crust underside y = -crust - h * mirror       the surface hanging upside down: its ceiling
///   Inner world     y = -crust - gap - h * invert heights reversed: hills above become pits below
/// Sinkholes and worm tunnels connect the two. Gravity is normal everywhere.
///
/// Chunks are generated on worker threads; meshes and colliders are created on the main thread.
/// </summary>
[DisallowMultipleComponent]
public sealed class ProceduralWorld : MonoBehaviour
{
    [Header("Size")]
    [Tooltip("Horizontal size of the world in metres (x, z), centred on this transform.")]
    [SerializeField] private Vector2 worldSize = new Vector2(288f, 288f);
    [Range(0.5f, 3f)] [SerializeField] private float cellSize = 1.25f;
    [Range(8, 48)] [SerializeField] private int chunkCells = 24;
    [SerializeField] private int seed = 1;

    [Header("Surface")]
    [Tooltip("Height range of hills and valleys.")]
    [Min(0f)] [SerializeField] private float surfaceAmplitude = 32f;
    [Min(0.0001f)] [SerializeField] private float surfaceFrequency = 0.013f;
    [Tooltip("Sharp ridge lines on top of the rolling hills.")]
    [Range(0f, 1f)] [SerializeField] private float ridges = 0.7f;
    [Tooltip("3D noise for overhangs, ledges and lumps.")]
    [Min(0f)] [SerializeField] private float overhangAmplitude = 5f;
    [Min(0.001f)] [SerializeField] private float overhangFrequency = 0.06f;
    [Tooltip("Flat, safe ground around the spawn point (this transform).")]
    [Min(0f)] [SerializeField] private float spawnFlatRadius = 18f;
    [Min(0f)] [SerializeField] private float spawnBlend = 30f;
    [Tooltip("Cliffs that wall in the edge of the world.")]
    [Min(0f)] [SerializeField] private float rimHeight = 30f;

    [Header("Inner World")]
    [Min(1f)] [SerializeField] private float crustThickness = 9f;
    [Tooltip("Open space between the crust underside and the inner ground.")]
    [Min(2f)] [SerializeField] private float innerGap = 20f;
    [Tooltip("How strongly surface hills hang down from the inner ceiling.")]
    [Range(0f, 1f)] [SerializeField] private float ceilingMirror = 0.8f;
    [Tooltip("How strongly inner ground heights are the reverse of the surface.")]
    [Range(0f, 1.5f)] [SerializeField] private float innerInvert = 1f;
    [Min(0f)] [SerializeField] private float innerDetail = 5f;

    [Header("Caves")]
    [Min(0.001f)] [SerializeField] private float caveFrequency = 0.025f;
    [Tooltip("Tunnel thickness. 0 disables worm caves.")]
    [Range(0f, 0.2f)] [SerializeField] private float caveWidth = 0.12f;
    [Tooltip("Caves only form within this depth of a rock surface, so they open onto the world.")]
    [Min(0f)] [SerializeField] private float caveDepth = 14f;
    [Range(0, 16)] [SerializeField] private int sinkholeCount = 6;
    [SerializeField] private Vector2 sinkholeRadius = new Vector2(4f, 7f);
    [Tooltip("A guaranteed sinkhole this far ahead (+z) of the spawn. 0 = none.")]
    [Min(0f)] [SerializeField] private float firstSinkholeDistance = 45f;

    [Header("Rendering")]
    [SerializeField] private Material surfaceMaterial;
    [SerializeField] private Material innerMaterial;
    [Tooltip("Layer for the generated chunks; Ground lets the player jump and creatures grip.")]
    [SerializeField] private int chunkLayer = 7;

    [Header("Loading")]
    [Tooltip("Chunks within this distance of the spawn are generated before the first frame.")]
    [Min(0f)] [SerializeField] private float blockingRadius = 40f;
    [Range(1, 32)] [SerializeField] private int meshesPerFrame = 6;
    [Tooltip("Teleport the player back to spawn if they fall out of the world.")]
    [SerializeField] private bool respawnFallingPlayer = true;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private int chunksBuilt;
    [SerializeField] private int chunksTotal;

    private struct Sinkhole
    {
        public Vector2 position;
        public float radius;
    }

    private sealed class ChunkResult
    {
        public Vector3Int coord;
        public Vector3[] vertices;
        public int[] surfaceTriangles;
        public int[] innerTriangles;
    }

    private static readonly Vector3Int[] CornerOffsets =
    {
        new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 1), new Vector3Int(0, 0, 1),
        new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0), new Vector3Int(1, 1, 1), new Vector3Int(0, 1, 1),
    };

    private static readonly int[,] EdgeConnections =
    {
        { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
        { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
        { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 },
    };

    private readonly ConcurrentQueue<ChunkResult> finished = new ConcurrentQueue<ChunkResult>();
    private readonly List<GameObject> chunkObjects = new List<GameObject>();
    private readonly List<Mesh> chunkMeshes = new List<Mesh>();
    private Sinkhole[] sinkholes;
    private Vector3 gridMin;
    private Vector3Int chunkCount;
    private float yMin, yMax;
    private Vector2 noiseOffset;
    private Rigidbody playerBody;
    private Vector3 spawnPosition;
    private bool generating;

    /// <summary>Raised on the main thread after each chunk object and its mesh are created.</summary>
    public event System.Action<GameObject, Mesh> ChunkCreated;

    private void Start()
    {
        Rigidbody[] bodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
        foreach (Rigidbody body in bodies)
            if (body.GetComponent<SmoothFirstPersonController>() != null) playerBody = body;
        if (playerBody != null) spawnPosition = playerBody.position;

        Generate(blockUntilDone: false);
        PlacePlayerOnSurface();
    }

    /// <summary>Stands the player on the top surface under the spawn, whatever its exact height.</summary>
    private void PlacePlayerOnSurface()
    {
        if (playerBody == null) return;
        Physics.SyncTransforms();
        Vector3 from = new Vector3(spawnPosition.x, transform.position.y + yMax + 5f, spawnPosition.z);
        float distance = yMax - yMin + 10f;
        RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore);
        float bestY = float.MinValue;
        foreach (RaycastHit hit in hits)
            if (hit.collider.transform.IsChildOf(transform) && hit.point.y > bestY) bestY = hit.point.y;
        if (bestY == float.MinValue) return;

        Collider playerCollider = playerBody.GetComponent<Collider>();
        float halfHeight = playerCollider != null ? playerCollider.bounds.extents.y : 1f;
        spawnPosition = new Vector3(spawnPosition.x, bestY + halfHeight + 0.05f, spawnPosition.z);
        playerBody.position = spawnPosition;
        playerBody.transform.position = spawnPosition;
        playerBody.linearVelocity = Vector3.zero;
    }

    private void Update()
    {
        for (int i = 0; i < meshesPerFrame && finished.TryDequeue(out ChunkResult result); i++)
            CreateChunkObject(result);

        if (respawnFallingPlayer && playerBody != null && playerBody.position.y < transform.position.y + yMin - 20f)
        {
            playerBody.position = spawnPosition + Vector3.up;
            playerBody.linearVelocity = Vector3.zero;
        }
    }

    private void OnDestroy() => Clear();

    [ContextMenu("Preview In Editor")]
    private void PreviewInEditor() => Generate(blockUntilDone: true);

    [ContextMenu("Clear Preview")]
    private void Clear()
    {
        foreach (GameObject chunk in chunkObjects)
            if (chunk != null)
            {
                if (Application.isPlaying) Destroy(chunk);
                else DestroyImmediate(chunk);
            }
        foreach (Mesh mesh in chunkMeshes)
            if (mesh != null)
            {
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
        chunkObjects.Clear();
        chunkMeshes.Clear();

        // Editor previews are not saved, but can survive into Play Mode; remove leftovers too.
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (!child.name.StartsWith("World Chunk")) continue;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
        while (finished.TryDequeue(out _)) { }
        chunksBuilt = 0;
    }

    /// <summary>Starts generating every chunk. Nearby chunks are always finished before returning.</summary>
    public void Generate(bool blockUntilDone)
    {
        if (generating && Application.isPlaying) return;
        Clear();
        PrepareLayout();

        var near = new List<Vector3Int>();
        var far = new List<Vector3Int>();
        Vector3 spawnLocal = transform.InverseTransformPoint(Application.isPlaying && playerBody != null ? spawnPosition : transform.position);
        for (int cx = 0; cx < chunkCount.x; cx++)
        for (int cy = 0; cy < chunkCount.y; cy++)
        for (int cz = 0; cz < chunkCount.z; cz++)
        {
            var coord = new Vector3Int(cx, cy, cz);
            Vector3 centre = gridMin + (new Vector3(cx, 0f, cz) + new Vector3(0.5f, 0f, 0.5f)) * (chunkCells * cellSize);
            float distance = new Vector2(centre.x - spawnLocal.x, centre.z - spawnLocal.z).magnitude;
            (blockUntilDone || distance <= blockingRadius ? near : far).Add(coord);
        }
        far.Sort((a, b) => ChunkDistance(a, spawnLocal).CompareTo(ChunkDistance(b, spawnLocal)));
        chunksTotal = near.Count + far.Count;

        // Ground under the player must exist on the first physics step.
        Parallel.ForEach(near, coord => Enqueue(BuildChunk(coord)));
        while (finished.TryDequeue(out ChunkResult result)) CreateChunkObject(result);

        if (far.Count == 0) return;
        generating = true;
        Task.Run(() =>
        {
            Parallel.ForEach(far, new ParallelOptions { MaxDegreeOfParallelism = System.Math.Max(1, System.Environment.ProcessorCount - 1) },
                coord => Enqueue(BuildChunk(coord)));
            generating = false;
        });
    }

    private float ChunkDistance(Vector3Int coord, Vector3 spawnLocal)
    {
        Vector3 centre = gridMin + (new Vector3(coord.x, 0f, coord.z) + new Vector3(0.5f, 0f, 0.5f)) * (chunkCells * cellSize);
        return new Vector2(centre.x - spawnLocal.x, centre.z - spawnLocal.z).sqrMagnitude;
    }

    private void Enqueue(ChunkResult result)
    {
        if (result != null) finished.Enqueue(result);
    }

    // ---------------- Layout ----------------

    private void PrepareLayout()
    {
        var random = new System.Random(seed);
        noiseOffset = new Vector2((float)random.NextDouble() * 1000f, (float)random.NextDouble() * 1000f);

        float maxHill = surfaceAmplitude * (1f + ridges) + rimHeight + overhangAmplitude + 4f;
        yMax = maxHill;
        yMin = -crustThickness - innerGap - surfaceAmplitude * (1f + ridges) * Mathf.Max(innerInvert, ceilingMirror) - innerDetail - caveDepth - 4f;

        float chunkSize = chunkCells * cellSize;
        chunkCount = new Vector3Int(
            Mathf.CeilToInt(worldSize.x / chunkSize),
            Mathf.CeilToInt((yMax - yMin) / chunkSize),
            Mathf.CeilToInt(worldSize.y / chunkSize));
        gridMin = new Vector3(-chunkCount.x * chunkSize * 0.5f, yMin, -chunkCount.z * chunkSize * 0.5f);

        var holes = new List<Sinkhole>();
        if (firstSinkholeDistance > 0f)
            holes.Add(new Sinkhole { position = new Vector2(0f, firstSinkholeDistance), radius = sinkholeRadius.y });
        int attempts = 0;
        while (holes.Count < sinkholeCount + (firstSinkholeDistance > 0f ? 1 : 0) && attempts++ < 200)
        {
            var position = new Vector2(
                ((float)random.NextDouble() - 0.5f) * worldSize.x * 0.8f,
                ((float)random.NextDouble() - 0.5f) * worldSize.y * 0.8f);
            if (position.magnitude < spawnFlatRadius + spawnBlend) continue;
            bool crowded = false;
            foreach (Sinkhole other in holes)
                crowded |= Vector2.Distance(other.position, position) < 30f;
            if (crowded) continue;
            float radius = Mathf.Lerp(sinkholeRadius.x, sinkholeRadius.y, (float)random.NextDouble());
            holes.Add(new Sinkhole { position = position, radius = radius });
        }
        sinkholes = holes.ToArray();
    }

    // ---------------- Density field (thread safe: pure math only) ----------------

    /// <summary>Surface height above this transform at a local column.</summary>
    private float SurfaceHeight(float x, float z)
    {
        Vector2 p = new Vector2(x, z) * surfaceFrequency + noiseOffset;
        // Domain warp so hills do not line up on a grid.
        Vector2 warp = new Vector2(Fbm2(p * 0.7f + new Vector2(13.1f, 7.7f), 3), Fbm2(p * 0.7f + new Vector2(-4.3f, 21.9f), 3)) * 1.6f;
        p += warp;
        float rolling = Fbm2(p, 5);
        float ridge = 1f - Mathf.Abs(Fbm2(p * 1.7f + new Vector2(31.7f, -12.4f), 4) * 2f);
        float height = (rolling * 1.4f + ridge * ridge * ridges - 0.25f * ridges) * surfaceAmplitude;

        float flat = Mathf.SmoothStep(0f, 1f, (new Vector2(x, z).magnitude - spawnFlatRadius) / Mathf.Max(spawnBlend, 0.01f));
        height *= flat;

        // Cliffs around the edge of the world.
        float edge = Mathf.Max(Mathf.Abs(x) / (worldSize.x * 0.5f), Mathf.Abs(z) / (worldSize.y * 0.5f));
        height += Mathf.SmoothStep(0f, 1f, (edge - 0.82f) / 0.14f) * rimHeight;
        return height;
    }

    private float CeilingHeight(float h) => -crustThickness - h * ceilingMirror;

    private float InnerGroundHeight(float x, float z, float h)
    {
        Vector2 p = new Vector2(x, z) * surfaceFrequency * 2.3f + noiseOffset * 1.37f;
        float detail = Fbm2(p, 4) * innerDetail;
        float ground = -crustThickness - innerGap - h * innerInvert + detail;
        // Keep a minimum cave height so the inner world never pinches shut.
        return Mathf.Min(ground, CeilingHeight(h) - 6f);
    }

    /// <summary>Positive inside rock.</summary>
    private float Density(Vector3 p, float h, float ceiling, float floor)
    {
        float top = h - p.y;
        float under = p.y - ceiling;
        float crust = Mathf.Min(top, under);
        float ground = floor - p.y;
        float density = Mathf.Max(crust, ground);

        // Seal the inner world at the edge of the map (but leave the sky open).
        float edge = Mathf.Max(Mathf.Abs(p.x) / (worldSize.x * 0.5f), Mathf.Abs(p.z) / (worldSize.y * 0.5f));
        if (edge > 0.9f)
            density = Mathf.Max(density, Mathf.Min((edge - 0.93f) * 200f, top));

        // 3D noise only near a surface, where it can change the shape; none on the spawn flat.
        float spawnDistance = new Vector2(p.x, p.z).magnitude;
        float wild = Mathf.SmoothStep(0f, 1f, (spawnDistance - spawnFlatRadius) / Mathf.Max(spawnBlend, 0.01f));
        if (wild > 0f && Mathf.Abs(density) < overhangAmplitude * 1.2f + 1f)
            density += Fbm3(p * overhangFrequency + new Vector3(noiseOffset.x, 0f, noiseOffset.y), 2) * overhangAmplitude * 2f * wild;

        // Worm tunnels: where two noise fields are both near zero.
        if (caveWidth > 0f && density > 0f && density < caveDepth)
        {
            if (spawnDistance > spawnFlatRadius + spawnBlend * 0.5f)
            {
                Vector3 q = p * caveFrequency + new Vector3(noiseOffset.y, 3.7f, noiseOffset.x);
                float a = ValueNoise3(q) - 0.5f;
                float b = ValueNoise3(q * 1.13f + new Vector3(41.3f, 17.9f, -8.4f)) - 0.5f;
                float tunnel = (Mathf.Sqrt(a * a + b * b) - caveWidth) / caveFrequency * 0.5f;
                density = Mathf.Min(density, tunnel);
            }
        }

        // Sinkholes: wobbly shafts through the crust, stopping above the inner ground.
        if (p.y > floor + 1.5f)
        {
            foreach (Sinkhole hole in sinkholes)
            {
                float dx = p.x - hole.position.x, dz = p.y * 0.07f + p.z - hole.position.y;
                float wobble = 1f + 0.25f * Mathf.Sin(p.y * 0.35f + hole.position.x);
                float shaft = Mathf.Sqrt(dx * dx + dz * dz) - hole.radius * wobble;
                density = Mathf.Min(density, shaft);
            }
        }
        return density;
    }

    // ---------------- Chunk meshing (worker threads) ----------------

    private ChunkResult BuildChunk(Vector3Int coord)
    {
        int n = chunkCells + 1;
        Vector3 origin = gridMin + new Vector3(coord.x, coord.y, coord.z) * (chunkCells * cellSize);

        // Column heights once per (x, z).
        var tops = new float[n * n];
        var ceilings = new float[n * n];
        var floors = new float[n * n];
        float maxTop = float.MinValue, minCeiling = float.MaxValue, maxFloor = float.MinValue, minFloor = float.MaxValue;
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            float wx = origin.x + x * cellSize, wz = origin.z + z * cellSize;
            float h = SurfaceHeight(wx, wz);
            int column = z * n + x;
            tops[column] = h;
            ceilings[column] = CeilingHeight(h);
            floors[column] = InnerGroundHeight(wx, wz, h);
            maxTop = Mathf.Max(maxTop, h);
            minCeiling = Mathf.Min(minCeiling, ceilings[column]);
            maxFloor = Mathf.Max(maxFloor, floors[column]);
            minFloor = Mathf.Min(minFloor, floors[column]);
        }

        // Skip chunks that are certainly empty sky or deep solid bedrock.
        float band = overhangAmplitude * 2.5f + 2f;
        float chunkBottom = origin.y, chunkTop = origin.y + chunkCells * cellSize;
        if (chunkBottom > maxTop + band) return null;
        if (chunkTop < minFloor - caveDepth - band) return null;

        var density = new float[n * n * n];
        bool anyPositive = false, anyNegative = false;
        for (int y = 0; y < n; y++)
        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int column = z * n + x;
            var p = new Vector3(origin.x + x * cellSize, origin.y + y * cellSize, origin.z + z * cellSize);
            float value = Density(p, tops[column], ceilings[column], floors[column]);
            density[(y * n + z) * n + x] = value;
            anyPositive |= value > 0f;
            anyNegative |= value <= 0f;
        }
        if (!anyPositive || !anyNegative) return null;

        var vertices = new List<Vector3>();
        var surfaceTriangles = new List<int>();
        var innerTriangles = new List<int>();
        var edgeLookup = new Dictionary<int, int>();
        var cornerIndices = new int[8];
        var edgeVertices = new int[12];

        for (int z = 0; z < chunkCells; z++)
        for (int y = 0; y < chunkCells; y++)
        for (int x = 0; x < chunkCells; x++)
        {
            int cubeIndex = 0;
            for (int c = 0; c < 8; c++)
            {
                Vector3Int o = CornerOffsets[c];
                int sample = ((y + o.y) * n + (z + o.z)) * n + (x + o.x);
                cornerIndices[c] = sample;
                if (density[sample] > 0f) cubeIndex |= 1 << c;
            }

            int edgeMask = MarchingCubesTables.EdgeTable[cubeIndex];
            if (edgeMask == 0) continue;

            for (int e = 0; e < 12; e++)
            {
                if ((edgeMask & (1 << e)) == 0) continue;
                int a = cornerIndices[EdgeConnections[e, 0]];
                int b = cornerIndices[EdgeConnections[e, 1]];
                int low = Mathf.Min(a, b), high = Mathf.Max(a, b);
                // Each sample has at most three edges to higher neighbours: +x, +z, +y.
                int axis = high - low == 1 ? 0 : high - low == n ? 1 : 2;
                int key = low * 3 + axis;
                if (!edgeLookup.TryGetValue(key, out int vertexIndex))
                {
                    float da = density[a], db = density[b];
                    float t = Mathf.Abs(da - db) < 1e-6f ? 0.5f : Mathf.Clamp01(da / (da - db));
                    vertexIndex = vertices.Count;
                    vertices.Add(Vector3.Lerp(SampleLocal(a, n), SampleLocal(b, n), t) * cellSize);
                    edgeLookup.Add(key, vertexIndex);
                }
                edgeVertices[e] = vertexIndex;
            }

            for (int i = 0; MarchingCubesTables.TriTable[cubeIndex, i] != -1; i += 3)
            {
                int v0 = edgeVertices[MarchingCubesTables.TriTable[cubeIndex, i]];
                int v1 = edgeVertices[MarchingCubesTables.TriTable[cubeIndex, i + 1]];
                int v2 = edgeVertices[MarchingCubesTables.TriTable[cubeIndex, i + 2]];

                // Above the middle of the crust is the surface world; below it, the inner world.
                Vector3 centroid = (vertices[v0] + vertices[v1] + vertices[v2]) / 3f;
                int cx = Mathf.Clamp(Mathf.RoundToInt(centroid.x / cellSize), 0, n - 1);
                int cz = Mathf.Clamp(Mathf.RoundToInt(centroid.z / cellSize), 0, n - 1);
                int column = cz * n + cx;
                float crustMiddle = (tops[column] + ceilings[column]) * 0.5f;
                List<int> target = origin.y + centroid.y >= crustMiddle ? surfaceTriangles : innerTriangles;
                target.Add(v0);
                target.Add(v1);
                target.Add(v2);
            }
        }

        if (vertices.Count == 0) return null;
        return new ChunkResult
        {
            coord = coord,
            vertices = vertices.ToArray(),
            surfaceTriangles = surfaceTriangles.ToArray(),
            innerTriangles = innerTriangles.ToArray(),
        };
    }

    private static Vector3 SampleLocal(int sample, int n)
    {
        int x = sample % n;
        int z = (sample / n) % n;
        int y = sample / (n * n);
        return new Vector3(x, y, z);
    }

    // ---------------- Main thread ----------------

    private void CreateChunkObject(ChunkResult result)
    {
        var mesh = new Mesh
        {
            name = $"World Chunk {result.coord}",
            hideFlags = HideFlags.DontSave,
            indexFormat = result.vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };
        mesh.vertices = result.vertices;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(result.surfaceTriangles, 0);
        mesh.SetTriangles(result.innerTriangles, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var chunk = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))
        {
            hideFlags = HideFlags.DontSave,
            layer = chunkLayer,
        };
        chunk.transform.SetParent(transform, false);
        chunk.transform.localPosition = gridMin + new Vector3(result.coord.x, result.coord.y, result.coord.z) * (chunkCells * cellSize);
        chunk.GetComponent<MeshFilter>().sharedMesh = mesh;
        chunk.GetComponent<MeshRenderer>().sharedMaterials = new[] { surfaceMaterial, innerMaterial != null ? innerMaterial : surfaceMaterial };
        chunk.GetComponent<MeshCollider>().sharedMesh = mesh;

        chunkObjects.Add(chunk);
        chunkMeshes.Add(mesh);
        chunksBuilt++;
        ChunkCreated?.Invoke(chunk, mesh);
    }

    // ---------------- Noise (thread safe) ----------------

    private static float Hash(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }

    private static float ValueNoise2(Vector2 p)
    {
        int ix = Mathf.FloorToInt(p.x), iz = Mathf.FloorToInt(p.y);
        float fx = p.x - ix, fz = p.y - iz;
        float ux = fx * fx * (3f - 2f * fx), uz = fz * fz * (3f - 2f * fz);
        float a = Hash(ix, 0, iz), b = Hash(ix + 1, 0, iz), c = Hash(ix, 0, iz + 1), d = Hash(ix + 1, 0, iz + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uz);
    }

    private static float ValueNoise3(Vector3 p)
    {
        int ix = Mathf.FloorToInt(p.x), iy = Mathf.FloorToInt(p.y), iz = Mathf.FloorToInt(p.z);
        float fx = p.x - ix, fy = p.y - iy, fz = p.z - iz;
        float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy), uz = fz * fz * (3f - 2f * fz);
        float x00 = Mathf.Lerp(Hash(ix, iy, iz), Hash(ix + 1, iy, iz), ux);
        float x10 = Mathf.Lerp(Hash(ix, iy + 1, iz), Hash(ix + 1, iy + 1, iz), ux);
        float x01 = Mathf.Lerp(Hash(ix, iy, iz + 1), Hash(ix + 1, iy, iz + 1), ux);
        float x11 = Mathf.Lerp(Hash(ix, iy + 1, iz + 1), Hash(ix + 1, iy + 1, iz + 1), ux);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, uy), Mathf.Lerp(x01, x11, uy), uz);
    }

    /// <summary>Roughly -0.5..0.5.</summary>
    private static float Fbm2(Vector2 p, int octaves)
    {
        float sum = 0f, amplitude = 0.5f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * (ValueNoise2(p) - 0.5f);
            p = p * 2.03f + new Vector2(17.3f, 9.1f);
            amplitude *= 0.5f;
        }
        return sum;
    }

    /// <summary>Roughly -0.5..0.5.</summary>
    private static float Fbm3(Vector3 p, int octaves)
    {
        float sum = 0f, amplitude = 0.5f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * (ValueNoise3(p) - 0.5f);
            p = p * 2.03f + new Vector3(5.2f, 1.3f, 7.7f);
            amplitude *= 0.5f;
        }
        return sum;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
        Gizmos.DrawWireCube(new Vector3(0f, 0f, 0f), new Vector3(worldSize.x, 1f, worldSize.y));
        Gizmos.color = new Color(1f, 0.78f, 0.015f, 0.6f);
        Gizmos.DrawWireCube(new Vector3(0f, -crustThickness - innerGap, 0f), new Vector3(worldSize.x, 1f, worldSize.y));
    }
}
