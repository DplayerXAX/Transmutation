using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Faceted mesh builders for the Ore Colossus and the Geode Larva: rough rock limbs, boulders, slabs,
/// crystals and geode halves. Every triangle has its own vertices, so lighting shows flat facets.
/// Meshes are readable (the meditation journal copies them) and DontSave; owners destroy them.
/// </summary>
public static class OreShapes
{
    /// <summary>Flat-shaded mesh builder; submesh 0 is rock, 1 is ore/crystal.</summary>
    public sealed class Facets
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int>[] triangles = { new List<int>(), new List<int>() };

        /// <summary>Adds a triangle facing along outward (winding is fixed up to match).</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, int submesh = 0)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { Vector3 t = b; b = c; c = t; }
            List<int> list = triangles[submesh];
            list.Add(vertices.Count); vertices.Add(a);
            list.Add(vertices.Count); vertices.Add(b);
            list.Add(vertices.Count); vertices.Add(c);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, int submesh = 0)
        {
            Tri(a, b, c, outward, submesh);
            Tri(a, c, d, outward, submesh);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave,
                indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            // Empty submeshes are dropped: an all-crystal mesh has a single submesh.
            var used = new List<List<int>>();
            foreach (List<int> list in triangles) if (list.Count > 0) used.Add(list);
            mesh.subMeshCount = Mathf.Max(1, used.Count);
            for (int i = 0; i < used.Count; i++) mesh.SetTriangles(used[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    private static float Noise(System.Random random, float amount) => ((float)random.NextDouble() * 2f - 1f) * amount;

    /// <summary>A rough rock column along +Z from 0 to length, tapering from radiusA to radiusB, with blunt ends.</summary>
    public static Mesh Limb(float length, float radiusA, float radiusB, int sides, int seed, string name = "Ore Limb")
    {
        var f = new Facets();
        var random = new System.Random(seed);
        float[] t = { 0f, 0.12f, 0.45f, 0.78f, 1f };
        float[] swell = { 0.8f, 1.08f, 0.94f, 1.05f, 0.75f };
        var rings = new Vector3[t.Length][];
        for (int r = 0; r < t.Length; r++)
        {
            rings[r] = new Vector3[sides];
            float radius = Mathf.Lerp(radiusA, radiusB, t[r]) * swell[r];
            float twist = Noise(random, 0.3f);
            for (int k = 0; k < sides; k++)
            {
                float a = (k + twist) * Mathf.PI * 2f / sides;
                float jitter = 1f + Noise(random, 0.16f);
                rings[r][k] = new Vector3(Mathf.Cos(a) * radius * jitter, Mathf.Sin(a) * radius * jitter, t[r] * length + Noise(random, 0.06f) * length * (r > 0 && r < t.Length - 1 ? 1f : 0f));
            }
        }
        for (int r = 0; r < t.Length - 1; r++)
            for (int k = 0; k < sides; k++)
            {
                int k1 = (k + 1) % sides;
                Vector3 mid = (rings[r][k] + rings[r + 1][k1]) * 0.5f;
                Vector3 outward = new Vector3(mid.x, mid.y, 0f);
                f.Quad(rings[r][k], rings[r][k1], rings[r + 1][k1], rings[r + 1][k], outward);
            }
        Vector3 start = new Vector3(0f, 0f, -radiusA * 0.25f), end = new Vector3(0f, 0f, length + radiusB * 0.25f);
        for (int k = 0; k < sides; k++)
        {
            int k1 = (k + 1) % sides;
            f.Tri(start, rings[0][k], rings[0][k1], Vector3.back);
            f.Tri(end, rings[t.Length - 1][k], rings[t.Length - 1][k1], Vector3.forward);
        }
        return f.ToMesh(name);
    }

    /// <summary>A lumpy faceted ellipsoid of the given full size, centred on the origin.</summary>
    public static Mesh Boulder(Vector3 size, int seed, string name = "Ore Boulder", float lumpiness = 0.14f)
    {
        var f = new Facets();
        AddBoulder(f, Vector3.zero, size * 0.5f, seed, lumpiness, 0);
        return f.ToMesh(name);
    }

    private static void AddBoulder(Facets f, Vector3 centre, Vector3 radii, int seed, float lumpiness, int submesh)
    {
        var random = new System.Random(seed);
        const int lat = 6, lon = 9;
        var points = new Vector3[lat + 1, lon];
        for (int i = 0; i <= lat; i++)
            for (int k = 0; k < lon; k++)
            {
                float theta = i * Mathf.PI / lat, phi = (k + (i % 2) * 0.5f) * Mathf.PI * 2f / lon;
                var n = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                float bump = (i == 0 || i == lat) ? 1f : 1f + Noise(random, lumpiness);
                points[i, k] = centre + Vector3.Scale(n, radii) * bump;
            }
        for (int i = 0; i < lat; i++)
            for (int k = 0; k < lon; k++)
            {
                int k1 = (k + 1) % lon;
                Vector3 a = points[i, k], b = points[i, k1], c = points[i + 1, k1], d = points[i + 1, k];
                Vector3 outward = (a + c) * 0.5f - centre;
                if (i == 0) f.Tri(a, c, d, outward, submesh);
                else if (i == lat - 1) f.Tri(a, b, c, outward, submesh);
                else f.Quad(a, b, c, d, outward, submesh);
            }
    }

    /// <summary>A faceted slab along +Z from 0 to length (width on x, thickness down from y = 0).</summary>
    public static Mesh Slab(float length, float width, float thickness, int seed, string name = "Ore Slab")
    {
        var f = new Facets();
        var random = new System.Random(seed);
        float w = width * 0.5f;
        // Flat top (it is walked on); chipped, bevelled sides and underside.
        Vector3[] top = { new Vector3(-w, 0f, 0f), new Vector3(w, 0f, 0f), new Vector3(w, 0f, length), new Vector3(-w, 0f, length) };
        Vector3[] bottom = new Vector3[4];
        for (int i = 0; i < 4; i++) bottom[i] = top[i] + new Vector3(-Mathf.Sign(top[i].x) * w * 0.12f + Noise(random, 0.05f), -thickness, Noise(random, 0.05f));
        f.Quad(top[0], top[1], top[2], top[3], Vector3.up);
        f.Quad(bottom[0], bottom[1], bottom[2], bottom[3], Vector3.down);
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            Vector3 mid = (top[i] + top[j]) * 0.5f;
            Vector3 outward = new Vector3(mid.x, 0f, mid.z - length * 0.5f);
            f.Quad(top[i], top[j], bottom[j], bottom[i], outward);
        }
        return f.ToMesh(name);
    }

    /// <summary>One crystal prism pointing along +Y from the origin, with a pyramid tip (submesh 1 = ore).</summary>
    public static void AddCrystal(Facets f, Vector3 basePoint, Vector3 axis, float radius, float height, int sides, System.Random random, int submesh = 1)
    {
        axis = axis.normalized;
        Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 v = Vector3.Cross(axis, u);
        float shaft = height * (0.62f + Noise(random, 0.1f));
        var low = new Vector3[sides];
        var high = new Vector3[sides];
        float twist = (float)random.NextDouble();
        for (int k = 0; k < sides; k++)
        {
            float a = (k + twist) * Mathf.PI * 2f / sides;
            Vector3 radial = (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
            low[k] = basePoint + radial * 0.85f;
            high[k] = basePoint + axis * shaft + radial;
        }
        Vector3 tip = basePoint + axis * height;
        for (int k = 0; k < sides; k++)
        {
            int k1 = (k + 1) % sides;
            Vector3 outward = (high[k] + high[k1]) * 0.5f - (basePoint + axis * shaft);
            f.Quad(low[k], low[k1], high[k1], high[k], outward, submesh);
            f.Tri(high[k], high[k1], tip, outward + axis * radius, submesh);
            f.Tri(basePoint, low[k], low[k1], -axis, submesh);
        }
    }

    /// <summary>A spray of crystals rooted near the origin, pointing roughly along +Y.</summary>
    public static Mesh ShardCluster(int count, float size, int seed, string name = "Ore Shards")
    {
        var f = new Facets();
        var random = new System.Random(seed);
        for (int i = 0; i < count; i++)
        {
            Vector3 axis = (Vector3.up + new Vector3(Noise(random, 0.75f), 0f, Noise(random, 0.75f))).normalized;
            Vector3 at = new Vector3(Noise(random, size * 0.35f), 0f, Noise(random, size * 0.35f));
            float scale = size * (0.55f + (float)random.NextDouble() * 0.6f);
            AddCrystal(f, at - axis * scale * 0.15f, axis, scale * 0.16f, scale, 5, random);
        }
        return f.ToMesh(name);
    }

    /// <summary>A ball of crystals radiating from the centre: the colossus heart.</summary>
    public static Mesh HeartCrystal(float radius, int seed, string name = "Ore Heart")
    {
        var f = new Facets();
        var random = new System.Random(seed);
        AddBoulder(f, Vector3.zero, Vector3.one * radius * 0.62f, seed, 0.1f, 1);
        for (int i = 0; i < 14; i++)
        {
            Vector3 axis = Direction(random);
            float length = radius * (0.7f + (float)random.NextDouble() * 0.45f);
            AddCrystal(f, axis * radius * 0.25f, axis, radius * 0.2f, length, 5, random);
        }
        return f.ToMesh(name);
    }

    /// <summary>The Geode Larva: faceted beads along +Z (submesh 0) with crystal spines on its back (submesh 1).</summary>
    public static Mesh Larva(int seed, string name = "Geode Larva")
    {
        var f = new Facets();
        var random = new System.Random(seed);
        float[] z = { 0.3f, 0.13f, -0.05f, -0.21f, -0.34f };
        float[] r = { 0.19f, 0.23f, 0.21f, 0.17f, 0.12f };
        for (int i = 0; i < z.Length; i++)
        {
            var centre = new Vector3(0f, 0f, z[i]);
            AddBoulder(f, centre, new Vector3(r[i], r[i] * 0.85f, r[i] * 0.9f), seed + i, 0.1f, 0);
            if (i == 0 || i == z.Length - 1) continue;
            Vector3 axis = (Vector3.up + Vector3.back * 0.35f + Vector3.right * Noise(random, 0.3f)).normalized;
            AddCrystal(f, centre + Vector3.up * r[i] * 0.6f, axis, r[i] * 0.22f, r[i] * 1.2f, 5, random, 1);
        }
        return f.ToMesh(name);
    }

    private static Vector3 Direction(System.Random random)
    {
        Vector3 v;
        do v = new Vector3(Noise(random, 1f), Noise(random, 1f), Noise(random, 1f));
        while (v.sqrMagnitude > 1f || v.sqrMagnitude < 0.05f);
        return v.normalized;
    }

    /// <summary>
    /// One half of a hollow geode split on the x = 0 plane, side = -1 (left) or +1 (right). Rock outside
    /// (submesh 0), crystal-lined inside (submesh 1). The origin is the geode centre.
    /// </summary>
    public static Mesh GeodeHalf(float radius, float shell, int side, int seed, string name = "Geode Half")
    {
        var f = new Facets();
        var random = new System.Random(seed);
        const int lat = 6, lon = 5;
        var outer = new Vector3[lat + 1, lon + 1];
        var inner = new Vector3[lat + 1, lon + 1];
        for (int i = 0; i <= lat; i++)
            for (int k = 0; k <= lon; k++)
            {
                float theta = i * Mathf.PI / lat;
                float phi = -Mathf.PI * 0.5f + k * Mathf.PI / lon; // half circle on the +x side
                var n = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi) * side, Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                bool rim = k == 0 || k == lon;
                outer[i, k] = n * radius * (rim || i == 0 || i == lat ? 1f : 1f + Noise(random, 0.08f));
                inner[i, k] = n * (radius - shell);
            }
        for (int i = 0; i < lat; i++)
            for (int k = 0; k < lon; k++)
            {
                Vector3 mid = (outer[i, k] + outer[i + 1, k + 1]) * 0.5f;
                f.Quad(outer[i, k], outer[i, k + 1], outer[i + 1, k + 1], outer[i + 1, k], mid, 0);
                f.Quad(inner[i, k], inner[i, k + 1], inner[i + 1, k + 1], inner[i + 1, k], -mid, 1);
            }
        // The cut face: a ring between outer and inner along the rim.
        Vector3 cut = Vector3.left * side;
        for (int i = 0; i < lat; i++)
        {
            f.Quad(outer[i, 0], outer[i + 1, 0], inner[i + 1, 0], inner[i, 0], cut, 1);
            f.Quad(outer[i, lon], outer[i + 1, lon], inner[i + 1, lon], inner[i, lon], cut, 1);
        }
        // Crystals lining the hollow.
        for (int c = 0; c < 10; c++)
        {
            Vector3 n = Direction(random);
            n.x = Mathf.Abs(n.x) * side;
            AddCrystal(f, n * (radius - shell), -n, radius * 0.08f, radius * (0.2f + (float)random.NextDouble() * 0.2f), 5, random);
        }
        return f.ToMesh(name);
    }
}
