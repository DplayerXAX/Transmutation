using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Real physics checks for balloon buoyancy, walking, reeling and ownership cleanup.</summary>
[InitializeOnLoad]
public static class BubbleLeashValidation
{
    private const string Pending = "Capstone.BubbleLeashValidation.Pending";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static BubbleLeashValidation()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false)) Validate();
        };
        if (EditorApplication.isPlaying && SessionState.GetBool(Pending, false)) EditorApplication.delayCall += Validate;
    }
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    private static void Validate()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        int checks = 0;
        SimulationMode oldMode = Physics.simulationMode;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Bubble leash: " + message);
            checks++;
        }
        void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
        object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            Vector3 origin = new Vector3(10000f, 1000f, 10000f);
            var camera = new GameObject("Camera").AddComponent<Camera>(); camera.tag = "MainCamera"; camera.transform.position = origin + Vector3.up;
            var player = new GameObject("Player"); player.transform.position = origin;
            var body = player.AddComponent<Rigidbody>(); body.useGravity = false; body.constraints = RigidbodyConstraints.FreezeRotation;
            var playerCollider = player.AddComponent<SphereCollider>(); playerCollider.radius = 0.35f;
            player.AddComponent<SmoothFirstPersonController>();
            var carrier = player.AddComponent<PlayerCreatureCarrier>(); carrier.enabled = false;
            var tether = player.AddComponent<TentacleTether>();
            Set(tether, "view", camera);
            var tentacleObject = new GameObject("Tentacle"); tentacleObject.transform.position = origin + Vector3.right;
            tentacleObject.AddComponent<SphereCollider>().radius = 0.3f;
            var tentacle = tentacleObject.AddComponent<TentacleCreature>(); Call(carrier, "BeginCarry", tentacle);
            var live = new List<TrappingBubble>();
            var fireBubbles = new List<FireBubble>();
            TrappingBubble Bubble(Vector3 position)
            {
                var go = new GameObject("Balloon"); go.transform.position = position;
                var bubble = go.AddComponent<TrappingBubble>(); bubble.Launch(null, null, Vector3.zero);
                Set(bubble, "riseSeconds", 100f); Set(bubble, "lifetimeSeconds", 1000f); live.Add(bubble);
                return bubble;
            }
            void Attach(LeashBubble bubble) => tether.FireAt(bubble.transform.position, bubble.transform);
            FireBubble Fire(Vector3 position)
            {
                var go = new GameObject("Fire balloon"); go.transform.position = position;
                var bubble = go.AddComponent<FireBubble>();
                bubble.LifetimeLimited = true;
                Set(bubble, "driftVelocity", Vector3.zero); Set(bubble, "fallStartWorldY", float.MaxValue);
                Set(bubble, "lifetimeSeconds", 0.01f); Set(bubble, "inanimateToFlowerDelay", 0.01f);
                fireBubbles.Add(bubble); return bubble;
            }
            void Step(int frames)
            {
                for (int i = 0; i < frames; i++)
                {
                    foreach (TrappingBubble bubble in live) if (bubble != null && !bubble.IsPopped) bubble.SimulateStep(0.02f);
                    foreach (FireBubble bubble in fireBubbles) if (bubble != null && !bubble.IsDead) Call(bubble, "FixedUpdate");
                    Call(tether, "FixedUpdate"); Physics.Simulate(0.02f);
                }
            }
            void ClearBubbles()
            {
                tether.LetGoAll();
                foreach (TrappingBubble bubble in live) if (bubble != null) Object.DestroyImmediate(bubble.gameObject);
                live.Clear();
                foreach (FireBubble bubble in fireBubbles) if (bubble != null) Object.DestroyImmediate(bubble.gameObject);
                fireBubbles.Clear();
            }
            TrappingBubble balloon = Bubble(tether.BalloonOrigin + Vector3.up * 3f + Vector3.forward * 3f);
            Attach(balloon);
            Check(balloon.IsLeashed && tether.BalloonCount == 1 && tentacle.PinnedCount == 1, "a normal arm shot attaches a bubble leash");
            Check(Physics.GetIgnoreCollision(balloon.GetComponent<SphereCollider>(), playerCollider), "balloon cannot trap or push its handler");
            Check(!balloon.TryCapture(player), "the leash handler cannot be captured by their own balloon");
            float initialY = balloon.transform.position.y;
            Step(250);
            Check(balloon.transform.position.y > initialY + 0.25f && balloon.IsLeashed, "a slack balloon continues floating upwards");
            Check(Vector3.Distance(balloon.transform.position, tether.BalloonOrigin) <= balloon.LeashLength + balloon.Radius + 0.08f,
                "a taut leash stops unbounded rising without teleporting the shell");
            Vector3 before = balloon.transform.position;
            body.linearVelocity = Vector3.right * 2f; Step(200); body.linearVelocity = Vector3.zero;
            Check(balloon.IsLeashed && balloon.transform.position.x > before.x + 3f, "walking leads the balloon along behind the player");
            Check(Mathf.Abs(body.position.y - origin.y) < 0.01f, "balloon buoyancy never lifts the player");
            Check(Vector3.Distance(balloon.transform.position, tether.BalloonOrigin) <= balloon.LeashLength + balloon.Radius + 0.15f,
                "walking preserves the leash length");
            float oldLength = balloon.LeashLength;
            Set(tether, "reeling", true); Step(200); Set(tether, "reeling", false);
            Check(balloon.LeashLength < oldLength - 1f && balloon.LeashLength >= 1.4f, "holding reel shortens the balloon leash to a safe minimum");
            Check(Vector3.Distance(balloon.transform.position, tether.BalloonOrigin) - balloon.Radius < 1.6f,
                "reeling actually brings the balloon to the hand");
            Check(body.linearVelocity.sqrMagnitude < 0.0001f && !tether.Reeling, "balloon reeling does not fling or accelerate the handler");
            tether.LetGoAll();
            Check(!balloon.IsLeashed && tether.BalloonCount == 0 && tentacle.PinnedCount == 0, "tap release clears both physical and visual ownership");
            Check(!Physics.GetIgnoreCollision(balloon.GetComponent<SphereCollider>(), playerCollider), "release restores previous player collision settings");
            initialY = balloon.transform.position.y; Step(50);
            Check(balloon.transform.position.y > initialY + 0.15f, "released balloon floats away");
            ClearBubbles();

            balloon = Bubble(tether.BalloonOrigin + Vector3.up * 2f + Vector3.forward * 4f); Attach(balloon);
            var prey = GameObject.CreatePrimitive(PrimitiveType.Cube); prey.transform.position = balloon.transform.position;
            prey.AddComponent<Rigidbody>().useGravity = false; var creature = prey.AddComponent<CaveHarvestItem>();
            float smallRadius = balloon.Radius; Physics.SyncTransforms();
            Check(balloon.TryCapture(prey) && balloon.IsLeashed && balloon.IsOccupied, "a leashed balloon can capture and transport a creature");
            int arm = ((List<int>)Get(tether, "pinnedArms"))[0]; tentacle.ArmPin(arm, out Vector3 pin);
            Check(balloon.Radius > smallRadius && Mathf.Abs(Vector3.Distance(pin, balloon.transform.position) - balloon.Radius) < 0.01f,
                "sucker contact follows the grown shell instead of remaining buried inside it");
            body.linearVelocity = Vector3.left; Step(120); body.linearVelocity = Vector3.zero;
            Check(balloon.IsLeashed && !creature.SimulationEnabled && Vector3.Distance(prey.transform.position, balloon.transform.position) < 0.1f,
                "the occupant follows while the balloon is led");
            balloon.Pop();
            Check(!balloon.IsPopped && balloon.IsLeashed && balloon.IsOccupied && !balloon.CanBeConsumed,
                "a leashed shell survives explicit popping and cannot be consumed");
            balloon.Pop(true);
            Check(!balloon.IsPopped && balloon.IsLeashed && !balloon.IsOccupied && creature.SimulationEnabled,
                "an occupant can escape without destroying the protected shell");
            balloon.ReleaseLeash(); balloon.Pop();
            Check(tether.BalloonCount == 0 && tentacle.PinnedCount == 0 && creature.SimulationEnabled,
                "popping clears the sucker and restores its occupant");
            Object.DestroyImmediate(prey); ClearBubbles();

            TrappingBubble first = Bubble(tether.BalloonOrigin + new Vector3(-3f, 2f, 4f));
            TrappingBubble second = Bubble(tether.BalloonOrigin + new Vector3(3f, 2f, 4f));
            Attach(first); Attach(second); Attach(second);
            Check(tether.BalloonCount == 2 && tentacle.PinnedCount == 2, "two balloons can be led, and repeating a target does not duplicate ownership");
            tether.FireAt(tether.BalloonOrigin + Vector3.forward * 5f);
            Check(!first.IsLeashed && second.IsLeashed && tether.BalloonCount == 1 && tentacle.PinnedCount == 2 && !tether.NewestIsBalloon,
                "a third shot replaces the oldest balloon and preserves terrain-arm behaviour");
            tether.LetGoAll(); Attach(first); Attach(second); carrier.Release();
            Step(1); Call(tether, "Update");
            Check(first.IsLeashed && second.IsLeashed && tether.BalloonCount == 0 && tentacle.PinnedCount == 2
                && !tentacle.Suspended && tether.CordCount == 0,
                "putting down the octopus retains its two balloons without constructing a floating bridge");
            Check(Vector3.Distance(first.LeashOrigin, tentacle.transform.position) < tentacle.TetherBodyRadius,
                "a dropped balloon anchors to the octopus body");
            Vector3 parkedOrigin = first.LeashOrigin;
            body.position += Vector3.right * 20f; Physics.SyncTransforms(); Step(60);
            Check(first.IsLeashed && second.IsLeashed && Vector3.Distance(first.LeashOrigin, parkedOrigin) < 0.01f,
                "walking away leaves both balloons attached to the octopus, not the player");
            Check(!Physics.GetIgnoreCollision(first.GetComponent<SphereCollider>(), playerCollider),
                "drop removes the old handler's collision exemption while retaining the octopus connection");
            for (int i = 0; i < 150; i++)
            {
                tentacle.GetComponent<Rigidbody>().position += Vector3.right * 0.02f;
                Physics.SyncTransforms(); Step(1);
            }
            Check(first.IsLeashed && second.IsLeashed && Vector3.Distance(first.LeashOrigin, parkedOrigin) > 2.5f,
                "the persistent anchor follows movement of the dropped octopus");
            var otherObject = new GameObject("Other octopus"); otherObject.transform.position = body.position + Vector3.forward;
            otherObject.AddComponent<SphereCollider>().radius = 0.3f;
            var other = otherObject.AddComponent<TentacleCreature>(); Call(carrier, "BeginCarry", other);
            TrappingBubble third = Bubble(tether.BalloonOrigin + Vector3.forward * 3f); Attach(third);
            Check(first.IsLeashed && second.IsLeashed && third.IsLeashed && tether.BalloonCount == 1,
                "a second octopus has independent arms without overwriting dropped balloon connections");
            tether.LetGoAll();
            Check(!third.IsLeashed && first.IsLeashed && second.IsLeashed, "release affects only the currently carried octopus");
            carrier.Release(); Call(tether, "Update");
            body.position = tentacle.transform.position + Vector3.back; camera.transform.position = body.position + Vector3.up * 0.8f;
            Call(carrier, "BeginCarry", tentacle);
            Check(first.IsLeashed && second.IsLeashed && tether.BalloonCount == 2 && tentacle.PinnedCount == 2 && tether.NewestIsBalloon,
                "pickup preserves both suckers and restores hand control");
            Check(Vector3.Distance(second.LeashOrigin, tether.BalloonOrigin) < 0.01f,
                "pickup switches the balloon anchor back to the hand");
            float restoredLength = second.LeashLength;
            Set(tether, "reeling", true); Step(12); Set(tether, "reeling", false);
            Check(second.LeashLength < restoredLength - 0.5f && body.linearVelocity.sqrMagnitude < 0.0001f,
                "restored control can reel the balloon without moving the player");
            ClearBubbles(); Object.DestroyImmediate(otherObject);

            balloon = Bubble(tether.BalloonOrigin + Vector3.forward * 3f); Attach(balloon);
            var ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube); ceiling.transform.position = balloon.transform.position + Vector3.up * 0.75f;
            ceiling.transform.localScale = new Vector3(4f, 0.1f, 4f); Physics.SyncTransforms(); Step(180);
            Check(!balloon.IsPopped && balloon.IsLeashed, "leashed balloons survive normal scenery contact");
            Check(balloon.transform.position.y + balloon.Radius <= ceiling.GetComponent<Collider>().bounds.min.y + 0.03f,
                "normal physics prevents a leashed bubble from passing through a ceiling");
            Object.DestroyImmediate(ceiling); ClearBubbles();

            camera.transform.position = body.position + Vector3.up * 0.8f;
            balloon = Bubble(camera.transform.position + Vector3.forward * 5f);
            var own = new GameObject("Own arm collider"); own.transform.SetParent(tentacle.transform);
            own.transform.position = camera.transform.position + Vector3.forward; own.AddComponent<SphereCollider>().radius = 0.2f;
            Physics.SyncTransforms(); object[] aim = { Vector3.zero, false }; Call(tether, "Aim", aim);
            Check((Transform)Get(tether, "aimAnchor") == balloon.transform, "aiming ignores the held octopus and reaches the bubble");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = camera.transform.position + Vector3.forward * 2f;
            wall.transform.localScale = new Vector3(4f, 4f, 0.2f); Physics.SyncTransforms(); Call(tether, "Aim", aim);
            Check((Transform)Get(tether, "aimAnchor") == wall.transform, "aiming cannot attach to a bubble through a wall");
            Object.DestroyImmediate(own); Object.DestroyImmediate(wall); Attach(balloon);
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = (tether.BalloonOrigin + balloon.transform.position) * 0.5f;
            wall.transform.localScale = new Vector3(4f, 4f, 0.2f); Physics.SyncTransforms(); Step(20);
            Check(!balloon.IsLeashed && tentacle.PinnedCount == 0, "an obstructed leash releases instead of cutting through terrain");
            Object.DestroyImmediate(wall); ClearBubbles();
            balloon = Bubble(tether.BalloonOrigin + Vector3.forward * 3f); Attach(balloon); carrier.Release(); Call(tether, "Update");
            balloon.LifetimeLimited = true; Set(balloon, "lifetimeSeconds", 0.01f); Step(1);
            Check(!balloon.IsPopped && balloon.IsLeashed && balloon.ElapsedTime == 0f,
                "a dropped octopus's bubble stays alive with its lifetime paused");
            balloon.Die(); balloon.ReceivePickupAttempt(carrier);
            Check(!balloon.IsDead && !balloon.IsPopped && balloon.IsLeashed, "death and pickup cannot remove a tethered shell");
            balloon.ReleaseLeash(); Step(1);
            Check(balloon.IsPopped && tether.BalloonCount == 0 && tentacle.PinnedCount == 0,
                "remaining lifetime resumes after releasing a protected bubble");
            Call(carrier, "BeginCarry", tentacle); ClearBubbles();

            FireBubble fire = Fire(tether.BalloonOrigin + Vector3.forward * 4f);
            camera.transform.position = tether.BalloonOrigin; camera.transform.rotation = Quaternion.identity;
            Physics.SyncTransforms(); Call(tether, "Aim", aim);
            Check((Transform)Get(tether, "aimAnchor") == fire.transform, "the regular aiming path selects fire bubbles too");
            Attach(fire); Step(300);
            Check(fire.IsLeashed && !fire.IsDead && !fire.IsInanimate && !(bool)Get(fire, "descending")
                && (float)Get(fire, "elapsedTime") == 0f && tether.NewestIsBalloon,
                "a fire balloon stays floating without expiring, falling or transforming while tethered");
            fire.Die();
            Check(!fire.IsDead && !fire.CanBeConsumed, "a tethered fire bubble is protected from death and feeding");
            float fireLength = fire.LeashLength;
            Set(tether, "reeling", true); Step(16); Set(tether, "reeling", false);
            Check(fire.LeashLength < fireLength - 0.5f && body.linearVelocity.sqrMagnitude < 0.0001f,
                "fire bubbles reel towards the handler without pulling the handler");
            balloon = Bubble(tether.BalloonOrigin + Vector3.left * 3f + Vector3.up);
            Check(!balloon.TryCapture(fire.gameObject) && fire.IsLeashed,
                "another bubble cannot steal the physical ownership of a tethered fire bubble");
            Attach(balloon); carrier.Release(); Call(tether, "Update"); Step(20);
            Check(fire.IsLeashed && balloon.IsLeashed && tentacle.PinnedCount == 2 && tether.BalloonCount == 0,
                "ordinary and fire bubbles both remain tethered after putting down the octopus");
            Call(carrier, "BeginCarry", tentacle);
            Check(fire.IsLeashed && balloon.IsLeashed && tether.BalloonCount == 2,
                "pickup restores both bubble types together");
            var bone = new GameObject("Fuel receiver").AddComponent<BoneChild>();
            Check(fire.BindToBone(bone) && !fire.IsLeashed && fire.IsBoundToBone && tether.BalloonCount == 1,
                "feeding a bone transfers ownership cleanly from the sucker to the ribs");
            fire.BurnBoneHeat(100f);
            Check(fire.IsDead && !fire.gameObject.activeSelf, "bone fuel still burns out normally after leaving the leash");
            Object.DestroyImmediate(bone.gameObject); ClearBubbles();
            fire = Fire(tether.BalloonOrigin + Vector3.forward * 3f); Call(fire, "StartFalling"); Attach(fire); Step(100);
            Check(fire.IsLeashed && (float)Get(fire, "inanimateTimer") == 0f && !(bool)Get(fire, "transformationStarted")
                && !fire.GetComponent<Rigidbody>().useGravity, "even a fallen fire bubble can be led without becoming a flower");
            fire.ReleaseLeash();
            Check(fire.GetComponent<Rigidbody>().useGravity && fire.IsInanimate,
                "releasing a fallen fire bubble restores its original gravity and life state");
            ClearBubbles();
            balloon = Bubble(tether.BalloonOrigin + Vector3.forward * 3f); Attach(balloon); carrier.Release(); Call(tether, "Update"); tentacle.enabled = false;
            Check(!balloon.IsLeashed && tentacle.PinnedCount == 0, "disabling a dropped octopus releases its persistent balloons");
            tentacle.enabled = true; Call(carrier, "BeginCarry", tentacle); ClearBubbles();
            balloon = Bubble(tether.BalloonOrigin + Vector3.forward * 3f); Attach(balloon); tether.enabled = false;
            Check(!balloon.IsLeashed && tentacle.PinnedCount == 0, "disabling the tether releases every balloon synchronously");
            Debug.Log($"Bubble leash Play Mode validation passed: {checks} assertions.");
            Physics.simulationMode = oldMode; EditorApplication.Exit(0);
        }
        catch (Exception error) { Physics.simulationMode = oldMode; Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
