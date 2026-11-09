using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Switches the open scene's sky to plain black (MAT_Sky_Black, made on first use), or back to the fractal calm sky.
/// Ambient light is left as it is (the scenes use gradient ambient, not the sky), so only the background changes.
/// </summary>
public static class BlackSkySetup
{
    private const string BlackPath = "Assets/Materials/World/MAT_Sky_Black.mat";
    private const string FractalPath = "Assets/Materials/World/MAT_Sky_FractalCalm.mat";

    public static void Black()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(BlackPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Skybox/Cubemap") ?? Shader.Find("Skybox/Procedural");
            material = new Material(shader) { name = "MAT_Sky_Black" };
            if (material.HasProperty("_Tint")) material.SetColor("_Tint", Color.black);
            if (material.HasProperty("_Exposure")) material.SetFloat("_Exposure", 0f);
            if (material.HasProperty("_SkyTint")) material.SetColor("_SkyTint", Color.black);
            if (material.HasProperty("_GroundColor")) material.SetColor("_GroundColor", Color.black);
            if (material.HasProperty("_SunSize")) material.SetFloat("_SunSize", 0f);
            AssetDatabase.CreateAsset(material, BlackPath);
            AssetDatabase.SaveAssets();
        }
        Set(material, "Pure black sky");
    }

    public static void Fractal() => Set(AssetDatabase.LoadAssetAtPath<Material>(FractalPath), "Fractal calm sky");

    private static void Set(Material sky, string label)
    {
        if (sky == null) { Debug.LogWarning("Sky material not found."); return; }
        Undo.RecordObject(Unsupported.GetRenderSettings(), label);
        RenderSettings.skybox = sky;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        SceneView.RepaintAll();
        Debug.Log(label + " set on " + SceneManager.GetActiveScene().name + ". Save the scene to keep it.");
    }
}
