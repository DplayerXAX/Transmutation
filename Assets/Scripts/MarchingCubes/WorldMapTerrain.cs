using UnityEngine;

/// <summary>
/// Shape of Scene_World, the combined map from Daniel's whiteboard (world metres, +z = forward from the spawn).
/// South: the spawn clearing in a placeholder forest, yoyo's Goo cave grafted in beside it (Scene_Goo).
/// Middle: the flower slope to the north-west, marcus's mushroom basin (Scene_FungalHollow) sunk to the east, a gentle
/// ramp between them; the crystal sky island (Scene_CisternBasin, grafted) floats above where they meet Flame Canyon.
/// North: Flame Canyon (Scene_FlameCanyon, grafted) runs east-west where the great crack used to be, the bone mother
/// asleep in her sinkhole beside it, and beyond it the terraced plateau of the tower ruins round the Hanging Spire.
/// Placeholder mountains ring it all.
/// Pure sampling, safe on terrain workers.
/// </summary>
public static class WorldMapTerrain
{
    // ---------------- Layout (world metres) ----------------

    public static readonly Vector2 Spawn = new Vector2(-174f, -105f);
    public const float SpawnHeight = -9.4f;
    public static Vector2 SpawnExit => Spawn + new Vector2(40f, 8f);
    /// <summary>
    /// Scene_Goo's exterior plain meets the world surface; its cave, canyon and mountain retain their donor heights.
    /// </summary>
    public static readonly Vector3 GooOffset = new Vector3(4f, 5f, -138f);
    public static readonly Vector3 GooLower = new Vector3(-55f, -44f, -100f), GooUpper = new Vector3(55f, 50f, 116f);
    public static bool InGooExterior(float x, float z) =>
        x > GooOffset.x + GooLower.x - 4f && x < GooOffset.x + GooUpper.x + 4f &&
        z > GooOffset.z + GooLower.z - 4f && z < GooOffset.z + GooUpper.z + 4f;
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
    /// <summary>The mushroom hollow's centre follows the donor floor; its sides are part of the world mesh.</summary>
    public const float MushroomRadius = 64f;
    public static readonly Vector3 MushroomOffset = new Vector3(125f, -36f, 40f);
    /// <summary>Scene_CisternBasin is grafted onto a floating island: donor origin moves here. It floats over the
    /// point where the flower field, the mushroom basin and Flame Canyon meet (moved from (0, 92, 60) on 2026-11-18).</summary>
    public static readonly Vector3 IslandOffset = new Vector3(32f, 92f, 100f);
    public const float IslandRadius = 88f;
    public static readonly Vector2 SpireSpot = new Vector2(0f, 250f);
    public const float SpireHoleRadius = 7f;
    /// <summary>The bone mother's sinkhole, on open ground beside Flame Canyon.</summary>
    public static readonly Vector2 MotherHole = new Vector2(2f, 112f);
    public const float MotherHoleRadius = 5.5f;
    public const float InnerGround = -65f;
    public static readonly Vector2 WorldSize = new Vector2(560f, 640f);
    public const float GridBottom = -84f, GridTop = 134f;

    // ---------------- Flame Canyon placement ----------------
    // Scene_FlameCanyon's terrain and life are grafted in (WorldMapFlameCanyon). Its origin lands on CanyonOrigin,
    // turned so its south-to-north length runs west to east, and its floor (donor y = 0) sits at CanyonFloor.

    public static readonly Vector2 CanyonOrigin = new Vector2(0f, 162f);
    public const float CanyonYaw = 90f;
    public const float CanyonFloor = -34f;
    public static Quaternion CanyonRotation => Quaternion.Euler(0f, CanyonYaw, 0f);
    public static Vector3 FromCanyon(Vector3 donor) => new Vector3(CanyonOrigin.x, CanyonFloor, CanyonOrigin.y) + CanyonRotation * donor;
    public static Vector3 ToCanyon(Vector3 world) => Quaternion.Inverse(CanyonRotation) * (world - new Vector3(CanyonOrigin.x, CanyonFloor, CanyonOrigin.y));

    // Footprint of the canyon in donor metres (same layout as FlameCanyonSetup): the main slot, the side canyon and
    // the chambers, each with its rim half-width (cut radius plus the widest terrace).
    private static readonly Vector2[] CanyonMain = {
        new Vector2(-6f, -108f), new Vector2(-28f, -80f), new Vector2(-20f, -46f), new Vector2(8f, -6f),
        new Vector2(34f, 30f), new Vector2(24f, 62f), new Vector2(-4f, 84f), new Vector2(-12f, 108f) };
    private static readonly Vector2[] CanyonDen = { new Vector2(8f, -6f), new Vector2(-16f, 6f), new Vector2(-34f, 14f) };
    private static readonly Vector3[] CanyonChambers = {
        new Vector3(-6f, -108f, 12f), new Vector3(8f, -6f, 19f), new Vector3(34f, 30f, 11f), new Vector3(-12f, 108f, 17f), new Vector3(-34f, 14f, 10f) };
    private const float CanyonWiden = 6.6f;

    /// <summary>Horizontal distance from (x, z) to the canyon's rim: negative over the canyon, positive outside.</summary>
    public static float CanyonDistance(Vector2 p)
    {
        Vector3 local = ToCanyon(new Vector3(p.x, 0f, p.y));
        Vector2 q = new Vector2(local.x, local.z);
        float best = float.MaxValue;
        for (int i = 1; i < CanyonMain.Length; i++) best = Mathf.Min(best, Distance(q, CanyonMain[i - 1], CanyonMain[i], out _) - 5f - CanyonWiden);
        for (int i = 1; i < CanyonDen.Length; i++) best = Mathf.Min(best, Distance(q, CanyonDen[i - 1], CanyonDen[i], out _) - 4.5f - CanyonWiden);
        foreach (Vector3 c in CanyonChambers) best = Mathf.Min(best, Vector2.Distance(q, new Vector2(c.x, c.y)) - c.z - CanyonWiden);
        return best;
    }

    // Walked routes that stay smooth: spawn -> flowers, spawn -> mushroom ramp -> basin floor, middle -> crack rim.
    private static readonly Vector3[] RampRoute = {
        new Vector3(5f, 6f, -20f), new Vector3(28f, 6f, 22f), new Vector3(42f, 0f, 36f), new Vector3(52f, -8f, 40f) };
    private static readonly Vector3[] FlowerRoute = {
        new Vector3(-10f, 5f, -40f), new Vector3(-28f, 7f, -5f), new Vector3(-45f, 11f, 30f), new Vector3(-70f, 18f, 62f) };
    // ---------------- Surface ----------------

    /// <summary>Surface height of a column of the generated ground (Flame Canyon is cut into it by saved edits).</summary>
    public static float Height(float x, float z) => BaseHeight(x, z);

    /// <summary>Generated ground height.</summary>
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

        // A broad west slope leaves the flower field, then breaks into a deep mushroom hollow.
        // The floor and walls are one continuous marching-cubes surface, with no imported box walls.
        Vector2 m = new Vector2(x - MushroomCentre.x, z - MushroomCentre.y);
        float angle = Mathf.Atan2(m.y, m.x);
        float wobble = 1f + 0.07f * Mathf.Sin(angle * 3f + 0.6f) + 0.05f * Mathf.Sin(angle * 7f - 1.1f);
        float r = m.magnitude / wobble;
        float westness = Mathf.Clamp01(-Mathf.Cos(angle) * 1.3f - 0.3f);
        float rim = Mathf.Lerp(13f, 7f, westness) + (Mathf.PerlinNoise(angle * 2f + 11f, 3f) - 0.5f) * 4f;
        float floor = FungalFloor(m.x, m.y) + MushroomOffset.y - 0.25f;
        float innerEdge = 62f;
        float breakAt = Mathf.Lerp(71f, 80f, westness);
        float rimAt = Mathf.Lerp(91f, 111f, westness);
        float upper = Mathf.Lerp(-9f, rim, Mathf.SmoothStep(0f, 1f, (r - breakAt) / (rimAt - breakAt)));
        float bowl = r <= innerEdge ? floor : r < breakAt
            ? Mathf.Lerp(floor, -9f, Mathf.SmoothStep(0f, 1f, (r - innerEdge) / (breakAt - innerEdge))) : upper;
        float basin = 1f - Mathf.SmoothStep(0f, 1f, (r - rimAt) / 12f);
        h = Mathf.Lerp(h, bowl, basin);

        h = Path(h, x, z, FlowerRoute, 5f, 7f);
        // The former narrow mushroom ramp is now the broad west flank of the hollow.
        // The forest start is a carved room beneath this intact surface, not a flat clearing.

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

    /// <summary>Applied after the base crust / inner world density: the spawn chamber and the dark tunnels.</summary>
    public static float ShapeDensity(Vector3 p, float density)
    {
        density = SpawnCave(p, density);
        // Dark tunnels behind the spawn chamber (DarkTunnelShape).
        return DarkTunnelShape.Carve(p, density);
    }

    /// <summary>A small forest chamber, a left bend, then a shroom ledge and a sloped daylight exit.</summary>
    private static float SpawnCave(Vector3 p, float density)
    {
        Vector2 q = new Vector2(p.x - Spawn.x, p.z - Spawn.y);
        if (q.sqrMagnitude > 47f * 47f) return density;
        float rough = (Mathf.PerlinNoise(p.x * 0.19f + 31f, p.z * 0.18f + 19f) - 0.5f) * 1.1f;
        float room = (new Vector2((q.x + 1f) / 11f, q.y / 9f).magnitude - 1f) * 9f + rough;
        room = Mathf.Max(room, Mathf.Max(-10.5f - p.y, p.y - 1.2f - rough));
        float turn = Mathf.Min(Distance(q, new Vector2(6f, -1f), new Vector2(13f, 8f), out _),
            Distance(q, new Vector2(13f, 8f), new Vector2(27f, 8f), out _));
        float corridor = Mathf.Max(turn - 3.8f + rough * 0.35f,
            Mathf.Max(-10.5f - p.y, p.y - 2.8f));
        // The first rise (lowered 1.5 m on 2026-11-19 so the mushroom jump clears it). The remaining height unfolds
        // over a long walkable ramp, open to daylight beyond the turn.
        float exitFloor = Mathf.Lerp(-1.8f, 6f, Mathf.SmoothStep(0f, 1f, (q.x - 27f) / 13f));
        float exit = Mathf.Max(Mathf.Abs(q.y - 8f) - 5.5f,
            Mathf.Max(exitFloor - p.y, Mathf.Max(24f - q.x, q.x - 40f)));
        return Mathf.Min(density, Mathf.Min(room, Mathf.Min(corridor, exit)));
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
