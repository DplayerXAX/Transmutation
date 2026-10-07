using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>Explicit prototype checks. Runtime objects live in a temporary, unsaved scene.</summary>
public static class SkySpiderValidation
{
    private const string OutputDirectory = "Temp/SkySpiderValidation";
    private static readonly Vector3 TestOrigin = new Vector3(1000f, 1000f, 1000f);
    private static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static IEnumerator routine;
    private static Scene testScene;
    private static Scene originalScene;
    private static int lastFrame;
    private static int checkCount;
    private static readonly List<string> results = new List<string>();
    public static string Status { get; private set; } = "Not run";

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Sky Spider validation: " + message);
        checkCount++;
    }

    [MenuItem("Tools/Creatures/Validate Sky Spider Geometry")]
    public static void ValidateGeometry()
    {
        checkCount = 0;
        for (int seed = 0; seed < 32; seed++)
        {
            var geometry = new SkyWebGeometry(seed, 6f, 7, 256, 6, 0.035f, 0.008f);
            var duplicate = new SkyWebGeometry(seed, 6f, 7, 256, 6, 0.035f, 0.008f);
            var strands = new Mesh();
            var patches = new Mesh();
            try
            {
                Check(geometry.StrandCount <= 256 && geometry.StrandCount > 100, "bounded strand budget");
                Check(geometry.StrandCount == duplicate.StrandCount, "seeded strand count");
                Check(geometry.CellCount == 7, "seven complete D12 cages");
                var corners = new Dictionary<Vector3, int>();
                float edgeLength = (geometry.GetStrand(0).End - geometry.GetStrand(0).Start).magnitude;
                for (int edge = 0; edge < SkyWebGeometry.EdgesPerCell; edge++)
                {
                    SkyWebGeometry.Strand cageEdge = geometry.GetStrand(edge);
                    Check(Mathf.Abs((cageEdge.End - cageEdge.Start).magnitude - edgeLength) < 0.00001f,
                        "regular D12 edges have equal lengths");
                    foreach (Vector3 corner in new[] { cageEdge.Start, cageEdge.End })
                        corners[corner] = corners.TryGetValue(corner, out int degree) ? degree + 1 : 1;
                }
                Check(corners.Count == SkyWebGeometry.VerticesPerCell, "D12 has twenty corners");
                foreach (int degree in corners.Values) Check(degree == 3, "three edges meet at each D12 corner");
                Check(Vector3.Distance(geometry.GetCellCenter(0), geometry.GetCellCenter(1)) < 1.3f &&
                    Vector3.Distance(geometry.GetCellCenter(0), geometry.GetCellCenter(2)) > 4f,
                    "nearby cluster members and separated clusters");
                int connections = 0;
                for (int i = 0; i < geometry.StrandCount; i++)
                {
                    SkyWebGeometry.Strand strand = geometry.GetStrand(i);
                    if (strand.Connection) connections++;
                    Check(strand.Start == duplicate.GetStrand(i).Start && strand.End == duplicate.GetStrand(i).End,
                        "seeded endpoints");
                    Check(strand.Start.magnitude + strand.Radius <= 6.00001f &&
                        strand.End.magnitude + strand.Radius <= 6.00001f, "tube remains inside maximum radius");
                }
                Check(connections == geometry.CellCount - 1 + geometry.CellCount * 3,
                    "connected cages plus three loose threads per cage");
                int longThreads = 0;
                for (int i = 0; i < geometry.StrandCount; i++)
                    if (geometry.GetStrand(i).Connection &&
                        Vector3.Distance(geometry.GetStrand(i).Start, geometry.GetStrand(i).End) > 2f) longThreads++;
                Check(longThreads >= 12, "sprawling single strands extend beyond the cages");
                foreach (float scale in new[] { 0.5f, 2f })
                {
                    var scaled = new SkyWebGeometry(seed, 6f, 7, 256, 6, 0.035f, 0.008f, webScale: scale);
                    Check(scaled.StrandCount == geometry.StrandCount && scaled.MaximumRadius == 6f * scale,
                        "scale preserves budget and scales bounds");
                    for (int i = 0; i < geometry.StrandCount; i++)
                    {
                        SkyWebGeometry.Strand source = geometry.GetStrand(i), target = scaled.GetStrand(i);
                        Check((target.Start - source.Start * scale).sqrMagnitude < 0.000001f &&
                            (target.End - source.End * scale).sqrMagnitude < 0.000001f &&
                            Mathf.Abs(target.Radius - source.Radius * scale) < 0.000001f,
                            "uniform scale applies to contact paths and thickness");
                    }
                    Check((scaled.SamplePatrolPoint(0.75f, Vector3.one) -
                        geometry.SamplePatrolPoint(0.75f, Vector3.one) * scale).sqrMagnitude < 0.000001f,
                        "patrol scales with visuals");
                }
                geometry.Render(strands, patches, geometry.InitialStrandCount);
                Check(geometry.RenderedStrandCount < geometry.StrandCount, "starts partially grown");
                geometry.Render(strands, patches, geometry.StrandCount);
                Check(geometry.RenderedPatchCount == 6, "six sparse patches at full growth");
                Check(strands.vertexCount == geometry.StrandCount * 12, "combined six-sided tubes");
                Vector3[] positions = strands.vertices;
                Vector3 minimum = positions[0], maximum = positions[0];
                foreach (Vector3 position in positions)
                {
                    Check(position.magnitude <= 6.00001f, "all rendered vertices bounded");
                    minimum = Vector3.Min(minimum, position);
                    maximum = Vector3.Max(maximum, position);
                }
                Vector3 extent = maximum - minimum;
                Check(Mathf.Min(extent.x, extent.y, extent.z) > 4f, "substantial depth along all axes");
                int revision = geometry.MeshRevision;
                geometry.Render(strands, patches, geometry.StrandCount);
                Check(geometry.MeshRevision == revision, "finished meshes remain static");
            }
            finally { Object.DestroyImmediate(strands); Object.DestroyImmediate(patches); }
        }
        float crossing = SkyWebGeometry.SegmentDistanceSquared(Vector3.left, Vector3.right,
            Vector3.down, Vector3.up, out _, out _);
        Check(crossing < 0.00001f, "crossing swept segments");
        float separated = SkyWebGeometry.SegmentDistanceSquared(Vector3.zero, Vector3.right,
            Vector3.up * 2f, Vector3.right + Vector3.up * 2f, out _, out _);
        Check(Mathf.Abs(separated - 4f) < 0.00001f, "parallel separated segments");
        float point = SkyWebGeometry.SegmentDistanceSquared(Vector3.zero, Vector3.zero,
            Vector3.right, Vector3.right, out _, out _);
        Check(Mathf.Abs(point - 1f) < 0.00001f, "degenerate point segments");
        Status = "Geometry passed: " + checkCount + " assertions across 32 seeds";
        Directory.CreateDirectory(OutputDirectory);
        File.WriteAllText(OutputDirectory + "/geometry.txt", Status);
        Debug.Log(Status);
    }

    [MenuItem("Tools/Creatures/Validate Sky Spider Runtime (Play Mode)")]
    public static void StartRuntimeChecks()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isPaused)
            throw new InvalidOperationException("Run Sky Spider runtime checks in unpaused Play Mode.");
        if (routine != null) throw new InvalidOperationException("Sky Spider checks are already running.");
        ValidateGeometry();
        results.Clear();
        checkCount = 0;
        originalScene = SceneManager.GetActiveScene();
        testScene = SceneManager.CreateScene("Sky Spider Temporary Validation");
        SceneManager.SetActiveScene(testScene);
        lastFrame = -1;
        routine = RuntimeChecks();
        Status = "Running";
        EditorApplication.update += Pump;
    }

    private static void Pump()
    {
        if (!EditorApplication.isPlaying) { Finish("Cancelled: Play Mode ended"); return; }
        if (lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            if (!routine.MoveNext()) Finish("Passed: " + checkCount + " runtime assertions");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish("FAILED: " + exception.Message);
        }
    }

    private static void Finish(string status)
    {
        EditorApplication.update -= Pump;
        routine = null;
        Status = status;
        if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
        if (testScene.IsValid() && testScene.isLoaded && EditorApplication.isPlaying) SceneManager.UnloadSceneAsync(testScene);
        Directory.CreateDirectory(OutputDirectory);
        File.WriteAllText(OutputDirectory + "/runtime.txt", status + "\n" + string.Join("\n", results));
        Debug.Log("Sky Spider " + status);
    }

    private static GameObject InstantiateAsset(string path, Vector3 position)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Check(prefab != null, "generated prefab exists: " + path);
        return Object.Instantiate(prefab, position, Quaternion.identity);
    }

    private static Rigidbody Cube(Vector3 position, float size = 0.12f)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = position;
        cube.transform.localScale = Vector3.one * size;
        cube.layer = 31;
        Rigidbody body = cube.AddComponent<Rigidbody>();
        body.useGravity = false;
        return body;
    }

    private static void InvokeTick(SkySpider spider)
    {
        typeof(SkySpider).GetMethod("TickCreature", PrivateInstance).Invoke(spider, new object[] { 0.02f });
    }

    private static Vector3 FindGap(SkyWeb web, int seed)
    {
        var random = new System.Random(seed);
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            Vector3 point = new Vector3((float)random.NextDouble() * 7f - 3.5f,
                (float)random.NextDouble() * 7f - 3.5f, (float)random.NextDouble() * 7f - 3.5f);
            if (point.magnitude > 4.5f) continue;
            bool clear = true;
            for (int i = 0; i < web.StrandCount; i++)
            {
                SkyWebGeometry.Strand strand = web.Geometry.GetStrand(i);
                if (SkyWebGeometry.SegmentDistanceSquared(point, point, strand.Start, strand.End, out _, out _) < 0.36f)
                { clear = false; break; }
            }
            if (clear) return web.transform.position + point;
        }
        throw new InvalidOperationException("No clear test gap found");
    }

    private static IEnumerator RuntimeChecks()
    {
        SkySpider spider = InstantiateAsset(SkySpiderSetup.SpiderPath, TestOrigin).GetComponent<SkySpider>();
        SkyWeb first = spider.CurrentWeb;
        Check(first != null && !first.CanBeCarried, "initial web is a non-carryable Creature");
        Check(first.transform.parent == null, "web independent of spider transform");
        first.AdvanceGrowth(45f);
        Check(first.IsFullyGrown && first.StrandCount <= 256 && first.PatchCount == 6, "growth completes within budget");
        int revision = first.MeshRevision;
        first.AdvanceGrowth(100f);
        Check(first.MeshRevision == revision, "completed web stops rebuilding");
        Vector3 initialSpider = spider.transform.position;
        for (int frame = 0; frame < 30; frame++) yield return null;
        Check((spider.transform.position - initialSpider).sqrMagnitude > 0.001f, "autonomous roaming");
        Check((spider.transform.position - first.transform.position).magnitude <= first.MaximumRadius, "roams inside territory");

        var carrierObject = new GameObject("Validation Carrier");
        carrierObject.transform.position = TestOrigin;
        carrierObject.SetActive(false);
        Transform anchor = new GameObject("Validation Carry Anchor").transform;
        anchor.position = TestOrigin + Vector3.right * 8f;
        PlayerCreatureCarrier carrier = carrierObject.AddComponent<PlayerCreatureCarrier>();
        var carrierFields = new SerializedObject(carrier);
        carrierFields.FindProperty("carryPoint").objectReferenceValue = anchor;
        carrierFields.ApplyModifiedPropertiesWithoutUndo();
        carrier.enabled = false; // Keep validation independent of focus and input.
        carrierObject.SetActive(true);
        Vector3 originalWebPosition = first.transform.position;
        Check(spider.TryBeginCarry(carrier, anchor), "spider can enter shared carry");
        InvokeTick(spider);
        int heldStrands = first.StrandCount;
        for (int frame = 0; frame < 3; frame++) yield return null;
        Check(first.StrandCount == heldStrands && first.transform.position == originalWebPosition, "carry pauses growth without moving silk");
        spider.GetComponent<Rigidbody>().position = anchor.position;
        spider.EndCarry(carrier);
        InvokeTick(spider);
        Check(spider.CurrentWeb == first && spider.IsReturning && spider.CreatedWebCount == 1, "nearby release returns to old web");

        Check(spider.TryBeginCarry(carrier, anchor), "second carry");
        InvokeTick(spider);
        Vector3 distant = TestOrigin + Vector3.right * 30f;
        spider.GetComponent<Rigidbody>().position = distant;
        spider.EndCarry(carrier);
        InvokeTick(spider);
        SkyWeb second = spider.CurrentWeb;
        Check(second != first && second.transform.position == distant && spider.CreatedWebCount == 2, "distant release creates new territory");
        Check(first.transform.position == originalWebPosition && first.IsFullyGrown, "old web persists");
        Check(spider.TryBeginCarry(carrier, anchor), "carry during unfinished growth");
        InvokeTick(spider);
        heldStrands = second.StrandCount;
        for (int frame = 0; frame < 5; frame++) yield return null;
        Check(second.StrandCount == heldStrands, "unfinished web pauses while held");
        spider.GetComponent<Rigidbody>().position = distant;
        spider.EndCarry(carrier);
        InvokeTick(spider);
        for (int frame = 0; frame < 3; frame++) yield return null;
        second.AdvanceGrowth(45f);

        SkyWebGeometry.Strand strand = first.Geometry.GetStrand(0);
        Vector3 contact = first.transform.position + (strand.Start + strand.End) * 0.5f;
        Rigidbody prey = Cube(contact);
        GameObject extraCollider = new GameObject("Compound Collider");
        extraCollider.transform.SetParent(prey.transform, false);
        extraCollider.AddComponent<BoxCollider>().size = Vector3.one * 0.5f;
        int events = 0;
        first.Captured += _ => events++;
        Physics.SyncTransforms();
        for (int frame = 0; frame < 5; frame++) yield return null;
        WebCapture capture = prey.GetComponent<WebCapture>();
        Check(capture != null && capture.OwningWeb == first, "strand contact traps compound body");
        Check(events == 1, "compound body emits one capture event");
        Vector3 capturedPosition = prey.position;
        prey.AddForce(Vector3.up * 20f, ForceMode.Impulse);
        for (int frame = 0; frame < 5; frame++) yield return null;
        Check(Vector3.Distance(prey.position, capturedPosition) < 0.15f, "fixed attachment resists physics impulse");
        Rigidbody gap = Cube(FindGap(first, 81));
        Physics.SyncTransforms();
        for (int frame = 0; frame < 5; frame++) yield return null;
        Check(gap.GetComponent<WebCapture>() == null, "clear gaps do not trap");
        Check(!first.IsEligible(spider.GetComponent<Rigidbody>()), "spider excluded");
        gap.gameObject.layer = LayerMask.NameToLayer("Player");
        Check(!first.IsEligible(gap), "player layer excluded");
        gap.gameObject.layer = 31;
        gap.isKinematic = true;
        Check(!first.IsEligible(gap), "kinematic bodies excluded");
        gap.isKinematic = false;

        Rigidbody triggerOnly = Cube(contact);
        triggerOnly.GetComponent<Collider>().isTrigger = true;
        Physics.SyncTransforms();
        for (int frame = 0; frame < 5; frame++) yield return null;
        Check(triggerOnly.GetComponent<WebCapture>() == null, "trigger-only bodies excluded");

        GameObject creatureObject = new GameObject("Captured Fire Eater");
        creatureObject.transform.position = FindGap(first, 91);
        Rigidbody creatureBody = creatureObject.AddComponent<Rigidbody>();
        creatureObject.AddComponent<BoxCollider>();
        FireEater creature = creatureObject.AddComponent<FireEater>();
        creatureBody.useGravity = false;
        Check(first.TryCapture(creatureBody, creatureBody.position), "existing creature can be trapped");
        Check(!creature.SimulationEnabled, "captured creature simulation suspended");
        Check(creature.TryBeginCarry(carrier, anchor), "pickup releases captured creature");
        Check(creature.SimulationEnabled && !creature.GetComponent<WebCapture>().IsCaptured, "pickup restores original simulation state");
        Check(!first.IsEligible(creatureBody), "held creature excluded");
        creature.EndCarry(carrier);
        creatureObject.SetActive(false);
        Check(first.CaptureCount == 1, "carry handoff forgets capture");
        first.enabled = false;
        Check(!capture.IsCaptured && first.CaptureCount == 0, "disabling web releases all captures");
        first.enabled = true;
        prey.gameObject.SetActive(false);
        triggerOnly.gameObject.SetActive(false);

        // Inactive prior state must not accidentally become active after release.
        first.enabled = false; // Avoid automatic recapture racing the explicit attach assertion.
        creatureObject.SetActive(true);
        creature.SetSimulationEnabled(false);
        WebCapture creatureCapture = creature.GetComponent<WebCapture>();
        float cooldownEnd = Time.time + 0.6f;
        while (Time.time < cooldownEnd) yield return null;
        first.enabled = true;
        Check(first.TryCapture(creatureBody, creatureBody.position), "recapture after cooldown");
        creatureCapture.Release();
        Check(!creature.SimulationEnabled, "restore previously disabled simulation");
        creatureObject.SetActive(false);

        // Sweep completely across a strand in one step, while both ends miss the strand.
        Rigidbody fast = Cube(contact + Vector3.forward * 2f, 0.08f);
        fast.linearVelocity = Vector3.forward * 100f;
        typeof(SkyWeb).GetField("previousCenters", PrivateInstance).SetValue(first,
            new Dictionary<Rigidbody, Vector3> { { fast, contact - Vector3.forward * 2f } });
        Physics.SyncTransforms();
        Action scan = (Action)Delegate.CreateDelegate(typeof(Action), first, typeof(SkyWeb).GetMethod("ScanForCaptures", PrivateInstance));
        scan();
        Check(fast.GetComponent<WebCapture>() != null, "fast swept contact traps");
        fast.gameObject.SetActive(false);

        SkyWeb third = InstantiateAsset(SkySpiderSetup.WebPath, TestOrigin + Vector3.left * 30f).GetComponent<SkyWeb>();
        third.AdvanceGrowth(45f);
        SkyWeb[] webs = { first, second, third };
        for (int webIndex = 0; webIndex < webs.Length; webIndex++)
        for (int i = 0; i < 4; i++) Cube(FindGap(webs[webIndex], 100 + i));
        Physics.SyncTransforms();
        Action[] scans = new Action[3];
        for (int i = 0; i < scans.Length; i++)
        {
            scans[i] = (Action)Delegate.CreateDelegate(typeof(Action), webs[i], typeof(SkyWeb).GetMethod("ScanForCaptures", PrivateInstance));
            scans[i]();
        }
        for (int webCount = 1; webCount <= 3; webCount += 2)
        {
            var timer = new Stopwatch();
            timer.Start();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 200; iteration++)
            for (int i = 0; i < webCount; i++) scans[i]();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            timer.Stop();
            Check(allocated == 0, "steady trapping scan has zero managed allocations");
            results.Add(webCount + " web(s): " + (timer.Elapsed.TotalMilliseconds / 200d).ToString("F3") +
                " ms per physics scan, " + allocated + " managed bytes over 200 scans");
        }
        Check(first.MeshRevision == revision, "old completed mesh remained static throughout checks");
        results.Add("Geometry: " + first.StrandCount + " strands, " + first.PatchCount + " patches, two renderers per web");
        var scaledObject = new GameObject("Scaled Web Validation");
        scaledObject.SetActive(false);
        scaledObject.transform.position = TestOrigin + Vector3.forward * 60f;
        SkyWeb scaledWeb = scaledObject.AddComponent<SkyWeb>();
        var scaledFields = new SerializedObject(scaledWeb);
        scaledFields.FindProperty("webScale").floatValue = 2f;
        scaledFields.ApplyModifiedPropertiesWithoutUndo();
        scaledObject.SetActive(true);
        scaledWeb.AdvanceGrowth(45f);
        Check(scaledWeb.MaximumRadius == 12f && scaledWeb.StrandCount == first.StrandCount,
            "double scale changes radius without increasing geometry count");
        Vector3 farContact = Vector3.zero;
        for (int i = 0; i < scaledWeb.StrandCount; i++)
        {
            SkyWebGeometry.Strand path = scaledWeb.Geometry.GetStrand(i);
            Vector3 candidate = Vector3.Lerp(path.Start, path.End, 0.9f);
            if (candidate.sqrMagnitude > farContact.sqrMagnitude) farContact = candidate;
        }
        Check(farContact.magnitude > 8f, "scaled contact lies beyond original broad phase");
        Rigidbody scaledPrey = Cube(scaledObject.transform.position + farContact);
        Physics.SyncTransforms();
        Action scaledScan = (Action)Delegate.CreateDelegate(typeof(Action), scaledWeb,
            typeof(SkyWeb).GetMethod("ScanForCaptures", PrivateInstance));
        scaledScan();
        Check(scaledPrey.GetComponent<WebCapture>() != null, "scaled strands and broad phase capture at visual contact");
        CaptureViews(first);
        Object.Destroy(spider.gameObject);
        yield return null;
        Check(first != null && second != null, "webs outlive their spider");
    }

    private static void CaptureViews(SkyWeb web)
    {
        foreach (Transform child in web.GetComponentsInChildren<Transform>()) child.gameObject.layer = 30;
        var cameraObject = new GameObject("Web Validation Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.cullingMask = 1 << 30;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.88f, 0.9f, 0.92f);
        camera.fieldOfView = 48f;
        var target = new RenderTexture(1200, 900, 24);
        var texture = new Texture2D(1200, 900, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            Vector3[] angles = { new Vector3(11f, 6f, -11f), new Vector3(-10f, 9f, 10f), new Vector3(0f, 16f, 4f) };
            for (int i = 0; i < angles.Length; i++)
            {
                camera.transform.position = web.transform.position + angles[i];
                camera.transform.LookAt(web.transform.position);
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0);
                texture.Apply();
                File.WriteAllBytes(OutputDirectory + "/web-view-" + i + ".png", texture.EncodeToPNG());
            }
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            target.Release();
            Object.Destroy(target); Object.Destroy(texture); Object.Destroy(cameraObject);
        }
    }
}
