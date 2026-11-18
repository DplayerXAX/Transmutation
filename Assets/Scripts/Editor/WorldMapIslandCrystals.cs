using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// The underside of Scene_World's crystal sky island is one giant ore gem instead of rock: a rough, diamond-like crystal
/// as wide as the island, hanging in crooked layers that narrow down to an off-centre point, with a few smaller crystals
/// breaking out of its sides; its top is buried in the slab. The hanging rock cone the island graft used to add under the slab is
/// taken out of the saved terrain (and no longer made by WorldMapBuild), so what shows from below is crystal.
/// Teal ore and purple geode, the island's ore colours (copied materials; the Ore Colossus is not touched).
/// </summary>
public static class WorldMapIslandCrystals
{
    public const string GroupName = "08b Giant ore gem — under the crystal sky island";
    private const string OldGroupName = "08b Giant ore crystals — under the crystal sky island";
    private const string MeshPath = "Assets/Generated/WorldMap/IslandOreGem.asset";
    private const string OrePath = "Assets/Materials/World/MAT_SkyIsland_CrystalOre.mat";
    private const string GeodePath = "Assets/Materials/World/MAT_SkyIsland_CrystalGeode.mat";
    private const string ShardMeshPath = "Assets/Generated/WorldMap/IslandOreGemShards.asset";
    /// <summary>Metres of facet per texture tile.</summary>
    private const float UvTile = 22f;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Capstone/World Map/Grow Giant Gem Under Sky Island")]
    public static void BuildInOpenWorld()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        Scene scene = SceneManager.GetSceneByPath(WorldMapBuild.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(WorldMapBuild.ScenePath, OpenSceneMode.Additive);
        Transform root = scene.GetRootGameObjects().First(g => g.name == "World — combined map").transform;
        var world = scene.GetRootGameObjects().Select(r => r.GetComponent<ProceduralWorld>()).First(w => w != null);
        var log = new List<string>();
        WorldMapBuild.PrepareSampler(world);
        if (RemoveHangingRock(world, log) > 0)
        {
            AssetDatabase.SaveAssetIfDirty(world.SculptData);
            world.Generate(true);
            WorldMapDress.BakeChunks(world, log);
        }
        Build(root, world, log);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log(string.Join("\n", log));
    }

    /// <summary>Slab-bottom height of the island at a world column (the crystals' bases are buried above it).</summary>
    private static float SlabBottom(float x, float z)
    {
        Vector3 o = WorldMapTerrain.IslandOffset;
        float s = Mathf.Clamp01(WorldMapBuild.IslandRadial(x - o.x, z - o.z) / WorldMapTerrain.IslandRadius);
        return o.y - 4f - 10f * (1f - s * s);
    }

    /// <summary>Takes the hanging rock cone out of the saved terrain: below the slab only the slab's own shape is kept.</summary>
    public static int RemoveHangingRock(ProceduralWorld world, List<string> log)
    {
        object field = typeof(ProceduralWorld).GetField("field", Private).GetValue(world);
        var height = (Func<float, float, float>)Delegate.CreateDelegate(typeof(Func<float, float, float>), field, "SurfaceHeight");
        var ceiling = (Func<float, float>)Delegate.CreateDelegate(typeof(Func<float, float>), field, "CeilingHeight");
        var floor = (Func<float, float, float, float>)Delegate.CreateDelegate(typeof(Func<float, float, float, float>), field, "InnerGroundHeight");
        var density = (Func<Vector3, float, float, float, float>)Delegate.CreateDelegate(typeof(Func<Vector3, float, float, float, float>), field, "Density");
        Vector3 o = WorldMapTerrain.IslandOffset, min = world.GridMinimum;
        float R = WorldMapTerrain.IslandRadius, cell = world.CellSize;
        TerrainSculptData data = world.SculptData;
        int changed = 0;
        foreach (var pair in data.Edits.ToList())
        {
            Vector3 p = min + (Vector3)pair.Key * cell, q = p - o;
            if (q.y > -6f || q.y < -46f || new Vector2(q.x, q.z).magnitude > R * 1.25f) continue;
            float h = height(p.x, p.z);
            float baseValue = density(p, h, ceiling(h), floor(p.x, p.z, h));
            float r = WorldMapBuild.IslandRadial(q.x, q.z), s = Mathf.Clamp01(r / R);
            float slab = Mathf.Min(R - r, q.y - (-4f - 10f * (1f - s * s)));
            float result = Mathf.Max(baseValue, slab);
            if (Mathf.Abs(baseValue + pair.Value - result) < 1e-4f) continue;
            data.SetEdit(pair.Key, result - baseValue);
            changed++;
        }
        EditorUtility.SetDirty(data);
        log.Add("Sky island: hanging rock under the slab removed (" + changed + " terrain samples).");
        return changed;
    }

    /// <summary>Profile of the gem from its top (buried in the slab) down to its point: (radius, depth below the top).
    /// Steep facet walls alternate with narrow inward steps, so it hangs in layers that narrow to a single point.</summary>
    private static readonly (float r, float d)[] Profile =
    {
        (78f, 0f),
        (71f, 10f), (59f, 12.5f),
        (51f, 24f), (40f, 26.5f),
        (32f, 38f), (22f, 40f),
        (14.5f, 49f), (8f, 50.5f),
        (0f, 60f),
    };
    private const int Sides = 14;
    /// <summary>How far under the island's middle the gem's top ring sits (inside the slab's rock).</summary>
    private const float TopDepth = 5f;
    /// <summary>Seed for the gem's irregularity (change it for a different but equally rough gem).</summary>
    private const int Seed = 5311;

    public static void Build(Transform root, ProceduralWorld world, List<string> log)
    {
        foreach (var name in new[] { GroupName, OldGroupName }) { var old = root.Find(name); if (old != null) Object.DestroyImmediate(old.gameObject); }
        var group = new GameObject(GroupName).transform;
        group.SetParent(root, false);

        Vector3 c = WorldMapTerrain.IslandOffset, top = c + Vector3.down * TopDepth;
        var stones = root.GetComponentsInChildren<SkyIslands>(true).SelectMany(s => s.stones).ToList();
        float Clearance(Vector3 local)
        {
            Vector3 p = top + local;
            return p.y - (world.TryFindSurfaceBelow(p, out float g) ? g : WorldMapTerrain.Height(p.x, p.z));
        }
        // A side crystal may grow only if its point stays well over the ground and clear of the floating-stone climb.
        bool SpurFits(Vector3 from, Vector3 to, float radius) =>
            Clearance(to) >= 10f && !stones.Any(st => DistanceToSegment(st - top, from, to) < radius + 8f);

        Mesh gem = GemMesh(SpurFits, out Vector3 tipLocal, out int spurs, out int details, out Mesh looseShards);
        var tex = GemTextures.Ensure();
        Material ore = CopyMaterial("Assets/Materials/Creatures/OreColossus/MAT_OreColossus_Ore.mat", OrePath, 0.9f, tex, Vector2.zero);
        Material geode = CopyMaterial("Assets/Materials/Creatures/OreColossus/MAT_OreColossus_Geode.mat", GeodePath, 1.2f, tex, new Vector2(0.37f, 0.61f));

        var loose = new GameObject("Loose gem shards", typeof(MeshFilter), typeof(MeshRenderer));
        loose.transform.SetParent(group, false);
        loose.transform.SetPositionAndRotation(top, Quaternion.identity);
        loose.GetComponent<MeshFilter>().sharedMesh = looseShards;
        loose.GetComponent<MeshRenderer>().sharedMaterials = new[] { ore, geode };

        var go = new GameObject("Giant ore gem", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        go.transform.SetParent(group, false);
        go.transform.SetPositionAndRotation(top, Quaternion.identity);
        go.GetComponent<MeshFilter>().sharedMesh = gem;
        go.GetComponent<MeshRenderer>().sharedMaterials = new[] { ore, geode };
        go.GetComponent<MeshCollider>().sharedMesh = gem;
        go.layer = 7; go.tag = "Ground";

        // A soft glow from inside so the facets read from the ground.
        foreach (var (at, colour, range, intensity) in new[] {
            (top + Vector3.down * 22f, new Color(0.35f, 0.95f, 0.85f), 110f, 2.5f),
            (top + tipLocal * 0.8f, new Color(0.62f, 0.45f, 1f), 50f, 2f) })
        {
            var lamp = new GameObject("Gem glow", typeof(Light));
            lamp.transform.SetParent(group, false);
            lamp.transform.position = at;
            var light = lamp.GetComponent<Light>();
            light.type = LightType.Point; light.shadows = LightShadows.None;
            light.color = colour; light.range = range; light.intensity = intensity;
        }
        log.Add("Sky island underside: one giant rough ore gem with " + spurs + " side crystals and " + details +
                " small druse/hanging crystals, point at y " +
                (top + tipLocal).y.ToString("0.0") + " (" + Clearance(tipLocal).ToString("0.0") + " m over the ground).");
    }

    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector3.Distance(p, a + ab * t);
    }

    /// <summary>The gem, in metres round its top centre. It follows <see cref="Profile"/> but like grown ore, not a cut
    /// stone: every corner is pushed in or out and up or down, each layer bulges and tilts its own way so the steps run
    /// crooked, corners crowd unevenly round the rings, the point is off to one side, a few facets are purple, and a
    /// handful of smaller crystals break out of the layers at odd angles. Walls are ore (sub-mesh 0); steps, stray facets
    /// and some side crystals geode (sub-mesh 1). Flat shaded, rebuilt into the same asset each time.</summary>
    private static Mesh GemMesh(Func<Vector3, Vector3, float, bool> spurFits, out Vector3 tip, out int spurs, out int details, out Mesh shards)
    {
        var random = new System.Random(Seed);
        float Range(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
        var v = new List<Vector3>(); var walls = new List<int>(); var steps = new List<int>();
        int rings = Profile.Length - 1;
        tip = new Vector3(Range(-7f, 7f), -Profile[rings].d + Range(0f, 2f), Range(-7f, 7f));
        Vector3 tipAt = tip;
        // The gem's crooked middle line: straight down from the top centre, drifting out to the off-centre point.
        Vector3 Axis(float y) { float t = Mathf.Clamp01(-y / -tipAt.y); return new Vector3(tipAt.x * t * t, y, tipAt.z * t * t); }

        // Each layer (a wall and the step under it) bulges and tilts as one, so its step stays a step.
        int layers = rings / 2 + 1;
        var tilt = new (float a, float pa, float b, float pb)[layers];
        var bulge = new (float a, float pa, float b, float pb)[layers];
        for (int l = 0; l < layers; l++)
        {
            tilt[l] = (Range(1.5f, 4f), Range(0f, 6.3f), Range(0.5f, 2f), Range(0f, 6.3f));
            bulge[l] = (Range(0.06f, 0.14f), Range(0f, 6.3f), Range(0.03f, 0.08f), Range(0f, 6.3f));
        }
        var corners = new Vector3[rings, Sides];
        for (int ring = 0; ring < rings; ring++)
        {
            int l = (ring + 1) / 2;
            for (int i = 0; i < Sides; i++)
            {
                float a = (i + 0.5f * ring + Range(-0.3f, 0.3f)) * Mathf.PI * 2f / Sides;
                float r = Profile[ring].r, d = Profile[ring].d;
                if (ring == 0) { r = 76f * Range(0.96f, 1.04f); d = Range(0f, 2f); }  // buried in the slab, kept under the rim
                else
                {
                    var (ba, bpa, bb, bpb) = bulge[l]; var (ta, tpa, tb, tpb) = tilt[l];
                    r *= 1f + ba * Mathf.Sin(a + bpa) + bb * Mathf.Sin(3f * a + bpb) + Range(-0.07f, 0.07f);
                    d += ta * Mathf.Sin(a + tpa) + tb * Mathf.Sin(2f * a + tpb) + Range(-0.8f, 0.8f);
                }
                Vector3 axis = Axis(-d);
                corners[ring, i] = new Vector3(axis.x + Mathf.Cos(a) * r, -d, axis.z + Mathf.Sin(a) * r);
            }
        }
        void Tri(List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 outFrom)
        {
            Vector3 mid = (a + b + c) / 3f, outward = mid - outFrom;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) (b, c) = (c, b);
            int n = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(n); t.Add(n + 1); t.Add(n + 2);
        }
        void BodyTri(List<int> t, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 mid = (a + b + c) / 3f, axis = Axis(mid.y);
            Tri(t, a, b, c, new Vector3(axis.x, mid.y, axis.z));
        }
        for (int ring = 0; ring < rings; ring++)
        {
            bool step = ring % 2 == 1;
            bool point = ring + 1 == rings;
            for (int i = 0; i < Sides; i++)
            {
                Vector3 a0 = corners[ring, i], a1 = corners[ring, (i + 1) % Sides];
                List<int> Pick() => step ? steps : Range(0f, 1f) < 0.08f ? steps : walls;
                if (point) { BodyTri(Pick(), a0, a1, tip); continue; }
                Vector3 b0 = corners[ring + 1, i], b1 = corners[ring + 1, (i + 1) % Sides];
                BodyTri(Pick(), a0, a1, b0);
                BodyTri(Pick(), b0, a1, b1);
            }
        }
        // Flat cap over the top (inside the slab's rock), so the collider is closed.
        for (int i = 0; i < Sides; i++)
        {
            Vector3 a = corners[0, i], b = corners[0, (i + 1) % Sides];
            Tri(walls, a, b, Vector3.zero, new Vector3(0f, -10f, 0f));
        }

        // A crystal: an n-sided prism from a buried root that tapers into a point; double-ended ones get a second point
        // instead of the root ring (for loose shards). Ranges are drawn in the same order as the gem's first side crystals.
        void Shard(List<int> t, Vector3 root, Vector3 dir, float radius, float length, int n, bool doubleEnded)
        {
            Vector3 end = root + dir * length;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            side = side.sqrMagnitude < 1e-4f ? Vector3.right : side.normalized;
            Vector3 up2 = Vector3.Cross(side, dir);
            var bottom = new Vector3[n]; var shoulder = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                float a = (k + Range(-0.15f, 0.15f)) * Mathf.PI * 2f / n, rr = radius * Range(0.85f, 1.1f);
                Vector3 off = side * Mathf.Cos(a) * rr + up2 * Mathf.Sin(a) * rr;
                bottom[k] = root + off * 0.9f;
                shoulder[k] = root + dir * length * Range(0.72f, 0.8f) + off;
            }
            Vector3 point = end + side * Range(-0.15f, 0.15f) * radius + up2 * Range(-0.15f, 0.15f) * radius;
            Vector3 back = root - dir * length * 0.3f;
            Vector3 centreAt(Vector3 q) => root + dir * Vector3.Dot(q - root, dir);
            for (int k = 0; k < n; k++)
            {
                int k1 = (k + 1) % n;
                Tri(t, bottom[k], shoulder[k], shoulder[k1], centreAt((bottom[k] + shoulder[k] + shoulder[k1]) / 3f));
                Tri(t, bottom[k], shoulder[k1], bottom[k1], centreAt((bottom[k] + shoulder[k1] + bottom[k1]) / 3f));
                Tri(t, shoulder[k], shoulder[k1], point, centreAt((shoulder[k] + shoulder[k1] + point) / 3f));
                if (doubleEnded) Tri(t, bottom[k1], bottom[k], back, centreAt((bottom[k] + bottom[k1] + back) / 3f));
            }
        }

        // Side crystals breaking out of the layers: six-sided, leaning out and down, buried well into the body.
        spurs = 0;
        for (int tries = 0; tries < 30 && spurs < 7; tries++)
        {
            int ring = 1 + 2 * random.Next(0, 4);                       // the foot of a wall
            Vector3 foot = corners[ring, random.Next(Sides)];
            Vector3 axisAt = Axis(foot.y), radial = new Vector3(foot.x - axisAt.x, 0f, foot.z - axisAt.z).normalized;
            float lean = Range(30f, 62f) * Mathf.Deg2Rad;
            Vector3 dir = (Vector3.down * Mathf.Cos(lean) + radial * Mathf.Sin(lean)
                           + Vector3.Cross(Vector3.up, radial) * Range(-0.3f, 0.3f)).normalized;
            float radius = Range(3.5f, 8.5f) * Mathf.Lerp(1f, 0.6f, ring / (float)rings);
            float length = radius * Range(3f, 4.5f);
            Vector3 root = foot - radial * radius * 1.6f + Vector3.up * radius * 0.5f, end = root + dir * length;
            if (!spurFits(root, end, radius)) continue;
            Shard(Range(0f, 1f) < 0.35f ? steps : walls, root, dir, radius, length, 6, false);
            spurs++;
        }

        // ---- Underside detail (its own random stream, so the body and side crystals above keep their shape) ----
        random = new System.Random(Seed + 17);
        Vector3 Jitter(float s) => new Vector3(Range(-s, s), Range(-s, s), Range(-s, s));
        Vector3 Radial(Vector3 p) { Vector3 ax = Axis(p.y); return new Vector3(p.x - ax.x, 0f, p.z - ax.z).normalized; }

        // Druse: clusters of small crystals hanging off the underside of every step and, sparser, out of the walls.
        details = 0;
        for (int ring = 0; ring + 1 < rings; ring++)
        {
            bool step = ring % 2 == 1;
            float scale = Mathf.Lerp(1f, 0.55f, ring / (float)rings);
            for (int i = 0; i < Sides; i++)
            {
                int clusters = step ? random.Next(1, 4) : (random.NextDouble() < 0.45 ? 1 : 0);
                for (int c = 0; c < clusters; c++)
                {
                    Vector3 a0 = corners[ring, i], a1 = corners[ring, (i + 1) % Sides];
                    Vector3 b0 = corners[ring + 1, i], b1 = corners[ring + 1, (i + 1) % Sides];
                    float s = Range(0.12f, 0.88f), w = Range(step ? 0.15f : 0.35f, step ? 0.85f : 0.95f);
                    Vector3 p = Vector3.Lerp(Vector3.Lerp(a0, a1, s), Vector3.Lerp(b0, b1, s), w);
                    Vector3 radial = Radial(p), face = Vector3.Cross(a1 - a0, b0 - a0).normalized;
                    if (Vector3.Dot(face, radial + Vector3.down) < 0f) face = -face;
                    int count = random.Next(step ? 3 : 2, step ? 8 : 5);
                    List<int> t = random.NextDouble() < (step ? 0.3 : 0.6) ? steps : walls;
                    for (int k = 0; k < count; k++)
                    {
                        Vector3 dir = (face * 0.7f + Vector3.down * Range(0.25f, 0.9f) + radial * Range(0f, 0.4f) + Jitter(0.45f)).normalized;
                        float radius = Range(0.45f, k == 0 ? 2.2f : 1.5f) * scale;
                        float length = radius * Range(2.5f, 5.5f);
                        Vector3 root = p + Vector3.ProjectOnPlane(Jitter(2.6f * scale), face) - dir * radius * 0.9f;
                        if (!spurFits(root, root + dir * length, radius)) continue;
                        Shard(t, root, dir, radius, length, random.Next(5, 7), false);
                        details++;
                    }
                }
            }
        }

        // A chandelier of long crystals hanging round the point.
        int lastRing = rings - 1;
        for (int k = 0; k < 11; k++)
        {
            int i = random.Next(Sides);
            Vector3 p = Vector3.Lerp(corners[lastRing, i], tip, Range(0.15f, 0.8f));
            Vector3 dir = (Vector3.down + Radial(p) * Range(0.1f, 0.55f) + Jitter(0.2f)).normalized;
            float radius = Range(0.8f, 2.2f), length = radius * Range(4f, 7.5f);
            Vector3 root = p - dir * radius;
            if (!spurFits(root, root + dir * length, radius)) continue;
            Shard(random.NextDouble() < 0.4 ? steps : walls, root, dir, radius, length, 6, false);
            details++;
        }

        // Loose shards floating round the lower layers (own mesh, no collider): double-ended, mostly upright.
        int shardStart = v.Count;
        var looseOre = new List<int>(); var looseGeode = new List<int>();
        for (int k = 0, placed = 0; k < 40 && placed < 18; k++)
        {
            float d = Range(22f, 62f), angle = Range(0f, Mathf.PI * 2f);
            float body = BodyRadius(d) + Range(5f, 18f);
            Vector3 centre = Axis(-d) + new Vector3(Mathf.Cos(angle) * body, Range(-3f, 3f), Mathf.Sin(angle) * body);
            Vector3 dir = (Vector3.up * (random.NextDouble() < 0.5 ? 1f : -1f) + Jitter(0.6f)).normalized;
            float radius = Range(0.6f, 2f), length = radius * Range(2.5f, 4f);
            Vector3 root = centre - dir * length * 0.4f;
            if (!spurFits(root - dir * length * 0.3f, root + dir * length, radius + 2f)) continue;
            Shard(random.NextDouble() < 0.5 ? looseGeode : looseOre, root, dir, radius, length, random.Next(5, 7), true);
            placed++;
        }

        var shardV = v.GetRange(shardStart, v.Count - shardStart);
        v.RemoveRange(shardStart, v.Count - shardStart);
        List<int> Shift(List<int> list) => list.Select(x => x - shardStart).ToList();
        shards = SaveMesh(ShardMeshPath, "Island ore gem — loose shards", shardV, Shift(looseOre), Shift(looseGeode));
        return SaveMesh(MeshPath, "Island ore gem", v, walls, steps);
    }

    /// <summary>The gem's profile radius at a depth below its top.</summary>
    private static float BodyRadius(float d)
    {
        for (int i = 1; i < Profile.Length; i++)
            if (d <= Profile[i].d)
                return Mathf.Lerp(Profile[i - 1].r, Profile[i].r, Mathf.InverseLerp(Profile[i - 1].d, Profile[i].d, d));
        return 0f;
    }

    /// <summary>Saves a flat-shaded two-material mesh with per-facet UVs (each triangle projected onto its own plane in
    /// world metres, <see cref="UvTile"/> per tile, u horizontal, so near-flat neighbours carry on without a seam and
    /// sharp edges break the pattern) and tangents for the normal maps.</summary>
    private static Mesh SaveMesh(string path, string name, List<Vector3> v, List<int> ore, List<int> geode)
    {
        var uv = new Vector2[v.Count];
        for (int i = 0; i + 2 < v.Count; i += 3)
        {
            Vector3 n = Vector3.Cross(v[i + 1] - v[i], v[i + 2] - v[i]).normalized;
            Vector3 t = Vector3.Cross(n, Vector3.up);
            t = t.sqrMagnitude < 1e-4f ? Vector3.Cross(n, Vector3.forward).normalized : t.normalized;
            Vector3 b = Vector3.Cross(n, t);
            for (int j = 0; j < 3; j++) uv[i + j] = new Vector2(Vector3.Dot(v[i + j], t), Vector3.Dot(v[i + j], b)) / UvTile;
        }
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool fresh = mesh == null;
        if (fresh) mesh = new Mesh();
        mesh.Clear();
        mesh.name = name;
        mesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(v);
        mesh.SetUVs(0, uv);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(ore, 0); mesh.SetTriangles(geode, 1);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        if (fresh) { Directory.CreateDirectory(Path.GetDirectoryName(path)); AssetDatabase.CreateAsset(mesh, path); }
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static Material CopyMaterial(string source, string path, float glow, GemTextures.Set tex, Vector2 offset)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        var original = AssetDatabase.LoadAssetAtPath<Material>(source);
        if (material == null)
        {
            material = new Material(original);
            AssetDatabase.CreateAsset(material, path);
        }
        else material.CopyPropertiesFromMaterial(original);
        material.SetFloat("_Smoothness", 0.85f);
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", original.GetColor("_EmissionColor") * glow);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        // Detail: fractures, sub-facets and growth lines (albedo + normal), cracks that glow (emission), fine grain on top.
        foreach (var slot in new[] { "_BaseMap", "_MainTex" }) { material.SetTexture(slot, tex.Albedo); material.SetTextureOffset(slot, offset); }
        material.SetTexture("_BumpMap", tex.Normal); material.SetTextureOffset("_BumpMap", offset);
        material.SetFloat("_BumpScale", 1f);
        material.EnableKeyword("_NORMALMAP");
        material.SetTexture("_EmissionMap", tex.Emission); material.SetTextureOffset("_EmissionMap", offset);
        material.SetTexture("_DetailNormalMap", tex.Grain);
        material.SetFloat("_DetailNormalMapScale", 0.6f);
        material.SetTextureScale("_DetailAlbedoMap", new Vector2(5f, 5f));
        material.EnableKeyword("_DETAIL_MULX2");
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }
}

/// <summary>Tileable crystal textures for the island gem, generated once into <see cref="Dir"/> (delete them to remake):
/// big fracture cells each tilted as its own flat sub-facet, a finer set of cracks inside them, wavy growth lines that
/// run along the tile's u (horizontal on the gem's walls) and cloudy inclusions. Emission is bright in the cracks.</summary>
public static class GemTextures
{
    public const string Dir = "Assets/Generated/WorldMap/GemTextures/";
    public struct Set { public Texture2D Albedo, Normal, Emission, Grain; }

    public static Set Ensure()
    {
        string albedo = Dir + "Gem_Albedo.png", normal = Dir + "Gem_Normal.png", emission = Dir + "Gem_Emission.png", grain = Dir + "Gem_Grain_Normal.png";
        if (!File.Exists(albedo) || !File.Exists(normal) || !File.Exists(emission) || !File.Exists(grain))
        {
            Directory.CreateDirectory(Dir);
            BuildMain(512, albedo, normal, emission);
            BuildGrain(256, grain);
            foreach (var p in new[] { albedo, normal, emission, grain }) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
        }
        Configure(albedo, false, true); Configure(emission, false, true);
        Configure(normal, true, false); Configure(grain, true, false);
        return new Set
        {
            Albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedo), Normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normal),
            Emission = AssetDatabase.LoadAssetAtPath<Texture2D>(emission), Grain = AssetDatabase.LoadAssetAtPath<Texture2D>(grain),
        };
    }

    private static void Configure(string path, bool normalMap, bool srgb)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (importer.textureType == type && importer.sRGBTexture == srgb && importer.wrapMode == TextureWrapMode.Repeat && importer.anisoLevel == 4) return;
        importer.textureType = type; importer.sRGBTexture = srgb;
        importer.wrapMode = TextureWrapMode.Repeat; importer.anisoLevel = 4; importer.mipmapEnabled = true;
        importer.SaveAndReimport();
    }

    public static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    private static int Wrap(int i, int n) => ((i % n) + n) % n;

    /// <summary>GLSL-style smoothstep (Mathf.SmoothStep interpolates between its first two arguments instead).</summary>
    private static float Smooth(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

    private static float Noise(float u, float v, int period, int seed)
    {
        float x = u * period, y = v * period;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        float H(int dx, int dy) => Hash(Wrap(x0 + dx, period), Wrap(y0 + dy, period), seed);
        return Mathf.Lerp(Mathf.Lerp(H(0, 0), H(1, 0), fx), Mathf.Lerp(H(0, 1), H(1, 1), fx), fy);
    }

    private static float Fbm(float u, float v, int period, int octaves, int seed)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int o = 0; o < octaves; o++, period *= 2, amp *= 0.5f) { sum += amp * Noise(u, v, period, seed + o); norm += amp; }
        return sum / norm;
    }

    /// <summary>Periodic Voronoi: distance to nearest/second-nearest point (cell units), nearest cell id and its
    /// planar "facet" height (each cell a flat plane with its own tilt).</summary>
    private static void Cells(float u, float v, int n, int seed, out float f1, out float f2, out int id, out float facet)
    {
        float x = u * n, y = v * n;
        int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
        f1 = f2 = 9f; id = 0; Vector2 best = default;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gy = cy + dy, wx = Wrap(gx, n), wy = Wrap(gy, n);
                var p = new Vector2(gx + 0.12f + 0.76f * Hash(wx, wy, seed), gy + 0.12f + 0.76f * Hash(wx, wy, seed + 1));
                float d = Vector2.Distance(p, new Vector2(x, y));
                if (d < f1) { f2 = f1; f1 = d; id = wx * n + wy; best = p; }
                else if (d < f2) f2 = d;
            }
        var g = new Vector2(Hash(id, 7, seed) - 0.5f, Hash(id, 8, seed) - 0.5f) * 2f;
        facet = Vector2.Dot(g, new Vector2(x, y) - best) + Hash(id, 9, seed) * 0.3f;
    }

    private static void BuildMain(int size, string albedoPath, string normalPath, string emissionPath)
    {
        var height = new float[size * size];
        var albedo = new Color32[size * size]; var emission = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float wu = u + 0.035f * (Fbm(u, v, 4, 3, 11) - 0.5f), wv = v + 0.035f * (Fbm(u, v, 4, 3, 12) - 0.5f);
                Cells(wu, wv, 5, 21, out float a1, out float a2, out int id1, out float facet1);
                Cells(wu, wv, 11, 31, out float b1, out float b2, out _, out float facet2);
                float crack1 = 1f - Smooth(0f, 0.035f, a2 - a1);
                float crack2 = (1f - Smooth(0f, 0.025f, b2 - b1)) * (0.4f + 0.6f * Fbm(u, v, 6, 2, 91));  // fades in and out
                float halo = 1f - Smooth(0f, 0.2f, a2 - a1);
                float growth = Mathf.Sin((wv * 48f + 2.5f * Fbm(u, v, 3, 2, 41)) * Mathf.PI * 2f) * 0.5f + 0.5f;
                float cloud = Fbm(u, v, 4, 5, 51);
                // Thin wandering veins of brighter ore that cross the fracture cells.
                float ridge = 1f - Mathf.Abs(Fbm(u, v, 3, 2, 81) * 2f - 1f);
                float vein = Smooth(0.965f, 0.995f, ridge), veinHalo = Smooth(0.86f, 0.98f, ridge);
                height[y * size + x] = facet1 * 0.3f + facet2 * 0.08f + growth * 0.004f - crack1 * 0.15f - crack2 * 0.04f + vein * 0.03f;
                float a = Mathf.Clamp01(0.62f + 0.38f * cloud + 0.14f * (Hash(id1, 3, 21) - 0.5f) + 0.2f * crack1 + 0.08f * crack2 - 0.03f * growth + 0.25f * vein);
                float e = Mathf.Clamp01(0.3f + 0.25f * cloud + 0.55f * crack1 + 0.2f * crack2 + 0.1f * halo + 0.75f * vein + 0.15f * veinHalo);
                albedo[y * size + x] = Grey(a); emission[y * size + x] = Grey(e);
            }
        Save(albedo, size, albedoPath);
        Save(emission, size, emissionPath);
        Save(NormalsFrom(height, size, 0.25f), size, normalPath);
    }

    private static void BuildGrain(int size, string path)
    {
        var height = new float[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                height[y * size + x] = Fbm(u, v, 16, 4, 61) + 0.4f * Fbm(u, v, 48, 2, 71);
            }
        Save(NormalsFrom(height, size, 0.04f), size, path);
    }

    /// <summary>Tangent-space normals (OpenGL, +y up) from a tileable height field; strength scales the slope per uv unit.</summary>
    private static Color32[] NormalsFrom(float[] h, int size, float strength)
    {
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float du = (h[y * size + Wrap(x + 1, size)] - h[y * size + Wrap(x - 1, size)]) * size * 0.5f;
                float dv = (h[Wrap(y + 1, size) * size + x] - h[Wrap(y - 1, size) * size + x]) * size * 0.5f;
                Vector3 n = new Vector3(-du * strength, -dv * strength, 1f).normalized;
                px[y * size + x] = new Color32((byte)(n.x * 127.5f + 127.5f), (byte)(n.y * 127.5f + 127.5f), (byte)(n.z * 127.5f + 127.5f), 255);
            }
        return px;
    }

    private static Color32 Grey(float g) { byte b = (byte)(Mathf.Clamp01(g) * 255f); return new Color32(b, b, b, 255); }

    private static void Save(Color32[] pixels, int size, string path)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        t.SetPixels32(pixels); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }
}
