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
        Material face = GetOrCreate("MAT_Reflection_Face", "Capstone/Meditation/Ink", m =>
        {
            m.SetFloat("_EdgeRagged", 0.12f);
            m.SetFloat("_HatchStrength", 0f);
            m.SetFloat("_RimInk", 0f);
            m.SetColor("_PaperColor", new Color(0.95f, 0.94f, 0.91f, 1f));
        });
        Material cast = GetOrCreate("MAT_Reflection_Cast", "Capstone/Meditation/Ink", m =>
        {
            m.SetFloat("_Breath", 0.006f);
        });
        if (veil == null || floor == null)
        {
            EditorUtility.DisplayDialog("Meditation", "Meditation shaders not found. Let Unity finish compiling and try again.", "OK");
            return;
        }

        var go = new GameObject("Meditation");
        Undo.RegisterCreatedObjectUndo(go, "Add Meditation");
        go.AddComponent<ReflectionSpace>().SetMaterials(floor, stone, face, cast);
        go.AddComponent<CreatureJournal>();
        go.AddComponent<ReflectionVisitor>();
        go.AddComponent<MeditationHUD>();
        var controller = go.AddComponent<MeditationController>();
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("veilMaterial").objectReferenceValue = veil;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log("Meditation added. Press M in Play mode. Save the scene to keep it.");
    }

    [MenuItem("Tools/Capstone/Meditation/Open Saved Drawings Folder")]
    private static void OpenFolder()
    {
        Directory.CreateDirectory(ReflectionStorage.Root);
        EditorUtility.RevealInFinder(ReflectionStorage.Root);
    }

    [MenuItem("Tools/Capstone/Meditation/Clear Saved Drawings And Journal")]
    private static void Clear()
    {
        if (!Directory.Exists(ReflectionStorage.Root)) return;
        if (!EditorUtility.DisplayDialog("Meditation", "Delete all saved drawings, creature casts and the journal?", "Delete", "Cancel"))
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
