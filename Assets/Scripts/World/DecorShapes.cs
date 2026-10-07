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
        Cage,
        Needles,
        Halo,
        Totem,
        Ribbon,
        Fruit,
        Flower,
        HangingStalk,
        Mushrooms,
        Pebbles,
        Fronds,
        Obelisk,
    }

    public static bool IsFaceted(Shape shape) =>
        shape == Shape.Shards || shape == Shape.Arch || shape == Shape.Cairn || shape == Shape.Cage || shape == Shape.Totem ||
        shape == Shape.Pebbles || shape == Shape.Obelisk;

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
            case Shape.Cage: Cage(mesh, random); break;
            case Shape.Needles: Needles(mesh, random); break;
            case Shape.Halo: Halo(mesh, random); break;
            case Shape.Totem: Totem(mesh, random); break;
            case Shape.Ribbon: Ribbon(mesh, random); break;
            case Shape.Fruit: Fruit(mesh, random); break;
            case Shape.Flower: Flower(mesh, random); break;
            case Shape.HangingStalk: HangingStalk(mesh, random); break;
            case Shape.Mushrooms: Mushrooms(mesh, random); break;
            case Shape.Pebbles: Pebbles(mesh, random); break;
            case Shape.Fronds: Fronds(mesh, random); break;
            case Shape.Obelisk: Obelisk(mesh, random); break;
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

    /// <summary>An irregular wire polyhedron standing on thin legs, like a drawing of a rock.</summary>
    private static void Cage(DecorMesh mesh, System.Random random)
    {
        int count = Range(random, 6, 11);
        float size = Range(random, 0.6f, 1.1f);
        var centre = new Vector3(0f, size * Range(random, 1.1f, 1.8f), 0f);
        var stretch = new Vector3(Range(random, 0.7f, 1.2f), Range(random, 0.9f, 1.6f), Range(random, 0.7f, 1.2f)) * size;
        var points = new Vector3[count];
        for (int i = 0; i < count; i++) points[i] = centre + Vector3.Scale(OnSphere(random), stretch);

        float thickness = Range(random, 0.02f, 0.04f);
        float height = centre.y + stretch.y;
        var edges = new HashSet<(int, int)>();
        for (int i = 0; i < count; i++)
        {
            // Connect each corner to its nearest few neighbours.
            var order = new List<int>();
            for (int j = 0; j < count; j++) if (j != i) order.Add(j);
            Vector3 from = points[i];
            order.Sort((x, y) => (points[x] - from).sqrMagnitude.CompareTo((points[y] - from).sqrMagnitude));
            int links = Range(random, 2, 4);
            for (int k = 0; k < links && k < order.Count; k++)
                edges.Add((Mathf.Min(i, order[k]), Mathf.Max(i, order[k])));
        }
        foreach (var (a, b) in edges)
            Rod(mesh, points[a], points[b], thickness, random, height);

        // A few legs down to the ground from the lowest corners.
        var lowest = new List<int>();
        for (int i = 0; i < count; i++) lowest.Add(i);
        lowest.Sort((x, y) => points[x].y.CompareTo(points[y].y));
        int legs = Range(random, 2, 4);
        for (int i = 0; i < legs && i < lowest.Count; i++)
        {
            Vector3 top = points[lowest[i]];
            Rod(mesh, new Vector3(top.x * 1.3f, -0.2f, top.z * 1.3f), top, thickness * 0.8f, random, height);
        }
    }

    /// <summary>A dense tuft of thin spikes of very different heights.</summary>
    private static void Needles(DecorMesh mesh, System.Random random)
    {
        int count = Range(random, 14, 40);
        float reach = Range(random, 0.4f, 0.9f);
        for (int i = 0; i < count; i++)
        {
            Vector3 root = Flat(OnDisc(random)) * reach;
            float height = Mathf.Lerp(0.25f, 1.9f, Mathf.Pow((float)random.NextDouble(), 2.2f)) * (1.2f - root.magnitude / reach * 0.5f);
            Vector3 lean = root * Range(random, 0.1f, 0.5f) + Flat(OnDisc(random)) * 0.15f;
            Vector3 direction = (Vector3.up + lean).normalized;
            var path = new List<Vector3>
            {
                root - direction * 0.1f,
                root + direction * height * 0.5f + Flat(OnDisc(random)) * 0.04f,
                root + direction * height,
            };
            float radius = Range(random, 0.012f, 0.03f);
            Tube(mesh, path, t => radius * (1f - 0.7f * t), 3, 0f, random, height * 0.15f, 0f, height / 1.9f);
        }
    }

    /// <summary>One or two tilted rings hovering above the ground, sometimes held by a hair-thin stem.</summary>
    private static void Halo(DecorMesh mesh, System.Random random)
    {
        int count = random.NextDouble() < 0.35 ? 2 : 1;
        float height = Range(random, 1.4f, 3f);
        for (int r = 0; r < count; r++)
        {
            float radius = Range(random, 0.5f, 1.3f) * (r == 0 ? 1f : 0.6f);
            Quaternion tilt = Quaternion.Euler(Range(random, -60f, 60f), Range(random, 0f, 360f), Range(random, -60f, 60f));
            var centre = new Vector3(0f, height + r * 0.35f, 0f);
            var path = new List<Vector3>();
            const int points = 28;
            float phase = Range(random, 0f, 6.28f);
            for (int i = 0; i <= points; i++)
            {
                float angle = i / (float)points * Mathf.PI * 2f;
                float wobble = 1f + 0.08f * Mathf.Sin(angle * 3f + phase);
                path.Add(centre + tilt * new Vector3(Mathf.Cos(angle) * radius * wobble, 0f, Mathf.Sin(angle) * radius * wobble));
            }
            float thickness = Range(random, 0.035f, 0.07f);
            Tube(mesh, path, t => thickness, 7, 0.05f, random, 0f, 0.95f, 1f);
        }
        if (random.NextDouble() < 0.6)
            Rod(mesh, new Vector3(0f, -0.2f, 0f), new Vector3(0f, height, 0f), 0.012f, random, height);
    }

    /// <summary>Unrelated solids skewered on one thin axis: discs, stones, small prisms.</summary>
    private static void Totem(DecorMesh mesh, System.Random random)
    {
        float height = Range(random, 1.8f, 3.6f);
        Rod(mesh, new Vector3(0f, -0.3f, 0f), new Vector3(0f, height, 0f), Range(random, 0.025f, 0.045f), random, height);
        int count = Range(random, 2, 6);
        for (int i = 0; i < count; i++)
        {
            float y = height * Mathf.Lerp(0.25f, 0.95f, (i + (float)random.NextDouble() * 0.6f) / count);
            var centre = new Vector3(Range(random, -0.05f, 0.05f), y, Range(random, -0.05f, 0.05f));
            float size = Range(random, 0.15f, 0.45f);
            float t = y / height;
            switch (random.Next(3))
            {
                case 0:
                    Blob(mesh, centre, new Vector3(size, size * 0.12f, size),
                        Quaternion.Euler(Range(random, -25f, 25f), 0f, Range(random, -25f, 25f)), 3, 10, 0.05f, random, t, t);
                    break;
                case 1:
                    Blob(mesh, centre, Vector3.one * size * 0.7f,
                        Quaternion.Euler(Range(random, 0f, 360f), Range(random, 0f, 360f), 0f), 4, 6, 0.2f, random, t - 0.05f, t + 0.05f);
                    break;
                default:
                    Vector3 axis = Quaternion.Euler(Range(random, -40f, 40f), Range(random, 0f, 360f), 0f) * Vector3.up * size;
                    Tube(mesh, new List<Vector3> { centre - axis, centre + axis }, _ => size * 0.45f, Range(random, 3, 5), 0.1f, random, 0f, t, t);
                    break;
            }
        }
    }

    /// <summary>A flat band spiralling upwards and twisting as it goes.</summary>
    private static void Ribbon(DecorMesh mesh, System.Random random)
    {
        float height = Range(random, 1.4f, 3.4f);
        float spiral = Range(random, 0.15f, 0.5f);
        float turns = Range(random, 0.5f, 1.6f);
        float phase = Range(random, 0f, 6.28f);
        var path = new List<Vector3>();
        const int points = 22;
        for (int i = 0; i <= points; i++)
        {
            float t = i / (float)points;
            float angle = phase + t * turns * Mathf.PI * 2f;
            float r = spiral * (0.3f + t);
            path.Add(new Vector3(Mathf.Cos(angle) * r, t * height - 0.15f, Mathf.Sin(angle) * r));
        }
        float width = Range(random, 0.18f, 0.35f);
        Tube(mesh, path, t => width * (1f - 0.6f * t * t), 8, 0.05f, random, 0f, 0f, 1f, null,
            Range(random, 0.08f, 0.16f), Range(random, -6f, 6f));
    }

    /// <summary>A bunch of lumpy berries hanging below the origin, with a short stem up to it.</summary>
    private static void Fruit(DecorMesh mesh, System.Random random)
    {
        int count = Range(random, 3, 8);
        float size = Range(random, 0.05f, 0.085f);
        Tube(mesh, new List<Vector3> { new Vector3(0f, 0.02f, 0f), new Vector3(0f, -0.08f, 0f) }, _ => 0.012f, 4, 0f, random, 0f, 0.95f, 0.9f);
        for (int i = 0; i < count; i++)
        {
            // Berries settle into a loose teardrop: wider at the top, one at the bottom.
            float depth = Mathf.Lerp(0.1f, 0.3f, i / (float)Mathf.Max(1, count - 1));
            Vector3 centre = new Vector3(0f, -depth, 0f) + Flat(OnDisc(random)) * size * (1.4f - depth * 2.5f);
            float r = size * Range(random, 0.75f, 1.2f);
            Blob(mesh, centre, new Vector3(r, r * Range(random, 1f, 1.3f), r), Quaternion.Euler(0f, Range(random, 0f, 360f), 0f), 6, 8, 0.1f, random, 0.6f, 1f);
        }
    }

    /// <summary>A curved stem with a ring of petals around a swollen centre.</summary>
    private static void Flower(DecorMesh mesh, System.Random random)
    {
        float height = Range(random, 0.3f, 0.65f);
        Vector3 lean = Flat(OnDisc(random)) * 0.35f;
        var path = new List<Vector3>();
        for (int i = 0; i <= 6; i++)
        {
            float t = i / 6f;
            path.Add(new Vector3(0f, t * height - 0.03f, 0f) + lean * t * t * height);
        }
        Tube(mesh, path, t => 0.014f * (1.2f - 0.4f * t), 5, 0.05f, random, 0f, 0f, 0.65f);

        Vector3 head = path[path.Count - 1];
        Vector3 facing = (Vector3.up + lean * 1.5f).normalized;
        Quaternion toHead = Quaternion.FromToRotation(Vector3.up, facing);
        int petals = Range(random, 4, 8);
        float petalLength = Range(random, 0.07f, 0.13f);
        float cup = Range(random, 15f, 55f);
        float spin = Range(random, 0f, 360f);
        for (int p = 0; p < petals; p++)
        {
            Quaternion around = toHead * Quaternion.Euler(0f, spin + p * 360f / petals + Range(random, -10f, 10f), 0f);
            Quaternion tilt = around * Quaternion.Euler(cup + Range(random, -8f, 8f), 0f, 0f);
            Vector3 centre = head + tilt * new Vector3(0f, 0f, petalLength * 0.6f);
            Blob(mesh, centre, new Vector3(petalLength * 0.38f, petalLength * 0.07f, petalLength * 0.6f), tilt, 4, 8, 0.08f, random, 0.8f, 1f);
        }
        Blob(mesh, head, Vector3.one * petalLength * 0.28f, toHead, 5, 7, 0.15f, random, 0.9f, 1f);
    }

    /// <summary>A thin, slightly wavering thread of unit length along +y, for fruit hanging from a ceiling.</summary>
    private static void HangingStalk(DecorMesh mesh, System.Random random)
    {
        var path = new List<Vector3>();
        float phase = Range(random, 0f, 6.28f);
        for (int i = 0; i <= 8; i++)
        {
            float t = i / 8f;
            path.Add(new Vector3(Mathf.Sin(t * 5f + phase) * 0.03f, t - 0.03f, Mathf.Cos(t * 4f + phase) * 0.03f));
        }
        Tube(mesh, path, t => 0.016f * (1.3f - 0.6f * t), 5, 0.05f, random, 0f, 0f, 1f);
    }

    /// <summary>A clump of mushrooms: bent stems with flat, tilted caps of different sizes.</summary>
    private static void Mushrooms(DecorMesh mesh, System.Random random)
    {
        int count = Range(random, 1, 7);
        for (int i = 0; i < count; i++)
        {
            float height = Range(random, 0.25f, 1.2f) * (i == 0 ? 1.3f : 1f);
            Vector3 root = Flat(OnDisc(random)) * 0.45f;
            Vector3 bend = Flat(OnDisc(random)) * 0.3f;
            var path = new List<Vector3>();
            for (int s = 0; s <= 5; s++)
            {
                float t = s / 5f;
                path.Add(root + new Vector3(0f, t * height - 0.05f, 0f) + bend * t * t * height);
            }
            float stem = Range(random, 0.035f, 0.1f) * (0.6f + height * 0.5f);
            Tube(mesh, path, t => stem * (1.25f - 0.35f * t), 7, 0.1f, random, 0f, 0f, 0.8f);

            Vector3 top = path[path.Count - 1];
            float cap = stem * Range(random, 3f, 5.5f);
            Quaternion tilt = Quaternion.Euler(Range(random, -20f, 20f), Range(random, 0f, 360f), Range(random, -20f, 20f));
            Blob(mesh, top + tilt * Vector3.up * cap * 0.12f, new Vector3(cap, cap * Range(random, 0.25f, 0.45f), cap), tilt, 6, 12, 0.08f, random, 0.8f, 1f);
        }
    }

    /// <summary>Scattered small stones half sunk into the ground.</summary>
    private static void Pebbles(DecorMesh mesh, System.Random random)
    {
        int count = Range(random, 6, 20);
        for (int i = 0; i < count; i++)
        {
            float r = Mathf.Lerp(0.05f, 0.28f, Mathf.Pow((float)random.NextDouble(), 2f));
            Vector3 centre = Flat(OnDisc(random)) * 1.6f + Vector3.up * r * 0.15f;
            var radii = new Vector3(r * Range(random, 0.8f, 1.3f), r * Range(random, 0.4f, 0.8f), r * Range(random, 0.7f, 1.1f));
            Blob(mesh, centre, radii, Quaternion.Euler(Range(random, -15f, 15f), Range(random, 0f, 360f), 0f), 3, 6, 0.15f, random, 0f, 0.15f);
        }
    }

    /// <summary>Flat leaves that rise from one root and arch over towards the ground.</summary>
    private static void Fronds(DecorMesh mesh, System.Random random)
    {
        int count = Range(random, 4, 10);
        for (int i = 0; i < count; i++)
        {
            float length = Range(random, 0.6f, 1.5f);
            float angle = i / (float)count * Mathf.PI * 2f + Range(random, -0.3f, 0.3f);
            var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float rise = Range(random, 0.5f, 1.1f);
            var path = new List<Vector3>();
            for (int s = 0; s <= 10; s++)
            {
                float t = s / 10f;
                // Up first, then out and down: a fountain-like arch.
                path.Add(outward * t * length + Vector3.up * (Mathf.Sin(t * Mathf.PI * 0.85f) * rise * length - 0.03f));
            }
            float width = Range(random, 0.06f, 0.13f);
            Tube(mesh, path, t => width * Mathf.Sin(Mathf.Clamp(t, 0.05f, 1f) * Mathf.PI * 0.9f + 0.15f), 6, 0.04f, random, 0.02f, 0f, 1f, null,
                0.15f, Range(random, -1.5f, 1.5f));
        }
    }

    /// <summary>A tall, thin, slightly bent needle of stone with a loose block floating above it.</summary>
    private static void Obelisk(DecorMesh mesh, System.Random random)
    {
        float height = Range(random, 3f, 6.5f);
        float radius = Range(random, 0.18f, 0.35f);
        Vector3 bend = Flat(OnDisc(random)) * 0.25f;
        var path = new List<Vector3> { Vector3.down * 0.5f, Vector3.up * height * 0.5f + bend * 0.5f, Vector3.up * height + bend };
        Tube(mesh, path, t => radius * (1f - 0.5f * t), Range(random, 3, 5), 0.12f, random, 0f, 0f, 0.85f);

        if (random.NextDouble() < 0.7)
        {
            float block = radius * Range(random, 1.2f, 2.2f);
            Vector3 centre = Vector3.up * (height + block + Range(random, 0.25f, 0.6f)) + bend;
            Blob(mesh, centre, new Vector3(block, block * Range(random, 0.6f, 1.4f), block),
                Quaternion.Euler(Range(random, -30f, 30f), Range(random, 0f, 360f), Range(random, -30f, 30f)), 2, 4, 0.1f, random, 0.9f, 1f);
        }
    }

    // ---------------- Builders ----------------

    /// <summary>A straight thin rod between two points. uv.x follows height over the given total.</summary>
    private static void Rod(DecorMesh mesh, Vector3 from, Vector3 to, float radius, System.Random random, float totalHeight)
    {
        float t0 = Mathf.Clamp01(from.y / Mathf.Max(totalHeight, 0.01f));
        float t1 = Mathf.Clamp01(to.y / Mathf.Max(totalHeight, 0.01f));
        Tube(mesh, new List<Vector3> { from, to }, _ => radius, 4, 0f, random, 0f, t0, t1);
    }

    /// <summary>
    /// A tube along a path with an irregular cross-section. tipLength 0 = flat cap.
    /// flatten below 1 squashes the section into a band; twist turns the band along the path (radians).
    /// </summary>
    private static void Tube(DecorMesh mesh, IList<Vector3> path, System.Func<float, float> radius, int sides, float noise,
        System.Random random, float tipLength, float t0, float t1, IList<float> pathT = null, float flatten = 1f, float twist = 0f)
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
            float turn = twist * t;
            Vector3 wide = normal * Mathf.Cos(turn) + binormal * Mathf.Sin(turn);
            Vector3 thin = binormal * Mathf.Cos(turn) - normal * Mathf.Sin(turn);
            float r = radius(t);
            for (int j = 0; j < sides; j++)
            {
                float theta = j / (float)sides * Mathf.PI * 2f;
                float bump = 1f;
                foreach (var h in harmonics) bump += h.amplitude * Mathf.Sin(h.k * theta + h.phase + t * h.twist);
                Vector3 offset = (wide * Mathf.Cos(theta) + thin * Mathf.Sin(theta) * flatten) * r * bump;
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
