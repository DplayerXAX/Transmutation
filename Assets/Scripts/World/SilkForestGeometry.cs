using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Real perforated shells and bundled threads; no opaque sphere behind the holes.</summary>
public static class SilkForestGeometry
{
    public sealed class MeshBuffer
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<int> indices = new List<int>();
        private readonly List<Color> colours = new List<Color>();
        private readonly List<Vector2> uv = new List<Vector2>();
        private readonly Dictionary<int, Vector3> surfaceNormals = new Dictionary<int, Vector3>();
        public int Vertex(Vector3 p, Color colour, Vector2 tex)
        {
            int i = vertices.Count; vertices.Add(p); colours.Add(colour); uv.Add(tex); return i;
        }
        public void Triangle(int a, int b, int c) { indices.Add(a); indices.Add(b); indices.Add(c); }
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour, Vector3? sphericalCentre = null)
        {
            int k = Vertex(a, colour, Vector2.zero);
            Vertex(b, colour, Vector2.right); Vertex(c, colour, Vector2.one); Vertex(d, colour, Vector2.up);
            Triangle(k, k + 1, k + 2); Triangle(k, k + 2, k + 3);
            if (sphericalCentre.HasValue)
                for (int j = k; j < k + 4; j++) surfaceNormals[j] = (vertices[j] - sphericalCentre.Value).normalized;
        }
        public void Tube(Vector3[] path, float radius, int sides, Color colour, bool taper = false)
        {
            if (path.Length < 2 || radius <= 0) return;
            int start = vertices.Count;
            Vector3 previousNormal = Vector3.zero;
            for (int p = 0; p < path.Length; p++)
            {
                Vector3 tangent = (path[Mathf.Min(p + 1, path.Length - 1)] - path[Mathf.Max(0, p - 1)]).normalized;
                if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.up;
                Vector3 normal = p == 0 ? Vector3.Cross(tangent, Mathf.Abs(tangent.y) > 0.9f ? Vector3.right : Vector3.up).normalized : Vector3.ProjectOnPlane(previousNormal, tangent).normalized;
                if (normal.sqrMagnitude < 0.001f) normal = Vector3.Cross(tangent, Vector3.forward).normalized;
                Vector3 binormal = Vector3.Cross(tangent, normal).normalized;
                previousNormal = normal;
                float thickness = radius * (taper ? Mathf.Lerp(1, 0.08f, p / (float)(path.Length - 1)) : 1);
                for (int s = 0; s < sides; s++)
                {
                    float a = s * 2 * Mathf.PI / sides;
                    Vertex(path[p] + (normal * Mathf.Cos(a) + binormal * Mathf.Sin(a)) * thickness, colour, new Vector2(s / (float)sides, p / (float)(path.Length - 1)));
                    if (p == 0) continue;
                    int v = start + p * sides + s, next = start + p * sides + (s + 1) % sides;
                    Triangle(v - sides, next - sides, next); Triangle(v - sides, next, v);
                }
            }
        }
        public Mesh CreateMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetColors(colours); mesh.SetUVs(0, uv); mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            if (surfaceNormals.Count > 0)
            {
                var normals = mesh.normals;
                foreach (var pair in surfaceNormals) normals[pair.Key] = pair.Value;
                mesh.normals = normals;
            }
            mesh.RecalculateBounds(); return mesh;
        }
    }

    private struct Face
    {
        public int a, b, c;
        public Face(int x, int y, int z) { a = x; b = y; c = z; }
        public bool Contains(int v) => a == v || b == v || c == v;
    }
    private static float R(System.Random random, float lo, float hi) => Mathf.Lerp(lo, hi, (float)random.NextDouble());

    public static void Crown(MeshBuffer silk, MeshBuffer film, Vector3 centre, float radius, float asymmetry,
        float webThickness, float tornAmount, float coverage, System.Random random)
    {
        // Dual of a jittered icosphere: each vertex becomes an irregular five/six-sided
        // pore. Shared outer edges make a continuous web, with rounded inner rims.
        float phi = (1 + Mathf.Sqrt(5)) / 2;
        var points = new List<Vector3> {
            new Vector3(-1,phi,0),new Vector3(1,phi,0),new Vector3(-1,-phi,0),new Vector3(1,-phi,0),
            new Vector3(0,-1,phi),new Vector3(0,1,phi),new Vector3(0,-1,-phi),new Vector3(0,1,-phi),
            new Vector3(phi,0,-1),new Vector3(phi,0,1),new Vector3(-phi,0,-1),new Vector3(-phi,0,1) };
        int[] raw = {0,11,5,0,5,1,0,1,7,0,7,10,0,10,11,1,5,9,5,11,4,11,10,2,10,7,6,7,1,8,3,9,4,3,4,2,3,2,6,3,6,8,3,8,9,4,9,5,2,4,11,6,2,10,8,6,7,9,8,1};
        var faces = new List<Face>();
        for (int i = 0; i < raw.Length; i += 3) faces.Add(new Face(raw[i], raw[i + 1], raw[i + 2]));
        for (int i = 0; i < points.Count; i++) points[i] = points[i].normalized;
        var mids = new Dictionary<long, int>();
        int Mid(int a, int b)
        {
            long key = ((long)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
            if (mids.TryGetValue(key, out int found)) return found;
            int index = points.Count; points.Add((points[a] + points[b]).normalized); mids.Add(key, index); return index;
        }
        var subdivided = new List<Face>();
        foreach (Face f in faces)
        {
            int ab = Mid(f.a, f.b), bc = Mid(f.b, f.c), ca = Mid(f.c, f.a);
            subdivided.Add(new Face(f.a, ab, ca)); subdivided.Add(new Face(f.b, bc, ab));
            subdivided.Add(new Face(f.c, ca, bc)); subdivided.Add(new Face(ab, bc, ca));
        }
        faces = subdivided;
        Quaternion rotation = Quaternion.Euler(R(random, 0, 360), R(random, 0, 360), R(random, 0, 360));
        for (int i = 0; i < points.Count; i++)
            points[i] = rotation * (points[i] + new Vector3(R(random,-0.1f,0.1f),R(random,-0.1f,0.1f),R(random,-0.1f,0.1f))).normalized;
        var corners = new List<Vector3>();
        foreach (var f in faces) corners.Add((points[f.a] + points[f.b] + points[f.c]).normalized);
        Vector3 stretch = new Vector3(R(random, 0.86f, 1.12f), R(random, 0.86f, 1.18f), R(random, 0.86f, 1.12f));
        float phase = R(random, 0, 20);
        Vector3 Surface(Vector3 direction)
        {
            Vector3 n = direction.normalized;
            float bulge = 1 + asymmetry * (Mathf.Sin(n.x * 3 + phase) * Mathf.Sin(n.y * 4 - phase) * 0.38f + n.z * 0.3f);
            return centre + Vector3.Scale(n, stretch) * (radius * bulge);
        }
        for (int v = 0; v < points.Count; v++)
        {
            Vector3 axis = points[v];
            Vector3 tangent = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 bitangent = Vector3.Cross(axis, tangent);
            var neighbours = new List<Vector3>();
            for (int f = 0; f < faces.Count; f++) if (faces[f].Contains(v)) neighbours.Add(corners[f]);
            neighbours.Sort((a, b) => Mathf.Atan2(Vector3.Dot(a, bitangent), Vector3.Dot(a, tangent)).CompareTo(Mathf.Atan2(Vector3.Dot(b, bitangent), Vector3.Dot(b, tangent))));
            float opening = Mathf.Clamp(1 - webThickness * R(random, 0.65f, 2.5f), 0.3f, 0.95f);
            if (R(random, 0, 1) < 0.18f) opening *= R(random, 0.4f, 0.75f);
            int segments = neighbours.Count * 6;
            var rim = new Vector3[segments + 1];
            var outer = new Vector3[segments + 1];
            Color tint = Color.Lerp(new Color(0.65f, 0.83f, 0.83f), new Color(1, 0.9f, 0.83f), R(random, 0, 1));
            for (int k = 0; k <= segments; k++)
            {
                int edge = (k / 6) % neighbours.Count;
                float t = (k % 6) / 6f;
                Vector3 p0 = neighbours[(edge + neighbours.Count - 1) % neighbours.Count];
                Vector3 p1 = neighbours[edge];
                Vector3 p2 = neighbours[(edge + 1) % neighbours.Count];
                Vector3 p3 = neighbours[(edge + 2) % neighbours.Count];
                Vector3 rounded = 0.5f * ((2 * p1) + (-p0 + p2) * t + (2*p0 - 5*p1 + 4*p2 - p3)*t*t + (-p0 + 3*p1 - 3*p2 + p3)*t*t*t);
                rim[k] = Surface(Vector3.Lerp(axis, rounded.normalized, opening));
                outer[k] = Surface(Vector3.Lerp(p1, p2, t));
            }
            for (int k = 1; k <= segments; k++) silk.Quad(outer[k - 1], outer[k], rim[k], rim[k - 1], tint, centre);
            silk.Tube(rim, radius * R(random, 0.009f, 0.017f), 5, tint);
            if (R(random, 0, 1) < coverage)
            {
                Color filmColour = new Color(1, 1, 1, R(random, 0.35f, 0.85f));
                int middle = film.Vertex(Surface(axis) - axis * radius * 0.045f, filmColour, new Vector2(0.5f, 0.5f));
                for (int k = 0; k < segments; k++)
                {
                    int a = film.Vertex(rim[k], filmColour, new Vector2(k / (float)segments, 0));
                    int b = film.Vertex(rim[k + 1], filmColour, new Vector2((k + 1) / (float)segments, 1));
                    film.Triangle(middle, a, b);
                }
            }
            else if (R(random, 0, 1) < tornAmount * 0.6f)
            {
                int at = random.Next(segments), to = (at + segments / 2 + 2) % segments;
                Vector3 middle = (rim[at] + rim[to]) * 0.5f - axis * radius * 0.035f;
                silk.Tube(new[] { rim[at], middle, rim[to] }, radius * 0.004f, 4, tint);
                // A torn pennant remains attached to one side of the pore.
                if (R(random, 0, 1) < 0.5f)
                    film.Quad(rim[at], rim[(at + 3) % segments], Vector3.Lerp(rim[(at + 3) % segments], middle, 0.4f), middle, new Color(1,1,1,0.5f));
            }
            // Sparse radial slime points: a soft, radiolarian silhouette, not a regular ball.
            if (v % 4 == 0)
            {
                Vector3 root = Surface(axis), tip = root + axis * radius * R(random, 0.15f, 0.35f);
                silk.Tube(new[] { root, Vector3.Lerp(root, tip, 0.65f), tip }, radius * 0.035f, 6, tint, true);
            }
        }
    }
}
