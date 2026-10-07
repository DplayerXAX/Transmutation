using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Irregular stone slab shape. The same deformation is used for the slab body and
/// for the drawing face on its front, so the face always follows the stone.
/// </summary>
public struct SlabShape
{
    public float width, height, depth;
    public float taper;     // Width multiplier at the top.
    public float bend;      // Sideways drift of the top, metres.
    public float lean;      // Backward drift of the top, metres (negative = away from the front).
    public float roughness; // Noise displacement, metres.
    public int seed;

    public static SlabShape Random(System.Random random, float width, float height, float depth)
    {
        return new SlabShape
        {
            width = width * Range(random, 0.85f, 1.15f),
            height = height * Range(random, 0.85f, 1.2f),
            depth = depth * Range(random, 0.8f, 1.3f),
            taper = Range(random, 0.55f, 1.05f),
            bend = Range(random, -0.18f, 0.18f),
            lean = Range(random, -0.25f, 0.02f),
            roughness = Range(random, 0.03f, 0.08f),
            seed = random.Next(1, 100000)
        };
    }

    public Vector3 Deform(Vector3 p)
    {
        float t = Mathf.Clamp01(p.y / height);
        p.x *= Mathf.Lerp(1f, taper, t);
        p.x += bend * t * t;
        p.z += lean * t * t;
        return p;
    }

    private static float Range(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }
}

/// <summary>Procedural meshes for the reflection space.</summary>
public static class MeditationShapes
{
    public static float Noise3(Vector3 p)
    {
        return (Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y + 31.7f, p.z) +
                Mathf.PerlinNoise(p.z + 71.3f, p.x + 5.1f)) / 3f;
    }

    /// <summary>Unit UV sphere, used by the veil.</summary>
    public static Mesh UnitSphere(int longitude = 72, int latitude = 36)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int y = 0; y <= latitude; y++)
        {
            float v = (float)y / latitude * Mathf.PI;
            for (int x = 0; x <= longitude; x++)
            {
                float u = (float)x / longitude * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Sin(v) * Mathf.Cos(u), Mathf.Cos(v), Mathf.Sin(v) * Mathf.Sin(u)));
            }
        }
        int row = longitude + 1;
        for (int y = 0; y < latitude; y++)
        for (int x = 0; x < longitude; x++)
        {
            int a = y * row + x, b = a + 1, c = a + row, d = c + 1;
            triangles.Add(a); triangles.Add(c); triangles.Add(b);
            triangles.Add(b); triangles.Add(c); triangles.Add(d);
        }
        FixWinding(vertices, triangles, centroid => centroid);
        var mesh = new Mesh { name = "Veil Sphere" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        // Large bounds so the veil is never culled while the camera is inside it.
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
        return mesh;
    }

    /// <summary>Rounded, rough stone slab standing on y = 0, front facing +z.</summary>
    public static Mesh Slab(SlabShape shape, int resolution = 14)
    {
        var vertices = new List<Vector3>();
        var raw = new List<Vector3>();
        var uvs = new List<Vector2>();
        var masks = new List<Vector2>();
        var triangles = new List<int>();
        var welded = new Dictionary<Vector3Int, int>();
        Vector3 offset = new Vector3(shape.seed * 0.013f, shape.seed * 0.007f, 3.3f);

        Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        foreach (Vector3 n in normals)
        {
            Vector3 u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
            Vector3 v = Vector3.Cross(n, u);
            var index = new int[resolution + 1, resolution + 1];
            for (int i = 0; i <= resolution; i++)
            for (int j = 0; j <= resolution; j++)
            {
                Vector3 cube = n + u * (2f * i / resolution - 1f) + v * (2f * j / resolution - 1f);
                var key = Vector3Int.RoundToInt(cube * 1000f);
                if (!welded.TryGetValue(key, out int id))
                {
                    Vector3 q = SuperEllipse(cube, 6f);
                    raw.Add(q);
                    // Front projection for drawing; uv1.x fades the ink off the sides.
                    uvs.Add(new Vector2(q.x * 0.5f + 0.5f, q.y * 0.5f + 0.5f));
                    masks.Add(new Vector2(Mathf.SmoothStep(0f, 1f, (q.z - 0.3f) / 0.5f), 0f));
                    vertices.Add(SlabPoint(shape, q, offset));
                    id = vertices.Count - 1;
                    welded[key] = id;
                }
                index[i, j] = id;
            }
            for (int i = 0; i < resolution; i++)
            for (int j = 0; j < resolution; j++)
            {
                int a = index[i, j], b = index[i + 1, j], c = index[i, j + 1], d = index[i + 1, j + 1];
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(b); triangles.Add(d); triangles.Add(c);
            }
        }
        // Winding is decided on the convex rounded box before deformation.
        FixWinding(raw, triangles, centroid => centroid);

        var mesh = new Mesh { name = "Stele" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, masks);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector3 SuperEllipse(Vector3 p, float e)
    {
        float s = Mathf.Pow(Mathf.Pow(Mathf.Abs(p.x), e) + Mathf.Pow(Mathf.Abs(p.y), e) + Mathf.Pow(Mathf.Abs(p.z), e), 1f / e);
        return p / s;
    }

    private static Vector3 SlabPoint(SlabShape shape, Vector3 q, Vector3 offset)
    {
        Vector3 p = new Vector3(q.x * shape.width * 0.5f, (q.y * 0.5f + 0.5f) * shape.height, q.z * shape.depth * 0.5f);
        // The front stays nearly flat so the drawing face sits on it.
        float front = Mathf.Clamp01((q.z - 0.55f) / 0.3f);
        float amount = shape.roughness * Mathf.Lerp(1f, 0.08f, front);
        // Ragged top edge.
        if (q.y > 0.4f) amount *= 1.8f;
        float n = Noise3(q * 1.7f + offset) * 2f - 1f;
        Vector3 dir = new Vector3(q.x, q.y * 0.5f, q.z).normalized;
        p += dir * n * amount * 2f;
        return shape.Deform(p);
    }

    /// <summary>Slightly bulged drawing surface on the front of a slab. UV covers 0..1.</summary>
    public static Mesh Face(SlabShape shape, float faceWidth, float faceHeight, float centreY, float bulge = 0.015f)
    {
        const int segX = 16, segY = 20;
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        float z = shape.depth * 0.5f + 0.012f;
        for (int j = 0; j <= segY; j++)
        for (int i = 0; i <= segX; i++)
        {
            float u = (float)i / segX, v = (float)j / segY;
            float cu = u * 2f - 1f, cv = v * 2f - 1f;
            var p = new Vector3(cu * faceWidth * 0.5f, centreY + cv * faceHeight * 0.5f,
                z + bulge * (1f - cu * cu) * (1f - cv * cv));
            vertices.Add(shape.Deform(p));
            uvs.Add(new Vector2(u, v));
        }
        int row = segX + 1;
        for (int j = 0; j < segY; j++)
        for (int i = 0; i < segX; i++)
        {
            int a = j * row + i, b = a + 1, c = a + row, d = c + 1;
            // Normal points +z, so the face looks forward.
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(b); triangles.Add(d); triangles.Add(c);
        }
        var mesh = new Mesh { name = "Stele Face" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Flat floor disc on y = 0 with uv mapped across its diameter.</summary>
    public static Mesh Disc(float radius, int rings = 24, int segments = 96)
    {
        var vertices = new List<Vector3> { Vector3.zero };
        var uvs = new List<Vector2> { new Vector2(0.5f, 0.5f) };
        var triangles = new List<int>();
        for (int r = 1; r <= rings; r++)
        {
            float d = radius * r / rings;
            for (int s = 0; s < segments; s++)
            {
                float a = s * Mathf.PI * 2f / segments;
                var p = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                vertices.Add(p);
                uvs.Add(new Vector2(p.x / (2f * radius) + 0.5f, p.z / (2f * radius) + 0.5f));
            }
        }
        for (int s = 0; s < segments; s++)
        {
            int s1 = (s + 1) % segments;
            triangles.Add(0); triangles.Add(1 + s1); triangles.Add(1 + s);
        }
        for (int r = 1; r < rings; r++)
        {
            int inner = 1 + (r - 1) * segments, outer = 1 + r * segments;
            for (int s = 0; s < segments; s++)
            {
                int s1 = (s + 1) % segments;
                triangles.Add(inner + s); triangles.Add(inner + s1); triangles.Add(outer + s);
                triangles.Add(inner + s1); triangles.Add(outer + s1); triangles.Add(outer + s);
            }
        }
        FixWinding(vertices, triangles, centroid => Vector3.up);
        var mesh = new Mesh { name = "Reflection Floor" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Twisted, knotty post from y = 0 to height.</summary>
    public static Mesh Post(int seed, float height, float radius)
    {
        const int sides = 10, rows = 18;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var axis = new List<Vector3>();
        float offset = seed * 0.37f;
        for (int j = 0; j <= rows; j++)
        {
            float t = (float)j / rows;
            Vector3 centre = new Vector3((Mathf.PerlinNoise(offset, t * 2f) - 0.5f) * 0.12f, t * height,
                (Mathf.PerlinNoise(t * 2f, offset) - 0.5f) * 0.12f);
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides + t * 2.5f;
                float knot = 1f + (Mathf.PerlinNoise(i * 0.7f + offset, t * 6f) - 0.5f) * 0.7f;
                float r = radius * knot * Mathf.Lerp(1.25f, 0.8f, t);
                vertices.Add(centre + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                axis.Add(centre);
            }
        }
        for (int j = 0; j < rows; j++)
        for (int i = 0; i < sides; i++)
        {
            int i1 = (i + 1) % sides;
            int a = j * sides + i, b = j * sides + i1, c = a + sides, d = b + sides;
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(b); triangles.Add(d); triangles.Add(c);
        }
        // Outward = away from the axis point of the first vertex in the triangle.
        for (int t = 0; t < triangles.Count; t += 3)
        {
            Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
            Vector3 outward = (a + b + c) / 3f - axis[triangles[t]];
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
                (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
        }
        var mesh = new Mesh { name = "Tablet Post" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Lumpy stand-in cast for a creature whose meshes could not be copied.</summary>
    public static Mesh Pebble(int seed, float size)
    {
        Mesh sphere = UnitSphere(24, 12);
        var vertices = new List<Vector3>(sphere.vertices);
        Vector3 offset = new Vector3(seed * 0.011f, seed * 0.017f, 1.7f);
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector3 v = vertices[i];
            float n = Noise3(v * 1.6f + offset);
            v *= 0.6f + n * 0.8f;
            v.y = v.y * 1.3f;
            vertices[i] = v * size * 0.5f;
        }
        Bounds bounds = new Bounds(vertices[0], Vector3.zero);
        foreach (Vector3 v in vertices) bounds.Encapsulate(v);
        for (int i = 0; i < vertices.Count; i++) vertices[i] -= new Vector3(0f, bounds.min.y, 0f);
        sphere.SetVertices(vertices);
        sphere.RecalculateNormals();
        sphere.RecalculateBounds();
        sphere.name = "Pebble Cast";
        return sphere;
    }

    // Flips any triangle whose normal points against the given outward direction.
    private static void FixWinding(List<Vector3> vertices, List<int> triangles, Func<Vector3, Vector3> outward)
    {
        for (int t = 0; t < triangles.Count; t += 3)
        {
            Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(normal, outward((a + b + c) / 3f)) < 0f)
                (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
        }
    }
}
