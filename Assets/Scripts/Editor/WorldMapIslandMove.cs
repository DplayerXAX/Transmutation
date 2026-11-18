using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Moves the crystal sky island in the saved Scene_World without a full rebuild (the scene carries hand-placed
/// content a rebuild would lose): the island's saved terrain samples are cleared where it was and grafted again at
/// <see cref="WorldMapTerrain.IslandOffset"/>, everything on the island moves with it, the floating-stone climb is
/// laid out again (WorldMapDress.Stones), the Rewind Coils on its landings follow their stones, the giant gem is
/// regrown under the new spot, and the terrain chunks are re-baked.
/// </summary>
public static class WorldMapIslandMove
{
    private const string IslandGroup = "08 Crystal sky island — Scene_CisternBasin (marcus)";
    /// <summary>Route stones the Rewind Coils stand on (the broad landings), in coil order.</summary>
    private static readonly int[] CoilStones = { 5, 12, 19, 26, 33, 40 };

    [MenuItem("Tools/Capstone/World Map/Move Sky Island To IslandOffset")]
    public static void MoveInOpenWorld()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        Scene scene = SceneManager.GetSceneByPath(WorldMapBuild.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(WorldMapBuild.ScenePath, OpenSceneMode.Additive);
        Transform root = scene.GetRootGameObjects().First(g => g.name == "World — combined map").transform;
        var world = scene.GetRootGameObjects().Select(r => r.GetComponent<ProceduralWorld>()).First(w => w != null);
        var log = new List<string>();

        Transform island = root.Find(IslandGroup);
        Vector3 from = island.position, to = WorldMapTerrain.IslandOffset, delta = to - from;
        if (delta.sqrMagnitude < 0.01f) { Debug.Log("Sky island is already at " + to); return; }

        // Terrain: nothing but the island lies 45 m up inside its graft box, so those samples are all island.
        WorldMapBuild.PrepareSampler(world);
        Vector3 min = world.GridMinimum; float cell = world.CellSize;
        float reach = WorldMapTerrain.IslandRadius * 1.2f + 2f;
        int cleared = 0;
        foreach (var key in world.SculptData.Edits.Keys.ToList())
        {
            Vector3 p = min + (Vector3)key * cell;
            if (p.y < 45f || Mathf.Abs(p.x - from.x) > reach || Mathf.Abs(p.z - from.z) > reach) continue;
            world.SculptData.SetEdit(key, 0f); cleared++;
        }
        EditorUtility.SetDirty(world.SculptData);
        log.Add("Sky island: " + cleared + " terrain samples cleared at " + from + ".");
        Vector3 r = new Vector3(WorldMapTerrain.IslandRadius * 1.2f, 0f, WorldMapTerrain.IslandRadius * 1.2f);
        WorldMapBuild.Graft(world, WorldMapBuild.CisternPath, to, new Vector3(-r.x, -76f, -r.z), new Vector3(r.x, 48f, r.z), WorldMapBuild.Island, log);
        AssetDatabase.SaveAssetIfDirty(world.SculptData);
        WorldMapBuild.PrepareSampler(world);

        // Everything standing on the island, and the world positions its creatures keep (Ore Colossus routes).
        island.position = to;
        log.Add("Sky island content moved by " + delta + "; " + ShiftStoredPositions(island, from, delta) + " stored positions shifted.");

        // The climb, and the coils on its landings.
        var coils = scene.GetRootGameObjects().Where(g => g.name.StartsWith("Rewind Coil (Sky Stone")).OrderBy(g => g.name).ToList();
        var stonesBefore = root.GetComponentInChildren<SkyIslands>(true);
        var offsets = coils.Select(c => (c.transform.position - Nearest(stonesBefore, c.transform.position, out _))).ToList();
        WorldMapDress.Stones(root, log);
        var islands = root.GetComponentInChildren<SkyIslands>(true);
        for (int i = 0; i < coils.Count && i < CoilStones.Length; i++)
        {
            int s = Mathf.Min(CoilStones[i], islands.stones.Length - 2);
            while (s > 0 && islands.scenery[s]) s--;
            Vector3 off = offsets[i]; Vector2 flat = new Vector2(off.x, off.z);
            flat = Vector2.ClampMagnitude(flat, islands.radii[s] * 0.45f);
            coils[i].transform.position = islands.stones[s] + new Vector3(flat.x, off.y, flat.y);
            EditorUtility.SetDirty(coils[i].transform);
        }
        log.Add("Rewind Coils set back on route stones " + string.Join(", ", CoilStones.Take(coils.Count)) + ".");

        world.Generate(true);
        WorldMapIslandCrystals.Build(root, world, log);
        WorldMapBuild.Cameras(root);
        WorldMapDress.BakeChunks(world, log);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log(string.Join("\n", log));
    }

    /// <summary>Moves serialized world positions (Vector3 fields, e.g. OreColossus.route) on the island's components
    /// that still point at the island's old spot: anything over its old footprint at about its height.</summary>
    public static int ShiftStoredPositions(Transform island, Vector3 from, Vector3 delta)
    {
        int shifted = 0;
        foreach (var c in island.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (c == null) continue;
            var so = new SerializedObject(c);
            var it = so.GetIterator();
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.Vector3) continue;
                Vector3 v = it.vector3Value;
                bool onOldIsland = Mathf.Abs(v.y - from.y) < 50f && v.magnitude > 20f &&
                    WorldMapBuild.IslandRadial(v.x - from.x, v.z - from.z) < WorldMapTerrain.IslandRadius + 10f;
                if (!onOldIsland) continue;
                it.vector3Value = v + delta; shifted++;
            }
            if (so.ApplyModifiedPropertiesWithoutUndo()) EditorUtility.SetDirty(c);
        }
        return shifted;
    }

    private static Vector3 Nearest(SkyIslands islands, Vector3 p, out int index)
    {
        index = 0; float best = float.MaxValue;
        for (int i = 0; i < islands.stones.Length; i++)
        {
            float d = Vector3.Distance(p, islands.stones[i]);
            if (d < best) { best = d; index = i; }
        }
        return islands.stones[index];
    }
}
