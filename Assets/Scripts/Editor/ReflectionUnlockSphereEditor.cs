using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ReflectionSpace))]
public sealed class ReflectionSpaceSphereEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var space = (ReflectionSpace)target;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Creature unlock sphere", EditorStyles.boldLabel);
        if (GUILayout.Button("Open sphere preview (does not change saves)"))
            ReflectionSpherePreviewWindow.Open(space.SphereSettings);
        if (space.UnlockSphere != null) ReflectionUnlockSphereEditor.DrawPreview(space.UnlockSphere);
    }
}

[CustomEditor(typeof(ReflectionUnlockSphere))]
public sealed class ReflectionUnlockSphereEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var sphere = (ReflectionUnlockSphere)target;
        EditorGUILayout.LabelField($"{sphere.UnlockedCreatureCount} / {sphere.CreatureCount} families · {sphere.FragmentCount} fragments");
        EditorGUILayout.HelpBox("Tune Unlock Sphere on the ReflectionSpace component. Preview buttons only change this view.", MessageType.Info);
        DrawPreview(sphere);
    }

    internal static void DrawPreview(ReflectionUnlockSphere sphere)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Shattered")) sphere.PreviewFraction(0f);
            if (GUILayout.Button("Half")) sphere.PreviewFraction(0.5f);
            if (GUILayout.Button("Whole")) sphere.PreviewFraction(1f);
        }
        if (GUILayout.Button("Animate next family")) sphere.PreviewNextCreature();
        if (GUILayout.Button("Animate all remaining")) sphere.PreviewFraction(1f, true);
        if (GUILayout.Button("Show saved progress")) sphere.PreviewSavedProgress();
    }
}

/// <summary>Isolated editor preview: no scene serialization and no player save APIs.</summary>
public sealed class ReflectionSpherePreviewWindow : EditorWindow
{
    private PreviewRenderUtility renderer;
    private ReflectionUnlockSphere sphere;
    private ReflectionUnlockSphereSettings settings;
    private float fraction;
    private Vector2 orbit = new(20f, -22f);
    private bool rebuild;

    public static void Open(ReflectionUnlockSphereSettings source)
    {
        var window = GetWindow<ReflectionSpherePreviewWindow>("Unlock Sphere Preview");
        window.settings = JsonUtility.FromJson<ReflectionUnlockSphereSettings>(JsonUtility.ToJson(source));
        window.settings.position = Vector3.zero;
        window.rebuild = true;
        window.Show();
    }

    private void OnDisable() => Cleanup();
    private void Cleanup()
    {
        if (sphere != null) DestroyImmediate(sphere.gameObject);
        sphere = null; renderer?.Cleanup(); renderer = null;
    }

    private void Build()
    {
        Cleanup();
        settings ??= new ReflectionUnlockSphereSettings(); settings.position = Vector3.zero;
        renderer = new PreviewRenderUtility();
        renderer.camera.clearFlags = CameraClearFlags.SolidColor;
        renderer.camera.backgroundColor = new Color(0.84f, 0.82f, 0.77f);
        renderer.camera.nearClipPlane = 0.1f; renderer.camera.farClipPlane = 100f;
        var go = new GameObject("Sphere editor preview") { hideFlags = HideFlags.HideAndDontSave };
        renderer.AddSingleGO(go);
        sphere = go.AddComponent<ReflectionUnlockSphere>();
        sphere.Initialize(ReflectionCatalog.Load(), new ReflectionProgress(), settings, null);
        sphere.PreviewFraction(fraction);
        rebuild = false;
    }

    private void OnGUI()
    {
        if (settings == null) settings = new ReflectionUnlockSphereSettings();
        if (rebuild || renderer == null) Build();
        EditorGUILayout.LabelField("Static preview · drag to orbit · player progress untouched", EditorStyles.miniLabel);
        EditorGUI.BeginChangeCheck();
        fraction = EditorGUILayout.Slider("Restored", fraction, 0f, 1f);
        if (EditorGUI.EndChangeCheck()) sphere.PreviewFraction(fraction);
        EditorGUI.BeginChangeCheck();
        settings.fragmentMode = (ReflectionFragmentMode)EditorGUILayout.EnumPopup("Fragments", settings.fragmentMode);
        settings.fragmentCount = EditorGUILayout.IntSlider("Dense count", settings.fragmentCount, 1, 512);
        settings.fractureSeed = EditorGUILayout.IntField("Fracture seed", settings.fractureSeed);
        if (EditorGUI.EndChangeCheck()) rebuild = true;
        Rect rect = GUILayoutUtility.GetRect(100, 10000, 100, 10000);
        if (Event.current.type == EventType.MouseDrag && rect.Contains(Event.current.mousePosition))
        { orbit += Event.current.delta * 0.4f; Event.current.Use(); Repaint(); }
        if (Event.current.type != EventType.Repaint) return;
        renderer.BeginPreview(rect, GUIStyle.none);
        float distance = Mathf.Max(0.1f, settings.radius) * 4f + settings.scatterDistance * 2.5f;
        renderer.camera.transform.position = Quaternion.Euler(-orbit.y, orbit.x, 0) * new Vector3(0, 0, -distance);
        renderer.camera.transform.LookAt(Vector3.zero);
        renderer.camera.fieldOfView = 38f;
        renderer.Render(true);
        GUI.DrawTexture(rect, renderer.EndPreview(), ScaleMode.StretchToFill, false);
    }
}
