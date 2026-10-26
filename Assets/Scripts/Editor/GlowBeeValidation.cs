using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Batch integration checks for the authored Ariia colony. Run in an isolated project copy.</summary>
public static class GlowBeeValidation
{
    private const string Key = "GlowBeeValidation.Batch";
    private static IEnumerator routine;
    private static double deadline;
    private static int checks;

    [InitializeOnLoadMethod]
    private static void Install() => EditorApplication.playModeStateChanged += OnPlayMode;

    public static void RunBatch()
    {
        string path = Application.dataPath.Replace('\\', '/');
        if (!path.Contains("/Temp/GlowBeeChecks/UnityProject/Assets") && !path.Contains("/.utmp/GlowBeeChecks/UnityProject/Assets"))
            throw new InvalidOperationException("Run these batch checks in Temp/GlowBeeChecks/UnityProject.");
        EditorSceneManager.OpenScene("Assets/Scenes/Scene_Ariia.unity");
        // Native audio is unrelated to this check and is not installed in the isolated project.
        foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            if (behaviour != null && behaviour.GetType().Name.StartsWith("Ak", StringComparison.Ordinal))
                Object.DestroyImmediate(behaviour);
        EditorSceneManager.SaveOpenScenes();
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    private static void OnPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.SetBool(Key, false);
        checks = 0;
        Time.timeScale = 4f;
        deadline = EditorApplication.timeSinceStartup + 100;
        routine = Run();
        EditorApplication.update += Step;
    }

    private static void Step()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Glow bee checks timed out.");
            if (routine.MoveNext()) return;
            Finish(0, "PASS: " + checks + " glow bee integration checks");
        }
        catch (Exception error) { Finish(1, error.ToString()); }
    }

    private static IEnumerator Run()
    {
        yield return null;
        var colony = Object.FindFirstObjectByType<GlowBeeColony>();
        Check(colony != null, "Ariia scene contains colony");
        Check(colony.Nests.Count == 3, "three separated hidden nests generated");
        Check(colony.Flowers.Count == 15, "fifteen renewable nectar flowers generated");
        Check(Object.FindObjectsByType<GlowBee>(FindObjectsSortMode.None).Length == 12, "twelve bees generated");
        foreach (GlowBeeNest nest in colony.Nests)
        {
            bool clear = true;
            foreach (Collider hit in Physics.OverlapSphere(nest.transform.position + Vector3.up * 0.05f, 0.45f,
                colony.SceneryLayers, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(colony.transform) && hit.attachedRigidbody == null) clear = false;
            Check(clear, "nest does not overlap scenery");
        }
        NectarFlower flower = colony.Flowers[0];
        float before = flower.Nectar;
        Check(flower.TakeNectar(1f) == 1f && flower.Nectar < before, "sipping consumes real nectar");
        float end = Time.time + 85f;
        GlowHoney honey = null;
        while (Time.time < end && honey == null)
        {
            honey = Object.FindFirstObjectByType<GlowHoney>();
            yield return null;
        }
        if (honey == null)
        {
            foreach (GlowBeeNest nest in colony.Nests) Debug.Log($"NEST {nest.name}: nectar={nest.StoredNectar}, fermentation={nest.FermentationProgress}");
            foreach (GlowBee bee in Object.FindObjectsByType<GlowBee>(FindObjectsSortMode.None)) Debug.Log($"BEE {bee.State}: {bee.transform.position} nectar={bee.CarriedNectar}");
        }
        Check(honey != null, "autonomous forage-return-ferment loop produces honey");
        Check(honey.GetComponentInChildren<Light>() != null, "honey has a real local light");
        GlowBeeNest home = honey.GetComponentInParent<GlowBeeNest>();
        int previousCount = home.HoneyCount;
        var carrier = Object.FindFirstObjectByType<PlayerCreatureCarrier>();
        Check(carrier != null && carrier.TryCarryFromInteraction(honey), "existing player carrier picks up honey");
        Check(home.HoneyCount == previousCount - 1, "harvest frees a production slot");
        carrier.Release();
        Check(!honey.GetComponent<Rigidbody>().isKinematic && honey.GetComponent<Rigidbody>().useGravity &&
            honey.GetComponent<Rigidbody>().constraints == RigidbodyConstraints.None, "dropped honey regains normal physics");
        Check(honey.transform.parent == null, "harvested honey is independent of hive");
        var spider = Object.FindFirstObjectByType<SkySpider>();
        Check(spider != null && spider.CurrentWeb != null, "existing spider and its web spawn");
        GlowBee prey = null;
        foreach (GlowBee candidate in Object.FindObjectsByType<GlowBee>(FindObjectsSortMode.None))
            if (!candidate.IsDead && candidate.SimulationEnabled && !candidate.GetComponent<WebCapture>().IsCaptured)
            { prey = candidate; break; }
        Check(prey != null, "some foragers survive the predator");
        var body = prey.GetComponent<Rigidbody>();
        var capture = prey.GetComponent<WebCapture>();
        Vector3 capturePoint = spider.GetComponent<Rigidbody>().position + Vector3.right * 0.2f;
        // Keep interpolation's render pose and the immediate physics query in agreement.
        body.interpolation = RigidbodyInterpolation.None;
        body.position = capturePoint;
        prey.transform.position = capturePoint;
        Physics.SyncTransforms();
        Check(spider.CurrentWeb.TryCapture(body, body.position), "spider silk catches a dynamic flying bee");
        Check(!prey.SimulationEnabled, "capture suspends autonomous flight");
        float spiderAge = spider.Age;
        bool hunted = spider.TryDrainCapturedAge(capture);
        if (!hunted) Debug.Log($"HUNT prey age={prey.Age} dead={prey.IsDead} polarity={prey.Polarity}, spider age={spider.Age} returning={spider.IsReturning}, distance={Vector3.Distance(body.position, spider.GetComponent<Rigidbody>().position)}");
        Check(hunted, "spider can hunt its captured bee");
        Check(prey.IsDead && spider.Age > spiderAge, "predation kills bee and transfers remaining age");
        end = Time.time + 65f;
        while (home.HoneyCount == 0 && Time.time < end) yield return null;
        Check(home.HoneyCount > 0, "hive replenishes honey after harvest");
        // A full hive cannot accumulate unlimited products or nectar.
        Check(!home.TryStoreNectar(float.NaN) && !home.TryStoreNectar(float.PositiveInfinity) &&
            !home.TryStoreNectar(1000f), "storage rejects invalid and excessive deposits");

        var effects = PlayerHoneyEffects.Ensure(carrier);
        var hidden = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hidden.name = "Future transparent creature test";
        hidden.tag = "transparent";
        hidden.transform.position = home.Entrance + Vector3.up * 10f;
        var hiddenCreature = hidden.AddComponent<GlowHoney>();
        var hiddenRenderer = hidden.GetComponent<Renderer>();
        Material original = hiddenRenderer.sharedMaterial;
        float refresh = Time.time + 0.6f;
        while (Time.time < refresh) yield return null;
        Check(hiddenRenderer.forceRenderingOff, "future transparent-tagged creature is normally hidden");
        GlowHoney drink = home.GetComponentInChildren<GlowHoney>();
        Check(drink != null && carrier.TryCarryFromInteraction(drink), "pick up honey for drink choice");
        Check(drink.TryUse(GlowHoney.Use.Drink) && effects.HasHoneySight && !effects.IsScented,
            "drinking grants sight without coating");
        Check(!drink.TryUse(GlowHoney.Use.Coat), "one honey cannot be used twice");
        Check(!hiddenRenderer.forceRenderingOff && hiddenRenderer.sharedMaterial != original,
            "honey sight reveals future transparent creatures");
        typeof(PlayerHoneyEffects).GetMethod("AdvanceTime", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic).Invoke(effects, new object[] { 1000f });
        Check(!effects.HasHoneySight && hiddenRenderer.forceRenderingOff && hiddenRenderer.sharedMaterial == original,
            "expired sight hides creature and restores original material");
        // Production and replenishment were checked above. Use a fresh factory product here so
        // the choice assertion does not depend on random predator losses during another full batch.
        GlowHoney coating = (GlowHoney)typeof(GlowBeeColony).GetMethod("CreateHoney",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(colony, new object[] { home, 0 });
        Check(coating != null && carrier.TryCarryFromInteraction(coating) && coating.TryUse(GlowHoney.Use.Coat),
            "coating choice consumes a second honey");
        Check(effects.IsScented && !effects.HasHoneySight && effects.GetComponentsInChildren<Light>().Length >= 1,
            "coating grants glow and scent independently of sight");
        typeof(PlayerHoneyEffects).GetMethod("AdvanceTime", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic).Invoke(effects, new object[] { 1000f });
        Check(!effects.IsScented, "coating and scent expire");
        var playerBody = carrier.PlayerController.GetComponent<Rigidbody>();
        playerBody.constraints = RigidbodyConstraints.FreezeAll;
        playerBody.linearVelocity = Vector3.zero;
        playerBody.position = home.Entrance;
        carrier.PlayerController.transform.position = home.Entrance;
        Physics.SyncTransforms();
        refresh = Time.time + 0.6f;
        while (Time.time < refresh) yield return null;
        Check(effects.NearbyNest != null && effects.NestReadout.Contains("Honey:") && effects.NestReadout.Contains("Nectar:"),
            "approaching hive exposes live honey and nectar storage");
        GlowBee visitor = null;
        foreach (GlowBee candidate in Object.FindObjectsByType<GlowBee>(FindObjectsSortMode.None))
            if (!candidate.IsDead && candidate.SimulationEnabled && candidate.CarriedNectar == 0f)
            { visitor = candidate; break; }
        Check(visitor != null, "free bee available for player orbit");
        visitor.GetComponent<Rigidbody>().position = home.Entrance + Vector3.right * 2f;
        // Remove the predator for this isolated social-behavior assertion; it otherwise has priority.
        spider.gameObject.SetActive(false);
        end = Time.time + 12f;
        while (visitor.State != GlowBee.Activity.CirclingPlayer && Time.time < end) yield return null;
        Check(visitor.State == GlowBee.Activity.CirclingPlayer, "nearby bee circles player");
        Object.Destroy(hiddenCreature.gameObject);
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        checks++;
        Debug.Log("PASS: " + description);
    }

    private static void Finish(int code, string message)
    {
        EditorApplication.update -= Step;
        Time.timeScale = 1f;
        Directory.CreateDirectory("../Results");
        File.WriteAllText("../Results/integration.txt", message);
        Debug.Log(message);
        EditorApplication.Exit(code);
    }
}
