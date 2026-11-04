using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Two explicit milestones. Existing scene/sculpt assets are never silently replaced.</summary>
public static class GooSceneSetup
{
    public const string ScenePath = "Assets/Scenes/Scene_Goo.unity";
    public const string TerrainPath = "Assets/Terrain/Sculpted/Scene_Goo_Terrain.asset";
    public const string Generated = "Assets/Generated/Goo";
    public const string BugPath = "Assets/Prefabs/Creatures/CeilingBatBug.prefab";
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [InitializeOnLoadMethod]
    private static void RegisterEditorPreview()
    {
        EditorSceneManager.sceneOpened += (scene, mode) => { if (scene.path == ScenePath) EditorApplication.delayCall += ShowSavedTerrain; };
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += ShowSavedTerrain; };
        EditorApplication.delayCall += ShowSavedTerrain;
    }
    private static void ShowSavedTerrain()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || scene.path != ScenePath) continue;
            var world = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ProceduralWorld>()).FirstOrDefault();
            if (world != null) world.Generate(true);
        }
    }

    [MenuItem("Tools/Capstone/Goo/1 Create Level With Existing Creatures")]
    public static void CreateLevel()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null || AssetDatabase.LoadAssetAtPath<TerrainSculptData>(TerrainPath) != null)
            throw new InvalidOperationException("Scene Goo already exists. Open it and use the terrain sculpt tools; creation preserves existing work.");
        EnsureFolder(Generated);
        Scene previous = SceneManager.GetActiveScene();
        bool preservedUntitled = string.IsNullOrEmpty(previous.path);
        if (preservedUntitled)
            EditorSceneManager.SaveScene(previous, AssetDatabase.GenerateUniqueAssetPath(Generated + "/PreservedUntitled.unity"));
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Transform content = Group("Goo Level Content", null);
        Transform standalone = Group("Standalone Setup — omit when merging", null);
        GameObject audio = previous.GetRootGameObjects().FirstOrDefault(g => g.name == "WwiseGlobal");
        if (audio != null) { GameObject copy = Object.Instantiate(audio, standalone); copy.name = "WwiseGlobal"; }
        if (preservedUntitled) EditorSceneManager.CloseScene(previous, true);
        RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Sky_FractalCalm.mat");
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.28f, 0.34f, 0.4f);
        RenderSettings.ambientEquatorColor = new Color(0.11f, 0.14f, 0.17f);
        RenderSettings.ambientGroundColor = new Color(0.045f, 0.055f, 0.07f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 130f;
        RenderSettings.fogEndDistance = 300f;
        RenderSettings.fogColor = new Color(0.35f, 0.4f, 0.43f);
        var sun = Group("Sun", standalone).gameObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.4f;
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(52f, -30f, 0f);
        RenderSettings.sun = sun;

        ProceduralWorld world = Group("Procedural World — oval canyon, cavern, crater", content).gameObject.AddComponent<ProceduralWorld>();
        world.gameObject.layer = 7;
        world.gameObject.tag = "Ground";
        Edit(world, s =>
        {
            s.FindProperty("worldSize").vector2Value = new Vector2(160f, 224f);
            F(s, "cellSize", 1f); I(s, "chunkCells", 24); I(s, "seed", 7081);
            foreach (string n in new[] { "surfaceAmplitude", "ridges", "overhangAmplitude", "rimHeight", "innerDetail", "caveWidth", "caveDepth", "firstSinkholeDistance", "ceilingMirror", "innerInvert" }) F(s, n, 0f);
            F(s, "crustThickness", 40f); F(s, "innerGap", 2f); I(s, "sinkholeCount", 0);
            F(s, "blockingRadius", 300f); B(s, "placePlayerOnSurface", false);
            s.FindProperty("surfaceMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Surface.mat");
            s.FindProperty("innerMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Inner.mat");
        });
        var data = ScriptableObject.CreateInstance<TerrainSculptData>();
        data.Freeze(world.CaptureLayout(out float[] checks), checks, "Scene Goo / sketch / three connected teaching spaces v1");
        Edit(data, s => F(s, "headroom", 32f));
        AssetDatabase.CreateAsset(data, TerrainPath);
        Edit(world, s => s.FindProperty("sculptData").objectReferenceValue = data);
        world.Generate(true);
        Shape(world);

        Transform a1 = Group("01 Open-sky oval canyon — observe the food chain", content);
        Transform a2 = Group("02 Underground chamber — overhead corpse habitat", content);
        Transform a3 = Group("03 Open crater — Goo ascent", content);
        Transform markers = Group("Connections and habitat markers", content);
        Marker("ENTRY — flat ground", markers, new Vector3(0f, 0.15f, -80f));
        Marker("EXIT — crater rim", markers, new Vector3(19f, 32.1f, 64f));
        Marker("Area 2 future bug habitat", a2, new Vector3(0f, -5.3f, 9f));
        Marker("Area 3 future bug habitat", a3, new Vector3(0f, -3f, 54f));

        var recycler1 = Spawn("CompostCreature", a1, new Vector3(0f, CanyonFloor(-39f) + 0.07f, -39f), "Recycle — beneath the web");
        Edit(recycler1.GetComponent<CompostCreature>(), s => { F(s, "roamRadius", 1.4f); F(s, "walkSpeed", 0.25f); });
        var spider = Spawn("SkySpider", a1, new Vector3(0f, CanyonFloor(-39f) + 7f, -39f), "Sky Spider — canyon canopy");
        Edit(spider.GetComponent<SkySpider>(), s => { F(s, "webScale", 0.7f); F(s, "ageRate", 0f); F(s, "age", 600f); });
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.PI / 3f;
            float x = Mathf.Cos(angle) * 3.4f, z = -39f + Mathf.Sin(angle) * 3.4f;
            Spawn("BubbleFlower", a1, new Vector3(x, CanyonFloor(z) + 0.05f, z), "Bubble Flower — bird lure " + (i + 1));
        }
        var flock = Group("Canyon bird flight bounds", a1).gameObject.AddComponent<BirdFlockManager>();
        flock.SkyBounds = new Bounds(new Vector3(0f, 0f, -39f), new Vector3(28f, 20f, 34f));
        var birds = Group("24 birds — one initial population", a1).gameObject.AddComponent<BirdSpawnArea>();
        birds.transform.position = new Vector3(0f, 2f, -46f);
        birds.Configure(AssetDatabase.LoadAssetAtPath<BirdCreature>("Assets/Prefabs/Creatures/BirdCreature.prefab"), 24, 5f, 7081);
        birds.SetFlightSpeedOverride(true, new Vector2(2.5f, 4f));
        var recycler2 = Spawn("CompostCreature", a2, new Vector3(0f, -16.9f, 9f), "Recycle — beneath ceiling bugs");
        Edit(recycler2.GetComponent<CompostCreature>(), s => { F(s, "roamRadius", 1.4f); F(s, "walkSpeed", 0.25f); });

        var runtime = content.gameObject.AddComponent<GooSceneRuntime>();
        GooCreature[] drops = new GooCreature[2];
        for (int i = 0; i < 2; i++)
        {
            var go = Spawn("GooCreature", content, GateDropPosition(i), "Gate Goo drop " + (i + 1));
            drops[i] = go.GetComponent<GooCreature>();
            Edit(drops[i], s => F(s, "minimumImpactSpeed", 2f));
            go.GetComponent<Rigidbody>().isKinematic = true;
        }
        Edit(runtime, s =>
        {
            s.FindProperty("world").objectReferenceValue = world;
            var array = s.FindProperty("gateDrops"); array.arraySize = 2;
            for (int i = 0; i < 2; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = drops[i];
        });
        GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerObjects.prefab"), standalone);
        player.transform.position = new Vector3(0f, 1.1f, -80f);
        Edit(player.GetComponentInChildren<SmoothFirstPersonController>(), s => F(s, "maxSlopeAngle", 60f));
        AddFill(a2, new Vector3(0f, -6.5f, 2f), new Color(0.45f, 0.64f, 0.67f), 1.7f, 21f);
        AddFill(a2, new Vector3(0f, -3f, 29f), new Color(0.58f, 0.66f, 0.54f), 1.5f, 20f);
        AddFill(a3, new Vector3(4f, 4f, 64f), new Color(0.5f, 0.6f, 0.72f), 2f, 28f);
        AddRockyExterior();
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = world.gameObject;
        SceneView.lastActiveSceneView?.LookAt(new Vector3(0f, -1f, 10f), Quaternion.Euler(52f, -30f, 0f), 135f);
        Debug.Log("Scene Goo phase 1 saved: oval canyon, underground chamber, crater, two Recycle creatures, sky spider, plants, birds, two physical Goo drops.");
    }

    private static void Shape(ProceduralWorld world)
    {
        var field = typeof(ProceduralWorld).GetField("field", Private).GetValue(world);
        var sample = (Func<Vector3, float, float, float, float>)Delegate.CreateDelegate(typeof(Func<Vector3, float, float, float, float>), field, "Density");
        var surfaceHeight = (Func<float, float, float>)Delegate.CreateDelegate(typeof(Func<float, float, float>), field, "SurfaceHeight");
        var ceilingHeight = (Func<float, float>)Delegate.CreateDelegate(typeof(Func<float, float>), field, "CeilingHeight");
        var groundHeight = (Func<float, float, float, float>)Delegate.CreateDelegate(typeof(Func<float, float, float, float>), field, "InnerGroundHeight");
        world.SculptData.ClearEdits();
        Vector3 min = world.GridMinimum;
        Vector3Int count = Vector3Int.RoundToInt(world.VolumeBounds.size / world.CellSize);
        for (int z = 0; z <= count.z; z++) for (int x = 0; x <= count.x; x++)
        {
            Vector3 column = min + new Vector3(x, 0f, z) * world.CellSize;
            if (Mathf.Abs(column.x) > 60f || column.z < -84f || column.z > 111f) continue;
            float h = surfaceHeight(column.x, column.z), c = ceilingHeight(h), f = groundHeight(column.x, column.z, h);
            for (int y = 0; y <= count.y; y++)
            {
                Vector3 p = min + new Vector3(x, y, z) * world.CellSize;
                if (p.y < -27f || p.y > 40f) continue;
                float original = sample(p, h, c, f);
                float desired = Density(p);
                float delta = desired - original;
                if (Mathf.Abs(delta) > 0.001f) world.SculptData.SetEdit(new Vector3Int(x, y, z), delta);
            }
        }
        EditorUtility.SetDirty(world.SculptData);
        world.Generate(true);
        Physics.SyncTransforms();
    }

    private static float OriginalCanyonFloor(float z) => -14f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-75f, -12f, z));
    public static float CanyonFloor(float z)
    {
        float terrace = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-51f, -46f, z)) *
            (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-32f, -27f, z)));
        return Mathf.Lerp(OriginalCanyonFloor(z), OriginalCanyonFloor(-39f), terrace);
    }
    public static float GateFloor(float z)
    {
        if (z < 28f) return -17f;
        float theta = Mathf.Asin(Mathf.Clamp(0.5f + (z - 28f) / 10f, 0.5f, Mathf.Sin(86f * Mathf.Deg2Rad)));
        return -17f + 10f * (Mathf.Cos(30f * Mathf.Deg2Rad) - Mathf.Cos(theta));
    }
    public static Vector3 GateDropPosition(int index)
    {
        // Clear the sphere's radius from the upper mesh edge, so impacts use the steep face normal.
        float z = index == 0 ? 31.85f : 34.7f;
        return new Vector3(CaveCentre(z) + (index == 0 ? -0.3f : 0.3f), 0f, z);
    }

    public static float Density(Vector3 p)
    {
        float dx = p.x - 4f, dz = p.z - 64f;
        float radial = Mathf.Sqrt(dx * dx + dz * dz);
        float angle = Mathf.Atan2(dz, dx);
        float r = radial / (1f + 0.035f * Mathf.Sin(angle * 5f));
        float cliffs = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(angle - Mathf.PI)), 8f) +
            0.8f * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(angle + Mathf.PI * 0.25f)), 10f);
        float foot = 44f - cliffs * 18f;
        // Broad eastern and northern flanks are walkable; compressed west/southeast flanks become cliffs.
        float mountain = 32f * Mathf.Clamp01((foot - r) / (foot - 14f));
        mountain += Mathf.Clamp01(mountain / 10f) * (Mathf.Sin(angle * 5f + r * 0.12f) * 0.7f + Mathf.Sin(angle * 9f - r * 0.17f) * 0.35f);
        float shoulder = 10f * Mathf.Clamp01(1f - p.x * p.x / (24f * 24f) - (p.z - 35f) * (p.z - 35f) / (38f * 38f));
        float h = Mathf.Max(mountain, shoulder);
        // Same flat crust as the frozen generator, with an authored mountain above it.
        float density = Mathf.Max(Mathf.Min(h - p.y, p.y + 40f), -42f - p.y);
        float rough = Mathf.Sin(p.x * 1.1f + p.z * 0.43f) * Mathf.Sin(p.y * 0.8f - p.z * 0.32f) * 0.16f;
        float oval = Ellipse(p.x, p.z, 0f, -43f, 19f, 35f);
        density = Mathf.Min(density, Mathf.Max(oval + rough, CanyonFloor(p.z) - p.y));
        float cave = WindingCave(p, rough, h);
        density = Mathf.Min(density, cave);
        float cavityRadius = ShaftRadius(p.y, angle);
        float crater = Mathf.Max(radial - cavityRadius + rough * 2f, -9f - p.y);
        density = Mathf.Min(density, crater);
        // An eroded saddle breaks the upper wall into a short walkable finish, rather than another timed climb.
        float notchPlan = Mathf.Max(12f - p.x, Mathf.Max(p.x - 24f, Mathf.Abs(p.z - 64f) - 3.4f));
        float notchFloor = 26.5f + (p.x - 12f) * 0.65f;
        density = Mathf.Min(density, Mathf.Max(notchPlan, notchFloor - p.y));
        density = Mathf.Max(density, FracturedShell(p));
        return density;
    }

    private static readonly Vector2[] CaveBends =
    {
        new Vector2(0f, -20f), new Vector2(-5.5f, -6f), new Vector2(0f, 9f),
        new Vector2(8f, 22f), new Vector2(6f, 29f), new Vector2(6.5f, 39f),
        new Vector2(5f, 48f), new Vector2(4f, 60f),
    };
    private static readonly float[] CaveWidths = { 5.4f, 6.6f, 9f, 9.5f, 8.5f, 7.8f, 7f, 5f };
    public static float CaveCentre(float z)
    {
        for (int i = 1; i < CaveBends.Length; i++)
            if (z <= CaveBends[i].y) return Mathf.Lerp(CaveBends[i - 1].x, CaveBends[i].x, Mathf.InverseLerp(CaveBends[i - 1].y, CaveBends[i].y, z));
        return CaveBends[CaveBends.Length - 1].x;
    }
    public static float CaveFloor(float z) => z < 4f ? Mathf.Lerp(-14f, -17f, Mathf.InverseLerp(-18f, 4f, z)) : GateFloor(z);
    private static float WindingCave(Vector3 p, float rough, float surface)
    {
        Vector2 column = new Vector2(p.x, p.z);
        float air = float.PositiveInfinity;
        for (int i = 1; i < CaveBends.Length; i++)
        {
            Vector2 a = CaveBends[i - 1], d = CaveBends[i] - a;
            float t = Mathf.Clamp01(Vector2.Dot(column - a, d) / d.sqrMagnitude);
            Vector2 closest = a + d * t;
            float width = Mathf.Lerp(CaveWidths[i - 1], CaveWidths[i], t);
            float floor = CaveFloor(p.z);
            float height = Mathf.Lerp(5.8f, 7f, Mathf.InverseLerp(17f, 29f, p.z)) + Mathf.Sin(p.z * 0.2f) * 0.35f;
            height += 4.5f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(12f, 27f, p.z));
            height += 12f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 56f, p.z));
            height = Mathf.Min(height, Mathf.Max(5.5f, (surface - 3f - floor) * 0.5f));
            float horizontal = Vector2.Distance(column, closest) / width;
            float vertical = (p.y - floor - height) / height;
            float tunnel = (Mathf.Sqrt(horizontal * horizontal + vertical * vertical) - 1f) * width + rough;
            air = Mathf.Min(air, Mathf.Max(tunnel, floor - p.y));
        }
        // A generous central chamber leads to the dominant exit arch; side openings remain low nooks.
        air = Mathf.Min(air, Room(p, new Vector3(0f, -10.5f, 9f), new Vector3(14f, 7f, 16f), -17f));
        air = Mathf.Min(air, Room(p, new Vector3(-10f, -13.5f, 1f), new Vector3(5f, 2.8f, 2.1f), -17f));
        air = Mathf.Min(air, Room(p, new Vector3(-14f, -13f, 0f), new Vector3(4.5f, 3.6f, 5f), -17.2f));
        air = Mathf.Min(air, Room(p, new Vector3(12f, -13f, 18f), new Vector3(3f, 2.6f, 2.3f), -17f));
        air = Mathf.Min(air, Room(p, new Vector3(16f, -13f, 18f), new Vector3(4.3f, 3.2f, 5f), -17.5f));
        return air;
    }
    private static float Room(Vector3 p, Vector3 centre, Vector3 size, float floor)
    {
        Vector3 q = p - centre;
        return Mathf.Max((new Vector3(q.x / size.x, q.y / size.y, q.z / size.z).magnitude - 1f) * Mathf.Min(size.x, size.y, size.z), floor - p.y);
    }
    public static float ShaftRadius(float y, float angle)
    {
        float radius = 5.8f + 0.065f * (y + 9f) + Mathf.Sin(angle * 3f + y * 0.11f) * 0.45f;
        for (int i = 0; i < 3; i++)
        {
            float height = -1f + i * 11f + Mathf.Sin(angle * (i + 2f) + i * 1.6f) * 3.2f;
            float width = 0.45f + 1.8f * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(angle * 1.5f + i * 0.7f)), 2f);
            radius += width * Mathf.SmoothStep(0f, 1f, (y - height) / (0.85f + 0.4f * (1f + Mathf.Sin(angle * 3f + i))));
        }
        return radius;
    }

    private static float FracturedShell(Vector3 p)
    {
        Vector3 q = p - new Vector3(0f, -4f, -43f);
        float widthVariation = 1f + Mathf.Sin(p.z * 0.17f) * 0.1f + Mathf.Sin(p.z * 0.41f) * 0.06f;
        float heightVariation = 1f + Mathf.Sin(p.z * 0.12f + 1f) * 0.13f + Mathf.Sin(p.x * 0.22f + p.z * 0.19f) * 0.06f;
        q.x /= widthVariation;
        q.y /= heightVariation;
        float outer = (new Vector3(q.x / 25f, q.y / 20f, q.z / 39f).magnitude - 1f) * 20f;
        float inner = (new Vector3(q.x / 21.8f, q.y / 17.2f, q.z / 35.8f).magnitude - 1f) * 17f;
        float rock = Mathf.Min(Mathf.Min(-outer, inner), p.y + 1f);
        Vector3 tallWave = new Vector3(p.x, p.y / 1.55f, p.z);
        rock = Mathf.Max(rock, Mathf.Max(Wave(tallWave, false), Wave(tallWave, true)));
        // A torn, long skylight keeps the encounter open while the shell curls far overhead on both sides.
        float opening = Ellipse(p.x, p.z, Mathf.Sin(p.z * 0.13f) * 1.1f, -42f, 5.2f, 37f) +
            Mathf.Sin(p.z * 0.61f + p.x * 0.3f) * 0.4f + Mathf.Sin(p.z * 1.2f) * 0.2f;
        rock = Mathf.Min(rock, Mathf.Max(opening, 6f - p.y));
        // Break away the front face so the shell reads as a navigable, shattered mouth.
        float mouth = Mathf.Max(Mathf.Abs(p.x) - 11f, Mathf.Max(p.z + 69f, p.y - 12f));
        rock = Mathf.Min(rock, mouth);
        for (int i = 0; i < 5; i++)
        {
            float z = -66f + i * 12f;
            float slit = Mathf.Max(Mathf.Abs(p.z - z - p.x * (i % 2 == 0 ? 0.23f : -0.17f)) - 0.65f,
                8f + i % 3 - p.y);
            rock = Mathf.Min(rock, slit);
        }
        // Uneven leaning stone shards, fused into the shell rather than placed as separate props.
        for (int side = -1; side <= 1; side += 2) for (int i = 0; i < ShardZ.Length; i++)
        {
            float z = ShardZ[i] + (side > 0 ? 3f + Mathf.Sin(i * 1.9f) * 3f : 0f);
            float x = side * (11.8f + Mathf.Sin(i * 1.9f + side * 0.4f) * 2.8f);
            float y = -4f + 20f * Mathf.Sqrt(Mathf.Max(0.05f, 1f - x * x / 625f - (z + 43f) * (z + 43f) / 1521f));
            Vector3 a = new Vector3(x, y - 1.7f, z);
            Vector3 b = a + new Vector3(-side * (2f + i % 3), ShardHeight[i] * (side > 0 ? 0.82f : 1f), i % 2 == 0 ? -5f : 3f);
            rock = Mathf.Max(rock, StoneShard(p, a, b, ShardWidth[i]));
        }
        return rock;
    }
    private static readonly float[] ShardZ = { -70f, -62f, -48f, -39f, -27f, -18f, -9f };
    private static readonly float[] ShardHeight = { 4.8f, 2.8f, 6.2f, 3.1f, 7.3f, 4.3f, 2.7f };
    private static readonly float[] ShardWidth = { 2.2f, 3.2f, 1.6f, 2.8f, 1.8f, 2.6f, 1.4f };

    private static float StoneShard(Vector3 p, Vector3 a, Vector3 b, float radius)
    {
        Vector3 axis = b - a;
        float length = axis.magnitude;
        float along = Vector3.Dot(p - a, axis / length);
        Vector3 radial = p - a - axis / length * along;
        float r = Mathf.Lerp(radius, 0.1f, Mathf.Clamp01(along / length));
        float side = Mathf.Max(Mathf.Abs(radial.x), Mathf.Max(Mathf.Abs(radial.y), Mathf.Abs(radial.z))) - r;
        return -Mathf.Max(side, Mathf.Max(-along, along - length));
    }

    private static readonly Vector2[] Curl = { new Vector2(23f, -1f), new Vector2(22f, 3f), new Vector2(19f, 7f), new Vector2(14f, 9f), new Vector2(9f, 8f) };
    private static float Wave(Vector3 p, bool right)
    {
        Vector3 q = new Vector3(p.x * (right ? 1f : -1f), p.y / (right ? 0.8f : 1f), (p.z - (right ? -35f : -54f)) * 0.3f);
        float best = float.NegativeInfinity;
        for (int i = 1; i < Curl.Length; i++)
        {
            Vector3 a = new Vector3(Curl[i - 1].x, Curl[i - 1].y, 0f), b = new Vector3(Curl[i].x, Curl[i].y, 0f);
            float t = Mathf.Clamp01(Vector3.Dot(q - a, b - a) / (b - a).sqrMagnitude);
            float radius = Mathf.Lerp(2.6f, 1.3f, (i - 1f + t) / (Curl.Length - 1f));
            best = Mathf.Max(best, radius - Vector3.Distance(q, Vector3.Lerp(a, b, t)));
        }
        return best;
    }
    private static float Ellipse(float x, float z, float cx, float cz, float rx, float rz) =>
        (new Vector2((x - cx) / rx, (z - cz) / rz).magnitude - 1f) * Mathf.Min(rx, rz);

    [MenuItem("Tools/Capstone/Goo/Add Rocky Tundra Exterior")]
    public static void AddRockyExterior()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != ScenePath)
            throw new InvalidOperationException("Open Scene Goo in Edit Mode first.");
        var content = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "Goo Level Content").transform;
        if (content.Find("Rocky tundra — scattered stone and ground shadows") != null) return;
        var world = content.GetComponentInChildren<ProceduralWorld>();
        if (!world.LayoutReady) world.Generate(true);
        Transform group = Group("Rocky tundra — scattered stone and ground shadows", content);
        string path = Generated + "/TundraStone.mat";
        Material stone = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (stone == null)
        {
            stone = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Decor_Stone.mat"));
            stone.name = "Tundra Stone";
            stone.SetColor("_Color", new Color(0.24f, 0.27f, 0.29f));
            stone.SetColor("_ShadowColor", new Color(0.025f, 0.035f, 0.045f));
            stone.SetFloat("_GlitchAmount", 0f); stone.SetFloat("_HatchStrength", 0.45f);
            AssetDatabase.CreateAsset(stone, path);
        }
        var random = new System.Random(7084);
        int placed = 0;
        for (int attempt = 0; attempt < 1000 && placed < 96; attempt++)
        {
            float x = -68f + (float)random.NextDouble() * 136f;
            float z = -99f + (float)random.NextDouble() * 198f;
            if (Ellipse(x, z, 0f, -43f, 31f, 45f) < 0f || new Vector2(x, z + 80f).magnitude < 13f) continue;
            float angle = Mathf.Atan2(z - 64f, x - 4f);
            float radial = Vector2.Distance(new Vector2(x, z), new Vector2(4f, 64f));
            if (radial < 18f || (radial < 46f && (Mathf.Abs(angle) < 0.2f || Mathf.Abs(angle - Mathf.PI * 0.5f) < 0.22f))) continue;
            RaycastHit hit = FindTerrainHit(world, new Vector3(x, 49f, z), Vector3.down, 65f);
            if (Vector3.Angle(hit.normal, Vector3.up) > 57f) continue;
            string kind = placed % 4 == 0 ? "Shards" : "Pebbles";
            string prefab = "Assets/Prefabs/WorldDecor/Decor_" + kind + "_" + random.Next(6) + ".prefab";
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefab), group);
            go.name = "Tundra " + kind + " " + (placed + 1);
            go.transform.position = hit.point;
            go.transform.rotation = Quaternion.AngleAxis((float)random.NextDouble() * 360f, Vector3.up);
            float scale = 0.55f + (float)random.NextDouble() * 1.6f;
            if (placed % 11 == 0) scale *= 1.7f;
            go.transform.localScale = new Vector3(scale * (0.8f + (float)random.NextDouble() * 0.5f), scale, scale);
            var renderer = go.GetComponentInChildren<Renderer>();
            renderer.sharedMaterial = stone;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            Vector3 p = go.transform.position; p.y += hit.point.y - renderer.bounds.min.y - 0.12f; go.transform.position = p;
            placed++;
        }
        var sun = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>()).First(l => l.type == LightType.Directional);
        sun.transform.rotation = Quaternion.Euler(33f, -40f, 0f); sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.85f;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }

    [MenuItem("Tools/Capstone/Goo/2 Add Or Update Ceiling Bat Bugs")]
    public static void AddBugs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != ScenePath)
            throw new InvalidOperationException("Open Scene Goo in Edit Mode first.");
        var content = SceneManager.GetActiveScene().GetRootGameObjects().Single(g => g.name == "Goo Level Content").transform;
        CreateBugPrefab();
        var world = content.GetComponentInChildren<ProceduralWorld>();
        if (!world.LayoutReady) world.Generate(true);
        Transform colony = content.Find("Ceiling bat bugs — local eggs and moving holds");
        if (colony == null) colony = content.Find("Ceiling bat bugs — finite population, eggs deferred");
        if (colony == null) colony = Group("Ceiling bat bugs — finite population, eggs deferred", content);
        var random = new System.Random(7082);
        for (int i = 0; i < 36; i++)
        {
            Vector3 origin, direction;
            if (i < 24)
            {
                origin = new Vector3((float)random.NextDouble() * 2.4f - 1.2f, -12f, 8f + (float)random.NextDouble() * 3f);
                direction = Vector3.up;
            }
            else
            {
                float a = (10f + (i - 24) * 160f / 11f) * Mathf.Deg2Rad;
                origin = new Vector3(4f, 6f + (i % 3) * 7f, 64f);
                direction = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
            RaycastHit hit = FindTerrainHit(world, origin, direction, 35f);
            string bugName = (i < 24 ? "Cavern" : "Crater") + " Bat Bug " + (i + 1);
            if (colony.Find(bugName) != null) { random.NextDouble(); random.NextDouble(); continue; }
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(BugPath), colony);
            go.name = bugName;
            go.transform.SetPositionAndRotation(hit.point + hit.normal * 0.22f,
                Quaternion.LookRotation(Vector3.Cross(hit.normal, Mathf.Abs(hit.normal.y) > 0.9f ? Vector3.right : Vector3.up), hit.normal));
            Edit(go.GetComponent<CeilingBatBug>(), s =>
            {
                s.FindProperty("habitatWorld").objectReferenceValue = world;
                F(s, "age", 20f + (float)random.NextDouble() * 40f);
            });
            Edit(go.GetComponent<CeilingBatBugSurfaceMotor>(), s => { F(s, "speed", 0.8f + (float)random.NextDouble() * 0.6f); F(s, "roamRadius", i < 24 ? 1.5f : 6f); });
        }
        CeilingBatBugSetup.Install();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
    }

    private static void CreateBugPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(BugPath) != null) return;
        Material shell = MaterialAsset("BatBugShell", new Color(0.075f, 0.095f, 0.085f));
        Material amber = MaterialAsset("BatBugAmber", new Color(0.72f, 0.47f, 0.08f));
        GameObject root = new GameObject("Ceiling Bat Bug");
        try
        {
            var body = root.AddComponent<Rigidbody>(); body.mass = 0.2f; body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            root.AddComponent<SphereCollider>().radius = 0.19f;
            var bug = root.AddComponent<CeilingBatBug>();
            Primitive(root.transform, "Rock shell", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.43f, 0.25f, 0.52f), shell);
            Primitive(root.transform, "Amber face", PrimitiveType.Sphere, new Vector3(0f, 0.035f, 0.23f), new Vector3(0.14f, 0.09f, 0.07f), amber);
            Transform[] legs = new Transform[6];
            for (int i = 0; i < 6; i++)
            {
                float side = i < 3 ? -1f : 1f;
                legs[i] = Group("Leg " + i, root.transform);
                legs[i].localPosition = new Vector3(side * 0.13f, -0.035f, ((i % 3) - 1) * 0.16f);
                Primitive(legs[i], "Short leg", PrimitiveType.Capsule, new Vector3(side * 0.12f, -0.04f, 0f), new Vector3(0.08f, 0.16f, 0.08f), shell).localRotation = Quaternion.Euler(0f, 0f, side * 62f);
                Primitive(legs[i], "Amber toe", PrimitiveType.Sphere, new Vector3(side * 0.24f, -0.09f, 0f), Vector3.one * 0.045f, amber);
            }
            Edit(bug, s =>
            {
                F(s, "age", 60f); F(s, "ageRate", 1f); F(s, "nutrition", 120f);
                var array = s.FindProperty("legs"); array.arraySize = 6;
                for (int i = 0; i < 6; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = legs[i];
            });
            PrefabUtility.SaveAsPrefabAsset(root, BugPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    public static RaycastHit FindTerrainHit(ProceduralWorld world, Vector3 origin, Vector3 direction, float distance)
    {
        Physics.SyncTransforms();
        foreach (var hit in Physics.RaycastAll(origin, direction, distance, 1 << 7, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            if (world.OwnsCollider(hit.collider)) return hit;
        throw new InvalidOperationException("No generated terrain at " + origin + " toward " + direction);
    }
    private static Transform Primitive(Transform parent, string name, PrimitiveType type, Vector3 at, Vector3 scale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = at; go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go.transform;
    }
    private static Material MaterialAsset(string name, Color color)
    {
        string path = Generated + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, path); return material;
    }
    private static void AddFill(Transform parent, Vector3 position, Color color, float intensity, float range)
    {
        var light = Group("Cave readability fill", parent).gameObject.AddComponent<Light>();
        light.transform.position = position; light.type = LightType.Point; light.color = color; light.intensity = intensity; light.range = range;
    }
    private static void Marker(string name, Transform parent, Vector3 at) { Group(name, parent).position = at; }
    private static Transform Group(string name, Transform parent)
    {
        var go = new GameObject(name); if (parent != null) go.transform.SetParent(parent, false); return go.transform;
    }
    private static GameObject Spawn(string prefab, Transform parent, Vector3 at, string name)
    {
        var template = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/" + prefab + ".prefab");
        if (template == null) throw new InvalidOperationException("Missing creature prefab: " + prefab);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(template, parent); go.name = name; go.transform.position = at; return go;
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/')); EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }
    private static void Edit(Object target, Action<SerializedObject> action)
    {
        using (var s = new SerializedObject(target)) { action(s); s.ApplyModifiedPropertiesWithoutUndo(); }
    }
    private static void F(SerializedObject s, string n, float value) => s.FindProperty(n).floatValue = value;
    private static void I(SerializedObject s, string n, int value) => s.FindProperty(n).intValue = value;
    private static void B(SerializedObject s, string n, bool value) => s.FindProperty(n).boolValue = value;
}
