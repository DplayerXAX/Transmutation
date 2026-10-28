using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class YoYoLightEcologyValidation
{
    public static IEnumerator Run()
    {
        Action<bool, string> check = FlashBloomValidation.Check;
        EditorSceneManager.LoadSceneInPlayMode("Assets/YoYoLightEcologyValidation.unity", new LoadSceneParameters(LoadSceneMode.Single));
        yield return null;
        var habitat = Object.FindFirstObjectByType<LightHabitat>();
        check(habitat != null, "YoYo contains the authored luminous habitat.");
        check(habitat.GetComponentsInChildren<FlashBloom>().Length == 2 &&
            habitat.GetComponentsInChildren<LightMoth>().Length == 6 && habitat.GetComponentsInChildren<AfterglowPod>().Length == 2,
            "All three species are authored together in the existing scene.");
        var colony = Object.FindFirstObjectByType<GlowBeeColony>();
        float until = Time.time + 3f;
        while (colony.Nests.Count == 0 && Time.time < until) yield return null;
        check(colony.Nests.Count == 1, "The existing colony builds a nest inside the shelter.");
        var nest = colony.Nests[0];
        check(nest.TryStoreNectar(nest.NectarPerHoney), "The existing nest accepts real gathered nectar.");
        typeof(GlowBeeNest).GetMethod("TickCreature", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(nest, new object[] { 15f });
        check(nest.HoneyCount > 0, "The real bee nest ferments nectar into honey.");
        var carrier = Object.FindFirstObjectByType<PlayerCreatureCarrier>();
        var honey = nest.GetComponentsInChildren<GlowHoney>().First(h => !h.IsDead);
        check(carrier.TryCarryFromInteraction(honey) && honey.TryUse(GlowHoney.Use.Coat), "Bee-produced honey coats the actual YoYo player.");
        var stats = carrier.GetComponentInParent<PlayerCreatureStats>();
        var visuals = stats.GetComponent<PlayerLuminosityVisuals>();
        visuals.Refresh();
        check(stats.Luminosity == 50f && visuals.Lamp != null &&
            Mathf.Abs(visuals.Lamp.intensity - LuminosityLighting.PointIntensity(stats.Luminosity)) < 0.001f,
            "Player fluorescence uses the same stat and light conversion as YoYo's Glow Bees.");
        check(visuals.Lamp.GetComponent<GlowBeeLight>() != null, "Player and bees use the very same visual driver.");
        check(!Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Any(t =>
            t.name == "Sweet scent mote" || t.name == "Honey on hand" || t.name == "Honey glow and sweet scent"),
            "Old honey motes and hand beads are absent.");
        yield return null;
        var body = Object.FindFirstObjectByType<FirstPersonBody>();
        var renderers = (Renderer[])typeof(FirstPersonBody).GetField("renderers", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(body);
        var block = new MaterialPropertyBlock();
        renderers[0].GetPropertyBlock(block);
        check(block.GetColor("_EmissionColor").maxColorComponent > 0f && renderers[0].sharedMaterial.HasProperty("_EmissionColor"),
            "The actual first-person body material receives shared fluorescence emission.");
        var moth = habitat.GetComponentsInChildren<LightMoth>()[0];
        var playerBody = carrier.PlayerBody;
        playerBody.position = habitat.transform.TransformPoint(new Vector3(0f, 1.1f, -1.6f));
        playerBody.linearVelocity = Vector3.zero;
        moth.GetComponent<Rigidbody>().position = playerBody.position + Vector3.right * 0.7f + Vector3.up * 0.5f;
        Physics.SyncTransforms();
        until = Time.time + 0.15f;
        while (Time.time < until) yield return null;
        moth.Sense();
        check(moth.TargetRoot == stats.transform, "Under the real shelter, the honey-coated player attracts a nearby moth.");
        habitat.ToggleLight(); moth.Sense();
        check(habitat.habitatLight.enabled && moth.TargetRoot == null, "The integrated lamp removes attraction by reducing contrast.");
        habitat.ToggleLight(); moth.Sense();
        check(moth.TargetRoot == stats.transform, "Turning the integrated lamp off restores attraction.");
        stats.GetComponent<HoneyLuminosityEffect>().AdvanceTime(61f); visuals.Refresh();
        check(stats.Luminosity == 0f && !visuals.Lamp.enabled, "Honey expiry removes emitted light without leaving old visuals.");
    }
}
