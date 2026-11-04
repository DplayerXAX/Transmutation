using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Seeded surface-constrained venation. Persistent edges extend, branch and reconnect.</summary>
[DisallowMultipleComponent]
public sealed class RecycleNetworkGeometry : MonoBehaviour
{
    [Min(8f)] public float strandBudget = 128f;
    [Min(0.05f)] public float stepLength = 0.16f;
    [Min(0.1f)] public float growthSpeed = 2f;
    [SerializeField] Material strandMaterial;
    sealed class Node
    {
        internal SurfaceAnchor anchor;
        internal int parent;
        internal Vector3 point, normal, heading;
        internal float born, duration, length;
        internal bool valid = true;
        internal SurfaceAnchor fine;
        internal bool hasFine;
    }
    readonly List<Node> nodes = new List<Node>();
    readonly List<int> tips = new List<int>();
    readonly List<SurfaceAnchor> path = new List<SurfaceAnchor>();
    readonly List<SurfaceAnchor> attractions = new List<SurfaceAnchor>();
    readonly Dictionary<Vector3Int, List<int>> cells = new Dictionary<Vector3Int, List<int>>();
    readonly HashSet<int> candidates = new HashSet<int>();
    readonly List<Vector3> vertices = new List<Vector3>();
    readonly List<Vector3> normals = new List<Vector3>();
    readonly List<Vector4> origins = new List<Vector4>();
    readonly List<Vector4> timings = new List<Vector4>();
    readonly List<Color> colours = new List<Color>();
    readonly List<Vector2> uv = new List<Vector2>();
    readonly List<int> triangles = new List<int>();
    System.Random random;
    Mesh mesh;
    MeshRenderer meshRenderer;
    Material runtimeMaterial;
    MaterialPropertyBlock properties;
    float clock, credits, nextStep, length, pulse, visualUntil;
    int nextTip;
    bool dirty, rebind;
    public float EstablishedLength { get; private set; }
    public int NodeCount => nodes.Count;
    public bool Ready => nodes.Count != 0;
    public float Clock => clock;
    public Bounds SurfaceBounds => meshRenderer != null ? meshRenderer.bounds : new Bounds(transform.position, Vector3.zero);

    public void Initialize(SurfaceAnchor seed, int randomSeed, float startingLength)
    {
        if (Ready || !seed.Resolve(out Vector3 point, out Vector3 normal)) return;
        random = new System.Random(randomSeed);
        SurfaceGeometry.Prepare(seed.Support, point, 3f);
        nodes.Add(new Node { anchor = seed, parent = -1, point = point, normal = normal });
        Vector3 forward = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
        for (int i = 0; i < 8; i++)
        {
            Vector3 heading = Quaternion.AngleAxis(i * 45f + (float)random.NextDouble() * 12f, normal) * forward;
            int index = AddStep(0, heading, 0.1f);
            if (index > 0) tips.Add(index);
        }
        credits = Mathf.Max(0f, startingLength - length);
        CreateRenderer(); RefreshMesh();
    }

    void CreateRenderer()
    {
        if (mesh != null) return;
        var child = new GameObject("Procedural Mycelium"); child.transform.SetParent(transform, false);
        mesh = new Mesh { name = "Recycle surface strands", indexFormat = IndexFormat.UInt32 }; mesh.MarkDynamic();
        child.AddComponent<MeshFilter>().sharedMesh = mesh;
        meshRenderer = child.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        if (strandMaterial != null) meshRenderer.sharedMaterial = strandMaterial;
        else { runtimeMaterial = new Material(Shader.Find("Transmutation/Recycle Organic")); meshRenderer.sharedMaterial = runtimeMaterial; }
        properties = new MaterialPropertyBlock();
    }
    public void AddGrowth(long charges)
    { credits = Mathf.Min(strandBudget - length, credits + Mathf.Min(charges, (long)strandBudget)); pulse = 1f; }

    public void Advance(float dt)
    {
        if (!Ready || dt <= 0f) return;
        clock += dt; pulse = Mathf.MoveTowards(pulse, 0f, dt * 0.35f);
        properties.SetFloat("_Clock", clock); properties.SetFloat("_Pulse", pulse);
        meshRenderer.SetPropertyBlock(properties);
        if (rebind) Rebind();
        if (clock < nextStep) return;
        nextStep = clock + 0.1f;
        EstablishedLength = 0f;
        foreach (Node node in nodes)
            if (node.valid && clock >= node.born + node.duration) EstablishedLength += node.length;
        float available = Mathf.Min(credits, growthSpeed * 0.1f);
        int attempts = Mathf.Min(12, tips.Count);
        while (available >= 0.04f && length < strandBudget && attempts-- > 0 && tips.Count > 0)
        {
            int slot = nextTip++ % tips.Count, index = tips[slot]; Node tip = nodes[index];
            if (!tip.valid) { tips.RemoveAt(slot); continue; }
            Vector3 heading = Direction(tip);
            float step = Mathf.Min(stepLength, available);
            int next = AddStep(index, heading, step);
            if (next < 0)
            {
                heading = Quaternion.AngleAxis(80f + (float)random.NextDouble() * 100f, tip.normal) * tip.heading;
                next = AddStep(index, heading, step);
            }
            if (next < 0) { tips.RemoveAt(slot); continue; }
            tips[slot] = next;
            available -= step; credits = Mathf.Max(0f, credits - step);
            if (tips.Count < 16 && random.NextDouble() < 0.12)
            {
                int branch = AddStep(index, Quaternion.AngleAxis(random.NextDouble() < 0.5 ? -55f : 55f, tip.normal) * heading, 0.08f);
                if (branch > 0) { tips.Add(branch); credits = Mathf.Max(0f, credits - 0.08f); }
            }
            TryJoin(next);
        }
        // A completely enclosed frontier can resume from an established node after later feeding.
        if (tips.Count == 0 && credits > 0f)
        {
            for (int tries = 0; tries < 4; tries++)
            {
                int index = random.Next(nodes.Count);
                if (nodes[index].valid) { tips.Add(index); break; }
            }
        }
        if (dirty || pulse > 0f || clock < visualUntil) RefreshMesh();
    }

    Vector3 Direction(Node tip)
    {
        // Local attraction samples are reached by actual surface walks. They cannot sit across an air gap.
        if (attractions.Count < 48)
        {
            for (int i = 0; i < 6; i++)
            {
                Vector3 direction = Quaternion.AngleAxis((float)random.NextDouble() * 360f, tip.normal) * tip.heading;
                if (SurfaceGeometry.Trace(tip.anchor, direction, 0.5f + (float)random.NextDouble(), out var sample)) attractions.Add(sample);
            }
        }
        Vector3 sum = Vector3.zero; int influenced = 0;
        for (int i = attractions.Count - 1; i >= 0; i--)
        {
            if (!attractions[i].Resolve(out Vector3 point, out _)) { attractions.RemoveAt(i); continue; }
            Vector3 delta = point - tip.point;
            if (delta.sqrMagnitude < 0.09f) { attractions.RemoveAt(i); continue; }
            if (delta.sqrMagnitude > 2.25f || Vector3.Dot(delta, tip.heading) < -0.15f) continue;
            Vector3 projected = Vector3.ProjectOnPlane(delta, tip.normal);
            if (!SurfaceGeometry.Trace(tip.anchor, projected, delta.magnitude, out var reached) ||
                !reached.Resolve(out Vector3 end, out _) || (end - point).sqrMagnitude > 0.04f) continue;
            sum += projected.normalized; if (++influenced >= 4) break;
        }
        Vector3 jitter = Quaternion.AngleAxis(((float)random.NextDouble() - 0.5f) * 35f, tip.normal) * tip.heading;
        return (jitter * 0.75f + sum.normalized * 0.25f).normalized;
    }

    int AddStep(int parent, Vector3 direction, float distance)
    {
        Node start = nodes[parent]; path.Clear();
        if (!SurfaceGeometry.Trace(start.anchor, direction, distance, out _, path)) return -1;
        int last = parent;
        foreach (SurfaceAnchor anchor in path)
        {
            if (!anchor.Resolve(out Vector3 point, out Vector3 normal)) return -1;
            float span = Vector3.Distance(nodes[last].point, point);
            if (span < 0.00005f) continue;
            if (length + span > strandBudget) return last == parent ? -1 : last;
            // Every edge stays in one source face; shader growth interpolation is safe within that face.
            var node = new Node { anchor = anchor, parent = last, point = point, normal = normal,
                heading = Quaternion.FromToRotation(start.normal, normal) * direction,
                born = Mathf.Max(clock, nodes[last].born + nodes[last].duration), duration = span / Mathf.Max(0.1f, growthSpeed / 4f), length = span };
            nodes.Add(node); last = nodes.Count - 1; length += span; Index(last); dirty = true;
            visualUntil = Mathf.Max(visualUntil, node.born + node.duration + 2f);
            if (last % 3 == 0)
            {
                Vector3 across = Vector3.Cross(normal, node.heading).normalized;
                node.hasFine = SurfaceGeometry.Trace(anchor, across, 0.045f, out node.fine);
            }
        }
        return last == parent ? -1 : last;
    }

    void TryJoin(int index)
    {
        Node node = nodes[index]; Vector3Int cell = Vector3Int.FloorToInt(node.point);
        if (!cells.TryGetValue(cell, out var nearby)) return;
        foreach (int other in nearby)
        {
            if (other == index || other == node.parent || nodes[other].parent == node.parent || !nodes[other].valid) continue;
            Vector3 delta = nodes[other].point - node.point;
            if (delta.sqrMagnitude < 0.0025f || delta.sqrMagnitude > 0.04f) continue;
            path.Clear();
            if (!SurfaceGeometry.Trace(node.anchor, delta, delta.magnitude, out var end, path) ||
                !end.Resolve(out Vector3 reached, out _) || (reached - nodes[other].point).sqrMagnitude > 0.0004f) continue;
            AddStep(index, delta, delta.magnitude);
            tips.Remove(index); break;
        }
    }
    void Index(int index)
    {
        Node n = nodes[index]; Vector3Int key = Vector3Int.FloorToInt(n.point);
        if (!cells.TryGetValue(key, out var list)) cells.Add(key, list = new List<int>());
        list.Add(index);
    }

    public bool TryContact(Collider target, out Vector3 contact)
    {
        contact = default;
        if (!Ready || target == null || target.isTrigger) return false;
        Bounds bounds = target.bounds; bounds.Expand(0.22f);
        Vector3Int lo = Vector3Int.FloorToInt(bounds.min), hi = Vector3Int.FloorToInt(bounds.max);
        candidates.Clear();
        for (int x = lo.x; x <= hi.x; x++) for (int y = lo.y; y <= hi.y; y++) for (int z = lo.z; z <= hi.z; z++)
            if (cells.TryGetValue(new Vector3Int(x,y,z), out var list)) foreach (int i in list) candidates.Add(i);
        foreach (int i in candidates)
        {
            Node n = nodes[i]; if (!n.valid || n.parent < 0 || clock < n.born + n.duration) continue;
            Vector3 a = nodes[n.parent].point, b = n.point;
            Vector3 p = SurfaceGeometry.SegmentClosest(bounds.center, a, b);
            Vector3 body = target.ClosestPoint(p); p = SurfaceGeometry.SegmentClosest(body, a, b);
            if ((body - p).sqrMagnitude > 0.0081f || Vector3.Dot(body - p, n.normal) < -0.015f) continue;
            contact = p; return true;
        }
        return false;
    }

    public bool TryFlowerSite(IReadOnlyList<RecycleFlower> flowers, out SurfaceAnchor anchor)
    {
        anchor = default;
        for (int i = nodes.Count - 1; i > 0; i--)
        {
            Node n = nodes[i]; if (!n.valid || clock < n.born + n.duration || n.length < 0.02f) continue;
            bool clear = true;
            foreach (var flower in flowers) if (flower != null && flower.IsAttached && (flower.transform.position - n.point).sqrMagnitude < 4f) { clear = false; break; }
            if (!clear) continue;
            anchor = n.anchor; return true;
        }
        return false;
    }

    void RefreshMesh()
    {
        dirty = false;
        vertices.Clear(); normals.Clear(); colours.Clear(); uv.Clear(); triangles.Clear(); origins.Clear(); timings.Clear();
        for (int i = 1; i < nodes.Count; i++)
        {
            Node n = nodes[i], parent = nodes[n.parent]; if (!n.valid || !parent.valid) continue;
            Vector3 direction = (n.point - parent.point).normalized;
            float thickness = Mathf.Lerp(0.012f, 0.003f, Mathf.Clamp01(i / 700f));
            AddCord(parent.point, parent.normal, n.point, n.normal, direction, thickness, n.born, n.duration, i);
            // Fine threads decorate actual supported faces, never enlarge the feeding footprint.
            if (n.hasFine && n.fine.Resolve(out var sidePoint, out var sideNormal))
                AddCord(n.point, n.normal, sidePoint, sideNormal, (sidePoint - n.point).normalized, 0.0015f, n.born + 0.3f, 0.45f, i + 1);
        }
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colours); mesh.SetUVs(0, uv);
        mesh.SetUVs(1, origins); mesh.SetUVs(2, timings); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
        Bounds bounds = mesh.bounds; bounds.Expand(0.08f); mesh.bounds = bounds;
    }

    void AddCord(Vector3 a, Vector3 na, Vector3 b, Vector3 nb, Vector3 direction, float radius, float born, float duration, int id)
    {
        int start = vertices.Count; const int sides = 5;
        Vector3 sa = Vector3.Cross(na, direction).normalized, sb = Vector3.Cross(nb, direction).normalized;
        for (int ring = 0; ring < 2; ring++)
        for (int col = 0; col < sides; col++)
        {
            float angle = col * Mathf.PI / (sides - 1), side = Mathf.Cos(angle) * radius;
            Vector3 n = ring == 0 ? na : nb, centre = ring == 0 ? a : b, across = ring == 0 ? sa : sb;
            Vector3 position = centre + across * side + n * (0.003f + Mathf.Sin(angle) * radius * 0.65f);
            Vector3 closed = a + sa * side + na * 0.003f;
            vertices.Add(transform.InverseTransformPoint(position)); normals.Add(transform.InverseTransformDirection(n * Mathf.Sin(angle) + across * Mathf.Cos(angle)).normalized);
            Vector3 localClosed = transform.InverseTransformPoint(closed);
            origins.Add(new Vector4(localClosed.x, localClosed.y, localClosed.z, born)); timings.Add(new Vector4(Mathf.Max(0.05f, duration), 1f, 0f, 0f));
            float fresh = 1f - Mathf.Clamp01((clock - born) / 2f);
            colours.Add(Color.Lerp(new Color(0.025f, 0.16f, 0.38f, 0.16f), new Color(0.15f, 0.95f, 0.27f, 0.9f), fresh));
            uv.Add(new Vector2(id * 0.16f + ring * Vector3.Distance(a,b), col / (float)(sides - 1)));
        }
        for (int col = 0; col < sides - 1; col++)
        { int p = start + col; triangles.Add(p); triangles.Add(p + sides); triangles.Add(p + 1); triangles.Add(p + 1); triangles.Add(p + sides); triangles.Add(p + sides + 1); }
    }

    public void Invalidate(Bounds bounds)
    { foreach (Node n in nodes) if (bounds.Contains(n.point)) { rebind = true; break; } }
    void Rebind()
    {
        rebind = false; cells.Clear();
        for (int i = 0; i < nodes.Count; i++)
        {
            Node n = nodes[i];
            if (n.parent >= 0 && !nodes[n.parent].valid) { n.valid = false; continue; }
            if (!n.anchor.Resolve(out var p, out var normal))
            {
                if (!SurfaceGeometry.Probe(gameObject.scene, n.point + n.normal * 0.08f, -n.normal, 0.16f, true, out n.anchor)) { n.valid = false; continue; }
                n.anchor.Resolve(out p, out normal);
            }
            n.point = p; n.normal = normal; Index(i);
        }
        dirty = true;
    }
    public bool AnchorSupported(SurfaceAnchor anchor) => anchor.Resolve(out _, out _);
    void OnDestroy()
    {
        if (mesh != null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
        if (runtimeMaterial != null) { if (Application.isPlaying) Destroy(runtimeMaterial); else DestroyImmediate(runtimeMaterial); }
    }
}
