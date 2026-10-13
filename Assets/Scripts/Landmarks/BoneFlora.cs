using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Designed, inorganic "flora" growing on the Bone Kiln's bones: flat geometric blooms (rings of kite
/// petals round a disc and a thin hoop), straight stems with crisp right-angle kinks and beads, planar
/// kite-shaped blades with a centre crease, and wire spirals of constant pitch wound round the bones.
/// Built into one mesh per bone (vertex coloured, drawn with the Flora shader) so they move with it.
/// uv.x = 0 at the stem .. 1 at the tip (for the sway), uv.y = 0..1 across a sheet (for the edge fringe).
/// </summary>
public sealed class BoneFlora
{
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color> colours = new List<Color>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<int> triangles = new List<int>();
    private readonly System.Random random;

    // A small, flat palette: powder pinks and ivory for blooms, grey-sage for blades, ink for wire and hearts.
    private static readonly Color[] Blooms =
    {
        new Color(0.92f, 0.7f, 0.7f), new Color(0.95f, 0.82f, 0.74f), new Color(0.94f, 0.91f, 0.84f), new Color(0.86f, 0.58f, 0.62f),
    };
    private static readonly Color Blade = new Color(0.55f, 0.63f, 0.56f);
    private static readonly Color BladeDark = new Color(0.36f, 0.45f, 0.39f);
    private static readonly Color Ink = new Color(0.08f, 0.07f, 0.09f);

    public BoneFlora(int seed) { random = new System.Random(seed); }

    public bool Empty => vertices.Count == 0;
    private float R(float min, float max) => min + (float)random.NextDouble() * (max - min);
    public float Chance() => (float)random.NextDouble();

    /// <summary>A flat geometric bloom facing along normal: one or two rings of kite petals, a disc and a thin hoop.</summary>
    public void Flower(Vector3 centre, Vector3 normal, float size)
    {
        normal = normal.normalized;
        Vector3 t1 = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 t2 = Vector3.Cross(normal, t1);
        Color colour = Blooms[random.Next(Blooms.Length)];
        int petals = new[] { 4, 5, 6, 8 }[random.Next(4)];
        float turn = R(0f, Mathf.PI * 2f);
        int tiers = Chance() < 0.6f ? 2 : 1;
        for (int tier = 0; tier < tiers; tier++)
        {
            float length = size * (tier == 0 ? 1f : 0.6f);
            Color tint = tier == 0 ? colour : Color.Lerp(colour, Color.white, 0.35f);
            for (int p = 0; p < petals; p++)
            {
                float angle = turn + (p + tier * 0.5f) * Mathf.PI * 2f / petals;
                Vector3 outDir = t1 * Mathf.Cos(angle) + t2 * Mathf.Sin(angle);
                // Flat kites, tilted up a fixed few degrees per tier: a clean stepped rosette.
                Vector3 tilted = (outDir + normal * (0.18f + tier * 0.25f)).normalized;
                Kite(centre + normal * (tier * size * 0.06f), tilted, normal, length, length * 0.42f, tint, Color.Lerp(tint, Ink, 0.15f));
            }
        }
        Disc(centre + normal * size * 0.1f, normal, size * 0.16f, Ink);
        Hoop(centre + normal * size * 0.14f, normal, size * 0.3f, size * 0.025f, Color.Lerp(colour, Ink, 0.5f));
    }

    /// <summary>A planar kite blade from base along dir with a centre crease (tilted towards up a little).</summary>
    public void LeafBlade(Vector3 basePoint, Vector3 dir, Vector3 up, float length)
    {
        dir = dir.normalized;
        Vector3 side = Vector3.Cross(up, dir);
        if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(Vector3.up, dir);
        side.Normalize();
        Vector3 crease = Vector3.Cross(dir, side);
        float width = length * 0.24f;
        // Base, shoulder (widest, a third of the way), tip: two triangles each side of a raised crease.
        Vector3 shoulder = basePoint + dir * (length * 0.36f);
        Vector3 tip = basePoint + dir * length;
        Vector3 ridge = shoulder + crease * (width * 0.25f);
        int b = Add(basePoint, BladeDark, new Vector2(0f, 0.5f));
        int r = Add(ridge, Blade, new Vector2(0.36f, 0.5f));
        int t = Add(tip, Blade, new Vector2(1f, 0.5f));
        int left = Add(shoulder - side * width, Color.Lerp(Blade, BladeDark, 0.4f), new Vector2(0.36f, 0f));
        int right = Add(shoulder + side * width, Color.Lerp(Blade, BladeDark, 0.4f), new Vector2(0.36f, 1f));
        Tri(b, left, r); Tri(r, left, t);
        Tri(b, r, right); Tri(r, t, right);
    }

    /// <summary>A wire spiral of constant pitch round the line from a to b, beaded, with now and then a straight sprig.</summary>
    public void Vine(Vector3 a, Vector3 b, float radius, float turns)
    {
        Vector3 axis = b - a;
        float length = axis.magnitude;
        if (length < 0.2f) return;
        Vector3 dir = axis / length;
        Vector3 u = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 v = Vector3.Cross(dir, u);
        int steps = Mathf.Max(12, Mathf.CeilToInt(turns * 16f));
        float phase = R(0f, Mathf.PI * 2f);
        var points = new List<Vector3>();
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float angle = phase + t * turns * Mathf.PI * 2f;
            points.Add(a + axis * t + (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * radius);
        }
        Rod(points, 0.035f, Ink);
        // Beads at even intervals, a sprig or two standing straight off the spiral.
        for (int i = 4; i < points.Count - 1; i += 8)
        {
            Vector3 outward = (points[i] - (a + axis * (i / (float)steps))).normalized;
            Bead(points[i], 0.09f, Blooms[random.Next(Blooms.Length)]);
            if (Chance() < 0.35f) Sprig(points[i], outward, R(0.5f, 0.9f));
        }
    }

    /// <summary>A small arrangement standing off a surface: straight stems with a right-angle kink, blooms and blades.</summary>
    public void Cluster(Vector3 point, Vector3 normal, float size)
    {
        normal = normal.normalized;
        int stems = random.Next(1, 3);
        for (int i = 0; i < stems; i++) Sprig(point, (normal + Random3() * 0.25f).normalized, size * R(1f, 1.6f));
        int blades = random.Next(1, 3);
        Vector3 t1 = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        for (int i = 0; i < blades; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(R(0f, 360f), normal) * t1;
            LeafBlade(point, (dir + normal * 0.8f).normalized, normal, size * R(0.9f, 1.4f));
        }
    }

    /// <summary>A straight stem from base along dir, kinked once at a right angle, ending in a bloom; a blade off its side.</summary>
    private void Sprig(Vector3 basePoint, Vector3 dir, float length)
    {
        dir = dir.normalized;
        Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        side = Quaternion.AngleAxis(R(0f, 360f), dir) * side;
        Vector3 kink = basePoint + dir * (length * 0.6f);
        Vector3 corner = kink + side * (length * 0.2f);
        Vector3 top = corner + dir * (length * 0.4f);
        Rod(new List<Vector3> { basePoint, kink, kink, corner, corner, top }, 0.03f, Ink);
        Bead(kink, 0.06f, Ink);
        Bead(corner, 0.06f, Ink);
        Flower(top, dir, length * R(0.28f, 0.4f));
        if (Chance() < 0.6f) LeafBlade(Vector3.Lerp(basePoint, kink, 0.5f), (side * -1f + dir).normalized, dir, length * 0.45f);
    }

    /// <summary>Puts what was built on a new child of parent, drawn with material. Returns null when nothing was built.</summary>
    public GameObject Attach(Transform parent, Material material, string name = "Flora")
    {
        if (Empty || material == null) return null;
        var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
        if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetColors(colours);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    // ---------------- Pieces ----------------

    private Vector3 Random3() => new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f));

    /// <summary>A flat kite: base, two shoulders, tip (separate vertices, so it shades flat).</summary>
    private void Kite(Vector3 basePoint, Vector3 outDir, Vector3 normal, float length, float width, Color colour, Color edge)
    {
        Vector3 side = Vector3.Cross(normal, outDir).normalized;
        Vector3 shoulder = basePoint + outDir * (length * 0.55f);
        int b = Add(basePoint, edge, new Vector2(0f, 0.5f));
        int l = Add(shoulder - side * width * 0.5f, colour, new Vector2(0.55f, 0f));
        int r = Add(shoulder + side * width * 0.5f, colour, new Vector2(0.55f, 1f));
        int t = Add(basePoint + outDir * length, Color.Lerp(colour, Color.white, 0.2f), new Vector2(1f, 0.5f));
        Tri(b, l, t);
        Tri(b, t, r);
    }

    private void Disc(Vector3 centre, Vector3 normal, float radius, Color colour)
    {
        Vector3 t1 = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 t2 = Vector3.Cross(normal, t1);
        const int sides = 10;
        int c = Add(centre, colour, new Vector2(0f, 0.5f));
        int first = vertices.Count;
        for (int k = 0; k < sides; k++)
        {
            float angle = k * Mathf.PI * 2f / sides;
            Add(centre + (t1 * Mathf.Cos(angle) + t2 * Mathf.Sin(angle)) * radius, colour, new Vector2(0f, 0.5f));
        }
        for (int k = 0; k < sides; k++) Tri(c, first + k, first + (k + 1) % sides);
    }

    private void Hoop(Vector3 centre, Vector3 normal, float radius, float thickness, Color colour)
    {
        Vector3 t1 = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 t2 = Vector3.Cross(normal, t1);
        var points = new List<Vector3>();
        for (int k = 0; k <= 16; k++)
        {
            float angle = k * Mathf.PI * 2f / 16f;
            points.Add(centre + (t1 * Mathf.Cos(angle) + t2 * Mathf.Sin(angle)) * radius);
        }
        Rod(points, thickness, colour);
    }

    private void Bead(Vector3 centre, float radius, Color colour)
    {
        // An octahedron: crisp and geometric.
        Vector3[] d = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        int[] faces = { 0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2, 1, 4, 2, 1, 3, 4, 1, 5, 3, 1, 2, 5 };
        for (int f = 0; f < faces.Length; f += 3)
        {
            int a = Add(centre + d[faces[f]] * radius, colour, new Vector2(0f, 0.5f));
            int b = Add(centre + d[faces[f + 1]] * radius, colour, new Vector2(0f, 0.5f));
            int c = Add(centre + d[faces[f + 2]] * radius, colour, new Vector2(0f, 0.5f));
            Tri(a, b, c);
        }
    }

    /// <summary>A thin square-section rod through points (repeat a point to make a sharp corner).</summary>
    private void Rod(List<Vector3> points, float radius, Color colour)
    {
        const int sides = 4;
        int start = vertices.Count;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 tangent = (points[Mathf.Min(points.Count - 1, i + 1)] - points[Mathf.Max(0, i - 1)]);
            if (tangent.sqrMagnitude < 1e-8f) tangent = points[Mathf.Min(points.Count - 1, i + 1)] - points[i];
            if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.up;
            tangent.Normalize();
            Vector3 u = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(tangent, u);
            for (int k = 0; k < sides; k++)
            {
                float angle = (k + 0.5f) * Mathf.PI * 2f / sides;
                Add(points[i] + (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * radius, colour, new Vector2(i / (float)Mathf.Max(1, points.Count - 1), 0.5f));
            }
        }
        for (int i = 0; i < points.Count - 1; i++)
        for (int k = 0; k < sides; k++)
        {
            int k1 = (k + 1) % sides;
            int a0 = start + i * sides + k, a1 = start + i * sides + k1, b0 = start + (i + 1) * sides + k, b1 = start + (i + 1) * sides + k1;
            Tri(a0, a1, b1);
            Tri(a0, b1, b0);
        }
    }

    private int Add(Vector3 p, Color c, Vector2 uv)
    {
        vertices.Add(p);
        colours.Add(c);
        uvs.Add(uv);
        return vertices.Count - 1;
    }

    private void Tri(int a, int b, int c)
    {
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
    }
}
