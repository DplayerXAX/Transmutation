using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Wind wall physics checks with a scripted player in an empty, unsaved scene (the open scene is reopened
/// afterwards; nothing is saved). Menu: Tools > Capstone > Wind > Run Wind Validation. Results go to the
/// console and Logs/WindValidation.txt.
/// </summary>
public static class WindValidation
{
    private const string Pending = "WindValidation.Pending";
    private const string ReturnScene = "WindValidation.ReturnScene";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly StringBuilder log = new StringBuilder();
    private static int checks;

    [InitializeOnLoadMethod]
    private static void Install() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

    [MenuItem("Tools/Capstone/Wind/Run Wind Validation (Empty Scene)")]
    public static void RunFromMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("Wind validation: stop Play mode first."); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        SessionState.SetString(ReturnScene, EditorSceneManager.GetActiveScene().path);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update += Run;
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            string back = SessionState.GetString(ReturnScene, "");
            if (string.IsNullOrEmpty(back)) return;
            SessionState.EraseString(ReturnScene);
            EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
        }
    }

    private static void Check(bool condition, string message)
    {
        log.AppendLine((condition ? "PASS  " : "FAIL  ") + message);
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }

    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static object Call(object target, string name, params object[] args) =>
        target.GetType().GetMethod(name, Private).Invoke(target, args);

    private static SmoothFirstPersonController Player(Vector3 position)
    {
        var root = new GameObject("Wind validation player");
        root.transform.position = position;
        var body = root.AddComponent<Rigidbody>();
        body.mass = 1f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.height = 2f;
        capsule.radius = 0.35f;
        var player = root.AddComponent<SmoothFirstPersonController>();
        var orientation = new GameObject("Orientation").transform;
        orientation.SetParent(root.transform, false);
        Set(player, "orientation", orientation);
        Set(player, "whatIsGround", (LayerMask)(1 << 7));
        // The PlayerObjects prefab's ground check (the script defaults start the cast inside the floor).
        Set(player, "groundCheckDistance", 0.5f);
        Set(player, "groundCheckOriginYOffset", 0.2f);
        Call(player, "Start");
        return player;
    }

    /// <summary>One frame: the Update half (speed limit) and the FixedUpdate half, as the real loops run them.</summary>
    private static void Step(SmoothFirstPersonController player, Vector2 input, float seconds, WindArea[] areas, params Rigidbody[] loose)
    {
        int steps = Mathf.RoundToInt(seconds / Time.fixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            Physics.SyncTransforms();
            Set(player, "horizontalInput", input.x);
            Set(player, "verticalInput", input.y);
            var wind = (Vector3)Call(player, "StripWind");
            Call(player, "GroundCheck");
            Call(player, "StateHandler");
            Call(player, "SpeedControl");
            Call(player, "RestoreWind", wind);
            Call(player, "FixedUpdate");
            foreach (var area in areas) Call(area, "FixedUpdate");
            foreach (var body in loose)
                if (body.TryGetComponent(out WindBody windBody) && windBody.enabled) Call(windBody, "FixedUpdate");
            Physics.Simulate(Time.fixedDeltaTime);
        }
    }

    private static void Place(SmoothFirstPersonController player, float z)
    {
        player.Body.position = new Vector3(0f, 1.02f, z);
        player.transform.position = player.Body.position;
        player.Body.linearVelocity = Vector3.zero;
        Physics.SyncTransforms();
    }

    private static WindArea Area(string name, Vector3 position, Vector3 flow, Vector3 size, float speed, float grip)
    {
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(flow));
        var area = go.AddComponent<WindArea>();
        area.Size = size;
        area.Speed = speed;
        area.Grip = grip;
        return area;
    }

    private static void Run()
    {
        EditorApplication.update -= Run;
        SimulationMode oldMode = Physics.simulationMode;
        float oldStep = Time.fixedDeltaTime;
        log.Clear();
        checks = 0;
        bool passed = false;
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            Time.fixedDeltaTime = 0.02f;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.layer = 7;
            floor.transform.position = new Vector3(0f, -0.25f, 0f);
            floor.transform.localScale = new Vector3(200f, 0.5f, 200f);

            // Wall spans z 5..7 and blows back toward -Z at 6.5 m/s: between walking (5.25) and sprinting (7.5),
            // the PlayerObjects speeds (walk 7 / sprint 10 x moveSpeedScale 0.75).
            WindArea wall = Area("Wind wall", new Vector3(0f, 1.5f, 6f), Vector3.back, new Vector3(30f, 4f, 2f), 6.5f, 12f);
            var areas = new[] { wall };
            var player = Player(new Vector3(0f, 1.02f, 0f));

            Step(player, Vector2.up, 4f, areas);
            float walkZ = player.Body.position.z;
            Check(walkZ < 6f, $"walking (5.25 m/s) into a 6.5 m/s wall is held at its edge (z={walkZ:F2}, wall 5..7)");

            Set(player, "walkSpeed", 10f); // the sprint speed, without a key press
            Place(player, 0f);
            Step(player, Vector2.up, 5f, areas);
            float sprintZ = player.Body.position.z;
            Check(sprintZ > 7.2f, $"sprinting (7.5 m/s) pushes through the wall (z={sprintZ:F2})");

            Set(player, "walkSpeed", 7f);
            Place(player, 6.2f);
            float peakPush = 0f;
            for (int t = 0; t < 15; t++)
            {
                Step(player, Vector2.zero, 0.02f, areas);
                peakPush = Mathf.Min(peakPush, player.Body.linearVelocity.z);
            }
            Check(peakPush < -5f, $"standing mid-wall is pushed back out (peak vz={peakPush:F2} of -6.5)");
            Step(player, Vector2.zero, 1.5f, areas);
            Check(player.Body.position.z < 5.2f, $"and ends up outside the wall (z={player.Body.position.z:F2})");

            Place(player, 6f);
            float sideways = 0f, carried = 0f;
            for (int t = 0; t < 20; t++)
            {
                Step(player, Vector2.right, 0.02f, areas);
                sideways = Mathf.Max(sideways, player.Body.linearVelocity.x);
                carried = Mathf.Min(carried, player.Body.linearVelocity.z);
            }
            Check(Mathf.Abs(sideways - 5.25f) < 0.4f, $"walking across the wind keeps full sideways speed (vx={sideways:F2} of 5.25)");
            Check(carried < -5f, $"while being carried along the wind (peak vz={carried:F2})");

            Place(player, 6f);
            PlayerCreatureStats.EnsureFor(player).WindResistance = 1f;
            Step(player, Vector2.zero, 0.5f, areas);
            Check(Mathf.Abs(player.Body.linearVelocity.z) < 0.2f, $"full resistance on the player ignores the wind (vz={player.Body.linearVelocity.z:F2})");
            PlayerCreatureStats.EnsureFor(player).WindResistance = 0f;

            // Passive bodies in a soft breeze far from the player: one free, one half resistant.
            // Bottom face sunk below the floor, so ground-hugging bodies are not in the edge fade.
            WindArea breeze = Area("Breeze", new Vector3(40f, 1.5f, 0f), Vector3.right, new Vector3(20f, 4f, 20f), 4f, 3f);
            areas = new[] { wall, breeze };
            Rigidbody Box(float x, float z)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.transform.position = new Vector3(x, 0.6f, z);
                box.transform.localScale = Vector3.one * 0.5f;
                var body = box.AddComponent<Rigidbody>();
                box.GetComponent<Collider>().material = new PhysicsMaterial { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
                return body;
            }
            Rigidbody free = Box(35f, -3f), half = Box(35f, 3f);
            half.gameObject.AddComponent<WindResistance>().Value = 0.5f;
            Place(player, 0f);
            Step(player, Vector2.zero, 2f, areas, free, half);
            Check(free.linearVelocity.x > 3.3f, $"a loose box settles near the breeze speed (vx={free.linearVelocity.x:F2} of 4)");
            Check(Mathf.Abs(half.linearVelocity.x - 2f) < 0.6f, $"a 50% resistant box takes half (vx={half.linearVelocity.x:F2} of 2)");

            Object.DestroyImmediate(breeze.gameObject);
            Step(player, Vector2.zero, 0.5f, new[] { wall }, free, half);
            Check(!free.GetComponent<WindBody>().enabled, "the wind state switches itself off once a body is out of every area");
            passed = true;
        }
        catch (Exception exception)
        {
            Exception inner = exception is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : exception;
            log.AppendLine("ERROR " + inner);
        }
        finally
        {
            Physics.simulationMode = oldMode;
            Time.fixedDeltaTime = oldStep;
            string summary = (passed ? "WIND VALIDATION PASSED" : "WIND VALIDATION FAILED") + $" ({checks} checks)";
            log.Insert(0, summary + "\n");
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/WindValidation.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, log.ToString());
            if (passed) Debug.Log(log.ToString()); else Debug.LogError(log.ToString());
            EditorApplication.isPlaying = false;
        }
    }
}
