using UnityEditor;
using UnityEngine;
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
        // The crown seed copies a scene object named Colorful. If this copy of the scene has none, stand one in
        // (two parts under a parent) so the copying is still tested.
        if (GameObject.Find("Colorful") == null)
        {
            var stand = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/topics/06 creating meshes/Cylinder.prefab");
            if (stand != null)
            {
                var parent = new GameObject("Colorful");
                var a = (GameObject)PrefabUtility.InstantiatePrefab(stand, parent.transform);
                var b = (GameObject)PrefabUtility.InstantiatePrefab(stand, parent.transform);
                b.transform.localPosition = a.transform.localPosition + Vector3.up * 1.5f;
                b.transform.localScale *= 0.6f;
                b.AddComponent<BoxCollider>();
            }
        }
        // Same tower material as after running the landmark setup menu.
        EditorApplication.ExecuteMenuItem("Tools/Capstone/Landmarks/Add Hanging Spire To Open Scene");
        EditorApplication.EnterPlaymode();
    }
}

/// <summary>Batch check of the player prefab menu on the test copy (logs what ended up where, then quits).</summary>
public static class PlayerPrefabMenuCheck
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Scene_Daniel.unity");
        EditorApplication.ExecuteMenuItem("Tools/Capstone/Player/Move Arms, Legs And Climbing Into Player Prefab");
        var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Prefabs/PlayerObjects.prefab");
        UnityEngine.Debug.Log($"[PrefabCheck] prefab body {prefab.GetComponentsInChildren<FirstPersonBody>(true).Length}, climber {prefab.GetComponentsInChildren<HandClimber>(true).Length}, guard {prefab.GetComponentsInChildren<CameraWallGuard>(true).Length}, steps {prefab.GetComponentsInChildren<Footsteps>(true).Length}");
        UnityEngine.Debug.Log($"[PrefabCheck] scene body {UnityEngine.Object.FindObjectsByType<FirstPersonBody>(UnityEngine.FindObjectsSortMode.None).Length}, climber {UnityEngine.Object.FindObjectsByType<HandClimber>(UnityEngine.FindObjectsSortMode.None).Length}, guard {UnityEngine.Object.FindObjectsByType<CameraWallGuard>(UnityEngine.FindObjectsSortMode.None).Length}, steps {UnityEngine.Object.FindObjectsByType<Footsteps>(UnityEngine.FindObjectsSortMode.None).Length}");
        EditorSceneManager.OpenScene("Assets/Scenes/Scene_Yoyo.unity");
        UnityEngine.Debug.Log($"[PrefabCheck] yoyo body {UnityEngine.Object.FindObjectsByType<FirstPersonBody>(UnityEngine.FindObjectsSortMode.None).Length}, climber {UnityEngine.Object.FindObjectsByType<HandClimber>(UnityEngine.FindObjectsSortMode.None).Length}");
        EditorApplication.Exit(0);
    }
}
