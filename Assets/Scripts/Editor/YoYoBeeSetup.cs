using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Authors only the bee anchor and adapter in the non-tutorial YoYo scene.</summary>
public static class YoYoBeeSetup
{
    [MenuItem("Tools/Capstone/Glow Bees/Place Near YoYo Player")]
    public static void Place()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/Scene_Yoyo.unity")
            throw new InvalidOperationException("Open Scene_Yoyo in Edit Mode first.");
        SmoothFirstPersonController player = null;
        GlowBeeColony colony = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (player == null) player = root.GetComponentInChildren<SmoothFirstPersonController>();
            if (colony == null) colony = root.GetComponentInChildren<GlowBeeColony>();
        }
        if (player == null) throw new InvalidOperationException("YoYo player was not found.");
        if (player.GetComponent<PlayerCreatureStats>() == null) Undo.AddComponent<PlayerCreatureStats>(player.gameObject);
        if (colony == null)
        {
            var go = new GameObject("Glow Bee Colony — player test");
            Undo.RegisterCreatedObjectUndo(go, "Place YoYo bee colony");
            colony = Undo.AddComponent<GlowBeeColony>(go);
        }
        Transform anchor = colony.transform.Find("Nest Anchor");
        if (anchor == null)
        {
            var go = new GameObject("Nest Anchor");
            Undo.RegisterCreatedObjectUndo(go, "Place bee nest anchor");
            anchor = go.transform;
            anchor.SetParent(colony.transform, false);
            Transform orientation = player.transform.Find("Orientation");
            Vector3 forward = Vector3.ProjectOnPlane(orientation != null ? orientation.forward : player.transform.forward, Vector3.up).normalized;
            Vector3 desired = player.transform.position + forward * 6f;
            Physics.SyncTransforms();
            if (!scene.GetPhysicsScene().Raycast(desired + Vector3.up * 20f, Vector3.down, out RaycastHit floor,
                50f, 1 << LayerMask.NameToLayer("Ground"), QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("No Ground-layer floor near the YoYo player.");
            anchor.position = floor.point;
        }
        var settings = new SerializedObject(colony);
        settings.FindProperty("nestCount").intValue = 1;
        settings.FindProperty("beesPerNest").intValue = 4;
        settings.FindProperty("flowersPerNest").intValue = 5;
        settings.FindProperty("spawnSpider").boolValue = false;
        settings.FindProperty("sceneryLayers").intValue = LuminosityQuery.DefaultObstructionMask;
        var anchors = settings.FindProperty("nestAnchors");
        anchors.arraySize = 1;
        anchors.GetArrayElementAtIndex(0).objectReferenceValue = anchor;
        settings.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = colony.gameObject;
    }
}
