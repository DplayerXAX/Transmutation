using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Play-mode checks in a separate physics scene. Never edits the user's saved scene.</summary>
public static class LuminosityValidation
{
    public static string Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run luminosity validation in Play Mode.");
        int checks = 0;
        void Check(bool condition, string description)
        { if (!condition) throw new InvalidOperationException("Luminosity: " + description); checks++; }
        void Near(float actual, float expected, string description) => Check(Mathf.Abs(actual - expected) < 0.0001f, description);
        var scene = SceneManager.CreateScene("Luminosity checks", new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        GameObject Make(string name, Vector3 p)
        { var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene); go.transform.position = p; return go; }
        var origin = new Vector3(0f, 100f, 0f);
        var results = new List<LuminosityReading>(32);
        void Query(float radius = 20f, float threshold = 0f) => LuminosityQuery.Nearby(scene, origin,
            radius, threshold, null, LuminosityQuery.DefaultObstructionMask, results);
        try
        {
            var p = Make("Player adapter", origin + Vector3.right * 4f).AddComponent<PlayerCreatureStats>();
            var c = Make("Creature", origin + Vector3.right * 8f).AddComponent<CreatureLifecycleValidationProbe>();
            Near(c.Luminosity, 0f, "creatures default to no luminosity");
            p.SetLuminosity(2f); c.SetLuminosity(6f);
            var effect = HoneyLuminosityEffect.Apply(p, 50f, 60f);
            Near(p.Luminosity, 52f, "effect adds directly to the single stored stat");
            Near(new UnityEditor.SerializedObject(p).FindProperty("luminosity").floatValue, 52f,
                "Inspector field and readable number are the same value");
            effect.AdvanceTime(30f);
            Near(p.Luminosity, 52f, "honey remains active before expiry");
            Check(ReferenceEquals(HoneyLuminosityEffect.Apply(p, 50f, 60f), effect), "refresh reuses the timer component");
            Near(p.Luminosity, 52f, "refresh does not stack luminosity");
            p.SetLuminosity(p.Luminosity + 7f);
            effect.AdvanceTime(60f);
            Near(p.Luminosity, 9f, "expiry subtracts only honey's 50 and preserves other changes");
            Check(effect.Ending && effect.Remaining == 0f, "expired effect schedules its own removal");
            p.SetLuminosity(2f);
            effect = HoneyLuminosityEffect.Apply(p, 50f, 60f);
            Object.DestroyImmediate(effect);
            Near(p.Luminosity, 2f, "destroying an effect removes its addition once");
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f })
            {
                bool rejected = false;
                try { c.SetLuminosity(invalid); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected, "invalid stat value rejected");
            }
            Query();
            Check(results.Count == 2, "one query finds player and creature");
            foreach (var reading in results)
                Near(reading.PerceivedBrightness, reading.EmittedLuminosity / (1f + reading.Distance * reading.Distance), "distance falloff");
            int snapshots = LuminosityQuery.SnapshotCount;
            Query(); Check(LuminosityQuery.SnapshotCount == snapshots, "queries share a position snapshot");
            c.SetLuminosity(0f); Query(); Check(results.Count == 1, "brightness is read live between snapshots");
            c.SetLuminosity(6f);
            Query(3f); Check(results.Count == 0, "observer detection radius");
            Query(20f, 10f); Check(results.Count == 0, "observer threshold");
            LuminosityQuery.Nearby(scene, origin, 20f, 0f, p.transform, 0, results);
            Check(results.Count == 1 && ReferenceEquals(results[0].Source, c), "observer ignores own source");
            var wall = Make("Wall", origin + Vector3.right * 2f).AddComponent<BoxCollider>();
            wall.size = new Vector3(0.5f, 10f, 10f);
            Physics.SyncTransforms();
            Query(); Check(results.Count == 0, "solid scenery blocks light perception");
            wall.isTrigger = true; Physics.SyncTransforms();
            Query(); Check(results.Count == 2, "triggers do not block perception");
            wall.enabled = false;
            var targetCollider = p.gameObject.AddComponent<SphereCollider>(); targetCollider.radius = 0.6f;
            Physics.SyncTransforms();
            Query(); Check(results.Count > 0 && ReferenceEquals(results[0].Source, p), "target's own collider is ignored");
            targetCollider.enabled = false;
            p.enabled = false; Query(); Check(results.Count == 1, "disabled source disappears");
            p.enabled = true; Query(); Check(results.Count == 2, "reenabled source registered");
            var spawned = Make("Spawned emitter", origin + Vector3.forward * 2f).AddComponent<PlayerCreatureStats>();
            spawned.SetLuminosity(1f); Query(); Check(results.Count == 3, "runtime spawn invalidates membership");
            Object.DestroyImmediate(spawned.gameObject); Query(); Check(results.Count == 2, "destruction removes source");
            c.Die(); Query(); Check(results.Count == 1, "dead creature stops emitting");
            var consumed = Make("Consumed emitter", origin + Vector3.forward * 4f).AddComponent<GlowHoney>();
            consumed.SetLuminosity(4f); Query(); Check(results.Count == 2, "fresh honey emits before consumption");
            typeof(Creature).GetMethod("CompleteConsumption", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(consumed, new object[] { null });
            Query(); Check(results.Count == 1, "consumed body removed immediately");
            // Warm buffers before measuring repeated observer queries.
            Query();
            for (int i = 0; i < 8; i++) Query();
            p.enabled = false; p.enabled = true;
            snapshots = LuminosityQuery.SnapshotCount;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) Query();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(allocated == 0, "warm queries do not allocate (bytes=" + allocated + ")");
            Check(LuminosityQuery.SnapshotCount == snapshots + 1, "allocation check includes rebuilding the reused snapshot");
            return "PASS: " + checks + " luminosity checks";
        }
        finally
        {
            foreach (var root in scene.GetRootGameObjects()) Object.DestroyImmediate(root);
            SceneManager.UnloadSceneAsync(scene);
        }
    }
}
