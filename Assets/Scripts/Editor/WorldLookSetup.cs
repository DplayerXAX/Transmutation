using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One click setup for the lit line-world look: switches the ProceduralWorld in the open scene
/// to the lit terrain materials and adds a WorldDecorator with its materials.
/// </summary>
public static class WorldLookSetup
{
    private const string Folder = "Assets/Materials/World/";

    [MenuItem("Tools/Capstone/Apply Lit World Look To Open Scene")]
    private static void Apply()
    {
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null)
        {
            EditorUtility.DisplayDialog("World Look", "No ProceduralWorld found in the open scene.", "OK");
            return;
        }

        var surface = AssetDatabase.LoadAssetAtPath<Material>(Folder + "MAT_World_Surface.mat");
        var inner = AssetDatabase.LoadAssetAtPath<Material>(Folder + "MAT_World_Inner.mat");
        var stone = AssetDatabase.LoadAssetAtPath<Material>(Folder + "MAT_Decor_Stone.mat");
        var organic = AssetDatabase.LoadAssetAtPath<Material>(Folder + "MAT_Decor_Organic.mat");
        var shell = AssetDatabase.LoadAssetAtPath<Material>(Folder + "MAT_Decor_InnerShell.mat");
        if (surface == null || inner == null || stone == null || organic == null || shell == null)
        {
            EditorUtility.DisplayDialog("World Look", "Missing materials in " + Folder, "OK");
            return;
        }

        var serialized = new SerializedObject(world);
        serialized.FindProperty("surfaceMaterial").objectReferenceValue = surface;
        serialized.FindProperty("innerMaterial").objectReferenceValue = inner;
        serialized.ApplyModifiedProperties();

        WorldDecorator decorator = world.GetComponent<WorldDecorator>();
        if (decorator == null) decorator = Undo.AddComponent<WorldDecorator>(world.gameObject);
        Undo.RecordObject(decorator, "Set Decor Materials");
        decorator.SetMaterials(stone, organic, shell);
        EditorUtility.SetDirty(decorator);

        EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        Selection.activeGameObject = world.gameObject;
        Debug.Log("World look applied to " + world.name + ". Save the scene to keep it.");
    }
}
