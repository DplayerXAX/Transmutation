using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Carves the bone mother's ground scar into the saved terrain of the open scene: a long torn trench through her hole,
/// wide and deep by the hole and narrowing out to both ends, with raised lips and flat pads deep inside for the bubble
/// vents. The shape and an exact backup of what was changed are kept on a BoneScar component on the BoneKiln object,
/// so Remove Ground Scar puts the terrain back. Both steps can also be undone with Ctrl+Z.
/// Needs a scene with saved (sculpted) terrain and a Bone Kiln (Tools > Capstone > Terrain > Make Scene Editable).
/// </summary>
public static class BoneScarSetup
{
    private const string Menu = "Tools/Capstone/Landmarks/Bone Mother/";

    /// <summary>On: the whole crack is cut through the crust into the inner world. Off: the earlier trench, 13 m at most.</summary>
    public static bool throughToInner = true;

    private static void CarveMenu()
    {
        string problem = Carve(out BoneScar scar);
        if (problem != null) { EditorUtility.DisplayDialog("Bone Mother Scar", problem, "OK"); return; }
        Selection.activeObject = scar;
        EditorUtility.DisplayDialog("Bone Mother Scar", "The scar is carved through her hole (" + scar.Line.Count +
            " points, " + scar.VentPoints.Count + " vents).\n\nIn Play mode it glows, grows inner-world decor and lets fire bubbles " +
            "rise from the vents. Options are on the Bone Scar component of the Bone Kiln object.\n\nSave the scene to keep it.", "OK");
    }

    private static void RemoveMenu()
    {
        BoneScar scar = FindScar();
        if (scar == null || !scar.Carved) { EditorUtility.DisplayDialog("Bone Mother Scar", "There is no carved scar in the open scene.", "OK"); return; }
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (!Ready(world, out string problem)) { EditorUtility.DisplayDialog("Bone Mother Scar", problem, "OK"); return; }
        Remove(world, scar);
        EditorSceneManager.MarkSceneDirty(scar.gameObject.scene);
        LandmarkScenePreview.BuildNow();
    }

    /// <summary>For batch tests: opens Scene_Daniel, carves the scar and saves (only in an isolated batch copy).</summary>
    public static void CarveForTest()
    {
        if (!Application.isBatchMode) throw new System.InvalidOperationException("Run in an isolated batch project.");
        EditorSceneManager.OpenScene("Assets/Scenes/Scene_Daniel.unity", OpenSceneMode.Single);
        string problem = Carve(out BoneScar scar);
        if (problem != null) throw new System.InvalidOperationException(problem);
        Debug.Log("[BoneScar] carved " + scar.Line.Count + " points, " + scar.VentPoints.Count + " vents, sinkhole " + scar.Sinkhole);
        foreach (Vector3 v in scar.VentPoints) Debug.Log("[BoneScar] vent at " + v);
        EditorSceneManager.SaveScene(scar.gameObject.scene);
        AssetDatabase.SaveAssets();
    }

    private static BoneScar FindScar()
    {
        foreach (BoneScar scar in Object.FindObjectsByType<BoneScar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if ((scar.gameObject.hideFlags & HideFlags.DontSave) == 0) return scar;
        return null;
    }

    private static BoneKiln FindKiln()
    {
        foreach (BoneKiln kiln in Object.FindObjectsByType<BoneKiln>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if ((kiln.gameObject.hideFlags & HideFlags.DontSave) == 0) return kiln;
        return null;
    }

    private static bool Ready(ProceduralWorld world, out string problem)
    {
        problem = null;
        if (Application.isPlaying) problem = "Leave Play mode first.";
        else if (world == null) problem = "No ProceduralWorld in the open scene.";
        else if (!world.UsingSculptedTerrain) problem = "The terrain is not saved yet. Run Tools > Capstone > Terrain > Make Scene Editable first.";
        else
        {
            if (!world.LayoutReady) TerrainSculptWindow.ShowTerrain(world);
            if (!world.CanSculpt) problem = "The terrain cannot be sculpted right now.";
        }
        return problem == null;
    }

    /// <summary>Carves the scar (replacing an earlier one). Returns a problem, or null when done.</summary>
    public static string Carve(out BoneScar scar)
    {
        scar = null;
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (!Ready(world, out string problem)) return problem;
        BoneKiln kiln = FindKiln();
        if (kiln == null) return "No Bone Kiln in the open scene.";

        scar = kiln.GetComponent<BoneScar>();
        if (scar == null) scar = Undo.AddComponent<BoneScar>(kiln.gameObject);
        if (scar.Carved) Remove(world, scar);

        // Where she lies now (the Scene view preview builds her).
        LandmarkScenePreview.BuildNow();
        BoneKiln built = LandmarkScenePreview.PreviewOf<BoneKiln>();
        if (built == null || built.Root == null) return "The bone mother could not be built in the Scene view (is Tools > Capstone > Landmarks > Scene Preview on?).";
        Vector3 mouth = built.Root.position;
        Vector3 heading = Vector3.ProjectOnPlane(built.Root.forward, Vector3.up).normalized;
        int hole = built.SinkholeIndex;
        world.SinkholeCentre(hole, mouth.y, out float holeRadius);

        // ---- The shape ----
        int seed = 11 + hole * 7;
        var random = new System.Random(seed);
        float tilt = ((float)random.NextDouble() - 0.5f) * 24f;
        Vector3 axis = Quaternion.Euler(0f, tilt, 0f) * Vector3.Cross(Vector3.up, heading);
        float half = holeRadius + 24f;
        int count = Mathf.CeilToInt(half * 2f) + 1;
        var line = new Vector3[count];
        var widths = new float[count];
        var depths = new float[count];
        var lips = new float[count];
        var ceilings = new float[count];
        int terrain = 1 << world.TerrainLayer;
        for (int i = 0; i < count; i++)
        {
            float u = i / (float)(count - 1) * 2f - 1f;
            float f = 1f - Mathf.Abs(u);
            float bend = 2.2f * Mathf.Sin(u * 2.4f + seed) + 0.8f * Mathf.Sin(u * 7.1f + seed * 2f);
            Vector3 p = mouth + axis * (u * half) + heading * bend;
            widths[i] = Mathf.Lerp(1.3f, 5.2f, Mathf.Pow(f, 0.6f)) * (1f + 0.18f * Mathf.Sin(u * 13f + seed));
            // From the terrain's density, not its colliders (they may still show an earlier scar being removed).
            world.ColumnHeights(p, out float ground, out _, out _);
            lips[i] = float.IsNaN(ground) || ground < mouth.y - 4f ? mouth.y : ground;
            // All the way through the crust into the inner world (or the old shallow trench when throughToInner is off).
            world.ColumnHeights(p, out _, out float ceiling, out _);
            ceilings[i] = ceiling;
            depths[i] = throughToInner && ceiling < lips[i] - 3f ? lips[i] - ceiling + 3f : Mathf.Lerp(1.6f, 13f, Mathf.Pow(f, 1.3f));
            line[i] = new Vector3(p.x, lips[i], p.z);
        }

        // Placed objects over the scar keep their height above the ground.
        var placedGround = new Dictionary<Transform, float>();
        GameObject placedRoot = GameObject.Find(TerrainSculptWindow.PlacedRootName);
        scar.SetShape(hole, mouth, heading, seed, line, widths, depths, lips, new Vector3[0]);
        foreach (Transform item in GroundedThings(placedRoot))
            if (scar.Distance(item.position, out float w) < w + 8f)
            {
                float g = Ground(world, item.position, item.position.y + 3f, terrain);
                if (!float.IsNaN(g)) placedGround[item] = g;
            }

        // ---- Carving ----
        TerrainSculptData data = world.SculptData;
        Undo.IncrementCurrentGroup();
        Undo.RegisterCompleteObjectUndo(data, "Carve Bone Mother Scar");
        Undo.RecordObject(scar, "Carve Bone Mother Scar");
        var old = new Dictionary<Vector3Int, float>();
        foreach (var pair in data.Edits) old[pair.Key] = pair.Value;

        world.BeginSculptStroke();
        try
        {
            for (int i = 0; i < count; i++)
            {
                if (i % 8 == 0) EditorUtility.DisplayProgressBar("Bone Mother Scar", "Carving the trench...", i / (float)count);
                Vector3 across = Vector3.Cross(Vector3.up, Along(line, i));
                float r = Mathf.Clamp(widths[i], 1.4f, 5.5f);
                Vector3 centre = line[i] + across * (widths[i] * 0.25f * Mathf.Sin(i * 1.7f + seed));
                float bottom = lips[i] - depths[i] + r * 0.55f;
                for (float y = lips[i] + r * 0.25f; y >= bottom - 0.01f;)
                {
                    // Deep down the slot is kept at least 4 m wide, so it reads as one open crack from above.
                    float ball = y < lips[i] - 6f ? Mathf.Max(r, 2f) : r;
                    world.Sculpt(TerrainSculptTool.Carve, new Vector3(centre.x, y, centre.z), ball, ball * 3f, 0f);
                    y -= ball * (y < lips[i] - 6f ? 1.05f : 0.7f);
                }
                // Torn notches into the lips, alternating sides.
                if (i % 3 == 0)
                {
                    float side = (i / 3) % 2 == 0 ? 1f : -1f;
                    float notch = Mathf.Max(1.2f, widths[i] * 0.45f);
                    world.Sculpt(TerrainSculptTool.Carve, line[i] + across * side * widths[i] * 0.85f + Vector3.down * notch * 0.2f, notch, notch * 2f, 0f);
                }
            }
            // Raised, torn lips.
            for (int i = 0; i < count; i += 2)
            {
                Vector3 across = Vector3.Cross(Vector3.up, Along(line, i));
                float f = 1f - Mathf.Abs(i / (float)(count - 1) * 2f - 1f);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 at = line[i] + across * side * (widths[i] + 1.4f + 0.5f * Mathf.Sin(i * 0.9f + side));
                    float g = Ground(world, at, lips[i] + 6f, terrain);
                    if (float.IsNaN(g) || g < lips[i] - 3f) continue;
                    world.Sculpt(TerrainSculptTool.Raise, new Vector3(at.x, g, at.z), 1.8f, 0.35f + 0.5f * f, 0f);
                }
            }
        }
        finally
        {
            world.EndSculptStroke();
            EditorUtility.ClearProgressBar();
        }

        // ---- Vents: on the inner world's floor under the crack (or on flat pads in the trench when it is shallow) ----
        var vents = new List<Vector3>();
        float[] ventAt = { -0.55f, 0.55f, -0.7f, 0.7f, -0.85f, 0.85f }; // away from the hole, where her neck and skull hang
        Physics.SyncTransforms();
        world.BeginSculptStroke();
        try
        {
            foreach (float u in ventAt)
            {
                int i = Mathf.Clamp(Mathf.RoundToInt((u + 1f) * 0.5f * (count - 1)), 0, count - 1);
                Vector3 p = line[i];
                float ceiling = ceilings[i];
                bool below = throughToInner && depths[i] > lips[i] - ceiling;
                float floor = below ? Ground(world, p, ceiling - 1f, terrain) : Ground(world, p, lips[i], terrain);
                if (float.IsNaN(floor)) continue;
                if (!below)
                {
                    floor = Mathf.Max(floor, lips[i] - depths[i]) + 0.2f;
                    world.Sculpt(TerrainSculptTool.Carve, new Vector3(p.x, floor + 2f, p.z), 3.2f, 8f, 0f);
                    for (int pass = 0; pass < 4; pass++)
                        world.Sculpt(TerrainSculptTool.Flatten, new Vector3(p.x, floor, p.z), 3.6f, 1f, floor);
                }
                vents.Add(new Vector3(p.x, floor, p.z));
            }
        }
        finally { world.EndSculptStroke(); }
        Physics.SyncTransforms();
        for (int v = 0; v < vents.Count; v++)
        {
            float g = Ground(world, vents[v], vents[v].y + 1.5f, terrain);
            if (!float.IsNaN(g)) vents[v] = new Vector3(vents[v].x, g, vents[v].z);
        }

        // ---- Backup of every changed sample, so Remove Ground Scar can restore it exactly ----
        var samples = new List<Vector3Int>();
        var values = new List<float>();
        var keys = new HashSet<Vector3Int>(old.Keys);
        foreach (var pair in data.Edits) keys.Add(pair.Key);
        foreach (Vector3Int key in keys)
        {
            old.TryGetValue(key, out float was);
            float now = data.Edits.TryGetValue(key, out float value) ? value : 0f;
            if (Mathf.Abs(now - was) < 1e-5f) continue;
            samples.Add(key);
            values.Add(was);
        }
        scar.SetShape(hole, mouth, heading, seed, line, widths, depths, lips, vents.ToArray());
        scar.SetBackup(samples.ToArray(), values.ToArray());
        scar.wallDecor = LoadDecor("ShellTentacles", "ShellCarpets", "NeedleBeards", "Crystals", "HangingVeils", "GlowSacs", "CavePebbles");
        scar.lipDecor = LoadDecor("Creepers", "Polyps", "Bladders", "Needles", "Mushrooms");
        EditorUtility.SetDirty(scar);
        EditorUtility.SetDirty(data);

        foreach (var pair in placedGround)
        {
            if (pair.Key == null) continue;
            Vector3 p = pair.Key.position;
            float g = Ground(world, p, pair.Value + 3f, terrain);
            if (float.IsNaN(g) || Mathf.Abs(g - pair.Value) < 0.05f) continue;
            Undo.RecordObject(pair.Key, "Carve Bone Mother Scar");
            pair.Key.position = new Vector3(p.x, p.y + g - pair.Value, p.z);
        }
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        AssetDatabase.SaveAssetIfDirty(data);
        EditorSceneManager.MarkSceneDirty(kiln.gameObject.scene);
        LandmarkScenePreview.BuildNow();
        Debug.Log("Bone mother scar carved over sinkhole " + hole + ": " + count + " points, " + vents.Count + " vents, " + samples.Count + " terrain samples changed.");
        return null;
    }

    private static void Remove(ProceduralWorld world, BoneScar scar)
    {
        TerrainSculptData data = world.SculptData;
        Undo.IncrementCurrentGroup();
        Undo.RegisterCompleteObjectUndo(data, "Remove Bone Mother Scar");
        Undo.RecordObject(scar, "Remove Bone Mother Scar");
        scar.GetBackup(out Vector3Int[] samples, out float[] values);
        for (int i = 0; i < samples.Length; i++) data.SetEdit(samples[i], values[i]);
        scar.ClearShape();
        EditorUtility.SetDirty(scar);
        EditorUtility.SetDirty(data);
        world.SyncSculptFromData();
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        AssetDatabase.SaveAssetIfDirty(data);
        Debug.Log("Bone mother scar removed: " + samples.Length + " terrain samples restored.");
    }

    /// <summary>
    /// What should keep its height above the ground: every placed piece (prefab roots, also inside groups) and every
    /// flower in the scene, wherever it sits in the hierarchy.
    /// </summary>
    private static List<Transform> GroundedThings(GameObject placedRoot)
    {
        var found = new HashSet<Transform>();
        if (placedRoot != null)
            foreach (Transform t in placedRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == placedRoot.transform) continue;
                if (t.parent == placedRoot.transform || PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) found.Add(t);
            }
        foreach (MonoBehaviour flower in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if ((flower is BubbleFlower || flower is FireFlower || flower is NectarFlower) && (flower.gameObject.hideFlags & HideFlags.DontSave) == 0)
                found.Add(flower.transform);
        // A group moved with its children would move them twice: keep only the outermost of nested picks.
        var result = new List<Transform>();
        foreach (Transform t in found)
        {
            bool inner = false;
            for (Transform up = t.parent; up != null && !inner; up = up.parent) inner = found.Contains(up) && up != (placedRoot != null ? placedRoot.transform : null);
            if (!inner) result.Add(t);
        }
        return result;
    }

    private static Vector3 Along(Vector3[] line, int i)
    {
        Vector3 d = line[Mathf.Min(i + 1, line.Length - 1)] - line[Mathf.Max(i - 1, 0)];
        d.y = 0f;
        return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.right;
    }

    /// <summary>Highest terrain below a height at a point, or NaN.</summary>
    private static float Ground(ProceduralWorld world, Vector3 p, float from, int mask)
    {
        float best = float.NaN;
        foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(p.x, from, p.z), Vector3.down, 80f, mask, QueryTriggerInteraction.Ignore))
            if (world.OwnsCollider(hit.collider) && (float.IsNaN(best) || hit.point.y > best)) best = hit.point.y;
        return best;
    }

    private static GameObject[] LoadDecor(params string[] kinds)
    {
        var found = new List<GameObject>();
        foreach (string kind in kinds)
            for (int v = 0; v < 8; v++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/WorldDecor/Decor_" + kind + "_" + v + ".prefab");
                if (prefab != null) found.Add(prefab);
            }
        return found.ToArray();
    }
}
