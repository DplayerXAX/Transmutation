using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>Isolated Play Mode checks. Run with -executeMethod BoneRideValidation.Run (without -quit).</summary>
[InitializeOnLoad]
public static class BoneRideValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string Pending = "Capstone.BoneRideValidation.Pending";

    static BoneRideValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) ValidatePending();
        };
        if (EditorApplication.isPlaying && SessionState.GetBool(Pending, false)) EditorApplication.delayCall += ValidatePending;
    }

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run BoneRideValidation in batch mode.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }

    private static void ValidatePending()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        try { Validate(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void Validate()
    {
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Bone ride validation: " + message);
            checks++;
        }
        void Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Private).Invoke(target, args);

        try
        {
            var camera = new GameObject("Ride validation camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            var player = new GameObject("Ride validation player");
            player.AddComponent<SmoothFirstPersonController>();
            var carrier = player.AddComponent<PlayerCreatureCarrier>();
            Call(carrier, "Awake");
            var tentacle = new GameObject("Attached tentacle").AddComponent<TentacleCreature>();
            Call(carrier, "BeginCarry", tentacle);
            Check(carrier.CarriesTentacle && tentacle.IsCarried, "tentacle attaches before mounting");

            var mount = new GameObject("Mount").AddComponent<BoneChild>();
            BoneRide.GetOn(mount, mount.transform, mount.transform, Array.Empty<Transform>());
            Check(mount.Ridden && carrier.TentacleLockedForRide, "mount locks the attached tentacle");
            carrier.Drop();
            carrier.Release();
            Call(carrier, "OnApplicationFocus", false);
            Check(carrier.CarriesTentacle && tentacle.IsCarried, "drop, release and focus loss preserve attachment");
            InputState.Change(mouse, new MouseState());
            Call(carrier, "UpdateRightMouse", false, false, true, 0.02f);
            Check(carrier.CarriesTentacle, "releasing the right mouse button cannot drop the tentacle");

            BoneRide.ScriptedMove = Vector2.zero;
            Check(BoneRide.Steer(1f) == Vector3.zero, "neutral input hovers instead of flying forward automatically");
            camera.transform.rotation = Quaternion.Euler(70f, 140f, 0f);
            Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized;
            BoneRide.ScriptedMove = Vector2.up;
            Check(Vector3.Angle(forward, BoneRide.Steer(1f)) < 0.01f, "forward movement follows view yaw without adding pitch");
            BoneRide.ScriptedMove = Vector2.down;
            Check(Vector3.Angle(-forward, BoneRide.Steer(1f)) < 0.01f, "backward movement opposes view yaw");
            BoneRide.ScriptedMove = Vector2.left;
            Check(Vector3.Dot(Vector3.Cross(Vector3.up, forward), BoneRide.Steer(1f)) < -0.99f, "A strafes left");
            BoneRide.ScriptedMove = Vector2.right;
            Check(Vector3.Dot(Vector3.Cross(Vector3.up, forward), BoneRide.Steer(1f)) > 0.99f, "D strafes right");
            BoneRide.ScriptedMove = Vector2.one;
            Check(BoneRide.Steer(1f).magnitude <= 1.001f, "diagonal movement cannot exceed the straight movement speed");
            BoneRide.ScriptedMove = Vector2.zero;
            BoneRide.ScriptedRise = 1f;
            Check(BoneRide.Steer(1f).y > 0.5f, "up input climbs");
            BoneRide.ScriptedRise = -1f;
            Check(BoneRide.Steer(1f).y < -0.5f, "down input descends");
            BoneRide.ScriptedRise = 0f;
            Check(BoneRide.Steer(1f) == Vector3.zero, "releasing movement and altitude input settles into a hover");

            var tether = player.AddComponent<TentacleTether>();
            Call(tether, "Awake");
            typeof(TentacleTether).GetField("aiming", Private).SetValue(tether, true);
            typeof(TentacleTether).GetField("aimValid", Private).SetValue(tether, true);
            Call(tether, "Update");
            Check(!tether.Aiming && carrier.CarriesTentacle, "idle riding does not fire or drop the creature");
            Check(tentacle.CarriedPrompt.Contains("[W/S] Up/Down") && tentacle.CarriedPrompt.Contains("[RMB] Dismount"), "rider sees W/S altitude and right-click exit controls");

            BoneRide.GetOff();
            Check(!mount.Ridden && !carrier.TentacleLockedForRide && !carrier.PlayerBody.isKinematic,
                "dismount unlocks dropping and restores the player body");
            Check(BoneRide.ScriptedMove == null && BoneRide.ScriptedRise == 0f, "dismount clears scripted controls");
            carrier.Drop();
            Check(!carrier.CarriesTentacle && !tentacle.IsCarried, "dropping works again after dismount");

            ValidateBoarding(player, carrier, tentacle, tether, mouse, Check);

            Call(carrier, "BeginCarry", tentacle);
            BoneRide.GetOn(mount, mount.transform, mount.transform, Array.Empty<Transform>());
            Call(carrier, "OnDisable");
            Check(carrier.CarriedCreature == null && !tentacle.IsCarried, "component shutdown still cleans up carry ownership");
            Debug.Log($"Bone ride validation passed: {checks} assertions.");
        }
        finally
        {
            BoneRide.CancelApproach();
            BoneRide.GetOff();
            InputSystem.RemoveDevice(mouse);
        }
    }

    private static void ValidateBoarding(GameObject player, PlayerCreatureCarrier carrier, TentacleCreature tentacle,
        TentacleTether tether, Mouse mouse, Action<bool, string> check)
    {
        const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        // Register frame-edge tracking before queuing the first synthetic click.
        _ = mouse.rightButton.wasPressedThisFrame;
        var inputUpdate = typeof(InputSystem).GetMethod("Update", StaticPrivate, null, new[] { typeof(InputUpdateType) }, null);
        void UpdateInput() => inputUpdate.Invoke(null, new object[] { InputUpdateType.Dynamic });
        InputSettings.UpdateMode oldInputMode = InputSystem.settings.updateMode;
        var oldBackground = InputSystem.settings.backgroundBehavior;
        var oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        InputSystem.EnableDevice(mouse); mouse.MakeCurrent();
        void SetMouse(MouseState state) { UpdateInput(); InputState.Change(mouse, state, InputUpdateType.Dynamic); }
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>(); InputSystem.EnableDevice(keyboard);
        void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
        Vector3 origin = new Vector3(6000f, 1000f, 6000f);
        Rigidbody body = player.GetComponent<Rigidbody>();
        Set(carrier.PlayerController, "rb", body); // These synchronous checks run before Unity's first Start callback.
        var capsule = player.AddComponent<CapsuleCollider>(); capsule.height = 1.8f; capsule.radius = 0.3f;
        var child = new GameObject("Boarding child").AddComponent<BoneChild>(); child.size = 1f;
        var beads = (System.Collections.Generic.List<Transform>)typeof(BoneChild).GetField("beads", Private).GetValue(child);
        for (int i = 0; i < 4; i++)
        {
            var bead = new GameObject("Boarding vertebra " + i); bead.transform.SetParent(child.transform, false);
            bead.transform.localPosition = Vector3.back * i * 0.5f;
            bead.AddComponent<SphereCollider>().radius = 0.4f; beads.Add(bead.transform);
        }
        BoneChild.All.Add(child);
        void Reset(Vector3 playerAt, Vector3 childAt)
        {
            BoneRide.CancelApproach(); BoneRide.GetOff(); tether.LetGoAll();
            // These scenarios run synchronously in one editor frame; each starts a fresh simulated input frame.
            typeof(BoneRide).GetField("toggledFrame", StaticPrivate).SetValue(null, -1);
            Set(player.GetComponent<SmoothFirstPersonController>(), "suppressedJumpFrame", -1);
            Set(carrier, "rideInputGuard", false);
            body.position = playerAt; player.transform.position = playerAt;
            child.transform.position = childAt; child.gameObject.SetActive(true);
            if (!carrier.CarriesTentacle) Call(carrier, "BeginCarry", tentacle);
            SetMouse(new MouseState()); Physics.SyncTransforms();
        }
        void Finish()
        {
            for (int i = 0; i < 350 && BoneRide.Approaching; i++) { Physics.SyncTransforms(); BoneRide.StepApproach(0.02f); }
        }
        GameObject wall = null, motherObject = null;
        try
        {
            BoneRide.ScriptedMove = null; BoneRide.ScriptedRise = 0f;
            SetMouse(new MouseState()); InputState.Change(keyboard, new KeyboardState(Key.W), InputUpdateType.Dynamic);
            Vector3 keyMove = BoneRide.Steer(1f);
            check(keyMove.y > 0.7f && Vector3.ProjectOnPlane(keyMove, Vector3.up).magnitude < 0.001f,
                "the actual W key rises vertically without forward drift");
            InputState.Change(keyboard, new KeyboardState(Key.S), InputUpdateType.Dynamic); keyMove = BoneRide.Steer(1f);
            check(keyMove.y < -0.7f && Vector3.ProjectOnPlane(keyMove, Vector3.up).magnitude < 0.001f,
                "the actual S key descends vertically without backward drift");
            Vector3 viewForward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
            InputState.Change(keyboard, new KeyboardState(Key.Space), InputUpdateType.Dynamic); keyMove = BoneRide.Steer(1f);
            check(Mathf.Abs(keyMove.y) < 0.001f && Vector3.Dot(viewForward, keyMove) > 0.99f, "Space moves forward while altitude remains level");
            InputState.Change(keyboard, new KeyboardState(Key.LeftCtrl), InputUpdateType.Dynamic); keyMove = BoneRide.Steer(1f);
            check(Mathf.Abs(keyMove.y) < 0.001f && Vector3.Dot(viewForward, keyMove) < -0.99f, "Ctrl moves backward while altitude remains level");
            InputState.Change(keyboard, new KeyboardState(), InputUpdateType.Dynamic); BoneRide.Steer(1f);
            Reset(origin, origin + Vector3.forward * 3f);
            SetMouse(new MouseState().WithButton(MouseButton.Right));
            Call(carrier, "Update");
            check(BoneRide.Approaching && carrier.CarriesTentacle && BoneRide.Mount == null,
                $"a nearby right click starts boarding before the carried tentacle can drop (pressed={mouse.rightButton.wasPressedThisFrame}, held={mouse.rightButton.isPressed}, near={BoneRide.TryNearby(out _, out _)}, allowed={BoneRide.CanToggle}, blocked={carrier.PlayerController.BubbleMovementBlocked})");
            Vector3 before = body.position; BoneRide.StepApproach(0.02f);
            check(Vector3.Distance(before, body.position) < 0.1f, "boarding accelerates from rest without snapping to the seat");
            Finish();
            check(child.Ridden && Vector3.Distance(body.position, child.RiderPosition) < 0.15f,
                "nearby boarding arrives on the child's back");
            check(!BoneRide.RightReinHeld, "holding the mounting click does not immediately turn the mount right");
            Vector3 hoverHead = origin + Vector3.up * 10f;
            Set(child, "head", hoverHead); Set(child, "headDir", Vector3.forward);
            typeof(BoneChild).GetField("<Warmth>k__BackingField", Private).SetValue(child, 1f);
            BoneRide.ScriptedMove = Vector2.zero; Call(child, "Fly", 0.2f);
            check(Vector3.Distance((Vector3)typeof(BoneChild).GetField("head", Private).GetValue(child), hoverHead) < 0.001f,
                "the warm child's actual flight motor stays still with neutral movement input");

            Reset(origin, origin + Vector3.forward * 20f);
            tether.FireAt(beads[2].position, beads[2]);
            check(BoneRide.Approaching && carrier.TentacleLockedForRide && !child.Ridden,
                "a fired arm starts the pull from a distant bone without teleporting or allowing a drop");
            for (int i = 0; i < 350 && BoneRide.Approaching; i++)
            { child.transform.position += Vector3.right * 0.01f; Physics.SyncTransforms(); BoneRide.StepApproach(0.02f); }
            check(child.Ridden && Vector3.Distance(body.position, child.RiderPosition) < 0.15f,
                "the pull follows a moving bone and automatically mounts on arrival");
            check(child.Reined && carrier.CarriesTentacle, "automatic boarding establishes both reins and keeps the octopus");
            SetMouse(new MouseState()); BoneRide.HandleInput(carrier);
            SetMouse(new MouseState().WithButton(MouseButton.Right));
            typeof(BoneRide).GetField("toggledFrame", StaticPrivate).SetValue(null, -1);
            BoneRide.HandleInput(carrier);
            check(!child.Ridden && !body.isKinematic && carrier.CarriesTentacle,
                "a fresh right click dismounts without dropping the octopus");

            Reset(origin, origin + Vector3.forward * 3f);
            child.MountAt(beads[2]); body.position = child.RiderPosition; player.transform.position = body.position;
            typeof(BoneRide).GetField("seatVelocity", StaticPrivate).SetValue(null, Vector3.right * 4f);
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Next climbing wall"; wall.layer = 7;
            wall.transform.position = origin + Vector3.forward * 12f + Vector3.up * 3f;
            wall.transform.localScale = new Vector3(8f, 14f, 0.1f);
            var coating = player.AddComponent<PlayerGooCoating>(); coating.TryApplyGoo();
            var transferClimber = player.AddComponent<HandClimber>();
            Set(transferClimber, "regrabTimer", 10f);
            Physics.SyncTransforms(); tether.FireAt(wall.transform.position, wall.transform);
            check(!BoneRide.Busy && tether.AutomaticReel && carrier.CarriesTentacle && tentacle.PinnedCount == 1,
                "a wall shot from a mount directly becomes a player pull with one wall arm");
            check(Mathf.Abs(body.linearVelocity.x - 4f) < 0.01f, "leaving a moving mount preserves rider momentum");
            check((float)typeof(HandClimber).GetField("regrabTimer", Private).GetValue(transferClimber) == 0f,
                "the new traversal transfer clears stale wall regrab cooldown");
            Call(tether, "ReelIn", 0.02f);
            check(body.linearVelocity.z > 0f, "the wall transfer immediately starts hauling without holding R");
            body.position = wall.transform.position - Vector3.forward * 0.65f - Vector3.up * 0.5f;
            player.transform.position = body.position; player.transform.rotation = Quaternion.identity;
            Set(carrier.PlayerController, "suppressedJumpFrame", -1);
            transferClimber.ScriptedInput = Vector2.up; Physics.SyncTransforms(); Call(transferClimber, "Update");
            check(transferClimber.IsClimbing, "holding W at arrival catches the actual wall");
            SetMouse(new MouseState()); Call(tether, "Update");
            check(!tether.AutomaticReel && tentacle.PinnedCount == 0 && body.isKinematic,
                "hands taking the wall release the hauling arm and retain climbing ownership");
            transferClimber.StopForBubbleCapture(); UnityEngine.Object.DestroyImmediate(transferClimber);
            UnityEngine.Object.DestroyImmediate(coating);
            UnityEngine.Object.DestroyImmediate(wall); wall = null;

            Reset(origin, origin + Vector3.forward * 20f);
            tether.FireAt(beads[2].position, beads[2]); BoneRide.StepApproach(0.02f);
            SetMouse(new MouseState().WithButton(MouseButton.Right));
            Call(carrier, "Update");
            check(!BoneRide.Busy && !body.isKinematic && carrier.CarriesTentacle && tentacle.PinnedCount == 0,
                "right click cancels an approach without dropping the octopus or leaving a kinematic player");
            SetMouse(new MouseState()); Call(carrier, "Update");
            check(carrier.CarriesTentacle, "releasing the cancel click cannot become a delayed drop");

            Reset(origin, origin + Vector3.forward * 20f);
            tether.FireAt(beads[2].position, beads[2]);
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Appearing wall during grapple";
            wall.transform.position = origin + Vector3.forward * 8f; wall.transform.localScale = new Vector3(8f, 12f, 0.1f);
            Physics.SyncTransforms(); Finish();
            check(!BoneRide.Busy && !body.isKinematic && body.position.z < wall.GetComponent<Collider>().bounds.min.z - capsule.radius,
                "a full player-capsule sweep stops boarding before a thin wall and returns movement control");
            UnityEngine.Object.DestroyImmediate(wall); wall = null;

            Reset(origin, origin + Vector3.forward * 20f);
            tether.FireAt(beads[2].position, beads[2]); child.gameObject.SetActive(false); BoneRide.StepApproach(0.02f);
            check(!BoneRide.Busy && !body.isKinematic && !carrier.PlayerController.Restricted,
                "a disappearing or disabled target cancels the pull and restores movement");

            Reset(origin, origin + Vector3.back * 100f);
            motherObject = new GameObject("Boarding mother"); var mother = motherObject.AddComponent<BoneKiln>();
            Transform root = new GameObject("Mother skeleton").transform; root.SetParent(mother.transform, false);
            Set(mother, "root", root);
            Transform skull = new GameObject("Far skull").transform; skull.SetParent(root); skull.position = origin + Vector3.forward * 35f;
            Set(mother, "skull", skull);
            Type stationType = typeof(BoneKiln).GetNestedType("Station", BindingFlags.NonPublic);
            var stations = (System.Collections.IList)typeof(BoneKiln).GetField("stations", Private).GetValue(mother);
            Transform back = null;
            for (int i = 0; i < 4; i++)
            {
                var vertebra = new GameObject("Mother back " + i).transform; vertebra.SetParent(root);
                vertebra.position = origin + Vector3.forward * (12f + i);
                object station = Activator.CreateInstance(stationType, true); stationType.GetField("transform").SetValue(station, vertebra);
                stations.Add(station); if (i == 0) back = vertebra;
            }
            tether.FireAt(back.position, back);
            check(BoneRide.Approaching, "the mother's back also accepts automatic grapple boarding");
            Finish();
            check(mother.Ridden && Vector3.Distance(body.position, mother.RiderPosition(back)) < 0.15f,
                "a shot to the mother's back chooses that nearby seat instead of pulling to her distant skull");
            check(mother.Reined, "boarding the mother establishes her two reins");
            var worldObject = new GameObject("Hover terrain fixture"); worldObject.transform.SetParent(mother.transform);
            var world = worldObject.AddComponent<ProceduralWorld>(); world.enabled = false;
            Set(world, "sinkholeCount", 0); Set(world, "surfaceAmplitude", 0f); Set(world, "rimHeight", 0f);
            Call(world, "PrepareLayout");
            Type densityType = typeof(ProceduralWorld).GetNestedType("DensityField", BindingFlags.NonPublic);
            Set(world, "field", Activator.CreateInstance(densityType, new object[] { world })); Set(mother, "world", world);
            Set(mother, "warmth", 1f); Set(mother, "head", hoverHead); Set(mother, "headDir", Vector3.forward);
            ((System.Collections.Generic.List<Vector3>)typeof(BoneKiln).GetField("trail", Private).GetValue(mother)).Add(hoverHead);
            BoneRide.ScriptedMove = Vector2.zero; Call(mother, "MoveHead", 0.2f);
            check(Vector3.Distance((Vector3)typeof(BoneKiln).GetField("head", Private).GetValue(mother), hoverHead) < 0.001f,
                "the warm mother's flight motor also hovers instead of continuing its autonomous cruise");
            var spareBalloon = new GameObject("Balloon beside mounted player").AddComponent<TrappingBubble>();
            spareBalloon.transform.position = body.position + Vector3.left * 4f;
            tether.FireAt(spareBalloon.transform.position, spareBalloon.transform);
            check(mother.Reined && spareBalloon.IsLeashed && mother.Ridden,
                "a mounted balloon shot uses a spare arm without stealing either steering rein");
            spareBalloon.ReleaseLeash(); UnityEngine.Object.DestroyImmediate(spareBalloon.gameObject);
            Set(carrier.PlayerController, "suppressedJumpFrame", -1);
            child.transform.position = origin + Vector3.right * 20f;
            tether.FireAt(beads[2].position, beads[2]);
            check(BoneRide.Approaching && BoneRide.Mount == null, "shooting another bone starts a direct mount-to-mount transfer");
            Finish();
            check(child.Ridden && child.Reined, "a direct bone transfer arrives with keyboard control ready");
        }
        finally
        {
            BoneRide.CancelApproach(); BoneRide.GetOff(); BoneChild.All.Remove(child);
            InputSystem.settings.updateMode = oldInputMode;
            InputSystem.settings.backgroundBehavior = oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
            InputSystem.RemoveDevice(keyboard);
            if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
            if (motherObject != null) UnityEngine.Object.DestroyImmediate(motherObject);
            UnityEngine.Object.DestroyImmediate(child.gameObject); UnityEngine.Object.DestroyImmediate(capsule);
        }
    }
}
