using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The dark caves under the crack (Scene_World). The world's inner layer (open from about -33 to -65 under the whole
/// map) is the open cavern; this adds, round the west part of the crack, in the world's own density (so the walls are
/// normal terrain, colliders and chunk cache included):
///   stair tower - a hollow rock column standing from the inner floor to its ceiling south of the crack, with a
///                 spiral tunnel inside: the way down, from a pit in the ground south of the crack rim,
///   pillars     - rock columns in the open cavern (landmarks, walls for Bell Snails),
///   tunnels     - Minecraft-like worm tunnels dug into the cavern floor (loops, a grotto, dead ends) and into its
///                 ceiling (only Glims and water reach those), and the deep chamber with the skeleton and the baton.
/// After changing the layout the saved chunk meshes (TerrainChunks.bytes) must be baked again.
/// </summary>
public static class DarkTunnelShape
{
    private sealed class Tube
    {
        public Vector3[] points;
        public float radius, endRadius;
        public Bounds bounds;
        public Tube(float radius, float endRadius, params Vector3[] points)
        {
            this.points = points; this.radius = radius; this.endRadius = endRadius;
            bounds = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 p in points) bounds.Encapsulate(p);
            bounds.Expand(Mathf.Max(radius, endRadius) * 2f + 3f);
        }
    }

    // ---- Layout (world metres) ----

    /// <summary>Middle of the stair tower; the spiral turns round it.</summary>
    public static readonly Vector2 TowerCentre = new Vector2(-140f, 115f);
    public const float TowerRadius = 13f, SpiralRadius = 8.5f;
    public const float InnerFloor = -64f;
    /// <summary>Where the spiral comes out on the cavern floor.</summary>
    public static readonly Vector3 TowerExit = new Vector3(-140f, InnerFloor + 1.6f, 136f);
    /// <summary>The middle of the cave country under the crack, on the cavern floor.</summary>
    public static readonly Vector3 Hub = new Vector3(-110f, InnerFloor + 1f, 160f);
    public static readonly Vector3 Grotto = new Vector3(-158f, -72f, 186f);
    public static readonly Vector3 Skeleton = new Vector3(-72f, -75.5f, 180f);
    public const float SkeletonRadius = 7f;
    /// <summary>A long low channel in the floor where the Tone Crate slides.</summary>
    public static readonly Vector3 CrateYard = new Vector3(-92f, InnerFloor + 0.6f, 132f);

    public static readonly Bounds Area = new Bounds(new Vector3(-110f, -40f, 155f), new Vector3(130f, 110f, 130f));

    private static Vector3 mouth;
    private static Tube spiral;
    private static Tube[] tubes;
    private static readonly List<Vector3> spiralPoints = new List<Vector3>();

    // Rock columns standing in the open cavern: x, z, radius.
    private static readonly Vector3[] Pillars =
    {
        new Vector3(-118f, 172f, 4.5f), new Vector3(-96f, 182f, 3.2f), new Vector3(-128f, 150f, 3.6f),
        new Vector3(-82f, 162f, 2.8f), new Vector3(-150f, 166f, 5.5f), new Vector3(-104f, 196f, 4f),
    };

    static DarkTunnelShape() => Layout();

    private static void Layout()
    {
        // The spiral: from a pit in the ground just south of the tower down to the cavern floor, 3.6 turns.
        float top = WorldMapTerrain.Height(TowerCentre.x, TowerCentre.y - SpiralRadius) + 2f;
        float bottom = InnerFloor + 1.6f;
        mouth = new Vector3(TowerCentre.x, top + 1f, TowerCentre.y - SpiralRadius - 6f);
        spiralPoints.Clear();
        spiralPoints.Add(mouth);
        const float turns = 3.6f;
        int steps = Mathf.CeilToInt(turns * 14f);
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float a = -Mathf.PI * 0.5f + t * turns * Mathf.PI * 2f;
            float y = Mathf.Lerp(top, bottom, t);
            spiralPoints.Add(new Vector3(TowerCentre.x + Mathf.Cos(a) * SpiralRadius, y, TowerCentre.y + Mathf.Sin(a) * SpiralRadius));
        }
        // Out of the tower to the north, onto the cavern floor.
        Vector3 last = spiralPoints[spiralPoints.Count - 1];
        spiralPoints.Add(new Vector3(TowerCentre.x, bottom, last.z > TowerCentre.y ? last.z + 4f : TowerCentre.y + 10f));
        spiralPoints.Add(TowerExit + Vector3.forward * 4f);
        spiral = new Tube(2.1f, 2.4f, spiralPoints.ToArray());

        tubes = new[]
        {
            spiral,
            // Floor loop east of the hub: down into the floor, round and up again.
            new Tube(2f, 1.9f, new Vector3(-100f, -64f, 152f), new Vector3(-92f, -70f, 146f), new Vector3(-80f, -73f, 150f),
                new Vector3(-74f, -72f, 163f), new Vector3(-84f, -69f, 172f), new Vector3(-96f, -64f, 170f)),
            // West branch under the floor to the grotto.
            new Tube(2f, 1.7f, new Vector3(-128f, -64f, 168f), new Vector3(-136f, -70f, 176f), new Vector3(-148f, -72.5f, 178f), Grotto),
            // The deep descent from the floor loop to the skeleton chamber.
            new Tube(1.8f, 1.7f, new Vector3(-74f, -72f, 163f), new Vector3(-68f, -74.5f, 170f), new Vector3(-70f, -75.5f, 176f)),
            // The Tone Crate's channel: a long straight trench with a cross arm (slides stop at its ends).
            new Tube(2.4f, 2.4f, new Vector3(-104f, -64.6f, 132f), new Vector3(-80f, -64.6f, 132f)),
            new Tube(2.4f, 2.4f, new Vector3(-92f, -64.6f, 122f), new Vector3(-92f, -64.6f, 141f)),
            // Short dead ends in the floor.
            new Tube(1.4f, 1.1f, new Vector3(-112f, -64f, 186f), new Vector3(-116f, -69f, 194f), new Vector3(-124f, -70f, 196f)),
            new Tube(1.5f, 1.2f, new Vector3(-150f, -72f, 180f), new Vector3(-160f, -71f, 170f), new Vector3(-166f, -69f, 162f)),
            // Ceiling tunnels (only Glims and water get up there).
            new Tube(1.8f, 1.6f, new Vector3(-104f, -34f, 168f), new Vector3(-108f, -27f, 176f), new Vector3(-120f, -25f, 180f),
                new Vector3(-128f, -28f, 172f), new Vector3(-126f, -34f, 162f)),
            new Tube(1.5f, 1.2f, new Vector3(-86f, -34f, 186f), new Vector3(-84f, -28f, 196f), new Vector3(-90f, -24f, 204f)),
        };
    }

    /// <summary>The pit at the top of the spiral (world).</summary>
    public static Vector3 Mouth => mouth;

    /// <summary>Applies the caves to the world density (positive = rock).</summary>
    public static float Carve(Vector3 p, float density)
    {
        if (!Area.Contains(p)) return density;
        // Rock first (tower and pillars), then the tunnels cut through it.
        density = Mathf.Max(density, -Solid(p));
        return Mathf.Min(density, Distance(p));
    }

    /// <summary>Signed distance to the added rock (negative inside the tower or a pillar).</summary>
    private static float Solid(Vector3 p)
    {
        float d = float.MaxValue;
        // Only between the cavern floor and its ceiling; the ends sink into floor and ceiling rock.
        float span = Mathf.Max(InnerFloor - 2f - p.y, p.y + 30f);
        float tower = new Vector2(p.x - TowerCentre.x, p.z - TowerCentre.y).magnitude - TowerRadius
                      - (Mathf.PerlinNoise(p.y * 0.15f + 3f, Mathf.Atan2(p.z - TowerCentre.y, p.x - TowerCentre.x) * 2f) - 0.5f) * 2.4f;
        d = Mathf.Min(d, Mathf.Max(tower, span));
        foreach (Vector3 c in Pillars)
        {
            float flare = 1f + 0.0025f * (p.y + 48f) * (p.y + 48f); // thicker at floor and ceiling
            float pillar = new Vector2(p.x - c.x, p.z - c.y).magnitude - c.z * flare
                           - (Mathf.PerlinNoise(p.y * 0.2f + c.x, c.y * 0.1f) - 0.5f) * 1.6f;
            d = Mathf.Min(d, Mathf.Max(pillar, span));
        }
        return d;
    }

    /// <summary>Signed distance to the tunnel air (negative inside the tunnels).</summary>
    public static float Distance(Vector3 p)
    {
        float d = Mathf.Min(Room(p, Grotto, 5f, 3.2f), Room(p, Skeleton, SkeletonRadius, 3f));
        for (int i = 0; i < tubes.Length; i++)
        {
            if (!tubes[i].bounds.Contains(p)) continue;
            d = SmoothMin(d, TubeDistance(p, tubes[i]), 1.4f);
        }
        // Rough walls, never so rough that a tunnel pinches shut.
        float n = Mathf.PerlinNoise(p.x * 0.21f + p.y * 0.13f, p.z * 0.21f - 7f) + Mathf.PerlinNoise(p.y * 0.23f + 13f, p.x * 0.17f + p.z * 0.11f) - 1f;
        return d + n * 0.4f;
    }

    private static float Room(Vector3 p, Vector3 centre, float radius, float floorDrop)
    {
        float d = Vector3.Distance(p, centre) - radius;
        return Mathf.Max(d, centre.y - floorDrop - p.y);
    }

    private static float TubeDistance(Vector3 p, Tube tube, out float along)
    {
        float best = float.MaxValue, floorY = 0f;
        along = 0f;
        int segments = tube.points.Length - 1;
        for (int i = 0; i < segments; i++)
        {
            Vector3 a = tube.points[i], b = tube.points[i + 1], ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            float r = Mathf.Lerp(tube.radius, tube.endRadius, (i + t) / segments);
            Vector3 c = a + ab * t;
            float d = Vector3.Distance(p, c) - r;
            if (d < best) { best = d; floorY = c.y - r * 0.62f; along = (i + t) / segments; }
        }
        // Flat floor so the tunnels can be walked.
        return Mathf.Max(best, floorY - p.y);
    }

    private static float TubeDistance(Vector3 p, Tube tube) => TubeDistance(p, tube, out _);

    private static float SmoothMin(float a, float b, float k)
    {
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
        return Mathf.Lerp(b, a, h) - k * h * (1f - h);
    }

    // ---------------- Light ----------------

    /// <summary>
    /// How dark it is at a point: 0 above ground, growing on the way down the spiral, and 1 everywhere in the inner
    /// cavern (below its ceiling, under the whole map) and in the tunnels.
    /// </summary>
    public static float Darkness(Vector3 p)
    {
        float depth = Mathf.SmoothStep(0f, 1f, (-30f - p.y) / 8f);
        if (spiral.bounds.Contains(p) && TubeDistance(p, spiral, out float along) < 2.5f)
            depth = Mathf.Max(depth, Mathf.SmoothStep(0f, 1f, (along - 0.02f) / 0.12f));
        return depth;
    }

    /// <summary>0..1: the floor tunnels and the skeleton chamber (below the cavern floor), darker still.</summary>
    public static float Deep(Vector3 p) => Mathf.SmoothStep(0f, 1f, (InnerFloor - 3f - p.y) / 3f);
}
