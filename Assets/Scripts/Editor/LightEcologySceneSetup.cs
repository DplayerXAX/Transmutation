using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Builds one standalone scene for the complete luminous ecosystem.</summary>
public static class LightEcologySceneSetup
{
    public const string ScenePath = "Assets/Scenes/Scene_LightEcology.unity";
    public const string HabitatName = "Luminous Habitat";

    public static void IntegrateInBatch()
    {
        if (!Application.dataPath.Replace('\\', '/').Contains("/.utmp/GlowBeeChecks/UnityProject/Assets"))
            throw new InvalidOperationException("Batch authoring is restricted to the isolated project copy.");
        EditorSceneManager.OpenScene("Assets/Scenes/Scene_Yoyo.unity", OpenSceneMode.Single);
        CreateScene();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }

    [MenuItem("Tools/Creatures/Light Ecology/Create Standalone Scene")]
    public static void CreateScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            if (!SceneManager.GetSceneByPath(ScenePath).isLoaded) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(SceneManager.GetSceneByPath(ScenePath));
            return;
        }
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.12f, 0.14f, 0.18f);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Ground"; floor.layer = 7;
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(32f, 1f, 32f);
        floor.GetComponent<Renderer>().sharedMaterial = LightEcologySetup.Material("HabitatStone", new Color(0.12f, 0.18f, 0.21f));
        var sun = new GameObject("Daylight").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 0.5f;
        sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerObjects.prefab");
        var playerRoot = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
        playerRoot.transform.position = new Vector3(0f, 1.5f, -6f);
        new GameObject("Interaction Prompts").AddComponent<InteractionPromptHUD>();
        var colonyRoot = new GameObject("Glow Bee Colony");
        var newColony = colonyRoot.AddComponent<GlowBeeColony>();
        var anchorRoot = new GameObject("Nest Anchor"); anchorRoot.transform.SetParent(colonyRoot.transform, false);
        using (var settings = new SerializedObject(newColony))
        {
            settings.FindProperty("nestCount").intValue = 1;
            settings.FindProperty("beesPerNest").intValue = 4;
            settings.FindProperty("flowersPerNest").intValue = 5;
            settings.FindProperty("spawnSpider").boolValue = false;
            settings.FindProperty("sceneryLayers").intValue = LuminosityQuery.DefaultObstructionMask;
            var anchors = settings.FindProperty("nestAnchors"); anchors.arraySize = 1;
            anchors.GetArrayElementAtIndex(0).objectReferenceValue = anchorRoot.transform;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
        Populate(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    private static void Populate(Scene scene)
    {
        FlashBloomSetup.CreatePrefab(); LightEcologySetup.CreateMoth(); LightEcologySetup.CreatePod();
        SmoothFirstPersonController player = null;
        GlowBeeColony colony = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (player == null) player = root.GetComponentInChildren<SmoothFirstPersonController>();
            if (colony == null) colony = root.GetComponentInChildren<GlowBeeColony>();
        }
        if (player == null || colony == null) throw new InvalidOperationException("The scene needs a player and bee colony.");
        if (player.GetComponent<PlayerCreatureStats>() == null) Undo.AddComponent<PlayerCreatureStats>(player.gameObject);
        if (player.GetComponent<PlayerLuminosityVisuals>() == null) Undo.AddComponent<PlayerLuminosityVisuals>(player.gameObject);
        Transform orientation = player.transform.Find("Orientation");
        Vector3 forward = Vector3.ProjectOnPlane(orientation != null ? orientation.forward : player.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 desired = player.transform.position + forward * 5f + right * 3f;
        Physics.SyncTransforms();
        if (!scene.GetPhysicsScene().Raycast(desired + Vector3.up * 30f, Vector3.down, out var ground,
            80f, 1 << 7, QueryTriggerInteraction.Ignore)) throw new InvalidOperationException("No ground for the luminous habitat.");
        var habitat = new GameObject(HabitatName);
        Undo.RegisterCreatedObjectUndo(habitat, "Integrate luminous habitat");
        habitat.transform.position = ground.point;
        habitat.transform.rotation = Quaternion.LookRotation(forward);
        var stone = LightEcologySetup.Material("HabitatStone", new Color(0.12f, 0.18f, 0.21f));
        Block("Canopy", new Vector3(0f, 4.2f, 0f), new Vector3(9f, 0.3f, 9f));
        Block("Back wall", new Vector3(0f, 2.1f, 4.3f), new Vector3(9f, 4.2f, 0.3f));
        Block("Left wall", new Vector3(-4.3f, 2.1f, 0f), new Vector3(0.3f, 4.2f, 9f));
        Block("Right wall", new Vector3(4.3f, 2.1f, 0f), new Vector3(0.3f, 4.2f, 9f));
        void Block(string name, Vector3 position, Vector3 scale)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube); block.name = name; block.layer = 7;
            block.transform.SetParent(habitat.transform, false); block.transform.localPosition = position; block.transform.localScale = scale;
            block.GetComponent<Renderer>().sharedMaterial = stone;
        }
        void Place(string path, Vector3 local)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.SetParent(habitat.transform, false); instance.transform.localPosition = local;
        }
        for (int i = 0; i < 2; i++)
        {
            Place(FlashBloomSetup.PrefabPath, new Vector3(-1f + i * 2f, 0.4f, -2.8f));
            Place(LightEcologySetup.PodPath, new Vector3(-2f + i * 4f, 0.35f, 0f));
        }
        for (int i = 0; i < 6; i++) Place(LightEcologySetup.MothPath, new Vector3((i % 3 - 1) * 1.1f, 0.85f + i * 0.04f, -1.1f + i / 3 * 0.8f));
        Transform anchor = colony.transform.Find("Nest Anchor");
        if (anchor == null) throw new InvalidOperationException("Existing colony is missing its nest anchor.");
        Undo.RecordObject(anchor, "Move existing hive into habitat");
        anchor.position = habitat.transform.TransformPoint(new Vector3(-2.2f, 0f, 2.1f));
        var lampRoot = new GameObject("Habitat lamp"); lampRoot.transform.SetParent(habitat.transform, false);
        lampRoot.transform.localPosition = new Vector3(0f, 3.3f, 0f);
        var lamp = lampRoot.AddComponent<Light>(); lamp.type = LightType.Point; lamp.range = 14f; lamp.intensity = 30f; lamp.enabled = false;
        habitat.AddComponent<LightHabitat>().habitatLight = lamp;
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = habitat;
    }
}
