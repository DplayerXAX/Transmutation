using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One click setup for the lit line-world look: switches the ProceduralWorld in the open scene
/// to the lit terrain materials, adds a WorldDecorator with its materials,
/// adds pickable fruit and flowers to the inner world (CaveHarvest), switches the sky to a calmer copy of
/// the white fractal sky, and adds a CameraWallGuard to the player so the camera stays out of walls.
/// </summary>
public static class WorldLookSetup
{
    private const string Folder = "Assets/Materials/World/";

    [MenuItem("Tools/Capstone/Apply World Look And Camera Guard To Open Scene")]
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
        var glow = AssetDatabase.LoadAssetAtPath<Material>(Folder + "MAT_Decor_InnerGlow.mat");
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
        decorator.SetMaterials(stone, organic, shell, glow);
        decorator.ResetKinds();
        EditorUtility.SetDirty(decorator);

        var fruit = AssetDatabase.LoadAssetAtPath<Material>(Folder + "MAT_Harvest_Fruit.mat");
        var flower = AssetDatabase.LoadAssetAtPath<Material>(Folder + "MAT_Harvest_Flower.mat");
        CaveHarvest harvest = world.GetComponent<CaveHarvest>();
        if (harvest == null) harvest = Undo.AddComponent<CaveHarvest>(world.gameObject);
        Undo.RecordObject(harvest, "Set Harvest Materials");
        harvest.SetMaterials(fruit, flower, shell);
        EditorUtility.SetDirty(harvest);

        var sky = AssetDatabase.LoadAssetAtPath<Material>(Folder + "MAT_Sky_FractalCalm.mat");
        if (sky != null && RenderSettings.skybox != sky)
        {
            Undo.RecordObject(Unsupported.GetRenderSettings(), "Calm fractal sky");
            RenderSettings.skybox = sky;
        }

        SmoothFirstPersonController player = Object.FindFirstObjectByType<SmoothFirstPersonController>();
        if (player != null && player.GetComponent<CameraWallGuard>() == null)
            Undo.AddComponent<CameraWallGuard>(player.gameObject);

        EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        Selection.activeGameObject = world.gameObject;
        Debug.Log("World look applied to " + world.name + ". Save the scene to keep it.");
    }
}
