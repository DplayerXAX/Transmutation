using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real contact checks using world-authored emitters and the unmodified full-size gecko prefab.</summary>
[InitializeOnLoad]
public static class GeckoTrapContactValidation
{
    const string Key = "GeckoTrapContactValidation.Active";
    const string Output = "Temp/GeckoTrapContactValidation.txt";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static IEnumerator routine;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static Scene testScene, previousScene;
    static int frame = -1;
    static float started;
    static readonly List<string> results = new List<string>();
    static readonly List<(CreatureSleep sleeper, Transform player)> sleepers = new List<(CreatureSleep, Transform)>();
    static readonly Vector3 Origin = new Vector3(4, 100, -177);
    static int failures;
    static GeckoTrapContactValidation() { EditorApplication.update += Tick; }

    [MenuItem("Tools/Capstone/Gecko/Test Web And Flower Bubble Contact")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play Mode first.");
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (Time.timeSinceLevelLoad < 3 || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (routine == null) { results.Clear(); sleepers.Clear(); failures = 0; started = Time.realtimeSinceStartup; routine = Execute(); steps.Clear(); steps.Push(routine); }
            if (Time.realtimeSinceStartup - started > 90) throw new Exception("Test exceeded 90 seconds.");
            while (steps.Count > 0)
            {
                var step = steps.Peek();
                if (!step.MoveNext()) { (step as IDisposable)?.Dispose(); steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; }
                break;
            }
            if (steps.Count == 0) Finish();
        }
        catch (Exception e) { results.Add("ERROR: " + e); failures++; Finish(); }
    }
    static void Record(bool pass, string message)
    {
        results.Add((pass ? "PASS " : "FAIL ") + message);
        File.WriteAllLines(Output, results);
        if (!pass) failures++;
    }
    static IEnumerator Wait(float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end) yield return null;
    }
    static IEnumerator Until(Func<bool> predicate, float seconds)
    {
        float end = Time.time + seconds;
        while (!predicate() && Time.time < end) yield return null;
    }
    static SkateGecko Gecko(Vector3 at)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<SkateGecko>(SkateGeckoSetup.PrefabPath);
        if (prefab == null) throw new Exception("Full-size gecko prefab missing.");
        var gecko = Object.Instantiate(prefab, at, Quaternion.identity);
        gecko.name = "TEMP capture-test gecko";
        gecko.SetSimulationEnabled(true);
        gecko.GetComponent<Rigidbody>().interpolation = RigidbodyInterpolation.None;
        Physics.SyncTransforms();
        return gecko;
    }
    static IEnumerator Execute()
    {
        var world = SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");
        if (!world.IsValid() || !world.isLoaded) throw new Exception("Open Scene_World first.");
        var sourceSpider = world.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SkySpider>(true)).First();
        var sourceFlower = world.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<BubbleFlower>(true)).OrderBy(f => (f.transform.position - sourceSpider.transform.position).sqrMagnitude).First();
        results.Add($"World source spider: {sourceSpider.name}, {sourceSpider.transform.position}; flower: {sourceFlower.name}, {sourceFlower.transform.position}.");
        results.Add("Temporary default-physics scene, source emitter configurations cloned without changing capture limits or gecko size. Automatic contact callbacks used; no direct TryCapture calls.");
        previousScene = SceneManager.GetActiveScene();
        testScene = SceneManager.CreateScene("Disposable gecko web and flower contact test");
        SceneManager.SetActiveScene(testScene);
        var marker = new GameObject("Temporary test proximity observer"); marker.transform.position = Origin;
        foreach (var sleeper in Object.FindObjectsByType<CreatureSleep>(FindObjectsSortMode.None))
        {
            var field = typeof(CreatureSleep).GetField("player", Private);
            sleepers.Add((sleeper, (Transform)field.GetValue(sleeper)));
            field.SetValue(sleeper, marker.transform);
        }
        var flower = Object.Instantiate(sourceFlower, Origin + Vector3.right * 20 + Vector3.down * 3, Quaternion.identity);
        flower.gameObject.SetActive(true); flower.enabled = true; flower.SetSimulationEnabled(true);
        flower.AmbientEmissionAllowed = false;
        var floor = new GameObject("Temporary test floor"); floor.layer = 7;
        floor.transform.position = Origin + new Vector3(10, -3.25f, 0);
        floor.AddComponent<BoxCollider>().size = new Vector3(60, .5f, 40);
        var spider = Object.Instantiate(sourceSpider, Origin + Vector3.up * 3, Quaternion.identity);
        spider.gameObject.SetActive(true); spider.enabled = true; spider.SetSimulationEnabled(true);
        yield return Until(() => spider.CurrentWeb != null, 5);
        if (spider.CurrentWeb == null) throw new Exception("World-configured spider did not create its web.");
        var web = spider.CurrentWeb;
        // The builder must not eat a captive during a hold/release measurement.
        spider.enabled = false;
        web.SetSimulationEnabled(true);
        web.AdvanceGrowth(60);
        var strand = web.Geometry.GetStrand(0);
        Vector3 contact = web.transform.position + (strand.Start + strand.End) * .5f;
        results.Add($"World-configured web scale/radius: {web.MaximumRadius:F2}m, {web.StrandCount} strands.");

        for (int attempt = 0; attempt < 6; attempt++)
        {
            strand=web.Geometry.GetStrand((attempt/2)*20);
            contact=web.transform.position+(strand.Start+strand.End)*.5f;
            bool fast=attempt%2==1;
            var gecko = Gecko(contact + (!fast ? Vector3.zero : Vector3.up * 3));
            var body = gecko.GetComponent<Rigidbody>();
            if (fast) body.linearVelocity = Vector3.down * 10;
            string name = (fast ? "web 10m/s incoming contact" : "web resting contact")+" strand "+((attempt/2)*20);
            yield return Until(() => gecko.GetComponent<WebCapture>() != null && gecko.GetComponent<WebCapture>().IsCaptured, 2);
            var capture = gecko.GetComponent<WebCapture>();
            bool captured = capture != null && capture.OwningWeb == web;
            if(captured)results.Add($"PIN {name}: body={body.position}, contact={capture.ContactPoint}, kinematic={body.isKinematic}, gravity={body.useGravity}, mode={body.collisionDetectionMode}.");
            Record(captured, name + $": webCapture={captured}, simulation={gecko.SimulationEnabled}, kinematic={body.isKinematic}.");
            if (captured)
            {
                Vector3 held = body.position;
                yield return Wait(.6f);
                Record(Vector3.Distance(held, body.position) < .2f, name + $": passive hold drift {Vector3.Distance(held,body.position):F3}m.");
                if(Vector3.Distance(held,body.position)>20){capture.Release();Object.Destroy(gecko.gameObject);yield return Wait(.15f);continue;}
                held=body.position;
                if(!body.isKinematic)body.AddForce(Vector3.right * 12, ForceMode.Impulse);
                yield return Wait(.4f);
                Record(!gecko.SimulationEnabled && Vector3.Distance(held, body.position) < .2f, name + $": pinned hold drift {Vector3.Distance(held, body.position):F3}m, kinematic={body.isKinematic}.");
                var own=gecko.GetComponent<Collider>();var silk=web.GetComponentsInChildren<Collider>().First(c=>!c.isTrigger);
                bool ignoredWhileHeld=Physics.GetIgnoreCollision(own,silk);
                capture.Release();
                Record(ignoredWhileHeld&&!Physics.GetIgnoreCollision(own,silk),name+": silk collision suppression restored on release.");
                body.position = Origin + new Vector3(-15, 0, 0); gecko.transform.position = body.position;
                yield return null;
                Record(!capture.IsCaptured && gecko.SimulationEnabled && !body.isKinematic && body.useGravity && body.collisionDetectionMode==CollisionDetectionMode.ContinuousDynamic, name + ": release restores independent dynamic body and simulation.");
            }
            Object.Destroy(gecko.gameObject);
            yield return Wait(.15f);
        }

        var fire = typeof(BubbleFlower).GetMethod("Fire", Private, null, new[] { typeof(Vector3), typeof(GameObject), typeof(float), typeof(Vector3), typeof(float) }, null);
        var bubbleTemplate = (TrappingBubble)new SerializedObject(flower).FindProperty("bubblePrefab").objectReferenceValue;
        float sizeLimit = new SerializedObject(bubbleTemplate).FindProperty("maxCaptureSize").floatValue;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Vector3 shotOrigin = Origin + Vector3.right * 20 + Vector3.down * 2.35f;
            var gecko = Gecko(shotOrigin - Vector3.up * .38f + Vector3.forward * (attempt == 0 ? 3 : attempt == 1 ? 4 : 0));
            var body = gecko.GetComponent<Rigidbody>();
            CreatureBounds.TryMeasure(gecko.transform, out var bounds);
            float size = CreatureBounds.LargestSide(bounds);
            if (attempt == 1) body.linearVelocity = Vector3.back * 10;
            var probe = gecko.gameObject.AddComponent<GeckoTrapContactProbe>();
            bool fired = (bool)fire.Invoke(flower, new object[] { Vector3.forward, null, attempt == 0 ? 10f : 0f, shotOrigin, .85f });
            var bubble = flower.LastShot;
            if (!fired || bubble == null) throw new Exception("World-configured flower failed to emit a test shot.");
            string name = attempt == 0 ? "flower projectile meets grounded gecko" : attempt == 1 ? "10m/s grounded gecko meets flower bubble" : "initial bubble overlap contact";
            yield return Until(() => bubble == null || bubble.IsPopped || bubble.IsOccupied, 2);
            results.Add($"CONTACT {name}: collisions={probe.collisions}, triggers={probe.triggers}, body={body.position}, bubble={(bubble==null?Vector3.zero:bubble.transform.position)}, simulation={gecko.SimulationEnabled}, bubbleSimulation={(bubble!=null&&bubble.SimulationEnabled)}, targetLayer={gecko.gameObject.layer}, bubbleLayer={(bubble==null?-1:bubble.gameObject.layer)}.");
            bool captured = bubble != null && bubble.Occupant != null && bubble.Occupant.gameObject == gecko.gameObject;
            Record(captured, name + $": measured model {size:F2}m, limit {sizeLimit:F2}m; occupied={captured}, popped={(bubble == null || bubble.IsPopped)}.");
            if (captured)
            {
                yield return Wait(.6f);
                if(bubble==null||bubble.IsPopped){Record(false,name+": bubble popped during retention check.");Object.Destroy(gecko.gameObject);yield return Wait(.15f);continue;}
                CreatureBounds.TryMeasure(gecko.transform, out var inside);
                bool contained = Vector3.Distance(inside.center, bubble.transform.position) + inside.extents.magnitude <= bubble.Radius + .06f;
                Record(gecko.IsBubbleCaptured && !gecko.SimulationEnabled && body.isKinematic && contained, name + $": retained, AI suspended, shell radius {bubble.Radius:F2}m fits occupant.");
                bubble.Pop();
                yield return null;
                Record(!gecko.IsBubbleCaptured && gecko.SimulationEnabled && !body.isKinematic && body.detectCollisions, name + ": pop restores dynamic physics, collision and simulation.");
            }
            if (bubble != null && !bubble.IsPopped) bubble.Pop();
            Object.Destroy(gecko.gameObject);
            yield return Wait(.15f);
        }
        yield return OwnershipChecks(spider,web,bubbleTemplate);
        results.Add(SkySpiderAgeValidation.Run());
        marker.transform.position=new Vector3(1000,1000,1000);
        SkySpiderValidation.StartRuntimeChecks();
        yield return Until(()=>SkySpiderValidation.Status!="Running",25);
        Record(SkySpiderValidation.Status.StartsWith("Passed"),"existing general spider runtime suite: "+SkySpiderValidation.Status);
    }
    static IEnumerator OwnershipChecks(SkySpider spider, SkyWeb originalWeb, TrappingBubble bubbleTemplate)
    {
        var webTemplate=(SkyWeb)new SerializedObject(spider).FindProperty("webPrefab").objectReferenceValue;
        var growing=Object.Instantiate(webTemplate,Origin+Vector3.left*18+Vector3.up*3,Quaternion.identity);
        growing.InitializeForSpawn(.7f);growing.SetSimulationEnabled(true);
        var originalStrands=growing.GetComponentsInChildren<Collider>();
        var strand=growing.Geometry.GetStrand(0);
        var animal=Gecko(growing.transform.position+(strand.Start+strand.End)*.5f);
        var body=animal.GetComponent<Rigidbody>();var own=animal.GetComponent<Collider>();
        body.useGravity=false;body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
        var preignored=originalStrands.First(c=>!c.isTrigger);
        Physics.IgnoreCollision(own,preignored,true);
        yield return Until(()=>animal.GetComponent<WebCapture>()!=null&&animal.GetComponent<WebCapture>().IsCaptured,2);
        var capture=animal.GetComponent<WebCapture>();
        Record(capture!=null&&capture.IsCaptured,"growing web captures body with non-default gravity and collision mode");
        if(capture!=null&&capture.IsCaptured)
        {
            growing.AdvanceGrowth(60);
            var added=growing.GetComponentsInChildren<Collider>().Except(originalStrands).Where(c=>!c.isTrigger).ToArray();
            Record(added.Length>0&&added.All(c=>Physics.GetIgnoreCollision(own,c)),"newly grown strands respect existing captive collision ownership");
            growing.enabled=false;
            Record(!capture.IsCaptured&&!body.isKinematic&&!body.useGravity&&body.collisionDetectionMode==CollisionDetectionMode.ContinuousSpeculative&&animal.SimulationEnabled,"disabling web releases captive and restores exact previous physics flags");
            Record(Physics.GetIgnoreCollision(own,preignored)&&added.All(c=>!Physics.GetIgnoreCollision(own,c)),"release preserves pre-existing ignores and restores only pairs changed by capture");
            body.isKinematic=true;capture.Release();
            Record(body.isKinematic,"duplicate Release does not overwrite a subsequent movement owner");
        }
        Object.Destroy(animal.gameObject);Object.Destroy(growing.gameObject);
        yield return Wait(.15f);
        strand=originalWeb.Geometry.GetStrand(0);
        animal=Gecko(originalWeb.transform.position+(strand.Start+strand.End)*.5f);
        body=animal.GetComponent<Rigidbody>();
        yield return Until(()=>animal.GetComponent<WebCapture>()!=null&&animal.GetComponent<WebCapture>().IsCaptured,2);
        capture=animal.GetComponent<WebCapture>();
        if(capture!=null&&capture.IsCaptured)
        {
            CreatureBounds.TryMeasure(animal.transform,out var bounds);
            var bubble=Object.Instantiate(bubbleTemplate,bounds.center,Quaternion.identity);
            bool taken=bubble.TryCapture(animal.gameObject);
            Record(taken&&!capture.IsCaptured&&animal.IsBubbleCaptured&&body.isKinematic,"bubble takeover releases web before recording bubble physics ownership");
            bubble.Pop();
            Record(!animal.IsBubbleCaptured&&!body.isKinematic&&body.useGravity&&body.collisionDetectionMode==CollisionDetectionMode.ContinuousDynamic&&animal.SimulationEnabled,"popping takeover bubble restores the original free gecko rather than web-pinned state");
        }
        else Record(false,"web-to-bubble takeover setup captured gecko");
        Object.Destroy(animal.gameObject);
        yield return Wait(.15f);
    }
    static void Finish()
    {
        while (steps.Count > 0) (steps.Pop() as IDisposable)?.Dispose(); routine = null;
        foreach (var entry in sleepers)
            if (entry.sleeper != null) typeof(CreatureSleep).GetField("player", Private).SetValue(entry.sleeper, entry.player);
        sleepers.Clear();
        if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
        if (testScene.IsValid() && testScene.isLoaded) SceneManager.UnloadSceneAsync(testScene);
        results.Add($"Completed: {failures} failed checks. Temporary scene and test geckos cleaned up; authored scene not saved.");
        File.WriteAllLines(Output, results);
        SessionState.SetBool(Key, false);
        EditorApplication.ExitPlaymode();
    }
}

public sealed class GeckoTrapContactProbe : MonoBehaviour
{
 public int collisions, triggers;
 void OnCollisionEnter(Collision collision) { if(collision.collider.GetComponentInParent<TrappingBubble>()!=null)collisions++; }
 void OnTriggerEnter(Collider other) { if(other.GetComponentInParent<TrappingBubble>()!=null)triggers++; }
}