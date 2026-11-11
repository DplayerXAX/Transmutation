using UnityEngine;

/// <summary>
/// Shape of Scene_World, the combined map from Daniel's whiteboard (world metres, +z = forward from the spawn).
/// South: the spawn clearing in a placeholder forest, yoyo's Goo cave grafted in beside it (Scene_Goo).
/// Middle: the flower slope to the north-west, marcus's mushroom basin (Scene_FungalHollow) sunk to the east, a gentle
/// ramp between them; the crystal sky island (Scene_CisternBasin, grafted) floats above them.
/// North: a great east-west crack through the crust into the inner world, the bone mother wedged on a ledge in it,
/// and beyond it the terraced plateau of the tower ruins round the Hanging Spire. Placeholder mountains ring it all.
/// Pure sampling, safe on terrain workers.
/// </summary>
public static class WorldMapTerrain
{
    // ---------------- Layout (world metres) ----------------

    public static readonly Vector2 Spawn = new Vector2(-10f, -55f);
    public const float SpawnHeight = 4f;
    /// <summary>
    /// Scene_Goo is grafted underground with this offset; its box (donor space) is GooLower..GooUpper. The forest
    /// floor stays on top: the cave is roofed over below GooCeiling (donor y), and only its crater opens as the mouth.
    /// </summary>
    public static readonly Vector3 GooOffset = new Vector3(4f, -34f, -138f);
    public static readonly Vector3 GooLower = new Vector3(-42f, -30f, -95f), GooUpper = new Vector3(42f, 42f, 84f);
    /// <summary>The Goo crater (donor x, z): the one opening of the cave onto the forest floor.</summary>
    public static readonly Vector2 GooMouth = new Vector2(8f, 66f);
    public const float GooMouthRadius = 10f;
    public static Vector2 CaveMouth => new Vector2(GooOffset.x + GooMouth.x, GooOffset.z + GooMouth.y);
    /// <summary>Cave roof height (donor space) at a donor column: organic, raised over the crater mouth.</summary>
    public static float GooCeiling(float x, float z)
    {
        float c = 13f + (Mathf.PerlinNoise(x * 0.06f + 3f, z * 0.06f + 8f) - 0.5f) * 8f + (Mathf.PerlinNoise(x * 0.17f, z * 0.17f + 5f) - 0.5f) * 3f;
        float mouth = 1f - Mathf.SmoothStep(0f, 1f, (Vector2.Distance(new Vector2(x, z), GooMouth) - GooMouthRadius) / 10f);
        return Mathf.Lerp(c, 60f, mouth);
    }
    public static readonly Vector2 FlowerCentre = new Vector2(-55f, 45f);
    public static readonly Vector2 MushroomCentre = new Vector2(125f, 40f);
    /// <summary>Terrain under Scene_FungalHollow's floor mesh; the bowl wall starts at its edge (r 74, mesh edge ~ -8).</summary>
    public const float MushroomFloor = -22f, MushroomRadius = 74f, MushroomLip = -9f;
    /// <summary>Scene_FungalHollow content is moved by this (its cavern floor sits just above MushroomFloor).</summary>
    public static readonly Vector3 MushroomOffset = new Vector3(125f, -14f, 40f);
    /// <summary>Scene_CisternBasin is grafted onto a floating island: donor origin moves here.</summary>
    public static readonly Vector3 IslandOffset = new Vector3(0f, 88f, 60f);
    public const float IslandRadius = 98f;
    public static readonly Vector2 SpireSpot = new Vector2(0f, 250f);
    public const float SpireHoleRadius = 7f;
    public static readonly Vector2 MotherHole = new Vector2(2f, 161f);
    public const float MotherHoleRadius = 5.5f;
    /// <summary>Where she is jammed: rubble fills the crack wall to wall between these x, its top at LedgeTop.</summary>
    public static readonly Vector2 JamRange = new Vector2(-24f, 58f);
    public const float LedgeTop = -16f;
    public const float InnerGround = -65f, CrackFloor = -74f;
    public static readonly Vector2 WorldSize = new Vector2(560f, 640f);
    public const float GridBottom = -84f, GridTop = 134f;

    // The crack runs east-west across the whole map, into the mountains at both ends.
    private static readonly Vector2[] CrackKeys = {
        new Vector2(-262f, 141f), new Vector2(-205f, 152f), new Vector2(-150f, 146f), new Vector2(-95f, 158f),
        new Vector2(-45f, 155f), new Vector2(-14f, 161f), new Vector2(22f, 161f), new Vector2(60f, 158f),
        new Vector2(105f, 168f), new Vector2(155f, 163f), new Vector2(205f, 174f), new Vector2(262f, 168f) };
    private static readonly float[] CrackLength = Lengths(CrackKeys);

    // Walked routes that stay smooth: spawn -> flowers, spawn -> mushroom ramp -> basin floor, middle -> crack rim.
    private static readonly Vector3[] RampRoute = {
        new Vector3(5f, 6f, -20f), new Vector3(28f, 6f, 22f), new Vector3(42f, 0f, 36f), new Vector3(52f, -8f, 40f) };
    private static readonly Vector3[] FlowerRoute = {
        new Vector3(-10f, 5f, -40f), new Vector3(-28f, 7f, -5f), new Vector3(-45f, 11f, 30f), new Vector3(-70f, 18f, 62f) };
    private static float motherT = -1f;
    /// <summary>Crack parameter at the mother's hole, where the crack pinches round her.</summary>
    private static float MotherT { get { if (motherT < 0f) CrackDistance(MotherHole, out motherT); return motherT; } }

    private static float[] Lengths(Vector2[] keys)
    {
        var length = new float[keys.Length];
        for (int i = 1; i < keys.Length; i++) length[i] = length[i - 1] + Vector2.Distance(keys[i - 1], keys[i]);
        return length;
    }

    // ---------------- Surface ----------------

    /// <summary>Surface height of a column (the crack floor or the mother's ledge inside the crack).</summary>
    public static float Height(float x, float z)
    {
        float h = BaseHeight(x, z);
        float d = CrackDistance(new Vector2(x, z), out float t), w = CrackHalfWidth(t);
        float inside = 1f - Mathf.SmoothStep(0f, 1f, (d - w + 0.9f) / 1.7f);
        if (inside <= 0f) return h;
        return Mathf.Lerp(h, Mathf.Max(CrackFloor, JamTop(x, z)), inside);
    }

    /// <summary>Ground before the crack is cut.</summary>
    public static float BaseHeight(float x, float z)
    {
        // Soft rolling ground everywhere: warped, so it never lines up.
        float wx = x + (Mathf.PerlinNoise(x * 0.017f + 3.1f, z * 0.017f + 1.7f) - 0.5f) * 40f;
        float wz = z + (Mathf.PerlinNoise(x * 0.017f + 8.4f, z * 0.017f + 6.2f) - 0.5f) * 40f;
        float h = 6f + (Mathf.PerlinNoise(wx * 0.02f + 41f, wz * 0.02f + 13f) - 0.5f) * 7f
                     + (Mathf.PerlinNoise(wx * 0.07f + 7f, wz * 0.07f + 29f) - 0.5f) * 2f;

        // North of the crack the ground rises into the ruin plateau.
        float north = Mathf.SmoothStep(0f, 1f, (z - 150f) / 40f);
        h = Mathf.Lerp(h, 15f + (Mathf.PerlinNoise(wx * 0.03f + 5f, wz * 0.03f + 2f) - 0.5f) * 4f, north);
        h = Ruins(h, x, z);

        // The flower slope climbs to the north-west in long swells.
        // It reaches east right up to the mushroom basin's low west lip, so the two read as one sweep.
        float flower = 1f - Mathf.SmoothStep(0f, 1f, (Oval(wx, wz, FlowerCentre.x, FlowerCentre.y, 95f, 85f) - 0.7f) / 0.4f);
        if (flower > 0f)
        {
            Vector2 local = new Vector2((x - FlowerCentre.x) / 95f, (z - FlowerCentre.y) / 85f);
            float climb = Mathf.SmoothStep(0f, 1f, (Vector2.Dot(local, new Vector2(-0.72f, 0.69f)) + 0.75f) / 1.6f);
            float swells = Mathf.Sin(wx * 0.07f + wz * 0.03f) * 2.6f + Mathf.Cos(wz * 0.06f - wx * 0.025f) * 2.2f
                         + (Mathf.PerlinNoise(wx * 0.045f + 61f, wz * 0.045f + 7f) - 0.5f) * 8f;
            h = Mathf.Lerp(h, 7f + climb * 30f + swells, flower);
        }

        // The mushroom basin: a sunken bowl, its west lip low where the ramp comes down.
        Vector2 m = new Vector2(x - MushroomCentre.x, z - MushroomCentre.y);
        float angle = Mathf.Atan2(m.y, m.x);
        float wobble = 1f + 0.07f * Mathf.Sin(angle * 3f + 0.6f) + 0.05f * Mathf.Sin(angle * 7f - 1.1f);
        float r = m.magnitude / wobble;
        float westness = Mathf.Clamp01(-Mathf.Cos(angle) * 1.3f - 0.3f);
        float rim = Mathf.Lerp(16f, 7f, westness) + (Mathf.PerlinNoise(angle * 2f + 11f, 3f) - 0.5f) * 6f;
        float bowl = r < MushroomRadius ? MushroomFloor
            : Mathf.Lerp(MushroomLip, rim, Mathf.SmoothStep(0f, 1f, (r - MushroomRadius) / 18f));
        // Just under the cavern floor mesh, so its square edges meet the ground instead of hanging over the ramp.
        if (Mathf.Abs(m.x) < 75f && Mathf.Abs(m.y) < 75f) bowl = Mathf.Max(bowl, FungalFloor(m.x, m.y) + MushroomOffset.y - 1.2f);
        float basin = 1f - Mathf.SmoothStep(0f, 1f, (r - MushroomRadius - 18f) / 22f);
        h = Mathf.Lerp(h, bowl, basin);

        // The cave's crater mouth opens in a shallow hollow of the forest floor.
        h -= 2.5f * Mathf.Exp(-(new Vector2(x, z) - CaveMouth).sqrMagnitude / 500f);

        h = Path(h, x, z, FlowerRoute, 5f, 7f);
        h = Path(h, x, z, RampRoute, 6f, 9f);
        h = Clearing(h, x, z, Spawn.x, Spawn.y, 15f, SpawnHeight);

        // The ground falls in gently towards the crack rim, so it reads from a distance.
        float d = CrackDistance(new Vector2(x, z), out float t);
        h -= 3f * (1f - Mathf.SmoothStep(0f, 1f, (d - CrackHalfWidth(t)) / 22f));

        return Mountains(h, x, z);
    }

    /// <summary>Scene_FungalHollow's floor height (local metres), the same formula as FungalHollowSetup.H.</summary>
    private static float FungalFloor(float x, float z)
    {
        float r = new Vector2(x, z).magnitude;
        float swell = 1.1f * (Mathf.Sin(x * 0.09f) + Mathf.Cos(z * 0.075f)) + 2f * (Mathf.PerlinNoise(x * 0.04f + 10f, z * 0.04f + 10f) - 0.5f);
        float bog = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(7f, 17f, (new Vector2(x, z) - new Vector2(-22f, -12f)).magnitude));
        return swell - bog * 6f + Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(50f, 66f, r)) * 6f;
    }

    /// <summary>Terraced plazas round the spire: the ground is cut into flat built levels with soft ramps between.</summary>
    private static float Ruins(float h, float x, float z)
    {
        float plateau = 1f - Mathf.SmoothStep(0f, 1f, (Oval(x, z, SpireSpot.x, SpireSpot.y - 10f, 165f, 80f) - 0.75f) / 0.35f);
        if (plateau <= 0f) return h;
        // Quantised height = built terraces; a little noise on the step line keeps them from looking machined.
        float level = 14f + Mathf.Max(0f, 1f - Oval(x, z, SpireSpot.x, SpireSpot.y, 120f, 60f)) * 16f;
        float step = 3.5f;
        float jitter = (Mathf.PerlinNoise(x * 0.05f + 91f, z * 0.05f + 17f) - 0.5f) * 2.2f;
        float q = level + jitter;
        float stepped = Mathf.Floor(q / step) * step;
        float frac = q / step - Mathf.Floor(q / step);
        stepped += Mathf.SmoothStep(0f, 1f, (frac - 0.85f) / 0.15f) * step;
        h = Mathf.Lerp(h, stepped, plateau);
        // A broad plaza at the spire's foot.
        return Clearing(h, x, z, SpireSpot.x, SpireSpot.y, 30f, 31f);
    }

    /// <summary>Placeholder mountains round an organic, bean-shaped valley.</summary>
    private static float Mountains(float h, float x, float z)
    {
        Vector2 c = new Vector2(0f, 15f);
        Vector2 v = new Vector2(x - c.x, z - c.y);
        float a = Mathf.Atan2(v.y, v.x);
        Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        float wobble = 1f + (Mathf.PerlinNoise(dir.x * 1.6f + 7f, dir.y * 1.6f + 3f) - 0.5f) * 0.22f + Mathf.Sin(a * 5f + 1.3f) * 0.03f;
        float rx = 248f * wobble, rz = 278f * wobble;
        float e = new Vector2(v.x / rx, v.y / rz).magnitude;
        float rise = Mathf.SmoothStep(0f, 1f, (e - 0.93f) / 0.17f);
        if (rise <= 0f) return h;
        float wx = x + (Mathf.PerlinNoise(x * 0.01f, z * 0.01f) - 0.5f) * 60f, wz = z + (Mathf.PerlinNoise(x * 0.01f + 40f, z * 0.01f + 9f) - 0.5f) * 60f;
        float ridge = 1f - Mathf.Abs(Mathf.PerlinNoise(wx * 0.012f + 13f, wz * 0.012f + 77f) * 2f - 1f);
        float peaks = 50f + ridge * ridge * 60f + (Mathf.PerlinNoise(wx * 0.04f + 2f, wz * 0.04f + 5f) - 0.5f) * 14f;
        return Mathf.Lerp(h, Mathf.Max(h, peaks), rise);
    }

    // ---------------- The crack ----------------

    public static float CrackDistance(Vector2 p, out float t)
    {
        float best = float.MaxValue; t = 0f;
        float total = CrackLength[CrackKeys.Length - 1];
        for (int i = 1; i < CrackKeys.Length; i++)
        {
            float d = Distance(p, CrackKeys[i - 1], CrackKeys[i], out float u);
            if (d < best) { best = d; t = Mathf.Lerp(CrackLength[i - 1], CrackLength[i], u) / total; }
        }
        return best;
    }

    public static Vector2 CrackPoint(float t)
    {
        float at = Mathf.Clamp01(t) * CrackLength[CrackKeys.Length - 1];
        int i = 1; while (i < CrackKeys.Length - 1 && CrackLength[i] < at) i++;
        return Vector2.Lerp(CrackKeys[i - 1], CrackKeys[i], Mathf.InverseLerp(CrackLength[i - 1], CrackLength[i], at));
    }

    /// <summary>About 26 m across, pinching to about 20 m where the mother is jammed, narrowing to slots in the mountains.</summary>
    public static float CrackHalfWidth(float t)
    {
        float ends = Mathf.SmoothStep(0f, 1f, t / 0.12f) * Mathf.SmoothStep(0f, 1f, (1f - t) / 0.12f);
        float wave = Mathf.Sin(t * 23f + 0.4f) * 1.8f + Mathf.Sin(t * 57f) * 0.9f;
        return Mathf.Lerp(4f, 13f + wave, ends) - 3f * Mathf.Exp(-Mathf.Pow((t - MotherT) / 0.05f, 2f));
    }

    /// <summary>Top of the rubble jam that holds the mother (below the crack floor away from her).</summary>
    public static float JamTop(float x, float z)
    {
        float inside = Mathf.SmoothStep(0f, 1f, (x - JamRange.x) / 12f) * Mathf.SmoothStep(0f, 1f, (JamRange.y - x) / 12f);
        float rubble = (Mathf.PerlinNoise(x * 0.18f + 4f, z * 0.18f + 9f) - 0.5f) * 2.2f;
        return Mathf.Lerp(CrackFloor - 6f, LedgeTop + rubble * inside, Mathf.Pow(inside, 0.35f));
    }

    public static bool IsOpenCrackSurface(Vector3 p)
    {
        float d = CrackDistance(new Vector2(p.x, p.z), out float t);
        return d < CrackHalfWidth(t) + 6f && p.y > -34f && p.y < BaseHeight(p.x, p.z) - 1.5f;
    }

    /// <summary>Applied after the base crust / inner world density: cuts the crack and keeps the mother's ledge.</summary>
    public static float ShapeDensity(Vector3 p, float density)
    {
        float d = CrackDistance(new Vector2(p.x, p.z), out float t), w = CrackHalfWidth(t);
        if (d < w + 6f)
        {
            float surface = BaseHeight(p.x, p.z);
            float crust = Mathf.Min(surface - p.y, p.y + 32f + Mathf.Max(0f, surface) * 0.15f);
            float inner = InnerGround + Mathf.PerlinNoise(p.x * 0.025f + 71f, p.z * 0.025f + 31f) * 2f - p.y;
            // Strata ledges and rough breaks make the walls three dimensional.
            float ledges = Mathf.Sin(p.y * 0.6f + t * 31f) * 0.8f + Mathf.Sin(p.y * 1.37f - t * 47f) * 0.3f;
            float rough = (Mathf.PerlinNoise(t * 41f + 21f, p.y * 0.17f + 39f) - 0.5f) * 1.8f;
            float cut = Mathf.Max(d - w - ledges - rough, CrackFloor - p.y);
            density = Mathf.Max(Mathf.Min(crust, cut), inner);
            // Where the mother is jammed, fallen rubble fills the crack from wall to wall under her body.
            density = Mathf.Max(density, JamTop(p.x, p.z) - p.y);
        }
        return density;
    }

    // ---------------- Helpers ----------------

    public static float Distance(Vector2 p, Vector2 a, Vector2 b, out float t)
    {
        Vector2 v = b - a; t = Mathf.Clamp01(Vector2.Dot(p - a, v) / Mathf.Max(0.001f, v.sqrMagnitude));
        return Vector2.Distance(p, a + v * t);
    }
    private static float Oval(float x, float z, float cx, float cz, float rx, float rz) => new Vector2((x - cx) / rx, (z - cz) / rz).magnitude;
    private static float Clearing(float h, float x, float z, float cx, float cz, float radius, float y) =>
        Mathf.Lerp(h, y, 1f - Mathf.SmoothStep(0f, 1f, (Vector2.Distance(new Vector2(x, z), new Vector2(cx, cz)) - radius * 0.7f) / (radius * 0.3f)));
    private static float Path(float h, float x, float z, Vector3[] nodes, float width, float shoulder)
    {
        float nearest = float.MaxValue, y = h;
        for (int i = 1; i < nodes.Length; i++)
        {
            float d = Distance(new Vector2(x, z), new Vector2(nodes[i - 1].x, nodes[i - 1].z), new Vector2(nodes[i].x, nodes[i].z), out float t);
            if (d >= nearest) continue;
            nearest = d; y = Mathf.Lerp(nodes[i - 1].y, nodes[i].y, t);
        }
        return Mathf.Lerp(h, y, 1f - Mathf.SmoothStep(0f, 1f, (nearest - width) / shoulder));
    }
}
