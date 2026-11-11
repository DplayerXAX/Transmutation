using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch only (run in an isolated copy of the project, then copy the scene and assets back): applies this thread's
/// scene builders to Scene_LeafMap in one go and saves. -executeMethod LeafMapApplyAll.Run
/// </summary>
public static class LeafMapApplyAll
{
    public static void Run()
    {
        if (!Application.isBatchMode) throw new System.InvalidOperationException("Run in an isolated batch project.");
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Scene_LeafMap.unity", OpenSceneMode.Single);
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world.UsingSculptedTerrain) LeafMapTerrain.Scale = LeafMapTerrain.ReadScale(world.SculptData.Layout);
        if (!world.LayoutReady) TerrainSculptWindow.ShowTerrain(world);
        void Step(string name, System.Func<string> step)
        {
            try { Debug.Log("[ApplyAll] " + name + ": " + (step() ?? "done")); }
            catch (System.Exception e) { Debug.LogError("[ApplyAll] " + name + " FAILED: " + e); }
        }
        Step("Bone mother crack", () => BoneScarSetup.Carve(out BoneScar scar) ?? (scar.Line.Count + " points, " + scar.VentPoints.Count + " vents"));
        Step("Time ripples", () => BoneMotherVistaSetup.RemoveRipplesInScene() + " switched off");
        Step("Near and far decor", () => BoneMotherVistaSetup.AddVista(out int near, out int far) ?? (far + " far, " + near + " near"));
        Step("Sky islands", () => SkyIslandsSetup.Add(out SkyIslands islands) ?? (islands.stones.Length + " stones"));
        Step("Cliff ecosystem", () => LeafCliffEcosystemSetup.Build(out string report) ?? report);
        Step("Bellows", () => BellowsSetup.Add() + " placed");
        Step("Black sky", () => { BlackSkySetup.Black(); return RenderSettings.skybox != null ? RenderSettings.skybox.name : "none"; });
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[ApplyAll] saved " + scene.path + ", terrain " + (world.SculptData != null ? AssetDatabase.GetAssetPath(world.SculptData) : "-"));
    }
}
