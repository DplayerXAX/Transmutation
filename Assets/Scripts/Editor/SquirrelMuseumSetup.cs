using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Builds a small, self-contained variant while inheriting the normal squirrel behavior assets.</summary>
public static class SquirrelMuseumSetup
{
    private const string BasePath = "Assets/Prefabs/Systems/SquirrelSystemManager.prefab";
    private const string Path = "Assets/Prefabs/Systems/SquirrelMuseumDisplay.prefab";

    [MenuItem("Tools/Creatures/Create Squirrel Museum Display Prefab")]
    public static void Create()
    {
        if (Application.isPlaying) { Debug.LogWarning("Create the museum prefab outside Play Mode."); return; }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Path) != null)
        { Debug.Log("Squirrel museum prefab already exists; keeping its custom settings."); return; }
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(BasePath);
        if (source == null) throw new InvalidOperationException("Missing squirrel system prefab.");
        var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            root.name = "SquirrelMuseumDisplay";
            var floor = Primitive(root.transform, "Display Ground", PrimitiveType.Cube,
                new Vector3(0f, -0.5f, 0f), new Vector3(32f, 1f, 22f), Material("Stone", new Color(0.55f, 0.53f, 0.46f)));
            floor.layer = 7;
            var trees = new SquirrelTree[2];
            for (int i = 0; i < trees.Length; i++)
            {
                var trunk = Primitive(root.transform, "Exhibit Tree " + (i + 1), PrimitiveType.Cylinder,
                    new Vector3(i == 0 ? -8f : 8f, 2f, 0f), new Vector3(1f, 2f, 1f),
                    Material("Bark", new Color(0.32f, 0.18f, 0.09f)));
                trees[i] = trunk.AddComponent<SquirrelTree>();
                var canopy = Primitive(trunk.transform, "Canopy", PrimitiveType.Sphere,
                    new Vector3(0f, 1.1f, 0f), new Vector3(3f, 1.2f, 3f), Material("Leaves", new Color(0.22f, 0.45f, 0.2f)));
                Object.DestroyImmediate(canopy.GetComponent<Collider>());
            }
            var settings = new SerializedObject(root.GetComponent<SquirrelSystemManager>());
            settings.FindProperty("autoDiscoverScene").boolValue = false;
            settings.FindProperty("population").intValue = 2;
            settings.FindProperty("sizeRange").vector2Value = Vector2.one;
            settings.FindProperty("territoryRadius").floatValue = 8f;
            settings.FindProperty("squirrelSpawnRadius").floatValue = 2f;
            settings.FindProperty("initialFoodCount").intValue = 6;
            settings.FindProperty("foodSpawnRadius").floatValue = 4f;
            settings.FindProperty("respawnInterval").floatValue = 12f;
            settings.FindProperty("foodPerCycle").intValue = 1;
            settings.FindProperty("maxUnstoredFood").intValue = 8;
            var anchors = settings.FindProperty("trees"); anchors.arraySize = trees.Length;
            for (int i = 0; i < trees.Length; i++) anchors.GetArrayElementAtIndex(i).objectReferenceValue = trees[i];
            settings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(root.GetComponent<SquirrelSystemManager>());
            PrefabUtility.SaveAsPrefabAsset(root, Path);
            AssetDatabase.SaveAssets();
            Debug.Log("Created SquirrelMuseumDisplay prefab variant: two fixed-size squirrels, two local trees, ground and limited periodic food.");
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static GameObject Primitive(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name;
        go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }
    private static Material Material(string name, Color color)
    {
        const string folder = "Assets/Prefabs/Systems/MuseumMaterials";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Prefabs/Systems", "MuseumMaterials");
        string path = folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        material.color = color; AssetDatabase.CreateAsset(material, path);
        return material;
    }

    [MenuItem("Tools/Creatures/Validate Squirrel Museum Display (Play Mode)")]
    public static void Validate()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Enter Play Mode first."); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path);
        Check(prefab != null && PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant,
            "museum asset is a variant of the existing system");
        Check(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(prefab)) == BasePath,
            "variant inherits from the normal system prefab");
        var scene = SceneManager.CreateScene("Squirrel Museum Validation " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var random = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(4531);
            var unrelated = new GameObject("Tree unrelated to exhibit"); SceneManager.MoveGameObjectToScene(unrelated, scene);
            unrelated.AddComponent<SquirrelTree>();
            var root = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = new Vector3(70f, 5f, 40f);
            Physics.SyncTransforms();
            var manager = root.GetComponent<SquirrelSystemManager>(); manager.InitializeSystem();
            Check(manager.IsInitialized && manager.Squirrels.Count == 2 && manager.Nests.Count == 2, "drop-in initialization creates exactly two squirrels and homes");
            Check(manager.Trees.Count == 2 && manager.Trees[0].transform.IsChildOf(root.transform) && manager.Trees[1].transform.IsChildOf(root.transform),
                "only exhibit-local trees are used, even with other scene trees");
            foreach (var squirrel in manager.Squirrels)
            {
                Check(squirrel.transform.localScale == Vector3.one && squirrel.TerritoryRadius == 8f, "squirrels have consistent size and compact territories");
                Check(squirrel.Nest.Owner == squirrel && squirrel.transform.position.y > 5f && squirrel.transform.position.x > 50f,
                    "placement follows the relocated exhibit and preserves nest ownership");
            }
            Check(manager.UnstoredFoodCount == 6, "small initial food supply spawns on included ground");
            var tick = typeof(SquirrelSystemManager).GetMethod("TickFoodGrowth", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            for (int i = 0; i < 10; i++) tick.Invoke(manager, new object[] { 12f });
            Check(manager.UnstoredFoodCount == 8 && manager.Squirrels.Count == 2, "periodic food stays capped without adding squirrels");
            manager.InitializeSystem();
            Check(manager.Squirrels.Count == 2 && manager.Nests.Count == 2, "reinitialization cannot duplicate the exhibit population");
            Debug.Log("Squirrel Museum validation passed: inherited variant, movable self-contained habitat, exactly two equal-size squirrels, local-only trees, owned nests, periodic food and cap.");
        }
        finally
        {
            foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene); UnityEngine.Random.state = random;
        }
    }
    private static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException("Squirrel Museum validation failed: " + message); }
}
