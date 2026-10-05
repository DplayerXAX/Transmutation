using System.Collections.Generic;
using UnityEngine;

/// <summary>Raw mesh data for one decoration piece. uv.x = 0 at the base .. 1 at the tip.</summary>
public sealed class DecorMesh
{
    public readonly List<Vector3> vertices = new List<Vector3>();
    public readonly List<Vector3> normals = new List<Vector3>();
    public readonly List<float> lengths = new List<float>();
    public readonly List<int> triangles = new List<int>();
}

/// <summary>
/// Procedural shapes for the world decorations. Every shape is built from bent tubes and lumpy blobs
/// with random harmonics, so no two pieces look machine-made. Shapes stand on y = 0, growing along +y.
/// </summary>
public static class DecorShapes
{
    public enum Shape
    {
        Shards,
        Arch,
        Cairn,
        Polyps,
        Bladders,
        Creepers,
        ShellTentacle,
    }

    public static bool IsFaceted(Shape shape) => shape == Shape.Shards || shape == Shape.Arch || shape == Shape.Cairn;

    public static DecorMesh Build(Shape shape, int seed)
    {
        var random = new System.Random(seed);
        var mesh = new DecorMesh();
        switch (shape)
        {
            case Shape.Shards: Shards(mesh, random); break;
            case Shape.Arch: Arch(mesh, random); break;
            case Shape.Cairn: Cairn(mesh, random); break;
            case Shape.Polyps: Polyps(mesh, random); break;
            case Shape.Bladders: Bladders(mesh, random); break;
            case Shape.Creepers: Creepers(mesh, random); break;
            case Shape.ShellTentacle: ShellTentacle(mesh, random); break;
        }
        if (IsFaceted(shape)) Unweld(mesh);
        else SmoothNormals(mesh);
        return mesh;
    }

    // ---------------- Shapes ----------------

    /// <summary>Leaning crystal-like prisms with chiselled tops.</summary>
    private static void Shards(DecorMesh mesh, System.Random random)
    {
        int count = Range(random, 2, 6);
        for (int i = 0; i < count; i++)
        {
            float height = i == 0 ? Range(random, 2.2f, 3.4f) : Range(random, 0.7f, 2.2f);
            float radius = Range(random, 0.22f, 0.5f) * (i == 0 ? 1.2f : 1f);
            Vector3 root = i == 0 ? Vector3.zero : Flat(OnDisc(random) * 0.7f);
            Vector3 lean = (root.sqrMagnitude > 0.01f ? root.normalized : Flat(OnDisc(random))) * Range(random, 0.1f, 0.6f);
            Vector3 direction = (Vector3.up + lean).normalized;
            Vector3 kink = Flat(OnDisc(random)) * 0.12f * height;

            var path = new List<Vector3>
            {
                root - direction * 0.4f,
                root + direction * height * 0.5f + kink,
                root + direction * height,
            };
            float taper = Range(random, 0.25f, 0.6f);
            Tube(mesh, path, t => radius * Mathf.Lerp(1f, taper, t), Range(random, 4, 7), 0.22f, random,
                random.NextDouble() < 0.5 ? 0f : Range(random, 0.15f, 0.5f), 0f, 1f);
        }
    }

    /// <summary>A bent, sometimes broken loop half sunk into the ground.</summary>
    private static void Arch(DecorMesh mesh, System.Random random)
    {
        float span = Range(random, 2.4f, 4.4f);
        float height = span * Range(random, 0.5f, 1.05f);
        float end = random.NextDouble() < 0.4 ? Range(random, 0.6f, 0.85f) : 1f;
        float skew = Range(random, -0.35f, 0.35f);
        float phase = Range(random, 0f, 6.28f);

        var path = new List<Vector3>();
        const int points = 16;
        for (int i = 0; i <= points; i++)
        {
            float t = i / (float)points * end;
            float angle = Mathf.PI * t;
            float x = -Mathf.Cos(angle) * span * 0.5f;
            float y = Mathf.Sin(angle) * height - 0.35f;
            float z = Mathf.Sin(angle * 2f + phase) * 0.35f + skew * x;
            y += Mathf.Sin(t * 9f + phase) * 0.08f * height;
            path.Add(new Vector3(x, y, z));
        }
        float thickness = Range(random, 0.16f, 0.32f);
        Tube(mesh, path, t => thickness * (1.35f - 0.5f * Mathf.Sin(t / end * Mathf.PI)) * (1f + 0.2f * Mathf.Sin(t * 13f + phase)),
            Range(random, 5, 8), 0.2f, random, 0f, 0f, 1f);
    }

    /// <summary>A loose stack of flattened stones, never quite balanced.</summary>
    private static void Cairn(DecorMesh mesh, System.Random random)
    {
        int count = Range(random, 2, 6);
        float y = -0.1f;
        Vector3 offset = Vector3.zero;
        float width = Range(random, 0.55f, 0.9f);
        float totalHeight = 0f;
        var stones = new List<(Vector3 centre, Vector3 radii, Quaternion rotation)>();
        for (int i = 0; i < count; i++)
        {
            var radii = new Vector3(width * Range(random, 0.75f, 1.15f), width * Range(random, 0.25f, 0.5f), width * Range(random, 0.6f, 1f));
            y += radii.y;
            stones.Add((new Vector3(offset.x, y, offset.z), radii, Quaternion.Euler(Range(random, -12f, 12f), Range(random, 0f, 360f), Range(random, -12f, 12f))));
            y += radii.y * 0.85f;
            offset += Flat(OnDisc(random)) * width * 0.3f;
            width *= Range(random, 0.65f, 0.95f);
        }
        totalHeight = Mathf.Max(y, 0.1f);
        foreach (var stone in stones)
            Blob(mesh, stone.centre, stone.radii, stone.rotation, 5, 7, 0.2f, random,
                (stone.centre.y - stone.radii.y) / totalHeight, (stone.centre.y + stone.radii.y) / totalHeight);
    }

    /// <summary>A clump of soft stalks, each ending in a swollen bulb.</summary>
    private static void Polyps(DecorMesh mesh, System.Random random)
    {
        int count = Range(random, 1, 7);
        for (int i = 0; i < count; i++)
        {
            float height = Range(random, 0.7f, 2.4f);
            Vector3 root = Flat(OnDisc(random)) * 0.35f;
            Vector3 direction = (Vector3.up + Flat(OnDisc(random)) * 0.5f).normalized;
            Vector3 bend = Flat(OnDisc(random)).normalized;
            float curl = Range(random, -0.25f, 0.35f);

            var path = new List<Vector3> { root - direction * 0.15f };
            const int steps = 10;
            Vector3 point = root;
            for (int s = 1; s <= steps; s++)
            {
                path.Add(point);
                direction = (direction + bend * curl / steps + Flat(OnDisc(random)) * 0.12f).normalized;
                point += direction * height / steps;
            }
            path.Add(point);

            float stalk = Range(random, 0.05f, 0.1f);
            float neck = Range(random, 0.55f, 0.8f);
            Tube(mesh, path, t => stalk * (1.5f - 0.8f * Mathf.Sqrt(t)) * (t > neck ? 0.75f : 1f), 8, 0.12f, random, 0f, 0f, 0.9f);

            float bulb = stalk * Range(random, 2.2f, 3.6f);
            Quaternion along = Quaternion.FromToRotation(Vector3.up, direction);
            Blob(mesh, point + direction * bulb * 0.55f, new Vector3(bulb, bulb * Range(random, 1f, 1.6f), bulb), along, 7, 9, 0.18f, random, 0.85f, 1f);
        }
    }

    /// <summary>Half-buried swollen sacs huddled together.</summary>
    private static void Bladders(DecorMesh mesh, System.Random random)
    {
        int count = Range(random, 3, 9);
        for (int i = 0; i < count; i++)
        {
            float radius = Range(random, 0.18f, 0.55f) * (i == 0 ? 1.3f : 1f);
            Vector3 centre = Flat(OnDisc(random)) * 0.7f + Vector3.up * radius * Range(random, 0.2f, 0.7f);
            var radii = new Vector3(radius, radius * Range(random, 0.8f, 1.4f), radius * Range(random, 0.8f, 1.1f));
            Blob(mesh, centre, radii, Quaternion.Euler(Range(random, -20f, 20f), Range(random, 0f, 360f), 0f), 8, 10, 0.16f, random, 0f, 0.45f);
        }
    }

    /// <summary>Root-like tubes crawling over the ground, a few tips lifting up.</summary>
    private static void Creepers(DecorMesh mesh, System.Random random)
    {
        int count = Range(random, 3, 7);
        for (int i = 0; i < count; i++)
        {
            float length = Range(random, 1.4f, 3.4f);
            float angle = Range(random, 0f, Mathf.PI * 2f);
            float turn = Range(random, -0.6f, 0.6f);
            bool lifts = random.NextDouble() < 0.35;
            float phase = Range(random, 0f, 6.28f);

            var path = new List<Vector3>();
            Vector3 point = Vector3.down * 0.05f;
            const int steps = 9;
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                path.Add(point + Vector3.up * (0.04f * Mathf.Sin(t * 7f + phase) + (lifts ? t * t * t * 0.9f : 0f)));
                angle += turn / steps + Range(random, -0.25f, 0.25f);
                point += new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * length / steps;
            }
            float thickness = Range(random, 0.06f, 0.13f);
            Tube(mesh, path, t => thickness * (1f - 0.75f * t), 6, 0.2f, random, thickness, 0f, lifts ? 0.7f : 0.25f);
        }
    }

    /// <summary>
    /// A slender horn of stacked hard plates that curls more towards its tip.
    /// Unit length; the plate seams sit at uv.x = k / 9 so the shader rings line up with them.
    /// </summary>
    private static void ShellTentacle(DecorMesh mesh, System.Random random)
    {
        const int plates = 9;
        float[] platePoints = { 0f, 0.45f, 0.93f };
        Vector3 axis = Flat(OnDisc(random)).normalized;
        if (axis.sqrMagnitude < 0.01f) axis = Vector3.right;
        float curl = Range(random, 2.5f, 5.5f);
        float radius = Range(random, 0.07f, 0.1f);

        var path = new List<Vector3>();
        var ts = new List<float>();
        Vector3 direction = (Vector3.up + Flat(OnDisc(random)) * 0.15f).normalized;
        Vector3 point = Vector3.down * 0.05f;
        float previous = 0f;
        for (int p = 0; p < plates; p++)
        for (int k = 0; k < platePoints.Length; k++)
        {
            float t = (p + platePoints[k]) / plates;
            float step = t - previous;
            previous = t;
            direction = Quaternion.AngleAxis(curl * Mathf.Pow(t, 1.5f) * step * Mathf.Rad2Deg, axis) * direction;
            point += direction * step;
            path.Add(point);
            ts.Add(t);
        }
        path.Add(point + direction * (1f - previous));
        ts.Add(1f);

        Tube(mesh, path, t =>
        {
            float local = t * plates - Mathf.Floor(t * plates);
            // Each plate flares slightly towards its upper rim, then the next one starts narrower.
            return radius * (1f - 0.88f * t) * (0.86f + 0.22f * local * local);
        }, 8, 0.04f, random, radius * 0.6f, 0f, 1f, ts);
    }

    // ---------------- Builders ----------------

    /// <summary>A tube along a path with an irregular cross-section. tipLength 0 = flat cap.</summary>
    private static void Tube(DecorMesh mesh, IList<Vector3> path, System.Func<float, float> radius, int sides, float noise,
        System.Random random, float tipLength, float t0, float t1, IList<float> pathT = null)
    {
        int rings = path.Count;
        int start = mesh.vertices.Count;

        var harmonics = new (int k, float amplitude, float phase, float twist)[3];
        for (int h = 0; h < harmonics.Length; h++)
            harmonics[h] = (Range(random, 2, 6), noise * Range(random, 0.3f, 1f), Range(random, 0f, 6.28f), Range(random, -4f, 4f));

        Vector3 tangent = (path[1] - path[0]).normalized;
        Vector3 normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        for (int i = 0; i < rings; i++)
        {
            Vector3 a = path[Mathf.Max(i - 1, 0)], b = path[Mathf.Min(i + 1, rings - 1)];
            tangent = (b - a).normalized;
            normal = (normal - tangent * Vector3.Dot(normal, tangent)).normalized;
            Vector3 binormal = Vector3.Cross(tangent, normal);

            float t = pathT != null ? pathT[i] : i / (float)(rings - 1);
            float r = radius(t);
            for (int j = 0; j < sides; j++)
            {
                float theta = j / (float)sides * Mathf.PI * 2f;
                float bump = 1f;
                foreach (var h in harmonics) bump += h.amplitude * Mathf.Sin(h.k * theta + h.phase + t * h.twist);
                Vector3 offset = (normal * Mathf.Cos(theta) + binormal * Mathf.Sin(theta)) * r * bump;
                mesh.vertices.Add(path[i] + offset);
                mesh.lengths.Add(Mathf.Lerp(t0, t1, t));
            }
        }

        for (int i = 0; i < rings - 1; i++)
        for (int j = 0; j < sides; j++)
        {
            int a = start + i * sides + j;
            int b = start + i * sides + (j + 1) % sides;
            int c = a + sides;
            int d = b + sides;
            AddTriangle(mesh, a, b, c);
            AddTriangle(mesh, b, d, c);
        }

        // Close the tip with a point or a flat cap.
        Vector3 last = path[rings - 1];
        Vector3 lastTangent = (path[rings - 1] - path[rings - 2]).normalized;
        int tip = mesh.vertices.Count;
        mesh.vertices.Add(last + lastTangent * tipLength);
        mesh.lengths.Add(t1);
        int ring = start + (rings - 1) * sides;
        for (int j = 0; j < sides; j++)
            AddTriangle(mesh, ring + j, ring + (j + 1) % sides, tip);
    }

    /// <summary>A lumpy ellipsoid. uv.x runs from tBottom to tTop over its height.</summary>
    private static void Blob(DecorMesh mesh, Vector3 centre, Vector3 radii, Quaternion rotation, int rings, int sides, float noise,
        System.Random random, float tBottom, float tTop)
    {
        int start = mesh.vertices.Count;
        var lumps = new (Vector3 direction, float frequency, float amplitude, float phase)[3];
        for (int h = 0; h < lumps.Length; h++)
            lumps[h] = (OnSphere(random), Range(random, 1.5f, 4f), noise * Range(random, 0.4f, 1f), Range(random, 0f, 6.28f));

        for (int i = 0; i <= rings; i++)
        {
            float v = i / (float)rings;
            float polar = v * Mathf.PI;
            int count = i == 0 || i == rings ? 1 : sides;
            for (int j = 0; j < count; j++)
            {
                float azimuth = j / (float)sides * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Sin(polar) * Mathf.Cos(azimuth), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(azimuth));
                float bump = 1f;
                foreach (var l in lumps) bump += l.amplitude * Mathf.Sin(Vector3.Dot(direction, l.direction) * l.frequency + l.phase);
                Vector3 local = Vector3.Scale(direction, radii) * bump;
                mesh.vertices.Add(centre + rotation * local);
                mesh.lengths.Add(Mathf.Lerp(tTop, tBottom, v));
            }
        }

        int top = start;
        int bottom = mesh.vertices.Count - 1;
        int RingVertex(int i, int j) => start + 1 + (i - 1) * sides + (j % sides);
        for (int j = 0; j < sides; j++)
        {
            AddOutward(mesh, top, RingVertex(1, j), RingVertex(1, j + 1), centre);
            AddOutward(mesh, bottom, RingVertex(rings - 1, j + 1), RingVertex(rings - 1, j), centre);
        }
        for (int i = 1; i < rings - 1; i++)
        for (int j = 0; j < sides; j++)
        {
            int a = RingVertex(i, j), b = RingVertex(i, j + 1), c = RingVertex(i + 1, j), d = RingVertex(i + 1, j + 1);
            AddOutward(mesh, a, b, c, centre);
            AddOutward(mesh, b, d, c, centre);
        }
    }

    private static void AddTriangle(DecorMesh mesh, int a, int b, int c)
    {
        mesh.triangles.Add(a);
        mesh.triangles.Add(b);
        mesh.triangles.Add(c);
    }

    /// <summary>Adds a triangle wound so it faces away from the centre.</summary>
    private static void AddOutward(DecorMesh mesh, int a, int b, int c, Vector3 centre)
    {
        Vector3 pa = mesh.vertices[a], pb = mesh.vertices[b], pc = mesh.vertices[c];
        Vector3 faceNormal = Vector3.Cross(pb - pa, pc - pa);
        if (Vector3.Dot(faceNormal, (pa + pb + pc) / 3f - centre) < 0f) AddTriangle(mesh, a, c, b);
        else AddTriangle(mesh, a, b, c);
    }

    private static void SmoothNormals(DecorMesh mesh)
    {
        var sums = new Vector3[mesh.vertices.Count];
        for (int i = 0; i < mesh.triangles.Count; i += 3)
        {
            int a = mesh.triangles[i], b = mesh.triangles[i + 1], c = mesh.triangles[i + 2];
            Vector3 n = Vector3.Cross(mesh.vertices[b] - mesh.vertices[a], mesh.vertices[c] - mesh.vertices[a]);
            sums[a] += n;
            sums[b] += n;
            sums[c] += n;
        }
        mesh.normals.Clear();
        foreach (Vector3 n in sums) mesh.normals.Add(n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up);
    }

    /// <summary>Splits every triangle into its own vertices for a hard, faceted look.</summary>
    private static void Unweld(DecorMesh mesh)
    {
        var vertices = new List<Vector3>(mesh.triangles.Count);
        var lengths = new List<float>(mesh.triangles.Count);
        var normals = new List<Vector3>(mesh.triangles.Count);
        for (int i = 0; i < mesh.triangles.Count; i += 3)
        {
            int a = mesh.triangles[i], b = mesh.triangles[i + 1], c = mesh.triangles[i + 2];
            Vector3 n = Vector3.Cross(mesh.vertices[b] - mesh.vertices[a], mesh.vertices[c] - mesh.vertices[a]).normalized;
            foreach (int index in new[] { a, b, c })
            {
                vertices.Add(mesh.vertices[index]);
                lengths.Add(mesh.lengths[index]);
                normals.Add(n);
            }
        }
        mesh.vertices.Clear();
        mesh.vertices.AddRange(vertices);
        mesh.lengths.Clear();
        mesh.lengths.AddRange(lengths);
        mesh.normals.Clear();
        mesh.normals.AddRange(normals);
        mesh.triangles.Clear();
        for (int i = 0; i < vertices.Count; i++) mesh.triangles.Add(i);
    }

    // ---------------- Random helpers ----------------

    private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
    private static int Range(System.Random random, int min, int max) => random.Next(min, max);

    private static Vector2 OnDisc(System.Random random)
    {
        float angle = Range(random, 0f, Mathf.PI * 2f);
        float r = Mathf.Sqrt((float)random.NextDouble());
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
    }

    private static Vector3 OnSphere(System.Random random)
    {
        float z = Range(random, -1f, 1f);
        float angle = Range(random, 0f, Mathf.PI * 2f);
        float r = Mathf.Sqrt(1f - z * z);
        return new Vector3(r * Mathf.Cos(angle), r * Mathf.Sin(angle), z);
    }

    private static Vector3 Flat(Vector2 v) => new Vector3(v.x, 0f, v.y);
}
