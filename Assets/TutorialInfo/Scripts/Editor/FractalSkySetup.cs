using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Apply to the already-open scene as well as its saved asset, without saving
// unrelated in-memory edits. The session guard keeps later artistic edits intact.
[InitializeOnLoad]
internal static class FractalSkySetup
{
    private const string MaterialPath = "Assets/Shaders/FractalSky.mat";
    private const string SessionKey = "Capstone.FractalSky.InitialSetup.v1";

    static FractalSkySetup()
    {
        EditorApplication.delayCall += ApplyOnce;
    }

    private static void ApplyOnce()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(SessionKey, false))
            return;
        if (SceneManager.GetActiveScene().path != "Assets/_Recovery/0.unity")
            return;
        if (Apply())
            SessionState.SetBool(SessionKey, true);
    }

    [MenuItem("Tools/Capstone/Apply Fractal Sky to Current Scene")]
    private static void ApplyFromMenu() => Apply();

    private static bool Apply()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null || material.shader == null)
            return false;
        if (ShaderUtil.ShaderHasError(material.shader))
        {
            Debug.LogError("Fractal sky shader has compilation errors. Check the shader Inspector.");
            return false;
        }
        if (RenderSettings.skybox != material)
        {
            Undo.RecordObject(Unsupported.GetRenderSettings(), "Apply fractal sky");
            RenderSettings.skybox = material;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        }
        SceneView.RepaintAll();
        Debug.Log("Fractal sky assigned. Enter Play mode to see continuous morphing; tune Assets/Shaders/FractalSky.mat.");
        return true;
    }
}
