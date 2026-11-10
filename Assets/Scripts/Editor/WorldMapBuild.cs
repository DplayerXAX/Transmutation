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
/// Builds Scene_World (the combined map from Daniel's whiteboard) from a copy of Scene_LeafMap. Batch only, in the
/// isolated test copy: -executeMethod WorldMapBuild.Run. Source scenes are never saved.
/// Phase 1: terrain layout (WorldMapTerrain), Goo cave + Cistern sky island grafted into the saved terrain, spire and
/// bone mother moved onto their sinkholes, placeholder forest and ruins, review captures.
/// </summary>
public static class WorldMapBuild
{
    public const string ScenePath = "Assets/Scenes/Scene_World.unity";
    private const string LeafPath = "Assets/Scenes/Scene_LeafMap.unity";
    private const string GooPath = "Assets/Scenes/Scene_Goo.unity";
    private const string CisternPath = "Assets/Scenes/Scene_CisternBasin.unity";
    private const string TerrainPath = "Assets/Terrain/Sculpted/Scene_World_Terrain.asset";
    private const string Folder = "Assets/Generated/WorldMap";
    private const string Captures = "ArtCaptures/WorldMap";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string RootName = "World — combined map";

    public static void Run()
    {
        try
        {
            var log = new List<string>();
            Scene scene = CreateScene(log);
            ProceduralWorld world = FindInScene<ProceduralWorld>(scene);
            Terrain(world, log);
            Graft(world, GooPath, WorldMapTerrain.GooOffset, WorldMapTerrain.GooLower, WorldMapTerrain.GooUpper, UndergroundCave, log);
            Vector3 r = new Vector3(WorldMapTerrain.IslandRadius * 1.2f, 0f, WorldMapTerrain.IslandRadius * 1.2f);
            Graft(world, CisternPath, WorldMapTerrain.IslandOffset, new Vector3(-r.x, -76f, -r.z), new Vector3(r.x, 48f, r.z), Island, log);
            AssetDatabase.SaveAssetIfDirty(world.SculptData);
            Landmarks(scene, world, log);
            Player(scene, world, log);
            Transform root = scene.GetRootGameObjects().First(g => g.name == RootName).transform;
            WorldMapPlaceholders.Build(root, log);
            WorldMapImport.All(scene, root, log);
            WorldMapDress.All(scene, root, log);
            Cameras(root);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            // Last: the terrain is final, so its meshes can be saved and Play skips generating them.
            WorldMapDress.BakeChunks(world, log);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(Captures);
            File.WriteAllText(Captures + "/Build.txt", string.Join("\n", log));
            Capture(scene);
        }
        catch (Exception e)
        {
            Directory.CreateDirectory(Captures);
            File.WriteAllText(Captures + "/Build.txt", "FAIL\n" + e);
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>Re-renders the review cameras of the saved Scene_World.</summary>
    public static void CaptureOnly()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Capture(scene);
    }

    // ---------------- Scene ----------------

    private static Scene CreateScene(List<string> log)
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in the isolated batch copy.");
        if (File.Exists(ScenePath)) AssetDatabase.DeleteAsset(ScenePath);
        if (!AssetDatabase.CopyAsset(LeafPath, ScenePath)) throw new InvalidOperationException("Could not copy " + LeafPath);
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // The leaf's floating stones (with their bubble flowers) are kept and moved later to climb to the crystal island.
        var placed = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Placed World Objects");
        var sky = placed != null ? placed.transform.Find("Sky Islands") : null;
        var leafRoot = scene.GetRootGameObjects().First(g => g.GetComponent<LeafMapEcology>() != null);
        if (sky != null) sky.SetParent(leafRoot.transform, true);
        // The leaf's instanced bubble-lily field component is reused for the new flower field (new point data).
        var lilies = leafRoot.GetComponentInChildren<LilyField>(true);
        if (lilies != null) { lilies.transform.SetParent(leafRoot.transform, true); lilies.name = "Flower field lilies"; }
        string[] dropRoots = { "03b Cliff ecosystem — springs, birds, webs", "Leave tutorial — contextual hints", "Placed World Objects" };
        foreach (var go in scene.GetRootGameObjects())
            if (dropRoots.Contains(go.name)) Object.DestroyImmediate(go);

        var leaf = scene.GetRootGameObjects().First(g => g.GetComponent<LeafMapEcology>() != null);
        string[] keep = { "Sky Islands", "Flower field lilies", "Sun — warm neutral key", "04 Central tower — three climbing stages", "05 Bone sanctuary — mother and young" };
        foreach (Transform child in leaf.transform.Cast<Transform>().ToArray())
            if (!keep.Contains(child.name)) Object.DestroyImmediate(child.gameObject);
        Object.DestroyImmediate(leaf.GetComponent<LeafMapEcology>());
        leaf.name = RootName;
        foreach (var scar in leaf.GetComponentsInChildren<BoneScar>(true)) Object.DestroyImmediate(scar);
        log.Add("Scene copied from " + LeafPath + "; leaf-only content removed.");
        return scene;
    }

    // ---------------- Terrain ----------------

    private static void Terrain(ProceduralWorld world, List<string> log)
    {
        world.name = "Procedural World — combined map";
        var so = new SerializedObject(world);
        so.FindProperty("leafMap").boolValue = false;
        so.FindProperty("worldMap").boolValue = true;
        so.FindProperty("leafScale").floatValue = 1f;
        so.FindProperty("worldSize").vector2Value = WorldMapTerrain.WorldSize;
        so.FindProperty("cellSize").floatValue = 1.25f;
        so.FindProperty("chunkCells").intValue = 24;
        so.FindProperty("overhangAmplitude").floatValue = 0f;
        so.FindProperty("caveWidth").floatValue = 0f;
        so.FindProperty("sinkholeCount").intValue = 0;
        so.FindProperty("firstSinkholeDistance").floatValue = 0f;
        so.FindProperty("spawnFlatRadius").floatValue = 0f;
        so.FindProperty("rimHeight").floatValue = 0f;
        so.FindProperty("placePlayerOnSurface").boolValue = true;
        so.FindProperty("blockingRadius").floatValue = 190f;
        so.FindProperty("sculptData").objectReferenceValue = null;
        so.ApplyModifiedPropertiesWithoutUndo();
        world.transform.position = Vector3.zero;

        string layout = world.CaptureLayout(out _);
        // Two sinkholes: 0 for the Hanging Spire on the ruin plaza, 1 for the bone mother's dive on her ledge.
        string holes = "\"sinkholes\":[" + Hole(WorldMapTerrain.SpireSpot, WorldMapTerrain.SpireHoleRadius) + "," +
                       Hole(WorldMapTerrain.MotherHole, WorldMapTerrain.MotherHoleRadius) + "]";
        layout = System.Text.RegularExpressions.Regex.Replace(layout, "\"sinkholes\":\\[.*?\\]", holes);
        Type fieldType = typeof(ProceduralWorld).GetNestedType("DensityField", BindingFlags.NonPublic);
        object field = JsonUtility.FromJson(layout, fieldType);
        var checks = (float[])typeof(ProceduralWorld).GetMethod("CheckValues", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { field });

        Directory.CreateDirectory(Path.GetDirectoryName(TerrainPath));
        if (File.Exists(TerrainPath)) AssetDatabase.DeleteAsset(TerrainPath);
        var data = ScriptableObject.CreateInstance<TerrainSculptData>();
        AssetDatabase.CreateAsset(data, TerrainPath);
        data.Freeze(layout, checks, "Scene_World / WorldMapBuild (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")");
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
        so = new SerializedObject(world);
        so.FindProperty("sculptData").objectReferenceValue = data;
        so.ApplyModifiedPropertiesWithoutUndo();
        log.Add("Terrain layout " + WorldMapTerrain.WorldSize + " baked to " + TerrainPath);
    }

    private static string Hole(Vector2 at, float radius) =>
        "{\"position\":{\"x\":" + F(at.x) + ",\"y\":" + F(at.y) + "},\"radius\":" + F(radius) + "}";
    private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static void PrepareSampler(ProceduralWorld world)
    {
        typeof(ProceduralWorld).GetMethod("UseSavedLayout", Private).Invoke(world, null);
        typeof(ProceduralWorld).GetMethod("PrepareGrid", Private).Invoke(world, new object[] { world.SculptData != null ? world.SculptData.Headroom : 0f });
        typeof(ProceduralWorld).GetMethod("LoadSculpt", Private).Invoke(world, null);
    }

    /// <summary>
    /// Resamples a donor scene's saved terrain into the world's saved terrain inside a box (donor space), moved by
    /// offset. mask (donor-space point, base density, donor density) -> density lets a graft become a floating island or a roofed cave.
    /// </summary>
    private static void Graft(ProceduralWorld target, string donorPath, Vector3 offset, Vector3 lower, Vector3 upper,
        Func<Vector3, float, Func<float>, float> mask, List<string> log)
    {
        Scene own = target.gameObject.scene;
        Scene source = EditorSceneManager.OpenScene(donorPath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(own);
        try
        {
            var donor = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ProceduralWorld>(true)).Single();
            PrepareSampler(donor); PrepareSampler(target);
            object field = typeof(ProceduralWorld).GetField("field", Private).GetValue(target);
            var height = (Func<float, float, float>)Delegate.CreateDelegate(typeof(Func<float, float, float>), field, "SurfaceHeight");
            var ceiling = (Func<float, float>)Delegate.CreateDelegate(typeof(Func<float, float>), field, "CeilingHeight");
            var floor = (Func<float, float, float, float>)Delegate.CreateDelegate(typeof(Func<float, float, float, float>), field, "InnerGroundHeight");
            var density = (Func<Vector3, float, float, float, float>)Delegate.CreateDelegate(typeof(Func<Vector3, float, float, float, float>), field, "Density");
            Vector3 min = target.GridMinimum, donorOrigin = donor.transform.position;
            float cell = target.CellSize;
            Vector3 lo3 = lower + offset, hi3 = upper + offset;
            Vector3Int lo = Vector3Int.FloorToInt((lo3 - min) / cell), hi = Vector3Int.CeilToInt((hi3 - min) / cell);
            int saved = 0;
            for (int z = lo.z; z <= hi.z; z++) for (int x = lo.x; x <= hi.x; x++)
            {
                Vector3 column = min + new Vector3(x * cell, 0, z * cell);
                float edge = Mathf.Min(Mathf.Min(column.x - lo3.x, hi3.x - column.x), Mathf.Min(column.z - lo3.z, hi3.z - column.z));
                if (edge <= 0) continue;
                float weight = mask != null ? 1f : Mathf.SmoothStep(0, 1, edge / 6);
                float h = height(column.x, column.z), c = ceiling(h), f = floor(column.x, column.z, h);
                for (int y = lo.y; y <= hi.y; y++)
                {
                    var key = new Vector3Int(x, y, z); Vector3 p = min + (Vector3)key * cell;
                    float baseValue = density(p, h, c, f);
                    Vector3 q = p - offset;
                    float result;
                    if (mask != null)
                    {
                        result = mask(q, baseValue, () => donor.SampleDensity(q + donorOrigin, false));
                    }
                    else
                    {
                        float original = donor.SampleDensity(q + donorOrigin, false);
                        float fade = Mathf.SmoothStep(0, 1, Mathf.Min(p.y - lo3.y, hi3.y - p.y) / 5) * weight;
                        result = Mathf.Lerp(baseValue, original, fade);
                    }
                    // Away from any surface only the sign matters, so far samples keep the base value (fewer saved edits).
                    if ((result > 2.5f && baseValue > 2.5f) || (result < -2.5f && baseValue < -2.5f)) result = baseValue;
                    if (Mathf.Abs(result - baseValue) < 1e-4f) continue;
                    target.SculptData.SetEdit(key, result - baseValue); saved++;
                }
            }
            EditorUtility.SetDirty(target.SculptData);
            log.Add("Grafted " + donorPath + " at " + offset + ": " + saved + " saved samples.");
        }
        finally { EditorSceneManager.CloseScene(source, true); SceneManager.SetActiveScene(own); }
    }

    /// <summary>Cistern basin cut out as a floating island: its ground slab above a tapering, hanging rock root.</summary>
    /// <summary>
    /// Scene_Goo buried under the forest: below its roof (GooCeiling) the donor's cave and halls, above it the forest
    /// floor, a solid seal round the box so the cave never opens into the inner world; the crater is the only mouth.
    /// </summary>
    private static float UndergroundCave(Vector3 q, float baseValue, Func<float> donor)
    {
        Vector3 lo = WorldMapTerrain.GooLower, hi = WorldMapTerrain.GooUpper;
        float seal = 6f - Mathf.Min(Mathf.Min(q.x - lo.x, hi.x - q.x), Mathf.Min(Mathf.Min(q.z - lo.z, hi.z - q.z), q.y - lo.y));
        float roof = q.y - WorldMapTerrain.GooCeiling(q.x, q.z);
        // Above the roof: forest ground (base) with solid rock down to the roof. Below it: the donor cave.
        float above = Mathf.Min(roof, baseValue);
        if (roof > 3f && seal < -3f) return baseValue > 0f ? Mathf.Max(baseValue, Mathf.Min(roof, 3f)) : baseValue;
        float cave = Mathf.Max(donor(), above);
        if (seal > -3f) cave = Mathf.Max(cave, Mathf.Min(seal, roof > 0f ? baseValue : seal));
        return cave;
    }

    private static float Island(Vector3 q, float baseValue, Func<float> donor) => Mathf.Max(baseValue, IslandShape(q, donor));
    /// <summary>Distance from the island centre (donor x, z) scaled by the outline's wobble: the edge is IslandRadius.</summary>
    public static float IslandRadial(float x, float z)
    {
        float a = Mathf.Atan2(z, x);
        float wobble = 1f + 0.06f * Mathf.Sin(a * 3f + 0.7f) + 0.04f * Mathf.Sin(a * 7f - 1.3f) + (Mathf.PerlinNoise(a * 1.3f + 4f, 2f) - 0.5f) * 0.12f;
        return new Vector2(x, z).magnitude / wobble;
    }

    private static float IslandShape(Vector3 q, Func<float> donor)
    {
        float R = WorldMapTerrain.IslandRadius;
        float a = Mathf.Atan2(q.z, q.x);
        float r = IslandRadial(q.x, q.z);
        if (r > R + 4f || q.y < -74f) return -10f;
        float edge = R - r;
        float s = Mathf.Clamp01(r / R);
        // The slab's underside dips from the rim to just above the donor's crust underside (-24).
        float under = q.y - (-5f - 16f * (1f - s * s));
        float slab = Mathf.Min(edge, under);
        float hanging = float.MinValue;
        if (q.y < -12f)
        {
            float lumps = 1f + (Mathf.PerlinNoise(a * 2.2f + 9f, q.y * 0.08f + 1f) - 0.5f) * 0.35f;
            float radiusAt = Mathf.Pow(Mathf.Clamp01((q.y + 72f) / 58f), 0.8f) * R * 0.72f * lumps;
            hanging = Mathf.Min(radiusAt - r, -12f - q.y + 4f);
        }
        if (slab < -3f && hanging < -3f) return Mathf.Max(slab, hanging);
        float top = slab > -3f ? Mathf.Min(donor(), slab) : slab;
        return Mathf.Max(top, hanging);
    }

    // ---------------- Floating stones up to the crystal island ----------------

    /// <summary>
    /// Moves the leaf map's spiral of floating stones (and its bubble flowers) so its altar stone lands against the
    /// crystal island's west edge, then adds stones stepping down from its foot to the flower field.
    /// </summary>
    private static void SkyPath(Transform root, List<string> log)
    {
        var islands = root.GetComponentInChildren<SkyIslands>(true);
        if (islands == null || islands.stones.Length < 2) { log.Add("No sky islands to move."); return; }
        islands.transform.SetParent(root, true);
        islands.name = "07 Floating stones — flower field up to the crystal island";

        // Height of the island's top just inside its west edge (donor Cistern ground + island lift).
        Vector3 local = new Vector3(-92f, 0f, 22f);
        float top;
        Scene source = EditorSceneManager.OpenScene(CisternPath, OpenSceneMode.Additive);
        try
        {
            var donor = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ProceduralWorld>(true)).Single();
            PrepareSampler(donor);
            donor.ColumnHeights(local, out top, out _, out _);
        }
        finally { EditorSceneManager.CloseScene(source, true); }
        Vector3 altar = WorldMapTerrain.IslandOffset + new Vector3(-104f, top + 0.4f, 26f);
        Vector3 shift = altar - islands.stones[islands.stones.Length - 1];
        islands.transform.position += shift;
        for (int i = 0; i < islands.stones.Length; i++) islands.stones[i] += shift;
        var guide = islands.GetComponentInChildren<SkyIslandBubbleGuide>(true);
        if (guide != null) foreach (var lane in guide.lanes) lane.target += shift;

        // Stairs of stones from the field up to the spiral's first stone, one easy hop apart.
        var stones = new List<Vector3>(); var radii = new List<float>(); var scenery = new List<bool>();
        Vector3 at = islands.stones[0];
        Vector3 away = islands.stones[0] - islands.stones[1]; away.y = 0f; away.Normalize();
        int added = 0;
        while (added < 60)
        {
            float ground = WorldMapTerrain.Height(at.x, at.z);
            if (at.y - ground < 2.2f) break;
            away = Quaternion.Euler(0f, 9f, 0f) * away;
            at += away * 4.6f + Vector3.down * 1.45f;
            stones.Insert(0, at); radii.Insert(0, 2.3f); scenery.Insert(0, false);
            added++;
        }
        stones.AddRange(islands.stones);
        radii.AddRange(islands.radii.Length == islands.stones.Length ? islands.radii : Enumerable.Repeat(2.5f, islands.stones.Length));
        scenery.AddRange(islands.scenery.Length == islands.stones.Length ? islands.scenery : Enumerable.Repeat(false, islands.stones.Length));
        islands.stones = stones.ToArray(); islands.radii = radii.ToArray(); islands.scenery = scenery.ToArray();
        // Flowers that stood on the leaf's ground (not on a stone) would now hang in the air: set them on the nearest stone.
        int moved = 0;
        foreach (var flower in islands.GetComponentsInChildren<BubbleFlower>(true))
        {
            Vector3 p = flower.transform.position;
            int best = 0; float bestD = float.MaxValue;
            for (int i = 0; i < islands.stones.Length; i++)
            {
                float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(islands.stones[i].x, islands.stones[i].z));
                if (d < bestD) { bestD = d; best = i; }
            }
            Vector3 s = islands.stones[best];
            bool onIt = bestD < islands.radii[best] * 0.8f && Mathf.Abs(p.y - s.y) < 1.5f;
            if (onIt) continue;
            flower.transform.position = s + new Vector3(islands.radii[best] * 0.35f, 0.15f, 0f);
            moved++;
        }
        if (moved > 0) log.Add("Floating stones: " + moved + " bubble flowers set onto stones.");
        EditorUtility.SetDirty(islands); if (guide != null) EditorUtility.SetDirty(guide);
        log.Add("Floating stones: " + islands.stones.Length + " (" + added + " new steps from the field), altar at " + altar);
    }

    // ---------------- Landmarks and player ----------------

    private static void Landmarks(Scene scene, ProceduralWorld world, List<string> log)
    {
        var spire = FindInScene<HangingSpire>(scene);
        var mother = FindInScene<BoneKiln>(scene);
        Vector2 s = WorldMapTerrain.SpireSpot, m = WorldMapTerrain.MotherHole;
        spire.transform.position = new Vector3(s.x, WorldMapTerrain.Height(s.x, s.y), s.y);
        mother.transform.position = new Vector3(m.x, WorldMapTerrain.LedgeTop, m.y);
        foreach (var (target, index) in new (Object, int)[] { (spire, 0), (mother, 1) })
        {
            var so = new SerializedObject(target);
            so.FindProperty("sinkholeOverride").intValue = index;
            so.FindProperty("placeAtThisObject").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        spire.transform.parent.name = "04 Tower ruins — Hanging Spire";
        mother.transform.parent.name = "05 Great crack — bone mother wedged in it";
        log.Add("Spire on sinkhole 0 at " + spire.transform.position + ", mother on sinkhole 1 at " + mother.transform.position);
    }

    private static void Player(Scene scene, ProceduralWorld world, List<string> log)
    {
        var carrier = FindInScene<PlayerCreatureCarrier>(scene);
        Transform player = carrier.transform.root;
        Vector2 s = WorldMapTerrain.Spawn;
        player.SetPositionAndRotation(new Vector3(s.x, WorldMapTerrain.SpawnHeight + 1.5f, s.y), Quaternion.LookRotation(new Vector3(0.15f, 0f, 1f)));
        PrefabUtility.RecordPrefabInstancePropertyModifications(player);
        log.Add("Player spawn " + player.position);
    }

    // ---------------- Review cameras and captures ----------------

    private const string CameraGroup = "Review cameras — disabled during play";

    private static void Cameras(Transform root)
    {
        var old = root.Find(CameraGroup); if (old != null) Object.DestroyImmediate(old.gameObject);
        var group = new GameObject(CameraGroup).transform; group.SetParent(root, false);
        void Cam(string name, Vector3 at, Vector3 look, float fov, bool ortho = false, float size = 0f)
        {
            var go = new GameObject(name); go.transform.SetParent(group, false);
            go.transform.position = at; go.transform.LookAt(look);
            var c = go.AddComponent<Camera>(); c.enabled = false; c.fieldOfView = fov; c.farClipPlane = 2500f;
            c.orthographic = ortho; c.orthographicSize = size;
            go.SetActive(false);
        }
        Cam("01_TopDown", new Vector3(0, 900, 15), new Vector3(0, 0, 15.01f), 30, true, 335);
        Cam("02_Oblique", new Vector3(30, 330, -470), new Vector3(10, 0, 70), 42);
        Cam("03_SpawnView", new Vector3(-10, 9, -60), new Vector3(10, 6, 60), 70);
        Cam("04_CrackAndMother", new Vector3(-45, 30, 128), new Vector3(12, -16, 162), 60);
        Cam("05_RuinsFromCrack", new Vector3(10, 22, 135), new Vector3(0, 30, 250), 60);
        Cam("06_CaveMouth", new Vector3(30, 28, -105), new Vector3(12, -2, -72), 60);
        Cam("07_IslandFromFlowers", new Vector3(-150, 40, 20), new Vector3(0, 95, 60), 60);
        Cam("08_MushroomBasin", new Vector3(20, 25, 20), new Vector3(125, -18, 40), 65);
        Cam("09_GooCaveInside", new Vector3(4, -37, -175), new Vector3(4, -40, -125), 75);
        Cam("10_FloatingStones", new Vector3(-175, 62, 20), new Vector3(-105, 60, 82), 62);
    }

    private static void Capture(Scene scene)
    {
        SceneManager.SetActiveScene(scene);
        var world = FindInScene<ProceduralWorld>(scene);
        world.Generate(true);
        LandmarkScenePreview.BuildNow();
        Directory.CreateDirectory(Captures);
        bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        var cameras = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true))
            .Where(c => c.transform.parent != null && c.transform.parent.name == CameraGroup).ToArray();
        var target = new RenderTexture(1600, 1200, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var texture = new Texture2D(1600, 1200, TextureFormat.RGB24, false);
        RenderTexture old = RenderTexture.active; bool fog = RenderSettings.fog;
        try
        {
            foreach (var camera in cameras)
            {
                camera.gameObject.SetActive(true);
                RenderSettings.fog = camera.name.StartsWith("01") || camera.name.StartsWith("02") || camera.name.StartsWith("09") ? false : fog;
                camera.targetTexture = target; camera.aspect = 4f / 3f; camera.Render(); camera.Render();
                RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 1600, 1200), 0, 0); texture.Apply();
                File.WriteAllBytes(Captures + "/" + camera.name + ".png", texture.EncodeToPNG());
                camera.targetTexture = null; camera.gameObject.SetActive(false);
            }
        }
        finally
        {
            RenderSettings.fog = fog; RenderTexture.active = old; target.Release(); Object.DestroyImmediate(target);
            Object.DestroyImmediate(texture); ShaderUtil.allowAsyncCompilation = async;
        }
    }

    /// <summary>A 2 px per metre shaded height map of the layout (no Unity scene needed), for comparing with the whiteboard.</summary>
    public static void LayoutMap()
    {
        Vector2 size = WorldMapTerrain.WorldSize;
        int w = Mathf.RoundToInt(size.x), hgt = Mathf.RoundToInt(size.y);
        var tex = new Texture2D(w * 2, hgt * 2, TextureFormat.RGB24, false);
        float H(float x, float z) => WorldMapTerrain.Height(x, z);
        Vector3 light = new Vector3(-0.6f, 0.7f, 0.4f).normalized;
        for (int j = 0; j < hgt * 2; j++) for (int i = 0; i < w * 2; i++)
        {
            float x = -size.x * 0.5f + i * 0.5f, z = -size.y * 0.5f + j * 0.5f;
            float h = H(x, z);
            Vector3 n = new Vector3(H(x - 1f, z) - H(x + 1f, z), 2f, H(x, z - 1f) - H(x, z + 1f)).normalized;
            float shade = Mathf.Clamp01(Vector3.Dot(n, light)) * 0.65f + 0.35f;
            Color c;
            if (h < -30f) c = new Color(0.25f, 0.12f, 0.35f);
            else if (h < -5f) c = Color.Lerp(new Color(0.55f, 0.35f, 0.25f), new Color(0.7f, 0.55f, 0.35f), Mathf.InverseLerp(-30f, -5f, h));
            else if (h < 20f) c = Color.Lerp(new Color(0.45f, 0.62f, 0.4f), new Color(0.72f, 0.75f, 0.5f), Mathf.InverseLerp(-5f, 20f, h));
            else if (h < 45f) c = Color.Lerp(new Color(0.72f, 0.75f, 0.5f), new Color(0.6f, 0.55f, 0.5f), Mathf.InverseLerp(20f, 45f, h));
            else c = Color.Lerp(new Color(0.6f, 0.55f, 0.5f), new Color(0.95f, 0.95f, 0.97f), Mathf.InverseLerp(45f, 110f, h));
            // Island outline, Goo graft box.
            Vector2 q = new Vector2(x - WorldMapTerrain.IslandOffset.x, z - WorldMapTerrain.IslandOffset.z);
            if (Mathf.Abs(q.magnitude - WorldMapTerrain.IslandRadius) < 1.2f) c = new Color(0.3f, 0.85f, 1f);
            else if (q.magnitude < WorldMapTerrain.IslandRadius) c = Color.Lerp(c, new Color(0.4f, 0.8f, 1f), 0.18f);
            Vector3 g0 = WorldMapTerrain.GooOffset + WorldMapTerrain.GooLower, g1 = WorldMapTerrain.GooOffset + WorldMapTerrain.GooUpper;
            bool gx = x > g0.x && x < g1.x, gz = z > g0.z && z < g1.z;
            if ((gx && (Mathf.Abs(z - g0.z) < 1f || Mathf.Abs(z - g1.z) < 1f)) || (gz && (Mathf.Abs(x - g0.x) < 1f || Mathf.Abs(x - g1.x) < 1f))) c = new Color(1f, 0.6f, 0.2f);
            tex.SetPixel(i, j, c * shade);
        }
        tex.Apply();
        Directory.CreateDirectory(Captures);
        File.WriteAllBytes(Captures + "/00_LayoutMap.png", tex.EncodeToPNG());
    }

    public static T FindInScene<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).FirstOrDefault();
}
