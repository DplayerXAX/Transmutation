using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Object = UnityEngine.Object;

/// <summary>Checks actual camera callbacks, plus honey contributions and visual/data agreement.</summary>
public static class HoneyLuminosityValidation
{
    private static PlayerHoneyEffects effects;
    private static PlayerCreatureStats stats;
    private static Camera playerCamera, otherCamera;
    private static RenderTexture target;
    private static Light sight;
    private static Volume volume;
    private static int playerRenders, otherRenders, ended;
    private static bool wrongState;
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Begin()
    {
        if (!Application.isPlaying || effects != null) throw new InvalidOperationException("Run once in Play Mode.");
        var carrier = Object.FindFirstObjectByType<PlayerCreatureCarrier>();
        effects = PlayerHoneyEffects.Ensure(carrier);
        stats = effects.GetComponent<PlayerCreatureStats>();
        playerCamera = carrier.CarryCamera;
        if (stats.Luminosity != 0f) { effects = null; throw new InvalidOperationException("Start with an uncoated player."); }
        effects.TryApply(GlowHoney.Use.Drink);
        sight = (Light)typeof(PlayerHoneyEffects).GetField("sightLight", Private).GetValue(effects);
        volume = (Volume)typeof(PlayerHoneyEffects).GetField("sightVolume", Private).GetValue(effects);
        var cameraObject = new GameObject("Honey validation observer camera");
        otherCamera = cameraObject.AddComponent<Camera>();
        cameraObject.transform.SetPositionAndRotation(playerCamera.transform.position, playerCamera.transform.rotation);
        target = new RenderTexture(64, 64, 16);
        otherCamera.targetTexture = target;
        otherCamera.depth = playerCamera.depth + 1f;
        playerRenders = otherRenders = ended = 0;
        wrongState = false;
        RenderPipelineManager.beginCameraRendering += ObserveBegin;
        RenderPipelineManager.endCameraRendering += ObserveEnd;
    }

    private static void ObserveBegin(ScriptableRenderContext context, Camera camera)
    {
        if (camera == playerCamera)
        { playerRenders++; wrongState |= !sight.enabled || volume.weight != 1f; }
        else if (camera == otherCamera)
        { otherRenders++; wrongState |= sight.enabled || volume.weight != 0f; }
    }
    private static void ObserveEnd(ScriptableRenderContext context, Camera camera)
    {
        if (camera != playerCamera && camera != otherCamera) return;
        ended++;
        wrongState |= sight.enabled || volume.weight != 0f;
    }

    public static string Finish()
    {
        int checks = 0;
        void Check(bool condition, string text)
        { if (!condition) throw new InvalidOperationException("Honey luminosity: " + text); checks++; }
        try
        {
            Check(effects != null && playerRenders > 0 && otherRenders > 0 && ended > 0, "both real cameras rendered");
            Check(!wrongState, "night vision is present only during the owning camera render");
            Check(stats.Luminosity == 0f, "drinking does not publish luminosity");
            var advance = typeof(PlayerHoneyEffects).GetMethod("AdvanceTime", Private);
            advance.Invoke(effects, new object[] { 1000f });
            Check(!effects.HasHoneySight, "drinking expires");
            var carrier = Object.FindFirstObjectByType<PlayerCreatureCarrier>();
            var honeyObject = new GameObject("Actual honey coating check");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(honeyObject, stats.gameObject.scene);
            var honey = honeyObject.AddComponent<GlowHoney>();
            Check(carrier.TryCarryFromInteraction(honey) && honey.TryUse(GlowHoney.Use.Coat), "held honey coats the player and is consumed");
            var body = (Light)typeof(PlayerHoneyEffects).GetField("bodyLight", Private).GetValue(effects);
            Check(body.gameObject.activeInHierarchy && body.range == 5f, "coating provides a five metre light");
            Check(Mathf.Abs(LuminosityLighting.PointIntensity(stats.Luminosity) - body.intensity) < 0.0001f &&
                stats.Luminosity == 50f, "coating gives 50 luminosity with matching light strength");
            Check(new SerializedObject(stats).FindProperty("luminosity").floatValue == 50f,
                "single player Inspector value includes honey directly");
            effects.TryApply(GlowHoney.Use.Coat);
            Check(Mathf.Abs(LuminosityLighting.PointIntensity(stats.Luminosity) - body.intensity) < 0.0001f, "coating refresh does not stack");
            var bees = Object.FindObjectsByType<GlowBee>(FindObjectsSortMode.None);
            GlowBee bee = null;
            foreach (var candidate in bees) if (!candidate.IsDead) { bee = candidate; break; }
            Check(bee != null && bee.Luminosity == 50f, "living Glow Bees have luminosity 50");
            var glow = bee.GetComponentInChildren<GlowBeeLight>();
            var update = typeof(GlowBeeLight).GetMethod("Update", Private);
            update.Invoke(glow, null);
            var lamp = glow.GetComponent<Light>();
            float before = bee.Luminosity, lightBefore = lamp.intensity;
            bee.SetLuminosity(100f);
            update.Invoke(glow, null);
            Check(Mathf.Abs(bee.Luminosity - before - 50f) < 0.001f &&
                Mathf.Abs(lamp.intensity - lightBefore - LuminosityLighting.PointIntensity(50f)) < 0.001f,
                "changing bee luminosity changes its actual light together");
            Check(new SerializedObject(bee).FindProperty("luminosity").floatValue == bee.Luminosity,
                "single creature Inspector value is its actual brightness");
            bee.SetLuminosity(50f); update.Invoke(glow, null);
            stats.SetLuminosity(stats.Luminosity + 2f);
            advance.Invoke(effects, new object[] { 1000f });
            Check(stats.Luminosity == 2f && !body.gameObject.activeInHierarchy, "expiry subtracts honey's amount while preserving other stat changes");
            stats.SetLuminosity(0f);
            effects.TryApply(GlowHoney.Use.Coat);
            effects.enabled = false;
            Check(stats.Luminosity == 0f && !sight.enabled && volume.weight == 0f, "disable removes glow and camera effects");
            effects.enabled = true;
            return "PASS: " + checks + " honey luminosity checks; camera renders " + playerRenders + "/" + otherRenders;
        }
        finally
        {
            RenderPipelineManager.beginCameraRendering -= ObserveBegin;
            RenderPipelineManager.endCameraRendering -= ObserveEnd;
            if (effects != null) typeof(PlayerHoneyEffects).GetMethod("AdvanceTime", Private).Invoke(effects, new object[] { 1000f });
            if (stats != null) stats.SetLuminosity(0f);
            if (otherCamera != null) Object.DestroyImmediate(otherCamera.gameObject);
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            effects = null;
        }
    }
}
