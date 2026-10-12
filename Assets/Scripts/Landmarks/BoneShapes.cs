using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Mesh builders for the Bone Kiln: knobbly bone tubes, vertebrae, a skull and the kiln bowl.
/// uv0.y = a random value per part (the SpireStone shader uses it to vary its pattern).
/// </summary>
public static class BoneShapes
{
    public sealed class Builder
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<int> triangles = new List<int>();

        public int Add(Vector3 p, float part)
        {
            vertices.Add(p);
            uvs.Add(new Vector2(0f, part));
            return vertices.Count - 1;
        }

        public void Tri(int a, int b, int c)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh
            {
                name = name,
                hideFlags = HideFlags.DontSave,
                indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// A bone tube from a to b with knobbly ends (wider at both joints, slim in the middle), closed by
    /// rounded caps. squash flattens it sideways (1 = round); side is the flattened direction.
    /// </summary>
    public static void Bone(Builder b, Vector3 a, Vector3 c, float radiusA, float radiusC, float part, int sides = 10, float squash = 1f, Vector3 side = default)
    {
        Vector3 axis = c - a;
        float length = axis.magnitude;
        if (length < 1e-4f) return;
        Vector3 dir = axis / length;
        Vector3 u = side.sqrMagnitude > 1e-6f ? Vector3.ProjectOnPlane(side, dir).normalized : Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        Vector3 v = Vector3.Cross(dir, u);

        // Profile along the bone: rounded cap, swollen joint, slim shaft, swollen joint, rounded cap.
        float[] t = { -0.06f, 0f, 0.08f, 0.3f, 0.5f, 0.7f, 0.92f, 1f, 1.06f };
        float[] s = { 0.0f, 0.75f, 1.1f, 0.78f, 0.7f, 0.78f, 1.1f, 0.75f, 0.0f };
        int rings = t.Length;
        int start = b.vertices.Count;
        for (int i = 0; i < rings; i++)
        {
            float tt = Mathf.Clamp01(t[i]);
            float radius = Mathf.Lerp(radiusA, radiusC, tt) * s[i];
            Vector3 centre = a + axis * t[i];
            for (int k = 0; k < sides; k++)
            {
                float angle = k * Mathf.PI * 2f / sides;
                // A little irregular, like old bone.
                float wobble = 1f + 0.08f * Mathf.Sin(angle * 3f + part * 17f + i * 1.3f);
                Vector3 offset = (u * (Mathf.Cos(angle) * squash) + v * Mathf.Sin(angle)) * (radius * wobble);
                b.Add(centre + offset, part);
            }
        }
        for (int i = 0; i < rings - 1; i++)
        for (int k = 0; k < sides; k++)
        {
            int k1 = (k + 1) % sides;
            int a0 = start + i * sides + k, a1 = start + i * sides + k1;
            int b0 = start + (i + 1) * sides + k, b1 = start + (i + 1) * sides + k1;
            b.Tri(a0, b1, b0);
            b.Tri(a0, a1, b1);
        }
    }

    /// <summary>One vertebra: a thick body along the spine with a blade-like spine sticking up and two side wings.</summary>
    public static void Vertebra(Builder b, Vector3 centre, Vector3 tangent, Vector3 up, float radius, float part, float halfLength = -1f)
    {
        tangent = tangent.normalized;
        up = Vector3.ProjectOnPlane(up, tangent).normalized;
        Vector3 right = Vector3.Cross(up, tangent);
        float half = halfLength > 0f ? halfLength : radius * 0.75f;
        Bone(b, centre - tangent * half, centre + tangent * half, radius, radius * 0.95f, part, 12);
        // Spinous blade (flattened sideways), leaning back towards the tail.
        Bone(b, centre + up * (radius * 0.6f), centre + up * (radius * 2.1f) - tangent * (radius * 0.5f), radius * 0.45f, radius * 0.2f, part, 8, 0.35f, right);
        // Side wings.
        for (int s = -1; s <= 1; s += 2)
            Bone(b, centre + right * (s * radius * 0.6f) + up * (radius * 0.2f), centre + right * (s * radius * 1.7f) + up * (radius * 0.45f), radius * 0.35f, radius * 0.18f, part, 8, 0.5f, up);
    }

    /// <summary>A long skull: a stretched dome with a heavy brow, an eye ridge and a long snout, along +z.</summary>
    public static Mesh Skull(Vector3 radii, float part)
    {
        var b = new Builder();
        const int lat = 14, lon = 20;
        for (int i = 0; i <= lat; i++)
        {
            float v = i / (float)lat;
            float theta = v * Mathf.PI;
            for (int k = 0; k < lon; k++)
            {
                float phi = k * Mathf.PI * 2f / lon;
                float x = Mathf.Sin(theta) * Mathf.Cos(phi);
                float y = Mathf.Sin(theta) * Mathf.Sin(phi);
                float z = Mathf.Cos(theta);
                // Snout end (z > 0) narrows; the brow (top, middle) bulges.
                float narrow = Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(z));
                float brow = 1f + 0.12f * Mathf.Exp(-Mathf.Pow((z - 0.15f) * 4f, 2f)) * Mathf.Max(0f, y);
                b.Add(new Vector3(x * radii.x * narrow * brow, y * radii.y * narrow * brow, z * radii.z), part);
            }
        }
        for (int i = 0; i < lat; i++)
        for (int k = 0; k < lon; k++)
        {
            int k1 = (k + 1) % lon;
            int a0 = i * lon + k, a1 = i * lon + k1, b0 = (i + 1) * lon + k, b1 = (i + 1) * lon + k1;
            b.Tri(a0, b1, a1);
            b.Tri(a0, b0, b1);
        }
        return b.ToMesh("Bone Kiln Skull");
    }

    /// <summary>The kiln heart: a ring of fused bone round a shallow dish, lying on the ground (y = 0 is its floor).</summary>
    public static Mesh Kiln(float radius, float part)
    {
        var b = new Builder();
        const int around = 14;
        for (int i = 0; i < around; i++)
        {
            float a0 = i * Mathf.PI * 2f / around, a1 = (i + 1) * Mathf.PI * 2f / around;
            Vector3 p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius + Vector3.up * 0.35f;
            Vector3 p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius + Vector3.up * 0.35f;
            Bone(b, p0, p1, 0.42f, 0.42f, part + i * 0.01f, 8);
        }
        // Dish floor.
        int centre = b.Add(Vector3.up * 0.05f, part);
        int first = b.vertices.Count;
        for (int i = 0; i < around; i++)
        {
            float a = i * Mathf.PI * 2f / around;
            b.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius + Vector3.up * 0.15f, part);
        }
        for (int i = 0; i < around; i++) b.Tri(centre, first + (i + 1) % around, first + i);
        return b.ToMesh("Bone Kiln Heart");
    }

    /// <summary>
    /// A flat, curved band swept along points (like a real rib): an oval cross-section, wide along widthDir and
    /// thin across it, tapering from widthA to widthB, with rounded ends.
    /// </summary>
    public static Mesh Ribbon(IReadOnlyList<Vector3> points, float widthA, float widthB, float thickness, Vector3 widthDir, float part, float uvFrom = 0f, float uvTo = 1f)
    {
        var b = new Builder();
        const int sides = 10;
        int count = points.Count;
        int start = b.vertices.Count;
        for (int i = 0; i < count; i++)
        {
            Vector3 tangent = (points[Mathf.Min(count - 1, i + 1)] - points[Mathf.Max(0, i - 1)]).normalized;
            Vector3 u = Vector3.ProjectOnPlane(widthDir, tangent).normalized;
            if (u.sqrMagnitude < 0.5f) u = Vector3.Cross(tangent, Vector3.up).normalized;
            Vector3 v = Vector3.Cross(tangent, u);
            float t = i / (float)(count - 1);
            // Rounded ends: the band pinches in over the first and last few points.
            float end = Mathf.Sqrt(Mathf.Clamp01(Mathf.Min(t, 1f - t) * 12f));
            float width = Mathf.Lerp(widthA, widthB, t) * Mathf.Lerp(0.55f, 1f, end);
            float thick = thickness * Mathf.Lerp(0.6f, 1f, end) * (1f + 0.08f * Mathf.Sin(t * 23f + part * 9f));
            for (int k = 0; k < sides; k++)
            {
                float angle = k * Mathf.PI * 2f / sides;
                int index = b.Add(points[i] + u * (Mathf.Cos(angle) * width * 0.5f) + v * (Mathf.Sin(angle) * thick * 0.5f), part);
                // uv.x runs along the whole rib (for patterns that travel along it).
                b.uvs[index] = new Vector2(Mathf.Lerp(uvFrom, uvTo, t), part);
            }
        }
        for (int i = 0; i < count - 1; i++)
        for (int k = 0; k < sides; k++)
        {
            int k1 = (k + 1) % sides;
            int a0 = start + i * sides + k, a1 = start + i * sides + k1, b0 = start + (i + 1) * sides + k, b1 = start + (i + 1) * sides + k1;
            b.Tri(a0, b1, b0);
            b.Tri(a0, a1, b1);
        }
        // Caps.
        int c0 = b.Add(points[0], part), c1 = b.Add(points[count - 1], part);
        int last = start + (count - 1) * sides;
        for (int k = 0; k < sides; k++)
        {
            int k1 = (k + 1) % sides;
            b.Tri(c0, start + k1, start + k);
            b.Tri(c1, last + k, last + k1);
        }
        return b.ToMesh("Rib Band");
    }

    /// <summary>A lumpy blob of flesh, about radius across.</summary>
    public static Mesh Lump(float radius, int seed)
    {
        var b = new Builder();
        const int lat = 8, lon = 12;
        var random = new System.Random(seed);
        float o1 = (float)random.NextDouble() * 10f, o2 = (float)random.NextDouble() * 10f;
        for (int i = 0; i <= lat; i++)
        {
            float theta = i * Mathf.PI / lat;
            for (int k = 0; k < lon; k++)
            {
                float phi = k * Mathf.PI * 2f / lon;
                Vector3 n = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                float bump = 1f + 0.22f * Mathf.Sin(phi * 3f + o1) * Mathf.Sin(theta * 2f + o2) + 0.12f * Mathf.Sin(phi * 5f + theta * 3f + o2);
                b.Add(n * radius * bump, 0.5f);
            }
        }
        for (int i = 0; i < lat; i++)
        for (int k = 0; k < lon; k++)
        {
            int k1 = (k + 1) % lon;
            int a0 = i * lon + k, a1 = i * lon + k1, b0 = (i + 1) * lon + k, b1 = (i + 1) * lon + k1;
            b.Tri(a0, a1, b1);
            b.Tri(a0, b1, b0);
        }
        return b.ToMesh("Flesh Lump");
    }

    /// <summary>A single bone between a and b, as its own mesh (for rib segments).</summary>
    public static Mesh Segment(Vector3 a, Vector3 c, float radiusA, float radiusC, float part)
    {
        var b = new Builder();
        Bone(b, a, c, radiusA, radiusC, part, 10, 0.75f);
        return b.ToMesh("Rib Segment");
    }
}
