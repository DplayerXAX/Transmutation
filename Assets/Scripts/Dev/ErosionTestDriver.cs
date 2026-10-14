using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

/// <summary>Opt-in runtime acceptance runner. Also runs in a standalone player with -erosionTest.</summary>
public sealed class ErosionTestDriver : MonoBehaviour
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly StringBuilder report = new StringBuilder();
    private readonly List<float> frameTimes = new List<float>();
    private ProceduralWorld world;
    private ErosiveFluidSystem fluid;
    private ErosiveFluidSource source;
    private int passed, failed;
    private string output;
    private double started;
    private bool collecting, ended;
    private float lastFrame;
    private Camera verificationView;
    private RenderTexture verificationTexture;
    private UniversalRenderPipeline.SingleCameraRequest renderRequest;
    private ProfilerRecorder physicsRecorder;
    private readonly List<float> physicsTimes = new List<float>();
    private readonly List<float> uploadTimes = new List<float>();
    private readonly List<float> bakeTimes = new List<float>();
    private readonly List<float> commitTimes = new List<float>();
    private readonly List<float> latencyTimes = new List<float>();
    private float lastRemoval;
    private readonly Dictionary<string, ProfilerRecorder> diagnosticRecorders = new Dictionary<string, ProfilerRecorder>();
    private readonly Dictionary<string, List<float>> diagnosticTimes = new Dictionary<string, List<float>>();
    private readonly Collider[] terrainProbes = new Collider[32];
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-erosionTest") < 0 || FindFirstObjectByType<ErosionTestDriver>() != null) return;
        var runner = new GameObject("Erosion Acceptance Runner").AddComponent<ErosionTestDriver>(); DontDestroyOnLoad(runner.gameObject);
    }
    private void Log(string text)
    { report.AppendLine(text); Debug.Log("[ErosionTest] " + text); File.WriteAllText(output, report.ToString()); }
    private void Check(bool success, string text)
    { if (success) passed++; else failed++; Log((success ? "PASS " : "FAIL ") + text); }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    private IEnumerator Start()
    {
        started = Time.realtimeSinceStartupAsDouble;
        string[] args = Environment.GetCommandLineArgs();
        int option = Array.IndexOf(args, "-erosionTestOut");
        output = option >= 0 && option + 1 < args.Length ? args[option + 1] : Path.Combine(Application.persistentDataPath, "erosion-test.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1; Application.runInBackground = true; Time.timeScale = 1f;
        if (Array.IndexOf(args, "-erosionIntegration") >= 0)
        { SceneManager.LoadScene(1); yield return null; }
        world = FindFirstObjectByType<ProceduralWorld>(); fluid = FindFirstObjectByType<ErosiveFluidSystem>();
        if (world == null || fluid == null) { Check(false, "Missing terrain or fluid system"); Finish(); yield break; }
        yield return null; fluid.SetEmitting(false); fluid.ClearDroplets();
        source = fluid.GetComponentsInChildren<ErosiveFluidSource>()[0];
        Log($"Unity {Application.unityVersion}; {SystemInfo.processorType}; {SystemInfo.graphicsDeviceName}; memory {SystemInfo.systemMemorySize} MB; resolution {Screen.width}x{Screen.height}");
        yield return Settle(true);
        if (Array.IndexOf(args, "-erosionPerformance") >= 0)
        {
            var placement = FindFirstObjectByType<ErosionDemoPlacement>();
            while (placement != null && !placement.Placed) yield return null;
            fluid.SetEmitting(false); fluid.ClearDroplets();
            bool baseline = Array.IndexOf(args, "-erosionBaseline") >= 0;
            bool reduced = Array.IndexOf(args, "-erosionReduced") >= 0;
            Set(world, "densityBudgetPerSecond", 8f);
            if (reduced)
            {
                Set(fluid, "maximumDroplets", 64); Set(fluid, "sampleInterval", 0.2f);
                Set(world, "erosionMeshesPerFrame", 1); Set(world, "workerCount", 1); Set(world, "densityBudgetPerSecond", 4f);
                Set(source, "dropletsPerSecond", 4f); Set(source, "lifetime", 12f);
            }
            VerificationCamera();
            if (!baseline && Array.IndexOf(args, "-erosionStress") >= 0) fluid.SetEmitting(true);
            else source.SetEmitting(!baseline);
            Log($"PROFILE baseline={baseline} reduced={reduced} source={source.transform.position} world={world.transform.position}");
            physicsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Physics.Simulate");
            var available = new List<ProfilerRecorderHandle>(); ProfilerRecorderHandle.GetAvailable(available);
            foreach (ProfilerRecorderHandle handle in available)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                string name = description.Name;
                if (!name.Contains("ScriptRunBehaviour") && !name.Contains("GC.Collect") &&
                    !name.StartsWith("Erosion.") && !name.Contains("DoRenderLoop")) continue;
                if (diagnosticRecorders.ContainsKey(name)) continue;
                diagnosticRecorders[name] = ProfilerRecorder.StartNew(description.Category, name);
                diagnosticTimes[name] = new List<float>();
            }
            yield return new WaitForSeconds(10f);
            frameTimes.Clear(); collecting = true; lastFrame = Time.realtimeSinceStartup;
            yield return new WaitForSeconds(30f); collecting = false;
            float p95 = Percentile(frameTimes);
            Log($"PERFORMANCE explicit URP 1920x1080 render per frame; frames={frameTimes.Count} p95={p95:F2} ms drops={fluid.ActiveCount} physicsP95={Percentile(physicsTimes):F2} ms uploadP95={Percentile(uploadTimes):F2} ms bakeP95={Percentile(bakeTimes):F2} ms commitP95={Percentile(commitTimes):F2} ms updateLatencyP95={Percentile(latencyTimes):F2} ms managed={GC.GetTotalMemory(false) / 1048576f:F1} MB allocated={UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576f:F1} MB");
            foreach (var pair in diagnosticTimes) Log($"STAGE {pair.Key} P95={Percentile(pair.Value):F2} ms");
            Check(p95 <= 16.7f, "1080p normal-speed frame-time P95 <= 16.7 ms");
            Check(fluid.ActiveCount <= fluid.Capacity, "Performance run respects droplet cap");
            Check((baseline || world.TotalDensityRemoved > 0f) && world.LastUpdateError == null && ColliderMeshesMatch(), "Integration flow erodes owned terrain with matching colliders and no update errors");
            Capture(); Finish(); yield break;
        }
        Set(world, "densityBudgetPerSecond", 10000f);
        var controller = FindFirstObjectByType<SmoothFirstPersonController>();
        if (controller != null) controller.enabled = true;
        Vector3 pit = new Vector3(-12f, 4f, -8f);
        float original = Height(pit);
        GameObject witness = GameObject.Find("Pit Physics Witness");
        float witnessStart = witness.transform.position.y;
        for (int i = 0; i < 100; i++) { world.Erode(pit, 1.5f, 0.08f); yield return new WaitForFixedUpdate(); }
        yield return Settle(false); yield return new WaitForSeconds(0.5f);
        Check(Height(pit) < original - 0.3f, "Repeated erosion creates a measurable pit");
        Check(witness.transform.position.y < witnessStart - 0.25f, "Dynamic object falls into the updated collider");
        if (controller != null)
        {
            witness.transform.position = new Vector3(-14f, 5f, -12f);
            controller.Body.position = pit + Vector3.up * 2f; controller.Body.linearVelocity = Vector3.zero;
            controller.Body.useGravity = true; controller.Body.WakeUp();
            yield return new WaitForSeconds(1f);
            Log($"PLAYER pit ground={Height(pit):F3} centre={controller.Body.position.y:F3} baseline={original:F3}");
            Check(controller.Body.position.y < original + 0.8f, "Player rigidbody can enter the pit");
            controller.Body.position = new Vector3(-4f, 8f, -8f);
        }
        var fuzz = FindFirstObjectByType<FuzzCreature>();
        var tentacle = FindFirstObjectByType<TentacleCreature>();
        Set(fuzz, "noticeRadius", 0f); Set(fuzz, "wanderRadius", 0f); Set(fuzz, "maxSpeed", 0f);
        Set(tentacle, "wanderRadius", 0f); Set(tentacle, "moveSpeed", 0f);
        float fuzzY = fuzz.transform.position.y, tentacleY = tentacle.transform.position.y;
        Vector3 fuzzFoot = fuzz.transform.position, tentacleFoot = tentacle.transform.position;
        fuzzFoot.y = Height(fuzzFoot); tentacleFoot.y = Height(tentacleFoot);
        for (int i = 0; i < 40; i++)
        { world.Erode(fuzzFoot, 1.25f, 0.12f); world.Erode(tentacleFoot, 1.25f, 0.12f); yield return new WaitForFixedUpdate(); }
        yield return Settle(false);
        yield return new WaitForSeconds(1f);
        Log($"CREATURES fuzz={fuzz.transform.position} tentacle={tentacle.transform.position} ground={Height(tentacle.transform.position):F3}");
        Check(fuzz.transform.position.y < fuzzY - 0.25f, "Fuzz creature follows erosion beneath its existing position");
        Check(tentacle.transform.position.y < tentacleY - 0.25f, "Tentacle creature reacquires eroded support beneath its existing position");
        var climber = new GameObject("Erosion climb contact witness").AddComponent<HandClimber>();
        RigidbodyInterpolation playerInterpolation = controller.Body.interpolation;
        controller.enabled = false; controller.Body.interpolation = RigidbodyInterpolation.None;
        controller.Body.position = new Vector3(6.7f, -1.5f, 9f); controller.Body.linearVelocity = Vector3.zero;
        controller.transform.position = controller.Body.position;
        controller.Orientation.rotation = Quaternion.LookRotation(Vector3.left); Physics.SyncTransforms();
        typeof(HandClimber).GetMethod("TryStartClimb", Private).Invoke(climber, null);
        bool gripped = climber.IsClimbing;
        Check(gripped, "Player can initially grip the generated cave wall");
        world.Erode(new Vector3(6f, -0.9f, 9f), 3f, 100f); yield return Settle(false);
        Check(gripped && !climber.IsClimbing && !controller.Body.isKinematic, "Removing terrain grip releases the player back to gravity");
        Destroy(climber.gameObject); controller.Orientation.rotation = Quaternion.identity;
        controller.Body.position = new Vector3(-4f, 8f, -8f);
        controller.Body.interpolation = playerInterpolation; controller.enabled = true;
        if (Array.IndexOf(args, "-erosionSupportOnly") >= 0) { Finish(); yield break; }
        world.Erode(new Vector3(0f, 2f, 0f), 1.25f, 30f); yield return Settle(false);
        Check(Seams(), "Face, edge and corner edits share boundary positions and normals");
        Check(ColliderMeshesMatch(), "Every visible terrain mesh matches its collision mesh");
        int chunksBefore = world.GetComponentsInChildren<ProceduralWorldChunk>().Length;
        world.Erode(new Vector3(-4f, -10f, -4f), 1.25f, 40f); yield return Settle(false);
        Check(world.GetComponentsInChildren<ProceduralWorldChunk>().Length > chunksBefore, "Erosion creates a surface in a formerly solid chunk");
        float density = world.SampleDensity(pit); int edits = world.ModifiedSampleCount;
        world.Generate(true); yield return null;
        Check(world.ModifiedSampleCount == edits && Mathf.Abs(world.SampleDensity(pit) - density) < 0.0001f, "Regeneration preserves relative density edits");
        float removed = world.TotalDensityRemoved; Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(0.2f);
        Check(world.TotalDensityRemoved == removed, "Pause does not accumulate erosion time"); Time.timeScale = 1f;
        Set(world, "seed", Get<int>(world, "seed") + 1); world.Generate(true); yield return null;
        Check(world.ModifiedSampleCount == 0, "Changing baseline parameters discards incompatible edits");
        Set(world, "seed", 1); Set(world, "blockingRadius", 0f); world.Generate(false);
        world.Erode(pit, 1.5f, 10f); InjectStaleResult(); yield return Settle(true);
        Check(world.StaleResultsRejected > 0, "Old generation/version results are rejected");
        Check(world.SampleDensity(pit) < world.SampleDensity(pit, false) - 0.1f, "Background generation cannot replace erosion data");
        Set(world, "blockingRadius", 40f); world.ResetErosion(); yield return Settle(true);
        int pending = world.PendingSampleCount; world.Erode(pit, 1f, -1f); world.Erode(new Vector3(float.NaN, 0f, 0f), 1f, 1f);
        Check(world.PendingSampleCount == pending, "Invalid brushes are ignored");
        yield return EmptyChunkTest();
        Set(world, "densityBudgetPerSecond", 8f);
        Check(!fluid.CanErode(witness.GetComponent<Collider>()) && !fluid.CanErode(controller.GetComponent<Collider>()), "Ordinary objects and player colliders cannot submit terrain erosion");
        Set(source, "lifetime", 0.3f);
        for (int i = 0; i < 300; i++) fluid.Spawn(source, new Vector3(0f, 12f, -10f), Vector3.zero);
        Check(fluid.ActiveCount == fluid.Capacity && fluid.SkippedSpawns > 0, "Full pool skips emission and never exceeds capacity");
        Check(Physics.GetIgnoreCollision(fluid.Droplets[0].Collider, fuzz.GetComponent<Collider>()) &&
            Physics.GetIgnoreCollision(fluid.Droplets[0].Collider, tentacle.GetComponent<Collider>()), "Droplet collision pairs exclude creatures on the Default layer");
        Check((fluid.Droplets[0].Collider.excludeLayers.value & (1 << 0)) == 0 &&
            (fluid.Droplets[0].Collider.excludeLayers.value & (1 << 6)) != 0, "Environment collisions include Default and exclude the Player layer");
        var newCreature = new GameObject("Runtime actor exclusion witness", typeof(SphereCollider), typeof(Rigidbody), typeof(FuzzCreature));
        newCreature.transform.position = new Vector3(-12f, 6f, -12f);
        Check(Physics.GetIgnoreCollision(fluid.Droplets[0].Collider, newCreature.GetComponent<Collider>()), "Newly spawned creatures are excluded before their first physics step");
        newCreature.SetActive(false); newCreature.SetActive(true);
        Check(Physics.GetIgnoreCollision(fluid.Droplets[0].Collider, newCreature.GetComponent<Collider>()), "Reactivating a creature restores ignored water collision pairs");
        Destroy(newCreature);
        yield return new WaitForSeconds(0.6f);
        Check(fluid.ActiveCount == 0, "Expired droplets return to the pool");
        Set(source, "lifetime", 15f); Set(source, "erosionRate", 0f);
        var pathsBefore = new PathStats(); source.SetEmitting(true);
        yield return Track(10f, pathsBefore); source.SetEmitting(false);
        int basinDrops = 0;
        foreach (ErosiveFluidDroplet droplet in fluid.Droplets)
            if (droplet.Position.z > 7f && droplet.Position.z < 14f && droplet.Position.y < 2f) basinDrops++;
        Check(basinDrops >= 4, $"Droplets gather inside the lower basin (count={basinDrops})");
        fluid.ClearDroplets();
        Check(pathsBefore.count >= 10, $"Gravity carries droplets down the ramp (crossings={pathsBefore.count})");
        Log($"BASELINE crossing mean x={pathsBefore.MeanX:F3} y={pathsBefore.MeanY:F3}");
        Set(source, "erosionRate", 0.5f); source.SetEmitting(true);
        yield return Track(60f, new PathStats()); source.SetEmitting(false); fluid.ClearDroplets(); yield return Settle(false);
        float removedByWater = world.TotalDensityRemoved;
        Check(removedByWater > 2f, $"Water contacts remove terrain density ({removedByWater:F2})");
        var pathsAfter = new PathStats(); Set(source, "erosionRate", 0f); source.SetEmitting(true);
        yield return Track(10f, pathsAfter); source.SetEmitting(false); fluid.ClearDroplets(); yield return Settle(false);
        Log($"ERODED crossing mean x={pathsAfter.MeanX:F3} y={pathsAfter.MeanY:F3}; fresh-channel crossings={pathsAfter.eroded}");
        Check(pathsAfter.count >= 10 && pathsAfter.eroded > 0, "Subsequent droplets enter newly eroded channel space");
        Check(Mathf.Abs(pathsAfter.MeanX - pathsBefore.MeanX) > 0.15f, "Terrain change redirects the horizontal flow path");
        float stoppedDensity = world.SampleDensity(new Vector3(pathsAfter.MeanX, pathsAfter.MeanY, 2f));
        yield return new WaitForSeconds(1f);
        Check(Mathf.Abs(world.SampleDensity(new Vector3(pathsAfter.MeanX, pathsAfter.MeanY, 2f)) - stoppedDensity) < 0.0001f, "Channel persists after stopping and draining water");
        yield return StationaryTest();
        yield return RateAndBudgetTest();
        Capture(); Finish();
    }
    private IEnumerator Settle(bool generation)
    {
        yield return new WaitForFixedUpdate();
        double until = Time.realtimeSinceStartupAsDouble + 60;
        while ((world.UpdatesPending || (generation && !world.GenerationComplete)) && Time.realtimeSinceStartupAsDouble < until) yield return null;
        Check(!world.UpdatesPending && (!generation || world.GenerationComplete), "Terrain work completes within timeout");
    }
    private float Height(Vector3 p)
    { return Physics.Raycast(new Vector3(p.x, 20f, p.z), Vector3.down, out RaycastHit hit, 60f, 1 << 7) ? hit.point.y : -100f; }
    private bool ColliderMeshesMatch()
    {
        foreach (ProceduralWorldChunk chunk in world.GetComponentsInChildren<ProceduralWorldChunk>())
            if (chunk.GetComponent<MeshCollider>().sharedMesh != chunk.GetComponent<MeshFilter>().sharedMesh) return false;
        return true;
    }
    private bool Seams()
    {
        var lookup = new Dictionary<Vector3Int, ProceduralWorldChunk>();
        foreach (ProceduralWorldChunk chunk in world.GetComponentsInChildren<ProceduralWorldChunk>()) lookup[chunk.Coordinate] = chunk;
        foreach (var pair in lookup) for (int axis = 0; axis < 3; axis++)
        {
            Vector3Int direction = axis == 0 ? Vector3Int.right : axis == 1 ? Vector3Int.up : new Vector3Int(0, 0, 1);
            if (!lookup.TryGetValue(pair.Key + direction, out ProceduralWorldChunk next)) continue;
            Mesh a = pair.Value.GetComponent<MeshFilter>().sharedMesh, b = next.GetComponent<MeshFilter>().sharedMesh;
            if (a == null || b == null) continue;
            var boundary = new Dictionary<Vector3Int, Vector3>();
            Vector3[] av = a.vertices, an = a.normals, bv = b.vertices, bn = b.normals;
            for (int i = 0; i < av.Length; i++) if (Mathf.Abs(av[i][axis] - world.ChunkWidth) < 0.0001f)
            { Vector3 p = pair.Value.transform.TransformPoint(av[i]); boundary[Vector3Int.RoundToInt(p * 1000f)] = an[i]; }
            for (int i = 0; i < bv.Length; i++) if (Mathf.Abs(bv[i][axis]) < 0.0001f)
            { Vector3 p = next.transform.TransformPoint(bv[i]); if (!boundary.TryGetValue(Vector3Int.RoundToInt(p * 1000f), out Vector3 normal) || (normal - bn[i]).sqrMagnitude > 0.00001f) return false; }
        }
        return true;
    }
    private void InjectStaleResult()
    {
        Type resultType = typeof(ProceduralWorld).GetNestedType("ChunkResult", BindingFlags.NonPublic);
        object result = Activator.CreateInstance(resultType);
        resultType.GetField("epoch").SetValue(result, -999);
        object queue = typeof(ProceduralWorld).GetField("finished", Private).GetValue(world);
        queue.GetType().GetMethod("Enqueue").Invoke(queue, new[] { result });
    }
    private IEnumerator EmptyChunkTest()
    {
        Scene scene = SceneManager.CreateScene("Empty Chunk Fixture");
        GameObject root = new GameObject("Small density fixture"); SceneManager.MoveGameObjectToScene(root, scene);
        var fixture = root.AddComponent<ProceduralWorld>();
        Set(fixture, "worldSize", new Vector2(4f, 4f)); Set(fixture, "cellSize", 0.5f); Set(fixture, "chunkCells", 8);
        Set(fixture, "erosionTestCourse", true); Set(fixture, "densityBudgetPerSecond", 1000000f); fixture.Generate(true);
        MeshCollider candidate = null;
        foreach (ProceduralWorldChunk chunk in fixture.GetComponentsInChildren<ProceduralWorldChunk>())
            if (chunk.Coordinate.y == 4) candidate = chunk.GetComponent<MeshCollider>();
        fixture.Erode(new Vector3(0f, 4f, 0f), 8f, 200f);
        double until = Time.realtimeSinceStartupAsDouble + 30;
        yield return new WaitForFixedUpdate();
        while (fixture.UpdatesPending && Time.realtimeSinceStartupAsDouble < until) yield return null;
        Check(candidate != null && candidate.sharedMesh == null, "Fully eroded chunks clear their old collision surface");
        yield return SceneManager.UnloadSceneAsync(scene);
    }
    private sealed class PathStats
    {
        public int count, eroded; public float x, y;
        public float MeanX => count > 0 ? x / count : 0f;
        public float MeanY => count > 0 ? y / count : 0f;
    }
    private IEnumerator Track(float seconds, PathStats stats)
    {
        var previous = new Dictionary<int, Vector3>();
        var deepSteps = new Dictionary<int, int>();
        float until = Time.time + seconds; bool penetration = false; float deepest = 0f;
        while (Time.time < until)
        {
            foreach (ErosiveFluidDroplet droplet in fluid.Droplets)
            {
                int id = droplet.GetInstanceID(); Vector3 p = droplet.Position;
                if (droplet.Elapsed > Time.fixedDeltaTime * 2f && previous.TryGetValue(id, out Vector3 old) && old.z < 2f && p.z >= 2f)
                {
                    float t = (2f - old.z) / (p.z - old.z); Vector3 crossing = Vector3.Lerp(old, p, t);
                    stats.count++; stats.x += crossing.x; stats.y += crossing.y;
                    if (world.SampleDensity(crossing, false) > 0f && world.SampleDensity(crossing) < 0f) stats.eroded++;
                }
                previous[id] = p;
                bool deep = false;
                int count = Physics.OverlapSphereNonAlloc(p, source.Radius, terrainProbes, 1 << 7, QueryTriggerInteraction.Ignore);
                SphereCollider sphere = droplet.GetComponent<SphereCollider>();
                for (int j = 0; j < count; j++)
                {
                    Collider terrain = terrainProbes[j];
                    if (world.OwnsCollider(terrain) && Physics.ComputePenetration(sphere, p, droplet.transform.rotation,
                        terrain, terrain.transform.position, terrain.transform.rotation, out _, out float depth))
                    { deepest = Mathf.Max(deepest, depth); deep |= depth > source.Radius * 0.5f; }
                }
                deepSteps.TryGetValue(id, out int steps); steps = deep ? steps + 1 : 0; deepSteps[id] = steps;
                penetration |= steps >= 5;
            }
            yield return new WaitForFixedUpdate();
        }
        Check(!penetration, $"No sustained terrain penetration (max contact correction {deepest:F3} m)");
    }
    private IEnumerator StationaryTest()
    {
        source.SetEmitting(false); fluid.ClearDroplets();
        Set(source, "erosionRate", 0f);
        Vector3 low = new Vector3(-12f, 1f, -8f); low.y = Height(low) + source.Radius + 0.01f;
        fluid.Spawn(source, low, Vector3.zero);
        yield return new WaitForSeconds(2f);
        Check(fluid.ActiveCount == 1 && fluid.Droplets[0].Body.IsSleeping(), "A stationary droplet can sleep on flat terrain");
        Set(source, "erosionRate", 0.5f);
        float before = world.TotalDensityRemoved;
        yield return new WaitForSeconds(2f); yield return Settle(false);
        Check(world.TotalDensityRemoved > before, "Slow or sleeping droplets retain weak erosion");
        fluid.ClearDroplets(); yield return Settle(false);
    }
    private IEnumerator RateAndBudgetTest()
    {
        fluid.ClearDroplets(); world.ResetErosion(); yield return Settle(true);
        Set(world, "densityBudgetPerSecond", 8f); float start = world.TotalDensityRemoved;
        Vector3 point = new Vector3(-12f, 4f, -8f);
        for (int i = 0; i < 50; i++) { world.Erode(point, 1f, 100f); yield return new WaitForFixedUpdate(); }
        yield return Settle(false);
        Check(world.TotalDensityRemoved - start <= 8.8f, "Per-second budget caps the actual aggregated sample reduction");
        float slow = world.SampleDensity(point);
        world.ResetErosion(); yield return Settle(true); Set(world, "densityBudgetPerSecond", 10000f);
        for (int i = 0; i < 10; i++) { world.Erode(point, 1f, 0.1f); yield return new WaitForSeconds(0.1f); }
        yield return Settle(false); float density10 = world.SampleDensity(point);
        world.ResetErosion(); yield return Settle(true);
        for (int i = 0; i < 5; i++) { world.Erode(point, 1f, 0.2f); yield return new WaitForSeconds(0.2f); }
        yield return Settle(false);
        Check(Mathf.Abs(world.SampleDensity(point) - density10) < 0.001f, "Time-integrated brush strength is sampling-frequency independent");
    }
    private void Update()
    {
        if (collecting)
        {
            float now = Time.realtimeSinceStartup; frameTimes.Add((now - lastFrame) * 1000f); lastFrame = now;
            if (physicsRecorder.Valid) physicsTimes.Add(physicsRecorder.LastValue / 1000000f);
            foreach (var pair in diagnosticRecorders) if (pair.Value.Valid) diagnosticTimes[pair.Key].Add(pair.Value.LastValue / 1000000f);
            if (world.TotalDensityRemoved != lastRemoval)
            {
                uploadTimes.Add(world.LastUploadMilliseconds); bakeTimes.Add(world.LastBakeMilliseconds);
                commitTimes.Add(world.LastCommitMilliseconds); latencyTimes.Add(world.LastUpdateLatencyMilliseconds);
                lastRemoval = world.TotalDensityRemoved;
            }
        }
        if (!ended && started > 0 && Time.realtimeSinceStartupAsDouble - started > 600) { Check(false, "Overall test timeout"); Finish(); }
    }
    private void Capture()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        if (verificationTexture != null)
        {
            RenderTexture previous = RenderTexture.active; RenderTexture.active = verificationTexture;
            var pixels = new Texture2D(verificationTexture.width, verificationTexture.height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.ChangeExtension(output, ".png"), pixels.EncodeToPNG());
            RenderTexture.active = previous; Destroy(pixels); return;
        }
        ScreenCapture.CaptureScreenshot(Path.ChangeExtension(output, ".png"));
    }
    private void VerificationCamera()
    {
        Camera original = Camera.main;
        if (original == null) return;
        var camera = new GameObject("Erosion Verification View").AddComponent<Camera>();
        camera.CopyFrom(original); original.enabled = false; original.tag = "Untagged"; camera.tag = "MainCamera";
        Vector3 position = source.transform.position;
        camera.transform.position = position - source.transform.forward * 4f + source.transform.right * 6f + Vector3.up * 7f;
        camera.transform.LookAt(position + source.transform.forward * 6f - Vector3.up * 2f);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = original.GetUniversalAdditionalCameraData().renderPostProcessing;
        // Batch-mode players skip their backbuffer. Explicit rendering exercises the same URP
        // scene and physics at 1080p without taking focus from the user's open editor.
        camera.enabled = false; verificationView = camera;
        verificationTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        verificationTexture.Create(); camera.aspect = 1920f / 1080f;
        renderRequest = new UniversalRenderPipeline.SingleCameraRequest { destination = verificationTexture };
        Set(fluid, "view", camera);
    }
    private void LateUpdate()
    {
        if (verificationView != null && RenderPipeline.SupportsRenderRequest(verificationView, renderRequest))
            RenderPipeline.SubmitRenderRequest(verificationView, renderRequest);
    }
    private static float Percentile(List<float> samples)
    {
        if (samples.Count == 0) return float.NaN;
        samples.Sort(); return samples[Mathf.Min(samples.Count - 1, (int)(samples.Count * 0.95f))];
    }
    private void OnDestroy()
    {
        physicsRecorder.Dispose();
        foreach (ProfilerRecorder recorder in diagnosticRecorders.Values) recorder.Dispose();
        if (verificationTexture != null) { verificationTexture.Release(); Destroy(verificationTexture); }
    }
    private void Finish()
    {
        ended = true; Log($"RESULT passed={passed} failed={failed} elapsed={Time.realtimeSinceStartupAsDouble - started:F1}s");
        StartCoroutine(ExitAfterCapture());
    }
    private IEnumerator ExitAfterCapture()
    {
        yield return new WaitForSecondsRealtime(1f);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(failed == 0 ? 0 : 2);
#else
        Application.Quit(failed == 0 ? 0 : 2);
#endif
    }
}
