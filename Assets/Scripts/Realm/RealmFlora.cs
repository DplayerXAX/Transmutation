using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Colourful life all round the Hanging Spire, grouped into a few composed groves with open ground between
/// (so it never turns into clutter), giving the plain terrain colour and structure: fossil beds (rib cages with spines, coiled vertebrae, ribbed ammonites),
/// icy feather-crystal plants, glowing mushroom clusters, pink coral foam, translucent jelly arches,
/// sleeping fluorescent slugs and dark anemones with glowing tips.
/// Builds once the ground round the spire has loaded.
/// </summary>
[DisallowMultipleComponent]
public sealed class RealmFlora : MonoBehaviour
{
    [Header("Materials")]
    [SerializeField] private Material fossilMaterial;
    [SerializeField] private Material frostMaterial;
    [SerializeField] private Material mushroomMaterial;
    [SerializeField] private Material mushroomBlueMaterial;
    [SerializeField] private Material coralMaterial;
    [SerializeField] private Material jellyMaterial;
    [SerializeField] private Material slugMaterial;
    [SerializeField] private Material anemoneMaterial;

    [Header("Ring Round The Spire")]
    [SerializeField] private int seed = 17;
    [Tooltip("Distance from the spire's sinkhole rim (metres).")]
    [SerializeField] private Vector2 ring = new Vector2(3f, 48f);
    [Tooltip("Themed groves round the tower (bone garden, glass pool, slug rest, in turn).")]
    [Range(0, 12)] [SerializeField] private int groves = 6;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool built;
    [SerializeField] private int placed;

    private ProceduralWorld world;
    private Transform root;
    private Vector3 foot;
    private float rim, waited;

    public void Setup(Material fossil, Material frost, Material mushroom, Material mushroomBlue, Material coral, Material jelly, Material slug, Material anemone)
    {
        fossilMaterial = fossil;
        frostMaterial = frost;
        mushroomMaterial = mushroom;
        mushroomBlueMaterial = mushroomBlue;
        coralMaterial = coral;
        jellyMaterial = jelly;
        slugMaterial = slug;
        anemoneMaterial = anemone;
    }

    private void Update()
    {
        if (built) return;
        if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
        waited += Time.deltaTime;
        if (!RealmSites.ReadyToPlace(world, waited)) return;
        foot = RealmSites.AreaCentre(world);
        foot.y = RealmSites.Surface(world, foot);
        if (waited < 35f && !world.IsRegionReady(foot, ring.y * 0.6f)) return;
        built = true;
        rim = RealmSites.HoleRadiusAt(world, foot);
        Build();
    }

    private void Build()
    {
        var random = new System.Random(seed);
        root = new GameObject("Realm Flora").transform;
        var fossils = new RealmMeshes();
        var frost = new RealmMeshes();
        var mushrooms = new RealmMeshes();
        var blueMushrooms = new RealmMeshes();
        var coral = new RealmMeshes();
        var jelly = new RealmMeshes();
        var slugMesh = new RealmMeshes();
        var anemones = new RealmMeshes();

        // A few themed groves spread round the tower, each a small composed scene, with open ground between.
        float turn = (float)random.NextDouble() * Mathf.PI * 2f;
        for (int g = 0; g < groves; g++)
        {
            float a = turn + (g + Mathf.Lerp(-0.25f, 0.25f, (float)random.NextDouble())) / groves * Mathf.PI * 2f;
            float d = rim + Mathf.Lerp(ring.x + 5f, ring.y, (float)random.NextDouble());
            Vector3 centre = foot + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d;
            if (!Near(centre, 0f, random, 3f, out Vector3 heart, out Vector3 heartNormal)) continue;
            switch (g % 3)
            {
                case 0: // Bone garden: a fossil with mushrooms growing out of it and a little frost.
                    FossilBed(fossils, heart, heartNormal, g / 3, random);
                    MushroomCluster(mushrooms, heart, 3.5f, random);
                    if (Near(heart, 4f, random, 1f, out Vector3 f1, out Vector3 n1)) FrostPatch(frost, f1, n1, random);
                    break;
                case 1: // Glass pool: a jelly arch over a heap of coral, anemones at its feet.
                    JellyArch(jelly, heart, random);
                    CoralFoam(coral, heart, heartNormal, random);
                    if (Near(heart, 3f, random, 1f, out Vector3 a1, out Vector3 an1)) Anemone(anemones, a1, an1, random);
                    break;
                default: // Slug rest: a sleeping slug among blue mushrooms and frost.
                    Slug(slugMesh, heart, heartNormal, Mathf.Lerp(1.6f, 3f, (float)random.NextDouble()), random);
                    MushroomCluster(blueMushrooms, heart, 3f, random);
                    if (Near(heart, 3.5f, random, 1f, out Vector3 f2, out Vector3 n2)) FrostPatch(frost, f2, n2, random);
                    break;
            }
        }

        Part("Fossil Beds", fossils, fossilMaterial, true, true);
        Part("Frost Plants", frost, frostMaterial, false, false);
        Part("Mushrooms", mushrooms, mushroomMaterial, true, false);
        Part("Blue Mushrooms", blueMushrooms, mushroomBlueMaterial, true, false);
        Part("Coral Foam", coral, coralMaterial, true, false);
        Part("Jelly Arches", jelly, jellyMaterial, false, false);
        Part("Slugs", slugMesh, slugMaterial, true, false);
        Part("Anemones", anemones, anemoneMaterial, true, false);
    }

    private void MushroomCluster(RealmMeshes mesh, Vector3 near, float radius, System.Random random)
    {
        if (!Near(near, radius, random, 1f, out Vector3 at, out Vector3 n)) return;
        int count = random.Next(4, 9);
        float scale = Mathf.Lerp(0.7f, 1.6f, (float)random.NextDouble());
        for (int k = 0; k < count; k++) Mushroom(mesh, Ground(at + Scatter(random, 0.9f * scale)), n, scale * Mathf.Lerp(0.5f, 1.2f, (float)random.NextDouble()), random);
    }

    private void FrostPatch(RealmMeshes mesh, Vector3 at, Vector3 n, System.Random random)
    {
        int count = random.Next(3, 7);
        for (int k = 0; k < count; k++) FeatherCrystal(mesh, at + Scatter(random, 1f), n, Mathf.Lerp(0.5f, 1.3f, (float)random.NextDouble()), random);
    }

    /// <summary>A spot on the real ground within radius of a point, clear of sinkholes and not too steep.</summary>
    private bool Near(Vector3 point, float radius, System.Random random, float margin, out Vector3 at, out Vector3 normal)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector3 p = point + Scatter(random, radius + attempt * 0.5f);
            if (RealmSites.InSinkhole(world, p, margin)) continue;
            if (!RealmSites.GroundAt(world, p, out at, out normal) || normal.y < 0.55f) continue;
            placed++;
            return true;
        }
        at = default;
        normal = Vector3.up;
        return false;
    }

    // ---------------- Placement ----------------

    /// <summary>A random spot on the real ground in the ring round the spire, clear of sinkholes.</summary>
    private bool Spot(System.Random random, float margin, out Vector3 at, out Vector3 normal)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            // More things close to the tower, fewer further out.
            float d = rim + Mathf.Lerp(ring.x, ring.y, Mathf.Pow((float)random.NextDouble(), 1.4f));
            Vector3 p = foot + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d;
            if (RealmSites.InSinkhole(world, p, margin)) continue;
            if (!RealmSites.GroundAt(world, p, out at, out normal)) continue;
            if (normal.y < 0.55f) continue;
            placed++;
            return true;
        }
        at = default;
        normal = Vector3.up;
        return false;
    }

    private Vector3 Ground(Vector3 p)
    {
        RealmSites.GroundAt(world, p, out Vector3 hit, out _);
        return hit;
    }

    private static Vector3 Scatter(System.Random random, float radius)
    {
        float a = (float)random.NextDouble() * Mathf.PI * 2f;
        return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Mathf.Sqrt((float)random.NextDouble()) * radius;
    }

    private void Part(string name, RealmMeshes mesh, Material material, bool shadows, bool collider)
    {
        if (mesh.VertexCount == 0 || material == null) return;
        GameObject part = RealmSites.Part(name, root, mesh.Build(name), material, collider ? 7 : 0, collider);
        part.transform.position = Vector3.zero;
        if (!shadows) part.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // ---------------- Forms ----------------

    /// <summary>A fossil bed: a rib cage with its spine, coiled vertebrae, or a ribbed ammonite, half sunk.</summary>
    private void FossilBed(RealmMeshes mesh, Vector3 at, Vector3 normal, int kind, System.Random random)
    {
        float piece = (float)random.NextDouble();
        Quaternion lay = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
        switch (kind % 3)
        {
            case 0:
            {
                // Rib cage: a spine of vertebrae with paired ribs curving up out of the ground.
                float length = Mathf.Lerp(4f, 7f, (float)random.NextDouble());
                int vertebrae = Mathf.RoundToInt(length / 0.45f);
                for (int v = 0; v < vertebrae; v++)
                {
                    float u = v / (float)(vertebrae - 1);
                    Vector3 c = at + lay * new Vector3(0f, -0.15f + Mathf.Sin(u * Mathf.PI) * 0.35f, (u - 0.5f) * length);
                    float r = Mathf.Lerp(0.22f, 0.14f, u);
                    mesh.Sphere(c, new Vector3(r * 1.3f, r, r * 0.8f), lay, 6, 8, u, piece);
                    // A spine pointing up from each vertebra.
                    mesh.Tube(new List<Vector3> { c, c + lay * new Vector3(0f, r * 2.2f, -r) }, w => r * 0.35f * (1f - w * 0.7f), 5, u, u, piece, r * 0.3f);
                    if (v % 2 == 1 || v < 2 || v > vertebrae - 3) continue;
                    float span = Mathf.Lerp(1.6f, 0.8f, Mathf.Abs(u - 0.4f) * 1.8f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var rib = new List<Vector3>();
                        for (int k = 0; k <= 12; k++)
                        {
                            float t = k / 12f;
                            float angle = t * Mathf.PI * 0.85f;
                            rib.Add(c + lay * new Vector3(side * Mathf.Sin(angle) * span, Mathf.Sin(angle) * span * 0.9f - t * t * span * 0.7f, -t * 0.3f));
                        }
                        mesh.Tube(rib, t => 0.09f * (1f - t * 0.55f), 6, 0f, 1f, piece, 0.05f);
                    }
                }
                break;
            }
            case 1:
            {
                // Coiled vertebrae: a chain of beads winding inwards, each with a little spine.
                float size = Mathf.Lerp(1.4f, 2.4f, (float)random.NextDouble());
                Quaternion stand = lay * Quaternion.Euler(Mathf.Lerp(50f, 80f, (float)random.NextDouble()), 0f, 0f);
                const int beads = 34;
                for (int k = 0; k < beads; k++)
                {
                    float u = k / (float)beads;
                    float a = u * Mathf.PI * 2f * 2.2f;
                    float r = size * Mathf.Exp(-u * 1.6f);
                    Vector3 c = at + stand * new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r) + normal * size * 0.3f;
                    float b = size * 0.13f * Mathf.Exp(-u * 1.4f);
                    mesh.Sphere(c, new Vector3(b, b * 1.2f, b), stand * Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), 6, 8, u, piece);
                    Vector3 outward = stand * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    mesh.Tube(new List<Vector3> { c, c + outward * b * 2.2f }, w => b * 0.35f * (1f - w * 0.8f), 4, u, u, piece, b * 0.3f);
                }
                break;
            }
            default:
            {
                // Ammonite: a ribbed shell winding inwards; the ribs are real ridges, not just lines.
                float size = Mathf.Lerp(1.6f, 2.8f, (float)random.NextDouble());
                Quaternion stand = lay * Quaternion.Euler(Mathf.Lerp(55f, 85f, (float)random.NextDouble()), 0f, 0f);
                var path = new List<Vector3>();
                const int steps = 120;
                for (int k = 0; k <= steps; k++)
                {
                    float u = k / (float)steps;
                    float a = u * 2.7f * Mathf.PI * 2f;
                    float r = size * Mathf.Exp(-u * 1.9f);
                    path.Add(at + stand * new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r) + normal * size * 0.15f);
                }
                mesh.Tube(path, u => size * 0.34f * Mathf.Exp(-u * 1.9f) * (1f + 0.12f * Mathf.Pow(Mathf.Abs(Mathf.Sin(u * 70f)), 6f)), 12, 0f, 1f, piece, 0f);
                break;
            }
        }
    }

    /// <summary>An icy feather plant: a curving stem with flat crystal leaflets on both sides.</summary>
    private static void FeatherCrystal(RealmMeshes mesh, Vector3 at, Vector3 normal, float height, System.Random random)
    {
        Vector3 lean = (normal + new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * 0.6f).normalized;
        Vector3 bend = Vector3.Cross(lean, Vector3.up).normalized;
        if (bend.sqrMagnitude < 0.01f) bend = Vector3.right;
        var stem = new List<Vector3>();
        for (int k = 0; k <= 8; k++)
        {
            float u = k / 8f;
            stem.Add(at + lean * u * height + bend * u * u * height * 0.25f);
        }
        float piece = (float)random.NextDouble();
        mesh.Tube(stem, u => 0.025f * (1f - u * 0.6f), 4, 0f, 1f, piece, 0.03f);
        int pairs = random.Next(4, 8);
        for (int p = 1; p <= pairs; p++)
        {
            float u = p / (pairs + 1f);
            Vector3 c = stem[Mathf.RoundToInt(u * 8)];
            float size = height * 0.28f * (1f - u * 0.6f);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 dir = (bend * side + lean * 0.6f).normalized;
                Quaternion rotation = Quaternion.LookRotation(Vector3.Cross(dir, lean), dir) ;
                // A thin, flat crystal: long along dir, narrow, nearly flat.
                mesh.Crystal(c + dir * size * 0.5f, rotation, size, size * 0.12f, 4, piece, random);
            }
        }
    }

    /// <summary>A mushroom: a slightly bent stem and a cap whose underside (gills) glows.</summary>
    public static void Mushroom(RealmMeshes mesh, Vector3 at, Vector3 normal, float scale, System.Random random)
    {
        float height = Mathf.Lerp(0.25f, 0.6f, (float)random.NextDouble()) * scale;
        Vector3 lean = (normal + new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f) * 0.5f).normalized;
        var stem = new List<Vector3>();
        for (int k = 0; k <= 5; k++)
        {
            float u = k / 5f;
            stem.Add(at - normal * 0.05f + lean * u * height + new Vector3(Mathf.Sin(u * 3f), 0f, Mathf.Cos(u * 2f)) * 0.03f * scale);
        }
        float piece = (float)random.NextDouble();
        float stemRadius = 0.04f * scale;
        mesh.Tube(stem, u => stemRadius * (1.3f - u * 0.4f), 6, 0f, 0.7f, piece);
        float cap = Mathf.Lerp(0.12f, 0.22f, (float)random.NextDouble()) * scale;
        Quaternion tilt = Quaternion.FromToRotation(Vector3.up, lean);
        mesh.Sphere(stem[stem.Count - 1] + lean * cap * 0.15f, new Vector3(cap, cap * Mathf.Lerp(0.35f, 0.6f, (float)random.NextDouble()), cap), tilt, 7, 12, 1f, piece);
    }

    /// <summary>Pink coral foam: a heap of bubbles of different sizes piled on each other.</summary>
    private void CoralFoam(RealmMeshes mesh, Vector3 at, Vector3 normal, System.Random random)
    {
        int count = random.Next(14, 40);
        float piece = (float)random.NextDouble();
        float spread = Mathf.Lerp(0.6f, 1.4f, (float)random.NextDouble());
        for (int i = 0; i < count; i++)
        {
            float r = Mathf.Lerp(0.06f, 0.24f, Mathf.Pow((float)random.NextDouble(), 1.5f));
            Vector3 offset = Scatter(random, spread);
            // Heaped: higher in the middle.
            float pile = (1f - offset.magnitude / spread) * spread * 0.8f * (float)random.NextDouble();
            mesh.Sphere(at + offset + normal * (pile + r * 0.6f), Vector3.one * r, Quaternion.identity, 6, 8, Mathf.Clamp01(pile / spread + 0.3f), piece);
        }
    }

    /// <summary>A translucent jelly arch: a segmented tube looping out of the ground and back.</summary>
    private void JellyArch(RealmMeshes mesh, Vector3 at, System.Random random)
    {
        float span = Mathf.Lerp(4f, 7f, (float)random.NextDouble());
        float height = Mathf.Lerp(2.5f, 4.5f, (float)random.NextDouble());
        float yaw = (float)random.NextDouble() * Mathf.PI * 2f;
        Vector3 across = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
        Vector3 a = Ground(at - across * span * 0.5f), b = Ground(at + across * span * 0.5f);
        float thick = Mathf.Lerp(0.22f, 0.38f, (float)random.NextDouble());
        float piece = (float)random.NextDouble();
        const int segments = 9;
        for (int s = 0; s < segments; s++)
        {
            var seg = new List<Vector3>();
            for (int k = 0; k <= 4; k++)
            {
                float u = (s + k / 4f * 0.86f) / segments;
                seg.Add(Vector3.Lerp(a, b, u) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * height - 0.3f) + Vector3.Cross(across, Vector3.up) * Mathf.Sin(u * Mathf.PI * 2f) * 0.6f);
            }
            // Each segment swells in the middle, so the joints pinch like a jelly chain.
            mesh.Tube(seg, u => thick * (0.75f + 0.35f * Mathf.Sin(u * Mathf.PI)), 10, s / (float)segments, (s + 1f) / segments, piece, thick * 0.3f);
        }
    }

    /// <summary>A resting fluorescent slug: a long soft body, a head and two short feelers.</summary>
    private void Slug(RealmMeshes mesh, Vector3 at, Vector3 normal, float length, System.Random random)
    {
        float yaw = (float)random.NextDouble() * Mathf.PI * 2f;
        Vector3 forward = Vector3.ProjectOnPlane(new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)), normal).normalized;
        Vector3 side = Vector3.Cross(normal, forward);
        float bend = Mathf.Lerp(-0.5f, 0.5f, (float)random.NextDouble());
        float thick = length * 0.13f;
        var body = new List<Vector3>();
        for (int k = 0; k <= 14; k++)
        {
            float u = k / 14f;
            float s = (u - 0.5f) * length;
            // Lies on the ground; the front lifts a little.
            body.Add(at + forward * s + side * bend * s * s / length + normal * (thick * 0.55f + Mathf.SmoothStep(0.7f, 1f, u) * thick * 0.6f));
        }
        float piece = (float)random.NextDouble();
        mesh.Tube(body, u => thick * Mathf.Max(0.15f, Mathf.Pow(Mathf.Sin(Mathf.Lerp(0.05f, 0.92f, u) * Mathf.PI), 0.6f)), 12, 0f, 1f, piece, thick * 0.5f);
        Vector3 head = body[body.Count - 1];
        Vector3 dirHead = (body[body.Count - 1] - body[body.Count - 2]).normalized;
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 start = head + side * s * thick * 0.35f + normal * thick * 0.3f;
            mesh.Tube(new List<Vector3> { start, start + (dirHead + normal + side * s * 0.4f).normalized * thick * 1.4f },
                u => thick * 0.12f, 5, 0.9f, 1f, piece, thick * 0.12f);
        }
    }

    /// <summary>Dark anemone fingers, glowing at the tips.</summary>
    private void Anemone(RealmMeshes mesh, Vector3 at, Vector3 normal, System.Random random)
    {
        int fingers = random.Next(8, 15);
        for (int i = 0; i < fingers; i++)
        {
            Vector3 dir = Scatter(random, 1f).normalized;
            float length = Mathf.Lerp(0.6f, 1.5f, (float)random.NextDouble());
            Vector3 root = at + dir * (float)random.NextDouble() * 0.35f - normal * 0.1f;
            var path = new List<Vector3>();
            for (int k = 0; k <= 8; k++)
            {
                float u = k / 8f;
                path.Add(root + normal * u * length + dir * u * u * length * 0.5f);
            }
            mesh.Tube(path, u => Mathf.Lerp(0.08f, 0.035f, u), 6, 0f, 1f, (float)random.NextDouble(), 0.05f);
        }
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }
}
