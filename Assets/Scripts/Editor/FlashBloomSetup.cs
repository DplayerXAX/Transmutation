using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Reusable prefab and a separate playground; existing scenes are never rewritten.</summary>
public static class FlashBloomSetup
{
    public const string Folder = "Assets/Generated/FlashBloom";
    public const string PrefabPath = Folder + "/FlashBloom.prefab";

    private static Material Material(string name, Color color, bool glow = false)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.55f);
        if (glow) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", LuminosityLighting.Emission(color, 3f)); }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    [MenuItem("Tools/Creatures/Flash Bloom/Create Prefab")]
    public static void CreatePrefab()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        System.IO.Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
        Material glow = Material("FlashBloomGlow", new Color(0.65f, 0.9f, 1f), true);
        Material shell = Material("FlashBloomShell", new Color(0.13f, 0.23f, 0.3f));
        Material eye = Material("FlashBloomEyes", new Color(0.015f, 0.025f, 0.045f));
        var root = new GameObject("Flash Bloom");
        try
        {
            root.layer = 9;
            root.AddComponent<Rigidbody>();
            root.AddComponent<SphereCollider>().radius = 0.36f;
            var bloom = root.AddComponent<FlashBloom>();
            using (var fields = new SerializedObject(bloom))
            {
                fields.FindProperty("creatureName").stringValue = "Flash Bloom";
                fields.FindProperty("minimumImpactSpeed").floatValue = 1f;
                fields.FindProperty("luminosity").floatValue = 3f;
                fields.ApplyModifiedPropertiesWithoutUndo();
            }
            Part(root.transform, "Glow sac", Vector3.zero, new Vector3(0.68f, 0.6f, 0.68f), glow);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                Part(root.transform, "Protective petal " + i, new Vector3(Mathf.Cos(a) * 0.26f, -0.19f, Mathf.Sin(a) * 0.26f),
                    new Vector3(0.24f, 0.22f, 0.24f), shell);
            }
            Part(root.transform, "Left eye", new Vector3(-0.12f, 0.08f, 0.29f), Vector3.one * 0.085f, eye);
            Part(root.transform, "Right eye", new Vector3(0.12f, 0.08f, 0.29f), Vector3.one * 0.085f, eye);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        part.name = name;
        part.layer = parent.gameObject.layer;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.GetComponent<Renderer>().sharedMaterial = material;
    }

    [MenuItem("Tools/Creatures/Flash Bloom/Place Near Player")]
    public static void PlaceNearPlayer()
    {
        CreatePrefab();
        var player = Object.FindFirstObjectByType<SmoothFirstPersonController>();
        if (player == null) throw new InvalidOperationException("Open a scene with a player first.");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), player.gameObject.scene);
        Undo.RegisterCreatedObjectUndo(instance, "Place Flash Bloom");
        instance.transform.position = player.transform.position + Vector3.forward * 2f + Vector3.up * 0.5f;
        Selection.activeGameObject = instance;
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
    }

}
