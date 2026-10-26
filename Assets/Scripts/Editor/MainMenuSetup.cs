using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds Scene_MainMenu: a line-world plinth floating in fog with three 3D buttons (Map, Test Level,
/// Museum), drifting stone shards, accent motes, bloom, and a fade canvas. Also puts the menu first in
/// Build Settings, followed by the game scenes.
/// </summary>
public static class MainMenuSetup
{
    private const string ScenePath = "Assets/Scenes/Scene_MainMenu.unity";
    private const string MaterialFolder = "Assets/Materials/MainMenu";
    private const string MuseumScene = "Scene_Museum";

    // Scenes added to Build Settings right after the menu (if they exist).
    private static readonly string[] GameScenes =
    {
        "Assets/Scenes/Scene_Daniel.unity",
        "Assets/Scenes/Scene_Basin.unity",
        "Assets/Scenes/" + MuseumScene + ".unity",
    };

    private static readonly Color MapAccent = new Color(1f, 0.32f, 0.12f);
    private static readonly Color TestAccent = new Color(0.1f, 0.8f, 1f);
    private static readonly Color MuseumAccent = new Color(0.85f, 0.2f, 1f);

    [MenuItem("Tools/Capstone/Main Menu/Create Main Menu Scene")]
    private static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("Main Menu", "Scene_MainMenu already exists. Replace it?", "Replace", "Cancel"))
            return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        if (!Build()) return;
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings();
        Debug.Log("Scene_MainMenu created and saved, and put first in Build Settings. Press Play to try it.");
    }

    [MenuItem("Tools/Capstone/Main Menu/Add Menu And Game Scenes To Build Settings")]
    private static void AddToBuildSettings()
    {
        var list = new List<EditorBuildSettingsScene>();
        void Add(string path)
        {
            if (File.Exists(path) && list.All(s => s.path != path)) list.Add(new EditorBuildSettingsScene(path, true));
        }

        Add(ScenePath);
        foreach (string path in GameScenes) Add(path);
        // Keep whatever else was already there, after ours.
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            if (list.All(x => x.path != s.path)) list.Add(s);

        EditorBuildSettings.scenes = list.ToArray();
        Debug.Log("Build Settings: " + string.Join(", ", list.Select(s => Path.GetFileNameWithoutExtension(s.path))));
    }

    private static bool Build()
    {
        Material stone = LoadFirst("Assets/Materials/Landmarks/MAT_Landmark_SpireStone.mat", "Assets/Materials/World/MAT_Decor_Stone.mat");
        Material ground = LoadFirst("Assets/Materials/World/MAT_World_Surface.mat");
        Material sky = LoadFirst("Assets/Materials/World/MAT_Sky_QuietLines.mat", "Assets/Materials/World/MAT_Sky_FractalCalm.mat");
        if (stone == null || ground == null)
        {
            EditorUtility.DisplayDialog("Main Menu", "Missing world materials (MAT_Landmark_SpireStone / MAT_World_Surface).", "OK");
            return false;
        }
        Material mapMat = AccentMaterial("MAT_Menu_Accent_Map", MapAccent);
        Material testMat = AccentMaterial("MAT_Menu_Accent_Test", TestAccent);
        Material museumMat = AccentMaterial("MAT_Menu_Accent_Museum", MuseumAccent);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Sky, fog, ambient.
        if (sky != null) RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.44f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.035f;
        RenderSettings.fogColor = new Color(0.62f, 0.62f, 0.64f);

        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

        // Camera with post processing.
        var camGo = new GameObject("Menu Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 50f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.farClipPlane = 300f;
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        camGo.transform.position = new Vector3(0f, 2.7f, -9.5f);
        camGo.transform.LookAt(new Vector3(0f, 1.6f, 0f));

        var volumeGo = new GameObject("Menu Volume");
        var volume = volumeGo.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = VolumeProfileAsset();

        // Ground: a wide disc with the line-world terrain shader, and a raised plinth under the buttons.
        var world = new GameObject("Menu World").transform;
        Prim(PrimitiveType.Cylinder, "Ground", world, new Vector3(0f, -0.05f, 4f), new Vector3(60f, 0.05f, 60f), ground);
        Prim(PrimitiveType.Cylinder, "Plinth", world, new Vector3(0f, 0.1f, 0f), new Vector3(11f, 0.1f, 5f), stone);

        // Buttons.
        var menu = new GameObject("Main Menu").transform;
        var buttons = new[]
        {
            MakeButton(menu, world, "MAP", "Scene_Daniel", "", MapAccent, mapMat, stone, font, -3.6f, 0),
            MakeButton(menu, world, "TEST LEVEL", "Scene_Basin", "", TestAccent, testMat, stone, font, 0f, 1),
            MakeButton(menu, world, "MUSEUM", MuseumScene, "The museum is not open yet.", MuseumAccent, museumMat, stone, font, 3.6f, 2),
        };

        // Title.
        var title = Label(menu, "Title", "TRANSMUTATION", font, new Vector3(0f, 4.3f, 1.5f), 0.09f);
        title.color = new Color(0.08f, 0.08f, 0.09f);

        Decorate(world, stone, new[] { mapMat, testMat, museumMat });

        // UI: fade, message line, hint.
        var canvasGo = new GameObject("Menu Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        Text hint = UiText(canvasGo.transform, "Hint", font, 26, new Vector2(0f, 40f));
        hint.text = "mouse or arrow keys    enter to go    esc to quit";
        hint.color = new Color(0.15f, 0.15f, 0.16f, 0.8f);
        Text message = UiText(canvasGo.transform, "Message", font, 34, new Vector2(0f, 110f));
        message.color = new Color(0.1f, 0.1f, 0.1f, 1f);

        var fadeGo = new GameObject("Fade", typeof(RectTransform), typeof(Image));
        fadeGo.transform.SetParent(canvasGo.transform, false);
        var fadeRect = (RectTransform)fadeGo.transform;
        fadeRect.anchorMin = Vector2.zero;
        fadeRect.anchorMax = Vector2.one;
        fadeRect.offsetMin = fadeRect.offsetMax = Vector2.zero;
        var fade = fadeGo.GetComponent<Image>();
        fade.color = Color.black;
        fade.raycastTarget = false;

        var controller = menu.gameObject.AddComponent<MainMenuController>();
        controller.menuCamera = cam;
        controller.buttons = buttons;
        controller.fade = fade;
        controller.message = message;
        return true;
    }

    private static MainMenuButton MakeButton(Transform menu, Transform world, string label, string scene, string unavailable,
        Color accent, Material accentMat, Material stone, Font font, float x, int kind)
    {
        // Pedestal stays still; the button floats above it.
        Prim(PrimitiveType.Cube, label + " Pedestal", world, new Vector3(x, 0.6f, 0f), new Vector3(1.1f, 1f, 1.1f), stone);

        var root = new GameObject(label + " Button").transform;
        root.SetParent(menu, false);
        root.localPosition = new Vector3(x, 2.1f, 0f);
        var shape = new GameObject("Shape").transform;
        shape.SetParent(root, false);

        var glow = new List<Renderer>();
        switch (kind)
        {
            case 0: // Map: a stone globe with an orbiting ring of accent stones.
                Prim(PrimitiveType.Sphere, "Globe", shape, Vector3.zero, Vector3.one * 0.8f, stone);
                for (int i = 0; i < 10; i++)
                {
                    float a = i * Mathf.PI * 2f / 10f;
                    var p = new Vector3(Mathf.Cos(a), Mathf.Sin(a * 2f) * 0.08f, Mathf.Sin(a)) * 0.75f;
                    glow.Add(Prim(PrimitiveType.Cube, "Orbit", shape, p, Vector3.one * 0.11f, accentMat, Quaternion.Euler(45f, a * Mathf.Rad2Deg, 45f)).GetComponent<Renderer>());
                }
                break;
            case 1: // Test level: two towers with an accent bridge and a seed between them.
                Prim(PrimitiveType.Cube, "Tower A", shape, new Vector3(-0.32f, -0.15f, 0f), new Vector3(0.2f, 1.1f, 0.2f), stone);
                Prim(PrimitiveType.Cube, "Tower B", shape, new Vector3(0.32f, 0.05f, 0f), new Vector3(0.2f, 1.5f, 0.2f), stone);
                glow.Add(Prim(PrimitiveType.Cube, "Bridge", shape, new Vector3(0f, 0.42f, 0f), new Vector3(0.64f, 0.05f, 0.05f), accentMat, Quaternion.Euler(0f, 0f, 12f)).GetComponent<Renderer>());
                glow.Add(Prim(PrimitiveType.Sphere, "Seed", shape, new Vector3(0f, 0.05f, 0f), Vector3.one * 0.2f, accentMat).GetComponent<Renderer>());
                break;
            default: // Museum: an empty frame holding an accent crystal.
                const float s = 0.55f, t = 0.08f;
                Prim(PrimitiveType.Cube, "Frame Top", shape, new Vector3(0f, s, 0f), new Vector3(s * 2f + t, t, t), stone);
                Prim(PrimitiveType.Cube, "Frame Bottom", shape, new Vector3(0f, -s, 0f), new Vector3(s * 2f + t, t, t), stone);
                Prim(PrimitiveType.Cube, "Frame Left", shape, new Vector3(-s, 0f, 0f), new Vector3(t, s * 2f, t), stone);
                Prim(PrimitiveType.Cube, "Frame Right", shape, new Vector3(s, 0f, 0f), new Vector3(t, s * 2f, t), stone);
                glow.Add(Prim(PrimitiveType.Cube, "Crystal", shape, Vector3.zero, new Vector3(0.32f, 0.32f, 0.32f), accentMat, Quaternion.Euler(45f, 0f, 35f)).GetComponent<Renderer>());
                break;
        }

        TextMesh text = Label(root, "Label", label, font, new Vector3(0f, -1.12f, -0.62f), 0.035f);

        // One collider round the whole button for mouse picking.
        var box = root.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, -0.3f, 0f);
        box.size = new Vector3(1.8f, 2.4f, 1.4f);

        var button = root.gameObject.AddComponent<MainMenuButton>();
        button.label = label;
        button.sceneName = scene;
        if (!string.IsNullOrEmpty(unavailable)) button.unavailableMessage = unavailable;
        button.accent = accent;
        button.shape = shape;
        button.glow = glow.ToArray();
        button.text = text;
        return button;
    }

    // Floating stone shards in a loose ring, plus small accent motes near the plinth.
    private static void Decorate(Transform world, Material stone, Material[] accents)
    {
        var random = new System.Random(7);
        float R(float a, float b) => a + (float)random.NextDouble() * (b - a);

        var shards = new GameObject("Shards").transform;
        shards.SetParent(world, false);
        for (int i = 0; i < 28; i++)
        {
            float a = R(0f, Mathf.PI * 2f);
            float d = R(8f, 22f);
            var p = new Vector3(Mathf.Cos(a) * d, R(0.6f, 8f), Mathf.Sin(a) * d + 6f);
            var scale = new Vector3(R(0.3f, 1.2f), R(1f, 4.5f), R(0.3f, 1.2f));
            var go = Prim(PrimitiveType.Cube, "Shard", shards, p, scale, stone, Quaternion.Euler(R(-25f, 25f), R(0f, 360f), R(-25f, 25f)));
            var f = go.AddComponent<MainMenuFloat>();
            f.bobHeight = R(0.1f, 0.5f);
            f.turnSpeed = R(1f, 5f);
        }

        var motes = new GameObject("Motes").transform;
        motes.SetParent(world, false);
        for (int i = 0; i < 18; i++)
        {
            var p = new Vector3(R(-7f, 7f), R(0.8f, 5f), R(-1.5f, 6f));
            var go = Prim(PrimitiveType.Cube, "Mote", motes, p, Vector3.one * R(0.05f, 0.14f), accents[i % accents.Length], Quaternion.Euler(45f, 0f, 45f));
            var f = go.AddComponent<MainMenuFloat>();
            f.bobHeight = R(0.2f, 0.6f);
            f.bobSpeed = R(0.3f, 0.8f);
            f.turnSpeed = R(20f, 60f);
        }
    }

    private static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material, Quaternion? rotation = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = rotation ?? Quaternion.identity;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    private static TextMesh Label(Transform parent, string name, string text, Font font, Vector3 position, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        var mesh = go.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.font = font;
        mesh.fontSize = 100;
        mesh.characterSize = size;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = new Color(0.85f, 0.85f, 0.85f);
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        return mesh;
    }

    private static Text UiText(Transform parent, string name, Font font, int size, Vector2 bottomOffset)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = bottomOffset;
        rect.sizeDelta = new Vector2(1600f, 60f);
        var text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        return text;
    }

    private static Material LoadFirst(params string[] paths)
    {
        foreach (string path in paths)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
        }
        return null;
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "MainMenu");
    }

    private static Material AccentMaterial(string name, Color color)
    {
        EnsureFolder();
        string path = MaterialFolder + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", color * 1.5f);
        EditorUtility.SetDirty(m);
        return m;
    }

    private static VolumeProfile VolumeProfileAsset()
    {
        EnsureFolder();
        string path = MaterialFolder + "/MainMenuVolume.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile != null) return profile;

        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);
        var bloom = profile.Add<Bloom>(true);
        bloom.threshold.Override(1f);
        bloom.intensity.Override(1.3f);
        bloom.scatter.Override(0.65f);
        AssetDatabase.AddObjectToAsset(bloom, profile);
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.3f);
        AssetDatabase.AddObjectToAsset(vignette, profile);
        AssetDatabase.SaveAssets();
        return profile;
    }
}
