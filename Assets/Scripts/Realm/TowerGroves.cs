using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A different world round each of the spire's two side towers, so every tower has its own place:
/// - Tower 1, a kelp forest: tall wine-to-amber kelp blades swaying in clumps, and jellyfish lanterns drifting between them.
/// - Tower 2, a geode field: split geodes showing agate rings and amethyst hollows, and stepped basalt columns
///   with glowing cracks (their flat tops can be climbed like stairs).
/// Builds once the spire and the ground round its towers have loaded.
/// </summary>
[DisallowMultipleComponent]
public sealed class TowerGroves : MonoBehaviour
{
    [Header("Materials")]
    [SerializeField] private Material kelpMaterial;
    [SerializeField] private Material jellyfishMaterial;
    [SerializeField] private Material geodeMaterial;
    [SerializeField] private Material amethystMaterial;
    [SerializeField] private Material basaltMaterial;

    [Header("Layout")]
    [SerializeField] private int seed = 29;
    [Tooltip("How far from a tower's wall things grow (metres).")]
    [SerializeField] private Vector2 ring = new Vector2(4f, 18f);
    [Range(0, 16)] [SerializeField] private int kelpClumps = 5;
    [Range(0, 12)] [SerializeField] private int jellyfish = 3;
    [Range(0, 12)] [SerializeField] private int geodes = 4;
    [Range(0, 6)] [SerializeField] private int basaltClusters = 2;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool built;
    [SerializeField] private int placed;

    private ProceduralWorld world;
    private Transform root;
    private float waited;
    private RealmDecorLayout.Snapshot layout;
    private readonly List<(Transform body, Vector3 home, float phase)> drifting = new List<(Transform, Vector3, float)>();

    public void Setup(Material kelp, Material jelly, Material geode, Material amethyst, Material basalt)
    {
        kelpMaterial = kelp;
        jellyfishMaterial = jelly;
        geodeMaterial = geode;
        amethystMaterial = amethyst;
        basaltMaterial = basalt;
    }

    private void Update()
    {
        if (built)
        {
            // Jellyfish bob and drift in slow loops.
            float time = Time.time;
            foreach (var (body, home, phase) in drifting)
            {
                if (body == null) continue;
                body.position = home + new Vector3(Mathf.Sin(time * 0.13f + phase) * 1.6f, Mathf.Sin(time * 0.7f + phase * 2f) * 0.5f, Mathf.Cos(time * 0.11f + phase) * 1.6f);
                body.rotation = Quaternion.Euler(Mathf.Sin(time * 0.5f + phase) * 8f, phase * 57f, Mathf.Cos(time * 0.4f + phase) * 8f);
            }
            return;
        }
        if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
        waited += Time.deltaTime;
        HangingSpire spire = FindFirstObjectByType<HangingSpire>();
        if (!RealmSites.ReadyToPlace(world, waited) || spire == null || !spire.Built || spire.StageTowers.Count < 2) return;
        foreach (Transform tower in spire.StageTowers)
            if (waited < 35f && !world.IsRegionReady(tower.position, ring.y + 8f)) return;
        built = true;
        layout = RealmDecorLayout.Capture(world);
        root = new GameObject("Tower Groves").transform;
        var random = new System.Random(seed);
        KelpForest(spire.StageTowers[0], random);
        GeodeField(spire.StageTowers[1], random);
    }

    // ---------------- Placement ----------------

    /// <summary>Radius of a tower at its foot, from what is drawn under it.</summary>
    private static float TowerRadius(Transform tower)
    {
        var renderer = tower.GetComponent<Renderer>();
        if (renderer == null) return 6f;
        Vector3 e = renderer.bounds.extents;
        return Mathf.Clamp(Mathf.Min(e.x, e.z), 3f, 12f);
    }

    /// <summary>A spot on real, fairly flat ground in the ring round a tower.</summary>
    private bool Spot(Transform tower, float radius, System.Random random, float slope, out Vector3 at, out Vector3 normal)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            float d = radius + Mathf.Lerp(ring.x, ring.y, Mathf.Sqrt((float)random.NextDouble()));
            Vector3 p = tower.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d;
            int district = RealmDecorLayout.FlatDistance(tower.position, layout.kelp) < RealmDecorLayout.FlatDistance(tower.position, layout.crystal) ? 0 : 1;
            if (layout.active && (layout.OnApproach(p) || layout.District(p) != district)) continue;
            if (RealmSites.InSinkhole(world, p, 1.5f)) continue;
            if (!RealmSites.GroundAt(world, p, out at, out normal) || normal.y < slope) continue;
            placed++;
            return true;
        }
        at = default;
        normal = Vector3.up;
        return false;
    }

    private bool Near(Vector3 centre, float radius, System.Random random, out Vector3 at, out Vector3 normal)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            Vector3 p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * Mathf.Sqrt((float)random.NextDouble());
            if (layout.active && layout.OnApproach(p)) continue;
            if (RealmSites.InSinkhole(world, p, 1f)) continue;
            if (RealmSites.GroundAt(world, p, out at, out normal) && normal.y > 0.6f) return true;
        }
        at = default;
        normal = Vector3.up;
        return false;
    }

    private GameObject Part(string name, RealmMeshes mesh, Material material, bool shadows, bool collider, Transform parent = null)
    {
        if (mesh.VertexCount == 0 || material == null) return null;
        GameObject part = RealmSites.Part(name, parent != null ? parent : root, mesh.Build(name), material, collider ? 7 : 0, collider);
        if (!shadows) part.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return part;
    }

    private static float Range(System.Random random, float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());

    // ---------------- Tower 1: kelp forest ----------------

    private void KelpForest(Transform tower, System.Random random)
    {
        float radius = TowerRadius(tower);
        var kelp = new RealmMeshes();
        var clumps = new List<Vector3>();
        for (int c = 0; c < kelpClumps; c++)
        {
            if (!Spot(tower, radius, random, 0.6f, out Vector3 at, out _)) continue;
            clumps.Add(at);
            int blades = random.Next(4, 9);
            for (int b = 0; b < blades; b++)
                if (Near(at, 1.6f, random, out Vector3 foot, out _)) Blade(kelp, foot, random);
        }
        GameObject forest = Part("Kelp Forest", kelp, kelpMaterial, true, false);
        if (forest != null) forest.transform.position = Vector3.zero;

        // Jellyfish lanterns drifting above the clumps (each its own object so it can move).
        for (int j = 0; j < jellyfish && clumps.Count > 0; j++)
        {
            Vector3 over = clumps[j % clumps.Count] + new Vector3(Range(random, -3f, 3f), Range(random, 3.5f, 7.5f), Range(random, -3f, 3f));
            var mesh = new RealmMeshes();
            Jellyfish(mesh, Range(random, 0.5f, 1.1f), random);
            var holder = new GameObject("Jellyfish").transform;
            holder.SetParent(root, false);
            holder.position = over;
            GameObject body = Part("Jellyfish Body", mesh, jellyfishMaterial, false, false, holder);
            if (body == null) continue;
            drifting.Add((holder, over, Range(random, 0f, 100f)));
        }
    }

    /// <summary>One kelp blade: a wide ribbon rising in a lazy S, twisting as it goes, widest past the middle.</summary>
    private static void Blade(RealmMeshes mesh, Vector3 foot, System.Random random)
    {
        float height = Range(random, 3f, 8.5f);
        float width = Range(random, 0.18f, 0.38f);
        float piece = (float)random.NextDouble();
        float lean = Range(random, 0f, Mathf.PI * 2f);
        float twist = Range(random, 1.5f, 4f) * (random.Next(2) == 0 ? -1f : 1f);
        const int steps = 14;
        var spine = new Vector3[steps + 1];
        var side = new Vector3[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector3 bend = new Vector3(Mathf.Cos(lean), 0f, Mathf.Sin(lean)) * Mathf.Sin(t * Mathf.PI * 1.3f) * height * 0.12f;
            spine[i] = foot + Vector3.up * (t * height - 0.1f) + bend;
            float a = lean + t * twist;
            side[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }
        // Double sided: one set of vertices per face, normals both ways.
        float length = 0f;
        int[] front = new int[(steps + 1) * 3], back = new int[(steps + 1) * 3];
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            if (i > 0) length += Vector3.Distance(spine[i - 1], spine[i]);
            float w = width * Mathf.Sin(Mathf.Lerp(0.15f, 1f, t) * Mathf.PI * 0.85f) * (1f + 0.15f * Mathf.Sin(t * 23f + piece * 9f));
            Vector3 forward = (spine[Mathf.Min(i + 1, steps)] - spine[Mathf.Max(i - 1, 0)]).normalized;
            Vector3 normal = Vector3.Cross(side[i], forward).normalized;
            for (int k = 0; k < 3; k++)
            {
                float s = k - 1f;
                // A ruffled edge: the sides ripple in and out of the blade's plane.
                Vector3 p = spine[i] + side[i] * s * w + normal * Mathf.Abs(s) * Mathf.Sin(length * 4f + piece * 20f) * w * 0.35f;
                front[i * 3 + k] = mesh.Vertex(p, normal, new Vector2(t, piece), new Vector2(s, length));
                back[i * 3 + k] = mesh.Vertex(p, -normal, new Vector2(t, piece), new Vector2(s, length));
            }
        }
        for (int i = 0; i < steps; i++)
        for (int k = 0; k < 2; k++)
        {
            int a = i * 3 + k, b = a + 1, c = a + 3, d = c + 1;
            mesh.Triangle(front[a], front[b], front[c]);
            mesh.Triangle(front[b], front[d], front[c]);
            mesh.Triangle(back[a], back[c], back[b]);
            mesh.Triangle(back[b], back[c], back[d]);
        }
    }

    /// <summary>A jellyfish lantern round its own origin: a domed bell, a frilled skirt and long trailing tentacles.</summary>
    private static void Jellyfish(RealmMeshes mesh, float size, System.Random random)
    {
        float piece = (float)random.NextDouble();
        mesh.Sphere(Vector3.zero, new Vector3(size, size * 0.62f, size), Quaternion.identity, 8, 16, 0f, piece);
        // Inner bulb, so the bell has a glowing heart.
        mesh.Sphere(Vector3.up * size * 0.1f, Vector3.one * size * 0.35f, Quaternion.identity, 6, 10, 0f, piece);
        int tentacles = random.Next(6, 11);
        for (int i = 0; i < tentacles; i++)
        {
            float a = (i + Range(random, -0.2f, 0.2f)) / tentacles * Mathf.PI * 2f;
            float r = size * Range(random, 0.55f, 0.9f);
            float length = size * Range(random, 2.5f, 5.5f);
            var path = new List<Vector3>();
            for (int k = 0; k <= 10; k++)
            {
                float t = k / 10f;
                float curl = Mathf.Sin(t * 7f + a * 3f) * 0.18f * size * t;
                path.Add(new Vector3(Mathf.Cos(a) * (r * (1f - t * 0.4f) + curl), -size * 0.15f - t * length, Mathf.Sin(a) * (r * (1f - t * 0.4f) + curl)));
            }
            mesh.Tube(path, t => Mathf.Lerp(0.035f, 0.008f, t) * (1f + size), 4, 0f, 1f, piece);
        }
        // Frilly oral arms in the middle, thicker and shorter.
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + piece;
            var path = new List<Vector3>();
            for (int k = 0; k <= 8; k++)
            {
                float t = k / 8f;
                path.Add(new Vector3(Mathf.Cos(a + t * 3f) * 0.15f * size, -size * 0.2f - t * size * 1.8f, Mathf.Sin(a + t * 3f) * 0.15f * size));
            }
            mesh.Tube(path, t => Mathf.Lerp(0.09f, 0.02f, t) * size, 5, 0f, 1f, piece);
        }
    }

    // ---------------- Tower 2: geode field ----------------

    private void GeodeField(Transform tower, System.Random random)
    {
        float radius = TowerRadius(tower);
        var rind = new RealmMeshes();
        var crystals = new RealmMeshes();
        for (int g = 0; g < geodes; g++)
        {
            if (!Spot(tower, radius, random, 0.6f, out Vector3 at, out Vector3 normal)) continue;
            Geode(rind, crystals, at, normal, Range(random, 0.7f, 1.6f), random);
        }
        var basalt = new RealmMeshes();
        for (int c = 0; c < basaltClusters; c++)
        {
            if (!Spot(tower, radius, random, 0.75f, out Vector3 at, out _)) continue;
            BasaltCluster(basalt, at, random);
        }
        GameObject part = Part("Geodes", rind, geodeMaterial, true, true);
        if (part != null) part.transform.position = Vector3.zero;
        part = Part("Geode Amethyst", crystals, amethystMaterial, false, false);
        if (part != null) part.transform.position = Vector3.zero;
        part = Part("Basalt Columns", basalt, basaltMaterial, true, true);
        if (part != null) part.transform.position = Vector3.zero;
    }

    /// <summary>
    /// A geode broken in two: two half shells lying open like a cracked egg, cut faces up,
    /// each with agate rings (drawn by the shader) and a few amethyst points in the hollow.
    /// </summary>
    private static void Geode(RealmMeshes rind, RealmMeshes crystals, Vector3 at, Vector3 normal, float size, System.Random random)
    {
        float yaw = Range(random, 0f, 360f);
        Quaternion ground = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, yaw, 0f);
        for (int half = 0; half < 2; half++)
        {
            // Each half rolls open away from the other.
            float open = Range(random, 15f, 40f) * (half == 0 ? 1f : -1f);
            Quaternion turn = ground * Quaternion.Euler(0f, 0f, open);
            Vector3 centre = at + ground * new Vector3((half == 0 ? -1f : 1f) * size * 0.75f, size * 0.25f, 0f);
            HalfShell(rind, centre, turn, size, random);
            // Amethyst points rising from the hollow.
            int points = random.Next(4, 9);
            for (int i = 0; i < points; i++)
            {
                float a = Range(random, 0f, Mathf.PI * 2f);
                float r = size * Range(random, 0f, 0.35f);
                Vector3 local = new Vector3(Mathf.Cos(a) * r, -size * 0.15f, Mathf.Sin(a) * r);
                Quaternion tilt = turn * Quaternion.Euler(Range(random, -25f, 25f), 0f, Range(random, -25f, 25f));
                crystals.Crystal(centre + turn * local, tilt, size * Range(random, 0.25f, 0.55f), size * Range(random, 0.05f, 0.1f), 6, (float)random.NextDouble(), random);
            }
        }
    }

    /// <summary>A bumpy half sphere (rind, uv1.x = 0) closed by a flat face on top (uv1.x = 1, uv1.y = distance from centre).</summary>
    private static void HalfShell(RealmMeshes mesh, Vector3 centre, Quaternion rotation, float size, System.Random random)
    {
        const int rings = 6, sides = 14;
        float piece = Mathf.Min(size, 1f);
        float bumpSeed = Range(random, 0f, 50f);
        var rim = new int[sides + 1];
        var grid = new int[rings + 1, sides + 1];
        for (int i = 0; i <= rings; i++)
        {
            float polar = Mathf.PI * 0.5f + i / (float)rings * Mathf.PI * 0.5f; // equator .. bottom
            for (int j = 0; j <= sides; j++)
            {
                float az = j / (float)sides * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(polar) * Mathf.Cos(az), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(az));
                float bump = 1f + 0.12f * Mathf.Sin(az * 3f + bumpSeed) * Mathf.Sin(polar * 4f + bumpSeed);
                Vector3 p = centre + rotation * (dir * size * bump);
                grid[i, j] = mesh.Vertex(p, rotation * dir, new Vector2(0f, piece), new Vector2(0f, 0f));
            }
        }
        for (int i = 0; i < rings; i++)
        for (int j = 0; j < sides; j++)
        {
            int a = grid[i, j], b = grid[i, j + 1], c = grid[i + 1, j], d = grid[i + 1, j + 1];
            mesh.Triangle(a, b, c);
            mesh.Triangle(b, d, c);
        }
        // The flat cut face, with the rim following the bumps so it seals.
        Vector3 up = rotation * Vector3.up;
        int middle = mesh.Vertex(centre, up, new Vector2(1f, piece), new Vector2(1f, 0f));
        for (int j = 0; j <= sides; j++)
        {
            float az = j / (float)sides * Mathf.PI * 2f;
            float bump = 1f + 0.12f * Mathf.Sin(az * 3f + bumpSeed) * Mathf.Sin(Mathf.PI * 2f + bumpSeed);
            Vector3 p = centre + rotation * (new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az)) * size * bump);
            rim[j] = mesh.Vertex(p, up, new Vector2(1f, piece), new Vector2(1f, size * bump));
        }
        for (int j = 0; j < sides; j++) mesh.Triangle(middle, rim[j + 1], rim[j]);
    }

    /// <summary>Stepped basalt: hexagonal columns packed in a honeycomb, tallest in the middle, like stairs.</summary>
    private void BasaltCluster(RealmMeshes mesh, Vector3 at, System.Random random)
    {
        float cell = Range(random, 0.55f, 0.8f);
        float peak = Range(random, 2.5f, 5.5f);
        int reach = random.Next(2, 4);
        for (int q = -reach; q <= reach; q++)
        for (int r = -reach; r <= reach; r++)
        {
            int s = -q - r;
            int band = Mathf.Max(Mathf.Abs(q), Mathf.Abs(r), Mathf.Abs(s));
            if (band > reach || random.NextDouble() < 0.15) continue;
            // Honeycomb position (pointy-top hexes).
            Vector3 offset = new Vector3(cell * Mathf.Sqrt(3f) * (q + r * 0.5f), 0f, cell * 1.5f * r);
            Vector3 p = at + offset;
            if (!RealmSites.GroundAt(world, p, out Vector3 foot, out _)) continue;
            float height = Mathf.Max(0.4f, peak * (1f - band / (reach + 1f)) + Range(random, -0.4f, 0.6f));
            Column(mesh, foot - Vector3.up * 1.2f, cell * 0.98f, height + 1.2f, (float)random.NextDouble());
        }
    }

    private static void Column(RealmMeshes mesh, Vector3 foot, float radius, float height, float piece)
    {
        var corners = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            float a = (i + 0.5f) / 6f * Mathf.PI * 2f;
            corners[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
        }
        Vector3 top = foot + Vector3.up * height;
        for (int i = 0; i < 6; i++)
        {
            int j = (i + 1) % 6;
            Vector3 n = ((corners[i] + corners[j]) * 0.5f).normalized;
            float u0 = i / 6f, u1 = (i + 1) / 6f;
            int a = mesh.Vertex(foot + corners[i], n, new Vector2(0f, piece), new Vector2(u0, 0f));
            int b = mesh.Vertex(foot + corners[j], n, new Vector2(0f, piece), new Vector2(u1, 0f));
            int c = mesh.Vertex(top + corners[i], n, new Vector2(1f, piece), new Vector2(u0, height));
            int d = mesh.Vertex(top + corners[j], n, new Vector2(1f, piece), new Vector2(u1, height));
            mesh.Triangle(a, c, b);
            mesh.Triangle(b, c, d);
        }
        int middle = mesh.Vertex(top, Vector3.up, new Vector2(1f, piece), new Vector2(0f, height));
        var rim = new int[6];
        for (int i = 0; i < 6; i++) rim[i] = mesh.Vertex(top + corners[i], Vector3.up, new Vector2(1f, piece), new Vector2(i / 6f, height));
        for (int i = 0; i < 6; i++) mesh.Triangle(middle, rim[(i + 1) % 6], rim[i]);
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }
}
