using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Seeds a sculptable tutorial using Daniel's existing ProceduralWorld and saved-density workflow.</summary>
public static class YoTutorialTerrainSetup
{
    private const string DataPath = "Assets/Terrain/Sculpted/Scene_YoTutorial_Terrain.asset";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Capstone/Yo Tutorial/Generate Sculptable Terrain Opening")]
    public static void CreateBase()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/Scene_YoTutorial.unity")
            throw new InvalidOperationException("Open Yo Tutorial in Edit Mode first.");
        if (Object.FindObjectsByType<ProceduralWorld>(FindObjectsSortMode.None).Any(w => w.gameObject.scene == scene))
            throw new InvalidOperationException("A procedural world already exists. Use the Terrain Sculpt window to edit it.");
        if (AssetDatabase.LoadAssetAtPath<TerrainSculptData>(DataPath) != null)
            throw new InvalidOperationException("The tutorial terrain asset already exists; preserve it and sculpt it instead of overwriting it.");

        var worldObject = new GameObject("Procedural World — Sculptable Yo Tutorial");
        worldObject.transform.position = new Vector3(0f, 0f, 104f);
        worldObject.layer = LayerMask.NameToLayer("Ground");
        worldObject.tag = "Ground";
        var world = worldObject.AddComponent<ProceduralWorld>();
        var settings = new SerializedObject(world);
        settings.FindProperty("worldSize").vector2Value = new Vector2(224f, 272f);
        settings.FindProperty("cellSize").floatValue = 1.5f;
        settings.FindProperty("chunkCells").intValue = 24;
        settings.FindProperty("seed").intValue = 1;
        Float(settings, "surfaceAmplitude", 6f);
        Float(settings, "surfaceFrequency", 0.018f);
        Float(settings, "ridges", 0.45f);
        Float(settings, "overhangAmplitude", 1.8f);
        Float(settings, "spawnFlatRadius", 48f);
        Float(settings, "spawnBlend", 28f);
        Float(settings, "rimHeight", 26f);
        settings.FindProperty("basin").boolValue = true;
        Float(settings, "basinFloor", 0.7f);
        Float(settings, "basinSlope", 0.26f);
        Float(settings, "crustThickness", 10f);
        Float(settings, "innerGap", 22f);
        Float(settings, "ceilingMirror", 0.65f);
        Float(settings, "innerInvert", 0.7f);
        Float(settings, "innerDetail", 2f);
        Float(settings, "caveWidth", 0.035f);
        settings.FindProperty("sinkholeCount").intValue = 0;
        Float(settings, "firstSinkholeDistance", 0f);
        Float(settings, "blockingRadius", 65f);
        settings.FindProperty("placePlayerOnSurface").boolValue = false;
        settings.FindProperty("surfaceMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Surface.mat");
        settings.FindProperty("innerMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Inner.mat");
        settings.ApplyModifiedPropertiesWithoutUndo();

        var data = ScriptableObject.CreateInstance<TerrainSculptData>();
        data.Freeze(world.CaptureLayout(out float[] checks), checks, "Yo Tutorial / existing ProceduralWorld generator");
        AssetDatabase.CreateAsset(data, DataPath);
        settings.Update();
        settings.FindProperty("sculptData").objectReferenceValue = data;
        settings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        world.Generate(true);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>One-time density edits, saved in the same asset that the existing sculpt brushes edit.</summary>
    public static object ShapeOpening()
    {
        var world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null || !world.CanSculpt || world.gameObject.scene.path != "Assets/Scenes/Scene_YoTutorial.unity")
            throw new InvalidOperationException("Generate the Yo Tutorial base first.");
        if (world.SculptData.Edits.Count != 0)
            throw new InvalidOperationException("Terrain already has edits; use the sculpt brushes instead of replacing them.");
        var field = typeof(ProceduralWorld).GetField("field", Private).GetValue(world);
        var generatedDensity = (Func<Vector3, float, float, float, float>)Delegate.CreateDelegate(
            typeof(Func<Vector3, float, float, float, float>), field, "Density");
        Vector3 grid = world.GridMinimum;
        float cell = world.CellSize;
        int edits = 0;
        var min = Vector3Int.FloorToInt((new Vector3(-77f, -43f, -39f) - grid) / cell);
        var max = Vector3Int.CeilToInt((new Vector3(77f, 36f, 221f) - grid) / cell);
        for (int z = min.z; z <= max.z; z++)
        for (int x = min.x; x <= max.x; x++)
        {
            Vector3 column = grid + new Vector3(x * cell, 0f, z * cell);
            bool cave = Mathf.Abs(column.x) < 30f && column.z > -38f && column.z < 83f;
            bool outside = column.z > 77f && column.z < 220f && Mathf.Abs(column.x) < 76f;
            if (!cave && !outside) continue;
            world.ColumnHeights(column, out float surface, out float ceiling, out float innerFloor);
            float surfaceHeight = OutdoorHeight(column.x, column.z, surface);
            float horizontalBlend = Mathf.SmoothStep(0f, 1f, (76f - Mathf.Abs(column.x)) / 7f) *
                Mathf.SmoothStep(0f, 1f, (220f - column.z) / 8f) * Mathf.SmoothStep(0f, 1f, (column.z - 77f) / 5f);
            for (int y = min.y; y <= max.y; y++)
            {
                Vector3 point = grid + new Vector3(x, y, z) * cell;
                if ((!cave || point.y > 10f) && (!outside || point.y < -6f)) continue;
                float original = generatedDensity(point - world.transform.position, surface, ceiling, innerFloor);
                float density = original;
                if (cave)
                    density = OpeningDensity(point, original, true);
                if (outside && point.y > -6f)
                    density = Mathf.Lerp(density, surfaceHeight - point.y,
                        horizontalBlend * Mathf.SmoothStep(0f, 1f, (point.y + 6f) / 3f));
                float delta = density - original;
                if (Mathf.Abs(delta) < 0.001f) continue;
                world.SculptData.SetEdit(new Vector3Int(x, y, z), delta);
                edits++;
            }
        }
        EditorUtility.SetDirty(world.SculptData);
        AssetDatabase.SaveAssets();
        world.SyncSculptFromData();
        PlaceGameplay(world);
        TerrainSculptWindow.Open();
        Selection.activeGameObject = world.gameObject;
        var view = SceneView.lastActiveSceneView;
        if (view != null) view.LookAt(new Vector3(0f, -8f, 85f), Quaternion.Euler(48f, -30f, 0f), 130f);
        EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        EditorSceneManager.SaveScene(world.gameObject.scene);
        return new { edits, sculptAsset = DataPath, undergroundSpawn = "(0, -27.8, -24)", bounceRise = "8m" };
    }

    // The original profile is retained for applying only the shape difference to an already sculpted cave.
    private static float OpeningDensity(Vector3 point, float original, bool organic)
    {
        float enclosure = -RoundBox(point, new Vector3(0f, -22f, 3f), new Vector3(25f, 19f, 42f), 3f);
        float exitEnclosure = -RoundBox(point, new Vector3(0f, -19f, 56f), new Vector3(10f, 21f, 26f), 3f);
        float density = Mathf.Max(original, Mathf.Max(enclosure, exitEnclosure));
        float room = RoundBox(point, new Vector3(0f, -24f, -24f), new Vector3(5.5f, 5f, 8f), 1.5f);
        float corridor = RoundBox(point, new Vector3(0f, -24f, -10f), new Vector3(3.4f, 5f, 10f), 1f);
        float floor = Mathf.Lerp(-29f, -34f, Mathf.SmoothStep(0f, 1f, (point.z - 13f) / 9f));
        float chamberPlan = RoundBox(new Vector3(point.x, 0f, point.z), new Vector3(0f, 0f, 15f), new Vector3(14f, 30f, 19f), 2f);
        float ceiling = -12f;
        if (organic)
        {
            var offset = point - new Vector3(0f, -23.6f, -25f);
            float roughness = (Mathf.PerlinNoise(point.x * 0.23f + 31f, point.z * 0.19f + 71f) - 0.5f) * 1.1f
                + Mathf.Sin(point.y * 0.9f + point.z * 0.35f) * 0.15f;
            room = Mathf.Max((new Vector3(offset.x / 6.3f, offset.y / 6f, offset.z / 8.2f).magnitude - 1f) * 6f + roughness, -29f - point.y);
            corridor = CurvedPassage(point, roughness);
            float plan = Mathf.Min(Ellipse(point, new Vector2(0f, 14f), new Vector2(14f, 20.5f)),
                Mathf.Min(Ellipse(point, new Vector2(-7f, 8f), new Vector2(9f, 9.5f)),
                    Ellipse(point, new Vector2(7f, 18f), new Vector2(8.5f, 10f))));
            float blend = 1f - Mathf.SmoothStep(0f, 1f, (point.z - 27f) / 5f);
            chamberPlan = Mathf.Lerp(chamberPlan, plan + roughness, blend);
            ceiling = Mathf.Lerp(-12f, -12.5f - 0.012f * point.x * point.x + Mathf.Sin(point.z * 0.22f) * 0.8f + roughness, blend);
        }
        float chamber = Mathf.Max(chamberPlan, Mathf.Max(floor - point.y, point.y - ceiling));
        float rampFloor = Mathf.Lerp(-26f, 1f, Mathf.Clamp01((point.z - 34f) / 43f));
        float rampPlan = RoundBox(new Vector3(point.x, 0f, point.z), new Vector3(0f, 0f, 56f), new Vector3(4.8f, 30f, 24f), 1f);
        float ramp = Mathf.Max(rampPlan, Mathf.Max(rampFloor - point.y, point.y - (rampFloor + 7.5f)));
        return Mathf.Min(density, Mathf.Min(Mathf.Min(room, corridor), Mathf.Min(chamber, ramp)));
    }

    private static readonly Vector2[] PassageBends =
    {
        new Vector2(0f, -24f), new Vector2(2.8f, -18f), new Vector2(4.6f, -13f),
        new Vector2(-3.6f, -6f), new Vector2(-3.3f, -1f), new Vector2(0f, 5f),
    };

    private static float CurvedPassage(Vector3 point, float roughness)
    {
        var column = new Vector2(point.x, point.z);
        float distance = float.MaxValue;
        for (int i = 1; i < PassageBends.Length; i++)
        {
            Vector2 a = PassageBends[i - 1], direction = PassageBends[i] - a;
            float along = Mathf.Clamp01(Vector2.Dot(column - a, direction) / direction.sqrMagnitude);
            distance = Mathf.Min(distance, Vector2.Distance(column, a + direction * along));
        }
        float radius = 3.15f + Mathf.Sin(point.z * 0.37f) * 0.25f;
        float vertical = (point.y + 24.3f) / 4.7f;
        float tunnel = (Mathf.Sqrt(distance * distance / (radius * radius) + vertical * vertical) - 1f) * radius + roughness * 0.4f;
        return Mathf.Max(tunnel, -29f - point.y);
    }

    private static float Ellipse(Vector3 point, Vector2 centre, Vector2 radius)
    {
        var delta = new Vector2((point.x - centre.x) / radius.x, (point.z - centre.y) / radius.y);
        return (delta.magnitude - 1f) * Mathf.Min(radius.x, radius.y);
    }

    private static float OutdoorHeight(float x, float z, float original)
    {
        float h = original + Hill(x, z, -9f, 105f, 19f, 24f, 18f) + Hill(x, z, 13f, 126f, 12f, 9f, 8f) + Hill(x, z, 8f, 156f, 14f, 11f, 7f);
        foreach (var centre in new[] { new Vector3(0f, 1f, 84f), new Vector3(29f, 0f, 102f), new Vector3(44f, 0f, 134f), new Vector3(23f, 0f, 165f), new Vector3(0f, 2f, 196f) })
        {
            float radius = centre.z > 190f ? 23f : 17f;
            float blend = 1f - Mathf.SmoothStep(0f, 1f, (Vector2.Distance(new Vector2(x, z), new Vector2(centre.x, centre.z)) - radius) / 7f);
            h = Mathf.Lerp(h, centre.y, blend);
        }
        return h;
    }

    private static float Hill(float x, float z, float cx, float cz, float rx, float rz, float height)
    {
        float dx = (x - cx) / rx, dz = (z - cz) / rz;
        return height * Mathf.Exp(-(dx * dx + dz * dz) * 1.5f);
    }

    private static float RoundBox(Vector3 point, Vector3 centre, Vector3 half, float radius)
    {
        Vector3 offset = point - centre;
        var q = new Vector3(Mathf.Abs(offset.x), Mathf.Abs(offset.y), Mathf.Abs(offset.z)) - half + Vector3.one * radius;
        return Vector3.Max(q, Vector3.zero).magnitude + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f) - radius;
    }

    private static void PlaceGameplay(ProceduralWorld world)
    {
        Scene scene = world.gameObject.scene;
        var reference = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Yo Tutorial");
        var gameplay = new GameObject("Tutorial Gameplay — movable placements").transform;
        var hud = Object.FindFirstObjectByType<InteractionPromptHUD>();
        if (hud != null) hud.transform.SetParent(gameplay, true);
        var trigger = Object.FindFirstObjectByType<InteractionHintsTrigger>();
        if (trigger != null)
        {
            trigger.transform.SetParent(gameplay, true);
            trigger.transform.position = new Vector3(0f, 4f, 83f);
            trigger.GetComponent<BoxCollider>().size = new Vector3(13f, 12f, 3f);
        }
        var shrooms = Object.FindObjectsByType<BounceShroom>(FindObjectsSortMode.None).Where(s => s.gameObject.scene == scene).OrderBy(s => s.name).ToArray();
        for (int i = 0; i < shrooms.Length; i++)
        {
            shrooms[i].transform.SetParent(gameplay, true);
            shrooms[i].transform.position = new Vector3((i - 1) * 3f, -28.85f, 7f + i % 2);
        }
        var player = Object.FindFirstObjectByType<SmoothFirstPersonController>();
        player.transform.root.position = new Vector3(0f, -27.8f, -24f);
        player.transform.root.rotation = Quaternion.identity;
        var anchors = new[] { new Vector3(29f, 0.3f, 102f), new Vector3(44f, 0.3f, 134f), new Vector3(23f, 0.3f, 165f), new Vector3(0f, 2.3f, 186f) };
        for (int i = 0; i < anchors.Length; i++)
        {
            var anchor = new GameObject(i < 3 ? "Encounter " + (i + 1) + " — sculpt terrain around this area" : "Tower Clearing — future combined encounter").transform;
            anchor.SetParent(gameplay, false);
            anchor.position = anchors[i];
        }
        if (reference != null)
        {
            var sourceTower = reference.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f => f.name == "Distant Tower Blockout");
            if (sourceTower != null)
            {
                var tower = Object.Instantiate(sourceTower.gameObject, gameplay);
                tower.name = "Tower Landmark (movable placeholder)";
                tower.transform.position = new Vector3(0f, 2.1f, 201f);
            }
            reference.name = "Blockout Reference — moved 400m east";
            reference.transform.position += Vector3.right * 400f;
        }
    }

    private static void Float(SerializedObject settings, string name, float value) => settings.FindProperty(name).floatValue = value;
}
