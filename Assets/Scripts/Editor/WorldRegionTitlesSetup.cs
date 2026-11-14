using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Region titles need no scene edits: RegionTitles adds itself to Scene_World at runtime. This only runs
/// the check: opens Scene_World (without saving anything), makes sure the creature material exists in
/// Resources, and plays with -regionTitleTest so DropletTestDriver walks the player into each region:
/// Unity.exe -batchmode -projectPath . -executeMethod WorldRegionTitlesSetup.PlayAndTest -regionTitleTest
/// </summary>
public static class WorldRegionTitlesSetup
{
    public const string ScenePath = "Assets/Scenes/Scene_World.unity";

    public static void PlayAndTest()
    {
        DropletSceneBuilder.CreatureMaterial();
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorApplication.update += () =>
        {
            if (EditorApplication.timeSinceStartup > 1500.0) EditorApplication.Exit(3);
        };
        EditorApplication.EnterPlaymode();
    }
}
