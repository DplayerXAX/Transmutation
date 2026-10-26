using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Editor-only authoring for the Ore Colossus: its materials, the Geode Larva prefab, and adding a colossus
/// to the open scene. The colossus builds its own body in Play Mode.
/// </summary>
public static class OreColossusSetup
{
    public const string Folder = "Assets/Prefabs/Creatures/OreColossus";
    public const string LarvaPath = Folder + "/GeodeLarva.prefab";
    private const string Materials = "Assets/Materials/Creatures/OreColossus";

    [MenuItem("Tools/Creatures/Ore Colossus/Create Assets")]
    public static void CreateAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before authoring assets.");
        EnsureFolder(Folder); EnsureFolder(Materials);
        Material("Rock", new Color(0.32f, 0.29f, 0.27f), Color.black, 0.12f);
        Material("Ore", new Color(0.25f, 0.7f, 0.65f), new Color(0.15f, 0.8f, 0.65f), 0.6f);
        Material("Heart", new Color(0.6f, 0.95f, 1f), new Color(0.6f, 2.2f, 2.4f), 0.75f);
        Material("Geode", new Color(0.62f, 0.45f, 0.85f), new Color(0.45f, 0.28f, 1f), 0.7f);
        Material larvaGlow = Material("Larva", new Color(0.55f, 0.95f, 0.85f), new Color(0.4f, 1.6f, 1.2f), 0.55f);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(LarvaPath) == null) BuildLarva(larvaGlow);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Creatures/Ore Colossus/Add Ore Colossus To Open Scene")]
    public static void AddToScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before editing the scene.");
        CreateAssets();
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        var go = new GameObject("Creature - Ore Colossus");
        Undo.RegisterCreatedObjectUndo(go, "Add Ore Colossus");
        go.transform.position = world != null ? world.transform.position : PlacementPoint();
        go.AddComponent<Rigidbody>().isKinematic = true;
        var colossus = go.AddComponent<OreColossus>();
        Set(colossus, fields =>
        {
            fields.FindProperty("world").objectReferenceValue = world;
            fields.FindProperty("placeInWorld").boolValue = world != null;
            fields.FindProperty("seed").intValue = UnityEngine.Random.Range(1, 100000);
            fields.FindProperty("rockMaterial").objectReferenceValue = Load("Rock");
            fields.FindProperty("oreMaterial").objectReferenceValue = Load("Ore");
            fields.FindProperty("heartMaterial").objectReferenceValue = Load("Heart");
            fields.FindProperty("geodeMaterial").objectReferenceValue = Load("Geode");
            fields.FindProperty("larvaPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GeodeLarva>(LarvaPath);
        });
        ConfigureCreature(colossus, 1000f, 100000f);
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        bool water = Object.FindFirstObjectByType<ErosiveFluidSystem>() != null;
        if (world == null || !water)
            EditorUtility.DisplayDialog("Ore Colossus",
                (world == null ? "No ProceduralWorld in this scene: the colossus will roam around where it was placed, on Ground (layer 7).\n\n" :
                    "It places itself on the ProceduralWorld when Play starts.\n\n") +
                (water ? "" : "No ErosiveFluidSystem in this scene, so nothing can erode its heart here."), "OK");
        Debug.Log("Ore Colossus added" + (world != null ? "; it settles on flat ground about 70 m from spawn in Play Mode." : ".") + " Save the scene to keep it.", go);
    }

    [MenuItem("Tools/Creatures/Ore Colossus/Shatter Heart (Play Mode)")]
    public static void ShatterHearts()
    {
        foreach (OreColossus colossus in Object.FindObjectsByType<OreColossus>(FindObjectsSortMode.None)) colossus.DebugShatter();
    }

    [MenuItem("Tools/Creatures/Ore Colossus/Shatter Heart (Play Mode)", true)]
    private static bool CanShatterHearts() => Application.isPlaying;

    private static void BuildLarva(Material glow)
    {
        var root = new GameObject("GeodeLarva");
        root.SetActive(false);
        try
        {
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = 0.6f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            root.AddComponent<SphereCollider>().radius = 0.3f;
            var larva = root.AddComponent<GeodeLarva>();
            Set(larva, fields => fields.FindProperty("glowMaterial").objectReferenceValue = glow);
            ConfigureCreature(larva, 50f, 100000f);
            root.SetActive(true);
            PrefabUtility.SaveAsPrefabAsset(root, LarvaPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    /// <summary>Under the Scene view's centre, on Ground if there is any.</summary>
    private static Vector3 PlacementPoint()
    {
        Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        return Physics.Raycast(pivot + Vector3.up * 200f, Vector3.down, out RaycastHit hit, 600f, 1 << 7, QueryTriggerInteraction.Ignore) ? hit.point : pivot;
    }

    private static void ConfigureCreature(Creature creature, float health, float age)
    {
        Set(creature, fields =>
        {
            fields.FindProperty("maxHealth").floatValue = health;
            fields.FindProperty("initialPolarity").enumValueIndex = (int)CreaturePolarity.None;
            fields.FindProperty("age").floatValue = age;
            fields.FindProperty("ageRate").floatValue = 0f;
        });
    }

    private static Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>(Materials + "/MAT_OreColossus_" + name + ".mat");

    private static Material Material(string name, Color color, Color emission, float smoothness)
    {
        string path = Materials + "/MAT_OreColossus_" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "MAT_OreColossus_" + name, enableInstancing = true };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        if (emission.maxColorComponent > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void Set(Object target, Action<SerializedObject> configure)
    { var fields = new SerializedObject(target); configure(fields); fields.ApplyModifiedPropertiesWithoutUndo(); }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int split = path.LastIndexOf('/'); EnsureFolder(path.Substring(0, split));
        AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
    }
}
