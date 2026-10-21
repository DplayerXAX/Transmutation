using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Food refresh and complete nest migration in an isolated physics scene.</summary>
public static class SquirrelEcologyValidation
{
    [MenuItem("Tools/Creatures/Validate Squirrel Ecology (Play Mode)")]
    public static void Validate()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Enter Play Mode before validating squirrel ecology."); return; }
        Scene scene = SceneManager.CreateScene("Squirrel Ecology Validation " + Guid.NewGuid(),
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var randomState = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(7331);
            var floor = Primitive(scene, PrimitiveType.Cube, new Vector3(0f, -0.5f, 0f));
            floor.layer = 7;
            floor.transform.localScale = new Vector3(160f, 1f, 160f);
            var actor = Primitive(scene, PrimitiveType.Cube, new Vector3(0f, 0.5f, 0f));
            var squirrel = actor.AddComponent<Squirrel>();
            var body = actor.GetComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.None; // manual physics steps have no rendered interpolation tick
            var nestObject = Primitive(scene, PrimitiveType.Sphere, new Vector3(0f, 0.5f, -1f));
            var nest = nestObject.AddComponent<SquirrelNest>();
            nest.AssignOwner(squirrel);
            var managerObject = new GameObject("Test Food Manager");
            SceneManager.MoveGameObjectToScene(managerObject, scene);
            var foods = managerObject.AddComponent<FoodManager>();
            Set(foods, "foodPrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/Food.prefab"));
            Set(foods, "initialFoodCount", 0);
            Set(foods, "respawnInterval", 1f);
            Set(foods, "foodPerCycle", 2);
            Set(foods, "maxUnstoredFood", 3);
            var treeObject = new GameObject("Test Tree");
            SceneManager.MoveGameObjectToScene(treeObject, scene);
            treeObject.transform.position = new Vector3(25f, 0f, 0f);
            var tree = treeObject.AddComponent<SquirrelTree>();
            Physics.SyncTransforms();
            foods.GenerateAroundTrees(new[] { tree });
            Invoke(foods, "TickFoodGrowth", 0f);
            Check(foods.UnstoredFoodCount == 0, "paused growth creates no food");
            Invoke(foods, "TickFoodGrowth", 1f);
            Check(foods.UnstoredFoodCount == 2, "one interval grows the configured batch");
            Invoke(foods, "TickFoodGrowth", 1f);
            Check(foods.UnstoredFoodCount == 3, "growth respects the unstored-food cap");
            Invoke(foods, "TickFoodGrowth", 10f);
            Check(foods.Foods.Count == 3, "a long frame cannot exceed the cap");

            squirrel.Initialize(foods, nest, 1f, 3, 5f);
            var stored = foods.Foods[0];
            Check(stored.Reserve(squirrel) && nest.Deposit(squirrel, stored), "seed a stored item");
            Invoke(foods, "TickFoodGrowth", 1f);
            Check(foods.UnstoredFoodCount == 3 && foods.Foods.Count == 4 && nest.StoredFoodCount == 1,
                "refresh creates a distinct food instance and leaves the stored item in the nest");
            Set(squirrel, "foodShortageDelay", 1f);
            Set(squirrel, "migrationSearchRadius", 45f);
            var originalNest = squirrel.Nest;
            Vector3 originalTerritory = squirrel.TerritoryCenter;
            PhysicsScene physics = scene.GetPhysicsScene();
            Step(squirrel, physics, 20);
            Check(squirrel.CurrentActivity == Squirrel.Activity.Searching, "brief shortage does not trigger migration");
            squirrel.SetSimulationEnabled(false);
            Step(squirrel, physics, 100);
            Check(squirrel.CurrentActivity == Squirrel.Activity.Searching && !nest.IsMoving,
                "paused simulation does not count toward migration");
            squirrel.SetSimulationEnabled(true);
            bool carriedNest = false;
            for (int i = 0; i < 2500; i++)
            {
                Step(squirrel, physics, 1);
                if (squirrel.IsMigrating)
                {
                    carriedNest = true;
                    Check(nest.IsMoving && nest.transform.parent == squirrel.transform &&
                        !nest.GetComponent<Collider>().enabled && !squirrel.CanFight,
                        "migration physically carries the same nest without collisions or fighting");
                }
                if (carriedNest && !nest.IsMoving) break;
            }
            Check(carriedNest && !squirrel.IsMigrating && !nest.IsMoving,
                $"migration walks beyond the original territory and finishes (state={squirrel.CurrentActivity}, position={body.position}, destination={Get(squirrel, "migrationGround")})");
            Check(squirrel.Nest == originalNest && nest.Owner == squirrel && nest.StoredFoodCount == 1,
                "arrival preserves nest identity, owner and stored food");
            Check(Vector3.Distance(originalTerritory, squirrel.TerritoryCenter) > 10f &&
                Vector3.Distance(nest.transform.position, squirrel.TerritoryCenter) < 0.01f &&
                nest.GetComponent<Collider>().enabled, "arrival moves the territory and restores nest collision");
            int before = nest.StoredFoodCount;
            for (int i = 0; i < 1500 && nest.StoredFoodCount == before; i++) Step(squirrel, physics, 1);
            Check(nest.StoredFoodCount > before, "foraging and depositing resume at the new home");

            Check(nest.BeginMove(squirrel), "prepare interruption case");
            Set(squirrel, "activity", Squirrel.Activity.Migrating);
            squirrel.Die();
            Check(!nest.IsMoving && nest.transform.parent != squirrel.transform && nest.Owner == squirrel &&
                nest.GetComponent<Collider>().enabled && nest.StoredFoodCount > 0,
                "death detaches and restores the nest without deleting stores");
            Debug.Log("Squirrel Ecology validation passed: periodic growth, cap, real inventory, shortage delay, pause, complete migration, stores, resumed foraging and death cleanup.");
        }
        finally
        {
            foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);
            UnityEngine.Random.state = randomState;
        }
    }

    private static void Step(Squirrel squirrel, PhysicsScene physics, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            Set(squirrel, "nextSearch", 0f);
            if (squirrel.SimulationEnabled) Invoke(squirrel, "TickCreature", 0.02f);
            Invoke(squirrel.GetComponent<SquirrelMotor>(), "FixedUpdate");
            physics.Simulate(0.02f);
        }
    }
    private static GameObject Primitive(Scene scene, PrimitiveType type, Vector3 point)
    {
        var go = GameObject.CreatePrimitive(type);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = point;
        return go;
    }
    private static object Get(object target, string field) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Squirrel ecology validation failed: " + message);
    }
}
