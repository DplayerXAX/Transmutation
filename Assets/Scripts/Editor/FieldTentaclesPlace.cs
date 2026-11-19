using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Request-file helper (no menu): write "place" to Temp/FieldTentacles.request with Scene_World open, and four walking
/// tentacle creatures are put in the flower field at irregular spots (group "05d Flower field tentacles", replaced if
/// it exists). Each waits for its ground (SettleWhenGroundReady). The scene is marked modified, not saved.
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
        File.Delete(Request);
        try { File.WriteAllText(Status, Place()); }
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
            at.y = GroundGuess(tiles, at) + 1.5f;
            var creature = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
            creature.name = "Field Tentacle " + (i + 1);
            creature.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, Turns[i], 0f));
            if (creature.GetComponent<SettleWhenGroundReady>() == null) creature.AddComponent<SettleWhenGroundReady>().lift = 1f;
            report.Append($" ({at.x:0.0}, {at.y:0.0}, {at.z:0.0})");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        return report.ToString();
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
