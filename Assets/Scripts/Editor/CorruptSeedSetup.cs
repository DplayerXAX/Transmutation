using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Creates the seed assets without placing objects or overwriting existing art and tuning.</summary>
public static class CorruptSeedSetup
{
    public const string SeedPath = "Assets/Prefabs/Creatures/CorruptSeed.prefab";
    public const string DefinitionPath = "Assets/Prefabs/Creatures/CorruptSeedDefinition.asset";
    public const string MaterialPath = "Assets/Materials/Creatures/CorruptSeed.mat";

    [MenuItem("Tools/Creatures/Create Corrupt Seed Assets")]
    public static void CreateAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before creating Corrupt Seed assets.");
        EnsureFolder("Assets/Prefabs/Creatures");
        EnsureFolder("Assets/Materials/Creatures");

        CorruptSeedDefinition definition = AssetDatabase.LoadAssetAtPath<CorruptSeedDefinition>(DefinitionPath);
        if (definition == null)
        {
            definition = ScriptableObject.CreateInstance<CorruptSeedDefinition>();
            AssetDatabase.CreateAsset(definition, DefinitionPath);
        }
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("URP Lit shader is unavailable.");
            material = new Material(shader) { name = "CorruptSeed", enableInstancing = true };
            material.SetColor("_BaseColor", new Color(0.22f, 0.055f, 0.3f));
            material.SetFloat("_Smoothness", 0.3f);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(SeedPath) == null)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = new GameObject("CorruptSeed");
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, preview);
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.mass = 0.05f;
                body.useGravity = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                root.AddComponent<SphereCollider>().radius = 0.1f;

                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.name = "Seed Body";
                visual.transform.SetParent(root.transform, false);
                visual.transform.localScale = Vector3.one * 0.2f;
                Object.DestroyImmediate(visual.GetComponent<Collider>());
                visual.GetComponent<Renderer>().sharedMaterial = material;

                CorruptSeed seed = root.AddComponent<CorruptSeed>();
                using (var fields = new SerializedObject(seed))
                {
                    fields.FindProperty("creatureName").stringValue = "Corrupt Seed";
                    fields.FindProperty("foodTags").intValue = (int)Creature.FoodTag.Plant;
                    fields.FindProperty("dietTags").intValue = (int)Creature.FoodTag.None;
                    fields.FindProperty("ageRate").floatValue = 0f;
                    fields.FindProperty("offspringDefinition").objectReferenceValue = definition;
                    fields.ApplyModifiedPropertiesWithoutUndo();
                }
                root.SetActive(true);
                PrefabUtility.SaveAsPrefabAsset(root, SeedPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }

        // Repair missing links only. Existing references and artist tuning remain authoritative.
        GameObject contents = PrefabUtility.LoadPrefabContents(SeedPath);
        try
        {
            CorruptSeed seed = contents.GetComponent<CorruptSeed>();
            if (seed == null) throw new InvalidOperationException("Corrupt Seed prefab is missing its creature component.");
            using (var fields = new SerializedObject(seed))
            {
                if (fields.FindProperty("offspringDefinition").objectReferenceValue == null)
                    fields.FindProperty("offspringDefinition").objectReferenceValue = definition;
                if (fields.ApplyModifiedPropertiesWithoutUndo()) PrefabUtility.SaveAsPrefabAsset(contents, SeedPath);
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }

        using (var fields = new SerializedObject(definition))
        {
            if (fields.FindProperty("prefab").objectReferenceValue == null)
                fields.FindProperty("prefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CorruptSeed>(SeedPath);
            fields.ApplyModifiedPropertiesWithoutUndo();
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Corrupt Seed prefab ready: " + SeedPath);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/');
        string parent = path.Substring(0, split);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(split + 1));
    }
}
