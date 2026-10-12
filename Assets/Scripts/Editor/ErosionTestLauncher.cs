using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

public static class ErosionTestLauncher
{
    public static void Run()
    {
        PlayerSettings.productName = "Transmutation Erosion Validation";
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/ErosionFluidTest.unity");
        EditorApplication.EnterPlaymode();
    }
    public static void BuildPlayer()
    {
        PlayerSettings.productName = "Transmutation Erosion Validation";
        PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080;
        PlayerSettings.fullScreenMode = UnityEngine.FullScreenMode.Windowed;
        var options = new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/ErosionFluidTest.unity", ErosionSceneSetup.IntegrationScenePath },
            locationPathName = "Builds/ErosionValidation/ErosionValidation.exe", target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded) throw new System.Exception("Erosion validation build failed: " + report.summary.result);
    }
    public static void CreateIntegrationAndBuild()
    {
        ErosionSceneSetup.CreateIntegration();
        BuildPlayer();
    }
}
