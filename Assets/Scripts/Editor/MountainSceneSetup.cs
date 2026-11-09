using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Scene authoring only. Generation and editing run through ProceduralWorld's existing tools.</summary>
public static class MountainSceneSetup
{
    public const string ScenePath = "Assets/Scenes/Scene_Mountain.unity";
    public const string DataPath = "Assets/Terrain/Sculpted/Scene_Mountain_Terrain.asset";

    public static void Remake()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open Mountain first.");
        if (AssetDatabase.LoadAssetAtPath<TerrainSculptData>(DataPath) != null)
            throw new InvalidOperationException("Mountain has already been converted. Use the existing terrain sculpt window to edit it.");
        var surface = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Surface.mat");
        var inner = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Inner.mat");
        if (surface == null || inner == null) throw new InvalidOperationException("Shared world materials are missing.");
        foreach (GameObject root in scene.GetRootGameObjects())
            if (root.name == "Amplified Mountains and Skylands") Object.DestroyImmediate(root);
        var terrain = new GameObject("Procedural World — Mountain recipe");
        terrain.layer = LayerMask.NameToLayer("Ground"); terrain.tag = "Ground";
        var world = terrain.AddComponent<ProceduralWorld>();
        var settings = new SerializedObject(world);
        settings.FindProperty("worldSize").vector2Value = new Vector2(1152f,1152f);
        settings.FindProperty("cellSize").floatValue = 3f;
        settings.FindProperty("chunkCells").intValue = 24;
        settings.FindProperty("seed").intValue = 1707;
        settings.FindProperty("generationRecipe").FindPropertyRelative("kind").enumValueIndex = (int)TerrainRecipeKind.AmplifiedMountains;
        settings.FindProperty("surfaceMaterial").objectReferenceValue = surface;
        settings.FindProperty("innerMaterial").objectReferenceValue = inner;
        settings.FindProperty("sinkholeCount").intValue = 0;
        settings.FindProperty("firstSinkholeDistance").floatValue = 0f;
        settings.FindProperty("blockingRadius").floatValue = 150f;
        settings.FindProperty("placePlayerOnSurface").boolValue = true;
        settings.FindProperty("meshesPerFrame").intValue = 8;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var data = ScriptableObject.CreateInstance<TerrainSculptData>();
        data.Freeze(world.CaptureLayout(out float[] checks),checks,"Mountain / amplified recipe v1 / ProceduralWorld");
        AssetDatabase.CreateAsset(data,DataPath);
        settings.Update(); settings.FindProperty("sculptData").objectReferenceValue=data; settings.ApplyModifiedPropertiesWithoutUndo();
        var player = Object.FindFirstObjectByType<SmoothFirstPersonController>();
        Vector3 spawn = new TerrainGenerationRecipe().safeSpawn;
        player.transform.root.position += spawn+Vector3.up*2f-player.transform.position;
        player.transform.rotation = Quaternion.identity;
        player.Orientation.localRotation = Quaternion.identity;
        player.PlayerCamera.transform.localRotation = Quaternion.identity;
        player.SetLook(0f,0f);
        foreach (var camera in player.transform.root.GetComponentsInChildren<Camera>(true)) camera.farClipPlane=1800f;
        RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Sky_FractalCalm.mat");
        RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=new Color(0.48f,0.5f,0.55f);
        RenderSettings.fog=true; RenderSettings.fogMode=FogMode.Linear;
        RenderSettings.fogStartDistance=320f; RenderSettings.fogEndDistance=1300f;
        RenderSettings.fogColor=new Color(0.72f,0.74f,0.77f);
        if (RenderSettings.sun != null) { RenderSettings.sun.color=Color.white; RenderSettings.sun.intensity=1.25f; }
        AssetDatabase.SaveAssets();
        world.Generate(true);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        foreach (string path in new[] { "Assets/Generated/Mountain/MountainGeometry.asset", "Assets/Generated/Mountain/MountainLandscape.mat", "Assets/Generated/Mountain/MountainSky.mat" })
            AssetDatabase.DeleteAsset(path);
        FrameOverview();
    }

    public static void FrameOverview()
    {
        var view=SceneView.lastActiveSceneView;
        if(view==null) return;
        view.sceneLighting=true;
        view.LookAt(new Vector3(0f,240f,0f),Quaternion.Euler(17f,0f,0f),650f,false,true);
        view.Repaint();
    }
}
