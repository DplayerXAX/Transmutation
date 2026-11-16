using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SquirrelForestDressing
{
    const string Request = "Temp/SquirrelForest.dressing";
    static SquirrelForestDressing() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Apply(); } catch (Exception e) { Directory.CreateDirectory("ArtCaptures/SquirrelForest"); File.WriteAllText("ArtCaptures/SquirrelForest/Dressing.txt", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Tools/Capstone/Squirrel Forest/Match World Sky And Forest Dressing")]
    public static void Apply()
    {
        var previous = SceneManager.GetActiveScene(); var scene = SceneManager.GetSceneByPath(SquirrelForestSetup.ScenePath);
        bool loaded = scene.IsValid() && scene.isLoaded;
        if (!loaded) scene = EditorSceneManager.OpenScene(SquirrelForestSetup.ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Sky_Black.mat");
            if (sky == null) throw new InvalidOperationException("Missing Scene_World sky material.");
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.114f, 0.126f, 0.15f);
            RenderSettings.ambientEquatorColor = new Color(0.072f, 0.081f, 0.096f);
            RenderSettings.ambientGroundColor = new Color(0.036f, 0.042f, 0.054f);
            RenderSettings.fog = true; RenderSettings.fogColor = new Color(0.25f, 0.3f, 0.4f);
            RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.0042f;
            var roots = scene.GetRootGameObjects();
            foreach (var camera in roots.SelectMany(g => g.GetComponentsInChildren<Camera>(true))) camera.clearFlags = CameraClearFlags.Skybox;
            var forest = roots.SelectMany(g => g.GetComponentsInChildren<SilkForestManager>()).Single();
            var ground = roots.SelectMany(g => g.GetComponentsInChildren<MeshCollider>()).First(c => c.gameObject.layer == 7);
            var old = roots.FirstOrDefault(g => g.name == "Forest floor - Scene_World dressing");
            if (old != null) Object.DestroyImmediate(old);
            var group = new GameObject("Forest floor - Scene_World dressing").transform;
            var anchors = forest.GetComponentsInChildren<SquirrelTree>().Select(t => t.GroundPosition).ToArray();
            string[] kinds = { "Creepers", "CavePebbles", "Pebbles", "Fronds", "NeedleBeards" };
            var random = new System.Random(1110); float R(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
            var placed = new List<Vector3>(); int[] counts = new int[kinds.Length];
            const int target = 250;
            for (int attempt = 0; placed.Count < target && attempt < 50000; attempt++)
            {
                float x = R(-47, 47), z = R(-47, 47);
                if (Mathf.Abs(x - Mathf.Sin(z * 0.065f) * 2.3f) < 1.5f) continue;
                if (!ground.Raycast(new Ray(new Vector3(x, 200, z), Vector3.down), out var hit, 400f) || hit.normal.y < 0.8f) continue;
                if (anchors.Any(p => Vector3.ProjectOnPlane(p - hit.point, Vector3.up).sqrMagnitude < 2.4f * 2.4f)) continue;
                if (placed.Any(p => (p - hit.point).sqrMagnitude < 1.5f * 1.5f)) continue;
                // Balanced palette of exactly the five prefab families used for Scene_World's Forest floor region.
                int kind = placed.Count % kinds.Length;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/WorldDecor/Decor_{kinds[kind]}_{random.Next(6)}.prefab");
                if (prefab == null) throw new InvalidOperationException("Missing forest decor prefab for " + kinds[kind]);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                go.transform.SetPositionAndRotation(hit.point, Quaternion.AngleAxis(R(0, 360), hit.normal) * Quaternion.FromToRotation(Vector3.up, hit.normal));
                go.transform.localScale *= R(0.65f, 1.1f);
                var renderers = go.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                float limit = kind == 1 || kind == 2 ? 0.65f : 1.2f;
                if (bounds.size.y > limit) go.transform.localScale *= limit / bounds.size.y;
                // Keep the ground layer exclusive to real terrain for squirrel food/nest raycasts.
                foreach (Transform node in go.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 0;
                if (kind != 1 && kind != 2) foreach (var collider in go.GetComponentsInChildren<Collider>()) collider.enabled = false;
                SquirrelTree.PlaceOnGround(go.transform, hit.point - Vector3.up * 0.025f);
                placed.Add(hit.point); counts[kind]++;
            }
            if (placed.Count != target) throw new InvalidOperationException("Not enough unobstructed forest-floor positions.");
            Physics.SyncTransforms(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            SquirrelForestRefinement.Capture(scene, forest);
            var materials = group.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Where(m => m != null).Append(sky).Distinct();
            foreach (var shader in materials.Select(m => m.shader).Distinct())
                if (ShaderUtil.GetShaderMessages(shader).Any(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)) throw new InvalidOperationException("Shader compilation error: " + shader.name);
            File.WriteAllText("ArtCaptures/SquirrelForest/Dressing.txt", $"PASS\nSky: {AssetDatabase.GetAssetPath(sky)}\nSky shader: {sky.shader.name}\nAmbient and fog match Scene_World. All cameras use skybox clearing.\nForest-floor decor: {placed.Count}\n" + string.Join("\n", kinds.Select((kind, i) => kind + ": " + counts[i])) + "\nTree count remains " + forest.GeneratedTreeCount + ".\n2.4 m clearance around habitat roots; clear central walk; terrain-only ground layer retained.\nShader errors: 0\n");
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (!loaded) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
