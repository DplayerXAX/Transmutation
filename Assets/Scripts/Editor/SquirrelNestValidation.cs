using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class SquirrelNestValidation
{
    [MenuItem("Tools/Creatures/Validate Squirrel Nest Inventory And Building (Play Mode)")]
    public static void Validate()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Enter Play Mode before nest validation."); return; }
        var scene = SceneManager.CreateScene("Nest Validation " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var random = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(992);
            var floor = Primitive(scene, PrimitiveType.Cube, new Vector3(0f, -0.5f, 0f));
            floor.layer = 7; floor.transform.localScale = new Vector3(120f, 1f, 120f);
            var actor = Primitive(scene, PrimitiveType.Cube, new Vector3(-8f, 0.5f, 0f));
            var squirrel = actor.AddComponent<Squirrel>();
            var body = actor.GetComponent<Rigidbody>(); body.interpolation = RigidbodyInterpolation.None;
            var nestObject = Primitive(scene, PrimitiveType.Sphere, new Vector3(0f, 0.5f, 0f));
            var nest = nestObject.AddComponent<SquirrelNest>();
            nest.GetComponent<Rigidbody>().isKinematic = true;
            Set(nest, "foodCapacity", 2);
            nest.AssignOwner(squirrel);
            var fmObject = new GameObject("Food Manager"); SceneManager.MoveGameObjectToScene(fmObject, scene);
            var fm = fmObject.AddComponent<SquirrelSystemManager>(); Set(fm, "initialFoodCount", 0);
            var treeObject = new GameObject("Tree"); SceneManager.MoveGameObjectToScene(treeObject, scene);
            treeObject.transform.position = new Vector3(4f, 0f, 3f);
            var tree = treeObject.AddComponent<SquirrelTree>();
            fm.GenerateAroundTrees(new[] { tree });
            squirrel.ConfigureNestBuilding(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/Nest.prefab"));
            squirrel.Initialize(fm, nest, 1f, 1);
            var a = Food(scene, fm, new Vector3(-4f, 1f, 0f));
            var b = Food(scene, fm, new Vector3(-3f, 1f, 0f));
            var excess = Food(scene, fm, new Vector3(-2f, 1f, 0f));
            a.Reserve(squirrel); b.Reserve(squirrel); excess.Reserve(squirrel);
            Check(nest.Deposit(squirrel, a) && nest.Deposit(squirrel, b) && nest.IsFull, "real items fill the configured capacity");
            Check(!nest.Deposit(squirrel, excess) && excess.Owner == squirrel && !excess.IsStored, "full nest rejects without losing the excess item");
            bool tookFirst = nest.TryWithdraw(Vector3.up, out var first);
            bool tookSecond = nest.TryWithdraw(Vector3.up * 2f, out var second);
            Check(tookFirst && tookSecond &&
                first != second && (first == a || first == b) && (second == a || second == b) && nest.StoredFoodCount == 0,
                "withdrawal returns exactly the original stored instances");
            Check(!nest.TryWithdraw(Vector3.zero, out _), "empty nest cannot fabricate food");
            first.Reserve(squirrel); second.Reserve(squirrel);
            Check(nest.Deposit(squirrel, first) && nest.Deposit(squirrel, second), "withdrawn food can be deposited again");
            excess.Release(squirrel);

            // A normal player carrier takes a real item out and returns that same item.
            var player = new GameObject("Test Player"); SceneManager.MoveGameObjectToScene(player, scene); player.SetActive(false);
            player.transform.position = new Vector3(0f, 0.5f, -2f);
            var cameraObject = new GameObject("Test Camera"); cameraObject.transform.SetParent(player.transform, false);
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            var carrier = player.AddComponent<PlayerCreatureCarrier>(); Set(carrier, "carryCamera", camera);
            player.SetActive(true);
            nest.ReceiveInteraction(new CreatureInteraction(player, nest.transform.position, Vector3.forward));
            Check(nest.StoredFoodCount == 1 && carrier.CarriedCreature is SquirrelFood, "player withdraws one real food into the hand");
            var held = (SquirrelFood)carrier.CarriedCreature;
            Physics.SyncTransforms();
            held.ReceiveInteraction(new CreatureInteraction(player, nest.transform.position, Vector3.forward));
            Check(!held.IsAvailable && carrier.CarriedCreature == null && nest.StoredFoodCount == 2 &&
                held.StorageNest == nest, "aimed held-food interaction deposits the original instance without duplication");

            // Move the nest while its owner is away. Memory must stay at the old coordinates.
            Check(carrier.TryCarryFromInteraction(nest), "player can carry a stocked nest");
            nestObject.transform.position = new Vector3(24f, 0.5f, 0f);
            nest.GetComponent<Rigidbody>().position = nestObject.transform.position;
            Check(!squirrel.ActiveNestKnownMissing && squirrel.TerritoryCenter.x == 0f, "unseen nest movement does not update owner memory");
            Set(squirrel, "activity", Squirrel.Activity.Returning);
            Physics.SyncTransforms();
            var physics = scene.GetPhysicsScene();
            Step(squirrel, physics, 25);
            Check(!squirrel.ActiveNestKnownMissing && body.position.x < -2f, "owner first walks toward the remembered site");
            for (int i = 0; i < 600 && !squirrel.ActiveNestKnownMissing; i++) Step(squirrel, physics, 1);
            Check(squirrel.ActiveNestKnownMissing && squirrel.IsNestWork, "owner discovers the loss only after reaching the old site");
            // The one loose food cannot pay a three-item construction cost.
            Step(squirrel, physics, 200);
            Check(squirrel.KnownNestCount == 1, "no new nest appears without enough real materials");
            Food(scene, fm, new Vector3(0f, 1f, 2f));
            Food(scene, fm, new Vector3(2f, 1f, 1f));
            var rivalObject = Primitive(scene, PrimitiveType.Cube, new Vector3(50f, 0.5f, 0f));
            var rival = rivalObject.AddComponent<Squirrel>();
            var blocked = Primitive(scene, PrimitiveType.Sphere, tree.GroundPosition + Vector3.up * 0.5f).AddComponent<SquirrelNest>();
            blocked.AssignOwner(rival);
            blocked.GetComponent<Rigidbody>().isKinematic = true;
            Physics.SyncTransforms();
            Step(squirrel, physics, 1000);
            Check(squirrel.KnownNestCount == 1 && squirrel.CarriedCount == 3,
                "a rival nest blocks construction without spending gathered materials");
            Object.DestroyImmediate(blocked.gameObject);
            Physics.SyncTransforms();
            for (int i = 0; i < 2500 && squirrel.KnownNestCount == 1; i++) Step(squirrel, physics, 1);
            Check(squirrel.KnownNestCount == 2 && squirrel.Nest != nest && squirrel.Nest.Owner == squirrel &&
                squirrel.Nest.StoredFoodCount == 0 && squirrel.CarriedCount == 0, "missing nest is rebuilt from exactly three collected food items");
            Check(nest.StoredFoodCount == 2 && nest.Owner == squirrel, "stolen nest retains its real inventory and original owner");

            var replacement = squirrel.Nest; Vector3 replacementPosition = replacement.transform.position;
            Set(replacement, "foodCapacity", 2);
            for (int i = 0; i < 2; i++)
            {
                var food = Food(scene, fm, replacementPosition + Vector3.up * (i + 1));
                food.Reserve(squirrel); Check(replacement.Deposit(squirrel, food), "fill replacement storage");
            }
            for (int i = 0; i < 3; i++) Food(scene, fm, replacementPosition + new Vector3(-2f + i * 2f, 1f, -3f));
            for (int i = 0; i < 2500 && squirrel.KnownNestCount < 3; i++) Step(squirrel, physics, 1);
            Check(squirrel.KnownNestCount == 3 && replacement.StoredFoodCount == 2 && squirrel.Nest != replacement,
                "full storage creates a separate additional nest without discarding the full one");
            float spacing = Vector3.ProjectOnPlane(squirrel.Nest.transform.position - replacementPosition, Vector3.up).magnitude;
            Check(spacing >= 3.5f && spacing <= 10.1f, "additional nests stay nearby and do not overlap");
            Check(Vector3.ProjectOnPlane(tree.GroundPosition - squirrel.Nest.transform.position, Vector3.up).magnitude <= 6f,
                "new nests require a nearby tree");

            // A missing stocked nest can be discovered and carried back during later travel.
            carrier.Release();
            body.position = actor.transform.position = new Vector3(21f, 0.5f, 0f);
            Set(squirrel, "discoveryTimer", 0f);
            Physics.SyncTransforms();
            Step(squirrel, physics, 1);
            Check(squirrel.Nest == nest && (squirrel.CurrentActivity == Squirrel.Activity.PackingNest || squirrel.IsMigrating),
                "local rediscovery lets the owner choose to retrieve its stocked old nest");
            for (int i = 0; i < 2500 && (nest.IsMoving || squirrel.CurrentActivity == Squirrel.Activity.PackingNest || squirrel.IsMigrating); i++)
                Step(squirrel, physics, 1);
            Check(!nest.IsMoving && nest.StoredFoodCount == 2 && nest.transform.position.x < 15f,
                $"recovered original nest is carried back with its contents intact (state={squirrel.CurrentActivity}, actor={body.position}, nest={nest.transform.position}, moving={nest.IsMoving}, stored={nest.StoredFoodCount})");
            Debug.Log("Squirrel Nest validation passed: real player withdrawal/deposit, capacity, delayed discovery, paid rebuilding, nearby extra nests and stocked-nest recovery.");
        }
        finally
        {
            foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);
            UnityEngine.Random.state = random;
        }
    }
    private static GameObject Primitive(Scene scene, PrimitiveType type, Vector3 position)
    { var go = GameObject.CreatePrimitive(type); SceneManager.MoveGameObjectToScene(go, scene); go.transform.position = position; return go; }
    private static SquirrelFood Food(Scene scene, SquirrelSystemManager manager, Vector3 position)
    { var f = Primitive(scene, PrimitiveType.Capsule, position).AddComponent<SquirrelFood>(); manager.Register(f); return f; }
    private static void Step(Squirrel squirrel, PhysicsScene physics, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Set(squirrel, "nextSearch", 0f);
            Invoke(squirrel, "TickCreature", 0.02f);
            Invoke(squirrel.GetComponent<SquirrelMotor>(), "FixedUpdate");
            physics.Simulate(0.02f);
        }
    }
    private static void Set(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    private static void Invoke(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
    private static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException("Squirrel Nest validation failed: " + message); }
}
