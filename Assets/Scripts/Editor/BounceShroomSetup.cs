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
        const string prefabPath = "Assets/Prefabs/Creatures/BounceShroom.prefab";
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
}

