using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Disposable-project physics integration checks: real contacts, slopes, momentum and crest flight.</summary>
public static class SkateShoesValidation
{
    private const string Pending = "SkateShoesValidation.Pending";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static string ResultPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../../skate-results.txt"));

    [InitializeOnLoadMethod]
    private static void Install() => EditorApplication.playModeStateChanged += OnPlay;

    public static void RunBatch()
    {
        if (!Application.dataPath.Replace('\\', '/').Contains("/.utmp/GlowBeeChecks/UnityProject/Assets"))
            throw new InvalidOperationException("Run these checks in the disposable Unity project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true);
        EditorApplication.isPlaying = true;
    }

    private static void OnPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        EditorApplication.update += Run;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++;
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    private static object Call(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);

    private static SmoothFirstPersonController Player(Vector3 position)
    {
        var root = new GameObject("Skate validation player");
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
        Set(player, "spawnSkateShoes", false);
        Call(player, "Start");
        return player;
    }

    private static void Step(SmoothFirstPersonController player, Vector2 input, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            Physics.SyncTransforms();
            Set(player, "horizontalInput", input.x);
            Set(player, "verticalInput", input.y);
            Call(player, "GroundCheck");
            Call(player, "StateHandler");
            Call(player, "FixedUpdate");
            Call(player, "SpeedControl");
            Physics.Simulate(Time.fixedDeltaTime);
        }
    }

    private static void Run()
    {
        EditorApplication.update -= Run;
        SimulationMode oldMode = Physics.simulationMode;
        float oldStep = Time.fixedDeltaTime;
        int exit = 0;
        string result;
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            Time.fixedDeltaTime = 0.02f;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Skate validation floor";
            floor.layer = 7;
            floor.transform.position = new Vector3(0f, -0.25f, 0f);
            floor.transform.localScale = new Vector3(200f, 0.5f, 200f);
            var player = Player(new Vector3(0f, 1.02f, 0f));
            Step(player, Vector2.up, 100);
            float walking = new Vector2(player.Body.linearVelocity.x, player.Body.linearVelocity.z).magnitude;
            Step(player, Vector2.zero, 25);
            float normalStop = player.Body.linearVelocity.magnitude;
            var originalMaterial = player.GetComponent<Collider>().sharedMaterial;
            var shoes = player.gameObject.AddComponent<PlayerSkateShoes>();
            shoes.Equip();
            Step(player, Vector2.up, 100);
            float skating = player.Body.linearVelocity.magnitude;
            Check(skating > walking * 1.5f, "skates must increase powered cruising speed: " + walking + " -> " + skating);
            Check(player.GetComponent<Collider>().sharedMaterial.dynamicFriction < 0.02f, "equipping replaces physical friction");
            Step(player, Vector2.zero, 25);
            Check(player.Body.linearVelocity.magnitude > skating * 0.85f && normalStop < 0.5f,
                "skates coast after release while ordinary shoes stop");
            float beforeBrake = player.Body.linearVelocity.magnitude;
            Step(player, Vector2.down, 15);
            Check(player.Body.linearVelocity.magnitude < beforeBrake - 3f, "opposite input brakes deliberately");
            player.Body.linearVelocity = Vector3.forward * 12f;
            Step(player, Vector2.right, 1);
            Check(player.Body.linearVelocity.z > 10f && Mathf.Abs(player.Body.linearVelocity.x) < 1f,
                "steering retains inertia instead of instantly snapping velocity sideways");
            player.SetRestricted(true);
            Check(!shoes.CanDrive, "restricted movement retains ownership of the player");
            player.SetRestricted(false);
            player.SetClimbing(true);
            Check(!shoes.CanDrive, "hand climbing suspends skating");
            player.SetClimbing(false);
            player.Body.isKinematic = true;
            Check(!shoes.CanDrive, "equipment does not drive a kinematic body");
            player.Body.isKinematic = false;

            player.Body.linearVelocity = new Vector3(5f, 10f, 0f);
            Set(player, "exitingSlope", true);
            Set(player, "ownJumpRising", true);
            Step(player, Vector2.zero, 1);
            Check(player.Body.linearVelocity.y > 8f, "ordinary jumping remains possible with shoes");
            Set(player, "exitingSlope", false);
            Set(player, "ownJumpRising", false);
            shoes.SetEquipped(false);
            Check(player.GetComponent<Collider>().sharedMaterial == originalMaterial, "taking shoes off restores original physics material");
            player.Body.position = new Vector3(0f, 1.02f, 0f);
            player.Body.linearVelocity = Vector3.zero;
            Step(player, Vector2.up, 100);
            Check(player.Body.linearVelocity.magnitude < walking + 0.2f, "normal movement returns when unequipped");
            Object.DestroyImmediate(player.gameObject);

            // A continuous V-shaped valley leading to a convex uphill crest.
            Mesh mesh = RampMesh();
            var ramp = new GameObject("Downhill - valley - uphill - crest") { layer = 7 };
            ramp.AddComponent<MeshCollider>().sharedMesh = mesh;
            player = Player(new Vector3(0f, 6.55f, -17f));
            shoes = player.gameObject.AddComponent<PlayerSkateShoes>();
            shoes.Equip();
            player.Body.linearVelocity = new Vector3(0f, -7f / 22f, 1f).normalized * 12f;
            float peakSpeed = 0f, airbornePeak = 0f;
            bool uphill = false, flight = false;
            for (int i = 0; i < 250; i++)
            {
                Step(player, Vector2.zero, 1);
                Vector3 position = player.Body.position;
                peakSpeed = Mathf.Max(peakSpeed, player.Body.linearVelocity.magnitude);
                uphill |= position.z > 1f && player.Body.linearVelocity.y > 1f;
                if (position.z > 12f && !player.Grounded && player.Body.linearVelocity.y > 0.5f)
                { flight = true; airbornePeak = Mathf.Max(airbornePeak, position.y - 1f); }
            }
            Check(peakSpeed > 14f, "downhill gravity builds speed without input: " + peakSpeed);
            Check(uphill, "downhill momentum carries the player back uphill");
            Check(shoes.RampTakeoffs > 0 && flight, "crest creates an actual airborne phase without a jump press");
            Check(airbornePeak > 5.3f, "ramp takeoff rises above the five metre crest: " + airbornePeak);
            shoes.enabled = false;
            Check(player.GetComponent<Collider>().sharedMaterial == null, "disabling equipment restores collider material");
            Object.DestroyImmediate(player.gameObject);
            Object.DestroyImmediate(ramp);
            Object.DestroyImmediate(mesh);

            // Use the real aim/interaction path, including solid occlusion.
            player = Player(new Vector3(30f, 1.02f, 0f));
            var camera = new GameObject("Skate pickup camera").AddComponent<Camera>();
            camera.transform.position = player.transform.position;
            var carrier = player.gameObject.AddComponent<PlayerCreatureCarrier>();
            var interactor = player.gameObject.AddComponent<PlayerCreatureInteractor>();
            Set(interactor, "interactionCamera", camera);
            var pickupRoot = new GameObject("Skate shoes pickup");
            pickupRoot.transform.position = camera.transform.position + Vector3.forward * 2f;
            var pickup = pickupRoot.AddComponent<SkateShoesPickup>();
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.transform.position = camera.transform.position + Vector3.forward;
            Physics.SyncTransforms();
            Check(interactor.FindSkateShoes() == null, "pickup cannot be used through a solid wall");
            Object.DestroyImmediate(blocker);
            Physics.SyncTransforms();
            Check(interactor.FindSkateShoes() == pickup, "equipment is discoverable with the crosshair");
            interactor.TryInteract();
            Check(player.GetComponent<PlayerSkateShoes>().Equipped && !pickupRoot.activeSelf,
                "left-click wears shoes and consumes the pickup once");
            Check(!pickup.TryEquip(player), "the same pair cannot be claimed twice");
            Object.DestroyImmediate(pickupRoot);

            var offerRoot = new GameObject("Delayed spawn shoes");
            var offer = offerRoot.AddComponent<SkateShoesPickup>();
            Set(player, "grounded", false);
            var placement = (IEnumerator)typeof(SkateShoesPickup).GetMethod("PlaceNear", Private)
                .Invoke(offer, new object[] { player });
            Check(placement.MoveNext() && !offer.GetComponent<Collider>().enabled,
                "starting shoes wait for generated ground before becoming visible or solid");
            player.Body.position = new Vector3(50f, 1.02f, 0f);
            Set(player, "grounded", true);
            Physics.SyncTransforms();
            Check(!placement.MoveNext() && offer.GetComponent<Collider>().enabled &&
                Vector3.Distance(offer.transform.position, player.Body.position) < 4f,
                "starting offer follows procedural spawn relocation and rests near the real spawn");
            Object.DestroyImmediate(offerRoot);
            Object.DestroyImmediate(player.gameObject);
            Object.DestroyImmediate(camera.gameObject);
            Object.DestroyImmediate(floor);
            result = "PASS: " + checks + " skate shoe physics and interaction checks. Cruise " + walking.ToString("F2") +
                " -> " + skating.ToString("F2") + " m/s; downhill peak " + peakSpeed.ToString("F2") +
                " m/s; airborne height " + airbornePeak.ToString("F2") + " m above the valley.";
        }
        catch (Exception exception)
        {
            exit = 1;
            result = "FAIL after " + checks + " checks: " + exception;
            Debug.LogException(exception);
        }
        finally { Physics.simulationMode = oldMode; Time.fixedDeltaTime = oldStep; }
        File.WriteAllText(ResultPath, result);
        Debug.Log(result);
        EditorApplication.Exit(exit);
    }

    private static Mesh RampMesh()
    {
        var profile = new Vector2[] { new Vector2(-22f, 7f), new Vector2(0f, 0f), new Vector2(12f, 5f), new Vector2(35f, 5f) };
        var vertices = new Vector3[profile.Length * 2];
        var triangles = new int[(profile.Length - 1) * 6];
        for (int i = 0; i < profile.Length; i++)
        {
            vertices[i * 2] = new Vector3(-8f, profile[i].y, profile[i].x);
            vertices[i * 2 + 1] = new Vector3(8f, profile[i].y, profile[i].x);
            if (i == profile.Length - 1) continue;
            int t = i * 6, v = i * 2;
            triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        var mesh = new Mesh { name = "Skate validation valley" };
        mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals();
        return mesh;
    }
}
