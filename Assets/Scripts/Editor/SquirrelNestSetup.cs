using UnityEditor;
using UnityEngine;

public static class SquirrelNestSetup
{
    [MenuItem("Tools/Creatures/Configure Squirrel Food And Nest Prefabs")]
    public static void Configure()
    {
        if (Application.isPlaying) { Debug.LogWarning("Leave Play Mode before configuring prefabs."); return; }
        Add<SquirrelFood>("Assets/Prefabs/Creatures/Food.prefab");
        Add<SquirrelNest>("Assets/Prefabs/Creatures/Nest.prefab");
        Debug.Log("Squirrel food and nest prefabs configured for player carry, real inventory and capacity.");
    }
    private static void Add<T>(string path) where T : Component
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (root.GetComponent<T>() == null) root.AddComponent<T>();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
