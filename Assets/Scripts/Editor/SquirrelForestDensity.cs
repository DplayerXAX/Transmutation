using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SquirrelForestDensity
{
    const string Request = "Temp/SquirrelForest.density";
    static SquirrelForestDensity() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Apply(); } catch (Exception e) { File.WriteAllText("ArtCaptures/SquirrelForest/Density.txt", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Tools/Capstone/Squirrel Forest/Densify Forest")]
    public static void Apply()
    {
        var previous = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(SquirrelForestSetup.ScenePath);
        bool loaded = scene.IsValid() && scene.isLoaded;
        if (!loaded) scene = EditorSceneManager.OpenScene(SquirrelForestSetup.ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var roots = scene.GetRootGameObjects();
            var forest = roots.SelectMany(g => g.GetComponentsInChildren<SilkForestManager>()).Single();
            var system = roots.SelectMany(g => g.GetComponentsInChildren<SquirrelSystemManager>()).Single();
            var ground = roots.SelectMany(g => g.GetComponentsInChildren<MeshCollider>()).First(c => c.gameObject.layer == 7);
            foreach (Transform t in forest.GeneratedRoot.Cast<Transform>().ToArray())
                if (t.name.StartsWith("Dense silk tree ")) Object.DestroyImmediate(t.gameObject);
            var templates = forest.GeneratedRoot.Cast<Transform>().Where(t => t.name.StartsWith("Silk tree ")).ToArray();
            if (templates.Length == 0) throw new InvalidOperationException("No baked silk tree templates.");
            Vector3 Foot(Transform tree)
            {
                var capsule = tree.GetComponent<CapsuleCollider>();
                return tree.TransformPoint(capsule.center - Vector3.up * capsule.height * 0.5f);
            }
            var positions = templates.Select(Foot).ToList();
            var random = new System.Random(73421);
            const int target = 500;
            for (int attempt = 0; positions.Count < target && attempt < 60000; attempt++)
            {
                float x = ((float)random.NextDouble() - 0.5f) * forest.forestSize.x;
                float z = ((float)random.NextDouble() - 0.5f) * forest.forestSize.y;
                // A winding walkable opening through the denser understory.
                float path = Mathf.Sin(z * 0.065f) * 2.3f;
                if (Mathf.Abs(x - path) < 1.15f) continue;
                var at = forest.transform.TransformPoint(new Vector3(x, 0f, z));
                if (positions.Any(p => Vector3.ProjectOnPlane(p - at, Vector3.up).sqrMagnitude < 2.1f * 2.1f)) continue;
                if (!ground.Raycast(new Ray(at + Vector3.up * 200f, Vector3.down), out var hit, 400f) || hit.normal.y < 0.7f) continue;
                var template = templates[random.Next(templates.Length)];
                var tree = Object.Instantiate(template.gameObject, forest.GeneratedRoot).transform;
                tree.name = $"Dense silk tree {positions.Count + 1:000} - " + template.name;
                tree.position += hit.point - Foot(tree);
                positions.Add(hit.point);
            }
            if (positions.Count != target) throw new InvalidOperationException("Could not reach requested forest density.");
            var anchors = forest.GetComponentsInChildren<SquirrelTree>();
            var settings = new SerializedObject(system); var trees = settings.FindProperty("trees"); trees.arraySize = anchors.Length;
            for (int i = 0; i < anchors.Length; i++) trees.GetArrayElementAtIndex(i).objectReferenceValue = anchors[i];
            settings.ApplyModifiedPropertiesWithoutUndo();
            var forestSettings = new SerializedObject(forest);
            forestSettings.FindProperty("generatedTreeCount").intValue = positions.Count;
            forestSettings.FindProperty("maximumTrees").intValue = target;
            forestSettings.FindProperty("treeDensity").floatValue = target * 100f / (forest.forestSize.x * forest.forestSize.y);
            forestSettings.FindProperty("minimumSpacing").floatValue = 2.1f;
            forestSettings.ApplyModifiedPropertiesWithoutUndo();
            float nearest = positions.Average(a => positions.Where(b => b != a).Min(b => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude));
            float minimum = float.PositiveInfinity, maximum = 0;
            foreach (Transform tree in forest.GeneratedRoot)
            {
                if (!tree.name.StartsWith("Silk tree ") && !tree.name.StartsWith("Dense silk tree ")) continue;
                var renderers = tree.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                minimum = Mathf.Min(minimum, bounds.size.y); maximum = Mathf.Max(maximum, bounds.size.y);
            }
            if (minimum < 1.99f || maximum > 6.01f) throw new InvalidOperationException("Tree height limits changed.");
            Physics.SyncTransforms(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            SquirrelForestRefinement.Capture(scene, forest);
            File.WriteAllText("ArtCaptures/SquirrelForest/Density.txt", $"PASS\nWorld reference: 520 pines; 173 sampled in southern forest; mean nearest-tree distance 4.991 m.\nSilk forest: {positions.Count} trees, previously 90; 96 x 96 m.\nMean nearest-tree distance: {nearest:F3} m; extra trees minimum separation: 2.1 m.\nOverall heights retained: {minimum:F3}-{maximum:F3} m.\nLow-canopy habitat anchors: {anchors.Length}.\nAdded trees share the existing baked meshes and materials.\n");
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (!loaded) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
