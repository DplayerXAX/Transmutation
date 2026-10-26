using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Disposable impact fixtures, including real Play Mode contact callbacks.</summary>
public static class CreatureImpactValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IEnumerator routine;
    private static int lastFrame;
    private static double deadline;
    public static string Status { get; private set; } = "Not run";

    [MenuItem("Tools/Creatures/Goo/Validate Shared Impacts (Play Mode)")]
    public static void Start()
    {
        if (!Application.isPlaying || EditorApplication.isPaused)
            throw new InvalidOperationException("Enter unpaused Play Mode first.");
        if (routine != null) throw new InvalidOperationException("Impact checks are already running.");
        Status = "Running";
        lastFrame = -1;
        deadline = EditorApplication.timeSinceStartup + 45;
        routine = Run();
        EditorApplication.update += Pump;
    }

    private static void Pump()
    {
        try
        {
            if (!Application.isPlaying || EditorApplication.timeSinceStartup > deadline)
                throw new InvalidOperationException("Impact checks stopped or timed out.");
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            if (routine.MoveNext()) return;
        }
        catch (Exception exception) { Status = "FAIL: " + exception; Debug.LogException(exception); }
        EditorApplication.update -= Pump;
        (routine as IDisposable)?.Dispose();
        routine = null;
    }

    private static IEnumerator Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Creature impact: " + message);
            checks++;
        }
        void Set(object target, string field, object value) =>
            (target.GetType().GetField(field, Private) ?? typeof(Creature).GetField(field, Private)).SetValue(target, value);
        void Dispatch(Creature target, CreatureImpact impact) => typeof(CreatureImpactRelay)
            .GetMethod("Dispatch", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { target, impact });
        Scene scene = SceneManager.CreateScene("Creature Impact Validation " + Guid.NewGuid());
        var stage = new GameObject("Impact fixtures"); SceneManager.MoveGameObjectToScene(stage, scene);
        Vector3 origin = new Vector3(20000f, 100f, 20000f);
        float oldTimeScale = Time.timeScale;
        GameObject Solid(string name, Vector3 offset, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.SetParent(stage.transform, false);
            go.transform.position = origin + offset; go.transform.localScale = scale;
            return go;
        }
        CarryThrowValidationProbe Probe(Vector3 offset, bool kinematic = true)
        {
            var go = Solid("Impact probe", offset, Vector3.one * 0.5f);
            var body = go.AddComponent<Rigidbody>(); body.isKinematic = kinematic; body.useGravity = !kinematic;
            return go.AddComponent<CarryThrowValidationProbe>();
        }
        GooCreature Goo(Vector3 offset)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GooCreatureSetup.PrefabPath);
            var go = Object.Instantiate(prefab, origin + offset, Quaternion.identity, stage.transform);
            return go.GetComponent<GooCreature>();
        }
        GooPaint[] Paint() => stage.GetComponentsInChildren<GooPaint>();
        void ClearPaint() { foreach (var paint in Paint()) Object.DestroyImmediate(paint.gameObject); }
        CreatureImpact Hit(Collider other, Vector3 point, Vector3 normal, float speed = 20f) =>
            new CreatureImpact(other, point, normal, -normal * speed, -normal * speed);

        try
        {
            Time.timeScale = 1f;
            var wall = Solid("Paint wall", new Vector3(8, 2, 0), new Vector3(1, 8, 12));
            var wallCollider = wall.GetComponent<Collider>();
            Vector3 point = wall.transform.position + Vector3.left * 0.5f;
            var probe = Probe(Vector3.zero);
            Dispatch(probe, Hit(wallCollider, point, Vector3.left, 1.5f));
            Check(probe.Impacts == 0, "gentle placement is below threshold");
            Dispatch(probe, Hit(wallCollider, point, Vector3.left));
            Dispatch(probe, Hit(wallCollider, point, Vector3.left));
            Check(probe.Impacts == 1, "duplicate producer reports are coalesced in the same physics step");
            var secondCollider = wall.AddComponent<SphereCollider>();
            var wallBody = wall.AddComponent<Rigidbody>(); wallBody.isKinematic = true;
            var compoundProbe = Probe(Vector3.right * 2f);
            Dispatch(compoundProbe, Hit(wallCollider, point, Vector3.left));
            Dispatch(compoundProbe, Hit(secondCollider, point, Vector3.left));
            Check(compoundProbe.Impacts == 1, "compound colliders deduplicate by Rigidbody");
            Object.DestroyImmediate(secondCollider);
            var trigger = Solid("Trigger", new Vector3(40, 0, 0), Vector3.one).GetComponent<Collider>(); trigger.isTrigger = true;
            Dispatch(probe, Hit(trigger, point, Vector3.left));
            Dispatch(probe, Hit(probe.GetComponent<Collider>(), point, Vector3.left));
            Check(probe.Impacts == 1, "triggers and self contacts are ignored");
            Set(compoundProbe, "minimumImpactSpeed", 10f);
            Dispatch(compoundProbe, Hit(trigger, point, Vector3.left, 20f));
            Check(compoundProbe.Impacts == 1, "trigger filtering applies even above the threshold");
            var highThreshold = Probe(Vector3.right * 3f); Set(highThreshold, "minimumImpactSpeed", 10f);
            Dispatch(highThreshold, Hit(wallCollider, point, Vector3.left, 8f));
            Check(highThreshold.Impacts == 0, "species can tune the shared impact threshold");
            Dispatch(highThreshold, new CreatureImpact(wallCollider, point, Vector3.left, Vector3.left * 20f, Vector3.left * 20f));
            Check(highThreshold.Impacts == 0, "separating motion does not count as an impact");
            probe.enabled = false;
            Dispatch(probe, Hit(wallCollider, point, Vector3.left));
            Check(probe.Impacts == 1, "disabled receivers do not react");
            var capturedProbe = Probe(new Vector3(12, 3, 0));
            var captureBubble = new GameObject("Capture filter fixture"); captureBubble.transform.SetParent(stage.transform);
            captureBubble.transform.position = origin + Vector3.right * 60f;
            var bubble = captureBubble.AddComponent<TrappingBubble>();
            var capture = capturedProbe.gameObject.AddComponent<BubbleCapture>();
            Check(capture.Attach(bubble), "capture filter fixture owns the receiver");
            Dispatch(capturedProbe, Hit(wallCollider, point, Vector3.left));
            Check(capturedProbe.Impacts == 0, "captured receivers do not react");
            capture.Release(); Object.DestroyImmediate(captureBubble);

            var goo = Goo(new Vector3(6, 2, 0));
            Check(Mathf.Approximately(goo.MinimumImpactSpeed, 26f * 0.6f), "Goo requires 60 percent of maximum throw speed");
            Dispatch(goo, Hit(wallCollider, point, Vector3.left, 15.5f));
            Check(!goo.IsDead && Paint().Length == 0, "impact just below the splash threshold preserves Goo");
            Dispatch(goo, Hit(wallCollider, point, Vector3.left));
            Check(goo.IsDead && Paint().Length == 1, "Goo impact creates paint and consumes the blob");
            Dispatch(goo, Hit(wallCollider, point, Vector3.left));
            Check(Paint().Length == 1, "consumed Goo cannot splat twice");
            var paint = Paint()[0];
            Check(Mathf.Abs(paint.radius * paint.radius / (1.4f * 1.4f) - 2f) < 0.0001f,
                "paint has twice the former nominal area");
            Check(Mathf.Abs(paint.GetComponent<BoxCollider>().size.x - paint.radius * 1.8f) < 0.001f,
                "grip collider scales with paint");
            Check(GooPaint.Covers(point + Vector3.up * 1.8f, 0f) && !GooPaint.Covers(point + Vector3.up * 2.3f, 0f),
                "coverage uses the enlarged radius");
            wall.transform.position += Vector3.forward * 4f; wall.transform.rotation = Quaternion.Euler(0, 40, 0);
            Check(paint.transform.parent == wall.transform && GooPaint.Covers(paint.transform.position) &&
                !GooPaint.Covers(point) && Vector3.Dot(paint.Normal, Vector3.left) < 0.9f,
                "attached paint and coverage follow translated and rotated surfaces");
            ClearPaint();

            var fuzzObject = new GameObject("Impact Fuzz"); fuzzObject.transform.SetParent(stage.transform);
            fuzzObject.transform.position = origin + new Vector3(30, 3, 0);
            fuzzObject.AddComponent<SphereCollider>();
            var fuzz = fuzzObject.AddComponent<FuzzCreature>();
            var fuzzCollider = fuzz.GetComponent<Collider>();
            goo = Goo(new Vector3(29, 3, 0));
            Dispatch(goo, Hit(fuzzCollider, fuzz.transform.position, Vector3.left));
            Check(goo.IsDead && fuzz.HasGoo && Paint().Length == 0, "Fuzz receives a coat instead of paint");
            Check(fuzz.TryCoatWithGoo() && !fuzz.TryCoatWithGoo(), "Fuzz reaches its existing two-coat capacity");
            goo = Goo(new Vector3(29, 3, 2));
            Dispatch(goo, Hit(fuzzCollider, fuzz.transform.position, Vector3.left));
            Check(!goo.IsDead && Paint().Length == 0, "full Fuzz preserves Goo");
            Object.DestroyImmediate(goo.gameObject);
            goo = Goo(new Vector3(29, 3, 4));
            Dispatch(goo, Hit(compoundProbe.GetComponent<Collider>(), compoundProbe.transform.position, Vector3.left));
            Check(!goo.IsDead && Paint().Length == 0, "other creature hits preserve Goo");
            Object.DestroyImmediate(goo.gameObject);

            // Real contacts, using ordinary fixed updates rather than editor-mode Physics.Simulate.
            var floor = Solid("Impact floor", new Vector3(0, -0.5f, 20), new Vector3(12, 1, 12));
            // The authored sphere center is 0.19m above the root: root 0.07 puts its bottom at 0.01m.
            goo = Goo(new Vector3(0, 0.07f, 20));
            var falling = Solid("Falling striker", new Vector3(0, 6f, 20), Vector3.one * 0.6f);
            var fallingBody = falling.AddComponent<Rigidbody>();
            fallingBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var physicalProbe = Probe(new Vector3(3, 6, 20), false);
            float until = Time.time + 1.2f;
            while (Time.time < until) yield return null;
            Check(goo == null || goo.IsDead, "falling non-creature object bursts resting Goo through a real collision");
            Check(Paint().Length == 2 && Paint().Any(p => p.transform.parent == floor.transform) &&
                Paint().Any(p => p.transform.parent == falling.transform), "incoming strike paints both support and striker: " +
                string.Join(", ", Paint().Select(p => p.transform.parent.name)));
            Check(physicalProbe.Impacts == 1 && physicalProbe.CollisionEntries > 0,
                "physical impact relay coexists with species collision callbacks");
            Check(physicalProbe.LastImpact.Normal.y > 0.9f && physicalProbe.LastImpact.NormalSpeed >= 15.6f,
                "physical impact reports surface normal and pre-solver speed");
            Check(Vector3.Distance(-physicalProbe.LastCollisionRelativeVelocity, physicalProbe.LastImpact.RelativeVelocity) < 0.001f,
                "collision adapter converts Unity velocity into receiver-minus-other pre-solver velocity");
            int physicalCount = physicalProbe.Impacts;
            until = Time.time + 0.3f;
            while (Time.time < until) yield return null;
            Check(physicalProbe.Impacts == physicalCount, "resting contact does not retrigger");
            ClearPaint();
            Object.DestroyImmediate(falling);
            var gentleGoo = Goo(new Vector3(-3, 0.07f, 20));
            until = Time.time + 0.2f;
            while (Time.time < until) yield return null;
            Check(gentleGoo != null && !gentleGoo.IsDead && Paint().Length == 0, "Goo settles gently without bursting");
            var droppedGoo = Goo(new Vector3(-4, 2f, 20));
            until = Time.time + 0.8f;
            while (Time.time < until) yield return null;
            Check(droppedGoo != null && !droppedGoo.IsDead && Paint().Length == 0,
                "short spawn drops do not burst Goo");
            var hardDroppedGoo = Goo(new Vector3(-1, 6f, 20));
            until = Time.time + 0.9f;
            while (Time.time < until) yield return null;
            Check((hardDroppedGoo == null || hardDroppedGoo.IsDead) && Paint().Length == 1 && Paint()[0].transform.parent == floor.transform,
                "a hard fall above the threshold still splats through the shared impact hook");
            ClearPaint();

            // Capacity remains bounded even with two patches per burst.
            for (int i = 0; i < 26; i++) GooPaint.Splat(origin + Vector3.up * 20, Vector3.up, null, surface: floor.transform);
            yield return null;
            Check(Paint().Length == 24, "existing 24-patch cap is retained");
            Status = "PASS: " + checks + " shared creature impact checks.";
            Debug.Log(Status);
        }
        finally
        {
            Time.timeScale = oldTimeScale;
            if (stage != null) Object.DestroyImmediate(stage);
            SceneManager.UnloadSceneAsync(scene);
        }
    }
}
