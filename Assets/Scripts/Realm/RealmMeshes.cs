using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Smooth mesh builder for the realm dressing (lamps, roots, threads, fossils, crystals, the stream).
/// Writes the vertex data the Realm shaders expect:
/// uv0.x = 0 at the base .. 1 at the tip, uv0.y = random per piece,
/// uv1.x = position around a tube (0..1) or across a ribbon (-1..1), uv1.y = metres along the piece.
/// </summary>
public sealed class RealmMeshes
{
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector3> normals = new List<Vector3>();
    private readonly List<Vector2> uv0 = new List<Vector2>();
    private readonly List<Vector2> uv1 = new List<Vector2>();
    private readonly List<int> triangles = new List<int>();

    public int VertexCount => vertices.Count;
    private static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x)
        && !float.IsNaN(p.y) && !float.IsInfinity(p.y) && !float.IsNaN(p.z) && !float.IsInfinity(p.z);

    /// <summary>A round tube along a path. t0..t1 is the uv0.x range it covers; tipLength 0 = open end.</summary>
    public void Tube(IList<Vector3> path, System.Func<float, float> radius, int sides, float t0, float t1, float piece, float tipLength = 0f)
    {
        int rings = path.Count;
        if (rings < 2) return;
        foreach (Vector3 point in path) if (!Finite(point)) return;
        var radii = new float[rings];
        for (int i = 0; i < rings; i++)
        {
            float value = radius(i / (float)(rings - 1));
            // Reject this piece before it can poison a whole merged mesh and its collider.
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            radii[i] = Mathf.Max(0.0001f, value);
        }
        int start = vertices.Count;
        Vector3 tangent = (path[1] - path[0]).normalized;
        if (tangent.sqrMagnitude < 0.0001f) tangent = Vector3.up;
        Vector3 normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        float length = 0f;
        for (int i = 0; i < rings; i++)
        {
            if (i > 0) length += Vector3.Distance(path[i - 1], path[i]);
            Vector3 nextTangent = path[Mathf.Min(i + 1, rings - 1)] - path[Mathf.Max(i - 1, 0)];
            if (nextTangent.sqrMagnitude > 0.000001f) tangent = nextTangent.normalized;
            normal -= tangent * Vector3.Dot(normal, tangent);
            if (normal.sqrMagnitude < 0.000001f)
                normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right);
            normal.Normalize();
            Vector3 binormal = Vector3.Cross(tangent, normal);
            float t = i / (float)(rings - 1);
            float r = radii[i];
            // One extra vertex closes the ring, so uv1.x runs cleanly from 0 to 1.
            for (int j = 0; j <= sides; j++)
            {
                float a = j / (float)sides * Mathf.PI * 2f;
                Vector3 dir = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                vertices.Add(path[i] + dir * r);
                normals.Add(dir);
                uv0.Add(new Vector2(Mathf.Lerp(t0, t1, t), piece));
                uv1.Add(new Vector2(j / (float)sides, length));
            }
        }
        int stride = sides + 1;
        for (int i = 0; i < rings - 1; i++)
        for (int j = 0; j < sides; j++)
        {
            int a = start + i * stride + j, b = a + 1, c = a + stride, d = c + 1;
            Add(a, b, c);
            Add(b, d, c);
        }
        if (tipLength > 0f)
        {
            Vector3 last = path[rings - 1];
            Vector3 dir = (path[rings - 1] - path[rings - 2]).normalized;
            int tip = vertices.Count;
            vertices.Add(last + dir * tipLength);
            normals.Add(dir);
            uv0.Add(new Vector2(t1, piece));
            uv1.Add(new Vector2(0.5f, length + tipLength));
            int ring = start + (rings - 1) * stride;
            for (int j = 0; j < sides; j++) Add(ring + j, ring + j + 1, tip);
        }
    }

    /// <summary>A sphere (or ellipsoid, through the scale) made of smooth rings.</summary>
    public void Sphere(Vector3 centre, Vector3 scale, Quaternion rotation, int rings, int sides, float t, float piece)
    {
        int start = vertices.Count;
        for (int i = 0; i <= rings; i++)
        {
            float polar = i / (float)rings * Mathf.PI;
            for (int j = 0; j <= sides; j++)
            {
                float azimuth = j / (float)sides * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(polar) * Mathf.Cos(azimuth), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(azimuth));
                vertices.Add(centre + rotation * Vector3.Scale(dir, scale));
                normals.Add(rotation * new Vector3(dir.x / scale.x, dir.y / scale.y, dir.z / scale.z).normalized);
                uv0.Add(new Vector2(t, piece));
                uv1.Add(new Vector2(j / (float)sides, i / (float)rings));
            }
        }
        int stride = sides + 1;
        for (int i = 0; i < rings; i++)
        for (int j = 0; j < sides; j++)
        {
            int a = start + i * stride + j, b = a + 1, c = a + stride, d = c + 1;
            Add(a, b, c);
            Add(b, d, c);
        }
    }

    /// <summary>A double pointed crystal with flat facets (each facet has its own vertices).</summary>
    public void Crystal(Vector3 centre, Quaternion rotation, float length, float radius, int sides, float piece, System.Random random)
    {
        Vector3 top = centre + rotation * Vector3.up * length * 0.5f;
        Vector3 bottom = centre - rotation * Vector3.up * length * Mathf.Lerp(0.3f, 0.5f, (float)random.NextDouble());
        var ring = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = (i + (float)random.NextDouble() * 0.3f) / sides * Mathf.PI * 2f;
            float r = radius * Mathf.Lerp(0.7f, 1.1f, (float)random.NextDouble());
            ring[i] = centre + rotation * new Vector3(Mathf.Cos(a) * r, Mathf.Lerp(-0.1f, 0.1f, (float)random.NextDouble()) * length, Mathf.Sin(a) * r);
        }
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            Facet(ring[i], ring[j], top, centre, piece);
            Facet(ring[j], ring[i], bottom, centre, piece);
        }
    }

    private void Facet(Vector3 a, Vector3 b, Vector3 c, Vector3 centre, float piece)
    {
        Vector3 n = Vector3.Cross(b - a, c - a).normalized;
        if (Vector3.Dot(n, (a + b + c) / 3f - centre) < 0f) { (a, b) = (b, a); n = -n; }
        int start = vertices.Count;
        foreach (Vector3 v in new[] { a, b, c })
        {
            vertices.Add(v);
            normals.Add(n);
            uv0.Add(new Vector2(0.5f, piece));
            uv1.Add(new Vector2(0f, 0f));
        }
        Add(start, start + 1, start + 2);
    }

    /// <summary>A flat ribbon along a path (the stream). uv1.x = -1..1 across, uv1.y = metres along.</summary>
    public void Ribbon(IList<Vector3> path, IList<Vector3> up, float halfWidth, int across, float piece)
    {
        int start = vertices.Count;
        float length = 0f;
        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0) length += Vector3.Distance(path[i - 1], path[i]);
            Vector3 forward = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
            Vector3 side = Vector3.Cross(up[i], forward).normalized;
            for (int k = 0; k <= across; k++)
            {
                float s = k / (float)across * 2f - 1f;
                vertices.Add(path[i] + side * s * halfWidth);
                normals.Add(up[i]);
                uv0.Add(new Vector2(0f, piece));
                uv1.Add(new Vector2(s, length));
            }
        }
        int stride = across + 1;
        for (int i = 0; i < path.Count - 1; i++)
        for (int k = 0; k < across; k++)
        {
            int a = start + i * stride + k, b = a + 1, c = a + stride, d = c + 1;
            // Wound so the front faces up.
            Vector3 n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (Vector3.Dot(n, up[i]) >= 0f) { Add(a, b, c); Add(b, d, c); }
            else { Add(a, c, b); Add(b, c, d); }
        }
    }

    /// <summary>
    /// A grid from rows of points laid across a path (the stream). uv1.x = -1..1 across, uv1.y = metres along.
    /// Wound to face up.
    /// </summary>
    public void Grid(List<Vector3[]> rows, float piece)
    {
        int start = vertices.Count;
        int width = rows[0].Length;
        float length = 0f;
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) length += Vector3.Distance(rows[i - 1][width / 2], rows[i][width / 2]);
            for (int k = 0; k < width; k++)
            {
                vertices.Add(rows[i][k]);
                normals.Add(Vector3.up);
                uv0.Add(new Vector2(0f, piece));
                uv1.Add(new Vector2(k / (float)(width - 1) * 2f - 1f, length));
            }
        }
        for (int i = 0; i + 1 < rows.Count; i++)
        for (int k = 0; k + 1 < width; k++)
        {
            int a = start + i * width + k, b = a + 1, c = a + width, d = c + 1;
            Vector3 n = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (n.y >= 0f) { Add(a, b, c); Add(b, d, c); }
            else { Add(a, c, b); Add(b, c, d); }
        }
    }

    /// <summary>A sheet from triangular rows of points (row i has n + 1 - i points), for films.</summary>
    public void TriangleSheet(List<List<Vector3>> rows, Vector3 normal, float piece)
    {
        var index = new List<List<int>>();
        for (int i = 0; i < rows.Count; i++)
        {
            var ids = new List<int>();
            for (int j = 0; j < rows[i].Count; j++)
            {
                ids.Add(vertices.Count);
                vertices.Add(rows[i][j]);
                normals.Add(normal);
                uv0.Add(new Vector2(0.5f, piece));
                uv1.Add(new Vector2(i / (float)Mathf.Max(1, rows.Count - 1), j / (float)Mathf.Max(1, rows.Count - 1)));
            }
            index.Add(ids);
        }
        for (int i = 0; i + 1 < index.Count; i++)
        for (int j = 0; j + 1 < index[i].Count; j++)
        {
            Add(index[i][j], index[i + 1][j], index[i][j + 1]);
            if (j + 1 < index[i + 1].Count) Add(index[i + 1][j], index[i + 1][j + 1], index[i][j + 1]);
        }
    }

    private void Add(int a, int b, int c)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
    }

    /// <summary>Adds one vertex and returns its index (for shapes built by hand).</summary>
    public int Vertex(Vector3 position, Vector3 normal, Vector2 uvA, Vector2 uvB)
    {
        vertices.Add(position);
        normals.Add(normal);
        uv0.Add(uvA);
        uv1.Add(uvB);
        return vertices.Count - 1;
    }

    /// <summary>Adds a triangle from three vertex indices.</summary>
    public void Triangle(int a, int b, int c) => Add(a, b, c);

    public Mesh Build(string name)
    {
        var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uv0);
        mesh.SetUVs(1, uv1);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
