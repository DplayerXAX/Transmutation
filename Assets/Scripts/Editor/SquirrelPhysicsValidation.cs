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
            squirrel.Die();
            Check(!squirrel.CanMoveAutonomously && !squirrel.CanFight,
                "dead creatures stop foraging and fighting");
            Debug.Log("Squirrel Physics validation passed: gravity, landing, upright constraints, ownership, vertical velocity and death.");
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
