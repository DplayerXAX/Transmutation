using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit physics checks in a disposable local scene; authored scenes are untouched.</summary>
public static class SquirrelPhysicsValidation
{
    [MenuItem("Tools/Creatures/Validate Squirrel Physics (Play Mode)")]
    public static void Validate()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("Enter Play Mode before running Squirrel Physics validation.");
            return;
        }
        Scene scene = SceneManager.CreateScene("Squirrel Physics Validation " + Guid.NewGuid(),
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        try
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(floor, scene);
            floor.layer = 7;
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(20f, 1f, 20f);
            var actor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(actor, scene);
            actor.transform.position = new Vector3(0f, 4f, 0f);
            var squirrel = actor.AddComponent<Squirrel>();
            var body = actor.GetComponent<Rigidbody>();
            var motor = actor.GetComponent<SquirrelMotor>();
            var homeObject = new GameObject("Test Nest");
            SceneManager.MoveGameObjectToScene(homeObject, scene);
            var home = homeObject.AddComponent<SquirrelNest>();
            var foodObject = new GameObject("Test FoodManager");
            SceneManager.MoveGameObjectToScene(foodObject, scene);
            home.AssignOwner(squirrel);
            squirrel.Initialize(foodObject.AddComponent<FoodManager>(), home, 1f, 3);
            Check(body != null && !body.isKinematic && body.useGravity,
                "squirrel uses a dynamic body with gravity");
            Check(body.constraints == RigidbodyConstraints.FreezeRotation,
                "body remains upright without freezing vertical movement");
            Vector3 spawn = new Vector3(3f, 0f, 2f);
            SquirrelTree.PlaceOnGround(actor.transform, spawn);
            Check(Vector3.Distance(body.position, actor.transform.position) < 0.001f &&
                Mathf.Abs(body.position.x - spawn.x) < 0.001f && Mathf.Abs(body.position.z - spawn.z) < 0.001f,
                "prefab placement teleports the interpolated physics body with its transform");
            body.position = actor.transform.position = new Vector3(0f, 4f, 0f);
            Physics.SyncTransforms();
            PhysicsScene physics = scene.GetPhysicsScene();
            for (int i = 0; i < 150; i++) physics.Simulate(0.02f);
            Check(body.position.y > 0.45f && body.position.y < 0.6f,
                "squirrel falls and lands on the floor without passing through it");

            MethodInfo tick = typeof(SquirrelMotor).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            squirrel.SetSimulationEnabled(false);
            body.linearVelocity = new Vector3(1f, -1f, 0f);
            motor.SetIntent(Vector3.forward * 5f);
            tick.Invoke(motor, null);
            Check(body.linearVelocity == new Vector3(1f, -1f, 0f),
                "paused or externally controlled creatures retain movement ownership");
            squirrel.SetSimulationEnabled(true);
            motor.SetIntent(Vector3.forward * 2f);
            tick.Invoke(motor, null);
            Check(body.linearVelocity.y == -1f && body.linearVelocity.z > 0f,
                "locomotion changes horizontal velocity while preserving falling velocity");
            Vector3 beforeWalk = body.position;
            for (int i = 0; i < 50; i++)
            {
                motor.SetIntent(Vector3.forward * 2f);
                tick.Invoke(motor, null);
                physics.Simulate(0.02f);
            }
            Check(body.position.z - beforeWalk.z > 0.5f,
                "squirrel travels on the floor under project gravity instead of sticking from contact friction");
            // A solid destination must remain approachable, including a nest larger
            // than the actor's centre-based reach radius.
            homeObject.transform.position = new Vector3(0f, 0.5f, -3f);
            homeObject.AddComponent<SphereCollider>().radius = 1.5f;
            var foodModel = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            SceneManager.MoveGameObjectToScene(foodModel, scene);
            foodModel.transform.position = new Vector3(0f, 1f, 4f);
            var food = foodModel.AddComponent<SquirrelFood>();
            foodObject.GetComponent<FoodManager>().Register(food);
            squirrel.Initialize(foodObject.GetComponent<FoodManager>(), home, 1f, 3);
            body.position = actor.transform.position = new Vector3(0f, 0.5f, 0f);
            body.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            MethodInfo forage = typeof(Squirrel).GetMethod("TickCreature", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < 1000 && home.StoredFoodCount == 0; i++)
            {
                forage.Invoke(squirrel, new object[] { 0.02f });
                tick.Invoke(motor, null);
                physics.Simulate(0.02f);
            }
            Check(home.StoredFoodCount == 1 && squirrel.CarriedCount == 0,
                "squirrel approaches solid food, returns to a large solid nest and deposits its load");
            squirrel.Die();
            Check(!squirrel.CanMoveAutonomously && !squirrel.CanFight,
                "dead creatures stop foraging and fighting");
            Debug.Log("Squirrel Physics validation passed: spawn pose, gravity, landing, ownership, ground movement, solid food pickup, nest deposit and death.");
        }
        finally
        {
            foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("Squirrel physics validation failed: " + description);
    }
}
