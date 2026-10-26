using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds Scene_MainMenu: a plain UI menu (title and three buttons: Map, Test Level, Museum) over a
/// calm gray background, and puts the menu first in Build Settings, followed by the game scenes.
/// </summary>
public static class MainMenuSetup
{
    private const string ScenePath = "Assets/Scenes/Scene_MainMenu.unity";
    private const string MuseumScene = "Scene_Museum";

    // Scenes added to Build Settings right after the menu (if they exist).
    private static readonly string[] GameScenes =
    {
        "Assets/Scenes/Scene_Daniel.unity",
        "Assets/Scenes/Scene_Basin.unity",
        "Assets/Scenes/" + MuseumScene + ".unity",
    };

    private static readonly Color Background = new Color(0.86f, 0.86f, 0.85f);
    private static readonly Color Ink = new Color(0.08f, 0.08f, 0.09f);

    [MenuItem("Tools/Capstone/Main Menu/Create Main Menu Scene")]
    private static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("Main Menu", "Scene_MainMenu already exists. Replace it?", "Replace", "Cancel"))
            return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Build();
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

    private static void Build()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Camera only clears to a flat colour; there is nothing 3D in the scene.
        var camGo = new GameObject("Menu Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Background;
        camGo.AddComponent<AudioListener>();

        var eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();

        var canvasGo = new GameObject("Menu Canvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        Transform root = canvasGo.transform;

        Text title = MakeText(root, "Title", font, 96, new Vector2(0f, 260f), new Vector2(1400f, 140f));
        title.text = "TRANSMUTATION";
        title.color = Ink;

        var entries = new[]
        {
            MakeEntry(root, font, "MAP", "Scene_Daniel", "", new Color(1f, 0.32f, 0.12f), 60f),
            MakeEntry(root, font, "TEST LEVEL", "Scene_Basin", "", new Color(0.1f, 0.7f, 1f), -50f),
            MakeEntry(root, font, "MUSEUM", MuseumScene, "The museum is not open yet.", new Color(0.75f, 0.2f, 1f), -160f),
        };

        // Wrap keyboard / gamepad navigation from the last button back to the first.
        for (int i = 0; i < entries.Length; i++)
        {
            var nav = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = entries[(i + entries.Length - 1) % entries.Length].button,
                selectOnDown = entries[(i + 1) % entries.Length].button,
            };
            entries[i].button.navigation = nav;
        }

        Text message = MakeText(root, "Message", font, 32, new Vector2(0f, -300f), new Vector2(1400f, 60f));
        message.color = Ink;

        Text hint = MakeText(root, "Hint", font, 24, new Vector2(0f, -470f), new Vector2(1400f, 50f));
        hint.text = "mouse or arrow keys    enter to go    esc to quit";
        hint.color = new Color(Ink.r, Ink.g, Ink.b, 0.55f);

        var fadeGo = new GameObject("Fade", typeof(RectTransform), typeof(Image));
        fadeGo.transform.SetParent(root, false);
        var fadeRect = (RectTransform)fadeGo.transform;
        fadeRect.anchorMin = Vector2.zero;
        fadeRect.anchorMax = Vector2.one;
        fadeRect.offsetMin = fadeRect.offsetMax = Vector2.zero;
        var fade = fadeGo.GetComponent<Image>();
        fade.color = Color.black;
        fade.raycastTarget = false;

        var controller = canvasGo.AddComponent<MainMenuController>();
        controller.entries = entries;
        controller.fade = fade;
        controller.message = message;
    }

    // A flat dark button with a white label; the accent colour shows on hover / selection.
    private static MainMenuController.Entry MakeEntry(Transform parent, Font font, string label, string scene,
        string unavailable, Color accent, float y)
    {
        var go = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(420f, 84f);

        var image = go.GetComponent<Image>();
        image.color = Color.white;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Ink;
        colors.highlightedColor = accent;
        colors.selectedColor = accent;
        colors.pressedColor = accent * 0.7f;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        Text text = MakeText(go.transform, "Label", font, 38, Vector2.zero, rect.sizeDelta);
        text.text = label;
        text.color = Color.white;

        var entry = new MainMenuController.Entry { button = button, sceneName = scene };
        if (!string.IsNullOrEmpty(unavailable)) entry.unavailableMessage = unavailable;
        return entry;
    }

    private static Text MakeText(Transform parent, string name, Font font, int size, Vector2 position, Vector2 boxSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchoredPosition = position;
        rect.sizeDelta = boxSize;
        var text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        return text;
    }
}
