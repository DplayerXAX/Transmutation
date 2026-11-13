using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>A convex sphere partition. Both halves of every cut share the same cap vertices.</summary>
public static class ReflectionSphereGeometry
{
    public sealed class Chunk
    {
        public Mesh mesh;
        public Vector3 centre;
        public double volume;
    }

    private sealed class Face
    {
        public readonly List<Vector3> points;
        public readonly bool outer;
        public Face(List<Vector3> points, bool outer) { this.points = points; this.outer = outer; }
    }

    private sealed class Solid
    {
        public readonly List<Face> faces = new();
        public double volume;
        public Vector3 centre;

        public void Measure()
        {
            // Signed tetrahedra give the volume centroid even when the origin is outside a chunk.
            double total = 0, x = 0, y = 0, z = 0;
            foreach (Face face in faces)
                for (int i = 1; i + 1 < face.points.Count; i++)
                {
                    Vector3 a = face.points[0], b = face.points[i], c = face.points[i + 1];
                    double v = Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
                    Vector3 sum = (a + b + c) * 0.25f;
                    total += v; x += v * sum.x; y += v * sum.y; z += v * sum.z;
                }
            volume = Math.Abs(total);
            centre = Math.Abs(total) > 1e-12 ? new Vector3((float)(x / total), (float)(y / total), (float)(z / total)) : Vector3.zero;
        }
    }

    public static List<Chunk> Generate(int count, int seed, int subdivisions = 4)
    {
        count = Mathf.Max(1, count);
        var random = new System.Random(seed);
        var solids = new List<Solid> { Sphere(Mathf.Clamp(subdivisions, 2, 5)) };
        solids[0].Measure();
        while (solids.Count < count)
        {
            int largest = 0;
            for (int i = 1; i < solids.Count; i++)
                if (solids[i].volume > solids[largest].volume) largest = i;
            Solid source = solids[largest];
            Vector3 normal = Direction(random);
            float reach = 0f;
            foreach (Face face in source.faces)
                foreach (Vector3 p in face.points)
                    reach = Mathf.Max(reach, Mathf.Abs(Vector3.Dot(normal, p - source.centre)));
            float distance = Vector3.Dot(normal, source.centre) + Range(random, -0.12f, 0.12f) * reach;
            Split(source, normal, distance, out Solid negative, out Solid positive);
            negative.Measure(); positive.Measure();
            if (negative.volume < 1e-10 || positive.volume < 1e-10)
                throw new InvalidOperationException("Degenerate reflection sphere cut at fragment " + solids.Count);
            solids[largest] = negative;
            solids.Add(positive);
        }

        var result = new List<Chunk>(count);
        try
        {
            for (int i = 0; i < solids.Count; i++)
                result.Add(new Chunk { mesh = ToMesh(solids[i], i), centre = solids[i].centre, volume = solids[i].volume });
        }
        catch
        {
            foreach (Chunk chunk in result) Release(chunk.mesh);
            throw;
        }
        return result;
    }

    private static Solid Sphere(int subdivisions)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        Vector3[] vertices = {
            new(-1,t,0), new(1,t,0), new(-1,-t,0), new(1,-t,0),
            new(0,-1,t), new(0,1,t), new(0,-1,-t), new(0,1,-t),
            new(t,0,-1), new(t,0,1), new(-t,0,-1), new(-t,0,1)
        };
        int[] indices = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
        var solid = new Solid();
        for (int i = 0; i < indices.Length; i += 3)
            Subdivide(solid, vertices[indices[i]].normalized, vertices[indices[i + 1]].normalized,
                vertices[indices[i + 2]].normalized, subdivisions);
        return solid;
    }

    private static void Subdivide(Solid solid, Vector3 a, Vector3 b, Vector3 c, int depth)
    {
        if (depth == 0) { solid.faces.Add(new Face(new List<Vector3> { a, b, c }, true)); return; }
        Vector3 ab = (a + b).normalized, bc = (b + c).normalized, ca = (c + a).normalized;
        Subdivide(solid, a, ab, ca, depth - 1); Subdivide(solid, b, bc, ab, depth - 1);
        Subdivide(solid, c, ca, bc, depth - 1); Subdivide(solid, ab, bc, ca, depth - 1);
    }

    private static void Split(Solid source, Vector3 normal, float distance, out Solid negative, out Solid positive)
    {
        negative = new Solid(); positive = new Solid();
        var cap = new List<Vector3>();
        foreach (Face face in source.faces)
        {
            var below = new List<Vector3>(); var above = new List<Vector3>();
            for (int i = 0; i < face.points.Count; i++)
            {
                Vector3 a = face.points[i], b = face.points[(i + 1) % face.points.Count];
                float da = Vector3.Dot(normal, a) - distance, db = Vector3.Dot(normal, b) - distance;
                if (da <= 0) AddDistinct(below, a);
                if (da >= 0) AddDistinct(above, a);
                if ((da < 0 && db > 0) || (da > 0 && db < 0))
                {
                    // Exactly the same intersection is inserted in both halves.
                    Vector3 intersection = a + (b - a) * (da / (da - db));
                    AddDistinct(below, intersection); AddDistinct(above, intersection); AddDistinct(cap, intersection);
                }
                else if (da == 0) AddDistinct(cap, a);
            }
            if (below.Count >= 3) negative.faces.Add(new Face(below, face.outer));
            if (above.Count >= 3) positive.faces.Add(new Face(above, face.outer));
        }
        Vector3 centre = Vector3.zero;
        foreach (Vector3 p in cap) centre += p;
        centre /= Mathf.Max(1, cap.Count);
        Vector3 u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 v = Vector3.Cross(normal, u);
        cap.Sort((a, b) => Mathf.Atan2(Vector3.Dot(a - centre, v), Vector3.Dot(a - centre, u))
            .CompareTo(Mathf.Atan2(Vector3.Dot(b - centre, v), Vector3.Dot(b - centre, u))));
        negative.faces.Add(new Face(cap, false));
        var reverse = new List<Vector3>(cap); reverse.Reverse();
        positive.faces.Add(new Face(reverse, false));
    }

    private static void AddDistinct(List<Vector3> list, Vector3 point)
    {
        foreach (Vector3 p in list) if ((p - point).sqrMagnitude < 1e-12f) return;
        list.Add(point);
    }

    private static Mesh ToMesh(Solid solid, int index)
    {
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>();
        var outer = new List<int>(); var inside = new List<int>();
        foreach (Face face in solid.faces)
        {
            // New vertices on each face preserve sharp fracture normals. Outer normals are radial.
            Vector3 flat = Vector3.zero;
            for (int i = 1; i + 1 < face.points.Count && flat.sqrMagnitude < 1e-14f; i++)
                flat = Vector3.Cross(face.points[i] - face.points[0], face.points[i + 1] - face.points[0]);
            if (flat.sqrMagnitude < 1e-14f) continue;
            flat.Normalize();
            int start = vertices.Count;
            foreach (Vector3 p in face.points)
            {
                vertices.Add(p - solid.centre);
                normals.Add(face.outer ? p.normalized : flat);
                uv.Add(new Vector2(p.x, p.z));
            }
            var triangles = face.outer ? outer : inside;
            for (int i = 1; i + 1 < face.points.Count; i++)
            {
                triangles.Add(start); triangles.Add(start + i); triangles.Add(start + i + 1);
            }
        }
        var mesh = new Mesh { name = "Reflection sphere chunk " + index, indexFormat = IndexFormat.UInt32,
            hideFlags = HideFlags.DontSave };
        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv);
        mesh.subMeshCount = 2; mesh.SetTriangles(outer, 0); mesh.SetTriangles(inside, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    public static Vector3 Direction(System.Random random)
    {
        float z = Range(random, -1f, 1f), angle = Range(random, 0f, Mathf.PI * 2f);
        float r = Mathf.Sqrt(1f - z * z);
        return new Vector3(r * Mathf.Cos(angle), z, r * Mathf.Sin(angle));
    }

    public static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

    public static void Release(UnityEngine.Object resource)
    {
        if (resource == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(resource);
        else UnityEngine.Object.DestroyImmediate(resource);
    }
}
