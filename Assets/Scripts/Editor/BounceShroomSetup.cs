using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class BounceShroomSetup
{
    private const string PendingSetup = "Library/BounceShroomSetup.pending";

    [InitializeOnLoadMethod]
    private static void CompletePendingSetup()
    {
        if (!System.IO.File.Exists(PendingSetup)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlaying || !System.IO.File.Exists(PendingSetup)) return;
            Main();
            System.IO.File.Delete(PendingSetup);
            Debug.Log("BounceShroom prefab created and added to Scene_Yoyo.");
        };
    }

    [MenuItem("Tools/Creatures/Add BounceShroom to Scene Yoyo")]
    public static void AddToScene() => Main();

    public static object Main()
    {
        if (EditorApplication.isPlaying) throw new System.Exception("Exit Play Mode before creating assets.");
        const string prefabPath = "Assets/Prefabs/Creatures/Traversal/BounceShroom.prefab";
        const string materialPath = "Assets/Materials/Creatures/BounceShroom.mat";
        const string physicsPath = "Assets/Materials/Creatures/BouncySurface.physicMaterial";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = "BounceShroom";
            material.SetColor("_BaseColor", new Color(0.2f, 0.8f, 0.45f));
            material.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        PhysicsMaterial physics = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(physicsPath);
        if (physics == null)
        {
            physics = new PhysicsMaterial("BouncySurface") { dynamicFriction = 0f, staticFriction = 0f,
                bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
            AssetDatabase.CreateAsset(physics, physicsPath);
        }
        PhysicsMaterial grip = CompactGrip();
        GameObject root = new GameObject("BounceShroom");
        GameObject prefab;
        try
        {
            root.layer = LayerMask.NameToLayer("Ground");
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Platform";
            block.layer = root.layer;
            block.transform.SetParent(root.transform, false);
            block.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            block.transform.localScale = new Vector3(1f, 0.5f, 1f);
            block.GetComponent<Renderer>().sharedMaterial = material;
            block.GetComponent<Collider>().sharedMaterial = physics;
            BouncySurface surface = root.AddComponent<BouncySurface>();
            SerializedObject fields = new SerializedObject(surface);
            fields.FindProperty("platformCollider").objectReferenceValue = block.GetComponent<BoxCollider>();
            fields.FindProperty("bouncy").boolValue = false;
            fields.ApplyModifiedPropertiesWithoutUndo();
            BounceShroom shroom = root.AddComponent<BounceShroom>();
            fields = new SerializedObject(shroom);
            fields.FindProperty("platform").objectReferenceValue = block.transform;
            fields.FindProperty("creatureName").stringValue = "BounceShroom";
            fields.FindProperty("compactMaterial").objectReferenceValue = grip;
            SetLifespan(fields);
            fields.ApplyModifiedPropertiesWithoutUndo();
            prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally { Object.DestroyImmediate(root); }
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/Scene_Yoyo.unity");
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene("Assets/Scenes/Scene_Yoyo.unity", OpenSceneMode.Additive);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        Undo.RegisterCreatedObjectUndo(instance, "Add BounceShroom");
        Vector3 position = new Vector3(0f, 0.4f, 4f);
        foreach (var player in Object.FindObjectsByType<SmoothFirstPersonController>(FindObjectsSortMode.None))
            if (player.gameObject.scene == scene) { position = player.transform.position + Vector3.forward * 4f; break; }
        if (Physics.Raycast(position + Vector3.up * 5f, Vector3.down, out RaycastHit hit,
            100f, LayerMask.GetMask("Ground", "Form"), QueryTriggerInteraction.Ignore)) position.y = hit.point.y;
        instance.transform.position = position;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = instance;
        return new { prefab = prefabPath, scene = scene.path, position = position.ToString() };
    }

    /// <summary>
    /// Friction for a compact (carryable) shroom, so it rests on slopes. The platform keeps the frictionless
    /// BouncySurface material; BounceShroom swaps between the two as it expands and shrinks.
    /// </summary>
    public static PhysicsMaterial CompactGrip()
    {
        const string path = "Assets/Materials/Creatures/BounceShroomGrip.physicMaterial";
        PhysicsMaterial grip = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (grip != null) return grip;
        grip = new PhysicsMaterial("BounceShroomGrip") { dynamicFriction = 0.6f, staticFriction = 0.8f,
            frictionCombine = PhysicsMaterialCombine.Maximum };
        AssetDatabase.CreateAsset(grip, path);
        return grip;
    }

    /// <summary>
    /// An eight-minute life (varied per shroom at runtime); dying of old age it sprouts a young Mushroom Tree.
    /// Builds the tree prefab first if it does not exist yet.
    /// </summary>
    private static void SetLifespan(SerializedObject fields)
    {
        var tree = AssetDatabase.LoadAssetAtPath<MushroomTree>(MushroomTreeSetup.TreePath);
        if (tree == null) tree = MushroomTreeSetup.BuildPrefabs();
        fields.FindProperty("age").floatValue = 480f;
        fields.FindProperty("ageRate").floatValue = 1f;
        fields.FindProperty("growsInto").objectReferenceValue = tree;
    }

    /// <summary>Brings the existing prefab up to date (compact grip, lifespan) without rebuilding it, so scene references stay intact.</summary>
    [MenuItem("Tools/Creatures/BounceShroom/Update Prefab (Grip, Lifespan)")]
    public static void UpdatePrefab()
    {
        const string prefabPath = "Assets/Prefabs/Creatures/BounceShroom.prefab";
        GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var fields = new SerializedObject(contents.GetComponent<BounceShroom>());
            fields.FindProperty("compactMaterial").objectReferenceValue = CompactGrip();
            SetLifespan(fields);
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
    }
}

