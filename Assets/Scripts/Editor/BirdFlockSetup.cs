using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Repeatable prefab creation and explicit, undoable placement in Scene_Yoyo.</summary>
public static class BirdFlockSetup
{
    public const string BirdPath = "Assets/Prefabs/Creatures/BirdCreature.prefab";
    public const string MaterialPath = "Assets/Materials/Creatures/BirdCube.mat";
    public const string YoyoPath = "Assets/Scenes/Scene_Yoyo.unity";
    public const string RootName = "Bird Flock Setup";

    [MenuItem("Tools/Creatures/Create Bird Flock Assets")]
    public static void CreateAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before creating bird assets.");
        EnsureFolder("Assets/Prefabs/Creatures");
        EnsureFolder("Assets/Materials/Creatures");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("URP Lit shader is unavailable.");
            material = new Material(shader) { name = "BirdCube", enableInstancing = true };
            material.SetColor("_BaseColor", new Color(0.12f, 0.62f, 0.7f));
            material.SetFloat("_Smoothness", 0.2f);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(BirdPath) == null)
        {
            GameObject root = new GameObject("BirdCreature");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.mass = 0.25f;
                body.angularDamping = 4f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.size = new Vector3(0.45f, 0.3f, 0.8f);
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Body";
                cube.transform.SetParent(root.transform, false);
                cube.transform.localScale = collider.size;
                Object.DestroyImmediate(cube.GetComponent<Collider>());
                cube.GetComponent<Renderer>().sharedMaterial = material;
                BirdCreature bird = root.AddComponent<BirdCreature>();
                var fields = new SerializedObject(bird);
                fields.FindProperty("creatureName").stringValue = "Bird";
                fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, BirdPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        // Upgrade existing birds without rebuilding their mesh, material, or tuned flight settings.
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BirdPath);
        if (existingPrefab.GetComponent<WebCapture>() == null)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(BirdPath);
            try
            {
                contents.AddComponent<WebCapture>();
                PrefabUtility.SaveAsPrefabAsset(contents, BirdPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Bird prefab ready: " + BirdPath);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/');
        string parent = path.Substring(0, split);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(split + 1));
    }

    [MenuItem("Tools/Creatures/Add Bird Flock To Scene Yoyo")]
    public static void AddToYoyoScene()
    {
        CreateAssets();
        Scene scene = SceneManager.GetSceneByPath(YoyoPath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(YoyoPath, OpenSceneMode.Additive);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name != RootName) continue;
            Selection.activeGameObject = root;
            Debug.Log("Bird flock already placed; existing Inspector settings were preserved.");
            return;
        }
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Place Bird Flock");
        GameObject setup = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(setup, scene);
        Undo.RegisterCreatedObjectUndo(setup, "Place Bird Flock");
        GameObject managerObject = new GameObject("Bird Flock Manager");
        managerObject.transform.SetParent(setup.transform, false);
        managerObject.AddComponent<BirdFlockManager>();
        BirdCreature prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BirdPath).GetComponent<BirdCreature>();
        Vector3[] centers = { new Vector3(-24f, 17f, -20f), new Vector3(0f, 21f, 5f), new Vector3(26f, 19f, -24f) };
        for (int i = 0; i < centers.Length; i++)
        {
            GameObject area = new GameObject("Bird Spawn Area " + (i + 1));
            area.transform.SetParent(setup.transform, false);
            area.transform.position = centers[i];
            area.transform.rotation = Quaternion.LookRotation(new Vector3(-centers[i].x, 0f, -15f - centers[i].z).normalized);
            area.AddComponent<BirdSpawnArea>().Configure(prefab, 24, 5f, 1234 + i * 100);
        }
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = setup;
        Debug.Log("Three bird spawn areas placed in Scene_Yoyo (24 birds each). Save the scene to retain placement.");
    }
}
