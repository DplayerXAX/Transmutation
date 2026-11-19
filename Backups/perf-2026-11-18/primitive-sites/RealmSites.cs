using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared helpers for the exploration sites of the Human Realm (the area round the Hanging Spire and
/// the Bone Kiln): finding a free spot near the spire, simple faceted meshes, and seed rewards.
/// </summary>
public static class RealmSites
{
    private static readonly List<(Vector3 centre, float radius)> Claimed = new List<(Vector3, float)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Claimed.Clear();

    /// <summary>
    /// True once the world layout exists and the landmarks this area is built round have placed themselves,
    /// or after a timeout so a scene without them still gets its sites.
    /// </summary>
    public static bool ReadyToPlace(ProceduralWorld world, float waited)
    {
        if (world == null || !world.LayoutReady) return false;
        if (waited > 12f) return true;
        HangingSpire spire = Object.FindFirstObjectByType<HangingSpire>();
        if (spire != null && !spire.Built) return false;
        BoneKiln kiln = Object.FindFirstObjectByType<BoneKiln>();
        return kiln == null || kiln.Root != null;
    }

    /// <summary>Centre of the area: the spire's foot, or the world centre without a spire.</summary>
    public static Vector3 AreaCentre(ProceduralWorld world)
    {
        HangingSpire spire = Object.FindFirstObjectByType<HangingSpire>();
        if (spire != null && spire.Built) return world.SinkholeCentre(spire.SinkholeIndex, world.transform.position.y, out _);
        return world.transform.position;
    }

    /// <summary>Surface height of the procedural world at a point (no chunk needs to exist).</summary>
    public static float Surface(ProceduralWorld world, Vector3 point)
    {
        world.ColumnHeights(point, out float surface, out _, out _);
        return surface;
    }

    /// <summary>
    /// Finds a spot between minDistance and maxDistance from the area centre whose footprint stays inside
    /// the map and clear of sinkholes, the kiln and other claimed sites, and whose ground varies by no more
    /// than maxRelief metres. Claims it when found.
    /// </summary>
    public static bool FindSpot(ProceduralWorld world, float footprint, float minDistance, float maxDistance, int seed, out Vector3 spot,
        float maxRelief = float.PositiveInfinity)
    {
        var random = new System.Random(seed);
        Vector3 centre = AreaCentre(world);
        Vector3 worldCentre = world.transform.position;
        Vector2 half = world.WorldSize * 0.5f * 0.7f;
        BoneKiln kiln = Object.FindFirstObjectByType<BoneKiln>();

        for (int attempt = 0; attempt < 400; attempt++)
        {
            // Widen the search a little after every failed ring of attempts.
            float stretch = 1f + attempt / 100f;
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float distance = Mathf.Lerp(minDistance, maxDistance * stretch, (float)random.NextDouble());
            Vector3 candidate = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

            if (Mathf.Abs(candidate.x - worldCentre.x) + footprint > half.x || Mathf.Abs(candidate.z - worldCentre.z) + footprint > half.y) continue;
            if (!ClearOfSinkholes(world, candidate, footprint)) continue;
            if (kiln != null && kiln.Root != null)
            {
                if (Flat(candidate - kiln.Root.position).magnitude < footprint + 32f) continue;
                if (Flat(candidate - kiln.Head).magnitude < footprint + 20f) continue;
            }
            bool crowded = false;
            foreach (var claim in Claimed) crowded |= Flat(candidate - claim.centre).magnitude < footprint + claim.radius + 14f;
            if (crowded) continue;

            if (!float.IsPositiveInfinity(maxRelief))
            {
                SurfaceRange(world, candidate, footprint, out float low, out float high);
                if (high - low > maxRelief * Mathf.Min(stretch, 2f)) continue;
            }

            candidate.y = Surface(world, candidate);
            Claimed.Add((candidate, footprint));
            spot = candidate;
            return true;
        }
        spot = default;
        return false;
    }

    private static bool ClearOfSinkholes(ProceduralWorld world, Vector3 point, float footprint)
    {
        for (int i = 0; i < world.SinkholeCount; i++)
        {
            Vector3 hole = world.SinkholeCentre(i, world.transform.position.y, out float radius);
            if (Flat(point - hole).magnitude < radius + footprint + 12f) return false;
        }
        return true;
    }

    /// <summary>Lowest and highest surface height under a disc of the given radius.</summary>
    public static void SurfaceRange(ProceduralWorld world, Vector3 centre, float radius, out float low, out float high)
    {
        low = float.MaxValue;
        high = float.MinValue;
        for (int ring = 0; ring <= 2; ring++)
        for (int i = 0; i < 12; i++)
        {
            float angle = i / 12f * Mathf.PI * 2f;
            Vector3 p = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius * ring / 2f;
            float h = Surface(world, p);
            low = Mathf.Min(low, h);
            high = Mathf.Max(high, h);
        }
    }

    public static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>The real ground (terrain chunks only) under a point, with its normal. False if no chunk is there yet.</summary>
    public static bool GroundAt(ProceduralWorld world, Vector3 point, out Vector3 hit, out Vector3 normal)
    {
        float surface = Surface(world, point);
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(point.x, surface + 40f, point.z), Vector3.down, 90f, 1 << world.TerrainLayer, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        hit = new Vector3(point.x, surface, point.z);
        normal = Vector3.up;
        bool found = false;
        foreach (RaycastHit h in hits)
        {
            // The top surface near the expected height, not a cave floor far below.
            if (!world.OwnsCollider(h.collider) || h.point.y <= best || h.point.y < surface - 15f) continue;
            best = h.point.y;
            hit = h.point;
            normal = h.normal;
            found = true;
        }
        return found;
    }

    public static bool InSinkhole(ProceduralWorld world, Vector3 point, float margin)
    {
        for (int i = 0; i < world.SinkholeCount; i++)
        {
            Vector3 hole = world.SinkholeCentre(i, point.y, out float radius);
            if (Flat(hole - point).magnitude < radius + margin) return true;
        }
        return false;
    }

    /// <summary>Radius of the sinkhole the point stands over (0 when it is not near one).</summary>
    public static float HoleRadiusAt(ProceduralWorld world, Vector3 point)
    {
        float best = 0f, bestDistance = float.MaxValue;
        for (int i = 0; i < world.SinkholeCount; i++)
        {
            Vector3 hole = world.SinkholeCentre(i, point.y, out float radius);
            float d = Flat(hole - point).magnitude;
            if (d < bestDistance) { bestDistance = d; best = d < radius + 12f ? radius : 0f; }
        }
        return best;
    }

    /// <summary>The spire built at runtime (its root object), with the bounds of everything drawn under it.</summary>
    public static bool SpireBody(out Transform spireRoot, out Bounds bounds)
    {
        GameObject found = GameObject.Find("Hanging Spire (runtime)");
        spireRoot = found != null ? found.transform : null;
        bounds = default;
        if (spireRoot == null) return false;
        bool any = false;
        foreach (Renderer renderer in spireRoot.GetComponentsInChildren<Renderer>())
        {
            if (!any) bounds = renderer.bounds;
            else bounds.Encapsulate(renderer.bounds);
            any = true;
        }
        return any;
    }

    /// <summary>A seed reward the player takes by touching it (saved in LandmarkProgress).</summary>
    public static GameObject Seed(string id, Vector3 position, Material material, Transform parent)
    {
        var holder = new GameObject("Seed " + id);
        holder.transform.SetParent(parent, true);
        holder.transform.position = position;
        GameObject pod = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(pod.GetComponent<Collider>());
        pod.name = "Pod";
        pod.transform.SetParent(holder.transform, false);
        pod.transform.localScale = new Vector3(0.45f, 0.6f, 0.45f);
        if (material != null) pod.GetComponent<MeshRenderer>().sharedMaterial = material;
        var pickup = holder.AddComponent<LandmarkPickup>();
        pickup.kind = LandmarkPickup.Kind.Seed;
        pickup.id = id;
        pickup.hoverHeight = 1f;
        return holder;
    }

    /// <summary>Creates a child object with a mesh, material and (optionally) a mesh collider.</summary>
    public static GameObject Part(string name, Transform parent, Mesh mesh, Material material, int layer, bool collider)
    {
        var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)) { layer = layer };
        part.transform.SetParent(parent, false);
        part.GetComponent<MeshFilter>().sharedMesh = mesh;
        part.GetComponent<MeshRenderer>().sharedMaterial = material;
        if (collider) part.AddComponent<MeshCollider>().sharedMesh = mesh;
        return part;
    }
}

/// <summary>
/// Tiny faceted mesh builder for the realm sites. Every quad gets its own vertices so edges stay hard.
/// uv.x runs 0 (bottom) .. 1 (top) for the WorldDecor shader; uv.y is a per-mesh random value.
/// </summary>
public sealed class FacetMesh
{
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector3> normals = new List<Vector3>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<int> triangles = new List<int>();
    private readonly float bottom, top, piece;

    public FacetMesh(float bottom, float top, float piece)
    {
        this.bottom = bottom;
        this.top = Mathf.Max(top, bottom + 0.01f);
        this.piece = piece;
    }

    /// <summary>Quad a-b-c-d, wound so its front faces the given outward direction.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
        if (Vector3.Dot(normal, outward) < 0f)
        {
            (b, d) = (d, b);
            normal = -normal;
        }
        int start = vertices.Count;
        foreach (Vector3 v in new[] { a, b, c, d })
        {
            vertices.Add(v);
            normals.Add(normal);
            uvs.Add(new Vector2(Mathf.InverseLerp(bottom, top, v.y), piece));
        }
        triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
    }

    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
        if (Vector3.Dot(normal, outward) < 0f)
        {
            (b, c) = (c, b);
            normal = -normal;
        }
        int start = vertices.Count;
        foreach (Vector3 v in new[] { a, b, c })
        {
            vertices.Add(v);
            normals.Add(normal);
            uvs.Add(new Vector2(Mathf.InverseLerp(bottom, top, v.y), piece));
        }
        triangles.AddRange(new[] { start, start + 1, start + 2 });
    }

    /// <summary>A round tube between two points (for silk strands).</summary>
    public void Tube(Vector3 from, Vector3 to, float radius, int sides = 5)
    {
        Vector3 axis = (to - from).normalized;
        Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 other = Vector3.Cross(axis, side);
        for (int i = 0; i < sides; i++)
        {
            float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
            Vector3 o0 = (side * Mathf.Cos(a0) + other * Mathf.Sin(a0)) * radius;
            Vector3 o1 = (side * Mathf.Cos(a1) + other * Mathf.Sin(a1)) * radius;
            Quad(from + o0, from + o1, to + o1, to + o0, (o0 + o1) * 0.5f);
        }
    }

    public Mesh Build(string name)
    {
        var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
