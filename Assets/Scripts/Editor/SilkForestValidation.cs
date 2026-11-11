using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>Run in an isolated project with -executeMethod SilkForestValidation.RunBatch.</summary>
public static class SilkForestValidation
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks++; Debug.Log("SILK CHECK PASS: " + message);
    }
    public static void RunBatch()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var test = new GameObject("Parameter checks").AddComponent<SilkForestManager>();
            SilkForestSetup.AssignMaterials(test);
            test.forestSize = new Vector2(12, 12); test.treeDensity = 4; test.minimumSpacing = 2;
            test.crownCountRange = new Vector2Int(3, 3); test.trunkCrownCountRange = new Vector2Int(2, 2);
            test.heightRange = new Vector2(10, 10); test.vineDensity = 0;
            test.Regenerate();
            Check(test.GeneratedTreeCount == 6, "Density produces six trees in 144 square metres.");
            Check(test.GeneratedCrownCount == 30, "Exactly three upper crowns and two stem buds per tree.");
            Check(test.GeneratedVineCount == 0, "Zero vine density creates no connections.");
            var original = test.GeneratedRoot.GetComponentInChildren<MeshFilter>().sharedMesh.vertices;
            test.Regenerate();
            Check(original.SequenceEqual(test.GeneratedRoot.GetComponentInChildren<MeshFilter>().sharedMesh.vertices), "Same seed reproduces exact geometry.");
            test.vineDensity = 1; test.maximumVineDistance = 50; test.Regenerate();
            Check(test.GeneratedVineCount > 0 && test.GeneratedVineCount <= test.GeneratedTreeCount * test.maximumVinesPerTree / 2,
                "Dense vines connect trees while respecting the per-tree cap.");
            foreach (var filter in test.GeneratedRoot.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                Check(mesh.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)) &&
                    mesh.triangles.All(i => i >= 0 && i < mesh.vertexCount), "Finite mesh and valid indices: " + mesh.name);
            }
            test.treeDensity = 0; test.Regenerate();
            Check(test.GeneratedTreeCount == 0 && test.GeneratedCrownCount == 0, "Zero density clears all trees and crowns.");
            test.treeDensity = 10; test.forestSize = Vector2.zero; test.minimumSpacing = 100; test.Regenerate();
            Check(test.GeneratedTreeCount <= 1, "Impossible spacing and tiny bounds terminate without overlap.");
            test.forestSize = new Vector2(12, 12); test.minimumSpacing = 2; test.treeDensity = 2;
            test.Regenerate();
            var autosaveScene = test.gameObject.scene;
            EditorSceneManager.SaveScene(autosaveScene, "Assets/SaveHookCheck.unity");
            Check(test.GeneratedRoot.GetComponentsInChildren<MeshFilter>().All(f => EditorUtility.IsPersistent(f.sharedMesh)),
                "Saving a regenerated scene automatically persists its meshes.");
            Object.DestroyImmediate(test.gameObject);
            var forest = SilkForestSetup.BuildDemo();
            Check(forest.GeneratedTreeCount == 29, "Default demo generates 29 trees.");
            int expectedCrowns = forest.GeneratedCrownCount, expectedVines = forest.GeneratedVineCount;
            int vertices = forest.GeneratedRoot.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.vertexCount);
            Check(vertices > 10000, "Real geometry is generated, including perforated crowns.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SilkForestSetup.PrefabPath);
            Check(prefab != null && prefab.GetComponentsInChildren<MeshFilter>().All(f => EditorUtility.IsPersistent(f.sharedMesh)), "Saved prefab references persistent baked meshes.");
            var sharedMeshes = prefab.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh).ToArray();
            var instance = Object.Instantiate(prefab).GetComponent<SilkForestManager>();
            instance.treeDensity = 0; instance.Regenerate();
            Check(sharedMeshes.All(m => m != null && m.vertexCount > 0), "Regenerating one prefab instance preserves shared baked meshes.");
            Object.DestroyImmediate(instance.gameObject);
            EditorSceneManager.OpenScene(SilkForestSetup.ScenePath);
            forest = Object.FindFirstObjectByType<SilkForestManager>();
            Check(forest.GeneratedCrownCount == expectedCrowns && forest.GeneratedVineCount == expectedVines &&
                forest.GeneratedRoot.GetComponentsInChildren<MeshFilter>().All(f => f.sharedMesh != null), "Scene reload preserves the baked forest.");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) Capture(forest);
            foreach (string shaderName in new[] { "Capstone/Silk Forest/Pearlescent Silk", "Capstone/Silk Forest/Thin Film" })
            {
                Shader shader = Shader.Find(shaderName);
                Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Shader compiles: " + shaderName);
            }
            string report = $"PASS: {checks} checks\nTrees: {forest.GeneratedTreeCount}\nCrowns: {expectedCrowns}\nVine connections: {expectedVines}\nVertices: {vertices}\n";
            Directory.CreateDirectory("Validation"); File.WriteAllText("Validation/silk-forest-results.txt", report);
            Debug.Log(report); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private static void Capture(SilkForestManager forest)
    {
        string output = "ArtCaptures/SilkForest"; Directory.CreateDirectory(output);
        Camera camera = Camera.main;
        camera.enabled = false;
        var target = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
        camera.targetTexture = target;
        void Shot(string name)
        {
            camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var pixels = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(output, name + ".png"), pixels.EncodeToPNG());
            RenderTexture.active = previous; Object.DestroyImmediate(pixels);
        }
        Shot("ForestOverview");
        for (int i = 1; i < forest.GeneratedRoot.childCount; i++) forest.GeneratedRoot.GetChild(i).gameObject.SetActive(false);
        // Aim at an actual generated tree, regardless of seed.
        var first = forest.GeneratedRoot.GetChild(0).GetComponentsInChildren<Renderer>();
        Bounds bounds = first[0].bounds;
        foreach (var renderer in first) bounds.Encapsulate(renderer.bounds);
        Vector3 canopy = new Vector3(bounds.center.x, bounds.max.y - 2, bounds.center.z);
        camera.transform.position = canopy + new Vector3(5, 1.4f, -7.7f);
        camera.transform.LookAt(canopy); camera.fieldOfView = 42;
        Shot("CanopyDetail");
        for (int i = 1; i < forest.GeneratedRoot.childCount; i++) forest.GeneratedRoot.GetChild(i).gameObject.SetActive(true);
        camera.transform.position = new Vector3(7.5f, 3.1f, -18);
        camera.transform.LookAt(new Vector3(-1.5f, 6.4f, -1)); camera.fieldOfView = 61;
        Shot("PlayerScale");
        camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target);
    }
}
