using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Author reusable honey/equipment assets and their runtime reference library without editing scenes.</summary>
public static class GameplayPrefabSetup
{
    public const string HoneyPath = "Assets/Prefabs/Creatures/LightEcology/GlowHoney.prefab";
    public const string ShoesPath = "Assets/Prefabs/Equipment/SkateShoes/SkateShoes.prefab";
    public const string VisualPath = "Assets/Prefabs/Equipment/SkateShoes/SkateShoesVisual.prefab";
    private const string LibraryPath = "Assets/Resources/GameplayPrefabLibrary.asset";
    private const string MaterialFolder = "Assets/Materials/Equipment/SkateShoes";

    [MenuItem("Tools/Capstone/Prefabs/Create Missing Honey And Skate Shoes Assets")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before authoring prefabs.");
        foreach (string path in new[] { HoneyPath, ShoesPath, VisualPath, LibraryPath, MaterialFolder + "/placeholder" })
            Directory.CreateDirectory(Path.GetDirectoryName(path));
        AssetDatabase.Refresh();
        CreateVisual();
        CreateShoes();
        CreateHoney();
        var library = AssetDatabase.LoadAssetAtPath<GameplayPrefabLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<GameplayPrefabLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }
        library.glowHoney = AssetDatabase.LoadAssetAtPath<GameObject>(HoneyPath).GetComponent<GlowHoney>();
        library.skateShoes = AssetDatabase.LoadAssetAtPath<GameObject>(ShoesPath).GetComponent<SkateShoesPickup>();
        library.skateShoesVisual = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath).GetComponent<SkateShoesVisuals>();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        Debug.Log("Honey and skate shoe prefabs are classified under Assets/Prefabs and connected to GameplayPrefabLibrary.");
    }

    private static void CreateVisual()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath) != null) return;
        var root = new GameObject("SkateShoesVisual");
        try
        {
            var visual = root.AddComponent<SkateShoesVisuals>();
            visual.BuildModel();
            foreach (Material material in root.GetComponentsInChildren<Renderer>().Select(r => r.sharedMaterial).Distinct())
            {
                string path = MaterialFolder + "/" + material.name.Replace(" ", "") + ".mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing == null) AssetDatabase.CreateAsset(material, path);
                else
                {
                    foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                        if (renderer.sharedMaterial == material) renderer.sharedMaterial = existing;
                    var fields = new SerializedObject(visual);
                    foreach (string field in new[] { "upper", "sole", "rubber", "accent" })
                        if (fields.FindProperty(field).objectReferenceValue == material)
                            fields.FindProperty(field).objectReferenceValue = existing;
                    fields.ApplyModifiedPropertiesWithoutUndo();
                    Object.DestroyImmediate(material);
                }
            }
            PrefabUtility.SaveAsPrefabAsset(root, VisualPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void CreateShoes()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ShoesPath) != null) return;
        var root = new GameObject("SkateShoes");
        try
        {
            root.AddComponent<SkateShoesPickup>();
            var collider = root.GetComponent<BoxCollider>();
            collider.size = new Vector3(0.75f, 0.5f, 0.6f);
            collider.center = Vector3.up * 0.21f;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath));
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.up * 0.13f;
            PrefabUtility.SaveAsPrefabAsset(root, ShoesPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void CreateHoney()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(HoneyPath) != null) return;
        const string materialPath = "Assets/Materials/Creatures/LightEcology/GlowHoney.mat";
        Directory.CreateDirectory(Path.GetDirectoryName(materialPath));
        AssetDatabase.Refresh();
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "GlowHoney" };
            material.SetColor("_BaseColor", new Color(0.55f, 1f, 0.23f));
            material.SetFloat("_Smoothness", 0.65f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(0.7f, 1f, 0.24f) * 3f);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        var root = new GameObject("GlowHoney") { layer = 9 };
        try
        {
            root.AddComponent<GlowHoney>();
            var body = root.GetComponent<Rigidbody>();
            body.mass = 0.2f;
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            root.GetComponent<SphereCollider>().radius = 0.18f;
            var fields = new SerializedObject(root.GetComponent<GlowHoney>());
            fields.FindProperty("creatureName").stringValue = "Glow Honey";
            fields.FindProperty("foodTags").intValue = (int)Creature.FoodTag.Plant;
            fields.FindProperty("luminosity").floatValue = 50f;
            fields.ApplyModifiedPropertiesWithoutUndo();
            GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            drop.name = "Luminous honey drop";
            drop.layer = 9;
            drop.transform.SetParent(root.transform, false);
            drop.transform.localScale = new Vector3(0.32f, 0.38f, 0.32f);
            Object.DestroyImmediate(drop.GetComponent<Collider>());
            var renderer = drop.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            drop.AddComponent<GlowBeeLight>().Configure(renderer, new Color(0.7f, 1f, 0.24f), 2.5f);
            PrefabUtility.SaveAsPrefabAsset(root, HoneyPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    public static void RunBatch()
    {
        if (!Application.dataPath.Replace('\\', '/').Contains("/.utmp/GlowBeeChecks/UnityProject/Assets"))
            throw new InvalidOperationException("Batch authoring is restricted to the disposable project.");
        try
        {
            Build();
            int count = 0;
            var legacyIssues = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) throw new InvalidOperationException("Prefab cannot be loaded: " + path);
                if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Any(component => component == null))
                {
                    if (path.Contains("/Prototypes/")) { legacyIssues.Add(path); continue; }
                    throw new InvalidOperationException("Missing script in " + path);
                }
                count++;
            }
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../../prefab-results.txt")),
                "PASS: " + count + " classified prefabs load without missing scripts; honey/equipment prefabs and runtime library created.\n" +
                "Pre-existing prototype script issues (preserved): " + string.Join(", ", legacyIssues));
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
}
