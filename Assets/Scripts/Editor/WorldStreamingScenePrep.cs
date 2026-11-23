using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Switches the WorldStreaming groups off before Scene_World wakes, so they cost nothing at load.
/// Pressing Play: done just before the editor copies the open scene for Play, and undone when Play ends.
/// Building: done on the built copy. The scene file itself is never changed.
/// </summary>
[InitializeOnLoad]
public static class WorldStreamingScenePrep
{
    static WorldStreamingScenePrep()
    {
        EditorApplication.playModeStateChanged += PlayModeChanged;
        EditorSceneManager.sceneOpened += (scene, mode) => Restore(scene);
        // Also repairs the open scene if the editor stopped in the middle of Play.
        EditorApplication.delayCall += RestoreOpenScenes;
    }

    private static void PlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode)
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                // Play starts from the saved file when the scene is clean, so it is marked to use this state.
                if (SwitchOff(scene)) EditorSceneManager.MarkSceneDirty(scene);
            }
        }
        else if (change == PlayModeStateChange.EnteredEditMode) RestoreOpenScenes();
    }

    [PostProcessScene(-100)]
    private static void OnBuildScene()
    {
        if (!BuildPipeline.isBuildingPlayer) return;
        for (int s = 0; s < SceneManager.sceneCount; s++) SwitchOff(SceneManager.GetSceneAt(s));
    }

    private static bool SwitchOff(Scene scene)
    {
        Transform root = WorldStreaming.FindRoot(scene);
        if (root == null) return false;
        bool changed = false;
        foreach (WorldStreaming.Group entry in WorldStreaming.Groups)
        {
            Transform group = WorldStreaming.FindGroup(root, entry.path);
            if (group == null || !group.gameObject.activeSelf || group.name.EndsWith(WorldStreaming.Marker)) continue;
            group.gameObject.SetActive(false);
            group.name += WorldStreaming.Marker;
            changed = true;
        }
        return changed;
    }

    private static void RestoreOpenScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        for (int s = 0; s < SceneManager.sceneCount; s++) Restore(SceneManager.GetSceneAt(s));
    }

    private static void Restore(Scene scene)
    {
        Transform root = WorldStreaming.FindRoot(scene);
        if (root == null) return;
        foreach (WorldStreaming.Group entry in WorldStreaming.Groups)
        {
            Transform group = WorldStreaming.FindGroup(root, entry.path);
            if (group == null || !group.name.EndsWith(WorldStreaming.Marker)) continue;
            group.name = group.name.Substring(0, group.name.Length - WorldStreaming.Marker.Length);
            group.gameObject.SetActive(true);
        }
    }
}
