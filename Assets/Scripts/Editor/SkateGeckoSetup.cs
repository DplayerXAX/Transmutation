using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SkateGeckoSetup
{
    public static bool IsBuilding { get; private set; }
    public const string PrefabPath = "Assets/Prefabs/Creatures/SkateGecko/GiantSkateGecko.prefab";
    static SkateGeckoSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists("Temp/SkateGecko.build") || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete("Temp/SkateGecko.build");
        try { IsBuilding = true; Build(); } catch (Exception e) { File.WriteAllText("ArtCaptures/SquirrelForest/GeckoSetup.txt", e.ToString()); Debug.LogException(e); }
        finally { IsBuilding = false; }
    }
    [MenuItem("Tools/Capstone/Gecko/Create Gecko And Place In Forest")]
    public static void Build()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath)); AssetDatabase.Refresh();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            var temp = new GameObject("Giant Skate Gecko");
            try
            {
                temp.AddComponent<SkateGecko>();
                if (temp.transform.Find("Gecko model") == null) SkateGeckoVisuals.Build(temp.transform);
                var capsule = temp.GetComponent<CapsuleCollider>(); capsule.direction = 2; capsule.height = 2.8f; capsule.radius = .24f;
                var body = temp.GetComponent<Rigidbody>(); body.mass = 8; body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; body.interpolation = RigidbodyInterpolation.Interpolate;
                var materials = temp.GetComponentsInChildren<Renderer>().Select(r => r.sharedMaterial).Distinct().ToArray();
                for (int i = 0; i < materials.Length; i++) AssetDatabase.CreateAsset(materials[i], Path.GetDirectoryName(PrefabPath) + $"/Gecko_{i}.mat");
                prefab = PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
            }
            finally { Object.DestroyImmediate(temp); }
        }
        var library = GameplayPrefabLibrary.Load(); if (library == null) throw new Exception("Missing gameplay library.");
        library.skateGecko = prefab.GetComponent<SkateGecko>(); EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
        Capture(prefab);
        var scene = SceneManager.GetSceneByPath(SquirrelForestSetup.ScenePath);
        bool loaded = scene.IsValid() && scene.isLoaded; var previous = SceneManager.GetActiveScene();
        if (!loaded) scene = EditorSceneManager.OpenScene(SquirrelForestSetup.ScenePath, OpenSceneMode.Additive);
        try
        {
            var roots = scene.GetRootGameObjects();
            var ground = roots.SelectMany(g => g.GetComponentsInChildren<MeshCollider>()).First(c => c.gameObject.layer == 7);
            if (!roots.Any(g => g.GetComponent<SkateGecko>() != null))
            {
                var player = roots.SelectMany(g => g.GetComponentsInChildren<SmoothFirstPersonController>()).FirstOrDefault();
                Vector3 origin = player != null ? player.transform.position : new Vector3(0, 0, -35);
                Vector3 forward = player != null ? Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized : Vector3.forward;
                // Start close enough to see and chase, beside the existing open path.
                Vector3 at = origin + forward * 8 + Vector3.right * 1.5f;
                if (!ground.Raycast(new Ray(at + Vector3.up * 200, Vector3.down), out var hit, 400)) throw new Exception("No ground at gecko placement.");
                var gecko = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); gecko.name = "Giant Skate Gecko - chase and ride";
                gecko.transform.SetPositionAndRotation(hit.point + Vector3.up * .26f, Quaternion.LookRotation(forward));
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            File.WriteAllText("ArtCaptures/SquirrelForest/GeckoSetup.txt", "PASS: reusable giant gecko prefab created, linked in gameplay library, and placed near the forest player. Legacy shoe pickups spawn this gecko.\n");
        }
        finally { if (!loaded) EditorSceneManager.CloseScene(scene, true); if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); }
    }
    static void Capture(GameObject prefab)
    {
        var preview = new PreviewRenderUtility();
        try
        {
            var model = Object.Instantiate(prefab); preview.AddSingleGO(model);
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            preview.camera.transform.position = new Vector3(3.3f, 2.6f, 4.2f);
            preview.camera.transform.LookAt(new Vector3(0, 0, -.15f));
            preview.camera.nearClipPlane = .05f; preview.camera.farClipPlane = 30;
            preview.camera.fieldOfView = 45; preview.camera.aspect = 1.375f;
            preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(.035f, .045f, .055f);
            preview.lights[0].intensity = 1.3f; preview.lights[0].transform.rotation = Quaternion.Euler(45, 30, 0);
            preview.lights[1].intensity = .8f; preview.ambientColor = new Color(.35f, .4f, .45f);
            preview.BeginPreview(new Rect(0, 0, 1100, 800), GUIStyle.none); preview.Render(true);
            var rendered = (RenderTexture)preview.EndPreview(); var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = rendered; var png = new Texture2D(1100, 800, TextureFormat.RGB24, false);
                png.ReadPixels(new Rect(0, 0, 1100, 800), 0, 0); png.Apply();
                File.WriteAllBytes("ArtCaptures/SquirrelForest/Gecko.png", png.EncodeToPNG()); Object.DestroyImmediate(png);
            }
            finally { RenderTexture.active = previous; }
        }
        finally { preview.Cleanup(); }
    }
}
