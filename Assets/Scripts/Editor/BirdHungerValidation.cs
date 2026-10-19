using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit Play Mode checks; only runs when invoked, using disposable local physics objects.</summary>
public static class BirdHungerValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Bird hunger validation: " + message);
        checks++;
    }

    private static void Float(Creature creature, string field, float value)
    {
        var fields = new SerializedObject(creature);
        fields.FindProperty(field).floatValue = value;
        fields.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Diet(Creature creature, Creature.FoodTag food, Creature.FoodTag diet)
    {
        var fields = new SerializedObject(creature);
        fields.FindProperty("foodTags").intValue = (int)food;
        fields.FindProperty("dietTags").intValue = (int)diet;
        fields.ApplyModifiedPropertiesWithoutUndo();
    }

    private static BirdCreature Bird(Scene scene, Vector3 position, float hunger = 50f)
    {
        var root = new GameObject("Hunger Test Bird");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = position;
        root.AddComponent<BoxCollider>().size = new Vector3(0.45f, 0.3f, 0.8f);
        BirdCreature bird = root.AddComponent<BirdCreature>();
        Diet(bird, Creature.FoodTag.Meat, Creature.FoodTag.Plant);
        Float(bird, "hunger", hunger);
        Float(bird, "huntDesire", 4f);
        root.SetActive(true);
        bird.FlockManager.enabled = false;
        return bird;
    }

    private static BirdHungerFoodProbe Food(Scene scene, float nutrition = 120f)
    {
        var root = new GameObject("Hunger Test Food");
        SceneManager.MoveGameObjectToScene(root, scene);
        BirdHungerFoodProbe food = root.AddComponent<BirdHungerFoodProbe>();
        Diet(food, Creature.FoodTag.Plant, Creature.FoodTag.None);
        Float(food, "nutrition", nutrition);
        return food;
    }

    private static void Tick(BirdCreature bird, float seconds) =>
        typeof(BirdCreature).GetMethod("TickCreature", Private).Invoke(bird, new object[] { seconds });

    private static void Clear(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.GetComponent<BirdFlockManager>() == null) Object.DestroyImmediate(root);
    }

    private static List<BirdCreature> Group(Scene scene, int count)
    {
        var group = new List<BirdCreature>();
        for (int i = 0; i < count; i++) group.Add(Bird(scene, new Vector3(i % 6, 0f, i / 6)));
        return group;
    }

    [MenuItem("Tools/Creatures/Validate Bird Hunger and Sharing (Play Mode)")]
    public static string Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before running hunger validation.");
        checks = 0;
        Scene scene = SceneManager.CreateScene("Bird Hunger Validation " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        try
        {
            BirdCreature bird = Bird(scene, Vector3.zero);
            Check(bird.Hunger == 50f && bird.Nutrition == 120f && bird.EffectiveHuntDesire == 2f, "default hunger/nutrition and scaled maximum");
            Tick(bird, 40f);
            Check(bird.Hunger == 60f && bird.HuntDesire == 4f, "hunger rate and artist weight remain independent");
            bird.SetSimulationEnabled(false);
            Tick(bird, 40f);
            Check(bird.Hunger == 60f, "simulation pause stops hunger");
            bird.SetSimulationEnabled(true);
            bird.enabled = false;
            Tick(bird, 40f);
            Check(bird.Hunger == 60f, "disabled bird does not accumulate hunger");
            bird.enabled = true;
            Tick(bird, 10000f);
            Check(bird.Hunger == 100f && bird.EffectiveHuntDesire == 4f && !bird.IsDead && bird.Age == 40f,
                "hunger caps without starvation or Age changes");

            BirdHungerFoodProbe food = Food(scene, 0f);
            Float(bird, "hunger", 19.99f);
            bird.FlockManager.SimulateStep(0.02f, 0f);
            Check(bird.EffectiveHuntDesire == 0f && bird.FoodTarget == null && !bird.TryConsumeFood(food), "below threshold neither seeks nor eats");
            Float(bird, "hunger", 20f);
            Check(bird.EffectiveHuntDesire > 0f && bird.TryConsumeFood(food), "threshold is inclusive");
            Clear(scene);

            foreach (int count in new[] { 1, 6, 24 })
            {
                List<BirdCreature> birds = Group(scene, count);
                food = Food(scene);
                Check(birds[0].TryConsumeFood(food), "meal accepted for " + count + " birds");
                float expected = Mathf.Max(0f, 50f - 120f / count);
                foreach (BirdCreature recipient in birds) Check(Mathf.Abs(recipient.Hunger - expected) < 0.001f, "equal fixed-budget allocation");
                Check(!birds[count - 1].TryConsumeFood(food) && food.ConsumptionCount == 1, "one meal cannot award twice");
                Clear(scene);
            }

            bird = Bird(scene, Vector3.zero);
            BirdCreature nearFull = Bird(scene, Vector3.right, 5f);
            food = Food(scene, 40f);
            Check(bird.TryConsumeFood(food) && nearFull.Hunger == 0f && bird.Hunger == 15f,
                "small recipient is capped and unused share redistributed below the appetite threshold");
            Clear(scene);

            bird = Bird(scene, Vector3.zero);
            BirdCreature boundary = Bird(scene, Vector3.right * 100f);
            BirdCreature outside = Bird(scene, Vector3.right * 10.01f);
            bird.FlockManager.SimulateStep(0.02f, 0f);
            boundary.FlightBody.position = Vector3.right * 10f; // Deliberately differs from the last octree snapshot.
            food = Food(scene, 40f);
            Check(bird.TryConsumeFood(food) && bird.Hunger == 30f && boundary.Hunger == 30f && outside.Hunger == 50f,
                "sharing uses current positions and inclusive radius, not stale flock snapshots");
            Clear(scene);

            bird = Bird(scene, Vector3.right * 20f);
            food = Food(scene, 40f);
            Check(bird.TryConsumeFood(food) && bird.Hunger == 10f, "actual eater receives nutrition even outside the peer radius");
            Clear(scene);

            bird = Bird(scene, Vector3.zero);
            var excluded = new List<BirdCreature>();
            BirdCreature full = Bird(scene, Vector3.right, 0f);
            BirdCreature paused = Bird(scene, Vector3.left);
            paused.SetSimulationEnabled(false);
            excluded.Add(paused);
            BirdCreature disabled = Bird(scene, Vector3.up);
            disabled.enabled = false;
            excluded.Add(disabled);
            BirdCreature incompatible = Bird(scene, Vector3.down);
            Diet(incompatible, Creature.FoodTag.Meat, Creature.FoodTag.Meat);
            excluded.Add(incompatible);
            var carrierRoot = new GameObject("Hunger Test Carrier");
            carrierRoot.SetActive(false);
            SceneManager.MoveGameObjectToScene(carrierRoot, scene);
            var anchor = new GameObject("Carry Point");
            anchor.transform.SetParent(carrierRoot.transform, false);
            var carrier = carrierRoot.AddComponent<PlayerCreatureCarrier>();
            carrier.enabled = false;
            typeof(PlayerCreatureCarrier).GetField("carryPoint", Private).SetValue(carrier, anchor.transform);
            carrierRoot.SetActive(true);
            BirdCreature carried = Bird(scene, Vector3.forward);
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", Private).Invoke(carrier, new object[] { carried });
            Tick(carried, 40f);
            Check(carried.IsCarried && carried.Hunger == 60f && !carried.TryConsumeFood(Food(scene, 0f)), "carried hunger advances but eating is blocked");
            Float(carried, "hunger", 50f);
            excluded.Add(carried);
            var webRoot = new GameObject("Hunger Test Web");
            SceneManager.MoveGameObjectToScene(webRoot, scene);
            SkyWeb web = webRoot.AddComponent<SkyWeb>();
            BirdCreature captured = Bird(scene, Vector3.back);
            Check(web.TryCapture(captured.FlightBody, captured.FlightBody.position), "web capture fixture");
            Tick(captured, 40f);
            Check(captured.Hunger == 50f, "capture pauses hunger");
            excluded.Add(captured);
            BirdCreature bubbleHeld = Bird(scene, Vector3.forward * 2f);
            var bubbleRoot = new GameObject("Hunger Test Bubble");
            SceneManager.MoveGameObjectToScene(bubbleRoot, scene);
            bubbleRoot.transform.position = bubbleHeld.transform.position;
            TrappingBubble bubble = bubbleRoot.AddComponent<TrappingBubble>();
            Check(bubble.TryCapture(bubbleHeld.gameObject), "bubble capture fixture");
            Tick(bubbleHeld, 40f);
            Check(bubbleHeld.Hunger == 50f, "bubble capture pauses hunger");
            excluded.Add(bubbleHeld);
            BirdCreature pending = Bird(scene, Vector3.left * 2f);
            typeof(Creature).GetField("consumptionPending", Private).SetValue(pending, true);
            excluded.Add(pending);
            BirdCreature dead = Bird(scene, Vector3.right * 2f);
            dead.Die();
            Tick(dead, 40f);
            Check(dead.Hunger == 50f && dead.IsDead, "death stops hunger");
            food = Food(scene, 40f);
            Check(bird.TryConsumeFood(food) && bird.Hunger == 10f && full.Hunger == 0f, "only eligible hungry birds receive shares");
            foreach (BirdCreature recipient in excluded) Check(recipient.Hunger == 50f, "excluded recipient unchanged");
            Clear(scene);

            bird = Bird(scene, Vector3.zero);
            BirdCreature friend = Bird(scene, Vector3.right);
            food = Food(scene);
            Diet(food, Creature.FoodTag.Meat, Creature.FoodTag.None);
            Check(!bird.TryConsumeFood(food) && bird.Hunger == 50f && friend.Hunger == 50f,
                "rejected meal awards no nutrition");
            Scene otherScene = SceneManager.CreateScene("Hunger Sharing Boundary " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            try
            {
                BirdCreature otherSceneBird = Bird(otherScene, Vector3.zero);
                food = Food(scene, 40f);
                Check(bird.TryConsumeFood(food) && bird.Hunger == 30f && friend.Hunger == 30f && otherSceneBird.Hunger == 50f,
                    "sharing cannot cross scene boundaries");
            }
            finally { SceneManager.UnloadSceneAsync(otherScene); }
            Clear(scene);

            // Cache before callbacks: mutation and immediate removal must not change the award.
            foreach (bool immediateDestroy in new[] { false, true })
            {
                List<BirdCreature> birds = Group(scene, 6);
                food = Food(scene);
                food.MutateOnConsume = true;
                food.DestroyOnConsume = immediateDestroy;
                Check(birds[0].TryConsumeFood(food), "mutating meal still succeeds");
                foreach (BirdCreature recipient in birds) Check(recipient.Hunger == 30f, "original food nutrition, position and diet metadata survive callbacks");
                Clear(scene);
            }

            List<BirdCreature> infectedGroup = Group(scene, 6);
            CorruptSeed prefab = AssetDatabase.LoadAssetAtPath<CorruptSeed>(CorruptSeedSetup.SeedPath);
            Check(prefab != null, "Corrupt Seed integration prefab exists");
            var staging = new GameObject("Inactive Seed Staging");
            staging.SetActive(false);
            SceneManager.MoveGameObjectToScene(staging, scene);
            CorruptSeed seed = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity, staging.transform);
            Float(seed, "nutrition", 120f);
            seed.transform.SetParent(null, true);
            Check(infectedGroup[0].TryConsumeFood(seed), "Corrupt Seed overrides Consume without calling base");
            foreach (BirdCreature recipient in infectedGroup)
                Check(recipient.Hunger == 30f && recipient.TryGetComponent<CorruptSeedInfection>(out _) == (recipient == infectedGroup[0]),
                    "nutrition is shared but infection applies only to the actual eater");

            string report = "Bird hunger and sharing passed: " + checks + " assertions.";
            Directory.CreateDirectory("Temp/BirdFlockValidation");
            File.WriteAllText("Temp/BirdFlockValidation/hunger.txt", report);
            Debug.Log(report);
            return report;
        }
        finally { SceneManager.UnloadSceneAsync(scene); }
    }
}
