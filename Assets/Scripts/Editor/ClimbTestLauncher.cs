using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Starts the automated climbing check from the command line:
/// Unity.exe -batchmode -projectPath . -executeMethod ClimbTestLauncher.Run -climbTest -climbTestOut result.txt
/// The ClimbTestDriver does the climbing in Play mode and quits the editor when done.
/// </summary>
[InitializeOnLoad]
public static class ClimbTestLauncher
{
    static ClimbTestLauncher()
    {
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-climbTest") < 0) return;
        // Safety net: never leave a batch editor running.
        EditorApplication.update += () =>
        {
            if (EditorApplication.timeSinceStartup > 1500.0) EditorApplication.Exit(3);
        };
    }

    public static void Run()
    {
        // Real shaders in captured frames, not the placeholder used while shaders compile.
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Scene_Daniel.unity");
        // Same tower material as after running the landmark setup menu.
        EditorApplication.ExecuteMenuItem("Tools/Capstone/Landmarks/Add Hanging Spire To Open Scene");
        EditorApplication.EnterPlaymode();
    }
}
