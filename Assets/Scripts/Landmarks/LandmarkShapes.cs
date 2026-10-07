using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Settings and radius functions of one hollow spire. Noise is sampled on cos/sin of the angle,
/// so every function wraps around the tower without a seam.
/// y = 0 is the ground line; the spire grows up to height (plus crown teeth).
/// </summary>
public sealed class SpireShape
{
    public float height = 70f;
    public float bury = 8f;
    public float innerRadius = 8f;
    // Inner radius at the top, relative to the base.
    public float topScale = 0.8f;
    public float wallBase = 2.4f;
    public float wallTop = 1.3f;
    public float flare = 5f;
    public float lean = 3f;
    public float crown = 6f;
    public float doorAngle;
    // Half width of the door in radians. 0 = no door.
    public float doorHalfAngle = 0.2f;
    public float doorHeight = 5f;
    // Share of the upper wall cut away as slits (0..1).
    public float windows = 0.22f;
    public float noiseSeed = 13.7f;
    public int segments = 36;
    public float ringStep = 1.4f;

    /// <summary>Offset of the spire's axis at a height (it leans and wanders as it rises).</summary>
    public Vector3 Axis(float y)
    {
        float t = Mathf.Clamp01(y / height);
        return new Vector3(Mathf.Sin(t * 2.2f + noiseSeed) * lean * t, y, Mathf.Cos(t * 1.7f + noiseSeed * 1.3f) * lean * t * 0.7f);
    }

    public float Inner(float angle, float y)
    {
        float t = Mathf.Clamp01(y / height);
        float wobble = (Noise(angle, 1.6f, y * 0.05f, 3.1f) - 0.5f) * 1.2f;
        return Mathf.Max(1f, Mathf.Lerp(innerRadius, innerRadius * topScale, t) + wobble);
    }

    public float Outer(float angle, float y)
    {
        float t = Mathf.Clamp01(y / height);
        float radius = Mathf.Lerp(innerRadius + wallBase, innerRadius * topScale + wallTop, t);
        // Big slow bulges, horizontal courses (kept gentle so the wall stays climbable) and a few ribs.
        radius += (Noise(angle, 2.3f, y * 0.02f, 0f) - 0.5f) * 2.2f;
        radius += Mathf.Sin(y * (2f * Mathf.PI / 4.2f) + Noise(angle, 1.1f, 0f, 7.7f) * 3f) * 0.22f;
        radius += Mathf.Pow(Noise(angle, 4.1f, y * 0.012f, 21.3f), 3f) * 2f;
        radius += flare * Mathf.Pow(1f - t, 6f) * (0.6f + 0.8f * Noise(angle, 1.5f, 0f, 41.9f));
        return Mathf.Max(radius, Inner(angle, y) + 0.8f);
    }

    /// <summary>Extra height of the jagged crown at an angle.</summary>
    public float Crown(float angle)
    {
        // Stepped teeth with flat tops, so the rim between them can be stood on after climbing.
        float teeth = Mathf.Clamp01(Noise(angle, 3.4f, 0f, 63.1f) * 1.6f - 0.45f);
        return Mathf.Floor(teeth * 3f) / 3f * crown;
    }

    private float Noise(float angle, float scale, float v, float offset)
    {
        return Mathf.PerlinNoise(Mathf.Cos(angle) * scale + noiseSeed + offset + 50f, Mathf.Sin(angle) * scale + v + offset * 0.7f + 50f);
    }
}

/// <summary>
/// Mesh builders for the landmarks: hollow spires with ledges, beams, halos, glyph strokes, seeds.
/// Spire meshes use uv0.x = height 0..1 and uv0.y = a random value per part (for the WorldDecor shader).
/// </summary>
public static class LandmarkShapes
{
    private sealed class Builder
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<int> triangles = new List<int>();
        public float height = 1f;

        public int Add(Vector3 p, float part)
        {
            vertices.Add(p);
            uvs.Add(new Vector2(Mathf.Clamp01(p.y / height), part));
            return vertices.Count - 1;
        }

        /// <summary>Two triangles a-b-c-d, flipped if needed so the face points along facing.</summary>
        public void Quad(int a, int b, int c, int d, Vector3 facing)
        {
            Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (Vector3.Dot(normal, facing) < 0f)
            {
                (b, d) = (d, b);
            }
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(a); triangles.Add(c); triangles.Add(d);
        }

        /// <summary>A quad with its own vertices, so it gets a crisp normal.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, float part)
        {
            Quad(Add(a, part), Add(b, part), Add(c, part), Add(d, part), facing);
        }

        public Mesh ToMesh(string name, bool flipY)
        {
            if (flipY)
            {
                for (int i = 0; i < vertices.Count; i++)
                    vertices[i] = new Vector3(vertices[i].x, -vertices[i].y, vertices[i].z);
                for (int i = 0; i < triangles.Count; i += 3)
                    (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
            }
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

    // ---------------- Spire ----------------

    /// <summary>
    /// Hollow spire wall with a door, slit windows, crown teeth, ledges spiralling up the outside
    /// and an optional beam across the crown. flipY hangs it upside down (for the inner world).
    /// </summary>
    public static Mesh Spire(SpireShape shape, System.Random random, int ledgeCount, bool beam, bool flipY, out Vector3 beamCentre)
    {
        var b = new Builder { height = shape.height };
        int segments = shape.segments;
        int rings = Mathf.Max(2, Mathf.CeilToInt((shape.height + shape.bury) / shape.ringStep));
        float wallPart = (float)random.NextDouble();

        // Ring heights per segment; the top ring is flat, the crown teeth are added as blocks below.
        var ringY = new float[rings + 1, segments];
        var angles = new float[segments];
        for (int k = 0; k < segments; k++)
        {
            angles[k] = k * Mathf.PI * 2f / segments;
            for (int j = 0; j <= rings; j++)
                ringY[j, k] = -shape.bury + j * (shape.height + shape.bury) / rings;
        }

        var outer = new int[rings + 1, segments];
        var inner = new int[rings + 1, segments];
        for (int j = 0; j <= rings; j++)
        for (int k = 0; k < segments; k++)
        {
            outer[j, k] = b.Add(Point(shape, angles[k], ringY[j, k], shape.Outer(angles[k], ringY[j, k])), wallPart);
            inner[j, k] = b.Add(Point(shape, angles[k], ringY[j, k], shape.Inner(angles[k], ringY[j, k])), wallPart);
        }

        // The door is cut through the wall.
        bool Skip(int j, int k)
        {
            if (j < 0 || j >= rings) return false;
            k = (k + segments) % segments;
            float mid = (k + 0.5f) * Mathf.PI * 2f / segments;
            return shape.doorHalfAngle > 0f && Mathf.Abs(Mathf.DeltaAngle(mid * Mathf.Rad2Deg, shape.doorAngle * Mathf.Rad2Deg)) < shape.doorHalfAngle * Mathf.Rad2Deg
                && ringY[j + 1, k] <= shape.doorHeight + 0.01f;
        }

        // Windows are tall narrow niches set into the wall, not holes, so climbing hands still find their back.
        bool Window(int j, int k)
        {
            if (j < 0 || j >= rings || shape.windows <= 0f) return false;
            k = (k + segments) % segments;
            if (ringY[j, k] < 12f || ringY[j + 1, k] > shape.height - 5f || Skip(j, k)) return false;
            // Slow noise along the height, fast around the tower.
            float slit = Mathf.PerlinNoise(k * 0.93f + shape.noiseSeed * 3.1f, j * 0.22f + shape.noiseSeed);
            return slit > 1f - shape.windows * 0.9f;
        }

        Vector3 Recess(int j, int k)
        {
            float angle = angles[k], y = ringY[j, k];
            float outerRadius = shape.Outer(angle, y);
            float depth = Mathf.Min(0.6f, outerRadius - shape.Inner(angle, y) - 0.3f);
            return Point(shape, angle, y, outerRadius - Mathf.Max(0.1f, depth));
        }

        for (int j = 0; j < rings; j++)
        for (int k = 0; k < segments; k++)
        {
            if (Skip(j, k)) continue;
            int k1 = (k + 1) % segments;
            float mid = (k + 0.5f) * Mathf.PI * 2f / segments;
            Vector3 radial = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
            Vector3 tangent = new Vector3(-Mathf.Sin(mid), 0f, Mathf.Cos(mid));

            Vector3 P(int[,] grid, int jj, int kk) => b.vertices[grid[jj, kk]];
            if (Window(j, k))
            {
                // Back of the niche, then its sides, sill and lintel wherever the neighbour is solid wall.
                b.Quad(Recess(j, k), Recess(j, k1), Recess(j + 1, k1), Recess(j + 1, k), radial, wallPart);
                if (!Window(j, k - 1))
                    b.Quad(P(outer, j, k), P(outer, j + 1, k), Recess(j + 1, k), Recess(j, k), tangent, wallPart);
                if (!Window(j, k + 1))
                    b.Quad(P(outer, j, k1), P(outer, j + 1, k1), Recess(j + 1, k1), Recess(j, k1), -tangent, wallPart);
                if (!Window(j - 1, k))
                    b.Quad(P(outer, j, k), P(outer, j, k1), Recess(j, k1), Recess(j, k), Vector3.up, wallPart);
                if (!Window(j + 1, k))
                    b.Quad(P(outer, j + 1, k), P(outer, j + 1, k1), Recess(j + 1, k1), Recess(j + 1, k), Vector3.down, wallPart);
            }
            else b.Quad(outer[j, k], outer[j, k1], outer[j + 1, k1], outer[j + 1, k], radial);
            b.Quad(inner[j, k], inner[j, k1], inner[j + 1, k1], inner[j + 1, k], -radial);

            // Close the wall thickness wherever the neighbour cell is cut away.
            if (Skip(j, k + 1))
                b.Quad(P(outer, j, k1), P(outer, j + 1, k1), P(inner, j + 1, k1), P(inner, j, k1), tangent, wallPart);
            if (Skip(j, k - 1))
                b.Quad(P(outer, j, k), P(outer, j + 1, k), P(inner, j + 1, k), P(inner, j, k), -tangent, wallPart);
            if (j == rings - 1 || Skip(j + 1, k))
                b.Quad(P(outer, j + 1, k), P(outer, j + 1, k1), P(inner, j + 1, k1), P(inner, j + 1, k), Vector3.up, wallPart);
            if (j > 0 && Skip(j - 1, k))
                b.Quad(P(outer, j, k), P(outer, j, k1), P(inner, j, k1), P(inner, j, k), Vector3.down, wallPart);
        }

        // Crown: flat-topped teeth of different heights on the rim, so the rim between them
        // and the tops of the teeth can both be stood on after climbing.
        for (int k = 0; k < segments;)
        {
            float level = shape.Crown((k + 0.5f) * Mathf.PI * 2f / segments);
            int end = k + 1;
            while (end < segments && Mathf.Abs(shape.Crown((end + 0.5f) * Mathf.PI * 2f / segments) - level) < 0.01f) end++;
            if (level > 0.01f) Battlement(b, shape, k, end, segments, shape.height, shape.height + level, wallPart);
            k = end;
        }

        // Ledges spiral up the outside, each a rough wedge of stone to rest on.
        float ledgeAngle = shape.doorAngle + Mathf.PI * 0.35f;
        for (int i = 0; i < ledgeCount; i++)
        {
            float y = Mathf.Lerp(5.5f, shape.height - 4f, ledgeCount > 1 ? i / (float)(ledgeCount - 1) : 0f) + Range(random, -0.8f, 0.8f);
            float width = Range(random, 0.45f, 0.8f);
            Ledge(b, shape, ledgeAngle, ledgeAngle + width, y, Range(random, 1.8f, 2.6f), Range(random, 0.6f, 1f), (float)random.NextDouble());
            ledgeAngle += Range(random, 0.9f, 1.5f);
        }

        beamCentre = Vector3.zero;
        if (beam)
        {
            float y = shape.height - 2.5f;
            float angle = Range(random, 0f, Mathf.PI * 2f);
            Vector3 centre = shape.Axis(y);
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float reachA = shape.Inner(angle, y) + 1f;
            float reachB = shape.Inner(angle + Mathf.PI, y) + 1f;
            Bar(b, centre - direction * reachB, centre + direction * reachA, 1.1f, 0.7f, (float)random.NextDouble());
            beamCentre = centre + Vector3.up * 0.35f;
        }

        if (flipY) beamCentre.y = -beamCentre.y;
        return b.ToMesh("Spire", flipY);
    }

    private static Vector3 Point(SpireShape shape, float angle, float y, float radius)
    {
        Vector3 axis = shape.Axis(y);
        return new Vector3(axis.x + Mathf.Cos(angle) * radius, y, axis.z + Mathf.Sin(angle) * radius);
    }

    /// <summary>A block standing on the rim between two segment boundaries, with straight sides and a flat top.</summary>
    private static void Battlement(Builder b, SpireShape shape, int k0, int k1, int segments, float y0, float y1, float part)
    {
        int n = k1 - k0;
        var outLow = new Vector3[n + 1];
        var outHigh = new Vector3[n + 1];
        var inLow = new Vector3[n + 1];
        var inHigh = new Vector3[n + 1];
        Vector3 axis = shape.Axis(y0);
        for (int i = 0; i <= n; i++)
        {
            float angle = (k0 + i) * Mathf.PI * 2f / segments;
            Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 centre = new Vector3(axis.x, 0f, axis.z);
            float rOut = shape.Outer(angle, y0), rIn = shape.Inner(angle, y0);
            outLow[i] = centre + radial * rOut + Vector3.up * y0;
            outHigh[i] = centre + radial * rOut + Vector3.up * y1;
            inLow[i] = centre + radial * rIn + Vector3.up * y0;
            inHigh[i] = centre + radial * rIn + Vector3.up * y1;
        }
        for (int i = 0; i < n; i++)
        {
            float mid = (k0 + i + 0.5f) * Mathf.PI * 2f / segments;
            Vector3 radial = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
            b.Quad(outLow[i], outLow[i + 1], outHigh[i + 1], outHigh[i], radial, part);
            b.Quad(inLow[i], inLow[i + 1], inHigh[i + 1], inHigh[i], -radial, part);
            b.Quad(outHigh[i], outHigh[i + 1], inHigh[i + 1], inHigh[i], Vector3.up, part);
        }
        float a0 = k0 * Mathf.PI * 2f / segments, a1 = k1 * Mathf.PI * 2f / segments;
        Vector3 t0 = new Vector3(-Mathf.Sin(a0), 0f, Mathf.Cos(a0));
        Vector3 t1 = new Vector3(-Mathf.Sin(a1), 0f, Mathf.Cos(a1));
        b.Quad(outLow[0], outHigh[0], inHigh[0], inLow[0], -t0, part);
        b.Quad(outLow[n], outHigh[n], inHigh[n], inLow[n], t1, part);
    }

    private static void Ledge(Builder b, SpireShape shape, float a0, float a1, float y, float reach, float thickness, float part)
    {
        const int steps = 7;
        var topIn = new Vector3[steps + 1];
        var topOut = new Vector3[steps + 1];
        var lowIn = new Vector3[steps + 1];
        var lowOut = new Vector3[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float angle = Mathf.Lerp(a0, a1, t);
            // Rounded ends: the ledge is widest in the middle.
            float bulge = Mathf.Sin(t * Mathf.PI);
            float rOut = shape.Outer(angle, y) + reach * (0.35f + 0.65f * bulge);
            // The underside runs steeply back into the wall, so it can be climbed like a slanted wall.
            float drop = thickness + (rOut - shape.Outer(angle, y)) * 1.6f;
            float rIn = Mathf.Min(shape.Outer(angle, y), shape.Outer(angle, y - drop)) - 0.6f;
            topIn[i] = Point(shape, angle, y, rIn);
            topOut[i] = Point(shape, angle, y, rOut);
            lowIn[i] = Point(shape, angle, y - drop, rIn);
            lowOut[i] = Point(shape, angle, y - thickness * 0.4f, rOut - 0.15f);
        }
        for (int i = 0; i < steps; i++)
        {
            float mid = Mathf.Lerp(a0, a1, (i + 0.5f) / steps);
            Vector3 radial = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
            b.Quad(topIn[i], topIn[i + 1], topOut[i + 1], topOut[i], Vector3.up, part);
            b.Quad(lowIn[i], lowIn[i + 1], lowOut[i + 1], lowOut[i], Vector3.down + radial, part);
            b.Quad(topOut[i], topOut[i + 1], lowOut[i + 1], lowOut[i], radial, part);
        }
        Vector3 t0 = new Vector3(-Mathf.Sin(a0), 0f, Mathf.Cos(a0));
        Vector3 t1 = new Vector3(-Mathf.Sin(a1), 0f, Mathf.Cos(a1));
        b.Quad(topIn[0], topOut[0], lowOut[0], lowIn[0], -t0, part);
        b.Quad(topIn[steps], topOut[steps], lowOut[steps], lowIn[steps], t1, part);
    }

    /// <summary>A rough rectangular bar from a to b.</summary>
    private static void Bar(Builder b, Vector3 from, Vector3 to, float width, float thickness, float part)
    {
        Vector3 along = (to - from).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, along).normalized * (width * 0.5f);
        Vector3 up = Vector3.up * thickness;
        Vector3 down = Vector3.zero;
        Vector3[] c =
        {
            from - side + down, from + side + down, from + side + up, from - side + up,
            to - side + down, to + side + down, to + side + up, to - side + up,
        };
        b.Quad(c[3], c[2], c[6], c[7], Vector3.up, part);
        b.Quad(c[0], c[1], c[5], c[4], Vector3.down, part);
        b.Quad(c[1], c[2], c[6], c[5], side, part);
        b.Quad(c[0], c[3], c[7], c[4], -side, part);
        b.Quad(c[0], c[1], c[2], c[3], -along, part);
        b.Quad(c[4], c[5], c[6], c[7], along, part);
    }

    // ---------------- Halo ----------------

    /// <summary>An irregular thin ring lying in the XZ plane.</summary>
    public static Mesh Halo(float radius, float thickness, int seed)
    {
        var b = new Builder { height = 1f };
        var random = new System.Random(seed);
        const int around = 72, tube = 6;
        float phase = Range(random, 0f, 10f);
        var grid = new int[around, tube];
        var centres = new Vector3[around];
        for (int i = 0; i < around; i++)
        {
            float a = i * Mathf.PI * 2f / around;
            float r = radius * (1f + 0.04f * Mathf.Sin(a * 3f + phase));
            float t = thickness * (0.5f + Mathf.PerlinNoise(Mathf.Cos(a) * 2f + phase, Mathf.Sin(a) * 2f));
            Vector3 centre = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a * 2f + phase) * radius * 0.03f, Mathf.Sin(a) * r);
            Vector3 radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            centres[i] = centre;
            for (int j = 0; j < tube; j++)
            {
                float s = j * Mathf.PI * 2f / tube;
                Vector3 p = centre + (radial * Mathf.Cos(s) + Vector3.up * Mathf.Sin(s)) * t;
                grid[i, j] = b.vertices.Count;
                b.vertices.Add(p);
                b.uvs.Add(new Vector2(i / (float)around, 0.5f));
            }
        }
        for (int i = 0; i < around; i++)
        for (int j = 0; j < tube; j++)
        {
            int i1 = (i + 1) % around, j1 = (j + 1) % tube;
            Vector3 mid = (b.vertices[grid[i, j]] + b.vertices[grid[i1, j1]]) * 0.5f;
            b.Quad(grid[i, j], grid[i1, j], grid[i1, j1], grid[i, j1], mid - (centres[i] + centres[i1]) * 0.5f);
        }
        return b.ToMesh("Halo", false);
    }

    // ---------------- Glyph ----------------

    /// <summary>
    /// Flat stroke glyph in the XY plane, visible from both sides. Strokes are polylines in a box
    /// about 1 wide and 1.4 tall with its base at y = 0; size scales the whole glyph.
    /// </summary>
    public static Mesh Glyph(IReadOnlyList<Vector2[]> strokes, float size, float width)
    {
        var b = new Builder { height = 1f };
        float half = width * 0.5f;
        foreach (Vector2[] stroke in strokes)
        {
            if (stroke.Length == 1)
            {
                Vector3 p = stroke[0] * size;
                Dot(b, p, half * 1.6f);
                continue;
            }
            for (int i = 0; i < stroke.Length - 1; i++)
            {
                Vector3 a = stroke[i] * size, c = stroke[i + 1] * size;
                Vector3 along = (c - a).normalized;
                Vector3 side = new Vector3(-along.y, along.x, 0f) * half;
                // Overlap a little so joints look closed.
                a -= along * half * 0.8f;
                c += along * half * 0.8f;
                DoubleQuad(b, a - side, a + side, c + side, c - side);
            }
        }
        return b.ToMesh("Glyph", false);
    }

    private static void Dot(Builder b, Vector3 p, float radius)
    {
        DoubleQuad(b, p + new Vector3(-radius, 0f), p + new Vector3(0f, radius), p + new Vector3(radius, 0f), p + new Vector3(0f, -radius));
    }

    private static void DoubleQuad(Builder b, Vector3 a, Vector3 c1, Vector3 c2, Vector3 d)
    {
        b.Quad(a, c1, c2, d, Vector3.back, 0.5f);
        b.Quad(a, c1, c2, d, Vector3.forward, 0.5f);
    }

    /// <summary>Points around a circle, as a closed polyline.</summary>
    public static Vector2[] Ring(Vector2 centre, float radius, int steps = 10)
    {
        var points = new Vector2[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float a = i * Mathf.PI * 2f / steps;
            points[i] = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        return points;
    }

    // ---------------- Seed ----------------

    /// <summary>A small ribbed pod standing on y = 0, about 0.5 m tall.</summary>
    public static Mesh Seed(int seed)
    {
        var b = new Builder { height = 0.5f };
        var random = new System.Random(seed);
        const int around = 14, along = 12;
        float twist = Range(random, 0.6f, 1.4f);
        var grid = new int[along + 1, around];
        for (int j = 0; j <= along; j++)
        {
            float t = j / (float)along;
            float r = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * t)), 0.8f) * 0.17f * (1f + 0.35f * (1f - t));
            for (int i = 0; i < around; i++)
            {
                float a = i * Mathf.PI * 2f / around + t * twist;
                float rib = 1f + 0.12f * Mathf.Cos(a * 5f);
                grid[j, i] = b.Add(new Vector3(Mathf.Cos(a) * r * rib, t * 0.5f, Mathf.Sin(a) * r * rib), 0.5f);
            }
        }
        for (int j = 0; j < along; j++)
        for (int i = 0; i < around; i++)
        {
            int i1 = (i + 1) % around;
            Vector3 mid = (b.vertices[grid[j, i]] + b.vertices[grid[j + 1, i1]]) * 0.5f;
            b.Quad(grid[j, i], grid[j, i1], grid[j + 1, i1], grid[j + 1, i], new Vector3(mid.x, 0f, mid.z));
        }
        return b.ToMesh("Seed", false);
    }

    private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
}
