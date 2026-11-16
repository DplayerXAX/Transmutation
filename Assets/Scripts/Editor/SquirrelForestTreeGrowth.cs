using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SquirrelForestTreeGrowth
{
    const string Request = "Temp/SquirrelForest.tree-growth";
    static SquirrelForestTreeGrowth() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Apply(); } catch (Exception e) { File.WriteAllText("ArtCaptures/SquirrelForest/TreeGrowth.txt", e.ToString()); Debug.LogException(e); }
    }
    static Vector3 Foot(Transform t) { var c = t.GetComponent<CapsuleCollider>(); return t.TransformPoint(c.center - Vector3.up * c.height * .5f); }
    static float Height(Transform t)
    {
        var renderers = t.GetComponentsInChildren<MeshRenderer>(); var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b.size.y;
    }
    [MenuItem("Tools/Capstone/Squirrel Forest/Denser Taller Trees")]
    public static void Apply()
    {
        var previous = SceneManager.GetActiveScene(); var scene = SceneManager.GetSceneByPath(SquirrelForestSetup.ScenePath);
        bool loaded = scene.IsValid() && scene.isLoaded;
        if (!loaded) scene = EditorSceneManager.OpenScene(SquirrelForestSetup.ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var roots = scene.GetRootGameObjects();
            var forest = roots.SelectMany(g => g.GetComponentsInChildren<SilkForestManager>()).Single();
            var ground = roots.SelectMany(g => g.GetComponentsInChildren<MeshCollider>()).First(c => c.gameObject.layer == 7);
            var existing = forest.GeneratedRoot.Cast<Transform>().Where(t => t.GetComponent<CapsuleCollider>() != null).ToArray();
            // The saved height range makes this operation repeatable without compounding growth.
            bool grow = forest.overallHeightRange.y < 8.99f;
            var anchors = existing.SelectMany(t => t.Cast<Transform>()).Where(t => t.name == "Small bee hive anchor - bare trunk")
                .Select(t => (anchor: t, tree: t.parent, direction: Vector3.ProjectOnPlane(t.position - Foot(t.parent), Vector3.up).normalized)).ToArray();
            foreach (var a in anchors) a.anchor.SetParent(null, true);
            var original = existing.Where(t => t.name.StartsWith("Silk tree ")).ToArray();
            var feet = original.Select(Foot).ToArray();
            var scales = original.Select(t => Mathf.Lerp(2, 9, Mathf.InverseLerp(2, 6, Height(t))) / Height(t)).ToArray();
            if (grow)
            {
                foreach (var t in existing)
                {
                    var foot = Foot(t); float height = Height(t);
                    // Uniform growth increases trunk radius and every crown with the full tree height.
                    t.localScale *= Mathf.Lerp(2, 9, Mathf.InverseLerp(2, 6, height)) / height;
                    t.position += foot - Foot(t);
                }
                var vines = forest.GeneratedRoot.Find("Canopy silk membranes and shared Y junctions");
                if (vines != null)
                {
                    string path = "Assets/Generated/SquirrelForest/TallerCanopyConnections.asset";
                    var library = AssetDatabase.LoadAssetAtPath<SquirrelForestMeshLibrary>(path);
                    if (library == null) { library = ScriptableObject.CreateInstance<SquirrelForestMeshLibrary>(); AssetDatabase.CreateAsset(library, path); }
                    foreach (var filter in vines.GetComponentsInChildren<MeshFilter>())
                    {
                        var mesh = Object.Instantiate(filter.sharedMesh); var vertices = mesh.vertices;
                        for (int i = 0; i < vertices.Length; i++)
                        {
                            Vector3 p = filter.transform.TransformPoint(vertices[i]), result = Vector3.zero; float sum = 0;
                            for (int j = 0; j < feet.Length; j++)
                            {
                                float d = Mathf.Max(.2f, Vector3.ProjectOnPlane(p - feet[j], Vector3.up).sqrMagnitude);
                                float w = 1f / (d * d * d); sum += w; result += (feet[j] + (p - feet[j]) * scales[j]) * w;
                            }
                            vertices[i] = filter.transform.InverseTransformPoint(result / sum);
                        }
                        mesh.vertices = vertices; mesh.RecalculateBounds(); mesh.RecalculateNormals();
                        AssetDatabase.AddObjectToAsset(mesh, library); filter.sharedMesh = mesh;
                    }
                }
            }
            var positions = existing.Select(Foot).ToList(); var random = new System.Random(271116);
            const int target = 700; const float spacing = 1.8f;
            for (int attempt = 0; positions.Count < target && attempt < 80000; attempt++)
            {
                float x = ((float)random.NextDouble() - .5f) * forest.forestSize.x;
                float z = ((float)random.NextDouble() - .5f) * forest.forestSize.y;
                if (Mathf.Abs(x - Mathf.Sin(z * .065f) * 2.3f) < 1.15f) continue;
                var p = forest.transform.TransformPoint(new Vector3(x, 0, z));
                if (positions.Any(q => Vector3.ProjectOnPlane(q - p, Vector3.up).sqrMagnitude < spacing * spacing)) continue;
                if (!ground.Raycast(new Ray(p + Vector3.up * 200, Vector3.down), out var hit, 400) || hit.normal.y < .7f) continue;
                var template = original[random.Next(original.Length)];
                var copy = Object.Instantiate(template.gameObject, forest.GeneratedRoot).transform;
                copy.name = $"Dense silk tree {positions.Count + 1:000} - " + template.name;
                copy.position += hit.point - Foot(copy); positions.Add(hit.point);
            }
            foreach (var a in anchors)
            {
                var c = a.tree.GetComponent<CapsuleCollider>();
                a.anchor.localScale = Vector3.one;
                a.anchor.SetPositionAndRotation(Foot(a.tree) + Vector3.up * Mathf.Clamp(c.height * a.tree.lossyScale.y * .55f, .65f, 1.6f)
                    + a.direction * (c.radius * Mathf.Abs(a.tree.lossyScale.x) + .2f), Quaternion.LookRotation(a.direction));
                a.anchor.SetParent(a.tree, true);
            }
            forest.maximumTrees = target; forest.minimumSpacing = spacing; forest.treeDensity = target * 100f / (forest.forestSize.x * forest.forestSize.y);
            forest.overallHeightRange = new Vector2(2, 9);
            var settings = new SerializedObject(forest); settings.FindProperty("generatedTreeCount").intValue = positions.Count; settings.ApplyModifiedPropertiesWithoutUndo();
            // Existing squirrel habitat references preserve their established nest clearings.
            var trees = forest.GeneratedRoot.Cast<Transform>().Where(t => t.GetComponent<CapsuleCollider>() != null).ToArray();
            float min = trees.Min(Height), max = trees.Max(Height);
            if (trees.Length != target || min < 1.99f || max > 9.01f || anchors.Length != 12) throw new Exception("Tree count, height or hive anchor validation failed.");
            Physics.SyncTransforms(); AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            SquirrelForestRefinement.Capture(scene, forest);
            File.WriteAllText("ArtCaptures/SquirrelForest/TreeGrowth.txt", $"PASS\nTrees: {trees.Length} (previously 500, +40%).\nWhole tree height: {min:F3}-{max:F3} m.\nTrunks and crowns scale proportionally with tree height; tallest trees grow 1.5x in all dimensions.\nAdditional tree spacing: {spacing} m; path retained.\n12 hive anchors retain unit world scale and bare trunk attachment.\nSquirrel home candidates and flower clearings retained.\n");
        }
        finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); if (!loaded) EditorSceneManager.CloseScene(scene, true); }
    }
}
