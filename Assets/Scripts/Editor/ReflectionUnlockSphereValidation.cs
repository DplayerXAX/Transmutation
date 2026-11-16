using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Save-free geometry, identity, lifecycle and animation regression checks.</summary>
public static class ReflectionUnlockSphereValidation
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Unlock sphere validation: " + message); checks++; }

    [MenuItem("Tools/Capstone/Meditation/Validate Unlock Sphere")]
    public static void Validate() => Debug.Log(Run());

    public static string Run()
    {
        checks = 0;
        Check(!Application.isPlaying, "run deterministic checks outside Play mode");
        int meshCount = Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m.name.StartsWith("Reflection sphere chunk"));
        int materialCount = Resources.FindObjectsOfTypeAll<Material>().Count(m => m.name.StartsWith("Reflection sphere ·"));
        double fullVolume = 0, fullArea = 0;
        foreach (int count in new[] { 1, 29, 232 })
        {
            var chunks = ReflectionSphereGeometry.Generate(count, 17041);
            try
            {
                Check(chunks.Count == count, "requested chunk count");
                double volume = 0, area = 0;
                foreach (var chunk in chunks)
                {
                    var vertices = chunk.mesh.vertices;
                    double meshVolume = 0;
                    foreach (int sub in new[] { 0, 1 })
                    {
                        int[] triangles = chunk.mesh.GetTriangles(sub);
                        for (int i = 0; i < triangles.Length; i += 3)
                        {
                            Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                            Vector3 cross = Vector3.Cross(b - a, c - a);
                            if (cross.sqrMagnitude > 1e-12f)
                                Check(Vector3.Dot(cross, (a + b + c) / 3f) > -1e-7f, "outward faces of convex solid");
                            meshVolume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
                            if (sub == 0) area += cross.magnitude * 0.5;
                        }
                    }
                    Check(meshVolume > 1e-8 && Math.Abs(meshVolume - chunk.volume) < 2e-6, "closed solid volume and centroid");
                    volume += chunk.volume;
                }
                if (count == 1) { fullVolume = volume; fullArea = area; }
                else
                {
                    Check(Math.Abs(volume - fullVolume) < 1e-5, "partition conserves sphere volume");
                    Check(Math.Abs(area - fullArea) < 1e-5, "outer patches cover exactly the original sphere surface");
                }
            }
            finally { foreach (var c in chunks) ReflectionSphereGeometry.Release(c.mesh); }
        }

        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Sphere validation (unsaved)");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var catalog = ScriptableObject.CreateInstance<ReflectionCatalog>();
        var progress = JsonUtility.FromJson<ReflectionProgress>("{\"version\":1,\"unlocked\":[\"b\",\"removed-id\"]}");
        var settings = new ReflectionUnlockSphereSettings { position = Vector3.zero, fragmentCount = 180, motion = false };
        try
        {
            SetRoster(catalog, "c", "a", "b");
            var sphere = root.AddComponent<ReflectionUnlockSphere>();
            sphere.Initialize(catalog, progress, settings, null);
            Check(sphere.CreatureCount == 3 && sphere.UnlockedCreatureCount == 1, "partial saves and stale IDs");
            Check(sphere.FragmentCount == 180 && sphere.RestoringFragmentCount == 0, "saved progress snaps immediately");
            CheckBottomUp(sphere);
            CheckSurroundingScatter(sphere);
            string lowerBand = RestoredShape(sphere);
            string first = Fingerprint(sphere);
            int mesh = root.GetComponentInChildren<MeshFilter>().sharedMesh.GetInstanceID();
            sphere.CheckConfiguration();
            Check(mesh == root.GetComponentInChildren<MeshFilter>().sharedMesh.GetInstanceID(), "unchanged configuration does not regenerate");
            SetRoster(catalog, "a", "b", "c"); sphere.CheckConfiguration();
            Check(Fingerprint(sphere) == first, "catalog order does not affect assignment or scatter");
            sphere.Initialize(catalog, progress, settings, null);
            Check(Fingerprint(sphere) == first, "repeatable generation and assignments");
            Check(SubscriberCount(progress) == 1, "reinitialization has one subscription");
            sphere.enabled = false;
            Check(SubscriberCount(progress) == 0, "disable unsubscribes");
            sphere.enabled = true;
            Check(SubscriberCount(progress) == 1, "reenable subscribes once");

            JsonUtility.FromJsonOverwrite("{\"unlocked\":[\"c\",\"c\",\"removed-id\"]}", progress); Notify(progress);
            Check(sphere.UnlockedCreatureCount == 1 && RestoredShape(sphere) == lowerBand,
                "any first creature restores the same bottom band; duplicate/stale IDs are ignored");
            Check(sphere.RestoringFragmentCount == 0, "identity changes at equal progress do not restart restored pieces");
            JsonUtility.FromJsonOverwrite("{\"unlocked\":[\"b\",\"removed-id\"]}", progress); Notify(progress);

            // Simulate notifications on an in-memory progress object. No player file is read or written.
            JsonUtility.FromJsonOverwrite("{\"unlocked\":[\"a\",\"b\"]}", progress); Notify(progress);
            Check(sphere.UnlockedCreatureCount == 2 && sphere.RestoringFragmentCount == 60, "new unlock starts all owned chunks");
            CheckBottomUp(sphere);
            JsonUtility.FromJsonOverwrite("{\"unlocked\":[\"a\",\"b\",\"c\"]}", progress); Notify(progress);
            Check(sphere.RestoringFragmentCount == 120, "overlapping unlocks preserve existing animation");
            typeof(ReflectionUnlockSphere).GetMethod("Advance", Private).Invoke(sphere, new object[] { 100f });
            Check(sphere.IsComplete && sphere.RestoringFragmentCount == 0, "restoration settles exactly");
            var whole = root.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Seamless completed sphere");
            Check(whole.enabled && root.GetComponentsInChildren<MeshRenderer>().Count(r => r.enabled) == 1, "complete surface is seamless");
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>().Where(f => f.name.StartsWith("Fragment ")))
            {
                Check(filter.transform.localRotation == Quaternion.identity, "precise assembled rotation");
                Check(filter.transform.localPosition.magnitude < 1f, "precise assembled centre within unit sphere");
            }
            sphere.PreviewFraction(0f); Check(sphere.UnlockedCreatureCount == 0, "shattered preview");
            sphere.PreviewFraction(0.5f); Check(sphere.UnlockedCreatureCount == 2, "partial preview");
            CheckBottomUp(sphere);
            CheckSurroundingScatter(sphere);
            sphere.PreviewFraction(1f); Check(sphere.IsComplete, "whole preview");
            sphere.PreviewSavedProgress(); Check(sphere.IsComplete && progress.Unlocked.Count == 3, "previews leave progress unchanged");

            settings.fragmentMode = ReflectionFragmentMode.OnePerCreature; sphere.CheckConfiguration();
            Check(sphere.FragmentCount == 3, "one solid fragment per family");
            sphere.PreviewFraction(1f / 3f); CheckBottomUp(sphere);
            sphere.PreviewSavedProgress();
            var names = root.GetComponentsInChildren<MeshFilter>().Where(f => f.name.StartsWith("Fragment ")).Select(f => f.name.Split('·')[1].Trim()).ToArray();
            Check(names.Distinct().Count() == 3, "exactly one owner per solid");
            SetRoster(catalog, "a", "d", "b", "c"); sphere.CheckConfiguration();
            Check(sphere.FragmentCount == 4 && sphere.UnlockedCreatureCount == 3 && !sphere.IsComplete, "roster addition creates a locked chunk");
            SetRoster(catalog, "a", "b"); sphere.CheckConfiguration();
            Check(sphere.FragmentCount == 2 && sphere.UnlockedCreatureCount == 2 && sphere.IsComplete, "roster removal ignores stale saved IDs");
            SetRoster(catalog); sphere.CheckConfiguration();
            Check(sphere.CreatureCount == 0 && sphere.FragmentCount == 1 && sphere.IsComplete, "zero creatures safely displays a whole neutral sphere");
            SetRoster(catalog, "a", "a", "", "b"); sphere.CheckConfiguration();
            Check(sphere.CreatureCount == 2 && sphere.FragmentCount == 2, "duplicate and empty roster IDs do not add progress");
            Check(root.GetComponentsInChildren<Collider>(true).Length == 0 && root.GetComponentsInChildren<Rigidbody>(true).Length == 0, "decorative sphere has no physics objects");
            int oldMesh = root.GetComponentInChildren<MeshFilter>().sharedMesh.GetInstanceID();
            settings.fractureSeed++; sphere.CheckConfiguration();
            Check(root.GetComponentInChildren<MeshFilter>().sharedMesh.GetInstanceID() != oldMesh, "seed change rebuilds geometry");
            Object.DestroyImmediate(sphere);
            Check(SubscriberCount(progress) == 0, "destroy unsubscribes");
        }
        finally
        {
            Object.DestroyImmediate(root); Object.DestroyImmediate(catalog);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
        Check(Resources.FindObjectsOfTypeAll<Mesh>().Count(m => m.name.StartsWith("Reflection sphere chunk")) == meshCount, "validation releases all generated meshes");
        Check(Resources.FindObjectsOfTypeAll<Material>().Count(m => m.name.StartsWith("Reflection sphere ·")) == materialCount, "validation releases its material");
        return "PASS: " + checks + " unlock sphere checks; volume/surface conservation, both modes, saved IDs, events, roster changes, previews and cleanup. No save files changed.";
    }

    private static void SetRoster(ReflectionCatalog catalog, params string[] ids) =>
        typeof(ReflectionCatalog).GetField("exhibits", Private).SetValue(catalog,
            ids.Select(id => new ReflectionCatalog.Definition { id = id, creatureType = id }).ToArray());
    private static int SubscriberCount(ReflectionProgress p) =>
        ((Action)typeof(ReflectionProgress).GetField("Changed", Private).GetValue(p))?.GetInvocationList().Length ?? 0;
    private static void Notify(ReflectionProgress p) =>
        ((Action)typeof(ReflectionProgress).GetField("Changed", Private).GetValue(p))?.Invoke();
    private static IEnumerable<object> Fragments(ReflectionUnlockSphere sphere) =>
        ((System.Collections.IEnumerable)typeof(ReflectionUnlockSphere).GetField("fragments", Private).GetValue(sphere)).Cast<object>();
    private static bool Restored(object f) => (bool)f.GetType().GetField("unlocked").GetValue(f);
    private static Vector3 Centre(object f) => (Vector3)f.GetType().GetField("assembledPosition").GetValue(f);
    private static void CheckBottomUp(ReflectionUnlockSphere sphere)
    {
        var restored = Fragments(sphere).Where(Restored).Select(f => Centre(f).y).ToArray();
        var locked = Fragments(sphere).Where(f => !Restored(f)).Select(f => Centre(f).y).ToArray();
        Check(restored.Length == 0 || locked.Length == 0 || restored.Max() <= locked.Min(),
            "every restored band is below the remaining fragments");
    }
    private static string RestoredShape(ReflectionUnlockSphere sphere) =>
        string.Join("|", Fragments(sphere).Where(Restored).Select(f => Centre(f).ToString("F5")));
    private static void CheckSurroundingScatter(ReflectionUnlockSphere sphere)
    {
        var positions = Fragments(sphere).Where(f => !Restored(f)).Select(f =>
            ((Transform)f.GetType().GetField("transform").GetValue(f)).localPosition).ToArray();
        Check(positions.Any(p => p.y < 0f) && positions.Any(p => p.y > 0f), "locked debris remains above and below the sphere");
        Check(positions.Any(p => p.x < 0f) && positions.Any(p => p.x > 0f) &&
            positions.Any(p => p.z < 0f) && positions.Any(p => p.z > 0f), "locked debris remains on all sides as bottom-up progress rises");
    }
    private static string Fingerprint(ReflectionUnlockSphere sphere) => string.Join("|",
        sphere.GetComponentsInChildren<MeshFilter>().Select(f => f.name + f.transform.localPosition.ToString("F5") +
            f.sharedMesh.vertexCount + f.sharedMesh.bounds.ToString()));
}
