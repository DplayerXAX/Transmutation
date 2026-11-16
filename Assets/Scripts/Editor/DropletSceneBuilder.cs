using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds Scene_Droplets, a small open test field for the Droplet Choir: flat ground, a few stone
/// pillars, the player, and two choirs (one writing test words, one drawing figures).
/// Plain static methods, no menu: run from the command line, e.g.
/// Unity.exe -batchmode -projectPath . -executeMethod DropletSceneBuilder.Build -quit
/// BuildAndTest also enters Play Mode with -dropletTest for DropletTestDriver.
/// </summary>
public static class DropletSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Scene_Droplets.unity";
    /// <summary>The earlier ink look (WaterDroplet shader), kept as an alternative material.</summary>
    public const string MaterialPath = "Assets/Materials/Creatures/Droplets/MAT_GlyphDroplet.mat";
    /// <summary>In Resources so RegionTitles can load it at runtime without any scene reference.</summary>
    public const string GoldPath = "Assets/Resources/GlyphCreatures/MAT_GlyphGold.mat";
    private const string OldGoldPath = "Assets/Materials/Creatures/Droplets/MAT_GlyphGold.mat";
    private const string PlayerPath = "Assets/Prefabs/Player/PlayerObjects.prefab";

    public static void Build()
    {
        Material droplet = DropletMaterial();
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        var surface = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Surface.mat");
        var inner = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Inner.mat");
        if (playerPrefab == null) throw new System.InvalidOperationException("Missing " + PlayerPath);

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(12f, 1f, 12f);
        if (surface != null) ground.GetComponent<Renderer>().sharedMaterial = surface;

        var stones = new GameObject("Stones").transform;
        var random = new System.Random(7);
        for (int i = 0; i < 9; i++)
        {
            float a = i / 9f * Mathf.PI * 2f + 0.3f;
            float r = 20f + (float)random.NextDouble() * 12f;
            var stone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stone.name = "Stone " + i;
            stone.transform.SetParent(stones, false);
            float h = 3f + (float)random.NextDouble() * 7f;
            stone.transform.position = new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r + 10f);
            stone.transform.localScale = new Vector3(1.2f + (float)random.NextDouble() * 1.5f, h, 1.2f + (float)random.NextDouble() * 1.5f);
            if (inner != null) stone.GetComponent<Renderer>().sharedMaterial = inner;
        }

        var choirs = new GameObject("Droplet Choirs").transform;
        Choir(choirs, "Droplet Choir - Words", new Vector3(0f, 0f, 9f), droplet,
            new[] { "WATER", "HELLO", "DRIFT", "RAIN 42?", "SKY\nWELL" }, 150, 1.6f);
        Choir(choirs, "Droplet Choir - Figures", new Vector3(-14f, 0f, 20f), droplet,
            new[] { "@spiral", "@eye", "@star", "@wave", "@circle" }, 110, 1.4f);

        var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
        // PlayerObjects must stay a scene root: carry collision filtering uses transform.root.
        player.name = "PlayerObjects";
        player.transform.SetPositionAndRotation(new Vector3(0f, 1.2f, 0f), Quaternion.identity);
        var hud = new GameObject("Interaction Hints").AddComponent<InteractionPromptHUD>();
        var hudFields = new SerializedObject(hud);
        hudFields.FindProperty("showText").boolValue = true;
        hudFields.FindProperty("hintsVisible").boolValue = true;
        hudFields.FindProperty("carrier").objectReferenceValue = player.GetComponentInChildren<PlayerCreatureCarrier>(true);
        hudFields.FindProperty("interactor").objectReferenceValue = player.GetComponentInChildren<PlayerCreatureInteractor>(true);
        hudFields.ApplyModifiedPropertiesWithoutUndo();

        foreach (GameObject existing in scene.GetRootGameObjects())
        {
            if (existing.name == "Main Camera") Object.DestroyImmediate(existing);
            else if (existing.TryGetComponent<Light>(out var light) && light.type == LightType.Directional)
            {
                existing.name = "Sun";
                light.transform.rotation = Quaternion.Euler(38f, -140f, 0f);
                light.intensity = 1.1f;
                light.color = new Color(1f, 0.94f, 0.86f);
                light.shadows = LightShadows.Soft;
            }
        }
        var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Sky_QuietLines.mat");
        if (sky != null) RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.47f, 0.52f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.6f, 0.68f, 0.7f);
        RenderSettings.fogStartDistance = 40f;
        RenderSettings.fogEndDistance = 180f;

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("Droplet test scene saved to " + ScenePath);
    }

    /// <summary>Builds the scene, then plays it with the automated check (needs -dropletTest).</summary>
    public static void BuildAndTest()
    {
        Build();
        EditorApplication.update += () =>
        {
            // Safety net: never leave a batch editor running.
            if (EditorApplication.timeSinceStartup > 1200.0) EditorApplication.Exit(3);
        };
        EditorApplication.EnterPlaymode();
    }

    private static void Choir(Transform parent, string name, Vector3 position, Material droplet, string[] glyphs, int count, float height)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        var choir = go.AddComponent<DropletChoir>();
        var fields = new SerializedObject(choir);
        fields.FindProperty("dropletMaterial").objectReferenceValue = droplet;
        fields.FindProperty("dropletCount").intValue = count;
        fields.FindProperty("glyphHeight").floatValue = height;
        SerializedProperty list = fields.FindProperty("glyphs");
        list.arraySize = glyphs.Length;
        for (int i = 0; i < glyphs.Length; i++) list.GetArrayElementAtIndex(i).stringValue = glyphs[i];
        fields.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>The glyph creatures' material: liquid gold glass (MAT_GlyphGold, made or refreshed here).</summary>
    private static Material DropletMaterial()
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(GoldPath) == null && AssetDatabase.LoadAssetAtPath<Material>(OldGoldPath) != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(GoldPath));
            AssetDatabase.Refresh();
            AssetDatabase.MoveAsset(OldGoldPath, GoldPath); // keeps its GUID, so scene references survive
        }
        var material = AssetDatabase.LoadAssetAtPath<Material>(GoldPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Capstone/Creature/GoldenGlyph");
            if (shader == null) throw new System.InvalidOperationException("GoldenGlyph shader not found.");
            Directory.CreateDirectory(Path.GetDirectoryName(GoldPath));
            AssetDatabase.Refresh();
            material = new Material(shader) { name = "MAT_GlyphGold", enableInstancing = true };
            AssetDatabase.CreateAsset(material, GoldPath);
        }
        material.SetColor("_GoldColor", new Color(2.2f, 1.15f, 0.22f));
        material.SetColor("_AmberColor", new Color(1f, 0.55f, 0.12f));
        material.SetColor("_LightColor", new Color(1.25f, 0.62f, 0.12f));
        material.SetFloat("_Refraction", 0.06f);
        material.SetFloat("_BodyDensity", 0.7f);
        material.SetFloat("_RimPower", 2.8f);
        material.SetFloat("_Threads", 1.2f);
        material.SetFloat("_ThreadScale", 4.5f);
        material.SetFloat("_Glitter", 1f);
        material.SetFloat("_Specular", 6f);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    /// <summary>The shared creature material (made on first use), for other builders.</summary>
    public static Material CreatureMaterial() => DropletMaterial();
}
