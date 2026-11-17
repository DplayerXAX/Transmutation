using UnityEngine;

/// <summary>
/// The dark tunnels under the spawn forest (Scene_World). Carved into the world's own density field, so the walls
/// are normal terrain (same material, colliders, chunk cache). Layout, all in world metres:
///   mouth   - a narrow crawl out of the back (west) wall of the spawn chamber, winding down,
///   hall    - one open cavern with rock pillars, about 40 x 30 m, 10 m high,
///   tunnels - Minecraft-like worm tunnels round the hall: an east loop, a west branch to a grotto, an upper loop
///             back to the mouth, short dead ends, and the deep descent south to the skeleton chamber (the baton).
/// Kept inside the crust (surface about +6, inner world ceiling about -33) and away from the Goo graft.
/// After changing the layout the saved chunk meshes (TerrainChunks.bytes) must be baked again.
/// </summary>
public static class DarkTunnelShape
{
    private struct Tube
    {
        public Vector3[] points;
        public float radius, endRadius;
        public Tube(float radius, float endRadius, params Vector3[] points) { this.points = points; this.radius = radius; this.endRadius = endRadius; }
    }

    // Start of the crawl, in the spawn chamber's air just inside its west wall.
    public static readonly Vector3 Mouth = new Vector3(-183.5f, -9.4f, -105f);
    public static readonly Vector3 HallCentre = new Vector3(-206f, -23f, -146f);
    public static readonly Vector3 HallRadii = new Vector3(20f, 6f, 15f);
    public static readonly Vector3 Grotto = new Vector3(-242f, -19.5f, -118f);
    public static readonly Vector3 Skeleton = new Vector3(-187f, -27.5f, -194f);
    public const float SkeletonRadius = 7f;

    public static readonly Bounds Area = new Bounds(new Vector3(-210f, -19f, -150f), new Vector3(86f, 30f, 112f));

    private static readonly Tube Entrance = new Tube(1.7f, 1.8f,
        Mouth, new Vector3(-190f, -10.6f, -107f), new Vector3(-196f, -13.5f, -112f), new Vector3(-201f, -16.5f, -119f),
        new Vector3(-204f, -19f, -128f), new Vector3(-205f, -21f, -134f));

    private static readonly Tube[] Tubes =
    {
        Entrance,
        // East loop.
        new Tube(2f, 1.8f, new Vector3(-190f, -24f, -141f), new Vector3(-179f, -23f, -150f), new Vector3(-176f, -24.5f, -164f),
            new Vector3(-187f, -25.5f, -172f), new Vector3(-198f, -25.5f, -160f)),
        // West branch to the grotto.
        new Tube(2f, 1.7f, new Vector3(-222f, -23f, -143f), new Vector3(-233f, -21f, -134f), new Vector3(-239f, -20f, -124f), Grotto),
        // Upper loop from the hall's north-west back to the mouth crawl.
        new Tube(1.6f, 1.7f, new Vector3(-214f, -19.5f, -134f), new Vector3(-222f, -16f, -125f), new Vector3(-224f, -15f, -113f),
            new Vector3(-214f, -15.5f, -107f), new Vector3(-206f, -16f, -114f), new Vector3(-201f, -16.5f, -119f)),
        // The deep descent south to the skeleton chamber.
        new Tube(1.8f, 1.7f, new Vector3(-208f, -26.5f, -156f), new Vector3(-213f, -27.5f, -168f), new Vector3(-206f, -28.5f, -179f),
            new Vector3(-196f, -29f, -186f), new Vector3(-190f, -28.5f, -191f)),
        // Short dead ends.
        new Tube(1.4f, 1.1f, new Vector3(-196f, -20f, -151f), new Vector3(-190f, -17.5f, -159f), new Vector3(-184f, -18f, -163f)),
        new Tube(1.5f, 1.2f, new Vector3(-221f, -26f, -153f), new Vector3(-231f, -26.5f, -158f), new Vector3(-236f, -25f, -167f)),
        new Tube(1.3f, 1.3f, new Vector3(-200f, -21f, -133f), new Vector3(-197f, -18.5f, -126f), new Vector3(-199f, -17f, -121f)),
        new Tube(1.4f, 1.1f, new Vector3(-176f, -24.5f, -164f), new Vector3(-172f, -22f, -176f), new Vector3(-176f, -21f, -184f)),
    };

    // Rock columns standing in the hall: x, z, radius.
    private static readonly Vector3[] Pillars =
    {
        new Vector3(-213f, -150f, 2.2f), new Vector3(-198f, -141f, 1.7f), new Vector3(-203f, -154f, 1.4f), new Vector3(-219f, -139f, 1.5f),
    };

    /// <summary>Applies the tunnels to the world density (positive = rock).</summary>
    public static float Carve(Vector3 p, float density)
    {
        if (!Area.Contains(p)) return density;
        return Mathf.Min(density, Distance(p));
    }

    /// <summary>Signed distance to the tunnel air (negative inside the tunnels).</summary>
    public static float Distance(Vector3 p)
    {
        float d = Hall(p);
        for (int i = 0; i < Tubes.Length; i++) d = SmoothMin(d, TubeDistance(p, Tubes[i]), 1.6f);
        d = SmoothMin(d, Room(p, Grotto, 4.6f, 3.2f), 1.5f);
        d = SmoothMin(d, Room(p, Skeleton, SkeletonRadius, 3f), 1.5f);
        // Rough walls, never so rough that a tunnel pinches shut.
        float n = Mathf.PerlinNoise(p.x * 0.21f + p.y * 0.13f, p.z * 0.21f - 7f) + Mathf.PerlinNoise(p.y * 0.23f + 13f, p.x * 0.17f + p.z * 0.11f) - 1f;
        return d + n * 0.45f;
    }

    private static float Hall(Vector3 p)
    {
        Vector3 q = p - HallCentre;
        float k = new Vector3(q.x / HallRadii.x, q.y / HallRadii.y, q.z / HallRadii.z).magnitude;
        float d = (k - 1f) * HallRadii.y;
        // A flat but uneven floor, a bumpy ceiling.
        float floor = HallCentre.y - 4.4f + Mathf.PerlinNoise(p.x * 0.09f, p.z * 0.09f) * 1.2f;
        d = Mathf.Max(d, floor - p.y);
        float bumps = Mathf.PerlinNoise(p.x * 0.31f + 5f, p.z * 0.31f + 9f);
        d += Mathf.Max(0f, q.y) * bumps * 0.35f;
        foreach (Vector3 c in Pillars)
        {
            // Pillars thicken towards floor and ceiling.
            float waist = c.z * (1f + 0.06f * q.y * q.y);
            float pillar = new Vector2(p.x - c.x, p.z - c.y).magnitude - waist;
            d = Mathf.Max(d, -pillar);
        }
        return d;
    }

    private static float Room(Vector3 p, Vector3 centre, float radius, float floorDrop)
    {
        float d = Vector3.Distance(p, centre) - radius;
        return Mathf.Max(d, centre.y - floorDrop - p.y);
    }

    private static float TubeDistance(Vector3 p, Tube tube)
    {
        float best = float.MaxValue, floorY = 0f, radius = tube.radius;
        int segments = tube.points.Length - 1;
        for (int i = 0; i < segments; i++)
        {
            Vector3 a = tube.points[i], b = tube.points[i + 1], ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            float r = Mathf.Lerp(tube.radius, tube.endRadius, (i + t) / segments);
            Vector3 c = a + ab * t;
            float d = Vector3.Distance(p, c) - r;
            if (d < best) { best = d; floorY = c.y - r * 0.62f; radius = r; }
        }
        // Flat floor so the tunnels can be walked.
        return Mathf.Max(best, floorY - p.y);
    }

    private static float SmoothMin(float a, float b, float k)
    {
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
        return Mathf.Lerp(b, a, h) - k * h * (1f - h);
    }

    // ---------------- Light ----------------

    /// <summary>How dark the tunnels are at a point: 0 outside, 1 once past the first bend of the crawl.</summary>
    public static float Darkness(Vector3 p)
    {
        if (!Area.Contains(p) || Distance(p) > 2.5f) return 0f;
        return Mathf.SmoothStep(0f, 1f, (Vector3.Distance(p, Mouth) - 3f) / 10f);
    }

    /// <summary>0..1: the deep descent and the skeleton chamber, darker still.</summary>
    public static float Deep(Vector3 p)
    {
        float south = Mathf.SmoothStep(0f, 1f, (-162f - p.z) / 10f);
        float low = Mathf.SmoothStep(0f, 1f, (-25f - p.y) / 2.5f);
        return south * low;
    }
}
