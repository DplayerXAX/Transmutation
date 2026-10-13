using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit lifecycle/feeding checks using real physics in a disposable scene.</summary>
public static class BirdLifeValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Bird life validation: " + message);
        checks++;
    }

    private static void Float(Creature creature, string field, float value)
    {
        var data = new SerializedObject(creature);
        data.FindProperty(field).floatValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Tags(Creature creature, Creature.FoodTag food, Creature.FoodTag diet)
    {
        var data = new SerializedObject(creature);
        data.FindProperty("foodTags").intValue = (int)food;
        data.FindProperty("dietTags").intValue = (int)diet;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Age(Creature creature, float seconds) =>
        typeof(Creature).GetMethod("AdvanceAge", Private).Invoke(creature, new object[] { seconds });

    private static T Create<T>(Scene scene, Vector3 position) where T : Component
    {
        GameObject root = new GameObject(typeof(T).Name);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = position;
        return root.AddComponent<T>();
    }

    private static BirdCreature Bird(GameObject prefab, Transform staging, Vector3 position)
    {
        GameObject root = Object.Instantiate(prefab, position, Quaternion.identity, staging);
        root.transform.SetParent(null, true);
        root.GetComponent<BirdCreature>().FlockManager.enabled = false;
        return root.GetComponent<BirdCreature>();
    }

    private static BirdBones SingleBones(Scene scene)
    {
        BirdBones found = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            BirdBones bones = root.GetComponent<BirdBones>();
            if (bones == null) continue;
            Check(found == null, "one remains object per bird");
            found = bones;
        }
        Check(found != null, "death creates bones");
        return found;
    }

    private static PlayerCreatureCarrier Carrier(Scene scene, Vector3 position)
    {
        GameObject root = new GameObject("Validation Carrier");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        GameObject anchor = new GameObject("Anchor");
        anchor.transform.SetParent(root.transform, false);
        anchor.transform.position = position;
        PlayerCreatureCarrier carrier = root.AddComponent<PlayerCreatureCarrier>();
        carrier.enabled = false;
        typeof(PlayerCreatureCarrier).GetField("carryPoint", Private).SetValue(carrier, anchor.transform);
        root.SetActive(true);
        return carrier;
    }

    [MenuItem("Tools/Creatures/Validate Bird Life and Food (Play Mode)")]
    public static string Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        checks = 0;
        Scene scene = SceneManager.CreateScene("Bird Life Validation " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        PhysicsScene physics = scene.GetPhysicsScene();
        GameObject staging = new GameObject("Inactive Staging");
        staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, scene);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BirdFlockSetup.BirdPath);
        try
        {
            Check(prefab != null && prefab.GetComponent<BirdCreature>().BonesPrefab != null, "authored bird references bones prefab");
            CreatureLifecycleValidationProbe probe = Create<CreatureLifecycleValidationProbe>(scene, Vector3.zero);
            Check(probe.Age == 40f && probe.AgeRate == 0f && !probe.IsDead, "shared defaults");
            Age(probe, 100f);
            Check(probe.Age == 40f, "zero rate never spends lifetime");
            Float(probe, "ageRate", 2f);
            Age(probe, 5f);
            Check(probe.Age == 30f, "rate uses seconds");
            probe.SetSimulationEnabled(false);
            Age(probe, 100f);
            Check(probe.Age == 30f, "simulation pause pauses age");
            probe.SetSimulationEnabled(true);
            Age(probe, 20f);
            Age(probe, 20f);
            probe.Die();
            probe.SetSimulationEnabled(true);
            Check(probe.Age == 0f && probe.IsDead && !probe.SimulationEnabled && probe.DeathCount == 1,
                "age clamps; death once; cannot resume dead simulation");
            Check(probe.gameObject.activeInHierarchy, "base death retains object");
            Object.DestroyImmediate(probe.gameObject);

            CreatureLifecycleValidationProbe eater = Create<CreatureLifecycleValidationProbe>(scene, Vector3.zero);
            CreatureLifecycleValidationProbe food = Create<CreatureLifecycleValidationProbe>(scene, Vector3.right);
            Tags(eater, Creature.FoodTag.None, Creature.FoodTag.Plant);
            Tags(food, Creature.FoodTag.Plant, Creature.FoodTag.None);
            var diet = new SerializedObject(eater);
            diet.FindProperty("canEatSameType").boolValue = true;
            diet.ApplyModifiedPropertiesWithoutUndo();
            food.Consume(eater);
            Check(food.ConsumptionCount == 1 && !food.IsDead && food.gameObject.activeInHierarchy,
                "Consume effects do not implicitly kill or remove food");
            Check(eater.TryConsumeFood(food), "feeding caller completes consumption");
            Check(food.ConsumptionCount == 2 && food.LastConsumer == eater && food.DeathCount == 1 && food.IsConsumptionPending,
                "feeding calls effects then death once");
            Check(!eater.TryConsumeFood(food) && food.ConsumptionCount == 2, "deferred destruction blocks duplicate feeding");
            Object.DestroyImmediate(food.gameObject);
            Object.DestroyImmediate(eater.gameObject);

            BirdCreature first = Bird(prefab, staging.transform, new Vector3(0f, 20f, 0f));
            BirdCreature second = Bird(prefab, staging.transform, new Vector3(-2f, 20f, 0f));
            BirdFlockManager manager = first.FlockManager;
            manager.SkyBounds = new Bounds(new Vector3(0f, 20f, 0f), new Vector3(20f, 10f, 20f));
            Check(first.HuntRadius == 25f && first.HuntDesire == 2f && first.DietTags == Creature.FoodTag.Plant, "bird hunt defaults and plant diet");
            food = Create<CreatureLifecycleValidationProbe>(scene, new Vector3(0f, 0f, 0f));
            Tags(food, Creature.FoodTag.Plant, Creature.FoodTag.None);
            manager.SimulateStep(0.02f, 0f);
            Check(first.FoodTarget == food && second.FoodTarget == food, "different birds share outside-bounds food");
            Float(first, "huntRadius", 19f);
            manager.SimulateStep(0.02f, 0f);
            Check(first.FoodTarget == null, "exact hunt radius excludes distant food");
            Float(first, "huntRadius", 25f);
            Float(first, "huntDesire", 0f);
            manager.SimulateStep(0.02f, 0f);
            Check(first.FoodTarget == null, "zero desire removes food target");
            Check(!first.TryConsumeFood(food), "zero desire also blocks the public feeding entry point");
            Float(first, "huntDesire", 2f);
            first.SetSimulationEnabled(false);
            Check(!first.TryConsumeFood(food), "paused bird cannot eat");
            first.SetSimulationEnabled(true);
            food.enabled = false;
            manager.SimulateStep(0.02f, 0f);
            Check(second.FoodTarget == null, "disabled food unregisters");
            food.enabled = true;
            Tags(food, Creature.FoodTag.Matter, Creature.FoodTag.None);
            manager.SimulateStep(0.02f, 0f);
            Check(first.FoodTarget == null, "non-Plant food is rejected");
            Tags(food, Creature.FoodTag.Plant, Creature.FoodTag.None);
            Scene otherScene = SceneManager.CreateScene("Food Scene Boundary " + Guid.NewGuid());
            SceneManager.MoveGameObjectToScene(food.gameObject, otherScene);
            manager.SimulateStep(0.02f, 0f);
            Check(first.FoodTarget == null && second.FoodTarget == null, "food queries cannot cross scene boundaries");
            SceneManager.MoveGameObjectToScene(food.gameObject, scene);
            SceneManager.UnloadSceneAsync(otherScene);
            manager.SimulateStep(0.02f, 0f);
            Check(first.FoodTarget == food, "food scene transfer is tracked without re-enabling");
            Type registry = typeof(Creature).Assembly.GetType("CreatureRegistry");
            registry.GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            registry.GetMethod("RestoreMembership", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            registry.GetMethod("RestoreMembership", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            var members = (List<Creature>)registry.GetField("creatures", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Check(new HashSet<Creature>(members).Count == members.Count, "registry reset/restore does not duplicate membership");
            manager.SimulateStep(0.02f, 0f);
            Check(first.FoodTarget == food, "registry restoration recovers existing active food");
            Object.DestroyImmediate(food.gameObject);
            manager.SimulateStep(0.02f, 0f);
            Check(first.FoodTarget == null, "destroyed target disappears safely");

            // A repeatable numerical check: food adds its weighted steering before the shared clamp.
            var calculate = typeof(BirdCreature).GetMethod("CalculateFlightVelocity", Private);
            Vector3[] positions = { new Vector3(0f, 20f, 0f), new Vector3(2f, 20f, 0f) };
            Vector3[] velocities = { Vector3.forward * 4.5f, Vector3.right * 4.5f };
            var neighbors = new List<int> { 0, 1 };
            Vector3 Calculate(Vector3 attraction) => (Vector3)calculate.Invoke(first, new object[]
                { 0, positions, velocities, neighbors, Vector3.up, 1f, 0f, 0.02f, attraction });
            Vector3 withoutFood = Calculate(Vector3.zero);
            Vector3 withFood = Calculate(Vector3.down);
            Check(withFood.y < withoutFood.y, "food attraction combines with upward bounds and neighbor steering");
            Check(withFood.magnitude <= first.FlightSpeedRange.y + 0.001f &&
                (withFood - velocities[0]).magnitude <= 5f * 0.02f + 0.001f, "combined steering obeys speed and acceleration limits");

            // Real collider contact; Repeat Stay cannot consume deferred-destroy food a second time.
            first.FlightBody.position = new Vector3(0f, 20f, 0f);
            first.FlightBody.linearVelocity = Vector3.forward * 4.5f;
            food = Create<CreatureLifecycleValidationProbe>(scene, new Vector3(0f, 20f, 0.35f));
            Tags(food, Creature.FoodTag.Plant, Creature.FoodTag.None);
            food.gameObject.AddComponent<BoxCollider>().size = Vector3.one;
            physics.Simulate(0.02f);
            Check(food.IsConsumptionPending && food.ConsumptionCount == 1 && food.IsDead, "physics contact consumes compatible food");
            Check(!second.TryConsumeFood(food), "second bird cannot repeat contact consumption");
            Object.DestroyImmediate(food.gameObject);
            Object.DestroyImmediate(second.gameObject);

            first.FlightBody.position = new Vector3(0f, 10f, 0f);
            Vector3 releaseVelocity = new Vector3(1f, 2f, 3f);
            first.FlightBody.linearVelocity = releaseVelocity;
            Float(first, "ageRate", 40f);
            Age(first, 1f);
            first.Die();
            BirdBones bones = SingleBones(scene);
            Rigidbody boneBody = bones.GetComponent<Rigidbody>();
            Check(!first.gameObject.activeInHierarchy && manager.RegisteredBirdCount == 0, "dead bird leaves flock immediately");
            Check(bones.FoodTags == Creature.FoodTag.Matter && bones.DietTags == Creature.FoodTag.None && bones.Age == 40f && bones.AgeRate == 0f,
                "bones are passive Matter with shared lifespan defaults");
            Check(boneBody.position == new Vector3(0f, 10f, 0f) && boneBody.linearVelocity == releaseVelocity && boneBody.useGravity,
                "bones retain death position and velocity with gravity");
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.localScale = new Vector3(50f, 1f, 50f);
            ground.transform.position = Vector3.down * 0.5f;
            for (int i = 0; i < 500; i++) physics.Simulate(0.02f);
            Check(boneBody.position.y > -0.1f && boneBody.position.y < 0.5f && boneBody.linearVelocity.sqrMagnitude < 0.1f,
                "bones fall and settle on normal ground collisions");
            PlayerCreatureCarrier boneCarrier = Carrier(scene, boneBody.position);
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(boneCarrier, new object[] { bones });
            Check(bones.IsCarried && boneCarrier.CarriedCreature == bones, "bones can be picked up with normal carrier");
            boneCarrier.Release();
            Check(!bones.IsCarried && boneBody.useGravity && !boneBody.isKinematic, "released bones resume falling physics");
            Object.DestroyImmediate(boneCarrier.gameObject);
            bones.Consume(null);
            Check(!bones.IsDead && bones.gameObject.activeInHierarchy, "base Consume hook alone leaves bones intact");
            eater = Create<CreatureLifecycleValidationProbe>(scene, Vector3.up);
            Tags(eater, Creature.FoodTag.None, Creature.FoodTag.Matter);
            Check(eater.TryConsumeFood(bones) && bones.IsDead && bones.IsConsumptionPending, "Matter eater consumes bones");
            Check(!eater.TryConsumeFood(bones), "bones cannot be consumed twice");
            Object.DestroyImmediate(bones.gameObject);
            Object.DestroyImmediate(eater.gameObject);

            // Test both consumption of a bird and actual capture/hand ownership on death.
            foreach (string attachment in new[] { "consume", "carry", "web", "bubble", "fallback" })
            {
                first = Bird(prefab, staging.transform, new Vector3(0f, 10f, 0f));
                PlayerCreatureCarrier carrier = null;
                BubbleCapture bubbleCapture = null;
                WebCapture webCapture = null;
                SkyWeb web = null;
                if (attachment == "carry")
                {
                    carrier = Carrier(scene, first.transform.position);
                    typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { first });
                    Check(first.IsCarried, "bird is carried before transformation");
                    Float(first, "ageRate", 1f);
                    Age(first, 1f);
                    Check(first.Age == 39f, "carrying alone does not pause aging");
                }
                if (attachment == "web")
                {
                    web = Create<SkyWeb>(scene, first.transform.position);
                    webCapture = first.GetComponent<WebCapture>();
                    Check(web.TryCapture(first.FlightBody, first.transform.position), "bird attaches to web");
                    Float(first, "ageRate", 1f);
                    Age(first, 10f);
                    Check(first.Age == 40f, "web pause freezes age");
                }
                if (attachment == "bubble")
                {
                    TrappingBubble bubble = Create<TrappingBubble>(scene, first.transform.position);
                    Check(bubble.TryCapture(first.gameObject), "bubble captures bird");
                    bubbleCapture = first.GetComponent<BubbleCapture>();
                    Float(first, "ageRate", 1f);
                    Age(first, 10f);
                    Check(first.Age == 40f, "bubble pause freezes age");
                }
                if (attachment == "fallback")
                    typeof(BirdCreature).GetField("bonesPrefab", Private).SetValue(first, null);
                if (attachment == "carry" || attachment == "web" || attachment == "bubble")
                {
                    CreatureLifecycleValidationProbe contactFood = Create<CreatureLifecycleValidationProbe>(scene, first.transform.position);
                    Tags(contactFood, Creature.FoodTag.Plant, Creature.FoodTag.None);
                    first.SetSimulationEnabled(true);
                    Check(!first.TryConsumeFood(contactFood) && !contactFood.IsConsumptionPending,
                        attachment + " ownership blocks feeding even with simulation manually enabled");
                    if (attachment != "carry") first.SetSimulationEnabled(false);
                    Object.DestroyImmediate(contactFood.gameObject);
                }
                if (attachment == "consume")
                {
                    eater = Create<CreatureLifecycleValidationProbe>(scene, Vector3.zero);
                    Tags(eater, Creature.FoodTag.None, Creature.FoodTag.Everything);
                    Check(eater.TryConsumeFood(first), "consuming bird calls its death transformation");
                    Check(!eater.TryConsumeFood(first), "consumed bird cannot produce duplicate remains");
                }
                else first.Die();
                first.Die();
                bones = SingleBones(scene);
                Check(!bones.IsCarried && !bones.IsBubbleCaptured && !bones.IsDead && bones.GetComponent<Rigidbody>().useGravity,
                    attachment + " death releases all ownership and creates falling bones");
                if (carrier != null) Check(carrier.CarriedCreature == null, "death clears carrier ownership");
                if (webCapture != null)
                {
                    Check(!webCapture.IsCaptured && !first.SimulationEnabled && web.CaptureCount == 0, "web release cannot revive dead bird");
                    Check(!web.IsEligible(bones.GetComponent<Rigidbody>()), "released bones inherit a short silk recapture cooldown");
                }
                if (bubbleCapture != null) Check(!bubbleCapture.IsCaptured && !first.SimulationEnabled, "bubble release cannot revive dead bird");
                Object.DestroyImmediate(bones.gameObject);
                Object.DestroyImmediate(first.gameObject);
                if (eater != null) Object.DestroyImmediate(eater.gameObject);
                if (web != null) Object.DestroyImmediate(web.gameObject);
                if (carrier != null) Object.DestroyImmediate(carrier.gameObject);
            }

            TrappingBubble timerBubble = Create<TrappingBubble>(scene, Vector3.up * 5f);
            timerBubble.SimulateStep(1f);
            Check(timerBubble.ElapsedTime == 1f && timerBubble.Age == 40f, "bubble timer stays separate from remaining lifetime");
            timerBubble.SetSimulationEnabled(false);
            timerBubble.SimulateStep(1f);
            Check(timerBubble.ElapsedTime == 1f, "bubble pause preserves elapsed timer");
            timerBubble.SetSimulationEnabled(true);
            typeof(TrappingBubble).GetField("lifetimeSeconds", Private).SetValue(timerBubble, 2f);
            timerBubble.SimulateStep(1f);
            Check(timerBubble.IsPopped, "existing bubble timeout still pops");

            string report = "Bird life and food passed: " + checks + " assertions.";
            Directory.CreateDirectory("Temp/BirdFlockValidation");
            File.WriteAllText("Temp/BirdFlockValidation/life-food.txt", report);
            Debug.Log(report);
            return report;
        }
        finally { SceneManager.UnloadSceneAsync(scene); }
    }
}
