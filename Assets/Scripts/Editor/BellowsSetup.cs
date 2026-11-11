using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Places Bellows creatures in the open scene: a few beside heat (fire sources, fire flowers, the bone mother's
/// heart and scar vents) so they swell by themselves, and one near the player's start, empty. They go under
/// "Placed World Objects" so they can be moved like any placed piece. The creature itself is built in code at Play.
/// </summary>
public static class BellowsSetup
{
    private static void AddMenu()
    {
        int made = Add();
        EditorUtility.DisplayDialog("Bellows", made + " Bellows placed under '" + TerrainSculptWindow.PlacedRootName +
            "'.\n\nThey drink heat from fire bubbles and fire sources that touch them, swell, and float when over-full. " +
            "Bump one or left-click it and it sighs a stream of air out of its spout; carry one and left-click to puff along your view.\n\n" +
            "Save the scene to keep them.", "OK");
    }

    public static int Add()
    {
        var spots = new List<Vector3>();
        foreach (FireSource fire in Object.FindObjectsByType<FireSource>(FindObjectsSortMode.None)) spots.Add(fire.transform.position);
        foreach (FireFlower flower in Object.FindObjectsByType<FireFlower>(FindObjectsSortMode.None)) spots.Add(flower.transform.position);
        foreach (BoneScar scar in Object.FindObjectsByType<BoneScar>(FindObjectsSortMode.None))
            foreach (Vector3 vent in scar.VentPoints) spots.Add(vent);
        // Keep a few, spread out.
        var chosen = new List<Vector3>();
        foreach (Vector3 s in spots)
        {
            bool near = false;
            foreach (Vector3 c in chosen) if ((c - s).sqrMagnitude < 30f * 30f) { near = true; break; }
            if (!near) chosen.Add(s);
            if (chosen.Count >= 4) break;
        }
        var player = Object.FindFirstObjectByType<SmoothFirstPersonController>();
        GameObject root = GameObject.Find(TerrainSculptWindow.PlacedRootName);
        if (root == null)
        {
            root = new GameObject(TerrainSculptWindow.PlacedRootName);
            Undo.RegisterCreatedObjectUndo(root, "Add Bellows");
        }
        Undo.IncrementCurrentGroup();
        int made = 0;
        var random = new System.Random(7);
        for (int i = 0; i <= chosen.Count; i++)
        {
            bool atStart = i == chosen.Count;
            if (atStart && player == null) continue;
            Vector3 centre = atStart ? player.transform.position + player.transform.forward * 5f : chosen[i];
            float angle = (float)random.NextDouble() * 360f;
            Vector3 at = centre + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * (atStart ? 0f : 2.6f);
            if (Physics.Raycast(at + Vector3.up * 30f, Vector3.down, out RaycastHit hit, 80f, 1 << 7, QueryTriggerInteraction.Ignore)) at = hit.point;
            var go = new GameObject("Bellows");
            Undo.RegisterCreatedObjectUndo(go, "Add Bellows");
            go.transform.SetParent(root.transform, true);
            // Facing away from the heat, so its stream blows outwards.
            go.transform.SetPositionAndRotation(at + Vector3.up * 0.6f, Quaternion.Euler(0f, angle, 0f));
            go.AddComponent<Rigidbody>();
            go.AddComponent<SphereCollider>().radius = 0.45f;
            go.AddComponent<BellowsCreature>();
            go.AddComponent<SettleWhenGroundReady>().lift = 0.55f;
            made++;
        }
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        if (made > 0) EditorSceneManager.MarkSceneDirty(root.scene);
        return made;
    }
}
