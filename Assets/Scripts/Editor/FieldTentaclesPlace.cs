using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Request-file helper (no menu): write "place" to Temp/FieldTentacles.request with Scene_World open, and four walking
/// tentacle creatures are put in the flower field at irregular spots (group "05d Flower field tentacles", replaced if
/// it exists), set on the terrain below (collider ray); in Play they follow the ground themselves.
/// "snap" only re-grounds the ones already there. The scene is marked modified, not saved.
/// </summary>
[InitializeOnLoad]
public static class FieldTentaclesPlace
{
    private const string Request = "Temp/FieldTentacles.request", Status = "Temp/FieldTentacles.status";
    private const string Prefab = "Assets/Prefabs/Creatures/TentacleCreature.prefab";
    private const string GroupName = "05d Flower field tentacles";

    // Scattered over the field (centre about -55, 45), deliberately uneven.
    private static readonly Vector3[] Spots =
    {
        new Vector3(-36.5f, 0f, 19.2f),
        new Vector3(-74.3f, 0f, 61.8f),
        new Vector3(-58.1f, 0f, 88.4f),
        new Vector3(-12.6f, 0f, 52.7f),
    };
    private static readonly float[] Turns = { 37f, 211f, 128f, 302f };

    static FieldTentaclesPlace() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try { File.WriteAllText(Status, command == "snap" ? Snap() : Place()); }
        catch (System.Exception e) { File.WriteAllText(Status, "error: " + e); }
    }

    private static string Place()
    {
        var scene = EditorSceneManager.GetActiveScene();
        Transform root = WorldStreaming.FindRoot(scene);
        if (root == null) return "error: open Scene_World first (active: " + scene.path + ")";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        if (prefab == null) return "error: prefab missing " + Prefab;
        Transform old = root.Find(GroupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var group = new GameObject(GroupName).transform;
        group.SetParent(root, false);

        var tiles = Object.FindObjectsByType<FloraPatch>(FindObjectsSortMode.None);
        var report = new System.Text.StringBuilder("placed:");
        for (int i = 0; i < Spots.Length; i++)
        {
            Vector3 at = Spots[i];
            at.y = Ground(GroundGuess(tiles, at), at);
            var creature = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            creature.name = "Field Tentacle " + (i + 1);
            creature.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, Turns[i], 0f));
            // No SettleWhenGroundReady: tentacles find and follow the ground themselves, and its handing the body
            // back to physics left them stuck in the air.
            report.Append($" ({at.x:0.0}, {at.y:0.0}, {at.z:0.0})");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        return report.ToString();
    }

    private static string Snap()
    {
        var scene = EditorSceneManager.GetActiveScene();
        Transform root = WorldStreaming.FindRoot(scene);
        Transform group = root != null ? root.Find(GroupName) : null;
        if (group == null) return "error: no " + GroupName;
        var tiles = Object.FindObjectsByType<FloraPatch>(FindObjectsSortMode.None);
        var report = new System.Text.StringBuilder("snapped:");
        foreach (Transform creature in group)
        {
            Vector3 at = creature.position;
            float before = at.y;
            at.y = Ground(GroundGuess(tiles, at), at);
            creature.position = at;
            var settle = creature.GetComponent<SettleWhenGroundReady>();
            if (settle != null) Object.DestroyImmediate(settle);
            report.Append($" ({at.x:0.0}, {before:0.0} -> {at.y:0.0}, {at.z:0.0})");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        return report.ToString();
    }

    // Highest terrain hit under a point a little above the guess (the sky island is far higher, so it is not hit).
    private static float Ground(float guess, Vector3 at)
    {
        float best = float.MinValue;
        foreach (RaycastHit hit in Physics.RaycastAll(new Vector3(at.x, guess + 25f, at.z), Vector3.down, 80f, 1 << 7, QueryTriggerInteraction.Ignore))
            best = Mathf.Max(best, hit.point.y);
        if (best > float.MinValue) return best + 1.0f;
        // No terrain colliders in edit mode: read the world's density instead, searching down from above the guess.
        var world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world != null && world.LayoutReady && world.TryFindSurfaceBelow(new Vector3(at.x, guess + 25f, at.z), out float y)) return y + 1.0f;
        return WorldMapTerrain.Height(at.x, at.z) + 1.0f;
    }

    // The meadow tiles sit on the field's ground, so the nearest one gives the height to start from.
    private static float GroundGuess(FloraPatch[] tiles, Vector3 at)
    {
        float best = float.MaxValue, y = 0f;
        foreach (FloraPatch tile in tiles)
        {
            Vector3 p = tile.transform.position;
            float d = new Vector2(p.x - at.x, p.z - at.z).sqrMagnitude;
            if (d < best) { best = d; y = p.y; }
        }
        return y;
    }
}
