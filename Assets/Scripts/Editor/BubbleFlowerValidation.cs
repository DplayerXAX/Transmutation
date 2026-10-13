using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

/// <summary>Prototype checks in an unsaved local physics scene; authored scenes are never changed.</summary>
public static class BubbleFlowerValidation
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static int checks;
    private static Scene scene;
    private static Transform staging;
    private static readonly Vector3 Origin = new Vector3(1000f, 1000f, 1000f);
    public static string Status { get; private set; } = "Not run";

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("Bubble Flower validation: " + description);
        checks++;
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void Call(object target, string method, params object[] arguments) => target.GetType().GetMethod(method, Private).Invoke(target, arguments);

    [MenuItem("Tools/Creatures/Validate Bubble Flower Assets")]
    private static void ValidateAssetsMenu() => Debug.Log(ValidateAssets());
    public static string ValidateAssets()
    {
        checks = 0;
        GameObject flower = AssetDatabase.LoadAssetAtPath<GameObject>(BubbleFlowerSetup.FlowerPath);
        GameObject bubble = AssetDatabase.LoadAssetAtPath<GameObject>(BubbleFlowerSetup.BubblePath);
        Check(flower != null && bubble != null, "both prefabs exist");
        Check(flower.GetComponent<BubbleFlower>().CanUseWhileCarried, "flower opts into carried use");
        Check(flower.transform.Find("Muzzle") != null, "flower has a muzzle");
        Check(!bubble.GetComponent<TrappingBubble>().CanBeCarried, "bubbles cannot be carried");
        Check(!bubble.GetComponent<TrappingBubble>().RequiresExplicitInteraction, "contact popping is enabled by default");
        Check(bubble.GetComponent<Rigidbody>().collisionDetectionMode == CollisionDetectionMode.ContinuousDynamic, "continuous collision detection");
        Check(!bubble.GetComponent<SphereCollider>().isTrigger && bubble.transform.Find("CaptureTrigger").GetComponent<SphereCollider>().isTrigger,
            "solid shell and separate capture trigger");
        Material material = bubble.transform.Find("Shell").GetComponent<Renderer>().sharedMaterial;
        Check(material.GetFloat("_Surface") == 1f && material.GetColor("_BaseColor").a < 0.3f, "translucent bubble material");
        Check(new SerializedObject(flower.GetComponent<BubbleFlower>()).FindProperty("bubblePrefab").objectReferenceValue != null, "flower references the trapping bubble");
        Status = "Bubble Flower assets passed: " + checks + " assertions.";
        Write("assets.txt", Status);
        return Status;
    }

    private static GameObject Spawn(GameObject prefab, Vector3 position)
    {
        GameObject instance = Object.Instantiate(prefab, position, Quaternion.identity, staging);
        instance.transform.SetParent(null, true);
        return instance;
    }
    private static TrappingBubble Bubble(Vector3 position)
    {
        var bubble = Spawn(AssetDatabase.LoadAssetAtPath<GameObject>(BubbleFlowerSetup.BubblePath), position).GetComponent<TrappingBubble>();
        return bubble;
    }
    private static Creature Prey(Vector3 position, Vector3 size)
    {
        GameObject root = new GameObject("Test Creature");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = position;
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.constraints = RigidbodyConstraints.FreezeRotation;
        GameObject model = GameObject.CreatePrimitive(PrimitiveType.Cube);
        model.transform.SetParent(root.transform, false); model.transform.localScale = size;
        CaveHarvestItem creature = root.AddComponent<CaveHarvestItem>();
        root.SetActive(true);
        body.isKinematic = false; body.useGravity = true;
        Physics.SyncTransforms();
        return creature;
    }
    private static void Step(TrappingBubble bubble, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            if (bubble != null && !bubble.IsPopped) bubble.SimulateStep(0.02f);
            scene.GetPhysicsScene().Simulate(0.02f);
        }
    }
    private static void Destroy(Object target) { if (target != null) Object.DestroyImmediate(target); }

    [MenuItem("Tools/Creatures/Validate Bubble Flower Runtime (Play Mode)")]
    private static void ValidateRuntimeMenu() => ValidateRuntime();
    public static string ValidateRuntime()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode before running bubble physics validation.");
        checks = 0;
        scene = SceneManager.CreateScene("Bubble Flower Validation", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        var stagingObject = new GameObject("Inactive Staging");
        SceneManager.MoveGameObjectToScene(stagingObject, scene);
        stagingObject.SetActive(false); staging = stagingObject.transform;
        try
        {
            ValidateCaptureAndCleanup();
            ValidateBounds();
            ValidateContactAndMotion();
            ValidatePlayerAndHeldUse();
            ValidatePickupInputs();
            ValidateWebHandoff();
            Status = "Bubble Flower runtime passed: " + checks + " assertions.";
            Write("runtime.txt", Status);
            Debug.Log(Status);
            return Status;
        }
        catch (Exception exception)
        {
            Status = "FAILED after " + checks + " assertions: " + exception;
            Write("runtime.txt", Status);
            throw;
        }
        finally { SceneManager.UnloadSceneAsync(scene); }
    }

    private static void ValidateCaptureAndCleanup()
    {
        foreach (float scale in new[] { 0.5f, 1f, 2f })
        {
            Creature prey = Prey(Origin, Vector3.one);
            prey.transform.localScale = Vector3.one * scale;
            GameObject extra = GameObject.CreatePrimitive(PrimitiveType.Cube);
            extra.transform.SetParent(prey.transform, false); extra.transform.localPosition = Vector3.right * 0.3f;
            extra.transform.localScale = Vector3.one * 0.2f;
            extra.GetComponent<Collider>().enabled = false;
            Physics.SyncTransforms();
            Rigidbody body = prey.GetComponent<Rigidbody>();
            Vector3 initialScale = prey.transform.localScale;
            TrappingBubble bubble = Bubble(Origin + Vector3.left * 2f);
            Check(bubble.TryCapture(prey.gameObject), "capture scaled compound creature " + scale);
            Check(bubble.IsOccupied && prey.IsBubbleCaptured && !prey.SimulationEnabled, "one occupant pauses creature logic");
            Check(body.isKinematic && !body.useGravity && !body.detectCollisions, "capture owns physics");
            Check(prey.transform.localScale == initialScale, "bubble fitting leaves creature scale unchanged");
            Creature another = Prey(Origin + Vector3.right * 8f, Vector3.one);
            Check(!bubble.TryCapture(another.gameObject), "second occupant rejected");
            Step(bubble, 5);
            Check(!bubble.IsPopped, "capture and initial overlap do not immediately pop bubble");
            bubble.ReceivePickupAttempt(null);
            Check(bubble.IsPopped && !prey.IsBubbleCaptured && prey.SimulationEnabled, "pickup pops occupied bubble and restores logic");
            Check(!body.isKinematic && body.useGravity && body.detectCollisions, "original physics restored");
            Check(!extra.GetComponent<Collider>().enabled && prey.GetComponentInChildren<Collider>().enabled, "collider enable states restored exactly");
            Check(!prey.GetComponent<BubbleCapture>().CanCapture, "release immunity prevents immediate recapture");
            Destroy(another.gameObject); Destroy(prey.gameObject); Destroy(bubble.gameObject);
        }
        Creature oversized = Prey(Origin, new Vector3(4.1f, 1f, 1f));
        TrappingBubble rejected = Bubble(Origin + Vector3.up * 10f);
        Check(!rejected.TryCapture(oversized.gameObject), "oversized model rejected");
        rejected.ReceivePickupAttempt(null);
        Check(rejected.IsPopped, "pickup also pops empty bubbles");
        Destroy(rejected.gameObject); Destroy(oversized.gameObject);

        Creature stationary = Prey(Origin, Vector3.one);
        stationary.SetSimulationEnabled(false);
        stationary.GetComponent<Rigidbody>().isKinematic = true;
        TrappingBubble paused = Bubble(Origin);
        Check(paused.TryCapture(stationary.gameObject), "stationary, already paused creature can be captured");
        paused.Pop();
        Check(!stationary.SimulationEnabled && stationary.GetComponent<Rigidbody>().isKinematic, "prior paused and stationary states restored");
        Destroy(paused.gameObject); Destroy(stationary.gameObject);

        foreach (bool disable in new[] { false, true })
        {
            Creature occupant = Prey(Origin, Vector3.one);
            TrappingBubble expiring = Bubble(Origin);
            Check(expiring.TryCapture(occupant.gameObject), "cleanup scenario captures occupant");
            if (disable) expiring.enabled = false;
            else { Set(expiring, "lifetimeSeconds", 0.05f); Step(expiring, 4); }
            Check(expiring.IsPopped && !occupant.IsBubbleCaptured && occupant.SimulationEnabled,
                disable ? "disable releases occupant" : "occupied timeout releases occupant");
            Destroy(expiring.gameObject); Destroy(occupant.gameObject);
        }

        Creature noBody = Prey(Origin, Vector3.one);
        Destroy(noBody.GetComponent<Rigidbody>());
        TrappingBubble transformBubble = Bubble(Origin);
        Check(transformBubble.TryCapture(noBody.gameObject), "creature without Rigidbody can be captured");
        transformBubble.Occupant.Follow(Origin + Vector3.up);
        Check(Vector3.Distance(noBody.transform.position, Origin + Vector3.up) < 0.01f, "transform-only occupant follows bubble");
        Destroy(transformBubble.gameObject);
        Check(!noBody.IsBubbleCaptured && noBody.GetComponent<Rigidbody>() == null, "destruction releases without introducing a Rigidbody");
        Destroy(noBody.gameObject);
    }

    private static void ValidateBounds()
    {
        Creature prey = Prey(Origin, Vector3.one);
        Creature nested = Prey(Origin + Vector3.right * 10f, Vector3.one * 8f);
        nested.transform.SetParent(prey.transform, true);
        GameObject effects = new GameObject("Effects", typeof(ParticleSystem), typeof(TrailRenderer), typeof(LineRenderer));
        effects.transform.SetParent(prey.transform, false);
        LineRenderer line = effects.GetComponent<LineRenderer>();
        line.positionCount = 2; line.SetPositions(new[] { Origin, Origin + Vector3.up * 20f });
        Physics.SyncTransforms();
        Check(CreatureBounds.TryMeasure(prey.transform, out Bounds visible) && CreatureBounds.LargestSide(visible) < 1.01f,
            "bounds exclude particles, trails, lines and independent nested creatures");
        prey.GetComponentInChildren<MeshRenderer>().enabled = false;
        Check(CreatureBounds.TryMeasure(prey.transform, out Bounds fallback) && CreatureBounds.LargestSide(fallback) < 1.01f,
            "solid colliders provide bounds when no visible model exists");
        Destroy(prey.gameObject);
    }

    private static void ValidateContactAndMotion()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(floor, scene);
        floor.transform.position = Origin - Vector3.up;
        floor.transform.localScale = new Vector3(4f, 1f, 4f);
        Creature groundedPrey = Prey(Origin, Vector3.one);
        TrappingBubble fitted = Bubble(Origin);
        Check(fitted.TryCapture(groundedPrey.gameObject), "capture next to a floor");
        Physics.SyncTransforms(); Step(fitted, 50);
        Check(!fitted.IsPopped && groundedPrey.IsBubbleCaptured, "expansion against scenery does not immediately pop the occupant");
        fitted.Pop();
        Destroy(fitted.gameObject); Destroy(groundedPrey.gameObject); Destroy(floor);

        foreach (bool explicitOnly in new[] { false, true })
        {
            Creature prey = Prey(Origin, Vector3.one);
            Creature other = Prey(Origin + Vector3.right * 8f, Vector3.one);
            TrappingBubble bubble = Bubble(Origin);
            bubble.RequiresExplicitInteraction = explicitOnly;
            Check(bubble.TryCapture(prey.gameObject), "contact scenario captures prey");
            Call(bubble, "HandleContact", other.GetComponentInChildren<Collider>(), false);
            Check(bubble.IsPopped == !explicitOnly, "creature touch follows explicit-interaction toggle");
            if (explicitOnly) bubble.ReceiveInteraction(new CreatureInteraction(other.gameObject, Origin, Vector3.forward));
            Check(bubble.IsPopped && !prey.IsBubbleCaptured, "explicit interaction releases occupant in either mode");
            Destroy(prey.gameObject); Destroy(other.gameObject); Destroy(bubble.gameObject);

            foreach (bool occupied in new[] { false, true })
            foreach (Vector3 direction in new[] { Vector3.forward, Vector3.up })
            {
                GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(wall, scene);
                wall.transform.position = Origin + direction * 1.5f;
                wall.transform.localScale = direction == Vector3.up ? new Vector3(4f, 0.2f, 4f) : new Vector3(4f, 4f, 0.2f);
                TrappingBubble projectile = Bubble(Origin);
                projectile.RequiresExplicitInteraction = explicitOnly;
                Creature passenger = occupied ? Prey(Origin, Vector3.one) : null;
                if (occupied) Check(projectile.TryCapture(passenger.gameObject), "wall scenario captures passenger");
                projectile.Launch(null, null, direction * 8f);
                Physics.SyncTransforms(); Step(projectile, 40);
                Check(projectile.IsPopped == !explicitOnly, "actual wall/ceiling contact follows toggle, occupied=" + occupied + ", direction=" + direction);
                if (explicitOnly) Check(Vector3.Dot(projectile.GetComponent<Rigidbody>().position - Origin, direction) < 1.5f, "scenery stops bubble without tunneling");
                if (occupied) Check(passenger.IsBubbleCaptured == explicitOnly, "wall contact releases only when popping is enabled");
                Destroy(projectile.gameObject); Destroy(wall);
                if (passenger != null) Destroy(passenger.gameObject);
            }
        }

        TrappingBubble drifting = Bubble(Origin);
        drifting.Launch(null, null, Vector3.forward * 8f);
        Set(drifting, "riseSeconds", 1f); Set(drifting, "lifetimeSeconds", 2f);
        Step(drifting, 20);
        Check(drifting.GetComponent<Rigidbody>().position.y > Origin.y + 0.1f, "bubble rises along Y");
        Check(drifting.GetComponent<Rigidbody>().linearVelocity.z < 4f, "launch momentum decays");
        Step(drifting, 40);
        Check(drifting.GetComponent<Rigidbody>().linearVelocity == Vector3.zero && !drifting.IsPopped, "rise ends in hover before timeout");
        Step(drifting, 50);
        Check(drifting.IsPopped, "automatic timeout pops bubble");
        Destroy(drifting.gameObject);
    }

    private static void ValidatePlayerAndHeldUse()
    {
        GameObject root = new GameObject("Test Player"); root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene); root.transform.position = Origin;
        Rigidbody body = root.AddComponent<Rigidbody>(); body.useGravity = true; body.constraints = RigidbodyConstraints.FreezeRotation;
        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>(); capsule.height = 2f; capsule.radius = 0.4f;
        var controller = root.AddComponent<SmoothFirstPersonController>();
        var carrier = root.AddComponent<PlayerCreatureCarrier>();
        GameObject group = new GameObject("PlayerObjects Test"); SceneManager.MoveGameObjectToScene(group, scene);
        root.transform.SetParent(group.transform, true);
        GameObject cameraObject = new GameObject("Test CameraHolder"); cameraObject.transform.SetParent(group.transform, false);
        cameraObject.transform.position = Origin;
        var interactor = cameraObject.AddComponent<PlayerCreatureInteractor>();
        var camera = cameraObject.AddComponent<Camera>();
        Transform anchor = new GameObject("Carry Point").transform; anchor.SetParent(camera.transform, false);
        anchor.localPosition = new Vector3(1f, -0.5f, 2f);
        Set(carrier, "carryCamera", camera); Set(carrier, "carryPoint", anchor); Set(carrier, "firmGrip", true);
        Set(interactor, "interactionCamera", camera); Set(controller, "playerCamera", camera);
        root.SetActive(true);
        var flower = Spawn(AssetDatabase.LoadAssetAtPath<GameObject>(BubbleFlowerSetup.FlowerPath), Origin + Vector3.right).GetComponent<BubbleFlower>();
        Call(flower, "TickCreature", 6.1f);
        TrappingBubble groundShot = flower.LastShot;
        Check(groundShot != null && groundShot.GetComponent<Rigidbody>().linearVelocity == Vector3.up * 0.5f, "ground flower emits a rising bubble on schedule");
        Call(carrier, "BeginCarry", flower);
        Check(carrier.CarriedCreature == flower, "flower uses existing carry system");
        Call(flower, "TickCreature", 10f);
        Check(flower.LastShot == groundShot, "automatic firing stops while carried");
        Destroy(groundShot.gameObject);
        // Place an interactive shroom on the crosshair, then verify held use wins even on cooldown.
        var shroom = Spawn(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/BounceShroom.prefab"), Origin + Vector3.forward * 4f - Vector3.up * 0.25f).GetComponent<BounceShroom>();
        Physics.SyncTransforms();
        Check(interactor.FindCreature() == shroom, "test crosshair actually targets the shroom");
        interactor.TryInteract();
        TrappingBubble shot = flower.LastShot;
        Check(shot != null && !shroom.IsExpanded && shroom.CurrentState == BounceShroom.ShroomState.Compact, "held flower fires instead of expanding crosshair shroom");
        interactor.TryInteract();
        Check(flower.LastShot == shot && shroom.CurrentState == BounceShroom.ShroomState.Compact, "cooldown consumes click without interacting with shroom");
        Check(!shot.TryCapture(root), "shooter is protected immediately after firing");
        Set(controller, "startYScale", 1f); Set(controller, "crouching", true);
        root.transform.localScale = new Vector3(1f, 0.5f, 1f);
        Physics.SyncTransforms();
        var trap = Bubble(Origin + Vector3.left * 2f);
        Check(trap.TryCapture(root), "player is capturable");
        Check(root.transform.localScale.y == 0.5f, "capturing a crouched player preserves their scale");
        Check(controller.BubbleMovementBlocked && carrier.CarriedCreature == flower, "trapped player keeps held flower and movement is locked");
        Set(flower, "nextManualShot", 0f); interactor.TryInteract();
        Check(flower.LastShot != shot, "held flower can still fire while trapped");
        var escapeCapture = trap.Occupant;
        body.linearDamping = 4f; // The actual controller may have been grounded when captured.
        var keyboard = InputSystem.AddDevice<Keyboard>();
        try
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftCtrl)); InputSystem.Update();
            Vector3 lockedScale = root.transform.localScale;
            Call(controller, "Update"); Call(controller, "FixedUpdate");
            Check(body.isKinematic && root.transform.localScale == lockedScale, "trapped movement and crouch inputs cannot take ownership");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); InputSystem.Update();
            Call(escapeCapture, "Update");
            Call(controller, "Update"); Call(controller, "FixedUpdate");
            Call(escapeCapture, "Update"); // The same press must not launch twice.
        }
        finally { InputSystem.RemoveDevice(keyboard); }
        Check(trap.IsPopped && !escapeCapture.IsCaptured && !body.isKinematic && body.useGravity, "escape pops and restores player physics");
        Check(root.transform.localScale.y == 1f, "Ctrl released while trapped does not leave crouching stuck");
        Check(body.linearDamping == 0f, "escape clears retained ground drag before the next physics step");
        Check(Mathf.Abs(body.linearVelocity.y - Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * 4f)) < 0.01f, "escape velocity corresponds to four metres of rise");
        Check(controller.BubbleMovementBlocked && !escapeCapture.CanCapture, "escape press consumed and recapture immunity applied");
        float start = body.position.y, apex = start;
        for (int i = 0; i < 100; i++) { scene.GetPhysicsScene().Simulate(0.02f); apex = Mathf.Max(apex, body.position.y); }
        Check(Mathf.Abs(apex - start - 4f) < 0.25f, "physics jump reaches approximately two body heights");
        carrier.Release();
        Destroy(flower.gameObject); Destroy(shot.gameObject); Destroy(trap.gameObject); Destroy(shroom.gameObject); Destroy(group);
    }

    private static void ValidateWebHandoff()
    {
        var webPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SkySpiderSetup.WebPath);
        Check(webPrefab != null, "web prefab available for handoff");
        var web = Spawn(webPrefab, Origin).GetComponent<SkyWeb>();
        Creature prey = Prey(Origin, Vector3.one);
        Check(web.TryCapture(prey.GetComponent<Rigidbody>(), Origin), "web captures initial prey");
        TrappingBubble bubble = Bubble(Origin);
        Check(bubble.TryCapture(prey.gameObject), "bubble takes ownership from silk");
        Check(!prey.GetComponent<WebCapture>().IsCaptured && !prey.SimulationEnabled, "silk releases before bubble pauses original simulation");
        Check(!web.IsEligible(prey.GetComponent<Rigidbody>()), "web cannot steal bubble occupant");
        bubble.Pop();
        Check(prey.SimulationEnabled && !prey.IsBubbleCaptured, "web handoff restores original active simulation on pop");
        Destroy(bubble.gameObject); Destroy(prey.gameObject); Destroy(web.gameObject);
    }

    private static void ValidatePickupInputs()
    {
        GameObject root = new GameObject("Pickup Input Test"); root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene); root.transform.position = Origin;
        var carrier = root.AddComponent<PlayerCreatureCarrier>();
        var cameraObject = new GameObject("Pickup Camera"); cameraObject.transform.SetParent(root.transform, false);
        var camera = cameraObject.AddComponent<Camera>();
        Set(carrier, "carryCamera", camera);
        var mouse = InputSystem.AddDevice<Mouse>();
        var keyboard = InputSystem.AddDevice<Keyboard>();
        try
        {
            root.SetActive(true);
            foreach (PlayerCreatureCarrier.CarryInput mode in Enum.GetValues(typeof(PlayerCreatureCarrier.CarryInput)))
            foreach (bool occupied in new[] { false, true })
            {
                Set(carrier, "carryInput", mode);
                InputSystem.QueueStateEvent(mouse, new MouseState());
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                TrappingBubble bubble = Bubble(Origin + Vector3.forward * 3f);
                Creature passenger = occupied ? Prey(bubble.transform.position, Vector3.one) : null;
                if (occupied) Check(bubble.TryCapture(passenger.gameObject), "pickup scenario has an occupant");
                Physics.SyncTransforms();
                Set(carrier, "pickupActionConsumed", false);
                Check(carrier.FindCreature() == bubble, "pickup targeting accepts bubble action in " + mode);
                InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right));
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
                InputSystem.Update();
                if (mode == PlayerCreatureCarrier.CarryInput.HoldRightMouse) Call(carrier, "UpdateHoldRightMouse");
                else Call(carrier, "UpdateToggle", mode == PlayerCreatureCarrier.CarryInput.ToggleRightMouse);
                Check(bubble.IsPopped && carrier.CarriedCreature == null, "actual pickup input pops without carrying in " + mode);
                if (occupied) Check(!passenger.IsBubbleCaptured, "pickup input releases occupied bubble in " + mode);
                Destroy(bubble.gameObject);
                if (passenger != null) Destroy(passenger.gameObject);
            }
        }
        finally
        {
            InputSystem.RemoveDevice(mouse); InputSystem.RemoveDevice(keyboard);
            Destroy(root);
        }
    }

    private static void Write(string name, string contents)
    {
        Directory.CreateDirectory("Temp/BubbleFlowerValidation");
        File.WriteAllText("Temp/BubbleFlowerValidation/" + name, contents);
    }
}
