using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SquirrelForestRefinement
{
    private const string Request = "Temp/SquirrelForest.refine";
    static SquirrelForestRefinement() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Apply(); }
        catch (Exception e) { File.WriteAllText("ArtCaptures/SquirrelForest/Refinement.txt", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Tools/Capstone/Squirrel Forest/Match World Ground And Player Tree Scale")]
    public static void Apply()
    {
        var previous = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(SquirrelForestSetup.ScenePath);
        bool wasLoaded = scene.IsValid() && scene.isLoaded;
        if (!wasLoaded) scene = EditorSceneManager.OpenScene(SquirrelForestSetup.ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var external = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => c.enabled && c.gameObject.scene != scene).ToArray();
        foreach (var collider in external) collider.enabled = false;
        try
        {
            var roots = scene.GetRootGameObjects();
            var forest = roots.SelectMany(g => g.GetComponentsInChildren<SilkForestManager>()).Single();
            var system = roots.SelectMany(g => g.GetComponentsInChildren<SquirrelSystemManager>()).Single();
            var player = roots.First(g => g.name == "PlayerObjects");
            var body = player.GetComponentsInChildren<CapsuleCollider>().First();
            float height = body.height * Mathf.Abs(body.transform.lossyScale.y);
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/WorldMap/World_InkStone.mat");
            if (material == null) throw new InvalidOperationException("Missing Scene_World surface material.");
            var ground = roots.SelectMany(g => g.GetComponentsInChildren<MeshCollider>()).First(c => c.gameObject.layer == 7);
            ground.GetComponent<Renderer>().sharedMaterial = material;
            forest.overallHeightRange = new Vector2(height, height * 3f);
            Physics.SyncTransforms(); forest.Regenerate();
            var anchors = forest.GetComponentsInChildren<SquirrelTree>();
            var settings = new SerializedObject(system); var trees = settings.FindProperty("trees"); trees.arraySize = anchors.Length;
            for (int i = 0; i < anchors.Length; i++) trees.GetArrayElementAtIndex(i).objectReferenceValue = anchors[i];
            settings.ApplyModifiedPropertiesWithoutUndo();
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Generated/SquirrelForest/ForestTrees_PlayerScale.asset");
            var library = ScriptableObject.CreateInstance<SquirrelForestMeshLibrary>(); AssetDatabase.CreateAsset(library, path);
            foreach (var filter in forest.GeneratedRoot.GetComponentsInChildren<MeshFilter>())
                if (filter.sharedMesh != null && !EditorUtility.IsPersistent(filter.sharedMesh)) AssetDatabase.AddObjectToAsset(filter.sharedMesh, library);
            EditorUtility.SetDirty(library); AssetDatabase.SaveAssetIfDirty(library);
            float smallest = float.PositiveInfinity, tallest = 0f;
            foreach (Transform tree in forest.GeneratedRoot)
            {
                if (!tree.name.StartsWith("Silk tree ")) continue;
                var renderers = tree.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                smallest = Mathf.Min(smallest, bounds.size.y); tallest = Mathf.Max(tallest, bounds.size.y);
                if (bounds.size.y < height - 0.01f || bounds.size.y > height * 3f + 0.01f)
                    throw new InvalidOperationException("Tree outside player-height limits: " + bounds.size.y);
            }
            if (Mathf.Abs(smallest - height) > 0.01f || Mathf.Abs(tallest - height * 3f) > 0.01f)
                throw new InvalidOperationException("Forest does not reach both requested height limits.");
            EditorUtility.SetDirty(forest); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Capture(scene, forest);
            File.WriteAllText("ArtCaptures/SquirrelForest/Refinement.txt", $"PASS\nGround material: {AssetDatabase.GetAssetPath(material)}\nShader: {material.shader.name}\nPlayer height: {height:F3} m\nOverall tree heights including crowns: {smallest:F3} to {tallest:F3} m\nTrees: {forest.GeneratedTreeCount}; low-canopy nest anchors: {anchors.Length}\nScaled tree colliders and vine sockets follow the generated trees.\n");
            Debug.Log($"Squirrel forest refined: overall tree heights {smallest:F2}-{tallest:F2} m; {material.shader.name} ground.");
        }
        finally
        {
            foreach (var collider in external) if (collider != null) collider.enabled = true;
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (!wasLoaded) EditorSceneManager.CloseScene(scene, true);
        }
    }

    public static void Capture(Scene scene, SilkForestManager forest)
    {
        var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).Single(c => c.name == "Forest review camera");
        var originalPosition = camera.transform.position; var originalRotation = camera.transform.rotation;
        camera.transform.position = new Vector3(18, 11, -26); camera.transform.LookAt(new Vector3(0, 2, 0));
        var layers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).Select(r => r.gameObject).Distinct().ToDictionary(g => g, g => g.layer);
        var target = new RenderTexture(1400, 1000, 24); var texture = new Texture2D(1400, 1000, TextureFormat.RGB24, false);
        var old = RenderTexture.active; int mask = camera.cullingMask; bool async = ShaderUtil.allowAsyncCompilation;
        try
        {
            foreach (var go in layers.Keys) go.layer = 29;
            ShaderUtil.allowAsyncCompilation = false; camera.cullingMask = 1 << 29; camera.scene = scene;
            camera.targetTexture = target; camera.aspect = 1.4f; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1400, 1000), 0, 0); texture.Apply(); File.WriteAllBytes("ArtCaptures/SquirrelForest/Overview.png", texture.EncodeToPNG());
        }
        finally
        {
            foreach (var pair in layers) pair.Key.layer = pair.Value;
            camera.transform.SetPositionAndRotation(originalPosition, originalRotation); camera.cullingMask = mask; camera.targetTexture = null;
            ShaderUtil.allowAsyncCompilation = async; RenderTexture.active = old; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(texture);
        }
    }
}
