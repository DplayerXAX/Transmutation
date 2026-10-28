using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Batch checks in the disposable Unity copy, including real collision and player throws.</summary>
public static class FlashBloomValidation
{
    private const string Key = "FlashBloomValidation.Run";
    private static IEnumerator routine;
    private static int checks, lastFrame;
    private static double deadline;
    private static string Result => Path.GetFullPath(Path.Combine(Application.dataPath, "../../flash-bloom-results.txt"));

    [InitializeOnLoadMethod]
    private static void Install() => EditorApplication.playModeStateChanged += OnPlay;

    public static void RunBatch()
    {
        if (!Application.dataPath.Replace('\\', '/').Contains("/.utmp/GlowBeeChecks/UnityProject/Assets"))
            throw new InvalidOperationException("Run batch validation in the disposable project copy.");
        FlashBloomSetup.CreateDemo(true);
        LightEcologySetup.CreateDemo(true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Key, true);
        EditorApplication.isPlaying = true;
    }

    private static void OnPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
        SessionState.SetBool(Key, false);
        checks = 0; lastFrame = -1;
        deadline = EditorApplication.timeSinceStartup + 60;
        routine = Run();
        EditorApplication.update += Pump;
    }

    private static void Pump()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Flash Bloom checks timed out.");
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            if (routine.MoveNext()) return;
            File.WriteAllText(Result, "PASS: " + checks + " light ecology checks: Flash Bloom, contrast-dependent Light Moth, Afterglow Pod and their combined interaction.");
            EditorApplication.update -= Pump;
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            File.WriteAllText(Result, "FAIL after " + checks + " checks: " + e);
            Debug.LogException(e);
            EditorApplication.update -= Pump;
            EditorApplication.Exit(1);
        }
    }

    internal static void Check(bool value, string text)
    {
        if (!value) throw new InvalidOperationException(text);
        checks++;
    }

    private static IEnumerator Run()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FlashBloomSetup.PrefabPath);
        var bloom = Object.Instantiate(prefab, new Vector3(0f, 2f, 0f), Quaternion.identity).GetComponent<FlashBloom>();
        bloom.GetComponent<Rigidbody>().isKinematic = true;
        var exposed = new GameObject("Exposed observer").AddComponent<CreatureLifecycleValidationProbe>();
        exposed.transform.position = new Vector3(-3f, 2f, 0f);
        exposed.gameObject.AddComponent<SphereCollider>().radius = 0.3f;
        var hidden = new GameObject("Occluded observer").AddComponent<CreatureLifecycleValidationProbe>();
        hidden.transform.position = new Vector3(3f, 2f, 0f);
        hidden.gameObject.AddComponent<SphereCollider>().radius = 0.3f;
        var far = new GameObject("Out of range observer").AddComponent<CreatureLifecycleValidationProbe>();
        far.transform.position = new Vector3(0f, 2f, 15f);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(1.5f, 2f, 0f);
        wall.transform.localScale = new Vector3(0.4f, 4f, 4f);
        Physics.SyncTransforms();
        yield return null;
        Check(Mathf.Approximately(bloom.Luminosity, 3f), "Idle bloom has luminosity 3.");
        var lamp = bloom.GetComponentInChildren<Light>();
        Check(lamp != null && Mathf.Abs(lamp.intensity - LuminosityLighting.PointIntensity(3f)) < 0.001f,
            "Idle light uses the shared luminosity conversion.");
        var contact = wall.GetComponent<Collider>();
        bloom.ReceiveImpact(new CreatureImpact(contact, bloom.transform.position, Vector3.up, Vector3.down, Vector3.down));
        Check(!bloom.IsFlashing, "Gentle contacts do not flash.");
        Check(bloom.TryFlash() && bloom.Luminosity == 1800f, "Danger creates a strong shared light source.");
        Check(FlashBlindness.IsBlinded(exposed), "Exposed nearby observer is blinded.");
        Check(!FlashBlindness.IsBlinded(hidden), "Wall blocks blindness.");
        Check(!FlashBlindness.IsBlinded(far), "Distant observer is unaffected.");
        Check(!FlashBlindness.IsBlinded(bloom), "Bloom is immune to its own flash.");
        Check(!bloom.TryFlash(), "Repeated triggers respect cooldown.");
        RaycastHit[] sightHits = new RaycastHit[8];
        Check(!CreatureSpatialQuery.Visible(exposed, bloom, exposed.AimPosition, ref sightHits),
            "Blindness blocks the shared visual line-of-sight API.");
        var readings = new List<LuminosityReading>();
        LuminosityQuery.Nearby(exposed.gameObject.scene, exposed.AimPosition, 20f, 0f, exposed.transform, 0, readings);
        Check(readings.Count == 0, "Blinded observers cannot discover luminous targets.");
        exposed.GetComponent<FlashBlindness>().Advance(4f);
        LuminosityQuery.Nearby(exposed.gameObject.scene, exposed.AimPosition, 20f, 0f, exposed.transform, 0, readings);
        Check(readings.Exists(r => ReferenceEquals(r.Source, bloom)), "Light perception recovers after blindness.");
        Check(CreatureSpatialQuery.Visible(exposed, bloom, exposed.AimPosition, ref sightHits),
            "Ordinary sight returns when blindness expires.");
        yield return null;
        Check(Mathf.Abs(lamp.intensity - LuminosityLighting.PointIntensity(bloom.Luminosity)) < 0.001f,
            "Flash lighting follows the same live luminosity value as perception.");

        // Exercise actual AI consumers, rather than only checking the status flag.
        var fighterObject = new GameObject("Dazzled fighter");
        fighterObject.SetActive(false);
        fighterObject.transform.position = new Vector3(-5f, 2f, 0f);
        fighterObject.AddComponent<SphereCollider>().radius = 0.3f;
        var fighter = fighterObject.AddComponent<MeleeAsura>();
        fighter.GetComponent<Rigidbody>().isKinematic = true;
        exposed.gameObject.AddComponent<Rigidbody>().isKinematic = true;
        var fighterFields = new SerializedObject(fighter);
        fighterFields.FindProperty("initialPolarity").enumValueIndex = (int)CreaturePolarity.Dark;
        fighterFields.FindProperty("polarity").enumValueIndex = (int)CreaturePolarity.Dark;
        fighterFields.ApplyModifiedPropertiesWithoutUndo();
        var targetFields = new SerializedObject(exposed);
        targetFields.FindProperty("polarity").enumValueIndex = (int)CreaturePolarity.Light;
        targetFields.ApplyModifiedPropertiesWithoutUndo();
        fighterObject.SetActive(true);
        Physics.SyncTransforms();
        var combat = fighter.GetComponent<CreatureCombat>();
        typeof(CreatureCombat).GetField("nextSearch", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(combat, 0f);
        combat.Tick(0.01f);
        Check(combat.Target == exposed, "Sighted Asura acquires the nearby target.");
        FlashBlindness.Apply(fighter, 3f);
        combat.Tick(0.01f);
        Check(combat.Target == null && combat.State == CreatureCombatState.Idle,
            "Blinded Asura cancels target and attack state.");
        fighter.GetComponent<FlashBlindness>().Advance(4f);
        typeof(CreatureCombat).GetField("nextSearch", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(combat, 0f);
        combat.Tick(0.01f);
        Check(combat.Target == exposed, "Asura can acquire targets again after recovery.");
        Object.Destroy(fighterObject);

        var birdObject = new GameObject("Dazzled bird");
        birdObject.transform.position = new Vector3(-6f, 3f, 0f);
        var bird = birdObject.AddComponent<BirdCreature>();
        var birdFields = new SerializedObject(bird);
        birdFields.FindProperty("dietTags").intValue = -1;
        birdFields.ApplyModifiedPropertiesWithoutUndo();
        var foodDirection = typeof(BirdCreature).GetMethod("FoodDirection", BindingFlags.NonPublic | BindingFlags.Instance);
        object[] foodArgs = { bird.transform.position, new List<Creature> { exposed }, new List<Vector3> { exposed.transform.position } };
        Check(((Vector3)foodDirection.Invoke(bird, foodArgs)).sqrMagnitude > 0f, "Sighted hungry bird seeks food.");
        FlashBlindness.Apply(bird, 3f);
        Check(((Vector3)foodDirection.Invoke(bird, foodArgs)) == Vector3.zero && bird.FoodTarget == null,
            "Blinded bird loses food target without disabling flight physics.");
        bird.GetComponent<FlashBlindness>().Advance(4f);
        Check(((Vector3)foodDirection.Invoke(bird, foodArgs)).sqrMagnitude > 0f, "Bird resumes food seeking after recovery.");
        Object.Destroy(birdObject);
        bloom.AdvanceFlash(1f);
        Check(!bloom.IsFlashing && Mathf.Approximately(bloom.Luminosity, 3f), "Flash fades to idle.");
        Check(!bloom.TryFlash(), "Cooldown continues after the visual flash.");
        bloom.AdvanceFlash(9f);
        bloom.ApplyDamage(1f);
        Check(bloom.IsFlashing, "Damage is another danger trigger.");
        bloom.AdvanceFlash(10f);
        bloom.ReceiveImpact(new CreatureImpact(contact, bloom.transform.position, Vector3.up, Vector3.down * 6f, Vector3.down * 6f));
        Check(bloom.IsFlashing, "Shared high-speed impact triggers the defense.");
        bloom.Die();
        Check(bloom.Luminosity == 0f && !bloom.TryFlash(), "Dead blooms stop emitting and cannot flash.");
        Object.Destroy(bloom.gameObject); Object.Destroy(wall);
        Object.Destroy(exposed.gameObject); Object.Destroy(hidden.gameObject); Object.Destroy(far.gameObject);
        yield return null;

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Physical test floor"; floor.layer = 7;
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(60f, 1f, 60f);
        bloom = Object.Instantiate(prefab, Vector3.up * 3f, Quaternion.identity).GetComponent<FlashBloom>();
        Physics.SyncTransforms();
        float until = Time.time + 3f;
        while (!bloom.IsFlashing && Time.time < until) yield return null;
        Check(bloom.IsFlashing, "Actual Rigidbody falling onto the floor triggers the impact relay.");
        Object.Destroy(bloom.gameObject);
        yield return null;

        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerObjects.prefab");
        var player = Object.Instantiate(playerPrefab, new Vector3(0f, 2f, -5f), Quaternion.identity);
        yield return null;
        var carrier = player.GetComponentInChildren<PlayerCreatureCarrier>();
        var camera = player.GetComponentInChildren<Camera>();
        camera.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
        bloom = Object.Instantiate(prefab, camera.transform.position + camera.transform.forward, Quaternion.identity).GetComponent<FlashBloom>();
        Check(carrier.TryCarryFromInteraction(bloom), "Real player can carry the authored prefab.");
        var thrower = carrier.GetComponent<CreatureThrower>();
        Check(thrower != null, "Player has the shared thrower.");
        thrower.Throw(1f);
        Check(!bloom.IsCarried, "Throw releases carry ownership.");
        until = Time.time + 4f;
        while (!bloom.IsFlashing && Time.time < until) yield return null;
        Check(bloom.IsFlashing && thrower.LastHit != null, "Actual charged player throw triggers a flash on landing.");
        Check(bloom.SimulationEnabled && !bloom.GetComponent<Rigidbody>().isKinematic,
            "Landing returns movement ownership before the flash reaction.");
        var mothChecks = LightMothValidation.Run();
        while (mothChecks.MoveNext()) yield return mothChecks.Current;
        var podChecks = AfterglowPodValidation.Run();
        while (podChecks.MoveNext()) yield return podChecks.Current;
    }
}
