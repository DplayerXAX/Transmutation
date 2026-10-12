using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>Explicit checks in a temporary local physics scene; never changes authored scene assets.</summary>
public static class BirdFlockValidation
{
    public static string Status { get; private set; } = "Not run";
    private static int checks;
    private static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Bird validation: " + message);
        checks++;
    }

    [MenuItem("Tools/Creatures/Validate Bird Octree")]
    private static void ValidateOctreeMenu() => ValidateOctree();

    public static string ValidateOctree()
    {
        checks = 0;
        var random = new System.Random(704);
        var points = new Vector3[200];
        var tree = new BirdOctree();
        var found = new List<int>(200);
        var expected = new HashSet<int>();
        foreach (int count in new[] { 0, 1, 8, 9, 72, 200 })
        {
            for (int i = 0; i < count; i++)
                points[i] = new Vector3((float)random.NextDouble() * 100f - 50f,
                    (float)random.NextDouble() * 40f - 20f, (float)random.NextDouble() * 100f - 50f);
            if (count > 8)
            {
                for (int i = 0; i < 9; i++) points[i] = Vector3.zero;
                points[count - 1] = Vector3.one * 1000f;
            }
            tree.Rebuild(points, count);
            for (int query = 0; query < 120; query++)
            {
                Vector3 center = query < count ? points[query] : new Vector3(query - 40, 0f, 0f);
                float radius = query % 4 == 0 ? 0f : query % 4 == 1 ? 9f : query % 4 == 2 ? 100f : 2000f;
                tree.Query(center, radius, found);
                expected.Clear();
                for (int i = 0; i < count; i++)
                    if ((points[i] - center).sqrMagnitude <= radius * radius) expected.Add(i);
                Check(found.Count == expected.Count && expected.SetEquals(found), "exact query equals exhaustive search");
                Check(new HashSet<int>(found).Count == found.Count, "no duplicate point indices");
            }
        }
        for (int i = 0; i < points.Length; i++) points[i] = Vector3.zero;
        tree.Rebuild(points, points.Length);
        tree.Query(Vector3.zero, 0f, found);
        Check(found.Count == points.Length && tree.NodeCount == 1 + BirdOctree.MaximumDepth * 8, "coincident points stop subdividing");
        points[0] = Vector3.right * 9f;
        points[1] = Vector3.left * 9f;
        points[2] = Vector3.up * 9.01f;
        tree.Rebuild(points, 3);
        tree.Query(Vector3.zero, 9f, found);
        Check(found.Count == 2 && found.Contains(0) && found.Contains(1), "inclusive radius boundary");
        tree.Query(Vector3.zero, 9f, found);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { tree.Rebuild(points, 3); tree.Query(Vector3.zero, 9f, found); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated == 0, "warmed rebuild and query allocate no managed memory");
        Status = "Octree passed: " + checks + " assertions; warmed allocation = " + allocated + " bytes.\n" + BenchmarkSearch();
        WriteReport("octree.txt", Status);
        Debug.Log(Status);
        return Status;
    }

    private static string BenchmarkSearch()
    {
        var reports = new List<string>();
        var tree = new BirdOctree();
        var neighbors = new List<int>(200);
        foreach (int count in new[] { 72, 200 })
        {
            var random = new System.Random(1234);
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 center = i % 3 == 0 ? new Vector3(-24f, 17f, -20f)
                    : i % 3 == 1 ? new Vector3(0f, 21f, 5f) : new Vector3(26f, 19f, -24f);
                points[i] = center + new Vector3((float)random.NextDouble() * 8f - 4f,
                    (float)random.NextDouble() * 8f - 4f, (float)random.NextDouble() * 8f - 4f);
            }
            tree.Rebuild(points, count);
            for (int i = 0; i < count; i++) tree.Query(points[i], 9f, neighbors);
            int indexedMatches = 0, exhaustiveMatches = 0;
            var watch = Stopwatch.StartNew();
            for (int pass = 0; pass < 200; pass++)
            {
                tree.Rebuild(points, count);
                for (int i = 0; i < count; i++)
                {
                    tree.Query(points[i], 9f, neighbors);
                    indexedMatches += neighbors.Count;
                }
            }
            watch.Stop();
            double indexedMs = watch.Elapsed.TotalMilliseconds / 200;
            watch.Restart();
            for (int pass = 0; pass < 200; pass++)
                for (int i = 0; i < count; i++)
                {
                    neighbors.Clear();
                    for (int j = 0; j < count; j++)
                        if ((points[i] - points[j]).sqrMagnitude <= 81f) neighbors.Add(j);
                    exhaustiveMatches += neighbors.Count;
                }
            watch.Stop();
            Check(indexedMatches == exhaustiveMatches, "benchmark searches return identical neighbors");
            reports.Add(count + " birds, initial clusters: octree rebuild + searches " + indexedMs.ToString("F4")
                + " ms; exhaustive searches " + (watch.Elapsed.TotalMilliseconds / 200).ToString("F4") + " ms.");
        }
        return string.Join("\n", reports);
    }

    [MenuItem("Tools/Creatures/Validate Bird Runtime (Play Mode)")]
    private static void ValidateRuntimeMenu() => ValidateRuntime();

    public static string ValidateRuntime()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before validating bird flight.");
        checks = 0;
        var reports = new List<string>();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BirdFlockSetup.BirdPath);
        Check(prefab != null, "bird prefab exists");
        Scene scene = SceneManager.CreateScene("Bird Validation " + Guid.NewGuid(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        PhysicsScene physics = scene.GetPhysicsScene();
        GameObject staging = new GameObject("Bird Validation Staging");
        staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, scene);
        var birds = new List<BirdCreature>();
        try
        {
            BirdCreature first = Spawn(prefab, staging.transform, new Vector3(0f, 20f, 0f));
            BirdFlockManager manager = first.FlockManager;
            Check(manager != null && manager.gameObject.scene == scene, "standalone bird creates scene-local manager");
            manager.enabled = false;
            Check(BirdFlockManager.GetOrCreate(scene, Vector3.zero) == manager, "one automatic manager");
            Check(manager.SkyBounds.center == first.FlightBody.position, "fallback volume centers on first bird");
            birds.Add(first);
            BirdCreature second = Spawn(prefab, staging.transform, new Vector3(2f, 20f, 0f));
            birds.Add(second);
            Check(second.FlockManager == manager && manager.RegisteredBirdCount == 2, "independent spawns share manager");

            GameObject areaObject = new GameObject("Test Bird Spawn Area");
            SceneManager.MoveGameObjectToScene(areaObject, scene);
            areaObject.transform.position = Vector3.up * 20f;
            BirdSpawnArea area = areaObject.AddComponent<BirdSpawnArea>();
            area.Configure(prefab.GetComponent<BirdCreature>(), 3, 3f, 55);
            area.SetFlightSpeedOverride(true, new Vector2(1f, 2f));
            area.SpawnOnce();
            area.SpawnOnce();
            Check(manager.RegisteredBirdCount == 5 && area.GetComponent<Creature>() == null,
                "spawn tool creates birds once and is not a creature");
            Object.DestroyImmediate(areaObject);
            Check(manager.RegisteredBirdCount == 5, "deleting spawn tool leaves independent birds");
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                BirdCreature spawned = root.GetComponent<BirdCreature>();
                if (spawned != null && spawned != first && spawned != second)
                {
                    Check(spawned.FlightSpeedRange == new Vector2(1f, 2f), "spawn area speed overrides only its bird instances");
                    Check(Mathf.Abs(spawned.FlightBody.linearVelocity.magnitude - 1.5f) < 0.001f,
                        "area speed is applied before initial flight velocity");
                    Object.DestroyImmediate(root);
                }
            }
            Check(first.FlightSpeedRange == prefab.GetComponent<BirdCreature>().FlightSpeedRange
                && second.FlightSpeedRange == first.FlightSpeedRange, "other birds and prefab retain their speed");
            areaObject = new GameObject("Inherited Speed Spawn Area");
            SceneManager.MoveGameObjectToScene(areaObject, scene);
            area = areaObject.AddComponent<BirdSpawnArea>();
            area.Configure(prefab.GetComponent<BirdCreature>(), 1, 0f, 56);
            area.SetFlightSpeedOverride(false, new Vector2(20f, 25f));
            area.SpawnOnce();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                BirdCreature spawned = root.GetComponent<BirdCreature>();
                if (spawned == null || spawned == first || spawned == second) continue;
                Check(spawned.FlightSpeedRange == first.FlightSpeedRange, "disabled speed override inherits prefab settings");
                Object.DestroyImmediate(root);
            }
            Object.DestroyImmediate(areaObject);

            Object.DestroyImmediate(manager.gameObject);
            typeof(BirdCreature).GetMethod("TickCreature", PrivateInstance).Invoke(first, new object[] { 0.02f });
            typeof(BirdCreature).GetMethod("TickCreature", PrivateInstance).Invoke(second, new object[] { 0.02f });
            manager = first.FlockManager;
            manager.enabled = false;
            Check(manager != null && second.FlockManager == manager && manager.RegisteredBirdCount == 2,
                "destroyed manager is replaced once without duplicate bird membership");

            manager.SimulateStep(0.02f, 0f);
            Check(manager.FlyingBirdCount == 2 && manager.CandidateChecks == 4,
                "nearby independent birds participate in the same neighbor queries");
            Vector3 beforeVelocity = first.FlightBody.linearVelocity;
            first.SetSimulationEnabled(false);
            manager.SimulateStep(0.02f, 0f);
            Check(manager.FlyingBirdCount == 1 && first.FlightBody.linearVelocity == beforeVelocity, "paused bird receives no flight steering");
            first.SetSimulationEnabled(true);
            first.enabled = false;
            Check(manager.RegisteredBirdCount == 1, "disable unregisters");
            first.enabled = true;
            Check(manager.RegisteredBirdCount == 2, "enable registers once");

            // Exercise the real carrier entry point, including base OnDisable releasing ownership.
            GameObject carrierObject = new GameObject("Bird Test Carrier");
            carrierObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(carrierObject, scene);
            GameObject anchor = new GameObject("Carry Point");
            SceneManager.MoveGameObjectToScene(anchor, scene);
            anchor.transform.position = first.FlightBody.position;
            PlayerCreatureCarrier carrier = carrierObject.AddComponent<PlayerCreatureCarrier>();
            carrier.enabled = false;
            typeof(PlayerCreatureCarrier).GetField("carryPoint", PrivateInstance).SetValue(carrier, anchor.transform);
            carrierObject.SetActive(true);
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", PrivateInstance).Invoke(carrier, new object[] { first });
            Check(first.IsCarried && carrier.CarriedCreature == first, "real carrier picks up bird");
            beforeVelocity = first.FlightBody.linearVelocity;
            Quaternion beforeRotation = first.FlightBody.rotation;
            manager.SimulateStep(0.02f, 1f);
            Check(manager.FlyingBirdCount == 1 && first.FlightBody.linearVelocity == beforeVelocity
                && first.FlightBody.rotation == beforeRotation, "carry excludes both velocity and rotation steering");
            first.enabled = false;
            Check(!first.IsCarried && carrier.CarriedCreature == null, "disabling a held bird releases carrier");
            first.enabled = true;
            typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", PrivateInstance).Invoke(carrier, new object[] { first });
            carrier.Release();
            first.FlightBody.position = new Vector3(80f, 0f, 0f);
            first.FlightBody.linearVelocity = Vector3.zero;
            Vector3 releasePosition = first.FlightBody.position;
            manager.SimulateStep(0.02f, 2f);
            Check(first.FlightBody.position == releasePosition && first.FlightBody.linearVelocity.sqrMagnitude > 0f,
                "stationary release resumes flight without teleporting");
            for (int i = 0; i < 1200; i++)
            {
                manager.SimulateStep(0.02f, i * 0.02f);
                physics.Simulate(0.02f);
            }
            Check(manager.SkyBounds.Contains(first.FlightBody.position), "released bird returns to sky volume");

            // Scene transfer must register with the destination, never query birds in another scene.
            Scene destination = SceneManager.CreateScene("Bird Transfer " + Guid.NewGuid());
            try
            {
                SceneManager.MoveGameObjectToScene(second.gameObject, destination);
                typeof(BirdCreature).GetMethod("TickCreature", PrivateInstance).Invoke(second, new object[] { 0.02f });
                Check(second.FlockManager != manager && second.FlockManager.gameObject.scene == destination,
                    "scene transfer changes manager");
                Check(manager.RegisteredBirdCount == 1, "scene transfer removes old membership");
            }
            finally { SceneManager.UnloadSceneAsync(destination); }
            birds.Remove(second);

            // Remove the first bird; all profiling populations are independent deterministic clusters.
            Object.DestroyImmediate(first.gameObject);
            birds.Clear();
            Check(manager.RegisteredBirdCount == 0, "destroy unregisters");
            foreach (int count in new[] { 72, 200 })
            {
                var random = new System.Random(1234);
                for (int i = 0; i < count; i++)
                {
                    Vector3 cluster = i % 3 == 0 ? new Vector3(-24f, 17f, -20f)
                        : i % 3 == 1 ? new Vector3(0f, 21f, 5f) : new Vector3(26f, 19f, -24f);
                    Vector3 offset = new Vector3((float)random.NextDouble() * 8f - 4f,
                        (float)random.NextDouble() * 8f - 4f, (float)random.NextDouble() * 8f - 4f);
                    BirdCreature bird = Spawn(prefab, staging.transform, cluster + offset);
                    bird.SetFlightSeed(i + 1234);
                    birds.Add(bird);
                }
                manager.SkyBounds = new Bounds(new Vector3(0f, 20f, -15f), new Vector3(100f, 24f, 100f));
                manager.SimulateStep(0.02f, 0f);
                Check(manager.FlyingBirdCount == count && manager.CandidateChecks < count * count, "spatial pruning for " + count + " birds");
                Vector3 startingPosition = birds[0].FlightBody.position;
                for (int i = 0; i < 400; i++) { manager.SimulateStep(0.02f, i * 0.02f); physics.Simulate(0.02f); }
                Check(Vector3.Distance(startingPosition, birds[0].FlightBody.position) > 1f, "visible flight motion");
                long allocated = 0;
                long ticks = 0;
                long candidateChecks = 0;
                var watch = new Stopwatch();
                for (int i = 0; i < 500; i++)
                {
                    long memoryBefore = GC.GetAllocatedBytesForCurrentThread();
                    watch.Restart();
                    manager.SimulateStep(0.02f, 8f + i * 0.02f);
                    watch.Stop();
                    allocated += GC.GetAllocatedBytesForCurrentThread() - memoryBefore;
                    ticks += watch.ElapsedTicks;
                    candidateChecks += manager.CandidateChecks;
                    physics.Simulate(0.02f);
                }
                manager.SimulateStep(0.02f, 18f);
                foreach (BirdCreature bird in birds)
                {
                    Vector3 p = bird.FlightBody.position;
                    Check(!float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z), "finite flight state");
                    Check(bird.FlightBody.linearVelocity.magnitude <= bird.FlightSpeedRange.y + 0.01f, "bounded flight speed");
                }
                reports.Add(count + " birds: " + (ticks * 1000.0 / Stopwatch.Frequency / 500).ToString("F4")
                    + " ms/step, " + allocated + " allocated bytes over 500 steps, "
                    + (candidateChecks / 500) + "/" + (count * count) + " average candidate distance checks.");
                Check(allocated == 0, "warmed simulation has no managed allocations");
                foreach (BirdCreature bird in birds) Object.DestroyImmediate(bird.gameObject);
                birds.Clear();
            }
            Status = "Runtime passed: " + checks + " assertions.\n" + string.Join("\n", reports);
            WriteReport("runtime.txt", Status);
            Debug.Log(Status);
            return Status;
        }
        catch (Exception exception)
        {
            Status = "Runtime failed: " + exception.Message;
            WriteReport("runtime.txt", Status);
            throw;
        }
        finally { SceneManager.UnloadSceneAsync(scene); }
    }

    private static BirdCreature Spawn(GameObject prefab, Transform inactiveParent, Vector3 position)
    {
        GameObject instance = Object.Instantiate(prefab, position, Quaternion.identity, inactiveParent);
        instance.transform.SetParent(null, true);
        return instance.GetComponent<BirdCreature>();
    }

    [MenuItem("Tools/Creatures/Validate Bird Web Capture (Play Mode)")]
    private static void ValidateWebCaptureMenu() => ValidateWebCapture();

    public static string ValidateWebCapture()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before validating bird web capture.");
        checks = 0;
        GameObject birdPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BirdFlockSetup.BirdPath);
        GameObject webPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SkySpiderSetup.WebPath);
        Check(birdPrefab != null && webPrefab != null, "bird and Sky Spider web prefabs exist");
        // The actual web scan queries the default physics world; place this temporary scene far from the play space.
        Scene scene = SceneManager.CreateScene("Bird Web Validation " + Guid.NewGuid());
        GameObject staging = new GameObject("Bird Web Validation Staging");
        staging.SetActive(false);
        SceneManager.MoveGameObjectToScene(staging, scene);
        try
        {
            GameObject webObject = Object.Instantiate(webPrefab, new Vector3(1000f, 20f, 1000f), Quaternion.identity, staging.transform);
            webObject.transform.SetParent(null, true);
            SkyWeb web = webObject.GetComponent<SkyWeb>();
            web.InitializeGeometry();
            web.AdvanceGrowth(1000f);
            SkyWebGeometry.Strand strand = web.Geometry.GetStrand(0);
            Vector3 contact = web.transform.position + (strand.Start + strand.End) * 0.5f;
            Action scan = (Action)Delegate.CreateDelegate(typeof(Action), web,
                typeof(SkyWeb).GetMethod("ScanForCaptures", PrivateInstance));

            GameObject carrierObject = new GameObject("Bird Web Test Carrier");
            carrierObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(carrierObject, scene);
            GameObject anchorObject = new GameObject("Bird Carry Anchor");
            SceneManager.MoveGameObjectToScene(anchorObject, scene);
            anchorObject.transform.position = contact;
            PlayerCreatureCarrier carrier = carrierObject.AddComponent<PlayerCreatureCarrier>();
            carrier.enabled = false;
            typeof(PlayerCreatureCarrier).GetField("carryPoint", PrivateInstance).SetValue(carrier, anchorObject.transform);
            carrierObject.SetActive(true);
            foreach (bool firmGrip in new[] { false, true })
            {
                BirdCreature bird = Spawn(birdPrefab, staging.transform, contact);
                BirdFlockManager manager = bird.FlockManager;
                manager.enabled = false;
                Check(bird.IsFlying && web.IsEligible(bird.FlightBody), "flying creature is eligible web prey");
                Physics.SyncTransforms();
                scan();
                WebCapture capture = bird.GetComponent<WebCapture>();
                Check(bird.IsWebCaptured && capture.OwningWeb == web && web.CaptureCount == 1,
                    "normal silk contact scan catches a flying bird");
                Check(!bird.SimulationEnabled && !bird.IsFlying, "web capture stops autonomous flock movement");
                Vector3 position = bird.FlightBody.position;
                Quaternion rotation = bird.FlightBody.rotation;
                bird.SetSimulationEnabled(true);
                manager.SimulateStep(0.02f, 0f);
                Check(manager.FlyingBirdCount == 0 && bird.FlightBody.linearVelocity == Vector3.zero
                    && bird.FlightBody.position == position && bird.FlightBody.rotation == rotation,
                    "captured-state guard prevents flocking from fighting the silk joint even if simulation is reenabled");
                bird.SetSimulationEnabled(false);
                typeof(PlayerCreatureCarrier).GetField("firmGrip", PrivateInstance).SetValue(carrier, firmGrip);
                typeof(PlayerCreatureCarrier).GetMethod("BeginCarry", PrivateInstance).Invoke(carrier, new object[] { bird });
                Check(bird.IsCarried && carrier.CarriedCreature == bird && !bird.IsWebCaptured
                    && bird.SimulationEnabled && web.CaptureCount == 0, "pickup frees bird and transfers movement to carrier");
                Check(!web.IsEligible(bird.FlightBody) && !capture.CanCapture, "held bird cannot be recaptured; cooldown is retained");
                manager.SimulateStep(0.02f, 1f);
                Check(manager.FlyingBirdCount == 0, "spring and firm grips both exclude carried birds from flocking");
                carrier.Release();
                Check(bird.IsFlying && !bird.FlightBody.isKinematic && !bird.FlightBody.useGravity,
                    "release restores dynamic flying creature physics");
                manager.SimulateStep(0.02f, 2f);
                Check(manager.FlyingBirdCount == 1 && bird.FlightBody.linearVelocity.magnitude >= bird.FlightSpeedRange.x,
                    "released prey resumes flock flight");
                Object.DestroyImmediate(bird.gameObject);
            }
            Status = "Bird web capture passed: " + checks + " assertions, including spring and firm carrying.";
            WriteReport("web-capture.txt", Status);
            Debug.Log(Status);
            return Status;
        }
        finally { SceneManager.UnloadSceneAsync(scene); }
    }

    private static void WriteReport(string name, string text)
    {
        Directory.CreateDirectory("Temp/BirdFlockValidation");
        File.WriteAllText("Temp/BirdFlockValidation/" + name, text);
    }
}
