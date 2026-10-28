using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class LightEcologySetup
{
    public const string Folder = "Assets/Generated/LightEcology";
    public const string MothPath = Folder + "/LightMoth.prefab";
    public const string PodPath = Folder + "/AfterglowPod.prefab";
    public const string DemoPath = "Assets/Scenes/Scene_LightEcologyTest.unity";

    internal static Material Material(string name, Color color, bool glow = false)
    {
        System.IO.Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        string path = Folder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        mat.SetColor("_BaseColor", color);
        if (glow) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", color * 0.1f); }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
    internal static void Part(Transform root, string name, Vector3 position, Vector3 scale, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        part.name = name; part.layer = 9;
        part.transform.SetParent(root, false);
        part.transform.localPosition = position; part.transform.localScale = scale;
        Object.DestroyImmediate(part.GetComponent<Collider>());
        part.GetComponent<Renderer>().sharedMaterial = material;
    }

    [MenuItem("Tools/Creatures/Light Ecology/Create Light Moth Prefab")]
    public static void CreateMoth()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(MothPath) != null) return;
        var body = Material("MothBody", new Color(0.65f, 0.8f, 1f), true);
        var wing = Material("MothWing", new Color(0.42f, 0.32f, 0.64f));
        var eye = Material("MothEye", new Color(0.02f, 0.025f, 0.04f));
        var root = new GameObject("Light Moth"); root.layer = 9;
        try
        {
            root.AddComponent<LightMoth>();
            Part(root.transform, "Body", Vector3.zero, new Vector3(0.16f, 0.17f, 0.32f), body);
            Part(root.transform, "Left wing", new Vector3(-0.23f, 0f, -0.04f), new Vector3(0.45f, 0.035f, 0.35f), wing);
            Part(root.transform, "Right wing", new Vector3(0.23f, 0f, -0.04f), new Vector3(0.45f, 0.035f, 0.35f), wing);
            Part(root.transform, "Left eye", new Vector3(-0.06f, 0.04f, 0.14f), Vector3.one * 0.06f, eye);
            Part(root.transform, "Right eye", new Vector3(0.06f, 0.04f, 0.14f), Vector3.one * 0.06f, eye);
            using (var fields = new SerializedObject(root.GetComponent<LightMoth>()))
            {
                fields.FindProperty("creatureName").stringValue = "Light Moth";
                fields.FindProperty("luminosity").floatValue = 0.5f;
                fields.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(root, MothPath);
            AssetDatabase.SaveAssets();
        }
        finally { Object.DestroyImmediate(root); }
    }

    [MenuItem("Tools/Creatures/Light Ecology/Create Playground")]
    public static void CreatePlayground() => CreateDemo(false);

    public static void CreateDemo(bool rebuild)
    {
        CreateMoth();
        CreatePod();
        if (!rebuild && AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoPath) != null)
        { Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoPath); return; }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.003f, 0.004f, 0.008f);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.layer = 7; ground.name = "Ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f); ground.transform.localScale = new Vector3(36f, 1f, 36f);
        ground.GetComponent<Renderer>().sharedMaterial = Material("EcologyFloor", new Color(0.12f, 0.15f, 0.2f));
        var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PlayerObjects.prefab"), scene);
        player.transform.position = new Vector3(0f, 1.5f, -4f);
        var lamp = new GameObject("Room light — L to toggle").AddComponent<Light>();
        lamp.type = LightType.Point; lamp.range = 25f; lamp.intensity = 20f;
        lamp.transform.position = new Vector3(0f, 4f, 2f); lamp.enabled = false;
        var demo = new GameObject("Playground controls").AddComponent<LightEcologyDemo>(); demo.roomLight = lamp;
        for (int i = 0; i < 6; i++)
        {
            var moth = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MothPath), scene);
            moth.transform.position = new Vector3((i % 3 - 1) * 2f, 1.7f + i * 0.1f, 1f + i / 3);
        }
        var bloomPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FlashBloomSetup.PrefabPath);
        if (bloomPrefab != null)
        {
            var bloom = (GameObject)PrefabUtility.InstantiatePrefab(bloomPrefab, scene);
            bloom.transform.position = new Vector3(2f, 0.4f, -2f);
        }
        for (int i = 0; i < 2; i++)
        {
            var pod = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PodPath), scene);
            pod.transform.position = new Vector3(-2.5f + i * 5f, 0.35f, 0f);
        }
        EditorSceneManager.SaveScene(scene, DemoPath);
        EditorSceneManager.CloseScene(scene, true);
    }

    [MenuItem("Tools/Creatures/Light Ecology/Create Afterglow Pod Prefab")]
    public static void CreatePod()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PodPath) != null) return;
        var shell = Material("PodShell", new Color(0.12f, 0.3f, 0.22f));
        var glow = Material("PodLight", new Color(0.5f, 1f, 0.65f), true);
        var root = new GameObject("Afterglow Pod"); root.layer = 9;
        try
        {
            root.AddComponent<AfterglowPod>();
            Part(root.transform, "Stored light", new Vector3(0f, 0.08f, 0f), new Vector3(0.42f, 0.55f, 0.42f), glow);
            for (int i = 0; i < 5; i++)
            {
                float angle = i * Mathf.PI * 2f / 5f;
                Part(root.transform, "Shell leaf " + i, new Vector3(Mathf.Cos(angle) * 0.22f, -0.06f, Mathf.Sin(angle) * 0.22f),
                    new Vector3(0.22f, 0.4f, 0.22f), shell);
            }
            using (var fields = new SerializedObject(root.GetComponent<AfterglowPod>()))
            {
                fields.FindProperty("creatureName").stringValue = "Afterglow Pod";
                fields.FindProperty("luminosity").floatValue = 0.5f;
                fields.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(root, PodPath);
            AssetDatabase.SaveAssets();
        }
        finally { Object.DestroyImmediate(root); }
    }
}
