using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Both terrain layers reappear when reopening Basilt or leaving Play mode.</summary>
[InitializeOnLoad]
public static class BasiltScenePreview
{
    static BasiltScenePreview()
    {
        EditorSceneManager.sceneOpened+=(_,__)=>Queue();
        EditorApplication.playModeStateChanged+=s=>{if(s==PlayModeStateChange.EnteredEditMode)Queue();};
        Queue();
    }
    private static void Queue()=>EditorApplication.delayCall+=Build;
    private static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)return;
        var scene=SceneManager.GetActiveScene();if(scene.path!=BasiltSceneSetup.ScenePath)return;
        foreach(var root in scene.GetRootGameObjects())foreach(var w in root.GetComponentsInChildren<ProceduralWorld>())
            // Domain reload can preserve the serialized density layout but drops its worker caches
            // and chunk ownership. Re-upload the saved meshes to restore both together.
            if(w.UsingSculptedTerrain)w.Generate(true);
        SceneView.RepaintAll();
    }
}
