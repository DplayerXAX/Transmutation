using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class FBMTerrainSetup
{
    private const string Path = "Assets/Shaders/Terrain/FBMRisingTerrain.mat";
    private const string Key = "Capstone.FBMTerrain.Setup.v1";
    static FBMTerrainSetup() { EditorApplication.delayCall += ApplyOnce; }

    private static void ApplyOnce()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Key, false)) return;
        if (SceneManager.GetActiveScene().path != "Assets/Scenes/Scene_Lam.unity") return;
        if (Apply()) SessionState.SetBool(Key, true);
    }

    [MenuItem("Tools/Capstone/Apply Rising FBM to Scene Terrain")]
    private static void ApplyMenu() { Apply(); }

    private static bool Apply()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(Path);
        if (!material || !material.shader) return false;
        var messages = ShaderUtil.GetShaderMessages(material.shader);
        Directory.CreateDirectory("Library/FBMTerrain");
        File.WriteAllText("Library/FBMTerrain/ShaderValidation.txt",
            "Shader: " + material.shader.name + "\n" +
            string.Join("\n", messages.Select(m => m.severity + ": " + m.message)));
        if (ShaderUtil.ShaderHasError(material.shader)) return false;
        bool applied = false;
        foreach (var terrain in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
        {
            if (terrain.gameObject.scene != SceneManager.GetActiveScene()) continue;
            Undo.RecordObject(terrain, "Apply rising FBM terrain");
            terrain.materialTemplate = material;
            terrain.heightmapPixelError = 1;
            // Terrain patch bounds need room for shader displacement.
            terrain.patchBoundsMultiplier = new Vector3(1, 2, 1);
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            applied = true;
        }
        if (applied)
        {
            File.AppendAllText("Library/FBMTerrain/ShaderValidation.txt", "\nAssigned to active scene terrain.");
            SceneView.RepaintAll();
        }
        return applied;
    }
}
