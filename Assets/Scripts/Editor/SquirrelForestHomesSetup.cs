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
public static class SquirrelForestHomesSetup
{
    const string Request = "Temp/SquirrelForest.homes";
    static SquirrelForestHomesSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Apply(); } catch (Exception e) { File.WriteAllText("ArtCaptures/SquirrelForest/Homes.txt", e.ToString()); Debug.LogException(e); }
    }
    [MenuItem("Tools/Capstone/Squirrel Forest/Spread Nests Across Forest")]
    public static void Apply()
    {
        var previous = SceneManager.GetActiveScene(); var scene = SceneManager.GetSceneByPath(SquirrelForestSetup.ScenePath);
        bool loaded = scene.IsValid() && scene.isLoaded;
        if (!loaded) scene = EditorSceneManager.OpenScene(SquirrelForestSetup.ScenePath, OpenSceneMode.Additive);
        var external = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => c.enabled).ToArray();
        var validation = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        var random = UnityEngine.Random.state;
        try
        {
            SceneManager.SetActiveScene(scene);
            var roots = scene.GetRootGameObjects();
            var manager = roots.SelectMany(g => g.GetComponentsInChildren<SquirrelSystemManager>()).Single();
            var settings = new SerializedObject(manager);
            settings.FindProperty("spreadNestLocations").boolValue = true;
            settings.FindProperty("minimumNestSeparation").floatValue = 28f;
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            foreach (var collider in external) collider.enabled = false;
            SceneManager.SetActiveScene(validation);
            var ground = roots.SelectMany(g => g.GetComponentsInChildren<MeshCollider>()).First(c => c.gameObject.layer == 7);
            var floor = new GameObject("Validation ground"); floor.layer = 7;
            floor.transform.SetPositionAndRotation(ground.transform.position, ground.transform.rotation); floor.transform.localScale = ground.transform.lossyScale;
            floor.AddComponent<MeshCollider>().sharedMesh = ground.sharedMesh;
            var sourceTrees = roots.SelectMany(g => g.GetComponentsInChildren<SquirrelTree>()).ToArray();
            var anchors = new List<SquirrelTree>();
            foreach (var tree in sourceTrees)
            {
                var proxy = new GameObject("Eligible low crown tree"); proxy.transform.position = tree.GroundPosition;
                anchors.Add(proxy.AddComponent<SquirrelTree>());
            }
            var copy = Object.Instantiate(manager.gameObject).GetComponent<SquirrelSystemManager>();
            foreach (Transform child in copy.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            var copySettings = new SerializedObject(copy);
            foreach (string field in new[] { "squirrels", "nests", "foods" }) copySettings.FindProperty(field).arraySize = 0;
            copySettings.FindProperty("autoDiscoverScene").boolValue = false;
            var trees = copySettings.FindProperty("trees"); trees.arraySize = anchors.Count;
            for (int i = 0; i < anchors.Count; i++) trees.GetArrayElementAtIndex(i).objectReferenceValue = anchors[i];
            copySettings.ApplyModifiedPropertiesWithoutUndo();
            UnityEngine.Random.InitState(4107); Physics.SyncTransforms(); copy.InitializeSystem();
            if (!copy.IsInitialized || copy.Nests.Count != 6) throw new InvalidOperationException("Forest ecology did not initialize six homes.");
            var positions = copy.Nests.Select(n => n.transform.position).ToArray(); float minimum = float.PositiveInfinity;
            for (int i = 0; i < positions.Length; i++) for (int j = i + 1; j < positions.Length; j++)
                minimum = Mathf.Min(minimum, Vector3.ProjectOnPlane(positions[i] - positions[j], Vector3.up).magnitude);
            float width = positions.Max(p => p.x) - positions.Min(p => p.x), depth = positions.Max(p => p.z) - positions.Min(p => p.z);
            if (minimum < 28f || width < 60f || depth < 60f) throw new InvalidOperationException("Nest distribution does not cover the forest with required spacing.");
            foreach (var point in positions)
                if (anchors.Min(t => Vector3.ProjectOnPlane(point - t.GroundPosition, Vector3.up).magnitude) > 1.81f)
                    throw new InvalidOperationException("Nest is not beside an eligible root.");
            if (copy.IsNestLocationAvailable(positions[0] + Vector3.right, null)) throw new InvalidOperationException("Later nest placement bypasses minimum separation.");
            copy.InitializeSystem(); if (copy.Nests.Count != 6) throw new InvalidOperationException("Duplicate homes after initialization.");
            File.WriteAllText("ArtCaptures/SquirrelForest/Homes.txt", $"PASS\nSix homes distributed across eligible low-canopy trees.\nConfigured minimum spacing: 28 m. Actual minimum: {minimum:F2} m.\nCoverage: {width:F2} x {depth:F2} m.\nEach home is within 1.8 m of a low-canopy tree root.\nMigration/build placement respects spacing; initialization remains idempotent.\n" + string.Join("\n", positions.Select((p, i) => $"Nest {i+1}: {p}")));
        }
        finally
        {
            UnityEngine.Random.state = random;
            foreach (var collider in external) if (collider != null) collider.enabled = true;
            foreach (var root in validation.GetRootGameObjects()) Object.DestroyImmediate(root);
            EditorSceneManager.CloseScene(validation, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (!loaded) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
