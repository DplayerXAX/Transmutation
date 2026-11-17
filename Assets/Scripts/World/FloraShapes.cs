using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Low-poly "designed" flora meshes for FloraPatch, built in code (faceted, crystalline, no textures).
/// All shapes stand on y = 0 and are about one unit tall or wide, so instances scale them.
/// </summary>
public static class FloraShapes
{
    private sealed class Builder
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<int>[] t;
        public Builder(int submeshes) { t = new List<int>[submeshes]; for (int i = 0; i < submeshes; i++) t[i] = new List<int>(); }

        // Flat-shaded triangle: own vertices so facets read as hard planes.
        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c)
        {
            int i = v.Count; v.Add(a); v.Add(b); v.Add(c);
            t[sub].Add(i); t[sub].Add(i + 1); t[sub].Add(i + 2);
        }
        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(sub, a, b, c); Tri(sub, a, c, d); }

        // Closed n-sided prism between two heights (the stems and discs).
        public void Prism(int sub, int sides, float r0, float r1, float y0, float y1, float twist = 0f)
        {
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = Ring(a0, r0, y0), p1 = Ring(a1, r0, y0), q0 = Ring(a0 + twist, r1, y1), q1 = Ring(a1 + twist, r1, y1);
                Quad(sub, p0, q0, q1, p1);
                Tri(sub, new Vector3(0f, y1, 0f), q1, q0);
                Tri(sub, new Vector3(0f, y0, 0f), p0, p1);
            }
        }

        // Elongated bipyramid (crystal point / lantern bulb).
        public void Crystal(int sub, int sides, float radius, float y0, float waist, float y1, float turn = 0f)
        {
            for (int s = 0; s < sides; s++)
            {
                float a0 = turn + s * Mathf.PI * 2f / sides, a1 = turn + (s + 1) * Mathf.PI * 2f / sides;
                Vector3 m0 = Ring(a0, radius, waist), m1 = Ring(a1, radius, waist);
                Tri(sub, new Vector3(0f, y1, 0f), m1, m0);
                Tri(sub, new Vector3(0f, y0, 0f), m0, m1);
            }
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(v);
            mesh.subMeshCount = t.Length;
            for (int i = 0; i < t.Length; i++) mesh.SetTriangles(t[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    private static Vector3 Ring(float angle, float radius, float y) => new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);

    /// <summary>Tiny standing crystal for the ground specks (about 1 unit tall).</summary>
    public static Mesh Speck()
    {
        var b = new Builder(1);
        b.Crystal(0, 4, 0.28f, 0f, 0.3f, 1f, 0.4f);
        return b.Build("Flora speck");
    }

    // ---------------- Flowers: submesh 0 stem and leaves, 1 petals, 2 centre ----------------

    // A faceted petal (or leaf): kite from base to tip with a raised mid ridge; "lift" bends it up.
    private static void Petal(Builder b, int sub, Vector3 root, Vector3 dir, float length, float width, float lift)
    {
        dir.Normalize();
        Vector3 side = Vector3.Cross(dir, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
        side.Normalize();
        Vector3 normal = Vector3.Cross(side, dir).normalized;
        Vector3 mid = root + dir * length * 0.45f + normal * lift * 0.5f;
        Vector3 tip = root + dir * length + normal * lift;
        Vector3 l = mid + side * width * 0.5f, r = mid - side * width * 0.5f, ridge = mid + normal * width * 0.18f;
        if (normal.y < 0f) normal = -normal;
        Sheet(b, sub, root, ridge, l, normal); Sheet(b, sub, root, r, ridge, normal);
        Sheet(b, sub, l, ridge, tip, normal); Sheet(b, sub, ridge, r, tip, normal);
    }

    // Thin two-sided sheet: an upper face whose normal points along "up" (so sunlight lights the top of a petal)
    // and a lower face 2 mm below facing the other way. A single face drawn from both sides kept its normal and
    // looked dark from above when the winding happened to point down.
    private static void Sheet(Builder b, int sub, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 up)
    {
        if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), up) < 0f) { Vector3 t = p1; p1 = p2; p2 = t; }
        b.Tri(sub, p0, p1, p2);
        Vector3 down = -up * 0.002f;
        b.Tri(sub, p0 + down, p2 + down, p1 + down);
    }

    private static void Leaves(Builder b, float height, int count, float size)
    {
        for (int i = 0; i < count; i++)
        {
            float a = i * 2.4f + 0.5f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0.55f, Mathf.Sin(a));
            Petal(b, 0, new Vector3(0f, height * (0.05f + 0.12f * i), 0f), dir, size, size * 0.32f, size * 0.15f);
        }
    }

    /// <summary>Low daisy: thin stem, flat ring of narrow petals round a domed centre (head at y = 1).</summary>
    public static Mesh Daisy(int petals, float length = 0.36f, float width = 0.1f, float cup = 0.22f, float centreSize = 0.09f, float stemHeight = 1f)
    {
        var b = new Builder(3);
        b.Prism(0, 4, 0.016f, 0.011f, 0f, stemHeight - 0.02f, 0.4f);
        Leaves(b, Mathf.Max(0.3f, stemHeight), 1, 0.22f);
        // Head tipped a little towards one side, as real daisies face the light.
        Quaternion tip = Quaternion.Euler(18f, 0f, 0f);
        Vector3 head = new Vector3(0f, stemHeight, 0f);
        for (int i = 0; i < petals; i++)
        {
            float a = i * Mathf.PI * 2f / petals + (i % 2) * 0.08f;
            float l = length * (i % 2 == 0 ? 1f : 0.88f);
            Petal(b, 1, head, tip * new Vector3(Mathf.Cos(a), cup, Mathf.Sin(a)), l, width, 0.04f);
        }
        b.Crystal(2, 7, centreSize, stemHeight - 0.04f, stemHeight, stemHeight + centreSize * 0.9f);
        return b.Build("Flora daisy " + petals);
    }

    /// <summary>Faceted cup (tulip-like): two rings of upright petals round stamens.</summary>
    public static Mesh Cup()
    {
        var b = new Builder(3);
        b.Prism(0, 4, 0.024f, 0.018f, 0f, 0.74f, 0.3f);
        Leaves(b, 1f, 2, 0.3f);
        for (int ring = 0; ring < 2; ring++)
            for (int i = 0; i < 5; i++)
            {
                float a = (i + ring * 0.5f) * Mathf.PI * 2f / 5f, open = ring == 0 ? 0.55f : 0.32f;
                Vector3 root = Ring(a, 0.03f, 0.72f);
                Petal(b, 1, root, new Vector3(Mathf.Cos(a) * open, 1f, Mathf.Sin(a) * open), ring == 0 ? 0.4f : 0.34f, 0.2f, -0.06f);
            }
        for (int i = 0; i < 3; i++) b.Crystal(2, 3, 0.018f, 0.74f, 0.86f, 0.9f + i * 0.02f, i);
        return b.Build("Flora cup");
    }

    /// <summary>Tall spike (lupine-like): whorls of small petals climbing the top half of the stem.</summary>
    public static Mesh Spike()
    {
        var b = new Builder(3);
        b.Prism(0, 4, 0.024f, 0.012f, 0f, 1f, 0.6f);
        Leaves(b, 1f, 2, 0.24f);
        const int Whorls = 9;
        for (int w = 0; w < Whorls; w++)
        {
            float t = w / (float)(Whorls - 1), y = Mathf.Lerp(0.42f, 0.97f, t), size = Mathf.Lerp(0.15f, 0.07f, t);
            for (int i = 0; i < 3; i++)
            {
                float a = (i + w * 0.37f) * Mathf.PI * 2f / 3f;
                Petal(b, 1, new Vector3(0f, y, 0f), new Vector3(Mathf.Cos(a), 0.35f, Mathf.Sin(a)), size, size * 0.8f, size * 0.4f);
            }
        }
        b.Crystal(2, 4, 0.02f, 0.96f, 1f, 1.06f);
        return b.Build("Flora spike");
    }

    /// <summary>Umbel: rays from the stem top, each ending in a tiny five-petal floret, forming a flat dome.</summary>
    public static Mesh Umbel()
    {
        var b = new Builder(3);
        b.Prism(0, 4, 0.022f, 0.016f, 0f, 0.8f, 0.2f);
        Leaves(b, 1f, 1, 0.26f);
        const int Rays = 9;
        for (int i = 0; i < Rays; i++)
        {
            float a = i * 2.39996f, d = 0.07f + 0.2f * Mathf.Sqrt((i + 0.5f) / Rays);
            Vector3 end = new Vector3(Mathf.Cos(a) * d, 0.97f - d * 0.35f, Mathf.Sin(a) * d);
            // A ray: thin strip from the stem top to the floret.
            Vector3 top = new Vector3(0f, 0.8f, 0f), side = Vector3.Cross(end - top, Vector3.up).normalized * 0.006f;
            b.Quad(0, top - side, end - side, end + side, top + side);
            for (int k = 0; k < 5; k++)
            {
                float pa = k * Mathf.PI * 2f / 5f + i;
                Petal(b, 1, end, new Vector3(Mathf.Cos(pa), 0.25f, Mathf.Sin(pa)), 0.08f, 0.055f, 0.015f);
            }
            b.Crystal(2, 3, 0.01f, end.y - 0.005f, end.y, end.y + 0.02f);
        }
        return b.Build("Flora umbel");
    }

    /// <summary>Arching stem with three hanging bells with flared, pointed rims.</summary>
    public static Mesh Bells()
    {
        var b = new Builder(3);
        Vector3 Arch(float u) => new Vector3(u * 0.32f, Mathf.Sin(u * Mathf.PI * 0.62f) * 1f, 0f);
        for (int s = 0; s < 6; s++)
        {
            Vector3 p0 = Arch(s / 6f), p1 = Arch((s + 1) / 6f), side = new Vector3(0f, 0f, 0.012f);
            b.Quad(0, p0 - side, p1 - side, p1 + side, p0 + side);
            b.Quad(0, p0 + Vector3.up * 0.012f, p1 + Vector3.up * 0.012f, p1 - Vector3.up * 0.012f, p0 - Vector3.up * 0.012f);
        }
        Leaves(b, 0.8f, 1, 0.24f);
        foreach (float u in new[] { 0.62f, 0.8f, 0.98f })
        {
            Vector3 hang = Arch(u), mouth = hang + Vector3.down * 0.16f;
            for (int i = 0; i < 6; i++)
            {
                float a0 = i * Mathf.PI / 3f, a1 = (i + 1) * Mathf.PI / 3f;
                Vector3 r0 = mouth + Ring(a0, 0.1f, 0f), r1 = mouth + Ring(a1, 0.1f, 0f);
                b.Tri(1, hang, r0, r1);
                b.Tri(1, r0, mouth + Ring((a0 + a1) * 0.5f, 0.15f, -0.05f), r1);   // pointed flare
            }
            b.Crystal(2, 3, 0.012f, mouth.y - 0.05f, mouth.y, mouth.y + 0.03f);
        }
        return b.Build("Flora bells");
    }

    /// <summary>Starburst (allium-like): a ball of thin rays on a tall bare stem.</summary>
    public static Mesh Starburst()
    {
        var b = new Builder(3);
        b.Prism(0, 4, 0.022f, 0.014f, 0f, 0.84f, 0.5f);
        Vector3 c = new Vector3(0f, 0.92f, 0f);
        for (int i = 0; i < 22; i++)
        {
            float y = 1f - (i + 0.5f) / 22f * 1.6f, r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)), a = i * 2.39996f;
            Vector3 dir = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            Petal(b, 1, c, dir, 0.16f, 0.045f, 0f);
        }
        b.Crystal(2, 5, 0.035f, 0.88f, 0.92f, 0.96f);
        return b.Build("Flora starburst");
    }

    /// <summary>Flat star of pointed, ridged blades (unit radius), lying low on the ground.</summary>
    public static Mesh Rosette(int blades)
    {
        var b = new Builder(1);
        for (int i = 0; i < blades; i++)
        {
            float a = i * Mathf.PI * 2f / blades + (i % 2) * 0.12f;
            float length = i % 2 == 0 ? 1f : 0.72f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), side = new Vector3(-dir.z, 0f, dir.x);
            Vector3 root = new Vector3(0f, 0.04f, 0f), tip = dir * length + Vector3.up * 0.16f * length;
            Vector3 left = dir * 0.34f * length + side * 0.11f + Vector3.up * 0.05f;
            Vector3 right = dir * 0.34f * length - side * 0.11f + Vector3.up * 0.05f;
            Vector3 ridge = dir * 0.4f * length + Vector3.up * 0.13f;
            b.Tri(0, root, ridge, left); b.Tri(0, root, right, ridge);
            b.Tri(0, left, ridge, tip); b.Tri(0, ridge, right, tip);
            // Underside so the blade is solid from low angles.
            b.Tri(0, root, left, tip); b.Tri(0, root, tip, right);
        }
        b.Crystal(0, 5, 0.09f, 0.02f, 0.1f, 0.26f);
        return b.Build("Flora rosette " + blades);
    }

    /// <summary>Thin stem with stacked hexagonal discs and a crystal tip; submesh 0 stem, 1 discs + tip.</summary>
    public static Mesh Stack(int discs, float spread)
    {
        var b = new Builder(2);
        b.Prism(0, 4, 0.022f, 0.014f, 0f, 1f, 0.3f);
        for (int i = 0; i < discs; i++)
        {
            float y = Mathf.Lerp(0.42f, 0.9f, discs == 1 ? 1f : i / (float)(discs - 1));
            float r = Mathf.Lerp(0.2f * spread, 0.07f, discs == 1 ? 0f : i / (float)(discs - 1));
            b.Prism(1, 6, r, r * 0.82f, y, y + 0.025f, i * 0.26f);
        }
        b.Crystal(1, 4, 0.04f, 0.97f, 1.02f, 1.13f);
        return b.Build("Flora stack " + discs);
    }

    /// <summary>Tall focal stalk with a lantern: submesh 0 stem and spikes, 1 the glowing bulb.</summary>
    public static Mesh Lantern()
    {
        var b = new Builder(2);
        b.Prism(0, 5, 0.03f, 0.016f, 0f, 0.8f, 0.5f);
        // Collar of downward spikes holding the bulb.
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            Vector3 root = new Vector3(0f, 0.8f, 0f), tip = Ring(a, 0.12f, 0.74f);
            Vector3 l = Ring(a - 0.25f, 0.035f, 0.81f), r = Ring(a + 0.25f, 0.035f, 0.81f);
            b.Tri(0, root, r, tip); b.Tri(0, root, tip, l); b.Tri(0, l, tip, r);
        }
        b.Crystal(1, 6, 0.085f, 0.79f, 0.88f, 1f, 0.26f);
        return b.Build("Flora lantern");
    }

    /// <summary>
    /// Sagging strands between pairs of points (world space), thick at both ends like webbed adhesion and thin
    /// in the middle.
    /// </summary>
    public static Mesh Strands(IReadOnlyList<(Vector3 a, Vector3 b)> pairs, float radius)
    {
        var b = new Builder(1);
        const int Segments = 12, Sides = 3;
        foreach (var (from, to) in pairs)
        {
            float sag = Vector3.Distance(from, to) * 0.12f;
            Vector3 Point(float u) => Vector3.Lerp(from, to, u) + Vector3.down * sag * 4f * u * (1f - u);
            for (int s = 0; s < Segments; s++)
            {
                float u0 = s / (float)Segments, u1 = (s + 1) / (float)Segments;
                float r0 = radius * (0.35f + 1.9f * Mathf.Pow(Mathf.Abs(u0 * 2f - 1f), 4f));
                float r1 = radius * (0.35f + 1.9f * Mathf.Pow(Mathf.Abs(u1 * 2f - 1f), 4f));
                Vector3 p0 = Point(u0), p1 = Point(u1), axis = (p1 - p0).normalized;
                Vector3 n = Vector3.Cross(axis, Vector3.up).normalized, m = Vector3.Cross(axis, n);
                for (int k = 0; k < Sides; k++)
                {
                    float a0 = k * Mathf.PI * 2f / Sides, a1 = (k + 1) * Mathf.PI * 2f / Sides;
                    Vector3 d0 = n * Mathf.Cos(a0) + m * Mathf.Sin(a0), d1 = n * Mathf.Cos(a1) + m * Mathf.Sin(a1);
                    b.Quad(0, p0 + d0 * r0, p1 + d0 * r1, p1 + d1 * r1, p0 + d1 * r0);
                }
            }
        }
        return b.Build("Flora strands");
    }
}
