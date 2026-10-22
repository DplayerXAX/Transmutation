using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Isolated lifecycle, shared supply, real wall entry, depletion, and render checks.</summary>
public static class GooCreatureValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Goo validation: " + message);
        checks++;
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    private static void Call(object target, string method, params object[] values) => target.GetType().GetMethod(method, Private).Invoke(target, values);
    private static void Supply(PlayerGooCoating coating, float seconds) => Call(coating, "SetRemaining", seconds);
    private static void Advance(PlayerGooCoating coating, float seconds) =>
        typeof(PlayerGooCoating).GetMethod("AdvanceAttachedTime", Private | BindingFlags.Public).Invoke(coating, new object[] { seconds });

    public static string ValidateAssets()
    {
        checks = 0;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GooCreatureSetup.PrefabPath);
        Check(prefab != null && prefab.GetComponent<GooCreature>() != null, "goo prefab exists");
        Check(prefab.GetComponent<Collider>() != null && !prefab.GetComponent<Collider>().isTrigger, "blob has solid pickup collider");
        Check(prefab.GetComponent<Rigidbody>() != null, "blob supports shared carry physics");
        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerObjects.prefab");
        Check(player.GetComponentsInChildren<PlayerGooCoating>(true).Length == 1, "shared prefab has exactly one goo supply");
        foreach (FirstPersonBody body in player.GetComponentsInChildren<FirstPersonBody>(true))
            Check(new SerializedObject(body).FindProperty("gooMaterial").objectReferenceValue != null, "arm coating material assigned");
        Shader shader = Shader.Find("Transmutation/Goo Coating");
        Check(shader != null && !ShaderUtil.ShaderHasError(shader), "goo shader compiles without errors");
        return "PASS: " + checks + " goo asset checks.";
    }

    [MenuItem("Tools/Creatures/Goo/Validate Gameplay (Play Mode)")]
    public static void Validate() => Debug.Log(Run());

    public static string Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode for goo validation.");
        checks = 0;
        Scene scene = SceneManager.CreateScene("Goo Validation " + Guid.NewGuid());
        var staging = new GameObject("Goo Validation Staging");
        SceneManager.MoveGameObjectToScene(staging, scene);
        staging.SetActive(false);
        GameObject rig = null;
        GameObject wall = null;
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerObjects.prefab");
            rig = Object.Instantiate(prefab, staging.transform);
            var controller = rig.GetComponentInChildren<SmoothFirstPersonController>(true);
            var coating = controller.GetComponent<PlayerGooCoating>();
            var carrier = rig.GetComponentInChildren<PlayerCreatureCarrier>(true);
            var climber = rig.GetComponentInChildren<HandClimber>(true);
            var body = rig.GetComponentInChildren<FirstPersonBody>(true);
            var camera = rig.GetComponentInChildren<Camera>(true);
            Vector3 origin = new Vector3(5000f, 10f, 5000f);
            rig.transform.position = origin;
            Set(body, "controller", controller); Set(body, "viewCamera", camera);
            Set(body, "carrier", carrier); Set(body, "climber", climber);
            Set(climber, "controller", controller); Set(climber, "viewCamera", camera);
            Set(carrier, "carryCamera", camera);
            rig.SetActive(true);
            staging.SetActive(true);
            rig.transform.SetParent(null, true);
            Call(controller, "Start");
            controller.enabled = false;
            var rb = controller.GetComponent<Rigidbody>();
            controller.transform.position = origin;
            rb.position = origin;
            controller.Orientation.rotation = Quaternion.identity;
            camera.transform.position = origin + Vector3.up * 0.6f;
            camera.transform.rotation = Quaternion.identity;
            Check(!coating.CanClimb && coating.RemainingSeconds == 0f, "player begins without goo");
            int changes = 0;
            coating.SupplyChanged += _ => changes++;
            Check(coating.TryApplyGoo() && coating.RemainingSeconds == 20f, "0 -> 20 seconds");
            Supply(coating, 10f);
            Check(coating.TryApplyGoo() && coating.RemainingSeconds == 30f, "10 -> 30 seconds");
            Supply(coating, 25f);
            Check(coating.TryApplyGoo() && coating.RemainingSeconds == 30f, "25 -> 30 seconds, excess discarded");
            Check(!coating.TryApplyGoo() && coating.RemainingSeconds == 30f, "full supply rejects another use");
            Check(changes == 5, "successful supply changes emit one notification each");
            Advance(coating, 5f);
            Check(coating.RemainingSeconds == 30f, "off-wall time preserves goo");

            GooCreature SpawnGoo()
            {
                var goo = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GooCreatureSetup.PrefabPath), staging.transform).GetComponent<GooCreature>();
                goo.transform.position = camera.transform.position + camera.transform.forward * 1.6f - Vector3.up * 0.19f;
                return goo;
            }
            var interaction = new CreatureInteraction(controller.gameObject, Vector3.zero, Vector3.forward);
            var goo = SpawnGoo();
            Supply(coating, 0f);
            goo.ReceiveInteraction(new CreatureInteraction(null, Vector3.zero, Vector3.forward));
            Check(!goo.IsDead && coating.RemainingSeconds == 0f, "invalid source preserves goo");
            var interactor = camera.GetComponent<PlayerCreatureInteractor>();
            if (interactor == null) interactor = camera.gameObject.AddComponent<PlayerCreatureInteractor>();
            Set(interactor, "interactionCamera", camera); Set(interactor, "carrier", carrier);
            Physics.SyncTransforms();
            Check(interactor.FindCreature() == goo, "world interaction ray reaches goo");
            interactor.TryInteract();
            Check(goo.IsDead && !goo.gameObject.activeSelf && coating.RemainingSeconds == 20f, "direct LMB use consumes goo and applies supply");
            goo.ReceiveInteraction(interaction);
            Check(coating.RemainingSeconds == 20f, "deferred destruction cannot repeat use");
            goo = SpawnGoo();
            Call(carrier, "BeginCarry", goo);
            Check(carrier.CarriedCreature == goo && goo.IsCarried && coating.RemainingSeconds == 20f, "pickup alone preserves creature and supply");
            carrier.Release();
            Check(!goo.IsCarried && !goo.IsDead && coating.RemainingSeconds == 20f, "drop does not consume goo");
            Call(carrier, "BeginCarry", goo);
            Supply(coating, 30f);
            interactor.TryInteract();
            Check(!goo.IsDead && carrier.CarriedCreature == goo, "held use at full capacity preserves creature");
            Supply(coating, 5f);
            interactor.TryInteract();
            Check(goo.IsDead && carrier.CarriedCreature == null && !goo.IsCarried && coating.RemainingSeconds == 25f,
                "held LMB use adds supply and clears carry ownership");

            wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(wall, scene);
            wall.name = "Goo validation wall"; wall.layer = 7;
            wall.transform.position = origin + Vector3.forward * 1.3f + Vector3.up * 2f;
            wall.transform.localScale = new Vector3(8f, 12f, 1f);
            Physics.SyncTransforms();
            Supply(coating, 0f);
            Set(climber, "canStartClimb", true);
            Check(!climber.CanStartClimb, "empty supply suppresses climb prompt");
            Call(climber, "TryStartClimb");
            Check(!climber.IsClimbing, "real wall entry is denied without goo");
            coating.TryApplyGoo();
            Call(climber, "TryStartClimb");
            Check(climber.IsClimbing && rb.isKinematic, "real hand climb begins with goo");
            climber.ScriptedInput = Vector2.zero;
            Advance(coating, 2f);
            Check(coating.RemainingSeconds == 18f, "hanging still consumes goo");
            climber.ScriptedInput = Vector2.up;
            Advance(coating, 2f);
            Check(coating.RemainingSeconds == 16f, "moving consumes goo at the same rate");
            Advance(coating, 0f);
            Check(coating.RemainingSeconds == 16f, "paused gameplay delta preserves supply");
            Advance(coating, 30f);
            Check(!coating.CanClimb && !climber.IsClimbing && !rb.isKinematic && rb.useGravity && !controller.Climbing,
                "depletion releases wall attachment and restores gravity");
            Call(climber, "TryStartClimb");
            Check(!climber.IsClimbing, "empty player cannot regrab");
            coating.TryApplyGoo();
            Call(climber, "TryStartClimb");
            Set(climber, "mantling", true);
            Advance(coating, 20f);
            Check(!climber.IsMantling && !climber.IsClimbing && !rb.isKinematic, "depletion cancels a mantle immediately");
            foreach (float seconds in new[] { 30f, 20f, 10f, 0.1f, 0f })
            {
                Supply(coating, seconds);
                Call(body, "LateUpdate");
                var overlays = (System.Collections.Generic.List<Renderer>)typeof(FirstPersonBody).GetField("gooRenderers", Private).GetValue(body);
                Check(overlays.Count == 26 && overlays.All(r => r.enabled == (seconds > 0f)), "both complete arms show correct visibility at " + seconds);
                var properties = new MaterialPropertyBlock(); overlays[0].GetPropertyBlock(properties);
                if (seconds > 0f)
                {
                    Check(Mathf.Approximately(properties.GetFloat("_Coverage"), Mathf.Min(1f, seconds / 20f)), "coverage follows supply at " + seconds);
                    Check(Mathf.Approximately(properties.GetFloat("_Thickness"), seconds > 20f ? 0.006f : 0.002f), "reserve changes thickness at " + seconds);
                }
                Check(overlays.All(r => r.GetComponent<Collider>() == null), "goo shells have no colliders");
            }
            Supply(coating, 1f);
            coating.enabled = false;
            Call(climber, "TryStartClimb");
            Check(!climber.IsClimbing, "disabled supply denies climb entry");
            Object.DestroyImmediate(coating);
            Call(climber, "TryStartClimb");
            Check(!climber.IsClimbing, "missing supply denies climb entry");
            return "PASS: " + checks + " goo gameplay checks.";
        }
        finally
        {
            if (rig != null) Object.DestroyImmediate(rig);
            if (wall != null) Object.DestroyImmediate(wall);
            if (staging != null) Object.DestroyImmediate(staging);
            SceneManager.UnloadSceneAsync(scene);
        }
    }
}
