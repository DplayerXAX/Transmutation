using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Grows pickable fruit and flowers in the inner world as ProceduralWorld chunks are built.
/// Fruit bunches hang on thin stalks from the cave ceiling; flowers stand on floors and walls.
/// Each fruit / flower is its own object with a CaveHarvestItem, so the player can pick it with E.
/// Stalks stay behind and are merged into one mesh per chunk. Only grows in Play Mode.
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

    [Header("Runtime State (Read Only)")]
    [SerializeField] private int itemsGrown;

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

        WorldDecorator.Ground ground = WorldDecorator.BuildGround(chunk.transform, chunkMesh.vertices, chunkMesh.normals, chunkMesh.GetTriangles(1));
        if (ground.totalArea <= 0f) return;

        Vector3 key = chunk.transform.localPosition;
        var random = new System.Random(unchecked(seed * 92821 ^ Mathf.RoundToInt(key.x) * 19349663 ^ Mathf.RoundToInt(key.y) * 83492791 ^ Mathf.RoundToInt(key.z) * 2971215));
        var stalks = new List<CombineInstance>();

        int fruitClusters = Count(ground.totalArea * fruitClustersPer1000m2 / 1000f, random);
        for (int i = 0; i < fruitClusters; i++)
            GrowCluster(ground, random, chunk.transform, fruit: true, stalks);

        int flowerClusters = Count(ground.totalArea * flowerClustersPer1000m2 / 1000f, random);
        for (int i = 0; i < flowerClusters; i++)
            GrowCluster(ground, random, chunk.transform, fruit: false, stalks);

        if (stalks.Count > 0) CreateStalks(chunk, stalks);
    }

    private void GrowCluster(WorldDecorator.Ground ground, System.Random random, Transform chunk, bool fruit, List<CombineInstance> stalks)
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
            if (fruit) GrowFruit(point, random, chunk, stalks);
            else GrowFlower(point, pointNormal, random, chunk);
        }
    }

    private void GrowFruit(Vector3 ceilingPoint, System.Random random, Transform chunk, List<CombineInstance> stalks)
    {
        float length = Range(random, stalkLength.x, stalkLength.y);
        // The stalk mesh grows along +y; turn it upside down so it hangs from the ceiling.
        Vector3 top = ceilingPoint + Vector3.up * 0.05f;
        Quaternion hang = Quaternion.Euler(180f + Range(random, -8f, 8f), Range(random, 0f, 360f), 0f);
        stalks.Add(new CombineInstance
        {
            mesh = ToMesh(stalkShape, "Stalk"),
            transform = chunk.worldToLocalMatrix * Matrix4x4.TRS(top, hang, Vector3.one * length),
        });

        Vector3 end = top + hang * Vector3.up * length;
        Mesh mesh = fruitMeshes[random.Next(fruitMeshes.Length)];
        CreateItem("Cave Fruit", mesh, fruitMaterial, end, Quaternion.Euler(0f, Range(random, 0f, 360f), 0f),
            Range(random, 0.85f, 1.3f), new Vector3(0f, -0.18f, 0f), 0.16f, chunk);
    }

    private void GrowFlower(Vector3 point, Vector3 normal, System.Random random, Transform chunk)
    {
        Vector3 up = Vector3.Slerp(Vector3.up, normal, 0.6f).normalized;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, Range(random, 0f, 360f), 0f);
        Mesh mesh = flowerMeshes[random.Next(flowerMeshes.Length)];
        float scale = Range(random, 0.9f, 1.5f);
        CreateItem("Cave Flower", mesh, flowerMaterial, point - up * 0.02f, rotation, scale,
            new Vector3(0f, mesh.bounds.max.y * 0.75f, 0f), 0.13f, chunk);
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

    private void CreateStalks(GameObject chunk, List<CombineInstance> stalks)
    {
        var mesh = new Mesh { name = "Harvest Stalks " + chunk.name, hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(stalks.ToArray(), true, true);
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
