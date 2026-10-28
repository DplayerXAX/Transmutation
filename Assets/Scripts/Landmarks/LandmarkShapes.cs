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
    // How lumpy the outer wall is (bulges, courses, ribs); 1 = the original rough rock.
    public float roughness = 1f;
    // Smooth glaze: one full ring round the outside (heights from the ground line; no windows or ledges on it),
    // plus patches that are rare low down and grow denser towards the ring. Off when top <= bottom.
    public float glazeBottom = -1f;
    public float glazeTop = -1f;
    public bool HasGlaze => glazeTop > glazeBottom;
    // The ring only covers angles within glazeArc (half-angle, radians) of glazeArcCentre; 0 = all the way round.
    public float glazeArc;
    public float glazeArcCentre;
    // Everything below this height is cut away: the tower floats, its lower end an open cut.
    public float cutBelow = float.MinValue;

    /// <summary>True for a ring of the wall cut away under a floating tower.</summary>
    public bool CutCell(int j) => j >= 0 && j < Rings && RingY(j + 1) <= cutBelow + 0.01f;
    // Share of the wall covered by patches just under the ring (outside), and above it.
    public float glazePatchDensity = 0.6f;
    // Angle of the wide balcony just below the ring.
    public float balconyAngle;
    public float ringStep = 1.4f;

    public int Rings => HasDrums ? Layout().Length - 1 : Mathf.Max(2, Mathf.CeilToInt((height + bury) / ringStep));
    public float RingY(int j) => HasDrums ? Layout()[Mathf.Clamp(j, 0, Rings)] : -bury + j * (height + bury) / Rings;

    /// <summary>Moves the glaze ring's edges onto ring lines, so the ring covers whole wall cells.</summary>
    public void SnapGlaze()
    {
        if (!HasGlaze) return;
        glazeBottom = RingY(NearestRing(glazeBottom));
        glazeTop = RingY(Mathf.Max(NearestRing(glazeBottom) + 1, NearestRing(glazeTop)));
    }

    private int NearestRing(float y)
    {
        int best = 0;
        for (int j = 1; j <= Rings; j++)
            if (Mathf.Abs(RingY(j) - y) < Mathf.Abs(RingY(best) - y)) best = j;
        return best;
    }

    // ---------------- Stacked drums ----------------
    // 0 = one continuous wall. Otherwise the wall is a stack of drums about this tall. Each drum is its own
    // polygon (square, hexagon, nine or twelve sides), turned and shifted a little against the one below,
    // with a recessed seam under it. Where the drum below sticks out past the one above, its top is a ledge.
    public float drumLength;
    public float seamHeight = 0.7f;
    public float seamDepth = 0.3f;
    public float drumShift = 0.6f;
    public bool HasDrums => drumLength > 0f;

    public sealed class Drum
    {
        public float y0, y1;  // y0 = bottom of its seam (or the buried foot), y1 = top
        public int sides;
        public float twist;   // radians; corners always fall on wall segments
        public Vector2 shift; // sideways offset of its axis
        public bool seam;     // false for the bottom drum
        public float BodyBottom(float seamHeight) => seam ? y0 + seamHeight : y0;
    }

    private readonly List<Drum> drums = new List<Drum>();
    private float[] ringHeights;
    private Vector4 layoutKey;

    /// <summary>The drums from the bottom up (empty without drumLength).</summary>
    public IReadOnlyList<Drum> Drums { get { if (HasDrums) Layout(); return drums; } }

    /// <summary>Lays out the drums and the ring heights (again whenever the height or settings change).</summary>
    private float[] Layout()
    {
        var key = new Vector4(height, bury, drumLength + seamHeight * 17f + doorHeight * 0.37f, noiseSeed + segments * 0.01f + ringStep);
        if (ringHeights != null && key == layoutKey) return ringHeights;
        layoutKey = key;
        drums.Clear();
        var random = new System.Random(Mathf.RoundToInt(noiseSeed * 997f) ^ 0x5d1);
        var joints = new List<float>();
        float y = Mathf.Max(doorHeight + 3f, drumLength * 0.75f);
        while (y < height - Mathf.Max(6f, drumLength * 0.6f))
        {
            joints.Add(y);
            y += drumLength * (0.8f + 0.45f * (float)random.NextDouble());
        }
        int[] choices = { 4, 6, 6, 9, 12 };
        int lastSides = 0;
        float bottom = -bury;
        float step = 360f / segments;
        for (int i = 0; i <= joints.Count; i++)
        {
            int sides;
            int tries = 0;
            do sides = choices[random.Next(choices.Length)]; while ((sides == lastSides || segments % sides != 0) && ++tries < 40);
            lastSides = sides;
            // Corners sit half a face from the face centre; keep them on wall segment lines so the faces are flat.
            float cornerOffset = Mathf.Repeat(180f / sides, step);
            float twist = (random.Next(segments) * step - cornerOffset) * Mathf.Deg2Rad;
            float a = (float)random.NextDouble() * Mathf.PI * 2f;
            Vector2 shift = i == 0 ? Vector2.zero : new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * drumShift * (0.5f + 0.5f * (float)random.NextDouble());
            float top = i < joints.Count ? joints[i] : float.MaxValue; // the last one runs on through the crown
            drums.Add(new Drum { y0 = bottom, y1 = top, sides = sides, twist = twist, shift = shift, seam = i > 0 });
            bottom = top;
        }

        // Rings: about ringStep apart, with two close together at each step in the outline (seam bottom and top),
        // so the steps come out as flat ledges and undersides.
        var breaks = new List<float>();
        foreach (float j in joints) { breaks.Add(j - 0.03f); breaks.Add(j); breaks.Add(j + seamHeight - 0.03f); breaks.Add(j + seamHeight); }
        breaks.Add(height);
        var list = new List<float> { -bury };
        foreach (float b in breaks)
        {
            float start = list[list.Count - 1];
            float span = b - start;
            if (span <= 0.001f) continue;
            int n = span < 0.2f ? 1 : Mathf.Max(1, Mathf.RoundToInt(span / ringStep));
            for (int i = 1; i <= n; i++) list.Add(start + span * i / n);
        }
        ringHeights = list.ToArray();
        return ringHeights;
    }

    /// <summary>The drum a height belongs to (a seam belongs to the drum above it).</summary>
    public Drum DrumAt(float y)
    {
        Layout();
        foreach (Drum d in drums) if (y < d.y1) return d;
        return drums[drums.Count - 1];
    }

    /// <summary>True within a seam between two drums.</summary>
    public bool InSeam(float y)
    {
        if (!HasDrums) return false;
        Drum d = DrumAt(y);
        return d.seam && y < d.y0 + seamHeight;
    }

    /// <summary>True within margin metres of a seam (ledges and windows keep clear of them).</summary>
    public bool NearSeam(float y, float margin)
    {
        if (!HasDrums) return false;
        foreach (Drum d in Drums)
            if (d.seam && y > d.y0 - margin && y < d.y0 + seamHeight + margin) return true;
        return false;
    }

    /// <summary>Radius of a drum's polygon at an angle, for a drum whose round radius would be radius.</summary>
    private static float Polygon(Drum d, float angle, float radius)
    {
        float half = Mathf.PI / d.sides;
        float delta = Mathf.Repeat(angle - d.twist + half, 2f * half) - half;
        // Average radius stays about the same as the round wall's.
        float meanSecant = Mathf.Log(1f / Mathf.Cos(half) + Mathf.Tan(half)) / half;
        // Corners are cut off (chamfered), so a drum never juts far out over a smaller one below it.
        return Mathf.Min(radius / (Mathf.Cos(delta) * meanSecant), radius * 1.1f);
    }

    /// <summary>Wall cell (ring j, segment k) cut away for the door.</summary>
    public bool DoorCell(int j, int k)
    {
        if (j < 0 || j >= Rings) return false;
        k = (k + segments) % segments;
        float mid = (k + 0.5f) * Mathf.PI * 2f / segments;
        return doorHalfAngle > 0f && Mathf.Abs(Mathf.DeltaAngle(mid * Mathf.Rad2Deg, doorAngle * Mathf.Rad2Deg)) < doorHalfAngle * Mathf.Rad2Deg
            && RingY(j + 1) <= doorHeight + 0.01f;
    }

    /// <summary>
    /// Wall cell cut through as a window. Windows come in whole pairs of rings (about 2.8 m tall), so the
    /// player can stand on the sill and walk through. Never on the glaze ring.
    /// </summary>
    public bool WindowCell(int j, int k)
    {
        if (j < 0 || j >= Rings || windows <= 0f) return false;
        k = (k + segments) % segments;
        int pair = j / 2;
        int j0 = pair * 2, j1 = j0 + 2;
        if (j1 > Rings || RingY(j0) < 12f || RingY(j1) > height - 5f || DoorCell(j0, k)) return false;
        if (HasGlaze && RingY(j1) > glazeBottom - 0.2f && RingY(j0) < glazeTop + 0.2f) return false;
        if (HasDrums && (RingY(j1) - RingY(j0) < 2.3f || NearSeam(RingY(j0), 0.6f) || NearSeam(RingY(j1), 0.6f) || DrumAt(RingY(j0) + 0.01f) != DrumAt(RingY(j1) - 0.01f))) return false;
        // Slow noise along the height, fast around the tower.
        float slit = Mathf.PerlinNoise(k * 0.93f + noiseSeed * 3.1f, pair * 0.44f + noiseSeed);
        return slit > 1f - windows * 0.9f;
    }

    /// <summary>
    /// Wall cell covered in glaze, on the outside or the inside face. Outside: the full ring, plus patches
    /// that grow from none near the ground to glazePatchDensity under the ring. Inside: thinner patches, never a ring.
    /// </summary>
    public bool GlazeCell(int j, int k, bool inside)
    {
        if (!HasGlaze || j < 0 || j >= Rings) return false;
        k = (k + segments) % segments;
        if (DoorCell(j, k) || WindowCell(j, k) || CutCell(j)) return false;
        float y0 = RingY(j), y1 = RingY(j + 1);
        if (!inside && y0 >= glazeBottom - 0.01f && y1 <= glazeTop + 0.01f)
        {
            if (glazeArc <= 0f) return true;
            float cellAngle = (k + 0.5f) * Mathf.PI * 2f / segments;
            if (Mathf.Abs(Mathf.DeltaAngle(cellAngle * Mathf.Rad2Deg, glazeArcCentre * Mathf.Rad2Deg)) <= glazeArc * Mathf.Rad2Deg) return true;
        }
        float y = (y0 + y1) * 0.5f;
        if (y < 6f || y > height - 3f) return false;
        float ramp = Mathf.InverseLerp(height * 0.12f, glazeBottom, y);
        float density = y > glazeTop ? glazePatchDensity * 0.75f : glazePatchDensity * ramp * ramp;
        if (inside) density = Mathf.Min(density * 0.7f, 0.4f);
        // Patches a few metres across: noise round the tower (seamless) and up it.
        float angle = (k + 0.5f) * Mathf.PI * 2f / segments;
        float offset = inside ? 37.3f : 0f;
        float n = Mathf.PerlinNoise(Mathf.Cos(angle) * 2.8f + y * 0.21f + noiseSeed * 2.3f + offset, Mathf.Sin(angle) * 2.8f + y * 0.33f + noiseSeed);
        n = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.72f, n));
        return n > 1f - density;
    }

    /// <summary>Offset of the spire's axis at a height (it leans and wanders as it rises).</summary>
    public Vector3 Axis(float y)
    {
        float t = Mathf.Clamp01(y / height);
        Vector3 axis = new Vector3(Mathf.Sin(t * 2.2f + noiseSeed) * lean * t, y, Mathf.Cos(t * 1.7f + noiseSeed * 1.3f) * lean * t * 0.7f);
        if (HasDrums)
        {
            Vector2 shift = DrumAt(y).shift;
            axis += new Vector3(shift.x, 0f, shift.y);
        }
        return axis;
    }

    public float Inner(float angle, float y)
    {
        float t = Mathf.Clamp01(y / height);
        float wobble = (Noise(angle, 1.6f, y * 0.05f, 3.1f) - 0.5f) * 1.2f;
        float radius = Mathf.Lerp(innerRadius, innerRadius * topScale, t) + wobble;
        // The shaft stays straight while the drums shift: undo the drum's sideways offset on the inside.
        if (HasDrums)
        {
            Vector2 shift = DrumAt(y).shift;
            radius -= shift.x * Mathf.Cos(angle) + shift.y * Mathf.Sin(angle);
        }
        return Mathf.Max(1f, radius);
    }

    public float Outer(float angle, float y)
    {
        float t = Mathf.Clamp01(y / height);
        float radius = Mathf.Lerp(innerRadius + wallBase, innerRadius * topScale + wallTop, t);
        // Big slow bulges, horizontal courses (kept gentle so the wall stays climbable) and a few ribs.
        radius += (Noise(angle, 2.3f, y * 0.02f, 0f) - 0.5f) * 2.2f * roughness;
        radius += Mathf.Sin(y * (2f * Mathf.PI / 4.2f) + Noise(angle, 1.1f, 0f, 7.7f) * 3f) * 0.22f * roughness;
        radius += Mathf.Pow(Noise(angle, 4.1f, y * 0.012f, 21.3f), 3f) * 2f * roughness;
        radius += flare * Mathf.Pow(1f - t, 6f) * (0.6f + 0.8f * Noise(angle, 1.5f, 0f, 41.9f));
        if (HasDrums)
        {
            Drum d = DrumAt(y);
            // The seam: a round neck set back into the wall. The drum itself: a flat-sided polygon.
            if (d.seam && y < d.y0 + seamHeight) return Mathf.Max(radius - seamDepth, Inner(angle, y) + 0.8f);
            radius = Polygon(d, angle, radius);
        }
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
    /// <summary>Destroy at runtime, DestroyImmediate in edit mode (the Scene view preview builds landmarks too).</summary>
    public static void Discard(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Object.Destroy(target);
        else Object.DestroyImmediate(target);
    }

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
        int rings = shape.Rings;
        float wallPart = (float)random.NextDouble();

        // Ring heights per segment; the top ring is flat, the crown teeth are added as blocks below.
        var ringY = new float[rings + 1, segments];
        var angles = new float[segments];
        for (int k = 0; k < segments; k++)
        {
            angles[k] = k * Mathf.PI * 2f / segments;
            for (int j = 0; j <= rings; j++)
                ringY[j, k] = shape.RingY(j);
        }

        var outer = new int[rings + 1, segments];
        var inner = new int[rings + 1, segments];
        for (int j = 0; j <= rings; j++)
        for (int k = 0; k < segments; k++)
        {
            outer[j, k] = b.Add(Point(shape, angles[k], ringY[j, k], shape.Outer(angles[k], ringY[j, k])), wallPart);
            inner[j, k] = b.Add(Point(shape, angles[k], ringY[j, k], shape.Inner(angles[k], ringY[j, k])), wallPart);
        }

        bool Skip(int j, int k) => shape.DoorCell(j, k) || shape.CutCell(j);
        // Windows are holes through the wall. Their sills can be stood on and lead into the shaft.
        bool Window(int j, int k) => shape.WindowCell(j, k);

        bool Open(int j, int k) => Skip(j, k) || Window(j, k);

        for (int j = 0; j < rings; j++)
        for (int k = 0; k < segments; k++)
        {
            if (Open(j, k)) continue;
            int k1 = (k + 1) % segments;
            float mid = (k + 0.5f) * Mathf.PI * 2f / segments;
            Vector3 radial = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
            Vector3 tangent = new Vector3(-Mathf.Sin(mid), 0f, Mathf.Cos(mid));

            Vector3 P(int[,] grid, int jj, int kk) => b.vertices[grid[jj, kk]];
            b.Quad(outer[j, k], outer[j, k1], outer[j + 1, k1], outer[j + 1, k], radial);
            b.Quad(inner[j, k], inner[j, k1], inner[j + 1, k1], inner[j + 1, k], -radial);

            // Close the wall thickness wherever the neighbour cell is cut away.
            if (Open(j, k + 1))
                b.Quad(P(outer, j, k1), P(outer, j + 1, k1), P(inner, j + 1, k1), P(inner, j, k1), tangent, wallPart);
            if (Open(j, k - 1))
                b.Quad(P(outer, j, k), P(outer, j + 1, k), P(inner, j + 1, k), P(inner, j, k), -tangent, wallPart);
            if (j == rings - 1 || Open(j + 1, k))
                b.Quad(P(outer, j + 1, k), P(outer, j + 1, k1), P(inner, j + 1, k1), P(inner, j + 1, k), Vector3.up, wallPart);
            if (j > 0 && Open(j - 1, k))
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
            float reach = Range(random, 1.8f, 2.6f), thickness = Range(random, 0.6f, 1f), part = (float)random.NextDouble();
            // Nothing to rest on across the glazed band.
            if ((!shape.HasGlaze || y < shape.glazeBottom - 2f || y > shape.glazeTop + 1f) && y > shape.cutBelow + 2.5f && !shape.NearSeam(y, 2f))
                Ledge(b, shape, ledgeAngle, ledgeAngle + width, y, reach, thickness, part);
            ledgeAngle += Range(random, 0.9f, 1.5f);
        }

        // A wide balcony just under the glaze: a place to stop, and room for a bounce mushroom.
        if (shape.HasGlaze)
            Ledge(b, shape, shape.balconyAngle - BalconyHalfWidth, shape.balconyAngle + BalconyHalfWidth, BalconyY(shape), BalconyReach, 1f, wallPart);

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

    private const float BalconyHalfWidth = 0.32f;
    private const float BalconyReach = 3.8f;
    private static float BalconyY(SpireShape shape) => shape.glazeBottom - 0.4f;

    /// <summary>Middle of the balcony's top, a little out from the wall (spire local space).</summary>
    public static Vector3 BalconyTop(SpireShape shape)
    {
        float y = BalconyY(shape);
        return Point(shape, shape.balconyAngle, y, shape.Outer(shape.balconyAngle, y) + BalconyReach * 0.5f);
    }

    /// <summary>
    /// Smooth shells over the glazed cells (SpireShape.GlazeCell), just outside the outer wall and just inside
    /// the inner wall, with thin sills along their top and bottom edges outside so they read as a coat.
    /// </summary>
    public static Mesh Glaze(SpireShape shape)
    {
        var b = new Builder { height = shape.height };
        int segments = shape.segments, rings = shape.Rings;
        const float gap = 0.06f, sill = 0.08f;
        for (int side = 0; side < 2; side++)
        {
            bool inside = side == 1;
            float R(float angle, float y) => inside ? shape.Inner(angle, y) - gap : shape.Outer(angle, y) + gap;
            for (int j = 0; j < rings; j++)
            for (int k = 0; k < segments; k++)
            {
                if (!shape.GlazeCell(j, k, inside)) continue;
                float a0 = k * Mathf.PI * 2f / segments, a1 = (k + 1) * Mathf.PI * 2f / segments;
                float y0 = shape.RingY(j), y1 = shape.RingY(j + 1);
                float mid = (a0 + a1) * 0.5f;
                Vector3 radial = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid)) * (inside ? -1f : 1f);
                b.Quad(Point(shape, a0, y0, R(a0, y0)), Point(shape, a1, y0, R(a1, y0)), Point(shape, a1, y1, R(a1, y1)), Point(shape, a0, y1, R(a0, y1)), radial, 0.5f);
                if (inside) continue;
                if (!shape.GlazeCell(j - 1, k, false))
                    b.Quad(Point(shape, a0, y0, R(a0, y0) + sill), Point(shape, a1, y0, R(a1, y0) + sill), Point(shape, a1, y0, R(a1, y0) - gap), Point(shape, a0, y0, R(a0, y0) - gap), Vector3.down, 0.5f);
                if (!shape.GlazeCell(j + 1, k, false))
                    b.Quad(Point(shape, a0, y1, R(a0, y1) - gap), Point(shape, a1, y1, R(a1, y1) - gap), Point(shape, a1, y1, R(a1, y1) + sill), Point(shape, a0, y1, R(a0, y1) + sill), Vector3.up, 0.5f);
            }
        }
        return b.ToMesh("Spire Glaze", false);
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
    /// <summary>A flat slab from a to b; its bottom is on the line, its top 'thickness' above.</summary>
    public static Mesh Slab(Vector3 from, Vector3 to, float width, float thickness, float part)
    {
        var b = new Builder { height = 10f };
        Bar(b, from, to, width, thickness, part);
        return b.ToMesh("Slab", false);
    }

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
