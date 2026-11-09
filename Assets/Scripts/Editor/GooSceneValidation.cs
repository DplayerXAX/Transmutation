using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit geometry checks, disposable live fixtures, and repeatable views of the authored level.</summary>
public static class GooSceneValidation
{
    public const string Output = "Temp/GooSceneValidation";
    public static string RuntimeStatus { get; private set; } = "Not run";
    public static string TraversalStatus { get; private set; } = "Not run";
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Scene Goo: " + message);
    }

    [MenuItem("Tools/Capstone/Goo/Validate Level Geometry")]
    public static string ValidateLevel()
    {
        var scene = SceneManager.GetActiveScene();
        Require(scene.path == GooSceneSetup.ScenePath, "Open Scene Goo.");
        var world = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ProceduralWorld>()).Single();
        world.Generate(true);
        Physics.SyncTransforms();
        Require(world.UsingSculptedTerrain && world.SculptData.Edits.Count > 0, "Terrain must retain saved sculpt data.");
        var creatures = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Creature>()).ToArray();
        Require(creatures.OfType<CompostCreature>().Count() == 2, "Two Recycle creatures.");
        Require(creatures.OfType<SkySpider>().Count() == 1, "One canopy spider.");
        Require(creatures.OfType<CeilingBatBug>().Count() == 36, "36 bugs in the completed second milestone.");
        var controller = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SmoothFirstPersonController>()).Single();
        Require(new SerializedObject(controller).FindProperty("maxSlopeAngle").floatValue == 60f, "Scene walking slope is 60 degrees.");
        foreach (float z in new[] { -72f, -60f, -42f, -22f })
        {
            var hit = GooSceneSetup.FindTerrainHit(world, new Vector3(0f, 20f, z), Vector3.down, 50f);
            Require(Mathf.Abs(hit.point.y - GooSceneSetup.CanyonFloor(z)) < 1f, "Open canyon floor at " + z);
            Require(!Physics.Raycast(hit.point + Vector3.up * 2f, Vector3.up, 35f, 1 << 7), "Canyon stays open to sky.");
        }
        foreach (float z in new[] { -10f, 4f, 16f, 25f, 35f, 46f, 55f })
        {
            float expected = GooSceneSetup.CaveFloor(z);
            var floor = GooSceneSetup.FindTerrainHit(world, new Vector3(GooSceneSetup.CaveCentre(z), expected + 4f, z), Vector3.down, 7f);
            Require(Mathf.Abs(floor.point.y - expected) < 2f, "Connected cave floor at " + z);
            if (z > -7f && z < 48f) GooSceneSetup.FindTerrainHit(world, floor.point + Vector3.up * 2f, Vector3.up, 45f);
        }
        var gate = GooSceneSetup.FindTerrainHit(world, new Vector3(GooSceneSetup.CaveCentre(32f), -12f, 27f), Vector3.forward, 12f);
        Require(Vector3.Angle(gate.normal, Vector3.up) > 60f, "Upper gate exceeds walking limit.");
        foreach (var point in new[] { new Vector3(-14f, -13f, 0f), new Vector3(16f, -13f, 18f) })
        {
            Require(world.SampleDensity(point) < 0f, "Side chamber is open.");
            GooSceneSetup.FindTerrainHit(world, point, Vector3.down, 10f);
        }
        var crater = GooSceneSetup.FindTerrainHit(world, new Vector3(4f, 40f, 64f), Vector3.down, 55f);
        Require(Mathf.Abs(crater.point.y + 9f) < 0.5f, "Crater base.");
        Require(!Physics.Raycast(crater.point + Vector3.up * 2f, Vector3.up, 60f, 1 << 7), "Crater opens to sky.");
        foreach (float y in new[] { -6f, 0f, 7f, 14f, 25f })
            GooSceneSetup.FindTerrainHit(world, new Vector3(4f, y, 64f), Vector3.right, 30f);
        foreach (Vector3 point in new[] { new Vector3(30f, 45f, 64f), new Vector3(4f, 45f, 92f) })
        {
            var flank = GooSceneSetup.FindTerrainHit(world, point, Vector3.down, 55f);
            Require(Vector3.Angle(flank.normal, Vector3.up) < 60f, "Exterior has a walkable mountain approach.");
        }
        var cliff = GooSceneSetup.FindTerrainHit(world, new Vector3(-20f, 45f, 64f), Vector3.down, 55f);
        Require(Vector3.Angle(cliff.normal, Vector3.up) > 60f, "Exterior includes a steep rocky face.");
        string result = "PASS: saved sculpt data, three connected spaces, open canyon/crater, covered cave, steep gate, 36 bugs, two Recycle creatures, 60-degree walking.";
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/geometry.txt", result);
        Debug.Log(result);
        return result;
    }

    [MenuItem("Tools/Capstone/Goo/Capture Review Views")]
    public static void CaptureViews()
    {
        Directory.CreateDirectory(Output);
        var world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world != null && !world.LayoutReady) world.Generate(true);
        Capture("overview", new Vector3(60f, 100f, -95f), new Vector3(0f, -2f, 12f));
        Capture("entrance", new Vector3(0f, 5f, -81f), new Vector3(0f, -3f, -39f));
        Capture("shell", new Vector3(38f, 31f, -101f), new Vector3(0f, 11f, -44f));
        Capture("cavern", new Vector3(-5f, -10f, -4f), new Vector3(1f, -11f, 15f));
        Capture("gate", new Vector3(8f, -11f, 23f), new Vector3(3f, -8f, 34f));
        Capture("wayfinding", new Vector3(2f, -14.8f, 12f), new Vector3(6f, 0f, 39f));
        Capture("crater", new Vector3(4f, -5f, 61f), new Vector3(4f, 14f, 67f));
    }

    private static void Capture(string name, Vector3 from, Vector3 toward)
    {
        var go = new GameObject("Goo review camera") { hideFlags = HideFlags.HideAndDontSave };
        var rt = new RenderTexture(1280, 800, 24);
        var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            var camera = go.AddComponent<Camera>(); camera.enabled = false;
            camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(toward - from));
            camera.nearClipPlane = 0.05f; camera.farClipPlane = 450f; camera.fieldOfView = 65f;
            if (name == "overview" || name == "shell") { camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.22f, 0.25f, 0.28f); }
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); image.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            Object.DestroyImmediate(go); Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
        }
    }

    [MenuItem("Tools/Capstone/Goo/Validate Bugs And Dead Web Filtering (Play Mode)")]
    public static async void RunRuntimeMenu()
    {
        try { await RunRuntime(); }
        catch (Exception e) { Debug.LogException(e); }
    }

    public static async Task<string> RunRuntime()
    {
        Require(Application.isPlaying, "Enter Play Mode for runtime validation.");
        RuntimeStatus = "Running";
        Scene scene = SceneManager.CreateScene("Goo disposable validation " + Guid.NewGuid());
        var root = new GameObject("Goo validation fixtures"); SceneManager.MoveGameObjectToScene(root, scene);
        Vector3 at = new Vector3(2000f, 0f, 2000f);
        try
        {
            Box(root.transform, at + new Vector3(0f, -0.25f, 0f), new Vector3(8f, 0.5f, 8f));
            Box(root.transform, at + new Vector3(0f, 2f, 3f), new Vector3(8f, 4f, 0.5f));
            Box(root.transform, at + new Vector3(0f, 4.25f, 0f), new Vector3(8f, 0.5f, 8f));
            var bugObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GooSceneSetup.BugPath), root.transform);
            bugObject.transform.SetPositionAndRotation(at + new Vector3(0f, 0.22f, 0f), Quaternion.identity);
            var bug = bugObject.GetComponent<CeilingBatBug>();
            var motor = bugObject.GetComponent<CeilingBatBugSurfaceMotor>();
            typeof(CeilingBatBugSurfaceMotor).GetField("home", BindingFlags).SetValue(motor, bugObject.transform.position);
            typeof(CeilingBatBugSurfaceMotor).GetField("nextTurn", BindingFlags).SetValue(motor, Time.time + 1000f);
            Physics.SyncTransforms();
            bool floor = false, wall = false, ceiling = false;
            float start = Time.time, deadline = Time.realtimeSinceStartup + 25f;
            while (Time.time - start < 11f && Time.realtimeSinceStartup < deadline)
            {
                Vector3 normal = motor.SurfaceNormal;
                floor |= motor.Attached && normal.y > 0.8f;
                wall |= motor.Attached && Mathf.Abs(normal.y) < 0.2f;
                ceiling |= motor.Attached && normal.y < -0.8f;
                if (floor && wall && ceiling) break;
                await Task.Yield();
            }
            Require(floor && wall && ceiling, "Autonomous floor → wall → ceiling traversal: " + floor + "/" + wall + "/" + ceiling);
            Require(!bug.CanUseWhileCarried && bug.CanBeCarried, "Carryable without held use.");
            typeof(Creature).GetMethod("SetRemainingAge", BindingFlags).Invoke(bug, new object[] { 0f });
            Require(bug.IsDead && !motor.enabled && bugObject.GetComponent<Rigidbody>().useGravity, "Age death releases adhesion and retains a falling corpse.");

            var webObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/SkyWeb.prefab"), root.transform);
            webObject.transform.position = at + Vector3.right * 20f;
            var web = webObject.GetComponent<SkyWeb>();
            var body = bugObject.GetComponent<Rigidbody>();
            Require(!web.IsEligible(body) && !web.TryCapture(body, body.position), "Dead bugs cannot be captured.");
            WebCapture direct = bugObject.AddComponent<WebCapture>();
            Require(!direct.Attach(web, body, body.position), "Direct attachment rejects dead creatures.");
            var livingObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GooSceneSetup.BugPath), root.transform);
            livingObject.transform.position = at + Vector3.right * 20f;
            var living = livingObject.GetComponent<CeilingBatBug>();
            Require(web.TryCapture(livingObject.GetComponent<Rigidbody>(), livingObject.transform.position), "Living prey still captures.");
            living.Die();
            Require(!livingObject.GetComponent<WebCapture>().IsCaptured, "Death releases the existing capture.");
            var bones = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/BirdBones.prefab"), root.transform);
            Require(bones.GetComponent<BirdBones>().IsDead && !web.IsEligible(bones.GetComponent<Rigidbody>()), "Bird bones permanently bypass silk.");
            RuntimeStatus = "PASS: floor/wall/ceiling crawling, carry contract, age death, falling corpse, dead and bones rejection, direct attachment guard, living capture and death release.";
            Directory.CreateDirectory(Output); File.WriteAllText(Output + "/runtime.txt", RuntimeStatus);
            return RuntimeStatus;
        }
        catch (Exception e) { RuntimeStatus = "FAIL: " + e.Message; throw; }
        finally { Object.Destroy(root); SceneManager.UnloadSceneAsync(scene); }
    }
    private const System.Reflection.BindingFlags BindingFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

    public static async void RunTraversalMenu()
    {
        try { await RunTraversal(); }
        catch (Exception e) { Debug.LogException(e); }
    }

    public static async Task<string> RunTraversal()
    {
        Require(Application.isPlaying, "Enter Play Mode for traversal checks.");
        TraversalStatus = "Running";
        var controller = Object.FindFirstObjectByType<SmoothFirstPersonController>();
        var climber = Object.FindFirstObjectByType<HandClimber>();
        var coating = controller.GetComponent<PlayerGooCoating>();
        var body = controller.GetComponent<Rigidbody>();
        Vector3 previous = body.position;
        float supply = coating.RemainingSeconds;
        var setSupply = typeof(PlayerGooCoating).GetMethod("SetRemaining", BindingFlags);
        var lines = new List<string>();
        try
        {
            Require(Object.FindObjectsByType<GooPaint>(FindObjectsSortMode.None).Length >= 2, "Both real tutorial drops created paint.");
            setSupply.Invoke(coating, new object[] { 0f });
            climber.StopForBubbleCapture();
            body.position = new Vector3(GooSceneSetup.CaveCentre(31.5f), GooSceneSetup.GateFloor(31.5f) + 1.02f, 31.5f);
            body.linearVelocity = Vector3.zero;
            controller.SetLook(0f, 0f);
            climber.ScriptedInput = Vector2.up;
            float start = Time.time, deadline = Time.realtimeSinceStartup + 25f;
            bool climbed = false;
            while (Time.time - start < 18f && Time.realtimeSinceStartup < deadline)
            {
                climbed |= climber.IsClimbing;
                if (body.position.z > 32.9f && body.position.y - 1f > -9.5f) break;
                if (!climber.IsClimbing && !climber.IsMantling) body.linearVelocity = new Vector3(0f, body.linearVelocity.y, 2.5f);
                await Task.Delay(15);
            }
            Require(climbed && body.position.z > 32.9f && body.position.y - 1f > -9.5f,
                "Painted gate traversal at " + body.position + ", last event " + climber.LastEvent);
            Require(coating.RemainingSeconds == 0f, "Painted gate needs no personal Goo supply.");
            lines.Add("PASS: painted gate crossed without personal Goo in " + (Time.time - start).ToString("0.0") + "s.");

            climber.StopForBubbleCapture();
            await Task.Delay(100);
            body.position = new Vector3(4f + GooSceneSetup.ShaftRadius(-7f, 0f) - 0.65f, -7.95f, 64f);
            body.linearVelocity = Vector3.zero;
            controller.SetLook(90f, 0f);
            setSupply.Invoke(coating, new object[] { 0f });
            coating.TryApplyGoo();
            coating.TryApplyGoo();
            climber.ScriptedInput = Vector2.up;
            start = Time.time; deadline = Time.realtimeSinceStartup + 55f;
            float highest = body.position.y;
            while (Time.time - start < 40f && Time.realtimeSinceStartup < deadline)
            {
                highest = Mathf.Max(highest, body.position.y);
                if (body.position.y - 1f > 30.5f && body.position.x > 18f) break;
                if (!climber.IsClimbing && !climber.IsMantling) body.linearVelocity = new Vector3(2.5f, body.linearVelocity.y, 0f);
                await Task.Delay(15);
            }
            Require(body.position.y - 1f > 30.5f && body.position.x > 18f,
                "Crater ascent on full supply; highest feet " + (highest - 1f).ToString("0.0") + ", position " + body.position + ", Goo " + coating.RemainingSeconds + ", last event " + climber.LastEvent);
            lines.Add("PASS: crater exit reached from full 30s supply, " + coating.RemainingSeconds.ToString("0.0") + "s remaining.");
            TraversalStatus = string.Join("\n", lines);
            File.WriteAllText(Output + "/traversal.txt", TraversalStatus);
            return TraversalStatus;
        }
        catch (Exception e) { TraversalStatus = string.Join("\n", lines) + "\nFAIL: " + e.Message; throw; }
        finally
        {
            climber.ScriptedInput = null;
            climber.StopForBubbleCapture();
            body.position = previous;
            if (!body.isKinematic) body.linearVelocity = Vector3.zero;
            setSupply.Invoke(coating, new object[] { supply });
            controller.SetLook(0f, 0f);
        }
    }
    private static void Box(Transform parent, Vector3 position, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.SetParent(parent, false);
        go.transform.position = position; go.transform.localScale = size; go.layer = 7;
    }
}
