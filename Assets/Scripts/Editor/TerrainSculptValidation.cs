using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Batch check for the saved, sculptable terrain: brushes move the ground, heights and raycasts follow,
/// edits survive saving and regenerating, the frozen layout ignores later inspector changes, and clearing
/// the edits restores the baked ground. Run with -executeMethod TerrainSculptValidation.Run.
/// </summary>
public static class TerrainSculptValidation
{
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Terrain sculpt: " + message);
            Debug.Log("Terrain sculpt OK: " + message);
            checks++;
        }

        var go = new GameObject("World");
        var world = go.AddComponent<ProceduralWorld>();
        var settings = new SerializedObject(world);
        settings.FindProperty("worldSize").vector2Value = new Vector2(96f, 96f);
        settings.FindProperty("sinkholeCount").intValue = 1;
        settings.ApplyModifiedPropertiesWithoutUndo();

        var data = ScriptableObject.CreateInstance<TerrainSculptData>();
        string layout = world.CaptureLayout(out float[] layoutChecks);
        data.Freeze(layout, layoutChecks, "validation");
        settings.FindProperty("sculptData").objectReferenceValue = data;
        settings.ApplyModifiedPropertiesWithoutUndo();
        world.Generate(true);
        Check(world.CanSculpt && world.GenerationComplete, "baked world builds in edit mode");

        Vector3 point = new Vector3(25f, 0f, 10f);
        world.ColumnHeights(point, out float before, out float ceilingBefore, out _);
        Check(GroundAt(world, point, out float hitBefore) && Mathf.Abs(hitBefore - before) < 6f, "generated column height matches the mesh");

        // Raise a mound.
        world.BeginSculptStroke();
        // Like the window: every dab is centred on the ground the mouse ray hits now.
        for (int i = 0; i < 6; i++)
            if (GroundAt(world, point, out float top)) world.Sculpt(TerrainSculptTool.Raise, new Vector3(point.x, top, point.z), 6f, 1f, 0f);
        world.EndSculptStroke();
        world.ColumnHeights(point, out float raised, out _, out _);
        Check(raised > hitBefore + 4f, "Raise lifts the column height (" + hitBefore.ToString("F2") + " -> " + raised.ToString("F2") + ")");
        Check(GroundAt(world, point, out float hitRaised) && Mathf.Abs(hitRaised - raised) < 0.75f, "the rebuilt collider follows the raised ground");
        Check(data.Edits.Count > 0 && world.SculptedSampleCount == data.Edits.Count, "edits are stored in the asset");

        // Carve a ball out of the mound.
        Vector3 hole = new Vector3(point.x, raised - 2f, point.z);
        world.Sculpt(TerrainSculptTool.Carve, hole, 2.5f, 8f, 0f);
        Check(world.SampleDensity(hole) < 0f, "Carve empties the rock");

        // Smooth, Add and Lower must run without errors.
        world.Sculpt(TerrainSculptTool.Smooth, new Vector3(point.x, raised, point.z), 5f, 0.8f, 0f);
        world.Sculpt(TerrainSculptTool.Add, new Vector3(point.x + 8f, before + 1f, point.z), 2f, 3f, 0f);
        world.Sculpt(TerrainSculptTool.Lower, new Vector3(point.x - 8f, before, point.z), 3f, 1f, 0f);
        Check(true, "Smooth, Add and Lower run");

        // Flatten a second spot to a fixed height.
        Vector3 flat = new Vector3(-20f, 0f, -15f);
        world.ColumnHeights(flat, out float flatBefore, out _, out _);
        float target = flatBefore + 2f;
        for (int i = 0; i < 12; i++) world.Sculpt(TerrainSculptTool.Flatten, new Vector3(flat.x, target, flat.z), 6f, 0.6f, target);
        world.ColumnHeights(flat, out float flattened, out _, out _);
        Check(Mathf.Abs(flattened - target) < 0.6f, "Flatten reaches the chosen height (" + flattened.ToString("F2") + " vs " + target.ToString("F2") + ")");

        // Saving and loading the asset keeps every edit.
        world.ColumnHeights(point, out float sculpted, out _, out _);
        string json = EditorJsonUtility.ToJson(data);
        var copy = ScriptableObject.CreateInstance<TerrainSculptData>();
        EditorJsonUtility.FromJsonOverwrite(json, copy);
        bool same = copy.Edits.Count == data.Edits.Count;
        foreach (var pair in data.Edits) same &= copy.Edits.TryGetValue(pair.Key, out float value) && value == pair.Value;
        Check(same && copy.Layout == data.Layout, "packed edits load back exactly (" + data.Edits.Count + " samples, " + json.Length + " chars)");

        // Inspector changes are ignored while a saved terrain is assigned.
        settings.Update();
        settings.FindProperty("seed").intValue = 999;
        settings.FindProperty("surfaceAmplitude").floatValue = 5f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        world.Generate(true);
        world.ColumnHeights(point, out float regenerated, out _, out _);
        Check(Mathf.Abs(regenerated - sculpted) < 0.01f, "regenerating keeps the sculpted, frozen terrain");
        Check(GroundAt(world, point, out float hitRegenerated) && Mathf.Abs(hitRegenerated - sculpted) < 0.75f, "regenerated collider keeps the mound");

        // Clearing (as an undo would) brings back the baked ground.
        data.ClearEdits();
        world.SyncSculptFromData();
        world.ColumnHeights(point, out float cleared, out float ceilingCleared, out _);
        Check(Mathf.Abs(cleared - before) < 0.01f && Mathf.Abs(ceilingCleared - ceilingBefore) < 0.01f, "clearing edits restores the baked heights");
        Check(GroundAt(world, point, out float hitCleared) && Mathf.Abs(hitCleared - hitBefore) < 0.05f, "clearing edits restores the baked collider");

        Object.DestroyImmediate(go);
        Object.DestroyImmediate(data);
        Object.DestroyImmediate(copy);
        Debug.Log("Terrain sculpt validation passed: " + checks + " checks.");
    }

    /// <summary>Logs build and brush times on a full-size default world (-executeMethod TerrainSculptValidation.Timing).</summary>
    public static void Timing()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("World");
        var world = go.AddComponent<ProceduralWorld>();
        var data = ScriptableObject.CreateInstance<TerrainSculptData>();
        data.Freeze(world.CaptureLayout(out float[] layoutChecks), layoutChecks, "timing");
        var settings = new SerializedObject(world);
        settings.FindProperty("sculptData").objectReferenceValue = data;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        world.Generate(true);
        Debug.Log("Terrain sculpt timing: full world build " + timer.ElapsedMilliseconds + " ms");
        Vector3 point = new Vector3(40f, 0f, 30f);
        world.BeginSculptStroke();
        for (int i = 0; i < 5; i++)
        {
            GroundAt(world, point, out float top);
            timer.Restart();
            world.Sculpt(TerrainSculptTool.Raise, new Vector3(point.x, top, point.z), 8f, 0.3f, 0f);
            Debug.Log("Terrain sculpt timing: dab " + i + " (radius 8) " + timer.ElapsedMilliseconds + " ms");
        }
        timer.Restart();
        world.EndSculptStroke();
        Debug.Log("Terrain sculpt timing: stroke end " + timer.ElapsedMilliseconds + " ms");
        timer.Restart();
        for (int i = 0; i < 100; i++) world.ColumnHeights(point + Vector3.right * (i * 0.05f), out _, out _, out _);
        Debug.Log("Terrain sculpt timing: 100 sculpted ColumnHeights " + timer.ElapsedMilliseconds + " ms");
        Object.DestroyImmediate(go);
        Object.DestroyImmediate(data);
    }

    private static bool GroundAt(ProceduralWorld world, Vector3 point, out float y)
    {
        y = float.MinValue;
        Physics.SyncTransforms();
        foreach (RaycastHit hit in Physics.RaycastAll(point + Vector3.up * 300f, Vector3.down, 600f, 1 << world.TerrainLayer))
            if (world.OwnsCollider(hit.collider)) y = Mathf.Max(y, hit.point.y);
        return y > float.MinValue;
    }
}
