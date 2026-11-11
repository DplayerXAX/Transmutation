using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Places SkyIslands over the open scene's flower field (the LilyField; else round the player): a spiral of floating
/// stones, each one jump on from the last and a little higher, up to an altar island with a resting place for meditating.
/// Running it again replaces the earlier set. The object can be moved or deleted like any placed piece.
/// </summary>
public static class SkyIslandsSetup
{
    private const string Name = "Sky Islands";

    private static void AddMenu()
    {
        string problem = Add(out SkyIslands islands);
        if (problem != null) { EditorUtility.DisplayDialog("Sky Islands", problem, "OK"); return; }
        Selection.activeObject = islands;
        EditorUtility.DisplayDialog("Sky Islands", "Floating islands rise unevenly over the field to an altar island.\n\nThe gaps are too wide to jump: " +
            "bubble flowers on each island fire in turn, and their bubbles hang in the gaps to hop through. At the top, sit at the altar " +
            "(right click) and meditate.\n\nStone positions are on the Sky Islands component (edit them and the Scene view follows). Save the scene to keep it.", "OK");
    }

    public static string Add(out SkyIslands islands)
    {
        islands = null;
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null) return "No ProceduralWorld in the open scene.";
        if (!world.LayoutReady) TerrainSculptWindow.ShowTerrain(world);
        int terrain = 1 << world.TerrainLayer;

        Bounds field;
        LilyField lilies = Object.FindFirstObjectByType<LilyField>();
        if (lilies == null || !lilies.TryFieldBounds(out field))
        {
            var player = Object.FindFirstObjectByType<SmoothFirstPersonController>();
            if (player == null) return "No flower field (LilyField) or player found to place the islands over.";
            field = new Bounds(player.transform.position + player.transform.forward * 30f, new Vector3(50f, 0f, 50f));
        }

        float Ground(Vector3 p)
        {
            float best = float.NaN;
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(p.x, field.max.y + 200f, p.z), Vector3.down, 500f, terrain, QueryTriggerInteraction.Ignore))
                if (world.OwnsCollider(h.collider) && (float.IsNaN(best) || h.point.y > best)) best = h.point.y;
            return float.IsNaN(best) ? field.center.y : best;
        }

        // ---- Islands too far apart to jump, rising unevenly over the field; bubble flowers on each carry the player across ----
        // A wandering climb (not a ring): each island 6-10 m beyond the last edge to edge, 3-7 m higher, turning by
        // a random amount round the field's middle. Sizes vary from a ledge to a meadow.
        Vector3 centre = field.center;
        var random = new System.Random(5);
        float R(float lo, float hi) => Mathf.Lerp(lo, hi, (float)random.NextDouble());
        var stones = new List<Vector3>();
        var radii = new List<float>();
        var scenery = new List<bool>();
        float orbit = Mathf.Clamp(Mathf.Min(field.extents.x, field.extents.z) * 0.45f, 12f, 24f);
        float angle = R(0f, 6.28f);
        Vector3 first = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * orbit;
        first.y = Ground(first) + R(6f, 7.5f);
        stones.Add(first); radii.Add(R(3f, 4.2f)); scenery.Add(false);
        const int path = 9;
        for (int i = 1; i < path; i++)
        {
            Vector3 last = stones[stones.Count - 1];
            float r = R(2.2f, 5.2f);
            float gap = R(6f, 10f);
            // Heading: round the middle, with a random swing; pulled back in if it strays out of the field.
            Vector3 toCentre = centre - last; toCentre.y = 0f;
            Vector3 tangent = Vector3.Cross(Vector3.up, toCentre.normalized);
            float drift = toCentre.magnitude > orbit * 1.3f ? 0.6f : toCentre.magnitude < orbit * 0.5f ? -0.6f : 0f;
            Vector3 dir = Quaternion.Euler(0f, R(-55f, 55f), 0f) * (tangent + toCentre.normalized * drift).normalized;
            Vector3 p = last + dir * (radii[radii.Count - 1] + gap + r);
            p.y = Mathf.Max(last.y + R(3f, 7f), Ground(p) + 6f);
            stones.Add(p); radii.Add(r); scenery.Add(false);
        }
        // The altar island: wide, one more crossing up.
        {
            Vector3 last = stones[stones.Count - 1];
            Vector3 toCentre = centre - last; toCentre.y = 0f;
            Vector3 dir = Quaternion.Euler(0f, R(-30f, 30f), 0f) * toCentre.normalized;
            Vector3 p = last + dir * (radii[radii.Count - 1] + R(6f, 8f) + 6f);
            p.y = last.y + R(4f, 6f);
            stones.Add(p); radii.Add(6f); scenery.Add(false);
        }
        // Drifting scenery rocks round the climb: not on the way, small and at odd heights, so the sky is not a staircase.
        float low = stones[0].y, high = stones[stones.Count - 1].y;
        for (int k = 0, tries = 0; k < 10 && tries < 80; tries++)
        {
            float a = R(0f, 6.28f);
            Vector3 p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * R(orbit * 0.3f, orbit * 1.8f);
            p.y = Mathf.Max(R(low - 2f, high + 10f), Ground(p) + 5f);
            float r = R(0.8f, 2.2f);
            bool clear = true;
            foreach (Vector3 q in stones) if ((q - p).magnitude < r + 9f) { clear = false; break; }
            if (!clear) continue;
            stones.Insert(0, p); radii.Insert(0, r); scenery.Insert(0, true);
            k++;
        }

        // ---- Object and look ----
        GameObject placed = GameObject.Find(TerrainSculptWindow.PlacedRootName);
        if (placed == null) { placed = new GameObject(TerrainSculptWindow.PlacedRootName); Undo.RegisterCreatedObjectUndo(placed, "Sky Islands"); }
        Undo.IncrementCurrentGroup();
        Transform old = placed.transform.Find(Name);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var go = new GameObject(Name);
        Undo.RegisterCreatedObjectUndo(go, "Sky Islands");
        go.transform.SetParent(placed.transform, false);
        go.transform.position = new Vector3(centre.x, field.center.y, centre.z);
        go.SetActive(false);
        islands = go.AddComponent<SkyIslands>();
        islands.stones = stones.ToArray();
        islands.radii = radii.ToArray();
        islands.scenery = scenery.ToArray();
        Material ground = null;
        foreach (MeshRenderer r in world.GetComponentsInChildren<MeshRenderer>(true))
            if (r.sharedMaterials.Length > 0 && r.sharedMaterials[0] != null) { ground = r.sharedMaterials[0]; break; }
        islands.topMaterial = ground;
        islands.underMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_World_Inner.mat");
        islands.altarGlow = GlowMaterial();
        islands.topDecor = Load("Fronds", "Mushrooms", "Polyps", "Needles", "Pebbles");
        islands.hangingDecor = Load("HangingVeils", "NeedleBeards", "GlowSacs");
        islands.altarStones = Load("Obelisks", "Totems");
        go.SetActive(true);

        // Bubble flowers: two on each island's edge facing the next, and two on the ground under the first island.
        // A BubbleCascade fires them in turn, so a bubble always hangs somewhere in each gap to hop through.
        var flowerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Generated/LeafMap/MeadowFlower.prefab")
                           ?? AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/BubbleFlower.prefab");
        var flowers = new List<BubbleFlower>();
        var flowerGroup = new GameObject("Island Bubble Flowers").transform;
        flowerGroup.SetParent(go.transform, false);
        var pathIndex = new List<int>();
        var lanes = new List<SkyIslandBubbleGuide.Lane>();
        for (int i = 0; i < stones.Count; i++) if (!scenery[i]) pathIndex.Add(i);
        void Flower(Vector3 at)
        {
            if (flowerPrefab == null) return;
            var made = (GameObject)PrefabUtility.InstantiatePrefab(flowerPrefab, flowerGroup);
            made.transform.position = at + Vector3.up * 0.02f;
            made.name = "Island bubble flower " + (flowers.Count + 1);
            var flower = made.GetComponent<BubbleFlower>();
            if (flower == null) return;
            flower.ConfigureGroundEmission(false, new Vector2(6f, 10f));
            EditorUtility.SetDirty(flower);
            flowers.Add(flower);
        }
        {
            Vector3 firstStone = stones[pathIndex[0]];
            for (int k = 0; k < 2; k++)
            {
                Vector3 at = firstStone + new Vector3(R(-3f, 3f), 0f, R(-3f, 3f)) + (centre - firstStone).normalized * 2f;
                at.y = Ground(at);
                Flower(at);
            }
        }
        for (int n = 0; n < pathIndex.Count - 1; n++)
        {
            Vector3 here = stones[pathIndex[n]], next = stones[pathIndex[n + 1]];
            Vector3 toward = next - here; toward.y = 0f; toward.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, toward);
            float r = radii[pathIndex[n]];
            float gapStart = r, gapEnd = new Vector2(next.x - here.x, next.z - here.z).magnitude - radii[pathIndex[n + 1]];
            for (int k = 0; k < 2; k++)
            {
                int before = flowers.Count;
                Flower(here + toward * r * 0.6f + side * r * (k == 0 ? 0.35f : -0.35f) + Vector3.up * 0.08f);
                if (flowers.Count == before) continue;
                // Its bubbles hang a third and two thirds of the way across the gap.
                float along = Mathf.Lerp(gapStart, gapEnd, k == 0 ? 0.33f : 0.7f);
                lanes.Add(new SkyIslandBubbleGuide.Lane { flower = flowers[flowers.Count - 1], target = here + toward * along + side * (k == 0 ? 0.8f : -0.8f) });
            }
        }
        if (flowers.Count > 0) flowerGroup.gameObject.AddComponent<BubbleCascade>().Configure(flowers, 7f);
        flowerGroup.gameObject.AddComponent<SkyIslandBubbleGuide>().lanes = lanes;
        var ecology = Object.FindFirstObjectByType<LeafMapEcology>();
        if (ecology != null) foreach (BubbleFlower f in flowers) ecology.RegisterFlower(f);
        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        EditorSceneManager.MarkSceneDirty(go.scene);
        return null;
    }

    private static Material GlowMaterial()
    {
        const string path = "Assets/Materials/World/MAT_SkyAltarGlow.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        material = new Material(shader) { name = "MAT_SkyAltarGlow" };
        var ice = new Color(0.55f, 0.85f, 1.6f);
        material.SetColor("_BaseColor", ice);
        material.SetColor("_Color", ice);
        AssetDatabase.CreateAsset(material, path);
        return material;
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

    /// <summary>For batch tests: adds the islands to the scene at path and saves it (only in an isolated batch copy).</summary>
    public static void AddForTest()
    {
        if (!Application.isBatchMode) throw new System.InvalidOperationException("Run in an isolated batch project.");
        string scene = "Assets/Scenes/Scene_Daniel.unity";
        string[] args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, "-skyScene");
        if (i >= 0 && i + 1 < args.Length) scene = args[i + 1];
        EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
        string problem = Add(out SkyIslands islands);
        if (problem != null) throw new System.InvalidOperationException(problem);
        Debug.Log("[SkyIslands] " + islands.stones.Length + " stones from " + islands.stones[0] + " to " + islands.AltarPoint);
        EditorSceneManager.SaveScene(islands.gameObject.scene);
    }
}
