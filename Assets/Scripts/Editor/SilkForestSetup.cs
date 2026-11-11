using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class SilkForestSetup
{
    public const string ScenePath = "Assets/Scenes/Scene_SilkForest.unity";
    public const string PrefabPath = "Assets/Prefabs/World/SilkForest.prefab";
    public const string MaterialFolder = "Assets/Materials/World/SilkForest";
    private const string LibraryFolder = "Assets/Generated/SilkForest";

    [MenuItem("Tools/Capstone/Silk Forest/Create Forest In Current Scene")]
    public static void CreateInCurrentScene()
    {
        var obj = new GameObject("黏菌蛛丝树林 · Silk Forest Manager");
        Undo.RegisterCreatedObjectUndo(obj, "Create silk forest manager");
        var manager = obj.AddComponent<SilkForestManager>();
        AssignMaterials(manager);
        manager.Regenerate();
        Selection.activeGameObject = obj;
        EditorSceneManager.MarkSceneDirty(obj.scene);
    }

    [MenuItem("Tools/Capstone/Silk Forest/Open Demo Scene")]
    public static void OpenDemo()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (File.Exists(ScenePath)) EditorSceneManager.OpenScene(ScenePath);
        else BuildDemo();
    }

    [MenuItem("Tools/Capstone/Silk Forest/Rebuild Demo Scene")]
    public static void RebuildDemo()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildDemo();
    }

    public static void AssignMaterials(SilkForestManager manager)
    {
        Directory.CreateDirectory(MaterialFolder);
        manager.silkMaterial = MaterialAsset(MaterialFolder + "/MAT_SilkPearl.mat", "Capstone/Silk Forest/Pearlescent Silk");
        manager.membraneMaterial = MaterialAsset(MaterialFolder + "/MAT_SilkMembrane.mat", "Capstone/Silk Forest/Thin Film");
    }

    private static Material MaterialAsset(string path, string shaderName)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find(shaderName);
        if (shader == null) throw new InvalidOperationException("Missing shader: " + shaderName);
        material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    public static void SaveForestPrefab(SilkForestManager manager, string path)
    {
        if (manager.GeneratedRoot == null) manager.Regenerate();
        BakeMeshes(manager);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        PrefabUtility.SaveAsPrefabAsset(manager.gameObject, path);
        AssetDatabase.SaveAssets();
    }

    public static void BakeMeshes(SilkForestManager manager)
    {
        if (manager.GeneratedRoot == null) return;
        var filters = manager.GeneratedRoot.GetComponentsInChildren<MeshFilter>();
        if (!Array.Exists(filters, f => f.sharedMesh != null && !EditorUtility.IsPersistent(f.sharedMesh))) return;
        Directory.CreateDirectory(LibraryFolder);
        string libraryPath = AssetDatabase.GenerateUniqueAssetPath(LibraryFolder + "/SilkForestMeshes.asset");
        var library = ScriptableObject.CreateInstance<SilkForestMeshLibrary>();
        AssetDatabase.CreateAsset(library, libraryPath);
        foreach (var filter in filters)
        {
            if (filter.sharedMesh == null || EditorUtility.IsPersistent(filter.sharedMesh)) continue;
            AssetDatabase.AddObjectToAsset(filter.sharedMesh, library);
        }
        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssetIfDirty(library);
    }

    public static SilkForestManager BuildDemo()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.35f, 0.46f, 0.52f);
        RenderSettings.ambientEquatorColor = new Color(0.23f, 0.25f, 0.31f);
        RenderSettings.ambientGroundColor = new Color(0.09f, 0.13f, 0.15f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.065f, 0.095f, 0.12f);
        RenderSettings.fogDensity = 0.012f;
        var key = new GameObject("Moonlight · warm pearl rim").AddComponent<Light>();
        key.type = LightType.Directional; key.intensity = 2.1f;
        key.color = new Color(0.92f, 0.9f, 0.79f);
        key.transform.rotation = Quaternion.Euler(36, -32, 0);
        key.shadows = LightShadows.Soft;
        var fill = new GameObject("Cool canopy fill").AddComponent<Light>();
        fill.type = LightType.Directional; fill.intensity = 0.5f;
        fill.color = new Color(0.35f, 0.7f, 0.9f);
        fill.transform.rotation = Quaternion.Euler(22, 155, 0);
        var manager = new GameObject("黏菌蛛丝树林 · Silk Forest Manager").AddComponent<SilkForestManager>();
        AssignMaterials(manager); manager.Regenerate();
        SaveForestPrefab(manager, PrefabPath);
        var floorMaterial = MaterialAsset(MaterialFolder + "/MAT_ForestGround.mat", "Universal Render Pipeline/Lit");
        floorMaterial.SetColor("_BaseColor", new Color(0.075f, 0.105f, 0.115f));
        floorMaterial.SetFloat("_Smoothness", 0.18f);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Quiet forest floor";
        ground.transform.localScale = new Vector3(8, 1, 8);
        ground.transform.position = Vector3.down * 0.035f;
        ground.GetComponent<Renderer>().sharedMaterial = floorMaterial;
        CreateScaleFigure(new Vector3(0, 0, -6));
        var camera = new GameObject("Forest overview · Camera").AddComponent<Camera>();
        camera.tag = "MainCamera"; camera.fieldOfView = 51; camera.nearClipPlane = 0.1f; camera.farClipPlane = 160;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = RenderSettings.fogColor;
        camera.transform.position = new Vector3(24, 11, -34); camera.transform.LookAt(new Vector3(0, 6.7f, 0));
        camera.gameObject.AddComponent<AudioListener>();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = manager.gameObject;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.LookAt(new Vector3(0, 6, 0), Quaternion.Euler(12, -30, 0), 27);
        return manager;
    }

    private static void CreateScaleFigure(Vector3 position)
    {
        var material = MaterialAsset(MaterialFolder + "/MAT_PlayerScale.mat", "Universal Render Pipeline/Lit");
        material.SetColor("_BaseColor", new Color(0.95f, 0.5f, 0.17f));
        material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", new Color(0.3f, 0.09f, 0.015f));
        var root = new GameObject("Player scale reference · 1.8 metres");
        root.transform.position = position;
        var geometry = new SilkForestGeometry.MeshBuffer();
        void Limb(Vector3 a, Vector3 b, float radius) => geometry.Tube(new[] { a, b }, radius, 8, Color.white);
        Limb(new Vector3(0,0.86f,0), new Vector3(0,1.45f,0), 0.07f);
        Limb(new Vector3(0,0.9f,0), new Vector3(-0.19f,0.04f,0), 0.045f);
        Limb(new Vector3(0,0.9f,0), new Vector3(0.19f,0.04f,0), 0.045f);
        Limb(new Vector3(0,1.38f,0), new Vector3(-0.29f,0.93f,0), 0.035f);
        Limb(new Vector3(0,1.38f,0), new Vector3(0.29f,0.93f,0), 0.035f);
        var mesh = geometry.CreateMesh("1.8m scale figure");
        string meshPath = LibraryFolder + "/PlayerScaleMesh.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (existing == null) AssetDatabase.CreateAsset(mesh, meshPath);
        else { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterial = material;
        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head · scale reference"; head.transform.SetParent(root.transform, false);
        head.transform.localPosition = new Vector3(0,1.64f,0); head.transform.localScale = new Vector3(0.25f,0.32f,0.25f);
        head.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(head.GetComponent<Collider>());
    }
}

// Procedural preview meshes must become real assets before a scene is saved.
// Otherwise a regenerated forest would disappear after reopening the scene.
[InitializeOnLoad]
internal static class SilkForestSceneSave
{
    static SilkForestSceneSave() { EditorSceneManager.sceneSaving += Save; }
    private static void Save(Scene scene, string path)
    {
        foreach (var root in scene.GetRootGameObjects())
        foreach (var forest in root.GetComponentsInChildren<SilkForestManager>(true))
            SilkForestSetup.BakeMeshes(forest);
    }
}
