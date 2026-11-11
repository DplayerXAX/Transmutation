using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One click setup for the meditation / reflection space: makes its materials and adds a
/// "Meditation" object with all its components to the open scene.
/// </summary>
public static class MeditationSetup
{
    private const string Folder = "Assets/Materials/Meditation/";
    private const string UnlockAllMenu = "Tools/Capstone/Meditation/Unlock All Exhibits";
    private const string ClearUnlocksMenu = "Tools/Capstone/Meditation/Clear Exhibit Unlocks";

    [MenuItem(UnlockAllMenu)]
    private static void UnlockAll()
    {
        var catalog = ReflectionCatalog.Load();
        if (catalog == null)
        {
            Debug.LogError("Reflection catalog is missing.");
            return;
        }

        try
        {
            int count = ReflectionProgress.UnlockAllForEditor(catalog);
            Debug.Log($"Saved {count} Reflection exhibit unlocks.");
        }
        catch (System.Exception exception)
        {
            Debug.LogError("Could not unlock Reflection exhibits: " + exception.Message);
        }
    }

    [MenuItem(ClearUnlocksMenu)]
    private static void ClearUnlocks()
    {
        try
        {
            ReflectionProgress.ClearForEditor();
            Debug.Log("Cleared Reflection exhibit unlocks.");
        }
        catch (System.Exception exception)
        {
            Debug.LogError("Could not clear Reflection exhibit unlocks: " + exception.Message);
        }
    }

    [MenuItem(UnlockAllMenu, true)]
    [MenuItem(ClearUnlocksMenu, true)]
    private static bool CanEditUnlocks() => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Tools/Capstone/Meditation/Add Meditation To Open Scene")]
    private static void Add()
    {
        if (Object.FindFirstObjectByType<MeditationController>() != null)
        {
            EditorUtility.DisplayDialog("Meditation", "The open scene already has a MeditationController.", "OK");
            return;
        }

        Material veil = GetOrCreate("MAT_Meditation_Veil", "Capstone/Meditation/Veil", null);
        Material floor = GetOrCreate("MAT_Reflection_Floor", "Capstone/Meditation/Ink", m =>
        {
            m.SetFloat("_RingCount", 22f);
            m.SetFloat("_RingKeep", 0.55f);
            m.SetFloat("_EdgeRagged", 0.18f);
            m.SetFloat("_EdgeRound", 1f);
            m.SetFloat("_RimInk", 0f);
        });
        Material stone = GetOrCreate("MAT_Reflection_Stone", "Capstone/Meditation/Ink", ReflectionSpace.MakeBlackStone);
        Shader spotlight = Shader.Find("Capstone/Meditation/Spotlight");
        if (veil == null || floor == null || stone == null || spotlight == null)
        {
            EditorUtility.DisplayDialog("Meditation", "Meditation shaders not found. Let Unity finish compiling and try again.", "OK");
            return;
        }

        var go = new GameObject("Meditation");
        Undo.RegisterCreatedObjectUndo(go, "Add Meditation");
        go.AddComponent<ReflectionSpace>().SetMaterials(floor, stone, spotlight);
        go.AddComponent<ReflectionVisitor>();
        go.AddComponent<MeditationHUD>();
        var controller = go.AddComponent<MeditationController>();
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("veilMaterial").objectReferenceValue = veil;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log("Meditation added. Look at a resting creature and click to sit, then click again to enter. Save the scene to keep it.");
    }

    [MenuItem("Tools/Capstone/Meditation/Open Saved Drawings Folder")]
    private static void OpenFolder()
    {
        Directory.CreateDirectory(ReflectionStorage.Root);
        EditorUtility.RevealInFinder(ReflectionStorage.Root);
    }

    [MenuItem("Tools/Capstone/Meditation/Clear Saved Drawings And Discoveries")]
    private static void Clear()
    {
        if (!Directory.Exists(ReflectionStorage.Root)) return;
        if (!EditorUtility.DisplayDialog("Meditation", "Delete all saved drawings, creature casts, journals and reflection discoveries?", "Delete", "Cancel"))
            return;
        Directory.Delete(ReflectionStorage.Root, true);
        Debug.Log("Cleared " + ReflectionStorage.Root);
    }

    private static Material GetOrCreate(string name, string shaderName, System.Action<Material> configure)
    {
        string path = Folder + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find(shaderName);
        if (shader == null) return null;
        Directory.CreateDirectory(Folder);
        material = new Material(shader) { name = name };
        configure?.Invoke(material);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
