using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit Age-hunting checks in an isolated physics scene; does not enter/exit Play Mode.</summary>
public static class SkySpiderAgeValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TimeSpider Age: " + message);
        checks++;
    }

    [MenuItem("Tools/Creatures/Validate TimeSpider Age Hunting (Play Mode)")]
    public static string Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        checks = 0;
        Scene scene = SceneManager.CreateScene("TimeSpider Age " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var staging = new GameObject("Inactive Age Validation Staging");
        staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, scene);
        var setAge = (Action<Creature, float>)Delegate.CreateDelegate(typeof(Action<Creature, float>),
            typeof(Creature).GetMethod("SetRemainingAge", Private));
        Vector3 origin = Vector3.up * 1000f;
        var preyObjects = new List<GameObject>();

        CreatureLifecycleValidationProbe Prey(Vector3 position, float age = 40f)
        {
            var root = new GameObject("Age Prey");
            root.transform.SetParent(staging.transform, false);
            root.transform.position = position;
            root.AddComponent<Rigidbody>().useGravity = false;
            root.AddComponent<BoxCollider>();
            var prey = root.AddComponent<CreatureLifecycleValidationProbe>();
            root.transform.SetParent(null, true);
            setAge(prey, age);
            preyObjects.Add(root);
            return prey;
        }
        WebCapture Trap(SkyWeb web, Creature prey)
        {
            Rigidbody body = prey.GetComponentInChildren<Rigidbody>();
            Check(web.TryCapture(body, body.position), "fixture captured by its web");
            return body.GetComponent<WebCapture>();
        }
        void ClearPrey()
        {
            foreach (GameObject root in preyObjects) if (root != null) Object.DestroyImmediate(root);
            preyObjects.Clear();
        }
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SkySpiderSetup.SpiderPath);
            Check(prefab != null, "existing spider prefab exists");
            var root = Object.Instantiate(prefab, origin, Quaternion.identity, staging.transform);
            root.transform.SetParent(null, true);
            SkySpider spider = root.GetComponent<SkySpider>();
            SkyWeb web = spider.CurrentWeb;
            Rigidbody spiderBody = spider.GetComponent<Rigidbody>();
            var step = (Action)Delegate.CreateDelegate(typeof(Action), spider, typeof(SkySpider).GetMethod("FixedUpdate", Private));
            void Tick() => typeof(SkySpider).GetMethod("TickCreature", Private).Invoke(spider, new object[] { 0.02f });
            Check(web.gameObject.scene == scene && spider.FoodTags == Creature.FoodTag.Meat &&
                spider.DietTags == Creature.FoodTag.Everything && spider.AgeRate == prefab.GetComponent<SkySpider>().AgeRate,
                "scene-local web, intended tags, unchanged authored Age Rate");
            Check(web.Captures is System.Collections.ObjectModel.ReadOnlyCollection<WebCapture>, "capture view cannot mutate web membership");

            var far = Prey(origin + Vector3.right * 5f);
            var near = Prey(origin + Vector3.right * 3f, 12.5f);
            Trap(web, far);
            WebCapture nearCapture = Trap(web, near);
            typeof(SkySpider).GetField("pauseTime", Private).SetValue(spider, 5f);
            float distanceBeforePursuit = (near.transform.position - spiderBody.position).sqrMagnitude;
            step();
            scene.GetPhysicsScene().Simulate(0.02f);
            Check(spider.HuntingTarget == near && !near.SimulationEnabled && near.AgeRate == 0f, "nearest captured prey interrupts pause despite paused ageing");
            Check((near.transform.position - spiderBody.position).sqrMagnitude < distanceBeforePursuit,
                "pursuit actually moves toward the captured target");
            var closer = Prey(origin + Vector3.right * 1.5f);
            Trap(web, closer);
            step();
            Check(spider.HuntingTarget == near, "new closer prey does not replace a valid target");
            // Hold speed at zero so repeated manual physics callbacks cannot reach/eat the fixture.
            FieldInfo speedField = typeof(SkySpider).GetField("moveSpeed", Private);
            float originalSpeed = (float)speedField.GetValue(spider);
            speedField.SetValue(spider, 0f);
            for (int i = 0; i < 32; i++) step();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 500; i++) step();
            long pursuitAllocations = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            speedField.SetValue(spider, originalSpeed);
            Check(pursuitAllocations == 0, "warmed pursuit has no managed allocations");
            float originalAge = spider.Age;
            Check(!spider.TryDrainCapturedAge(nearCapture) && spider.Age == originalAge, "out-of-range entry point cannot steal Age");
            spiderBody.position = near.transform.position + Vector3.left;
            Physics.SyncTransforms();
            bool callbackSawTransfer = false;
            near.Died += _ => callbackSawTransfer = spider.Age == originalAge + 12.5f && !nearCapture.IsCaptured;
            Check(spider.TryDrainCapturedAge(nearCapture), "nearby Age drain succeeds");
            Check(near.IsDead && near.Age == 0f && near.AgeDeathCount == 1 && near.DeathCount == 1 &&
                near.ConsumptionCount == 0 && near.gameObject.activeSelf && callbackSawTransfer,
                "fractional Age transfers before death callbacks, releases silk, runs age death, and retains ordinary corpse");
            Check(!spider.TryDrainCapturedAge(nearCapture) && spider.Age == originalAge + 12.5f,
                "repeated attempts cannot duplicate transfer");
            ClearPrey();
            spiderBody.position = origin;

            // Surface range, including an offset compound collider rather than the body's origin.
            var compound = Prey(origin + Vector3.right * 10f);
            compound.GetComponent<BoxCollider>().enabled = false;
            var solid = new GameObject("Offset Solid");
            solid.transform.SetParent(compound.transform, false);
            solid.transform.localPosition = Vector3.left * 8f;
            solid.AddComponent<BoxCollider>();
            WebCapture compoundCapture = Trap(web, compound);
            spiderBody.position = origin + Vector3.right * (1.5f - spider.BiteRange - 0.01f);
            Physics.SyncTransforms();
            Check(!spider.TryDrainCapturedAge(compoundCapture), "outside collider-surface bite boundary is rejected");
            spiderBody.position = origin + Vector3.right * (1.5f - spider.BiteRange);
            Physics.SyncTransforms();
            Check(spider.TryDrainCapturedAge(compoundCapture), "exact surface boundary works for a distant body origin");
            ClearPrey();
            spiderBody.position = origin;

            var interrupted = Prey(origin + Vector3.right * 3f);
            WebCapture interruptedCapture = Trap(web, interrupted);
            step();
            Check(spider.HuntingTarget == interrupted, "hunt begins before release");
            interruptedCapture.Release();
            Check(spider.HuntingTarget == null && !spider.TryDrainCapturedAge(interruptedCapture), "release immediately invalidates target and feeding");
            step();
            Check(!interrupted.IsDead, "released prey survives interrupted pursuit");
            ClearPrey();

            var pausedPrey = Prey(origin + Vector3.right);
            WebCapture pausedCapture = Trap(web, pausedPrey);
            spider.SetSimulationEnabled(false);
            step();
            Check(spider.HuntingTarget == null && !spider.TryDrainCapturedAge(pausedCapture), "paused spider cannot hunt or bite");
            spider.SetSimulationEnabled(true);
            spider.enabled = false;
            Check(!spider.TryDrainCapturedAge(pausedCapture), "disabled spider cannot bite");
            spider.enabled = true;
            pausedPrey.enabled = false;
            Check(!spider.TryDrainCapturedAge(pausedCapture), "disabled prey cannot be drained");
            pausedPrey.enabled = true;
            ClearPrey();

            // Carry release must finish returning before the web's prey can be eaten.
            var carrierRoot = new GameObject("Validation Carrier");
            carrierRoot.SetActive(false);
            SceneManager.MoveGameObjectToScene(carrierRoot, scene);
            var anchor = new GameObject("Anchor").transform;
            anchor.SetParent(carrierRoot.transform, false);
            var carrier = carrierRoot.AddComponent<PlayerCreatureCarrier>();
            carrier.enabled = false;
            typeof(PlayerCreatureCarrier).GetField("carryPoint", Private).SetValue(carrier, anchor);
            carrierRoot.SetActive(true);
            pausedPrey = Prey(origin + Vector3.right * 2f);
            pausedCapture = Trap(web, pausedPrey);
            Check(spider.TryBeginCarry(carrier, anchor), "spider can still be carried");
            Tick();
            Check(spider.HuntingTarget == null && !spider.TryDrainCapturedAge(pausedCapture), "carrying blocks hunt and attack");
            spiderBody.position = origin + Vector3.right * 2f;
            spider.EndCarry(carrier);
            Tick();
            Check(spider.IsReturning && !spider.TryDrainCapturedAge(pausedCapture), "return takes priority even next to prey");
            for (int i = 0; i < 160 && spider.IsReturning; i++) { step(); scene.GetPhysicsScene().Simulate(0.02f); }
            Check(!spider.IsReturning && spider.CurrentWeb == web, "existing return movement completes");
            ClearPrey();
            Object.DestroyImmediate(carrierRoot);
            spiderBody.position = origin;

            // Foreign web captures and non-creature bodies are never meals.
            SkyWeb foreign = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SkySpiderSetup.WebPath), staging.transform).GetComponent<SkyWeb>();
            foreign.transform.SetParent(null, true);
            var foreignPrey = Prey(origin + Vector3.right);
            WebCapture foreignCapture = Trap(foreign, foreignPrey);
            Check(!spider.TryDrainCapturedAge(foreignCapture), "another web's prey cannot be drained");
            var plainRoot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(plainRoot, scene);
            plainRoot.transform.position = origin + Vector3.right;
            Rigidbody plainBody = plainRoot.AddComponent<Rigidbody>(); plainBody.useGravity = false;
            Check(web.TryCapture(plainBody, plainBody.position), "plain body is still trappable");
            step();
            Check(spider.HuntingTarget == null && !spider.TryDrainCapturedAge(plainRoot.GetComponent<WebCapture>()), "bodies without Age are ignored");
            Object.DestroyImmediate(plainRoot);
            Object.DestroyImmediate(foreign.gameObject);
            ClearPrey();

            // Pending ordinary meals cannot also supply stolen Age.
            var food = Prey(origin + Vector3.right);
            var eater = Prey(origin + Vector3.left);
            using (var fields = new SerializedObject(eater))
            {
                fields.FindProperty("dietTags").intValue = (int)Creature.FoodTag.Everything;
                fields.FindProperty("canEatSameType").boolValue = true;
                fields.ApplyModifiedPropertiesWithoutUndo();
            }
            WebCapture pendingCapture = Trap(web, food);
            float beforePending = spider.Age;
            Check(eater.TryConsumeFood(food) && !spider.TryDrainCapturedAge(pendingCapture) && spider.Age == beforePending,
                "ordinary consumption cannot award a second Age meal");
            ClearPrey();

            var birdPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BirdFlockSetup.BirdPath);
            Check(birdPrefab != null, "bird fixture exists");
            var bird = Object.Instantiate(birdPrefab, origin + Vector3.right * 0.5f, Quaternion.identity, staging.transform).GetComponent<BirdCreature>();
            bird.transform.SetParent(null, true);
            bird.FlockManager.enabled = false;
            WebCapture birdCapture = Trap(web, bird);
            float beforeBird = spider.Age;
            float birdAge = bird.Age;
            Physics.SyncTransforms();
            Check(spider.TryDrainCapturedAge(birdCapture) && spider.Age == beforeBird + birdAge && bird.IsDead && !bird.gameObject.activeSelf,
                "trapped bird supplies all Age and uses its age-only transformation");
            int boneCount = 0;
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                if (sceneRoot.TryGetComponent<BirdBones>(out var bones))
                {
                    boneCount++;
                    Check(bones.IsDead && !bones.SimulationEnabled && bones.GetComponent<Rigidbody>().useGravity,
                        "age-drained bird bones have passive dead physics");
                }
            Check(boneCount == 1 && web.CaptureCount == 0, "bird creates exactly one bones object and leaves web captures");

            var seedPrefab = AssetDatabase.LoadAssetAtPath<CorruptSeed>("Assets/Prefabs/Creatures/CorruptSeed.prefab");
            Check(seedPrefab != null && seedPrefab.OffspringPrefab != null, "corrupt seed fixture has an offspring template");
            CorruptSeed SpawnSeed()
            {
                var seed = Object.Instantiate(seedPrefab, origin + Vector3.right * 0.5f, Quaternion.identity, staging.transform);
                seed.transform.SetParent(null, true);
                return seed;
            }
            var seedVictim = SpawnSeed();
            WebCapture seedCapture = Trap(web, seedVictim);
            Physics.SyncTransforms();
            Check(spider.TryDrainCapturedAge(seedCapture) && !spider.TryGetComponent<CorruptSeedInfection>(out _),
                "draining a Corrupt Seed skips its ordinary infection effect");
            var infected = Prey(origin + Vector3.right);
            var infectionMeal = SpawnSeed();
            infectionMeal.Consume(infected);
            Object.DestroyImmediate(infectionMeal.gameObject);
            Check(infected.TryGetComponent<CorruptSeedInfection>(out _), "victim starts infected");
            WebCapture infectedCapture = Trap(web, infected);
            Physics.SyncTransforms();
            Check(spider.TryDrainCapturedAge(infectedCapture) && infected.ConsumptionCount == 0 && infected.AgeDeathCount == 1,
                "infected victim dies of Age without Consume");
            int offspring = 0;
            foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                if (sceneRoot.TryGetComponent<CorruptSeed>(out var seed) && !seed.IsDead) offspring++;
            Check(offspring >= 3 && offspring <= 5 && !spider.TryGetComponent<CorruptSeedInfection>(out _),
                "existing victim infection still produces its brood without infecting spider");

            var last = Prey(origin + Vector3.right);
            WebCapture lastCapture = Trap(web, last);
            web.Die();
            Tick();
            Check(!lastCapture.IsCaptured && spider.CurrentWeb != web && !spider.TryDrainCapturedAge(lastCapture),
                "dead web releases prey and cannot be selected again");
            spider.Die();
            Check(spider.HuntingTarget == null && !spider.TryDrainCapturedAge(lastCapture), "dead spider cannot hunt or drain");
            float deadAge = last.Age;
            last.Die(); setAge(last, 100f);
            Check(last.IsDead && last.Age == 0f && deadAge > 0f, "controlled Age mutation cannot revive the dead");

            string report = "PASS: " + checks + " TimeSpider Age hunting checks; warmed pursuit allocated " + pursuitAllocations + " bytes / 500 steps.";
            Debug.Log(report);
            return report;
        }
        finally { SceneManager.UnloadSceneAsync(scene); }
    }
}
