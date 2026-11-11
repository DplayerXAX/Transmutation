using UnityEngine;

/// <summary>
/// Metres in Scene_LeafMap: the petiole is south, the tip north. Pure sampling, safe on terrain workers.
/// The leaf is authored in "design" coordinates and grown horizontally by Scale around the tutorial exit,
/// so the grafted tutorial cave stays put. Heights are not scaled. Every public method takes and returns
/// world coordinates.
/// </summary>
public static class LeafMapTerrain
{
    public static readonly Vector2 Pivot = new Vector2(0f, -97f);
    private static float scale = 1f;
    private static Vector3[] mainRoute, westRoute, eastRoute, northRoute;

    /// <summary>Horizontal growth of the leaf. Set from the saved terrain layout (leafScale).</summary>
    public static float Scale
    {
        get => scale;
        set
        {
            if (value <= 0f) value = 1f;
            if (Mathf.Approximately(value, scale)) return;
            scale = value;
            mainRoute = westRoute = eastRoute = northRoute = null;
            mainDesign = Lifted(MainDesign);
        }
    }

    /// <summary>The grown leaf lifts the tutorial cave and its stem into a highland above the flower basin.</summary>
    public static float CaveLift => scale > 1f ? 18f : 0f;
    private const float ShellThickness = 7f;
    private static bool Grown => scale > 1f;
    // The grown leaf's long east-west rift is retired: the mother now dives into a plain sinkhole shaft, as in
    // Scene_Daniel. Set to true to bring the old rift back.
    private const bool GrownRift = false;
    /// <summary>True while the leaf has its long rift (the first layout, or GrownRift).</summary>
    public static bool HasRift => !Grown || GrownRift;

    public static Vector2 ToWorld(Vector2 d) => Pivot + (d - Pivot) * scale;
    public static Vector2 ToDesign(Vector2 w) => Pivot + (w - Pivot) / scale;
    public static Vector3 ToWorld(Vector3 d) { Vector2 p = ToWorld(new Vector2(d.x, d.z)); return new Vector3(p.x, d.y, p.y); }

    /// <summary>Grid size that holds the whole leaf at a given scale (276 x 444 at scale 1).</summary>
    public static Vector2 WorldSizeFor(float s) => new Vector2(
        Mathf.Ceil(124f * s + 14f) * 2f,
        Mathf.Ceil(Mathf.Max(Mathf.Abs(Pivot.y + 269f * s), Mathf.Abs(Pivot.y - 121f * s)) + 4f) * 2f);

    /// <summary>Reads leafScale from a saved ProceduralWorld layout (1 when missing).</summary>
    public static float ReadScale(string layoutJson)
    {
        if (string.IsNullOrEmpty(layoutJson)) return 1f;
        var saved = JsonUtility.FromJson<ScaleOnly>(layoutJson);
        return saved != null && saved.leafScale > 0f ? saved.leafScale : 1f;
    }
    [System.Serializable] private sealed class ScaleOnly { public float leafScale; }

    // The bone mother rests on the east rim of the flower basin; her young sleep on the flower side.
    public static readonly Vector2 MotherDesign = new Vector2(33f, -38f);
    private static readonly Vector2[] ChildRestDesign = { new Vector2(21f, -42f), new Vector2(25f, -52f), new Vector2(37f, -51f) };
    public static Vector3 MotherSpot => Ground(MotherDesign);
    public static Vector3 ChildRest(int i) => Ground(ChildRestDesign[Mathf.Clamp(i, 0, ChildRestDesign.Length - 1)]) + Vector3.up * 0.3f;
    public static Vector3 SpireSpot => Ground(new Vector2(0f, 10f));
    /// <summary>The fire vent east of the mother, where the rift used to open (grown leaf without the rift).</summary>
    public static Vector3 VentSpot => Ground(new Vector2(60f, -31f));
    /// <summary>Knolls in design metres: x, z, radius, height (grown leaf only).</summary>
    public static readonly Vector4[] KnollDesign = {
        new Vector4(-30f, 50f, 14f, 7f), new Vector4(45f, 85f, 16f, 9f), new Vector4(-40f, 92f, 12f, 6f),
        new Vector4(10f, 140f, 12f, 6f), new Vector4(68f, 40f, 14f, 7f), new Vector4(-70f, 40f, 12f, 6f) };
    /// <summary>Bubble cascade mesas in design metres: x, z, radius, top height (grown leaf only).</summary>
    public static readonly Vector4[] BubbleTierDesign = {
        new Vector4(-24f,-67f,4.6f,10f), new Vector4(-34f,-58f,4.2f,17f), new Vector4(-41f,-47f,4.4f,24f),
        new Vector4(-47f,-35f,4.0f,31f), new Vector4(-52f,-24f,4.4f,38f) };
    private static Vector3 Ground(Vector2 design) { Vector2 w = ToWorld(design); return new Vector3(w.x, DesignHeight(design.x, design.y), w.y); }

    private static readonly Vector3[] MainDesign = {
        new Vector3(0, 10, -94), new Vector3(-2, 7, -84), new Vector3(-8, 4, -69), new Vector3(-8, 4, -59),
        new Vector3(-3, 5, -36), new Vector3(0, 7, -14), new Vector3(0, 7, -3)
    };
    private static readonly Vector3[] WestDesign = {
        new Vector3(-17,4,-48),new Vector3(-30,7,-34),new Vector3(-40,13,-21),
        new Vector3(-43,20,-7),new Vector3(-49,28,7),new Vector3(-57,29,18),new Vector3(-42,29,22),
        new Vector3(-28,17,28),new Vector3(-18,8,25)
    };
    private static readonly Vector3[] EastDesign = {
        new Vector3(0,5,-36),new Vector3(19,6,-24),new Vector3(27,7,-16),
        new Vector3(25,8,-3),new Vector3(29,10,12),new Vector3(40,13,21)
    };
    private static readonly Vector3[] NorthDesign = {
        new Vector3(-18,8,25),new Vector3(-4,10,33),new Vector3(3,12,47),
        new Vector3(23,17,55),new Vector3(33,18,65),new Vector3(7,17,82),new Vector3(9,22,112),new Vector3(0,24,130)
    };
    // The walk from the exit ramps down the highland edge into the basin.
    private static Vector3[] mainDesign = MainDesign;
    private static Vector3[] Lifted(Vector3[] route)
    {
        var result = (Vector3[])route.Clone();
        result[0].y += CaveLift; result[1].y += CaveLift * 0.55f;
        return result;
    }
    public static Vector3[] MainRoute => mainRoute ??= Scaled(mainDesign);
    public static Vector3[] WestRoute => westRoute ??= Scaled(WestDesign);
    public static Vector3[] EastRoute => eastRoute ??= Scaled(EastDesign);
    public static Vector3[] NorthRoute => northRoute ??= Scaled(NorthDesign);
    private static Vector3[] Scaled(Vector3[] route)
    {
        var result = new Vector3[route.Length];
        for (int i = 0; i < route.Length; i++) result[i] = ToWorld(route[i]);
        return result;
    }

    public static float HalfWidth(float z)
    {
        float t = Mathf.Clamp01((z + 113f) / 284f);
        float body = 124f * Mathf.Pow(Mathf.Max(0,Mathf.Sin(t * Mathf.PI)), 0.78f);
        body *= 0.97f + 0.03f * Mathf.Sin(t * 22f);
        // A short, narrow stem joins the tutorial cavern to the leaf blade.
        float stem = z >= -218f && z <= -95f ? 41f*Mathf.Pow(Mathf.Max(0,Mathf.Sin((z+218f)/123f*Mathf.PI)),0.55f) : 0f;
        return Mathf.Max(body, stem);
    }

    public static float Footprint(float x, float z) { Vector2 d = ToDesign(new Vector2(x, z)); return DesignFootprint(d.x, d.y) * scale; }
    private static float DesignFootprint(float x, float z) => Mathf.Min(HalfWidth(z) - Mathf.Abs(x - 2.5f * Mathf.Sin(z * 0.018f)), Mathf.Min(z + 218f, 172f - z));

    public static float Height(float x, float z) { Vector2 d = ToDesign(new Vector2(x, z)); return DesignHeight(d.x, d.y); }
    private static float DesignHeight(float x, float z)
    {
        float h=BaseHeight(x,z);
        float d=DesignRiftDistance(new Vector2(x,z),out float t), radius=DesignRiftHalfWidth(t);
        return Mathf.Lerp(h,RiftFloor(t),1f-Mathf.SmoothStep(0f,1f,(d-radius+0.9f)/1.7f));
    }

    public static Vector3 RiftPoint(float t) => ToWorld(DesignRiftPoint(t));
    // The grown leaf's rift opens out of the mother's resting crack and runs east across the leaf,
    // cut right through the crust into the inner world, like the sinkholes of Scene_Daniel.
    private static readonly Vector2[] RiftKeys = {
        MotherDesign, new Vector2(44f,-36.5f), new Vector2(56f,-35f), new Vector2(68f,-32.5f),
        new Vector2(79f,-30f), new Vector2(89f,-28f) };
    private static readonly float[] RiftLength = Lengths(RiftKeys);
    private static float[] Lengths(Vector2[] keys)
    {
        var length = new float[keys.Length];
        for (int i = 1; i < keys.Length; i++) length[i] = length[i-1] + Vector2.Distance(keys[i-1], keys[i]);
        return length;
    }
    private static Vector3 DesignRiftPoint(float t)
    {
        t=Mathf.Clamp01(t);
        if (Grown)
        {
            float at = t * RiftLength[RiftKeys.Length-1];
            int i = 1; while (i < RiftKeys.Length-1 && RiftLength[i] < at) i++;
            Vector2 p = Vector2.Lerp(RiftKeys[i-1], RiftKeys[i], Mathf.InverseLerp(RiftLength[i-1], RiftLength[i], at));
            return new Vector3(p.x, 0, p.y);
        }
        return new Vector3(Mathf.Lerp(18,80,t),0,34f+Mathf.Sin(t*Mathf.PI*1.8f)*2.2f);
    }
    public static Vector3 RiftSide(float t)
    {
        Vector3 forward=(DesignRiftPoint(Mathf.Min(1,t+0.01f))-DesignRiftPoint(Mathf.Max(0,t-0.01f))).normalized;
        return Vector3.Cross(Vector3.up,forward).normalized;
    }
    // Shallow and narrow where it holds the mother, then opening into the deep canyon.
    public static float RiftFloor(float t)
    {
        float floor = -26f + Mathf.Sin(t*9f)*1.4f;
        return Grown ? Mathf.Lerp(-1f, -72f, Mathf.SmoothStep(0f, 1f, t / 0.25f)) : floor;
    }
    public static float RiftHalfWidth(float t)=>DesignRiftHalfWidth(t)*scale;
    private static float DesignRiftHalfWidth(float t)
    {
        if (!Grown) return 8.6f+Mathf.Sin(t*7f+0.6f)*1.2f;
        float open = Mathf.SmoothStep(0f, 1f, t / 0.2f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 0.9f) / 0.1f) * 0.55f);
        // The head is a wider nest round the mother, so she has room to rise out of it.
        return Mathf.Lerp(8.5f, 13f + Mathf.Sin(t*7f+0.6f)*1.6f, open);
    }
    public static float RiftDistance(Vector2 p,out float t)=>DesignRiftDistance(ToDesign(p),out t)*scale;
    private static float DesignRiftDistance(Vector2 p,out float t)
    {
        if (!HasRift) { t = 0f; return 1e6f; }
        if (Grown)
        {
            float best = float.MaxValue; t = 0f;
            for (int i = 1; i < RiftKeys.Length; i++)
            {
                float d = Distance(p, RiftKeys[i-1], RiftKeys[i], out float u);
                if (d < best) { best = d; t = Mathf.Lerp(RiftLength[i-1], RiftLength[i], u) / RiftLength[RiftKeys.Length-1]; }
            }
            return best;
        }
        t=Mathf.Clamp01((p.x-18f)/62f);
        Vector3 c=DesignRiftPoint(t);
        return Vector2.Distance(p,new Vector2(c.x,c.z));
    }
    public static bool IsOpenRiftSurface(Vector3 p)
    {
        float d=RiftDistance(new Vector2(p.x,p.z),out float t);
        // In the grown leaf only the crust walls count as surface; below them is the inner world's own ground.
        return d<RiftHalfWidth(t)+6f && p.y>Mathf.Max(RiftFloor(t)-1.5f, Grown ? -34f : float.NegativeInfinity);
    }

    private static float BaseHeight(float x, float z)
    {
        float h = 5f + (Mathf.PerlinNoise(x * 0.033f + 27f, z * 0.033f + 59f) - 0.5f) * 2f;
        h += 2.4f * Mathf.Exp(-x * x / 135f);
        // Three strong masses, rather than noise peaks everywhere. The eastern bank stays open.
        h=Mathf.Max(h,29f*(1f-Mathf.SmoothStep(0,1,(Oval(x,z,-55,10,27,31)-0.72f)/0.28f)));
        h += Hill(x, z, 1, 112, 28, 35, 18);
        h += Hill(x,z,45,56,36,18,12);
        h += Hill(x,z,0,-159,35,57,18);
        h += Hill(x,z,0,-101,20,16,13);
        // A shallow bowl below the tutorial overlook, with higher banks around the flower drifts.
        float bowl=Oval(x,z,0,-61,46,38);
        h=Mathf.Lerp(h,3.8f+2.4f*bowl*bowl,1f-Mathf.SmoothStep(0,1,(bowl-0.75f)/0.4f));
        // The northwest cliff culminates in a broad, walkable plateau rather than a narrow noise peak.
        h=Clearing(h,x,z,-57,15,18,29);
        if (Grown)
        {
            // The stem is a highland: the cave opens high up, over a cliff above the flowers.
            float top = 10f + CaveLift + (Mathf.PerlinNoise(x * 0.06f + 5f, z * 0.06f + 9f) - 0.5f) * 3f;
            // The cliff edge wanders in bays and spurs rather than running straight across the leaf.
            float edge = -90f + Mathf.Sin(x * 0.11f + 0.7f) * 3.2f + (Mathf.PerlinNoise(x * 0.07f + 3f, 11f) - 0.5f) * 7f;
            float stem = 1f - Mathf.SmoothStep(0f, 1f, (z - edge) / 9f);
            h = Mathf.Lerp(h, Mathf.Max(h, top), stem);
        }
        // Soft knolls round the rest of the leaf, each carrying a glowing grove.
        if (Grown) foreach (Vector4 knoll in KnollDesign) h += Hill(x, z, knoll.x, knoll.y, knoll.z, knoll.z * 0.8f, knoll.w);
        if (Grown) h += Rolling(x, z);
        h = Path(h, x, z, mainDesign, 5.4f, 6f);
        h = Path(h, x, z, WestDesign, 3.8f, 4.5f);
        h = Path(h, x, z, EastDesign, 4.5f, 6f);
        h = Path(h, x, z, NorthDesign, 3.8f, 5f);
        h = Clearing(h, x, z, -6, -61, 25, 4);
        h = Clearing(h, x, z, 0, 10, 22, 7);
        // The mother's low rise on the east rim of the flower basin (scale 1 keeps the first layout's rest).
        h = scale > 1f ? Clearing(h, x, z, MotherDesign.x, MotherDesign.y, 17, 6.5f) : Clearing(h, x, z, 38, -9, 22, 8);
        // Optional bubble ascent beside the walkable meadow path: each crown is four metres higher.
        h=Clearing(h,x,z,3,-73,5,6);
        h=Clearing(h,x,z,5,-59,5,10);
        h=Clearing(h,x,z,7,-45,5,14);
        float meadow=1f-Mathf.SmoothStep(0f,1f,(Oval(x,z,0,-61,48,41)-0.55f)/0.45f);
        h+=meadow*(Mathf.Sin(x*0.13f+z*0.055f)*0.7f+Mathf.Cos(z*0.12f-x*0.045f)*0.55f);
        // A modest bubble-assisted terrace at the edge of the meadow.
        h = Mathf.Lerp(h, 10f, 1f - Mathf.SmoothStep(0f, 1f, (Oval(x,z,-29,-40,8,7)-0.6f)/0.4f));
        if (Grown)
        {
            // The bubble cascade: steep-sided mesas stepping up from the flower basin past the west plateau,
            // too far apart to walk; bubble flowers on them fire in turn so the player rides up between them.
            foreach (Vector4 tier in BubbleTierDesign)
            {
                float d = Vector2.Distance(new Vector2(x, z), new Vector2(tier.x, tier.y));
                float edge = tier.z * (1f + 0.14f * Mathf.Sin(Mathf.Atan2(z - tier.y, x - tier.x) * 3f + tier.x));
                float top = tier.w + (Mathf.PerlinNoise(x * 0.2f, z * 0.2f) - 0.5f) * 0.8f;
                h = Mathf.Max(h, Mathf.Lerp(h, top, 1f - Mathf.SmoothStep(0f, 1f, (d - edge * 0.72f) / (edge * 0.28f))));
            }
        }
        return h;
    }

    public static float ShapeDensity(Vector3 world, float density)
    {
        Vector2 q = ToDesign(new Vector2(world.x, world.z));
        Vector3 p = new Vector3(q.x, world.y, q.y);
        float d=DesignRiftDistance(q,out float t), radius=DesignRiftHalfWidth(t);
        if(d<radius+5f)
        {
            // The cut is three dimensional: irregular strata can overhang the chamber below them.
            float surface=BaseHeight(p.x,p.z);
            float crust=Mathf.Min(surface-p.y,p.y+32f+Mathf.Max(0,surface)*0.15f);
            float inner=-65f+Mathf.PerlinNoise(world.x*0.025f+71f,world.z*0.025f+31f)*2f-p.y;
            float ledges=Mathf.Sin(p.y*0.65f+t*13f)*0.7f+Mathf.Sin(p.y*1.33f-t*19f)*0.27f;
            float rough=(Mathf.PerlinNoise(t*17f+21f,p.y*0.19f+39f)-0.5f)*1.3f;
            float cut=Mathf.Max((d-radius-ledges-rough)*scale,RiftFloor(t)-p.y);
            density=Mathf.Max(Mathf.Min(crust,cut),inner);
        }
        float footprint = DesignFootprint(p.x, p.z) * scale;
        density = Mathf.Min(density, footprint);
        // The grown leaf keeps a solid shell round its edge, as Scene_Daniel's world does, so the inner world
        // is closed in from the side instead of ending open at the outline.
        if (Grown && footprint > 0f && footprint < ShellThickness)
            density = Mathf.Max(density, Mathf.Min(footprint, BaseHeight(p.x, p.z) - p.y));
        // The real YoYoTutorialFall cave is resampled into this world's saved sculpt asset by the editor graft.
        return density;
    }

    public static float RouteDistance(Vector2 p) => DesignRouteDistance(ToDesign(p)) * scale;
    private static float DesignRouteDistance(Vector2 p) => Mathf.Min(Distance(p, mainDesign), Mathf.Min(Distance(p, WestDesign), Mathf.Min(Distance(p, EastDesign), Distance(p, NorthDesign))));
    public static float Distance(Vector2 p, Vector3[] route)
    {
        float d = float.MaxValue;
        for (int i = 1; i < route.Length; i++) d = Mathf.Min(d, Distance(p, XZ(route[i-1]), XZ(route[i]), out _));
        return d;
    }
    public static float Distance(Vector2 p, Vector2 a, Vector2 b, out float t)
    {
        Vector2 v = b - a; t = Mathf.Clamp01(Vector2.Dot(p - a, v) / Mathf.Max(0.001f, v.sqrMagnitude));
        return Vector2.Distance(p, a + v * t);
    }
    private static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
    // Broad rolling swells and long soft dune ridges over the whole grown leaf, so the ground is never stiff
    // and flat between the landmarks. Warped noise keeps the forms organic; it fades out on the cave highland.
    private static float Rolling(float x, float z)
    {
        float wx = x + (Mathf.PerlinNoise(x * 0.018f + 3.1f, z * 0.018f + 1.7f) - 0.5f) * 34f;
        float wz = z + (Mathf.PerlinNoise(x * 0.018f + 8.4f, z * 0.018f + 6.2f) - 0.5f) * 34f;
        float swell = (Mathf.PerlinNoise(wx * 0.021f + 41f, wz * 0.021f + 13f) - 0.5f) * 8f;
        float small = (Mathf.PerlinNoise(wx * 0.06f + 7f, wz * 0.06f + 29f) - 0.5f) * 2.2f;
        float ridge = 1f - Mathf.Abs(Mathf.PerlinNoise(wx * 0.013f + 55f, wz * 0.03f + 17f) * 2f - 1f);
        ridge = ridge * ridge * ridge * 4.5f;
        float highland = Mathf.SmoothStep(0f, 1f, (z + 96f) / 14f);
        // Gentler in the flower basin so the field still reads as one sweep.
        float basin = Mathf.Lerp(0.55f, 1f, Mathf.SmoothStep(0f, 1f, (Oval(x, z, 0, -61, 46, 38) - 0.6f) / 0.5f));
        // None round the bone mother: her long body and ribs need the same even ground as in Scene_Daniel.
        float mother = Mathf.SmoothStep(0f, 1f, (Vector2.Distance(new Vector2(x, z), MotherDesign) - 24f) / 18f);
        return (swell + small + ridge) * highland * basin * mother;
    }
    private static float Hill(float x,float z,float cx,float cz,float rx,float rz,float height) => height * Mathf.Exp(-Oval(x,z,cx,cz,rx,rz) * Oval(x,z,cx,cz,rx,rz));
    private static float Oval(float x,float z,float cx,float cz,float rx,float rz) => new Vector2((x-cx)/rx,(z-cz)/rz).magnitude;
    private static float Clearing(float h,float x,float z,float cx,float cz,float radius,float y) => Mathf.Lerp(h,y,1f-Mathf.SmoothStep(0f,1f,(Vector2.Distance(new Vector2(x,z),new Vector2(cx,cz))-radius*0.7f)/(radius*0.3f)));
    private static float Path(float h,float x,float z,Vector3[] nodes,float width,float shoulder)
    {
        float nearest=float.MaxValue, y=h;
        for(int i=1;i<nodes.Length;i++)
        {
            float d=Distance(new Vector2(x,z),XZ(nodes[i-1]),XZ(nodes[i]),out float t);
            if(d>=nearest)continue;
            nearest=d; y=Mathf.Lerp(nodes[i-1].y,nodes[i].y,t);
        }
        return Mathf.Lerp(h,y,1f-Mathf.SmoothStep(0f,1f,(nearest-width)/shoulder));
    }
}
