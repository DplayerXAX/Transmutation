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
public static class SquirrelForestFlowerRefinement
{
    const string Request = "Temp/SquirrelForest.flower-refine";
    static SquirrelForestFlowerRefinement() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Apply(); } catch (Exception e) { File.WriteAllText("ArtCaptures/SquirrelForest/FlowerRefinement.txt", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Tools/Capstone/Squirrel Forest/Taller Flowers And Small Trunk Hives")]
    public static void Apply()
    {
        var previous = SceneManager.GetActiveScene(); var scene = SceneManager.GetSceneByPath(SquirrelForestSetup.ScenePath);
        bool loaded = scene.IsValid() && scene.isLoaded;
        if (!loaded) scene = EditorSceneManager.OpenScene(SquirrelForestSetup.ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var roots = scene.GetRootGameObjects();
            var root = roots.Single(g => g.name == "Forest flowers and glow bee meadow").transform;
            var forest = roots.SelectMany(g => g.GetComponentsInChildren<SilkForestManager>()).Single();
            var ground = roots.SelectMany(g => g.GetComponentsInChildren<MeshCollider>()).First(c => c.gameObject.layer == 7);
            var flowers = root.GetComponentsInChildren<NectarFlower>();
            foreach (var flower in flowers)
            {
                Vector3 scale = flower.transform.localScale; scale.y = scale.x * 1.65f; flower.transform.localScale = scale;
            }
            Material Material(string name, Color color)
            {
                string path = "Assets/Generated/SquirrelForest/Flowers/" + name + ".mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
                m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", 0.25f); m.SetFloat("_Cull", 0);
                m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * 0.08f); m.enableInstancing = true; EditorUtility.SetDirty(m); return m;
            }
            var palette = new[] { Material("MeadowIvory", new Color(0.76f, 0.79f, 0.71f)), Material("MeadowSoftLavender", new Color(0.53f, 0.5f, 0.65f)) };
            var original = root.Cast<Transform>().Where(t => t.name.StartsWith("Low wildflower ")).ToArray();
            foreach (Transform t in root.Cast<Transform>().Where(t => t.name.StartsWith("Dense low wildflower ")).ToArray()) Object.DestroyImmediate(t.gameObject);
            var manager = roots.SelectMany(g => g.GetComponentsInChildren<SquirrelSystemManager>()).Single();
            var args = new object[] { 6, null }; typeof(SquirrelSystemManager).GetMethod("TryPlanSpreadHomes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(manager, args);
            var homes = (List<Vector3>)args[1];
            var centers = root.Cast<Transform>().Where(t => t.name.StartsWith("Hyacinth patch ")).Select(t => t.position).ToArray();
            var random = new System.Random(161126); float R(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
            int extra = 0;
            for (int attempt = 0; extra < 1600 && attempt < 30000; attempt++)
            {
                float x = R(-47, 47), z = R(-47, 47);
                if (Mathf.Abs(x - Mathf.Sin(z * 0.065f) * 2.3f) < 1.3f || !ground.Raycast(new Ray(new Vector3(x, 180, z), Vector3.down), out var hit, 360) || hit.normal.y < 0.8f) continue;
                if (homes.Any(p => Vector3.ProjectOnPlane(p - hit.point, Vector3.up).sqrMagnitude < 6.76f) || centers.Any(p => Vector3.ProjectOnPlane(p - hit.point, Vector3.up).sqrMagnitude < 4.41f)) continue;
                var copy = Object.Instantiate(original[random.Next(original.Length)].gameObject, root).transform;
                copy.name = "Dense low wildflower " + (++extra); copy.SetPositionAndRotation(hit.point, Quaternion.Euler(0, R(0, 360), 0));
            }
            var meadow = root.Cast<Transform>().Where(t => t.name.StartsWith("Low wildflower ") || t.name.StartsWith("Dense low wildflower ")).ToArray();
            for (int i = 0; i < meadow.Length; i++) foreach (var renderer in meadow[i].GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = palette[i % 5 == 0 ? 1 : 0];
            var trunks = forest.GeneratedRoot.Cast<Transform>().Where(t => t.GetComponent<CapsuleCollider>() != null && t.GetComponent<SquirrelTree>() == null).ToArray();
            var used = new HashSet<Transform>(); int mounted = 0;
            foreach (var colony in root.GetComponentsInChildren<GlowBeeColony>())
            {
                var settings = new SerializedObject(colony); settings.FindProperty("mountHivesOnAnchors").boolValue = true; settings.FindProperty("hiveScale").floatValue = 0.5f;
                var anchors = settings.FindProperty("nestAnchors");
                for (int i = 0; i < anchors.arraySize; i++)
                {
                    var anchor = (Transform)anchors.GetArrayElementAtIndex(i).objectReferenceValue;
                    var tree = trunks.Where(t => !used.Contains(t)).OrderBy(t => Vector3.ProjectOnPlane(Foot(t) - anchor.position, Vector3.up).sqrMagnitude).First();
                    used.Add(tree); var capsule = tree.GetComponent<CapsuleCollider>(); Vector3 foot = Foot(tree);
                    Vector3 direction = Vector3.ProjectOnPlane(anchor.position - foot, Vector3.up).normalized;
                    if (direction.sqrMagnitude < 0.01f) direction = Vector3.forward;
                    float radius = capsule.radius * Mathf.Abs(tree.lossyScale.x);
                    float height = Mathf.Clamp(capsule.height * Mathf.Abs(tree.lossyScale.y) * 0.55f, 0.65f, 1.6f);
                    anchor.SetParent(null, true); anchor.localScale = Vector3.one;
                    anchor.SetPositionAndRotation(foot + Vector3.up * height + direction * (radius + 0.20f), Quaternion.LookRotation(direction));
                    anchor.SetParent(tree, true); anchor.name = "Small bee hive anchor - bare trunk"; mounted++;
                }
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            if (mounted != 12 || extra != 1600) throw new Exception("Unexpected hive or meadow count.");
            Physics.SyncTransforms(); AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            SquirrelForestRefinement.Capture(scene, forest);
            File.WriteAllText("ArtCaptures/SquirrelForest/FlowerRefinement.txt", $"PASS\nHyacinth vertical scale increased by 65%; nectar points follow flower transforms.\nMeadow: {meadow.Length} clumps (doubled), two subdued colours: ivory 80%, soft lavender 20%.\n12 half-size hives mounted to 12 distinct trees without small trunk crowns.\nAnchors mounted 0.65-1.6 m above tree roots, entrance points outward.\nFlower patch spacing, paths and squirrel clearings retained.\n");
        }
        finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); if (!loaded) EditorSceneManager.CloseScene(scene, true); }
    }
    static Vector3 Foot(Transform tree)
    {
        var shape = tree.GetComponent<CapsuleCollider>(); return tree.TransformPoint(shape.center - Vector3.up * shape.height * 0.5f);
    }
}
