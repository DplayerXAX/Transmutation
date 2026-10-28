using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Shows the Hanging Spire and the Bone Kiln in the Scene view while editing, so levels can be laid out around them.
///
/// The landmark objects in the scene stay small handles: each one is copied onto a hidden, unsaved preview object
/// and built there with the same code as in Play mode (minus creatures and her children, which roam anyway).
/// With "Place At This Object" on, moving a handle moves the landmark to the sinkhole nearest to it; the preview
/// follows while dragging and is rebuilt when the mouse is let go. Previews are removed before Play mode.
/// </summary>
[InitializeOnLoad]
public static class LandmarkScenePreview
{
    private const string PrefsKey = "Capstone.LandmarkScenePreview";
    private const string MenuPath = "Tools/Capstone/Landmarks/Show Landmarks In Scene View";
    private const string PreviewPrefix = "[Preview] ";
    private const double RebuildDelay = 0.4;

    private sealed class Preview
    {
        public MonoBehaviour source;
        public MonoBehaviour copy;
        public GameObject clone;
        public Vector3 builtAt;
        public Vector3 lastHandle;
        public string settings;
        public double changedAt;
        public bool dirty;
    }

    private static readonly Dictionary<System.Type, Preview> previews = new Dictionary<System.Type, Preview>();
    private static double nextTick;
    private static string builtLayout;

    static LandmarkScenePreview()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += change => { if (change == PlayModeStateChange.ExitingEditMode) ClearAll(); };
        EditorSceneManager.sceneClosing += (scene, removing) => ClearAll();
        AssemblyReloadEvents.beforeAssemblyReload += ClearAll;
        Selection.selectionChanged += RedirectSelection;
        SceneView.duringSceneGui += DrawHandles;
        EditorApplication.delayCall += ClearOrphans;
    }

    public static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefsKey, true);
        set { EditorPrefs.SetBool(PrefsKey, value); if (!value) ClearAll(); }
    }

    [MenuItem(MenuPath, false, 40)]
    private static void ToggleMenu() => Enabled = !Enabled;

    [MenuItem(MenuPath, true)]
    private static bool ToggleMenuCheck()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    /// <summary>The built preview copy of a landmark (null when there is none).</summary>
    public static T PreviewOf<T>() where T : MonoBehaviour =>
        previews.TryGetValue(typeof(T), out Preview p) && p.clone != null ? p.copy as T : null;

    /// <summary>Builds both previews now if they are missing or out of date (needs the terrain shown).</summary>
    public static void BuildNow()
    {
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null || !world.LayoutReady) return;
        foreach (Preview p in previews.Values) p.dirty = true;
        Refresh(world, force: true);
    }

    /// <summary>Removes every preview (they come back by themselves while Enabled).</summary>
    public static void ClearAll()
    {
        foreach (Preview p in previews.Values) DestroyClone(p);
        previews.Clear();
        builtLayout = null;
    }

    // ---------------- Keeping the previews up to date ----------------

    private static void Tick()
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        if (EditorApplication.timeSinceStartup < nextTick) return;
        nextTick = EditorApplication.timeSinceStartup + 0.2;
        if (!Enabled) return;
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null || !world.LayoutReady) return;
        Refresh(world, force: false);
    }

    private static void Refresh(ProceduralWorld world, bool force)
    {
        // A moved sinkhole (or a re-baked terrain) changes the layout: build both again.
        string layout = world.UsingSculptedTerrain ? world.SculptData.Layout : "procedural";
        if (layout != builtLayout)
        {
            builtLayout = layout;
            foreach (Preview p in previews.Values) p.dirty = true;
        }

        bool spireRebuilt = Update(Find<HangingSpire>(), force, out Preview spire);
        HangingSpire spireCopy = spire != null ? spire.copy as HangingSpire : null;
        // She avoids the spire's sinkhole, so she follows when it moves.
        Preview kiln = Get(typeof(BoneKiln));
        if (spireRebuilt && kiln != null) kiln.dirty = true;
        Update(Find<BoneKiln>(), force, out _, spireCopy != null && spireCopy.Built ? spireCopy.SinkholeIndex : -1);
    }

    private static Preview Get(System.Type type) => previews.TryGetValue(type, out Preview p) ? p : null;

    private static T Find<T>() where T : MonoBehaviour
    {
        foreach (T found in Object.FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
            if ((found.gameObject.hideFlags & HideFlags.DontSave) == 0 && found.enabled && found.gameObject.activeInHierarchy) return found;
        return null;
    }

    /// <summary>Follows, rebuilds or removes one landmark's preview. True when it was rebuilt.</summary>
    private static bool Update<T>(T source, bool force, out Preview p, int spireHole = -1) where T : MonoBehaviour
    {
        p = Get(typeof(T));
        if (source == null)
        {
            if (p != null) { DestroyClone(p); previews.Remove(typeof(T)); }
            p = null;
            return false;
        }
        if (p == null || p.source != source)
        {
            if (p != null) DestroyClone(p);
            p = new Preview { source = source, dirty = true };
            previews[typeof(T)] = p;
        }

        double now = EditorApplication.timeSinceStartup;
        bool dragging = GUIUtility.hotControl != 0;
        string settings = EditorJsonUtility.ToJson(source);
        if (settings != p.settings) { p.settings = settings; p.dirty = true; p.changedAt = now; }
        Vector3 handle = source.transform.position;
        if (PlacedAtObject(source) && (handle - p.lastHandle).sqrMagnitude > 1e-4f)
        {
            // Drag the old build along until the new one is ready.
            if (p.clone != null) p.clone.transform.position = handle;
            p.lastHandle = handle;
            p.changedAt = now;
            p.dirty = true;
        }
        if (p.clone == null) p.dirty = true;
        if (!p.dirty) return false;
        if (!force && (dragging || now - p.changedAt < RebuildDelay)) return false;

        DestroyClone(p);
        Build(p, spireHole);
        p.dirty = false;
        SceneView.RepaintAll();
        return true;
    }

    private static bool PlacedAtObject(MonoBehaviour source) =>
        source is HangingSpire spire ? spire.PlaceAtThisObject : source is BoneKiln kiln && kiln.PlaceAtThisObject;

    private static void Build(Preview p, int spireHole)
    {
        Vector3 at = p.source.transform.position;
        var clone = new GameObject(PreviewPrefix + p.source.name + " (not saved)") { hideFlags = HideFlags.DontSave };
        SceneManagerMove(clone, p.source.gameObject);
        clone.transform.position = at;
        var copy = (MonoBehaviour)clone.AddComponent(p.source.GetType());
        EditorUtility.CopySerialized(p.source, copy);
        p.clone = clone;
        p.copy = copy;
        p.builtAt = at;
        p.lastHandle = at;
        Physics.SyncTransforms();
        try
        {
            if (copy is HangingSpire spire) spire.BuildPreview();
            else if (copy is BoneKiln kiln) kiln.BuildPreview(spireHole);
        }
        catch (System.Exception error)
        {
            Debug.LogWarning("Could not build the Scene view preview of " + p.source.name + ": " + error.Message, p.source);
        }
        // Nothing of the preview is saved or edited; select the handle instead.
        foreach (Transform t in clone.GetComponentsInChildren<Transform>(true))
            t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
        Physics.SyncTransforms();
    }

    private static void SceneManagerMove(GameObject clone, GameObject next)
    {
        if (clone.scene != next.scene && next.scene.IsValid()) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone, next.scene);
    }

    private static void DestroyClone(Preview p)
    {
        if (p.clone == null) { p.clone = null; p.copy = null; return; }
        DestroyTree(p.clone);
        p.clone = null;
        p.copy = null;
    }

    private static void DestroyTree(GameObject clone)
    {
        // Meshes made by the build are not assets; free them with it.
        var meshes = new HashSet<Mesh>();
        foreach (MeshFilter filter in clone.GetComponentsInChildren<MeshFilter>(true))
            if (filter.sharedMesh != null && !EditorUtility.IsPersistent(filter.sharedMesh)) meshes.Add(filter.sharedMesh);
        foreach (MeshCollider collider in clone.GetComponentsInChildren<MeshCollider>(true))
            if (collider.sharedMesh != null && !EditorUtility.IsPersistent(collider.sharedMesh)) meshes.Add(collider.sharedMesh);
        Object.DestroyImmediate(clone);
        foreach (Mesh mesh in meshes) Object.DestroyImmediate(mesh);
    }

    // Previews left behind by a crash or an unusual reload.
    private static void ClearOrphans()
    {
        foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go != null && go.transform.parent == null && go.name.StartsWith(PreviewPrefix) &&
                (go.hideFlags & HideFlags.DontSave) != 0 && !EditorUtility.IsPersistent(go))
                DestroyTree(go);
    }

    // ---------------- Scene view ----------------

    private static void RedirectSelection()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null || (selected.hideFlags & HideFlags.DontSave) == 0) return;
        foreach (Preview p in previews.Values)
            if (p.clone != null && p.source != null && selected.transform.IsChildOf(p.clone.transform))
            {
                Selection.activeGameObject = p.source.gameObject;
                return;
            }
    }

    private static void DrawHandles(SceneView view)
    {
        if (Application.isPlaying || !Enabled) return;
        foreach (Preview p in previews.Values)
        {
            if (p.source == null) continue;
            Vector3 at = p.source.transform.position;
            bool follows = PlacedAtObject(p.source);
            Handles.color = follows ? new Color(0.3f, 0.9f, 1f, 0.9f) : new Color(1f, 0.7f, 0.3f, 0.9f);
            Handles.DrawWireDisc(at, Vector3.up, 6f);
            Handles.DrawLine(at, at + Vector3.up * 12f);
            string note = follows ? "move this object to move it" : "turn on Place At This Object to move it";
            Handles.Label(at + Vector3.up * 13f, p.source.name + "\n" + (p.dirty ? "rebuilding..." : note), EditorStyles.whiteLabel);
        }
    }

    // ---------------- Inspector part shared by both landmarks ----------------

    public static void PlacementGUI(MonoBehaviour landmark)
    {
        if (Application.isPlaying) return;
        bool follows = PlacedAtObject(landmark);
        EditorGUILayout.HelpBox(follows
            ? "Scene view preview: move this object and the landmark moves to the nearest sinkhole. Use the button " +
              "below to bring a sinkhole right under it (saved terrain only)."
            : "Placed automatically by distance from the spawn. Turn on 'Place At This Object' (Placement) to place it by hand, " +
              "or run Tools > Capstone > Terrain > Make Scene Editable.", MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(Enabled ? "Rebuild Preview" : "Show Preview")) { Enabled = true; BuildNow(); }
            using (new EditorGUI.DisabledScope(!follows))
                if (GUILayout.Button("Move Its Sinkhole Under This Object")) MoveSinkholeHere(landmark);
        }
        EditorGUILayout.Space();
    }

    private static void MoveSinkholeHere(MonoBehaviour landmark)
    {
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null) return;
        if (!world.UsingSculptedTerrain)
        {
            EditorUtility.DisplayDialog("Landmarks", "Moving sinkholes needs the saved terrain. Run Tools > Capstone > Terrain > " +
                "Bake Open Scene World To Sculpted Terrain first.", "OK");
            return;
        }
        int index = CurrentSinkhole(landmark);
        if (index < 0) { EditorUtility.DisplayDialog("Landmarks", "The preview has no sinkhole yet.", "OK"); return; }
        if (!EditorUtility.DisplayDialog("Landmarks", "Move sinkhole " + index + " under " + landmark.name + "?\n\nThe old hole fills in " +
                "and the world is rebuilt. Sculpting is kept. This is saved in the terrain asset and Ctrl+Z does not undo it " +
                "(move it back the same way). Baked decor near either spot may need re-baking.", "Move", "Cancel"))
            return;
        try
        {
            EditorUtility.DisplayProgressBar("Landmarks", "Moving the sinkhole and rebuilding the terrain...", 0.5f);
            MoveSinkholeUnder(landmark, world, index);
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    /// <summary>The sinkhole a landmark's preview is built on (-1 = none).</summary>
    public static int CurrentSinkhole(MonoBehaviour landmark)
    {
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null) return -1;
        if (!world.LayoutReady) TerrainSculptWindow.ShowTerrain(world);
        BuildNow();
        Preview p = Get(landmark.GetType());
        if (p == null || p.copy == null) return -1;
        return p.copy is HangingSpire spire ? (spire.Built ? spire.SinkholeIndex : -1) : ((BoneKiln)p.copy).Root != null ? ((BoneKiln)p.copy).SinkholeIndex : -1;
    }

    /// <summary>Moves the given sinkhole under the landmark's object, saves the terrain and rebuilds the previews.</summary>
    public static bool MoveSinkholeUnder(MonoBehaviour landmark, ProceduralWorld world, int index)
    {
        if (!world.MoveSinkhole(index, landmark.transform.position)) return false;
        EditorUtility.SetDirty(world.SculptData);
        AssetDatabase.SaveAssetIfDirty(world.SculptData);
        BuildNow();
        return true;
    }
}

[CustomEditor(typeof(HangingSpire))]
public sealed class HangingSpireEditor : Editor
{
    public override void OnInspectorGUI()
    {
        LandmarkScenePreview.PlacementGUI((MonoBehaviour)target);
        DrawDefaultInspector();
    }
}

[CustomEditor(typeof(BoneKiln))]
public sealed class BoneKilnEditor : Editor
{
    public override void OnInspectorGUI()
    {
        LandmarkScenePreview.PlacementGUI((MonoBehaviour)target);
        DrawDefaultInspector();
    }
}
