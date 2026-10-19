using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using FoodTag = Creature.FoodTag;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>Real feeding, inheritance, death, and physics checks in a disposable Play Mode scene.</summary>
public static class CorruptSeedValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("Corrupt Seed validation: " + description);
        checks++;
    }

    private static void Tags(Creature creature, FoodTag food, FoodTag diet)
    {
        using (var fields = new SerializedObject(creature))
        {
            fields.FindProperty("foodTags").intValue = (int)food;
            fields.FindProperty("dietTags").intValue = (int)diet;
            fields.FindProperty("canEatSameType").boolValue = true;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static CreatureLifecycleValidationProbe Host(Scene scene, FoodTag food, FoodTag diet = FoodTag.Plant)
    {
        GameObject root = new GameObject("Seed Validation Host");
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = new Vector3(0f, 2f, 0f);
        var host = root.AddComponent<CreatureLifecycleValidationProbe>();
        Tags(host, food, diet);
        return host;
    }

    private static CorruptSeed Seed(CorruptSeed prefab, Transform staging, FoodTag tags = FoodTag.Plant)
    {
        CorruptSeed seed = Object.Instantiate(prefab, Vector3.up * 5f, Quaternion.identity, staging);
        Tags(seed, tags, FoodTag.None);
        seed.transform.SetParent(null, true);
        return seed;
    }

    private static void Feed(Creature host, CorruptSeed seed)
    {
        Check(host.TryConsumeFood(seed), "compatible meal completes");
        Check(seed.IsConsumptionPending && seed.IsDead, "meal claims and kills the seed");
        Check(!host.TryConsumeFood(seed), "deferred destruction cannot repeat a meal");
        // Force actual removal now: subsequent reproduction must not depend on the eaten instance.
        Object.DestroyImmediate(seed.gameObject);
    }

    private static List<CorruptSeed> Seeds(Scene scene)
    {
        var seeds = new List<CorruptSeed>();
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.TryGetComponent<CorruptSeed>(out var seed) && !seed.IsConsumptionPending)
                seeds.Add(seed);
        return seeds;
    }

    private static List<CorruptSeed> Brood(Scene scene, Vector3 hostPosition, FoodTag expectedTags)
    {
        List<CorruptSeed> seeds = Seeds(scene);
        Check(seeds.Count >= 3 && seeds.Count <= 5, "one death produces 3-5 seeds total");
        foreach (CorruptSeed seed in seeds)
        {
            Vector3 offset = seed.transform.position - hostPosition;
            Check(seed.FoodTags == expectedTags && seed.DietTags == FoodTag.None,
                "offspring preserve all inherited food flags and have no diet");
            Check(seed.gameObject.activeInHierarchy && !seed.IsDead && seed.AgeRate == 0f,
                "offspring activate as live seeds without automatic aging");
            Check(new Vector2(offset.x, offset.z).magnitude <= 0.5001f && Mathf.Abs(offset.y - 0.25f) < 0.0001f,
                "offspring scatter within the specified radius and height");
        }
        return seeds;
    }

    private static void Clear(Scene scene, GameObject staging)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root != staging) Object.DestroyImmediate(root);
    }

    private static void ExpectLog(LogType type, string marker, Action action)
    {
        int matches = 0;
        void Capture(string message, string stack, LogType actualType)
        {
            if (actualType == type && message.Contains(marker)) matches++;
        }
        Application.logMessageReceived += Capture;
        try { action(); }
        finally { Application.logMessageReceived -= Capture; }
        Check(matches == 1, "expected failure is reported once: " + marker);
    }

    [MenuItem("Tools/Creatures/Validate Corrupt Seed (Play Mode)")]
    public static async void Validate()
    {
        try
        {
            Run();
            Debug.Log(await RunUnloadCheck());
        }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    /// <summary>Await real asynchronous scene teardown; it must not be interpreted as host death.</summary>
    public static async Task<string> RunUnloadCheck()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before validating scene unload.");
        Scene scene = SceneManager.CreateScene("Corrupt Seed Unload Validation " + Guid.NewGuid());
        try
        {
            var host = Host(scene, FoodTag.Meat);
            int deaths = 0;
            host.Died += dead => deaths++;
            CorruptSeed prefab = AssetDatabase.LoadAssetAtPath<CorruptSeed>(CorruptSeedSetup.SeedPath);
            CorruptSeed seed = Object.Instantiate(prefab);
            SceneManager.MoveGameObjectToScene(seed.gameObject, scene);
            if (!host.TryConsumeFood(seed) || !host.TryGetComponent<CorruptSeedInfection>(out _))
                throw new InvalidOperationException("Could not prepare infected unload host.");
            Object.DestroyImmediate(seed.gameObject);
            AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
            while (!unload.isDone) await Task.Delay(10);
            if (host != null || deaths != 0)
                throw new InvalidOperationException("Scene unload retained the host or notified death.");
            string report = "Corrupt Seed scene unload passed: infected host removed without death or offspring.";
            Directory.CreateDirectory("Temp/CorruptSeedValidation");
            File.WriteAllText("Temp/CorruptSeedValidation/unload-result.txt", report);
            return report;
        }
        finally
        {
            if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        }
    }

    public static string Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before validating Corrupt Seed.");
        checks = 0;
        Random.State previousRandom = Random.state;
        Random.InitState(1763);
        Scene scene = SceneManager.CreateScene("Corrupt Seed Validation " + Guid.NewGuid(),
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        GameObject staging = new GameObject("Inactive Seed Validation Staging");
        staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, scene);
        try
        {
            CorruptSeed prefab = AssetDatabase.LoadAssetAtPath<CorruptSeed>(CorruptSeedSetup.SeedPath);
            Check(prefab != null && prefab.OffspringPrefab == prefab, "authored seed has a durable offspring template");
            Check(prefab.FoodTags == FoodTag.Plant && prefab.DietTags == FoodTag.None && prefab.AgeRate == 0f,
                "authored seed is passive Plant food");
            Check(Mathf.Abs(prefab.GetComponent<SphereCollider>().radius - 0.1f) < 0.0001f &&
                Mathf.Abs(prefab.GetComponent<Rigidbody>().mass - 0.05f) < 0.0001f &&
                prefab.GetComponent<Rigidbody>().useGravity, "authored seed has the specified collider, mass, and gravity");

            // The first listener fails deliberately; remaining listeners and subclass death must still run.
            var host = Host(scene, FoodTag.Meat, FoodTag.Meat);
            CorruptSeed seed = Seed(prefab, staging.transform);
            Check(!host.TryConsumeFood(seed) && !seed.IsDead && !host.TryGetComponent<CorruptSeedInfection>(out _),
                "incompatible meals leave food and host unchanged");
            Tags(host, FoodTag.Meat, FoodTag.Plant);
            int deathNotifications = 0;
            host.Died += dead =>
            {
                Check(dead == host && dead.IsDead && dead.Age == 0f && !dead.SimulationEnabled,
                    "death notification exposes committed host state");
                dead.Die(); // Reentrancy must not raise death again.
                throw new InvalidOperationException("Expected Corrupt Seed validation listener failure");
            };
            host.Died += dead => deathNotifications++;
            Feed(host, seed);
            CorruptSeedInfection infection = host.GetComponent<CorruptSeedInfection>();
            Check(infection != null && infection.OffspringPrefab == prefab && infection.AccumulatedSeedTags == FoodTag.Plant,
                "infection retains a prefab after the consumed seed is destroyed");
            Feed(host, Seed(prefab, staging.transform, FoodTag.Plant | FoodTag.Matter));
            Check(host.GetComponents<CorruptSeedInfection>().Length == 1 &&
                infection.AccumulatedSeedTags == (FoodTag.Plant | FoodTag.Matter), "repeat meals merge tags into one infection");
            Check(host.FoodTags == FoodTag.Meat && host.DietTags == FoodTag.Plant && host.Age == 40f && host.Heat == 0f,
                "infection does not alter host food, diet, lifespan, or heat");
            ExpectLog(LogType.Exception, "Expected Corrupt Seed validation listener failure", () => host.Die());
            List<CorruptSeed> brood = Brood(scene, host.transform.position, FoodTag.Plant | FoodTag.Meat | FoodTag.Matter);
            host.Die();
            Check(deathNotifications == 1 && host.DeathCount == 1 && Seeds(scene).Count == brood.Count,
                "exception isolation and repeated death preserve one notification and one brood");

            // Query actual registered food in this additive scene, not an active-scene-only object search.
            var targets = new List<Creature>();
            var positions = new List<Vector3>();
            Type registry = typeof(Creature).Assembly.GetType("CreatureRegistry");
            registry.GetMethod("SnapshotFood", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { scene, false, targets, positions });
            foreach (CorruptSeed child in brood) Check(targets.Contains(child), "offspring enter scene-local food registry");
            Clear(scene, staging);

            // Food flags accumulate through successive generations, including tags a host does not share.
            seed = Seed(prefab, staging.transform);
            FoodTag expected = FoodTag.Plant;
            foreach (FoodTag hostTags in new[] { FoodTag.Meat, FoodTag.Matter })
            {
                host = Host(scene, hostTags, expected);
                Feed(host, seed);
                host.Die();
                expected |= hostTags;
                brood = Brood(scene, host.transform.position, expected);
                seed = brood[0];
                for (int i = 1; i < brood.Count; i++) Object.DestroyImmediate(brood[i].gameObject);
                Object.DestroyImmediate(host.gameObject);
            }
            Check(seed.FoodTags == (FoodTag.Plant | FoodTag.Meat | FoodTag.Matter), "two generations retain all previous hosts' tags");
            Clear(scene, staging);

            foreach (FoodTag hostTags in new[] { FoodTag.None, FoodTag.Meat | FoodTag.Matter, FoodTag.Everything, (FoodTag)8 })
            {
                host = Host(scene, hostTags);
                Feed(host, Seed(prefab, staging.transform));
                host.Die();
                Brood(scene, host.transform.position, FoodTag.Plant | hostTags);
                Clear(scene, staging);
            }

            // Age expiry and being eaten both use the same shared death notification.
            host = Host(scene, FoodTag.Meat);
            Feed(host, Seed(prefab, staging.transform));
            using (var fields = new SerializedObject(host))
            {
                fields.FindProperty("ageRate").floatValue = 40f;
                fields.ApplyModifiedPropertiesWithoutUndo();
            }
            typeof(Creature).GetMethod("AdvanceAge", Private).Invoke(host, new object[] { 1f });
            Brood(scene, host.transform.position, FoodTag.Plant | FoodTag.Meat);
            Check(host.IsDead && host.DeathCount == 1, "lifespan expiry spawns one brood");
            Clear(scene, staging);

            host = Host(scene, FoodTag.Meat);
            Feed(host, Seed(prefab, staging.transform));
            var predator = Host(scene, FoodTag.Matter, FoodTag.Meat);
            Check(predator.TryConsumeFood(host), "predator can consume the infected host");
            brood = Brood(scene, host.transform.position, FoodTag.Plant | FoodTag.Meat);
            Check(!predator.TryGetComponent<CorruptSeedInfection>(out _) && !predator.TryConsumeFood(host),
                "host consumption creates seeds once without directly infecting predator");
            Clear(scene, staging);

            host = Host(scene, FoodTag.Meat);
            Feed(host, Seed(prefab, staging.transform));
            host.gameObject.SetActive(false);
            Check(Seeds(scene).Count == 0, "disabling a host is not death");
            host.Die();
            Brood(scene, host.transform.position, FoodTag.Plant | FoodTag.Meat);
            Clear(scene, staging);

            host = Host(scene, FoodTag.Meat);
            Feed(host, Seed(prefab, staging.transform));
            Object.DestroyImmediate(host.GetComponent<CorruptSeedInfection>());
            host.Die();
            Check(Seeds(scene).Count == 0, "removing infection unsubscribes and creates no brood");
            Clear(scene, staging);

            host = Host(scene, FoodTag.Meat);
            Feed(host, Seed(prefab, staging.transform));
            Object.DestroyImmediate(host.gameObject);
            Check(Seeds(scene).Count == 0, "destroying host without Die creates no brood");

            host = Host(scene, FoodTag.Meat);
            seed = Seed(prefab, staging.transform);
            using (var fields = new SerializedObject(seed))
            {
                fields.FindProperty("offspringDefinition").objectReferenceValue = null;
                fields.ApplyModifiedPropertiesWithoutUndo();
            }
            Feed(host, seed);
            int laterNotifications = 0;
            host.Died += dead => laterNotifications++;
            ExpectLog(LogType.Error, "Corrupt Seed infection needs an offspring prefab", () => host.Die());
            Check(host.IsDead && laterNotifications == 1 && Seeds(scene).Count == 0,
                "missing template reports an error without blocking death or later listeners");
            Clear(scene, staging);

            GameObject birdPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BirdFlockSetup.BirdPath);
            Check(birdPrefab != null, "bird integration fixture exists");
            var bird = Object.Instantiate(birdPrefab, Vector3.up * 2f, Quaternion.identity, staging.transform)
                .GetComponent<BirdCreature>();
            bird.transform.SetParent(null, true);
            bird.FlockManager.enabled = false;
            bird.FlightBody.linearVelocity = Vector3.zero;
            seed = Seed(prefab, staging.transform);
            seed.GetComponent<Rigidbody>().position = bird.FlightBody.position + Vector3.forward * 0.15f;
            Physics.SyncTransforms();
            scene.GetPhysicsScene().Simulate(0.02f);
            Check(seed.IsConsumptionPending && bird.TryGetComponent<CorruptSeedInfection>(out _),
                "real collider contact lets a bird consume and become infected");
            Object.DestroyImmediate(seed.gameObject);
            FoodTag birdTags = bird.FoodTags;
            Vector3 deathPosition = bird.transform.position;
            bird.Die();
            brood = Brood(scene, deathPosition, FoodTag.Plant | birdTags);
            int bonesCount = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.GetComponent<BirdBones>() != null) bonesCount++;
            Check(bonesCount == 0 && bird.IsDead && bird.gameObject.activeInHierarchy && bird.FlightBody.useGravity,
                "infected bird retains its original corpse alongside seeds");
            Clear(scene, staging);

            // Gravity, normal ground collision, and zero aging use the actual prefab components.
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.position = Vector3.down * 0.5f;
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            seed = Seed(prefab, staging.transform);
            Physics.SyncTransforms();
            PhysicsScene physics = scene.GetPhysicsScene();
            for (int i = 0; i < 250; i++) physics.Simulate(0.02f);
            Rigidbody seedBody = seed.GetComponent<Rigidbody>();
            Check(seedBody.position.y > 0f && seedBody.position.y < 0.2f && seedBody.linearVelocity.sqrMagnitude < 0.1f,
                "seed falls and settles on ordinary ground");
            typeof(Creature).GetMethod("AdvanceAge", Private).Invoke(seed, new object[] { 10000f });
            Check(!seed.IsDead && seed.Age == 40f, "zero aging leaves uneaten seeds indefinitely");

            string report = "Corrupt Seed passed: " + checks + " assertions. Two intentional failure logs checked.";
            Directory.CreateDirectory("Temp/CorruptSeedValidation");
            File.WriteAllText("Temp/CorruptSeedValidation/results.txt", report);
            Debug.Log(report);
            return report;
        }
        finally
        {
            Random.state = previousRandom;
            Clear(scene, staging);
            Object.DestroyImmediate(staging);
            SceneManager.UnloadSceneAsync(scene);
        }
    }
}
