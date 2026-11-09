using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Scene touch-ups round the bone mother, for whichever scene is open:
/// - Remove Time Ripples: hides the Leaf Map's "time tide" rings spreading over the ground round her (kept, switched off).
/// - Add Near And Far Decor: a few huge pieces of the world's decor standing far off as silhouettes (depth behind her),
///   and small clusters close round her and her crack (detail in front of the camera). They are plain decor prefab
///   instances under "Placed World Objects/Bone Mother Vista", so they can be moved or deleted one by one;
///   running it again replaces the group.
/// </summary>
public static class BoneMotherVistaSetup
{
    private const string Menu = "Tools/Capstone/Landmarks/Bone Mother/";
    private const string GroupName = "Bone Mother Vista";

    private static void RemoveRipples()
    {
        int off = RemoveRipplesInScene();
        EditorUtility.DisplayDialog("Time Ripples", off > 0 ? off + " time ripple object(s) switched off (not deleted). Save the scene to keep it." : "No time ripples found in the open scene.", "OK");
    }

    /// <summary>Switches off the time ripple objects in the open scene. Returns how many.</summary>
    public static int RemoveRipplesInScene()
    {
        int off = 0;
        Material ripples = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/HabitatBlock/HB_FX_TimeRipples.mat");
        foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            bool tide = r.name.Contains("Time tide") || r.transform.parent != null && r.transform.parent.name.Contains("Ground tide");
            bool usesRipples = ripples != null && System.Array.IndexOf(r.sharedMaterials, ripples) >= 0;
            if (!tide && !usesRipples) continue;
            Undo.RecordObject(r.gameObject, "Remove Time Ripples");
            r.gameObject.SetActive(false);
            off++;
        }
        if (off > 0) EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        return off;
    }

    private static void AddMenu()
    {
        string problem = AddVista(out int near, out int far);
        EditorUtility.DisplayDialog("Bone Mother Vista", problem ?? (far + " far silhouettes and " + near + " near pieces placed under '" +
            TerrainSculptWindow.PlacedRootName + "/" + GroupName + "'.\n\nMove or delete any of them freely; run the menu again to replace the group. Save the scene to keep it."), "OK");
    }

    public static string AddVista(out int near, out int far)
    {
        near = far = 0;
        BoneKiln kiln = null;
        foreach (BoneKiln k in Object.FindObjectsByType<BoneKiln>(FindObjectsSortMode.None))
            if ((k.gameObject.hideFlags & HideFlags.DontSave) == 0) { kiln = k; break; }
        if (kiln == null) return "No Bone Kiln in the open scene.";
        LandmarkScenePreview.BuildNow();
        BoneKiln built = LandmarkScenePreview.PreviewOf<BoneKiln>();
        Vector3 centre = built != null && built.Root != null ? built.Root.position : kiln.transform.position;
        Vector3 heading = built != null && built.Root != null ? Vector3.ProjectOnPlane(built.Root.forward, Vector3.up).normalized : Vector3.forward;
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null) return "No ProceduralWorld in the open scene.";
        if (!world.LayoutReady) TerrainSculptWindow.ShowTerrain(world);

        GameObject placed = GameObject.Find(TerrainSculptWindow.PlacedRootName);
        if (placed == null) { placed = new GameObject(TerrainSculptWindow.PlacedRootName); Undo.RegisterCreatedObjectUndo(placed, "Bone Mother Vista"); }
        Undo.IncrementCurrentGroup();
        Transform old = placed.transform.Find(GroupName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var group = new GameObject(GroupName).transform;
        Undo.RegisterCreatedObjectUndo(group.gameObject, "Bone Mother Vista");
        group.SetParent(placed.transform, false);

        var random = new System.Random(23);
        int terrain = 1 << world.TerrainLayer;
        bool Ground(Vector3 p, out RaycastHit hit)
        {
            hit = default;
            float best = float.MinValue;
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(p.x, centre.y + 150f, p.z), Vector3.down, 400f, terrain, QueryTriggerInteraction.Ignore))
                if (world.OwnsCollider(h.collider) && h.point.y > best) { best = h.point.y; hit = h; }
            return best > float.MinValue;
        }
        float R(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());

        // ---- Far: tall silhouettes on the skyline, mostly behind her as seen from her children's side ----
        GameObject[] tall = Load("Obelisks", "Arches", "Halos", "Totems", "Cages");
        for (int n = 0, tries = 0; n < 9 && tries < 120; tries++)
        {
            float angle = R(-110f, 110f) + (n % 3 == 0 ? 180f : 0f);
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * heading;
            Vector3 p = centre + dir * R(70f, 140f);
            if (!InsideMap(world, p) || !Ground(p, out RaycastHit hit) || hit.normal.y < 0.75f || BoneScar.Covers(hit.point, 6f)) continue;
            if (NearOther(group, hit.point, 25f)) continue;
            Place(tall[random.Next(tall.Length)], group, hit.point, Quaternion.Euler(0f, R(0f, 360f), 0f), R(4f, 8.5f), "Far");
            n++; far++;
        }

        // ---- Near: small clusters round her and along her crack, on its lips but never in it ----
        GameObject[] small = Load("Shards", "Cairns", "Fronds", "Needles", "Polyps", "Mushrooms", "Creepers", "Bladders", "Pebbles");
        for (int cluster = 0, tries = 0; cluster < 10 && tries < 200; tries++)
        {
            Vector3 dir = Quaternion.Euler(0f, R(0f, 360f), 0f) * Vector3.forward;
            Vector3 c = centre + dir * R(9f, 28f);
            if (!Ground(c, out RaycastHit seat) || seat.normal.y < 0.7f || BoneScar.Covers(seat.point, 2.5f) || Mathf.Abs(seat.point.y - centre.y) > 12f) continue;
            GameObject kind = small[random.Next(small.Length)];
            int members = random.Next(3, 7);
            for (int m = 0; m < members; m++)
            {
                Vector3 p = seat.point + Quaternion.Euler(0f, R(0f, 360f), 0f) * Vector3.forward * R(0f, 2.8f);
                if (!Ground(p, out RaycastHit hit) || hit.normal.y < 0.6f || BoneScar.Covers(hit.point, 1.5f)) continue;
                Quaternion lean = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, hit.normal, 0.5f));
                Place(kind, group, hit.point, lean * Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.7f, 1.6f), "Near");
                near++;
            }
            cluster++;
        }
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        EditorSceneManager.MarkSceneDirty(group.gameObject.scene);
        return null;
    }

    private static bool InsideMap(ProceduralWorld world, Vector3 p)
    {
        Vector2 half = world.WorldSize * 0.5f * 0.9f;
        Vector3 local = p - world.transform.position;
        return Mathf.Abs(local.x) < half.x && Mathf.Abs(local.z) < half.y;
    }

    private static bool NearOther(Transform group, Vector3 p, float distance)
    {
        foreach (Transform t in group) if ((t.position - p).sqrMagnitude < distance * distance) return true;
        return false;
    }

    private static void Place(GameObject prefab, Transform parent, Vector3 at, Quaternion rotation, float scale, string label)
    {
        if (prefab == null) return;
        var made = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
        Undo.RegisterCreatedObjectUndo(made, "Bone Mother Vista");
        made.transform.SetParent(parent, true);
        // Sunk a little, so wide pieces on uneven ground do not hover at one corner.
        made.transform.SetPositionAndRotation(at - rotation * Vector3.up * (0.08f * scale), rotation);
        made.transform.localScale = prefab.transform.localScale * scale;
        made.name = prefab.name + " (" + label + ")";
    }

    private static GameObject[] Load(params string[] kinds)
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
