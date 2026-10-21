using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Real carried-use regression checks in an isolated Play Mode scene.</summary>
[InitializeOnLoad]
public static class BubbleUseValidation
{
    private const string Pending = "Capstone.BubbleUseValidation.Pending";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static BubbleUseValidation()
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
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Bubble use: " + message);
            checks++;
        }
        void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
        object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
        void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
        try
        {
            Vector3 origin = new Vector3(5000f, 1000f, 5000f);
            var player = new GameObject("Test player"); player.SetActive(false); player.transform.position = origin;
            player.AddComponent<Rigidbody>().useGravity = false;
            player.AddComponent<CapsuleCollider>();
            var cameraObject = new GameObject("Camera"); cameraObject.transform.SetParent(player.transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            var carrier = player.AddComponent<PlayerCreatureCarrier>();
            var interactor = cameraObject.AddComponent<PlayerCreatureInteractor>();
            Set(carrier, "carryCamera", camera); Set(interactor, "interactionCamera", camera); Set(interactor, "carrier", carrier);
            player.SetActive(true);
            var templateObject = new GameObject("Bubble template"); templateObject.transform.position = origin + Vector3.right * 100f;
            var template = templateObject.AddComponent<TrappingBubble>();
            var flowerObject = new GameObject("Flower"); flowerObject.transform.position = origin + Vector3.right;
            flowerObject.AddComponent<SphereCollider>().radius = 0.2f;
            var flower = flowerObject.AddComponent<BubbleFlower>(); Set(flower, "bubblePrefab", template);
            Call(carrier, "BeginCarry", flower);
            Check(carrier.CarriedCreature == flower && flower.SimulationEnabled, "pickup retains flower use");
            Physics.SyncTransforms();

            camera.transform.rotation = Quaternion.Euler(-30f, 0f, 0f);
            interactor.TryInteract();
            TrappingBubble shot = flower.LastShot;
            Check(shot != null && !shot.IsPopped, "carried click produces a live shot");
            Vector3 expected = camera.transform.forward * 13f;
            expected.x *= 0.75f; expected.z *= 0.75f;
            expected.y = expected.y * 0.5f + 0.25f;
            Check(Vector3.Distance(shot.GetComponent<Rigidbody>().linearVelocity, expected) < 0.01f,
                "upward launch Y stays halved while horizontal speed is reduced by 25 percent");
            Object.DestroyImmediate(shot.gameObject);
            camera.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            Set(flower, "nextManualShot", Time.time); interactor.TryInteract(); shot = flower.LastShot;
            expected = camera.transform.forward * 13f; expected.y = expected.y * 0.5f + 0.25f;
            expected.x *= 0.75f; expected.z *= 0.75f;
            Check(Vector3.Distance(shot.GetComponent<Rigidbody>().linearVelocity, expected) < 0.01f, "downward launch Y is halved too");
            Object.DestroyImmediate(shot.gameObject);

            camera.transform.rotation = Quaternion.identity;
            Set(flower, "nextManualShot", Time.time); interactor.TryInteract(); shot = flower.LastShot;
            Check(Mathf.Abs(shot.GetComponent<Rigidbody>().linearVelocity.y - 0.25f) < 0.001f, "level shots rise at the new 0.25m/s rate");
            shot.SimulateStep(0.02f);
            Check(Mathf.Abs(shot.GetComponent<Rigidbody>().linearVelocity.y - 0.25f) < 0.001f, "the reduced rise persists after the first physics step");
            Set(flower, "nextManualShot", Time.time + 0.1f); interactor.TryInteract(); Call(flower, "TickCreature", 0.02f);
            Check(flower.LastShot == shot, "buffered click respects cooldown");
            Set(flower, "nextManualShot", Time.time); Call(flower, "TickCreature", 0.02f);
            Check(flower.LastShot != shot && flower.LastShot != null, "near-ready click fires once cooldown completes");
            Object.DestroyImmediate(shot.gameObject); shot = flower.LastShot;
            Call(flower, "TickCreature", 0.02f);
            Check(flower.LastShot == shot, "a buffered click cannot repeat by itself");
            Set(flower, "nextManualShot", Time.time + 0.1f); interactor.TryInteract();
            Set(flower, "queuedUntil", Time.time - 0.01f); Set(flower, "nextManualShot", Time.time); Call(flower, "TickCreature", 0.02f);
            Check(flower.LastShot == shot, "expired click cannot fire much later");
            Set(flower, "nextManualShot", Time.time + 0.1f); interactor.TryInteract(); carrier.Release();
            Set(flower, "nextManualShot", Time.time); Call(flower, "TickCreature", 0.02f);
            Check(flower.LastShot == shot && Get(flower, "queuedCarrier") == null, "dropping cancels queued firing");
            Object.DestroyImmediate(shot.gameObject);

            Set(carrier, "pendingCreature", flower); Set(carrier, "pendingAt", Time.time + 0.1f);
            interactor.TryInteract();
            Check((Creature)Get(interactor, "pendingHeldUse") == flower && !flower.IsCarried, "click survives the pickup reach animation");
            Set(carrier, "pendingAt", Time.time); Call(carrier, "UpdatePendingGrab"); Call(interactor, "ResolvePendingHeldUse");
            Check(flower.IsCarried && flower.LastShot != null, "the pending click fires as soon as the hand completes pickup");
            Object.DestroyImmediate(flower.LastShot.gameObject);
            carrier.Release(); Set(carrier, "pendingCreature", flower); interactor.TryInteract(); Set(carrier, "pendingCreature", null);
            Call(interactor, "ResolvePendingHeldUse");
            Check(Get(interactor, "pendingHeldUse") == null && flower.LastShot == null, "cancelled pickup cancels its use request");
            Check(typeof(PlayerCreatureInteractor).GetCustomAttribute<DefaultExecutionOrder>().order >
                typeof(PlayerCreatureCarrier).GetCustomAttribute<DefaultExecutionOrder>().order, "same-frame pickup resolves before use");

            Call(carrier, "BeginCarry", flower);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = origin + Vector3.forward * 1.5f;
            wall.transform.localScale = new Vector3(4f, 4f, 0.2f); Physics.SyncTransforms();
            Set(flower, "nextManualShot", Time.time); interactor.TryInteract(); shot = flower.LastShot;
            Check(shot != null && !shot.IsPopped, "a nearby wall still permits a shot in the clear space before it");
            Collider[] overlaps = Physics.OverlapSphere(shot.transform.position, shot.Radius + 0.01f);
            bool insideWall = Array.Exists(overlaps, item => item.gameObject == wall);
            Check(!insideWall && shot.transform.position.z < wall.GetComponent<Collider>().bounds.min.z,
                "the whole spawned bubble stays in front of the wall, without penetrating or spawning behind it");
            Object.DestroyImmediate(shot.gameObject);
            wall.transform.position = origin; Physics.SyncTransforms();
            Set(flower, "nextManualShot", Time.time); interactor.TryInteract();
            Check(flower.LastShot == null && (float)Get(flower, "nextManualShot") <= Time.time,
                "a fully blocked shot does not spend cooldown or create a bubble inside geometry");
            Object.DestroyImmediate(wall); Call(flower, "TickCreature", 0.02f);
            Check(flower.LastShot != null, "moving clear within the input buffer recovers the blocked shot");

            var persistent = new GameObject("Persistent bubble").AddComponent<TrappingBubble>();
            persistent.transform.position = origin + Vector3.right * 200f;
            Set(persistent, "lifetimeSeconds", 0.01f);
            Check(!persistent.LifetimeLimited, "ordinary bubbles default to no lifetime limit");
            persistent.SimulateStep(120f);
            Check(!persistent.IsPopped, "an ordinary bubble survives beyond its former timeout");
            typeof(Creature).GetField("ageRate", Private).SetValue(persistent, 100f);
            typeof(LeashBubble).GetMethod("Update", Private).Invoke(persistent, null);
            Check(persistent.Age == 40f && !persistent.IsDead, "disabled bubble lifetime also skips shared creature ageing");
            persistent.Pop();
            Check(persistent.IsPopped, "persistent ordinary bubbles still pop on explicit interaction");

            var fire = new GameObject("Persistent fire bubble").AddComponent<FireBubble>();
            fire.transform.position = origin + Vector3.right * 250f;
            Set(fire, "fallStartWorldY", float.MaxValue); Set(fire, "lifetimeSeconds", 0.01f);
            for (int i = 0; i < 10; i++) Call(fire, "FixedUpdate");
            Check(!fire.LifetimeLimited && !fire.IsInanimate && !fire.GetComponent<Rigidbody>().useGravity,
                "fire bubbles do not fall due to an expired flight timer by default");
            var nest = new GameObject("Persistent bubble nest").AddComponent<KilnBubbleNest>();
            Set(nest, "placed", true); Set(nest, "timer", 100f);
            var nestBubbles = (System.Collections.IList)Get(nest, "alive");
            nestBubbles.Add((fire, Time.time - 1000f)); Call(nest, "Update");
            Check(nestBubbles.Count == 1 && !fire.IsDead, "the nest does not time out a persistent fire bubble");
            Set(fire, "fallStartWorldY", fire.GetComponent<Rigidbody>().position.y - 1f); Call(fire, "FixedUpdate");
            Check(fire.IsInanimate && fire.GetComponent<Rigidbody>().useGravity, "height-based fire bubble falling still works");
            Object.DestroyImmediate(nest.gameObject); Object.DestroyImmediate(fire.gameObject);
            Debug.Log($"Bubble use Play Mode validation passed: {checks} assertions.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
