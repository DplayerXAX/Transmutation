using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Basin test level: copies Scene_Daniel to Scene_Basin, strips the landmarks and leftovers, turns the
/// ProceduralWorld into a small round basin ringed by mountains, and adds the BasinLevel (towers, updraft,
/// spider silk, creatures) and a bird flock. Scene_Daniel itself is not changed.
/// </summary>
public static class BasinLevelSetup
{
    private const string SourceScene = "Assets/Scenes/Scene_Daniel.unity";
    private const string TargetScene = "Assets/Scenes/Scene_Basin.unity";

    // Root objects kept from Scene_Daniel (anything else is removed).
    private static readonly string[] KeepNames =
    {
        "Procedural World", "PlayerObjects", "Canvas", "EventSystem", "Directional Light", "Meditation", "AudioManager",
        "WwiseGlobal", "InteractionPromptHUD", "Basin Templates", "Basin Level", "Basin Birds",
    };

    [MenuItem("Tools/Capstone/Basin Level/Create Basin Test Scene (Copy Of Scene_Daniel)")]
    private static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (File.Exists(TargetScene))
        {
            if (!EditorUtility.DisplayDialog("Basin Level", "Scene_Basin already exists. Replace it with a fresh copy of Scene_Daniel?", "Replace", "Cancel"))
                return;
            AssetDatabase.DeleteAsset(TargetScene);
        }
        if (!AssetDatabase.CopyAsset(SourceScene, TargetScene))
        {
            EditorUtility.DisplayDialog("Basin Level", "Could not copy " + SourceScene + ".", "OK");
            return;
        }
        Scene scene = EditorSceneManager.OpenScene(TargetScene, OpenSceneMode.Single);
        if (!Configure(scene)) return;
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Scene_Basin created and saved. Press Play to build the basin, towers and creatures.");
    }

    [MenuItem("Tools/Capstone/Basin Level/Set Up Open Scene As Basin Level")]
    private static void SetUpOpenScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!EditorUtility.DisplayDialog("Basin Level", "Remove everything in " + scene.name + " except the world, player, UI, meditation and audio, and set it up as the basin level?", "Set Up", "Cancel"))
            return;
        if (Configure(scene)) EditorSceneManager.MarkSceneDirty(scene);
    }

    private static bool Configure(Scene scene)
    {
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null)
        {
            EditorUtility.DisplayDialog("Basin Level", "No ProceduralWorld in the scene.", "OK");
            return false;
        }

        var goo = AssetDatabase.LoadAssetAtPath<GooCreature>("Assets/Prefabs/Creatures/GooCreature.prefab");
        var flower = AssetDatabase.LoadAssetAtPath<BubbleFlower>("Assets/Prefabs/Creatures/BubbleFlower.prefab");
        var spider = AssetDatabase.LoadAssetAtPath<SkySpider>("Assets/Prefabs/Creatures/SkySpider.prefab");
        var fireSource = AssetDatabase.LoadAssetAtPath<FireSource>("Assets/Prefabs/Creatures/FireSource.prefab");
        var fireFlower = AssetDatabase.LoadAssetAtPath<FireFlower>("Assets/Prefabs/Creatures/Fire Flower.prefab");
        var compost = AssetDatabase.LoadAssetAtPath<CompostCreature>("Assets/Prefabs/Creatures/CompostCreature.prefab");
        var bird = AssetDatabase.LoadAssetAtPath<BirdCreature>("Assets/Prefabs/Creatures/BirdCreature.prefab");
        var tower = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Landmarks/MAT_Landmark_SpireStone.mat");
        var bone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Landmarks/MAT_Landmark_BoneMarble.mat");
        var silk = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Realm_Silk.mat");
        var ink = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Landmarks/MAT_Landmark_Ink.mat");
        var seed = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Landmarks/MAT_Landmark_Seed.mat");
        if (goo == null || flower == null || spider == null || fireSource == null || fireFlower == null || bird == null || compost == null)
        {
            EditorUtility.DisplayDialog("Basin Level", "Missing creature prefabs in Assets/Prefabs/Creatures (goo, bubble flower, spider, fire source, fire flower, bird, compost).", "OK");
            return false;
        }
        if (tower == null || bone == null || silk == null || ink == null || seed == null)
        {
            EditorUtility.DisplayDialog("Basin Level", "Missing landmark or realm materials. Run the Landmarks and Realm menus once in Scene_Daniel first.", "OK");
            return false;
        }

        // Templates: the scene's tentacle creature and one fuzz, kept under an inactive holder.
        GameObject holder = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Basin Templates");
        if (holder == null) holder = new GameObject("Basin Templates");
        holder.SetActive(false);
        TentacleCreature tentacle = holder.GetComponentInChildren<TentacleCreature>(true);
        if (tentacle == null)
        {
            tentacle = Object.FindFirstObjectByType<TentacleCreature>();
            if (tentacle != null) tentacle.transform.SetParent(holder.transform, true);
        }
        FuzzCreature fuzz = holder.GetComponentInChildren<FuzzCreature>(true);
        if (fuzz == null)
        {
            fuzz = Object.FindFirstObjectByType<FuzzCreature>();
            if (fuzz != null) fuzz.transform.SetParent(holder.transform, true);
        }
        if (tentacle == null || fuzz == null)
            Debug.LogWarning("Basin Level: no TentacleCreature or FuzzCreature in the scene to copy; that creature is left out.");

        // Goo counts as Matter here, so the birds (Plant eaters, plus anything untagged) leave it alone.
        GooCreature gooTemplate = holder.GetComponentInChildren<GooCreature>(true);
        if (gooTemplate == null) gooTemplate = ((GameObject)PrefabUtility.InstantiatePrefab(goo.gameObject, holder.transform)).GetComponent<GooCreature>();
        var serializedGoo = new SerializedObject(gooTemplate);
        serializedGoo.FindProperty("foodTags").intValue = (int)Creature.FoodTag.Matter;
        serializedGoo.ApplyModifiedProperties();
        // Bubble flowers that blow a bubble only every 10-16 seconds.
        BubbleFlower flowerTemplate = holder.GetComponentInChildren<BubbleFlower>(true);
        if (flowerTemplate == null) flowerTemplate = ((GameObject)PrefabUtility.InstantiatePrefab(flower.gameObject, holder.transform)).GetComponent<BubbleFlower>();
        var serializedFlower = new SerializedObject(flowerTemplate);
        serializedFlower.FindProperty("groundFiringInterval").vector2Value = new Vector2(10f, 16f);
        serializedFlower.ApplyModifiedProperties();

        // Remove the landmarks, realm sites and leftovers.
        int removed = 0;
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            if (Keep(rootObject)) continue;
            Object.DestroyImmediate(rootObject);
            removed++;
        }

        // The world: a small round basin ringed by mountains, no caves or sinkholes.
        var serializedWorld = new SerializedObject(world);
        serializedWorld.FindProperty("worldSize").vector2Value = new Vector2(176f, 176f);
        serializedWorld.FindProperty("surfaceAmplitude").floatValue = 5f;
        serializedWorld.FindProperty("surfaceFrequency").floatValue = 0.02f;
        serializedWorld.FindProperty("ridges").floatValue = 0.4f;
        serializedWorld.FindProperty("overhangAmplitude").floatValue = 2.5f;
        serializedWorld.FindProperty("spawnFlatRadius").floatValue = 14f;
        serializedWorld.FindProperty("spawnBlend").floatValue = 20f;
        serializedWorld.FindProperty("rimHeight").floatValue = 42f;
        serializedWorld.FindProperty("basin").boolValue = true;
        serializedWorld.FindProperty("basinFloor").floatValue = 0.62f;
        serializedWorld.FindProperty("basinSlope").floatValue = 0.32f;
        serializedWorld.FindProperty("caveWidth").floatValue = 0f;
        serializedWorld.FindProperty("sinkholeCount").intValue = 0;
        serializedWorld.FindProperty("firstSinkholeDistance").floatValue = 0f;
        serializedWorld.FindProperty("blockingRadius").floatValue = 50f;
        serializedWorld.ApplyModifiedProperties();

        // The player starts in the middle of the basin.
        SmoothFirstPersonController player = Object.FindFirstObjectByType<SmoothFirstPersonController>();
        if (player != null)
        {
            Vector3 offset = world.transform.position + Vector3.up * 3f - player.transform.position;
            player.transform.root.position += offset;
        }

        // The level itself.
        GameObject levelObject = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Basin Level") ?? new GameObject("Basin Level");
        // A spider whose webs are large enough to span the gap between the towers.
        SkySpider spiderTemplate = holder.GetComponentInChildren<SkySpider>(true);
        if (spiderTemplate == null) spiderTemplate = ((GameObject)PrefabUtility.InstantiatePrefab(spider.gameObject, holder.transform)).GetComponent<SkySpider>();
        var serializedSpider = new SerializedObject(spiderTemplate);
        serializedSpider.FindProperty("webScale").floatValue = 1.8f;
        serializedSpider.ApplyModifiedProperties();

        // Replaced each time so the layout follows the current defaults in BasinLevel.
        BasinLevel level = levelObject.GetComponent<BasinLevel>();
        if (level != null) Object.DestroyImmediate(level);
        level = levelObject.AddComponent<BasinLevel>();
        level.Setup(world, gooTemplate, flowerTemplate, spiderTemplate, fireSource, fireFlower, compost, tentacle, fuzz);
        level.SetMaterials(tower, bone, silk, ink, seed);
        // Keeps fire flowers from multiplying without end.
        if (levelObject.GetComponent<RealmPopulation>() == null) levelObject.AddComponent<RealmPopulation>();
        EditorUtility.SetDirty(level);

        // Birds circle low over the basin, below the towers' tops.
        GameObject birds = scene.GetRootGameObjects().FirstOrDefault(r => r.name == "Basin Birds");
        if (birds == null)
        {
            birds = new GameObject("Basin Birds");
            var manager = birds.AddComponent<BirdFlockManager>();
            var serializedManager = new SerializedObject(manager);
            serializedManager.FindProperty("skyBounds").boundsValue = new Bounds(world.transform.position + new Vector3(0f, 13f, 6f), new Vector3(100f, 12f, 100f));
            serializedManager.ApplyModifiedProperties();
            var area = new GameObject("Bird Spawn Area");
            area.transform.SetParent(birds.transform, false);
            area.transform.position = world.transform.position + new Vector3(0f, 13f, 10f);
            area.AddComponent<BirdSpawnArea>().Configure(bird, 16, 5f, 2027);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = levelObject;
        Debug.Log($"Basin Level set up in {scene.name}: removed {removed} root objects, world set to a 176 m basin.");
        return true;
    }

    private static bool Keep(GameObject rootObject)
    {
        if (KeepNames.Contains(rootObject.name)) return true;
        if (rootObject.GetComponentInChildren<ProceduralWorld>(true) != null) return true;
        if (rootObject.GetComponentInChildren<SmoothFirstPersonController>(true) != null) return true;
        if (rootObject.GetComponentInChildren<Canvas>(true) != null || rootObject.GetComponentInChildren<EventSystem>(true) != null) return true;
        Light light = rootObject.GetComponent<Light>();
        return light != null && light.type == LightType.Directional;
    }
}
