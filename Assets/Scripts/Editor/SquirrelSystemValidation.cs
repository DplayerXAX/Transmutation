using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class SquirrelSystemValidation
{
    private const string PrefabPath = "Assets/Prefabs/Systems/SquirrelSystemManager.prefab";
    [MenuItem("Tools/Creatures/Validate Squirrel System Prefab (Play Mode)")]
    public static void Validate()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Enter Play Mode before system validation."); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Check(prefab != null && prefab.GetComponent<SquirrelSystemManager>() != null,
            "prefab imports with the combined manager component");
        Check(prefab.GetComponents<MonoBehaviour>().Length == 1,
            "prefab contains a single manager rather than multiple legacy managers");
        var source = new SerializedObject(prefab.GetComponent<SquirrelSystemManager>());
        foreach (string field in new[] { "squirrelPrefab", "foodPrefab", "nestPrefab" })
            Check(source.FindProperty(field).objectReferenceValue != null, field + " is assigned");
        Scene scene = SceneManager.CreateScene("Squirrel System Prefab Validation " + Guid.NewGuid(),
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var random = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(3412);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.name = "Ground"; floor.layer = 7;
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(120f, 1f, 120f);
            for (int i = 0; i < 3; i++)
            {
                var tree = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                SceneManager.MoveGameObjectToScene(tree, scene);
                tree.name = "Environment Trunk " + (i + 1);
                tree.transform.position = new Vector3(-15f + i * 15f, 1f, 0f);
            }
            Physics.SyncTransforms();
            var root = Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(root, scene);
            var system = root.GetComponent<SquirrelSystemManager>();
            system.InitializeSystem();
            Check(system.IsInitialized && system.Trees.Count == 3, "discovers local trees without an Ariia bootstrap");
            Check(system.Squirrels.Count == 3 && system.Nests.Count == 3 && system.Foods.Count == 15,
                "one prefab creates the configured population, homes and initial food");
            foreach (var squirrel in system.Squirrels)
                Check(squirrel.gameObject.scene == scene && squirrel.Nest != null && squirrel.Nest.Owner == squirrel,
                    "spawned members retain local scene ownership and nest assignment");
            Vector3 scale = system.Squirrels[0].transform.localScale;
            system.InitializeSystem();
            Check(system.Squirrels.Count == 3 && system.Nests.Count == 3 && system.Foods.Count == 15 &&
                system.Squirrels[0].transform.localScale == scale, "repeated initialization does not duplicate or resize members");
            typeof(SquirrelSystemManager).GetMethod("TickFoodGrowth", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(system, new object[] { 15f });
            Check(system.UnstoredFoodCount == 18, "periodic food growth is owned by the same component");
            Debug.Log("Squirrel System prefab validation passed: one component, prefab bindings, scene discovery, population, homes, initial food, idempotence and periodic growth.");
        }
        finally
        {
            foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);
            UnityEngine.Random.state = random;
        }
        SquirrelEcologyValidation.Validate();
        SquirrelNestValidation.Validate();
        SquirrelPhysicsValidation.Validate();
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Squirrel System validation failed: " + message); }
}
