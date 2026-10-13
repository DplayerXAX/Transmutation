using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Puts the player's procedural arms and legs, hand-over-hand climbing, footsteps and camera wall guard
/// into the PlayerObjects prefab, so every scene that uses the prefab gets them.
/// Scene_Daniel had them on a separate scene object ("Player Body"); this moves them into the prefab
/// (keeping their tuned values) and removes the scene copies so nothing runs twice.
/// </summary>
public static class PlayerBodySetup
{
    private const string PrefabPath = "Assets/Prefabs/PlayerObjects.prefab";

    [MenuItem("Tools/Capstone/Player/Move Arms, Legs And Climbing Into Player Prefab")]
    private static void MoveIntoPrefab()
    {
        // Settings tuned in the open scene, copied into the prefab.
        FirstPersonBody sceneBody = Object.FindFirstObjectByType<FirstPersonBody>();
        HandClimber sceneClimber = Object.FindFirstObjectByType<HandClimber>();
        Footsteps sceneSteps = Object.FindFirstObjectByType<Footsteps>();
        bool sceneBodyIsInPrefab = sceneBody != null && PrefabUtility.IsPartOfPrefabInstance(sceneBody);

        // The scene's own camera guard (added on top of the prefab) goes first, so it is not doubled once the prefab has one.
        int removed = 0;
        foreach (CameraWallGuard guard in Object.FindObjectsByType<CameraWallGuard>(FindObjectsSortMode.None))
            if (PrefabUtility.IsAddedComponentOverride(guard))
            {
                Undo.DestroyObjectImmediate(guard);
                removed++;
            }

        GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            SmoothFirstPersonController player = prefab.GetComponentInChildren<SmoothFirstPersonController>(true);
            if (player == null)
            {
                EditorUtility.DisplayDialog("Player Body", "No SmoothFirstPersonController in " + PrefabPath, "OK");
                return;
            }

            Transform holder = prefab.transform.Find("Player Body");
            if (holder == null)
            {
                holder = new GameObject("Player Body").transform;
                holder.SetParent(prefab.transform, false);
            }
            HandClimber climber = holder.GetComponent<HandClimber>();
            if (climber == null) climber = holder.gameObject.AddComponent<HandClimber>();
            FirstPersonBody body = holder.GetComponent<FirstPersonBody>();
            if (body == null) body = holder.gameObject.AddComponent<FirstPersonBody>();
            if (!sceneBodyIsInPrefab)
            {
                if (sceneClimber != null) EditorUtility.CopySerialized(sceneClimber, climber);
                if (sceneBody != null) EditorUtility.CopySerialized(sceneBody, body);
            }
            ClearSceneReferences(climber);
            ClearSceneReferences(body);
            var settings = new SerializedObject(body);
            if (settings.FindProperty("limbMaterial").objectReferenceValue == null)
                settings.FindProperty("limbMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MAT_Player_Limb.mat");
            if (settings.FindProperty("jointMaterial").objectReferenceValue == null)
                settings.FindProperty("jointMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MAT_Player_Joint.mat");
            settings.ApplyModifiedPropertiesWithoutUndo();

            Footsteps steps = holder.GetComponent<Footsteps>();
            if (steps == null) steps = holder.gameObject.AddComponent<Footsteps>();
            if (sceneSteps != null && !PrefabUtility.IsPartOfPrefabInstance(sceneSteps)) EditorUtility.CopySerialized(sceneSteps, steps);

            if (player.GetComponent<CameraWallGuard>() == null) player.gameObject.AddComponent<CameraWallGuard>();
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }

        // Remove the old scene copies so the prefab's are the only ones.
        if (!sceneBodyIsInPrefab)
        {
            foreach (Component old in new Component[] { sceneSteps, sceneBody, sceneClimber })
            {
                if (old == null || PrefabUtility.IsPartOfPrefabInstance(old)) continue;
                GameObject owner = old.gameObject;
                Undo.DestroyObjectImmediate(old);
                removed++;
                if (owner.GetComponents<Component>().Length == 1 && owner.transform.childCount == 0) Undo.DestroyObjectImmediate(owner);
            }
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Arms, legs, climbing, footsteps and camera guard are now in {PrefabPath}. Removed {removed} old copies from the open scene; save it.");
    }

    /// <summary>References to scene objects cannot live in a prefab; the components find them at runtime.</summary>
    private static void ClearSceneReferences(Component component)
    {
        var serialized = new SerializedObject(component);
        SerializedProperty property = serialized.GetIterator();
        while (property.NextVisible(true))
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue == null) continue;
            if (!EditorUtility.IsPersistent(property.objectReferenceValue)) property.objectReferenceValue = null;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
