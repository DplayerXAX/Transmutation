using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Turns the runtime-only world content of the open scene into things that can be laid out in edit mode:
///   - Make Scene Editable: saved terrain, landmark handles placed where they appear now, decor baked to prefabs.
///   - Bake Decor Into Scene As Prefabs: every WorldDecorator piece becomes its own prefab instance under
///     'Placed World Objects', and the decorator is switched off so nothing is placed twice.
///   - Remove Baked Decor: deletes those instances and switches the decorator back on.
/// </summary>
public static class SceneEditableSetup
{
    private const string BakedSuffix = " (baked)";

    [MenuItem("Tools/Capstone/Terrain/Make Scene Editable (Terrain, Landmarks, Decor)", false, 0)]
    private static void MakeEditable()
    {
        if (Application.isPlaying) return;
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null) { EditorUtility.DisplayDialog("Scene", "No ProceduralWorld found in the open scene.", "OK"); return; }
        int pieces = MakeEditable(world);
        if (pieces < 0) return;
        EditorUtility.DisplayDialog("Scene", "The scene is editable:\n\n" +
            "- Terrain: sculpt with Tools > Capstone > Terrain > Sculpt And Place Window.\n" +
            "- Hanging Spire and Bone Kiln: shown in the Scene view. Move their objects to move them; the inspector " +
            "can bring a sinkhole under them.\n" +
            "- Decor: " + pieces + " prefabs under '" + TerrainSculptWindow.PlacedRootName + "'. Move, copy or delete them freely.\n\n" +
            "Save the scene to keep it.", "OK");
    }

    /// <summary>The menu's work without its closing dialog. Returns the number of baked decor pieces, or -1.</summary>
    public static int MakeEditable(ProceduralWorld world)
    {
        // 1. Saved terrain, shown in the Scene view.
        if (!world.UsingSculptedTerrain) TerrainSculptSetup.Bake(world);
        if (!world.LayoutReady) TerrainSculptWindow.ShowTerrain(world);
        if (!world.LayoutReady) return -1;

        // 2. Landmarks: added if missing, then the handles are put where they are built now and follow them from then on.
        if (Object.FindFirstObjectByType<HangingSpire>() == null)
            EditorApplication.ExecuteMenuItem("Tools/Capstone/Landmarks/Add Hanging Spire To Open Scene");
        if (Object.FindFirstObjectByType<BoneKiln>() == null)
            EditorApplication.ExecuteMenuItem("Tools/Capstone/Landmarks/Add Bone Kiln To Open Scene");
        LandmarkScenePreview.Enabled = true;
        LandmarkScenePreview.BuildNow();
        PinHandle(Object.FindFirstObjectByType<HangingSpire>(), LandmarkScenePreview.PreviewOf<HangingSpire>()?.Root);
        LandmarkScenePreview.BuildNow();
        PinHandle(Object.FindFirstObjectByType<BoneKiln>(), LandmarkScenePreview.PreviewOf<BoneKiln>()?.Root);
        LandmarkScenePreview.BuildNow();

        // 3. Decor.
        int pieces = BakeDecor(world, ask: false);
        EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        return pieces;
    }

    // Puts a landmark's handle on the spot it is built at now, and makes it follow the handle from then on.
    private static void PinHandle(MonoBehaviour landmark, Transform built)
    {
        if (landmark == null) return;
        bool follows = landmark is HangingSpire spire ? spire.PlaceAtThisObject : ((BoneKiln)landmark).PlaceAtThisObject;
        if (follows) return;
        Undo.RecordObject(landmark.transform, "Place Landmark Handle");
        Undo.RecordObject(landmark, "Place Landmark Handle");
        if (built != null) landmark.transform.position = built.position;
        if (landmark is HangingSpire s) s.PlaceAtThisObject = true;
        else ((BoneKiln)landmark).PlaceAtThisObject = true;
        EditorUtility.SetDirty(landmark);
    }

    [MenuItem("Tools/Capstone/Terrain/Bake Decor Into Scene As Prefabs", false, 30)]
    private static void BakeDecorMenu()
    {
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null) { EditorUtility.DisplayDialog("Decor", "No ProceduralWorld found in the open scene.", "OK"); return; }
        int pieces = BakeDecor(world, ask: true);
        if (pieces >= 0) Debug.Log(pieces + " decor prefabs placed under '" + TerrainSculptWindow.PlacedRootName + "'. Save the scene to keep them.");
    }

    /// <summary>Places every decorator piece as a prefab instance. Returns the count, or -1 when cancelled.</summary>
    public static int BakeDecor(ProceduralWorld world, bool ask)
    {
        if (Application.isPlaying) return -1;
        WorldDecorator decorator = world.GetComponent<WorldDecorator>();
        if (decorator == null) { EditorUtility.DisplayDialog("Decor", "No WorldDecorator on the world object.", "OK"); return -1; }
        GameObject root = PlacedRoot(world.gameObject.scene, false);
        int old = CountBaked(root);
        if (ask && old > 0 && !EditorUtility.DisplayDialog("Decor", "Replace the " + old + " baked decor pieces? Hand-placed objects are kept, " +
                "but changes to baked pieces are lost.", "Replace", "Cancel"))
            return -1;

        if (!world.LayoutReady) TerrainSculptWindow.ShowTerrain(world);
        if (!world.LayoutReady) return -1;
        // The tower and the kiln shape the decor districts and keep their ground clear, as at runtime.
        LandmarkScenePreview.BuildNow();
        HangingSpire spire = LandmarkScenePreview.PreviewOf<HangingSpire>();
        BoneKiln kiln = LandmarkScenePreview.PreviewOf<BoneKiln>();
        RealmDecorLayout.Snapshot districts = RealmDecorLayout.Capture(world, spire, kiln);
        var reserved = new List<Bounds>();
        if (kiln != null && kiln.Root != null)
            foreach (Renderer renderer in kiln.Root.GetComponentsInChildren<Renderer>())
            {
                Bounds bounds = renderer.bounds;
                bounds.Expand(0.3f);
                reserved.Add(bounds);
            }

        var chunks = new List<GameObject>();
        foreach (Transform child in world.transform)
            if (child.name.StartsWith("World Chunk")) chunks.Add(child.gameObject);
        List<WorldDecorator.BakedPiece> pieces = decorator.PlanPieces(chunks, districts, reserved.ToArray());

        // Prefabs per kind and variant; made (again) when any is missing.
        IReadOnlyList<WorldDecorator.Kind> kinds = decorator.Kinds;
        GameObject[][] prefabs = LoadPrefabs(kinds, decorator.VariantsPerShape);
        if (prefabs == null)
        {
            TerrainSculptSetup.MakeDecorPrefabs();
            prefabs = LoadPrefabs(kinds, decorator.VariantsPerShape);
            if (prefabs == null) { EditorUtility.DisplayDialog("Decor", "Could not make the decor prefabs.", "OK"); return -1; }
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        root = PlacedRoot(world.gameObject.scene, true);
        RemoveBaked(root);
        int placed = 0;
        try
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                if (i % 200 == 0) EditorUtility.DisplayProgressBar("Decor", "Placing decor prefabs...", (float)i / pieces.Count);
                WorldDecorator.BakedPiece piece = pieces[i];
                GameObject prefab = prefabs[piece.kind][piece.variant];
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.scene);
                instance.name = prefab.name + BakedSuffix;
                instance.transform.SetParent(root.transform, false);
                instance.transform.SetPositionAndRotation(piece.position, piece.rotation);
                instance.transform.localScale = prefab.transform.localScale * piece.scale;
                Undo.RegisterCreatedObjectUndo(instance, "Bake Decor");
                placed++;
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        // The decorator would place the same pieces again on top.
        Undo.RecordObject(decorator, "Bake Decor");
        decorator.enabled = false;
        EditorUtility.SetDirty(decorator);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(root.scene);
        return placed;
    }

    [MenuItem("Tools/Capstone/Terrain/Remove Baked Decor (Back To The Runtime Decorator)", false, 31)]
    private static void RemoveBakedMenu()
    {
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null) return;
        GameObject root = PlacedRoot(world.gameObject.scene, false);
        int count = CountBaked(root);
        if (count > 0 && !EditorUtility.DisplayDialog("Decor", "Delete the " + count + " baked decor pieces and switch the WorldDecorator back on?", "Delete", "Cancel"))
            return;
        Undo.IncrementCurrentGroup();
        if (root != null) RemoveBaked(root);
        WorldDecorator decorator = world.GetComponent<WorldDecorator>();
        if (decorator != null)
        {
            Undo.RecordObject(decorator, "Remove Baked Decor");
            decorator.enabled = true;
            EditorUtility.SetDirty(decorator);
        }
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
    }

    private static GameObject[][] LoadPrefabs(IReadOnlyList<WorldDecorator.Kind> kinds, int variants)
    {
        var result = new GameObject[kinds.Count][];
        for (int k = 0; k < kinds.Count; k++)
        {
            result[k] = new GameObject[variants];
            for (int v = 0; v < variants; v++)
            {
                result[k][v] = AssetDatabase.LoadAssetAtPath<GameObject>(TerrainSculptSetup.DecorPrefabPath(kinds[k].name, kinds[k].shape, v));
                if (result[k][v] == null) return null;
            }
        }
        return result;
    }

    private static GameObject PlacedRoot(UnityEngine.SceneManagement.Scene scene, bool create)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == TerrainSculptWindow.PlacedRootName) return root;
        if (!create) return null;
        var made = new GameObject(TerrainSculptWindow.PlacedRootName);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(made, scene);
        Undo.RegisterCreatedObjectUndo(made, "Create Placed Root");
        return made;
    }

    private static int CountBaked(GameObject root)
    {
        int count = 0;
        if (root != null) foreach (Transform child in root.transform) if (child.name.EndsWith(BakedSuffix)) count++;
        return count;
    }

    private static void RemoveBaked(GameObject root)
    {
        var doomed = new List<GameObject>();
        foreach (Transform child in root.transform) if (child.name.EndsWith(BakedSuffix)) doomed.Add(child.gameObject);
        foreach (GameObject target in doomed) Undo.DestroyObjectImmediate(target);
    }
}
