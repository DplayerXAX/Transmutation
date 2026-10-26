using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Batch check for Make Scene Editable on Scene_Daniel: landmark previews are built in edit mode and follow their
/// handles, a sinkhole can be moved under a handle, decor is baked to prefab instances, and nothing of the previews
/// is saved with the scene. Run with -executeMethod SceneEditableValidation.Run in an isolated batch project.
/// </summary>
public static class SceneEditableValidation
{
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Scene editable: " + message);
            Debug.Log("Scene editable OK: " + message);
            checks++;
        }

        EditorSceneManager.OpenScene("Assets/Scenes/Scene_Daniel.unity", OpenSceneMode.Single);
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        Check(world != null, "Scene_Daniel has a ProceduralWorld");

        int pieces = SceneEditableSetup.MakeEditable(world);
        Check(world.UsingSculptedTerrain && world.LayoutReady, "the world uses the saved terrain and is shown");
        HangingSpire spire = Object.FindFirstObjectByType<HangingSpire>();
        BoneKiln kiln = Object.FindFirstObjectByType<BoneKiln>();
        HangingSpire spirePreview = LandmarkScenePreview.PreviewOf<HangingSpire>();
        BoneKiln kilnPreview = LandmarkScenePreview.PreviewOf<BoneKiln>();
        Check(spirePreview != null && spirePreview.Built && spirePreview.Root != null, "the spire is built in edit mode");
        Check(kilnPreview != null && kilnPreview.Root != null, "the bone kiln is built in edit mode");
        Check(spirePreview.SinkholeIndex != kilnPreview.SinkholeIndex, "they stand on different sinkholes");
        Check(spire.PlaceAtThisObject && Flat(spire.transform.position, spirePreview.Root.position) < 0.5f, "the spire handle sits on the spire");
        Check(kiln.PlaceAtThisObject && Flat(kiln.transform.position, kilnPreview.Root.position) < 0.5f, "the kiln handle sits on the kiln");
        Debug.Log("Spire on sinkhole " + spirePreview.SinkholeIndex + " at " + spirePreview.Root.position +
                  ", kiln on " + kilnPreview.SinkholeIndex + " at " + kilnPreview.Root.position + ", decor " + pieces);

        WorldDecorator decorator = world.GetComponent<WorldDecorator>();
        GameObject placed = GameObject.Find(TerrainSculptWindow.PlacedRootName);
        Check(pieces > 100 && placed != null, "decor is baked to prefabs (" + pieces + ")");
        Check(!decorator.enabled, "the runtime decorator is switched off");
        int instances = 0;
        foreach (Transform child in placed.transform)
            if (child.name.EndsWith(" (baked)") && PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject)) instances++;
        Check(instances == pieces, "every baked piece is a prefab instance");

        // Moving the spire's handle onto another sinkhole moves the spire there.
        int oldHole = spirePreview.SinkholeIndex;
        int target = -1;
        for (int i = 0; i < world.SinkholeCount; i++)
            if (i != oldHole && i != kilnPreview.SinkholeIndex) { target = i; break; }
        Check(target >= 0, "there is a free sinkhole to move to");
        Vector3 targetCentre = world.SinkholeCentre(target, spire.transform.position.y, out _);
        spire.transform.position = targetCentre;
        LandmarkScenePreview.BuildNow();
        spirePreview = LandmarkScenePreview.PreviewOf<HangingSpire>();
        Check(spirePreview.SinkholeIndex == target, "moving the handle onto sinkhole " + target + " rebuilds the spire there");

        // Bring a sinkhole right under a handle placed away from every hole.
        Vector3 open = OpenSpot(world, kilnPreview.SinkholeIndex);
        spire.transform.position = open;
        int nearest = LandmarkScenePreview.CurrentSinkhole(spire);
        Check(LandmarkScenePreview.MoveSinkholeUnder(spire, world, nearest), "the sinkhole can be moved");
        Check(Flat(world.SinkholeCentre(nearest, world.transform.position.y, out _), open) < 0.5f, "the sinkhole is now under the handle");
        spirePreview = LandmarkScenePreview.PreviewOf<HangingSpire>();
        Check(spirePreview != null && spirePreview.Built && Flat(spirePreview.Root.position, open) < 3f, "the spire is rebuilt over the moved hole");
        Check(world.SampleDensity(world.SinkholeCentre(nearest, open.y - 6f, out _)) < 0f, "the terrain is open under the handle");

        // Saving: handles and baked decor are kept, previews are not.
        const string path = "Assets/Scenes/_SceneEditableValidation.unity";
        EditorSceneManager.SaveScene(spire.gameObject.scene, path, true);
        LandmarkScenePreview.ClearAll();
        Check(GameObject.Find("[Preview] " + spire.name + " (not saved)") == null, "previews are removed (as before Play mode)");
        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        bool previewSaved = false;
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name.StartsWith("[Preview]") || root.name.Contains("(runtime)")) previewSaved = true;
        Check(!previewSaved, "no preview object was saved in the scene");
        HangingSpire reopened = Object.FindFirstObjectByType<HangingSpire>();
        Check(reopened != null && reopened.PlaceAtThisObject && !reopened.Built && reopened.Root == null, "the spire handle is saved, unbuilt");
        int saved = 0;
        foreach (Transform child in GameObject.Find(TerrainSculptWindow.PlacedRootName).transform)
            if (child.name.EndsWith(" (baked)")) saved++;
        Check(saved == pieces, "the baked decor is saved with the scene");
        AssetDatabase.DeleteAsset(path);

        Debug.Log("Scene editable validation passed (" + checks + " checks).");
    }

    private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

    // A surface point at least 30 m from every sinkhole but the kiln's, inside the map.
    private static Vector3 OpenSpot(ProceduralWorld world, int keep)
    {
        Vector3 centre = world.transform.position;
        for (int ring = 20; ring < 120; ring += 10)
            for (int a = 0; a < 16; a++)
            {
                float angle = a * Mathf.PI / 8f;
                Vector3 p = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * ring;
                bool clear = true;
                for (int i = 0; i < world.SinkholeCount && clear; i++)
                    if (Flat(world.SinkholeCentre(i, p.y, out float r), p) < 30f + r) clear = false;
                if (!clear) continue;
                world.ColumnHeights(p, out float surface, out _, out _);
                return new Vector3(p.x, surface, p.z);
            }
        throw new InvalidOperationException("Scene editable: no open spot found");
    }
}
