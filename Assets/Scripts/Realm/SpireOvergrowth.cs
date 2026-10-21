using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Living overgrowth on and round the Hanging Spire:
///   - veins that crawl over the tower's walls and branch, following its real surface (found by raycasts),
///     with crystal clusters bursting out where they start and at the crown,
///   - mushrooms on the tower's ledges,
///   - a three-dimensional web round the tower: strands between hubs hanging in the air, anchor points on the
///     walls, the lamps and the ground; strands split into fanned "feet" where they grip; some cells of the web
///     are filled with iridescent film; dew beads sit along the strands and glowing knots at the hubs.
/// Builds once the spire and the realm dressing (for the lamps) exist. Spire code is not touched.
/// </summary>
[DisallowMultipleComponent]
public sealed class SpireOvergrowth : MonoBehaviour
{
    [Header("Materials")]
    [SerializeField] private Material veinMaterial;
    [SerializeField] private Material crystalMaterial;
    [SerializeField] private Material strandMaterial;
    [SerializeField] private Material membraneMaterial;
    [SerializeField] private Material beadMaterial;
    [SerializeField] private Material mushroomMaterial;

    [Header("Amounts")]
    [SerializeField] private int seed = 13;
    [Range(0, 60)] [SerializeField] private int veinRoots = 8;
    [Range(0, 40)] [SerializeField] private int webHubs = 6;
    [Range(0f, 1f)] [SerializeField] private float membraneShare = 0.12f;
    [Tooltip("Longest strand in the web (metres).")]
    [Min(2f)] [SerializeField] private float maxStrand = 15f;

    [Header("Runtime State (Read Only)")]
    [SerializeField] private bool built;
    [SerializeField] private int strands;
    [SerializeField] private int membranes;

    private ProceduralWorld world;
    private Transform spireRoot, root;
    private Bounds spireBounds;
    private float waited;

    private struct Node
    {
        public Vector3 position, normal;
        public int kind; // 0 hub in the air, 1 on the tower, 2 lamp, 3 ground
    }

    public void Setup(Material vein, Material crystal, Material strand, Material membrane, Material bead, Material mushroom)
    {
        veinMaterial = vein;
        crystalMaterial = crystal;
        strandMaterial = strand;
        membraneMaterial = membrane;
        beadMaterial = bead;
        mushroomMaterial = mushroom;
    }

    private void Update()
    {
        if (built) return;
        if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
        waited += Time.deltaTime;
        if (!RealmSites.ReadyToPlace(world, waited)) return;
        if (!RealmSites.SpireBody(out spireRoot, out spireBounds)) { if (waited > 30f) built = true; return; }
        Vector3 foot = RealmSites.AreaCentre(world);
        // Wait for the ground round the spire and for the lamps.
        RealmDressing dressing = FindFirstObjectByType<RealmDressing>();
        if (waited < 35f && (!world.IsRegionReady(foot, 20f) || (dressing != null && !dressing.Built))) return;
        built = true;
        Build(dressing);
    }

    private void Build(RealmDressing dressing)
    {
        var random = new System.Random(seed);
        root = new GameObject("Spire Overgrowth").transform;

        var anchors = SurfaceAnchors(random, 70);
        anchors.RemoveAll(anchor => anchor.position.y < spireBounds.min.y + 15f && !WebSide(anchor.position));
        Veins(anchors, random);
        Mushrooms(random);
        Web(anchors, dressing, random);
    }

    // ---------------- Surface of the tower ----------------

    private bool SpireHit(Ray ray, float distance, out RaycastHit hit)
    {
        hit = default;
        RaycastHit[] hits = Physics.RaycastAll(ray, distance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        foreach (RaycastHit h in hits)
        {
            if (!h.collider.transform.IsChildOf(spireRoot) || h.distance >= best) continue;
            best = h.distance;
            hit = h;
            found = true;
        }
        return found;
    }

    /// <summary>Points on the tower's walls, found by looking at it from all round.</summary>
    private List<Node> SurfaceAnchors(System.Random random, int tries)
    {
        var list = new List<Node>();
        Vector3 centre = spireBounds.center;
        float radius = Mathf.Max(spireBounds.extents.x, spireBounds.extents.z);
        for (int i = 0; i < tries; i++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float height = Mathf.Lerp(spireBounds.min.y + 2f, spireBounds.max.y - 1f, (float)random.NextDouble());
            Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 from = new Vector3(centre.x, height, centre.z) + dir * (radius + 8f);
            Vector3 aim = new Vector3(centre.x, height + ((float)random.NextDouble() - 0.5f) * 3f, centre.z) - from;
            if (SpireHit(new Ray(from, aim.normalized), aim.magnitude + 4f, out RaycastHit hit))
                list.Add(new Node { position = hit.point, normal = hit.normal, kind = 1 });
        }
        return list;
    }

    /// <summary>Veins crawling over the walls: each step is pressed back onto the surface by a raycast.</summary>
    private void Veins(List<Node> anchors, System.Random random)
    {
        var veins = new RealmMeshes();
        var crystals = new RealmMeshes();
        var walkers = new Queue<(Vector3 position, Vector3 normal, Vector3 direction, float thickness, int steps)>();
        for (int i = 0; i < Mathf.Min(veinRoots, anchors.Count); i++)
        {
            Node a = anchors[i];
            Vector3 dir = Vector3.ProjectOnPlane(Vector3.up + Random3(random) * 0.8f, a.normal).normalized;
            walkers.Enqueue((a.position, a.normal, dir, Mathf.Lerp(0.05f, 0.09f, (float)random.NextDouble()), random.Next(25, 70)));
            if (random.NextDouble() < 0.6) Cluster(crystals, a.position, a.normal, random.Next(3, 7), 0.5f, random);
        }

        int total = 0;
        while (walkers.Count > 0 && total < 160)
        {
            var w = walkers.Dequeue();
            total++;
            var path = new List<Vector3> { w.position + w.normal * 0.03f };
            Vector3 p = w.position, n = w.normal, d = w.direction;
            for (int s = 0; s < w.steps; s++)
            {
                d = Vector3.ProjectOnPlane(d + Random3(random) * 0.45f + Vector3.up * 0.05f, n).normalized;
                Vector3 next = p + d * 0.35f;
                if (!SpireHit(new Ray(next + n * 0.6f, -n), 1.4f, out RaycastHit hit)) break;
                p = hit.point;
                n = hit.normal;
                path.Add(p + n * 0.03f);
                // Branch now and then: a thinner vein heading off sideways.
                if (w.thickness > 0.025f && random.NextDouble() < 0.07)
                    walkers.Enqueue((p, n, Vector3.Cross(n, d) * (random.NextDouble() < 0.5 ? 1f : -1f), w.thickness * 0.6f, random.Next(10, 35)));
            }
            if (path.Count >= 3)
            {
                float thick = w.thickness;
                veins.Tube(path, u => thick * Mathf.Lerp(1f, 0.35f, u), 6, 0f, 1f, (float)random.NextDouble(), thick * 0.5f);
                if (random.NextDouble() < 0.25) Cluster(crystals, p, n, random.Next(2, 5), 0.3f, random);
            }
        }
        // A burst of crystals at the crown.
        HangingSpire spire = FindFirstObjectByType<HangingSpire>();
        if (spire != null && spire.CrownSeat(out Vector3 crown, out _))
            for (int i = 0; i < 4; i++) Cluster(crystals, crown + Random3(random) * 2f, Vector3.up, random.Next(5, 10), 1.1f, random);

        Part("Spire Veins", veins, veinMaterial, false);
        Part("Spire Crystals", crystals, crystalMaterial, true);
    }

    /// <summary>A cluster of shards bursting out of a surface.</summary>
    private void Cluster(RealmMeshes mesh, Vector3 at, Vector3 normal, int count, float size, System.Random random)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 dir = (normal + Random3(random) * 0.9f).normalized;
            float length = size * Mathf.Lerp(0.6f, 2.2f, (float)random.NextDouble());
            mesh.Crystal(at + dir * length * 0.4f, Quaternion.FromToRotation(Vector3.up, dir), length, length * 0.2f, random.Next(5, 7),
                (float)random.NextDouble(), random);
        }
    }

    /// <summary>Clusters of mushrooms on the tower's ledges.</summary>
    private void Mushrooms(System.Random random)
    {
        HangingSpire spire = FindFirstObjectByType<HangingSpire>();
        if (spire == null) return;
        var mesh = new RealmMeshes();
        foreach (var landing in spire.Landings)
        {
            if (random.NextDouble() < 0.65) continue;
            int count = random.Next(2, 5);
            for (int i = 0; i < count; i++)
            {
                Vector3 at = landing.top + Random3(random).normalized * Mathf.Lerp(0.2f, 1.2f, (float)random.NextDouble());
                at.y = landing.top.y;
                RealmFlora.Mushroom(mesh, at, Vector3.up, Mathf.Lerp(0.5f, 1.3f, (float)random.NextDouble()), random);
            }
        }
        Part("Spire Mushrooms", mesh, mushroomMaterial, true);
    }

    // ---------------- The web ----------------

    private void Web(List<Node> surface, RealmDressing dressing, System.Random random)
    {
        var nodes = new List<Node>();
        // Some points on the walls.
        for (int i = 0; i < surface.Count; i += 4) if (WebSide(surface[i].position)) nodes.Add(surface[i]);
        // Lamps near the tower.
        Vector3 axis = spireBounds.center;
        var lamps = new List<Vector3>();
        if (dressing != null)
            foreach (Vector3 lamp in dressing.LampTops)
            {
                if (!WebSide(lamp) || RealmDecorLayout.FlatDistance(lamp, axis) > 22f) continue;
                lamps.Add(lamp);
                nodes.Add(new Node { position = lamp + Vector3.down * 0.3f, normal = Vector3.down, kind = 2 });
            }
        // Ground round the tower's foot.
        float radius = Mathf.Max(spireBounds.extents.x, spireBounds.extents.z);
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.PI * 2f + (float)random.NextDouble() * 0.3f;
            Vector3 p = axis + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (radius + Mathf.Lerp(4f, 14f, (float)random.NextDouble()));
            if (!WebSide(p)) continue;
            if (RealmSites.InSinkhole(world, p, 1f)) continue;
            if (RealmSites.GroundAt(world, p, out Vector3 g, out Vector3 gn)) nodes.Add(new Node { position = g, normal = gn, kind = 3 });
        }
        // Near every lamp, a patch of ground for the web to root in.
        foreach (Vector3 lamp in lamps)
        {
            Vector3 p = lamp + Random3(random).normalized * Mathf.Lerp(2f, 4f, (float)random.NextDouble());
            if (!RealmSites.InSinkhole(world, p, 1f) && RealmSites.GroundAt(world, p, out Vector3 g, out Vector3 gn))
                nodes.Add(new Node { position = g, normal = gn, kind = 3 });
        }
        // Hubs hanging in the air round the tower, and one beside every lamp, so the web runs along the path too.
        int firstHub = nodes.Count;
        foreach (Vector3 lamp in lamps)
        {
            Vector3 p = lamp + Random3(random).normalized * Mathf.Lerp(3f, 5f, (float)random.NextDouble());
            p.y = Mathf.Max(p.y, lamp.y - 1.5f) + Mathf.Lerp(0.5f, 2.5f, (float)random.NextDouble());
            nodes.Add(new Node { position = p, normal = Vector3.up, kind = 0 });
        }
        for (int i = 0; i < webHubs; i++)
        {
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            float h = Mathf.Lerp(spireBounds.min.y + 3f, spireBounds.max.y * 0.95f + spireBounds.min.y * 0.05f, Mathf.Pow((float)random.NextDouble(), 0.8f));
            Vector3 p = new Vector3(axis.x, h, axis.z) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (radius + Mathf.Lerp(2f, 12f, (float)random.NextDouble()));
            if (!WebSide(p)) continue;
            nodes.Add(new Node { position = p, normal = Vector3.up, kind = 0 });
        }

        // Each hub links to its nearest few nodes; every other node to its nearest hubs.
        var edges = new HashSet<(int, int)>();
        void Link(int i, int j)
        {
            if (i == j) return;
            if (Vector3.Distance(nodes[i].position, nodes[j].position) > maxStrand) return;
            edges.Add((Mathf.Min(i, j), Mathf.Max(i, j)));
        }
        for (int i = 0; i < nodes.Count; i++)
        {
            var order = new List<int>();
            for (int j = 0; j < nodes.Count; j++) if (j != i && (i >= firstHub || j >= firstHub)) order.Add(j);
            Vector3 from = nodes[i].position;
            order.Sort((x, y) => (nodes[x].position - from).sqrMagnitude.CompareTo((nodes[y].position - from).sqrMagnitude));
            int links = i >= firstHub ? random.Next(2, 4) : 1;
            for (int k = 0; k < Mathf.Min(links, order.Count); k++) Link(i, order[k]);
        }

        var strandMesh = new RealmMeshes();
        var beadMesh = new RealmMeshes();
        var adjacency = new Dictionary<int, HashSet<int>>();
        foreach (var (i, j) in edges)
        {
            Strand(strandMesh, beadMesh, nodes[i], nodes[j], random);
            if (!adjacency.ContainsKey(i)) adjacency[i] = new HashSet<int>();
            if (!adjacency.ContainsKey(j)) adjacency[j] = new HashSet<int>();
            adjacency[i].Add(j);
            adjacency[j].Add(i);
            strands++;
        }
        // Glowing knots at the hubs.
        for (int i = firstHub; i < nodes.Count; i++)
        {
            if (!adjacency.ContainsKey(i)) continue;
            float size = Mathf.Lerp(0.1f, 0.2f, (float)random.NextDouble());
            beadMesh.Sphere(nodes[i].position, new Vector3(size, size * 1.2f, size), Quaternion.identity, 8, 10, 1f, (float)random.NextDouble());
        }

        // Film across some of the web's triangles.
        var membraneMesh = new RealmMeshes();
        foreach (var (i, j) in edges)
            foreach (int k in adjacency[i])
            {
                if (k <= j || !adjacency[j].Contains(k)) continue;
                if (random.NextDouble() > membraneShare) continue;
                Film(membraneMesh, nodes[i].position, nodes[j].position, nodes[k].position, random);
                membranes++;
            }

        Part("Web Strands", strandMesh, strandMaterial, false);
        Part("Web Dew", beadMesh, beadMaterial, false);
        Part("Web Film", membraneMesh, membraneMaterial, false);
    }

    private bool WebSide(Vector3 at)
    {
        Vector3 arrival = Vector3.ProjectOnPlane(world.transform.position - spireBounds.center, Vector3.up).normalized;
        if (arrival.sqrMagnitude < 0.1f) arrival = Vector3.back;
        Vector3 radial = Vector3.ProjectOnPlane(at - spireBounds.center, Vector3.up).normalized;
        return Vector3.Dot(radial, arrival) < -0.05f;
    }

    /// <summary>One strand: a sagging, slightly wavy thread that splits into a fan of feet where it grips a surface.</summary>
    private void Strand(RealmMeshes mesh, RealmMeshes beads, Node a, Node b, System.Random random)
    {
        float span = Vector3.Distance(a.position, b.position);
        float sag = Mathf.Lerp(0.03f, 0.1f, (float)random.NextDouble());
        float thick = Mathf.Lerp(0.014f, 0.035f, (float)random.NextDouble());
        Vector3 wobbleAxis = Random3(random).normalized;
        Vector3 Point(float u) => Vector3.Lerp(a.position, b.position, u) + Vector3.down * Mathf.Sin(u * Mathf.PI) * span * sag
            + wobbleAxis * Mathf.Sin(u * Mathf.PI * 3f) * 0.12f * Mathf.Sin(u * Mathf.PI);

        // Gripping ends (on the tower, a lamp, the ground) fan out into a few feet.
        float startU = a.kind == 0 ? 0f : 0.08f;
        float endU = b.kind == 0 ? 1f : 0.92f;
        var path = new List<Vector3>();
        for (int k = 0; k <= 20; k++) path.Add(Point(Mathf.Lerp(startU, endU, k / 20f)));
        mesh.Tube(path, u => thick * (1f + 0.6f * Mathf.Pow(Mathf.Abs(u - 0.5f) * 2f, 3f)), 5, 0f, 1f, (float)random.NextDouble());
        if (a.kind != 0) Feet(mesh, Point(startU), a, thick, random);
        if (b.kind != 0) Feet(mesh, Point(endU), b, thick, random);

        // Dew along the strand.
        for (float s = Mathf.Lerp(0.5f, 2f, (float)random.NextDouble()); s < span; s += Mathf.Lerp(1.2f, 3f, (float)random.NextDouble()))
        {
            float r = Mathf.Lerp(0.025f, 0.06f, (float)random.NextDouble());
            beads.Sphere(Point(s / span) + Vector3.down * r * 0.6f, new Vector3(r, r * 1.25f, r), Quaternion.identity, 5, 6, 1f, (float)random.NextDouble());
        }
    }

    private void Feet(RealmMeshes mesh, Vector3 from, Node anchor, float thick, System.Random random)
    {
        int count = random.Next(2, 5);
        Vector3 tangent = Vector3.Cross(anchor.normal, Mathf.Abs(anchor.normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        for (int i = 0; i < count; i++)
        {
            float a = (i + (float)random.NextDouble() * 0.5f) / count * Mathf.PI * 2f;
            Vector3 spread = Quaternion.AngleAxis(a * Mathf.Rad2Deg, anchor.normal) * tangent * Mathf.Lerp(0.25f, 0.7f, (float)random.NextDouble());
            Vector3 foot = anchor.position + spread + anchor.normal * 0.02f;
            mesh.Tube(new List<Vector3> { from, Vector3.Lerp(from, foot, 0.5f) + anchor.normal * 0.08f, foot },
                u => thick * Mathf.Lerp(0.9f, 1.6f, u), 5, 0f, 1f, (float)random.NextDouble(), thick);
        }
    }

    /// <summary>A billowing film filling one triangle of the web.</summary>
    private void Film(RealmMeshes mesh, Vector3 a, Vector3 b, Vector3 c, System.Random random)
    {
        const int n = 7;
        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
        float size = (Vector3.Distance(a, b) + Vector3.Distance(b, c) + Vector3.Distance(c, a)) / 3f;
        float billow = size * Mathf.Lerp(0.04f, 0.12f, (float)random.NextDouble()) * (random.NextDouble() < 0.5 ? 1f : -1f) + size * 0.03f;
        // Rows across the triangle, kept a little inside its edges so the strands frame it.
        var rows = new List<List<Vector3>>();
        for (int i = 0; i <= n; i++)
        {
            var row = new List<Vector3>();
            for (int j = 0; j <= n - i; j++)
            {
                float u = i / (float)n, v = j / (float)n, w = 1f - u - v;
                float inset = 0.92f;
                Vector3 centroid = (a + b + c) / 3f;
                Vector3 p = Vector3.Lerp(centroid, a * w + b * u + c * v, inset);
                p += normal * billow * 27f * u * v * w - Vector3.up * size * 0.05f * 27f * u * v * w;
                row.Add(p);
            }
            rows.Add(row);
        }
        mesh.TriangleSheet(rows, normal, (float)random.NextDouble());
    }

    private void Part(string name, RealmMeshes mesh, Material material, bool shadows)
    {
        if (mesh.VertexCount == 0) return;
        GameObject part = RealmSites.Part(name, root, mesh.Build(name), material, 0, false);
        part.transform.position = Vector3.zero;
        if (!shadows) part.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static Vector3 Random3(System.Random random) =>
        new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 2f;

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }
}
