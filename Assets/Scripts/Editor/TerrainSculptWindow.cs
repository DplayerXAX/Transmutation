using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Capstone > Terrain > Sculpt And Place Window.
///   Sculpt:   brushes on the saved terrain in the Scene view (Raise, Lower, Smooth, Flatten, Add, Carve).
///   Place:    click to drop prefabs (decor or creatures) onto the ground; Shift + click removes placed ones.
///   Spawners: switch the runtime spawners (procedural decor, flora, creatures) on or off.
/// Edits are stored in the TerrainSculptData asset on the ProceduralWorld and can be undone.
/// </summary>
public sealed class TerrainSculptWindow : EditorWindow
{
    private enum Tab { Sculpt, Place, Spawners }

    public const string PlacedRootName = "Placed World Objects";
    private const string PalettePrefsKey = "Capstone.TerrainSculpt.Palette";
    private const string AutoPreviewPrefsKey = "Capstone.TerrainSculpt.AutoPreview";

    // Runtime spawners that can be switched off when the same things are placed by hand.
    private static readonly (string type, string label)[] Spawners =
    {
        ("WorldDecorator", "Procedural decor on every chunk (stones, polyps, inner growths)"),
        ("CaveHarvest", "Cave fruit and flowers in the inner world"),
        ("RealmFlora", "Groves round the Hanging Spire"),
        ("RealmDressing", "Stream, lamps and slime between the spire and the kiln"),
        ("SpireOvergrowth", "Veins, mushrooms and web on the spire"),
        ("TowerGroves", "Kelp forest and geode field at the side towers"),
        ("SpireCreatures", "Goo, bubble flowers, spider and birds round the spire"),
        ("BasinLevel", "Basin level towers and creatures"),
    };

    private ProceduralWorld world;
    private Tab tab;
    private bool active = true;
    private Vector2 scroll;

    // Sculpt
    private TerrainSculptTool tool;
    private float radius = 6f;
    private float strength = 0.5f;
    private bool fixedFlattenHeight;
    private float flattenHeight;
    private bool keepPlacedOnGround = true;

    // Place
    private readonly List<GameObject> palette = new List<GameObject>();
    private int paletteIndex;
    private bool randomFromPalette;
    private int scatterCount = 1;
    private float scatterRadius = 4f;
    private Vector2 scaleRange = new Vector2(0.9f, 1.1f);
    private bool randomYaw = true;
    private float alignToSlope = 0.3f;
    private float sink = 0.05f;

    // Stroke state
    private bool stroking;
    private Ray lastRay;
    private double lastDab;
    private Bounds strokeArea;
    private readonly Dictionary<Transform, float> groundOffsets = new Dictionary<Transform, float>();

    [MenuItem("Tools/Capstone/Terrain/Sculpt And Place Window")]
    public static void Open() => GetWindow<TerrainSculptWindow>("Terrain Sculpt");

    private static bool AutoPreview
    {
        get => EditorPrefs.GetBool(AutoPreviewPrefsKey, true);
        set => EditorPrefs.SetBool(AutoPreviewPrefsKey, value);
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.update += WhileHeld;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        EditorSceneManager.sceneOpened += SceneOpened;
        LoadPalette();
        // After a script reload the preview chunks lose their data; rebuild them once the editor settles.
        EditorApplication.delayCall += AutoShowTerrain;
    }

    private void OnDisable()
    {
        if (stroking) EndStroke();
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.update -= WhileHeld;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        EditorSceneManager.sceneOpened -= SceneOpened;
    }

    private void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += AutoShowTerrain;
    }

    private void SceneOpened(Scene scene, OpenSceneMode mode)
    {
        world = null;
        EditorApplication.delayCall += AutoShowTerrain;
    }

    private void AutoShowTerrain()
    {
        if (this == null || Application.isPlaying || !AutoPreview) return;
        FindWorld();
        if (world != null && world.UsingSculptedTerrain && !world.LayoutReady) ShowTerrain(world);
    }

    private void FindWorld()
    {
        if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
    }

    public static void ShowTerrain(ProceduralWorld target)
    {
        try
        {
            EditorUtility.DisplayProgressBar("Terrain", "Building the terrain in the Scene view...", 0.5f);
            target.Generate(true);
        }
        finally { EditorUtility.ClearProgressBar(); }
        SceneView.RepaintAll();
    }

    // ---------------- Window ----------------

    private void OnGUI()
    {
        FindWorld();
        world = (ProceduralWorld)EditorGUILayout.ObjectField("World", world, typeof(ProceduralWorld), true);
        if (world == null)
        {
            EditorGUILayout.HelpBox("No ProceduralWorld in the open scene.", MessageType.Info);
            return;
        }
        if (Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Sculpting and placing work in Edit Mode only.", MessageType.Info);
            return;
        }

        tab = (Tab)GUILayout.Toolbar((int)tab, new[] { "Sculpt", "Place", "Spawners" });
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (tab != Tab.Spawners)
        {
            active = EditorGUILayout.Toggle(new GUIContent("Use In Scene View", "Left click in the Scene view uses this tool. Alt still orbits."), active);
            TerrainStatus();
        }
        if (tab == Tab.Sculpt) SculptGUI();
        else if (tab == Tab.Place) PlaceGUI();
        else SpawnersGUI();
        EditorGUILayout.EndScrollView();
    }

    private void TerrainStatus()
    {
        TerrainSculptData data = world.SculptData;
        if (data == null || !data.HasLayout)
        {
            EditorGUILayout.HelpBox("This world is still fully procedural. Bake it to a saved terrain first; the current layout is kept as the start.", MessageType.Warning);
            if (GUILayout.Button("Bake Current World To Sculpted Terrain")) TerrainSculptSetup.Bake(world);
            return;
        }
        EditorGUILayout.ObjectField("Saved Terrain", data, typeof(TerrainSculptData), false);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(world.LayoutReady ? "Rebuild Preview" : "Show Terrain In Scene")) ShowTerrain(world);
            if (GUILayout.Button("Save")) AssetDatabase.SaveAssetIfDirty(data);
        }
        AutoPreview = EditorGUILayout.Toggle(new GUIContent("Auto Show Terrain", "Rebuild the preview after opening the scene, leaving Play Mode or reloading scripts while this window is open."), AutoPreview);
        if (!world.LayoutReady) EditorGUILayout.HelpBox("The terrain is not built in the Scene view yet.", MessageType.Info);
    }

    private void SculptGUI()
    {
        if (!world.UsingSculptedTerrain) return;
        EditorGUILayout.Space();
        tool = (TerrainSculptTool)GUILayout.Toolbar((int)tool, System.Enum.GetNames(typeof(TerrainSculptTool)));
        EditorGUILayout.HelpBox(ToolHelp(tool), MessageType.None);
        radius = EditorGUILayout.Slider("Radius (m)", radius, 1f, 30f);
        strength = EditorGUILayout.Slider("Strength", strength, 0.02f, 1f);
        if (tool == TerrainSculptTool.Flatten)
        {
            fixedFlattenHeight = EditorGUILayout.Toggle(new GUIContent("Fixed Height", "Off: flatten to the height where the stroke started."), fixedFlattenHeight);
            if (fixedFlattenHeight) flattenHeight = EditorGUILayout.FloatField("Height (world y)", flattenHeight);
        }
        keepPlacedOnGround = EditorGUILayout.Toggle(new GUIContent("Keep Placed Objects On Ground",
            "Objects under '" + PlacedRootName + "' move up or down with the ground after each stroke."), keepPlacedOnGround);
        EditorGUILayout.LabelField("Sculpted samples", world.SculptedSampleCount.ToString("N0"));

        EditorGUILayout.Space();
        if (GUILayout.Button("Clear All Sculpting (Back To The Baked Layout)") &&
            EditorUtility.DisplayDialog("Terrain", "Remove every sculpt edit? This can be undone.", "Clear", "Cancel"))
        {
            Undo.RegisterCompleteObjectUndo(world.SculptData, "Clear Sculpting");
            world.SculptData.ClearEdits();
            EditorUtility.SetDirty(world.SculptData);
            world.SyncSculptFromData();
        }
    }

    private static string ToolHelp(TerrainSculptTool t)
    {
        switch (t)
        {
            case TerrainSculptTool.Raise: return "Lifts the ground under the brush (a vertical cylinder).";
            case TerrainSculptTool.Lower: return "Pushes the ground down under the brush.";
            case TerrainSculptTool.Smooth: return "Evens out bumps, ridges and sharp edges.";
            case TerrainSculptTool.Flatten: return "Pulls the ground to one height: where the stroke started, or a fixed height.";
            case TerrainSculptTool.Add: return "Adds rock in a ball where you click: ledges, overhangs, arches.";
            default: return "Removes rock in a ball: tunnels, caves, holes in walls.";
        }
    }

    private void PlaceGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Palette", EditorStyles.boldLabel);
        bool changed = false;
        for (int i = 0; i < palette.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool selected = GUILayout.Toggle(paletteIndex == i, GUIContent.none, EditorStyles.radioButton, GUILayout.Width(18f));
                if (selected) paletteIndex = i;
                var next = (GameObject)EditorGUILayout.ObjectField(palette[i], typeof(GameObject), false);
                if (next != palette[i]) { palette[i] = next; changed = true; }
                if (GUILayout.Button("-", GUILayout.Width(22f))) { palette.RemoveAt(i); changed = true; GUIUtility.ExitGUI(); }
            }
        }
        var added = (GameObject)EditorGUILayout.ObjectField("Add Prefab", null, typeof(GameObject), false);
        if (added != null) { palette.Add(added); paletteIndex = palette.Count - 1; changed = true; }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Decor Prefabs")) changed |= AddFolder(TerrainSculptSetup.DecorFolder);
            if (GUILayout.Button("Add Creature Prefabs")) changed |= AddFolder("Assets/Prefabs/Creatures");
            if (GUILayout.Button("Clear")) { palette.Clear(); changed = true; }
        }
        if (palette.Count == 0)
            EditorGUILayout.HelpBox("Add prefabs to place. Decor prefabs are made by Tools > Capstone > Terrain > Make Decor Prefabs.", MessageType.Info);
        if (changed) SavePalette();

        EditorGUILayout.Space();
        randomFromPalette = EditorGUILayout.Toggle("Random From Palette", randomFromPalette);
        scatterCount = EditorGUILayout.IntSlider("Per Click", scatterCount, 1, 20);
        if (scatterCount > 1) scatterRadius = EditorGUILayout.Slider("Scatter Radius (m)", scatterRadius, 0.5f, 20f);
        scaleRange = EditorGUILayout.Vector2Field("Scale Min / Max", scaleRange);
        randomYaw = EditorGUILayout.Toggle("Random Turn", randomYaw);
        alignToSlope = EditorGUILayout.Slider("Align To Slope", alignToSlope, 0f, 1f);
        sink = EditorGUILayout.FloatField(new GUIContent("Sink (m)", "Pushes the base into the ground."), sink);
        EditorGUILayout.HelpBox("Click: place.   Shift + click: remove placed objects under the brush.\n" +
            "Placed objects go under '" + PlacedRootName + "'. Ones with a Rigidbody get SettleWhenGroundReady so they wait for their ground at runtime.",
            MessageType.None);

        EditorGUILayout.Space();
        if (GUILayout.Button("Drop Selected Objects To The Ground")) DropSelection();
    }

    private void SpawnersGUI()
    {
        EditorGUILayout.HelpBox("Runtime spawners that place things by themselves. Switch one off when you place the same things by hand. " +
            "Off = the component is disabled in the scene (save the scene to keep it).", MessageType.None);
        bool any = false;
        foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (behaviour == null || behaviour.gameObject.scene != world.gameObject.scene) continue;
            string label = null;
            foreach (var spawner in Spawners) if (behaviour.GetType().Name == spawner.type) label = spawner.label;
            if (label == null) continue;
            any = true;
            using (new EditorGUILayout.HorizontalScope())
            {
                bool on = EditorGUILayout.ToggleLeft(behaviour.GetType().Name + " (" + behaviour.name + ")", behaviour.enabled, GUILayout.Width(260f));
                EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
                if (on != behaviour.enabled)
                {
                    Undo.RecordObject(behaviour, on ? "Spawner On" : "Spawner Off");
                    behaviour.enabled = on;
                    EditorUtility.SetDirty(behaviour);
                    EditorSceneManager.MarkSceneDirty(behaviour.gameObject.scene);
                }
            }
        }
        if (!any) EditorGUILayout.HelpBox("No runtime spawners in this scene.", MessageType.Info);
    }

    // ---------------- Scene view ----------------

    private void OnSceneGUI(SceneView view)
    {
        if (!active || tab == Tab.Spawners || world == null || Application.isPlaying || !world.LayoutReady) return;
        if (tab == Tab.Sculpt && !world.CanSculpt) return;
        Event e = Event.current;
        int control = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        bool hit = TerrainHit(ray, out RaycastHit ground);
        float brush = tab == Tab.Sculpt ? radius : scatterCount > 1 ? scatterRadius : 0.75f;
        if (hit)
        {
            Handles.color = tab == Tab.Place && e.shift ? new Color(1f, 0.4f, 0.3f) : new Color(0.4f, 0.9f, 1f);
            Handles.DrawWireDisc(ground.point, ground.normal, brush);
            Handles.DrawLine(ground.point, ground.point + ground.normal * Mathf.Min(brush, 3f));
            if (tab == Tab.Sculpt && tool == TerrainSculptTool.Flatten && (stroking || fixedFlattenHeight))
                Handles.DrawWireDisc(new Vector3(ground.point.x, flattenHeight, ground.point.z), Vector3.up, brush);
        }
        if (e.type == EventType.MouseMove) view.Repaint();
        // A release anywhere (even outside the view, or already used) ends the stroke.
        if (stroking && (e.rawType == EventType.MouseUp || e.type == EventType.MouseLeaveWindow))
        {
            if (GUIUtility.hotControl == control) GUIUtility.hotControl = 0;
            EndStroke();
            return;
        }
        if (e.alt || e.button != 0) return;

        if (e.type == EventType.MouseDown && hit && GUIUtility.hotControl == 0)
        {
            GUIUtility.hotControl = control;
            if (tab == Tab.Sculpt) BeginStroke(ray, ground);
            else if (e.shift) Erase(ground.point, brush);
            else Place(ground);
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == control)
        {
            if (stroking) { lastRay = ray; Dab(); }
            e.Use();
        }
        else if (e.type == EventType.MouseUp && GUIUtility.hotControl == control)
        {
            GUIUtility.hotControl = 0;
            e.Use();
        }
    }

    private bool TerrainHit(Ray ray, out RaycastHit best)
    {
        best = default;
        float nearest = float.MaxValue;
        foreach (RaycastHit h in Physics.RaycastAll(ray, 5000f, 1 << world.TerrainLayer, QueryTriggerInteraction.Ignore))
            if (h.distance < nearest && world.OwnsCollider(h.collider)) { nearest = h.distance; best = h; }
        return nearest < float.MaxValue;
    }

    /// <summary>The terrain below a point (the first ground under it, so the inner world works too).</summary>
    private bool GroundBelow(Vector3 point, float lift, out RaycastHit best)
    {
        best = default;
        float nearest = float.MaxValue;
        foreach (RaycastHit h in Physics.RaycastAll(point + Vector3.up * lift, Vector3.down, 400f, 1 << world.TerrainLayer, QueryTriggerInteraction.Ignore))
            if (h.distance < nearest && world.OwnsCollider(h.collider)) { nearest = h.distance; best = h; }
        return nearest < float.MaxValue;
    }

    // ---------------- Sculpting ----------------

    private void BeginStroke(Ray ray, RaycastHit ground)
    {
        TerrainSculptData data = world.SculptData;
        Undo.IncrementCurrentGroup();
        Undo.RegisterCompleteObjectUndo(data, "Sculpt Terrain");
        if (!fixedFlattenHeight) flattenHeight = ground.point.y;
        stroking = true;
        lastRay = ray;
        strokeArea = new Bounds(ground.point, Vector3.zero);
        groundOffsets.Clear();
        if (keepPlacedOnGround)
        {
            GameObject root = FindPlacedRoot(false);
            if (root != null)
                foreach (Transform child in root.transform)
                    if (GroundBelow(child.position, 2f, out RaycastHit below)) groundOffsets[child] = child.position.y - below.point.y;
        }
        world.BeginSculptStroke();
        lastDab = 0;
        Dab();
    }

    private void WhileHeld()
    {
        // Keep sculpting while the mouse button is held still.
        if (stroking && EditorApplication.timeSinceStartup - lastDab > 0.06) Dab();
    }

    private void Dab()
    {
        if (world == null || !world.CanSculpt) { if (stroking) EndStroke(); return; }
        if (!TerrainHit(lastRay, out RaycastHit ground)) return;
        double now = EditorApplication.timeSinceStartup;
        float dt = lastDab <= 0 ? 0.03f : Mathf.Clamp((float)(now - lastDab), 0.01f, 0.1f);
        lastDab = now;
        float amount = tool == TerrainSculptTool.Smooth || tool == TerrainSculptTool.Flatten
            ? 1f - Mathf.Exp(-strength * 10f * dt)
            : strength * 8f * dt;
        if (world.Sculpt(tool, ground.point, radius, amount, flattenHeight))
        {
            EditorUtility.SetDirty(world.SculptData);
            strokeArea.Encapsulate(new Bounds(ground.point, Vector3.one * radius * 2f));
        }
        SceneView.RepaintAll();
    }

    private void EndStroke()
    {
        stroking = false;
        if (world == null) return;
        world.EndSculptStroke();
        if (keepPlacedOnGround)
        {
            foreach (var pair in groundOffsets)
            {
                Transform placed = pair.Key;
                if (placed == null) continue;
                Vector3 p = placed.position;
                if (p.x < strokeArea.min.x || p.x > strokeArea.max.x || p.z < strokeArea.min.z || p.z > strokeArea.max.z) continue;
                if (!GroundBelow(p, 2f + radius, out RaycastHit below)) continue;
                Undo.RecordObject(placed, "Keep On Ground");
                placed.position = new Vector3(p.x, below.point.y + pair.Value, p.z);
            }
        }
        groundOffsets.Clear();
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        Repaint();
    }

    // ---------------- Placing ----------------

    private GameObject FindPlacedRoot(bool create)
    {
        Scene scene = world.gameObject.scene;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == PlacedRootName) return root;
        if (!create) return null;
        var made = new GameObject(PlacedRootName);
        SceneManager.MoveGameObjectToScene(made, scene);
        Undo.RegisterCreatedObjectUndo(made, "Create Placed Root");
        return made;
    }

    private void Place(RaycastHit centre)
    {
        if (palette.Count == 0) { ShowNotification(new GUIContent("Add a prefab to the palette first")); return; }
        Undo.IncrementCurrentGroup();
        Transform root = FindPlacedRoot(true).transform;
        var random = new System.Random(System.Environment.TickCount);
        for (int i = 0; i < scatterCount; i++)
        {
            GameObject prefab = palette[Mathf.Clamp(randomFromPalette ? random.Next(palette.Count) : paletteIndex, 0, palette.Count - 1)];
            if (prefab == null) continue;
            RaycastHit ground = centre;
            if (scatterCount > 1)
            {
                float angle = (float)random.NextDouble() * Mathf.PI * 2f, distance = Mathf.Sqrt((float)random.NextDouble()) * scatterRadius;
                Vector3 point = centre.point + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                if (!GroundBelow(point, scatterRadius + 2f, out ground)) continue;
            }
            var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(placed, "Place Prefab");
            placed.transform.SetParent(root, true);
            Vector3 up = Vector3.Slerp(Vector3.up, ground.normal, alignToSlope).normalized;
            Quaternion turn = Quaternion.Euler(0f, randomYaw ? (float)random.NextDouble() * 360f : 0f, 0f);
            placed.transform.SetPositionAndRotation(ground.point - up * sink, Quaternion.FromToRotation(Vector3.up, up) * turn * prefab.transform.rotation);
            float scale = Mathf.Lerp(scaleRange.x, scaleRange.y, (float)random.NextDouble());
            placed.transform.localScale = prefab.transform.localScale * scale;
            if (placed.GetComponentInChildren<Rigidbody>() != null && placed.GetComponent<SettleWhenGroundReady>() == null)
                Undo.AddComponent<SettleWhenGroundReady>(placed);
        }
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
    }

    private void Erase(Vector3 point, float area)
    {
        GameObject root = FindPlacedRoot(false);
        if (root == null) return;
        Undo.IncrementCurrentGroup();
        var doomed = new List<GameObject>();
        foreach (Transform child in root.transform)
            if ((child.position - point).sqrMagnitude <= area * area) doomed.Add(child.gameObject);
        foreach (GameObject target in doomed) Undo.DestroyObjectImmediate(target);
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        if (doomed.Count > 0) EditorSceneManager.MarkSceneDirty(root.scene);
    }

    private void DropSelection()
    {
        if (!world.LayoutReady) { ShowNotification(new GUIContent("Show the terrain first")); return; }
        Undo.IncrementCurrentGroup();
        int moved = 0;
        foreach (Transform selected in Selection.transforms)
        {
            if (!GroundBelow(selected.position, 2f, out RaycastHit below)) continue;
            Undo.RecordObject(selected, "Drop To Ground");
            Vector3 up = Vector3.Slerp(Vector3.up, below.normal, alignToSlope).normalized;
            selected.position = below.point - up * sink;
            moved++;
        }
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        ShowNotification(new GUIContent(moved + " dropped to the ground"));
    }

    // ---------------- Palette ----------------

    private bool AddFolder(string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder)) { ShowNotification(new GUIContent(folder + " not found")); return false; }
        bool any = false;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab != null && !palette.Contains(prefab)) { palette.Add(prefab); any = true; }
        }
        return any;
    }

    private void LoadPalette()
    {
        palette.Clear();
        foreach (string guid in EditorPrefs.GetString(PalettePrefsKey, "").Split(';'))
        {
            if (string.IsNullOrEmpty(guid)) continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab != null) palette.Add(prefab);
        }
    }

    private void SavePalette()
    {
        var guids = new List<string>();
        foreach (GameObject prefab in palette)
            if (prefab != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prefab, out string guid, out long _)) guids.Add(guid);
        EditorPrefs.SetString(PalettePrefsKey, string.Join(";", guids));
    }
}

/// <summary>Undo and redo of sculpting change the saved asset; this rebuilds the chunks that differ.</summary>
[InitializeOnLoad]
internal static class TerrainSculptUndo
{
    static TerrainSculptUndo()
    {
        Undo.undoRedoPerformed += () =>
        {
            if (Application.isPlaying) return;
            foreach (ProceduralWorld world in Object.FindObjectsByType<ProceduralWorld>(FindObjectsSortMode.None))
                if (world.CanSculpt) world.SyncSculptFromData();
        };
    }
}
