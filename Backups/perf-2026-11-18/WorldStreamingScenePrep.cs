using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Switches the WorldStreaming groups off in the copy of Scene_World that is being played or built, so they do not
/// wake with the scene. The scene file and the edit-mode scene are not changed.
/// </summary>
public static class WorldStreamingScenePrep
{
    [PostProcessScene(-100)]
    private static void Prepare()
    {
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Transform root = WorldStreaming.FindRoot(SceneManager.GetSceneAt(s));
            if (root == null) continue;
            foreach (WorldStreaming.Group entry in WorldStreaming.Groups)
            {
                Transform group = WorldStreaming.FindGroup(root, entry.path);
                if (group == null || !group.gameObject.activeSelf || group.name.EndsWith(WorldStreaming.Marker)) continue;
                group.gameObject.SetActive(false);
                group.name += WorldStreaming.Marker;
            }
        }
    }
}
