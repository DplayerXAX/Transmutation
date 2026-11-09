using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The west tentacle cliff of the Leaf Map as a small ecosystem (Daniel 2026-11-09), placed in the open scene:
/// - Springs on the plateau rim: erosive water (the Ore Colossus water) runs down the cliff face, slowly wearing grooves
///   and grips into it, and gathers at the foot. This water does not age creatures and there is no rain.
/// - Pools at the foot: wet growths, and goo creatures (the climbing supply) waiting there.
/// - Birds nest in the cliff and fly along it; tentacle creatures cling beside the falls; sky spiders hang webs across
///   the gullies between them (webs catch birds and make shortcuts).
/// Everything goes under "03b Cliff ecosystem" (replaced when run again). Tools > Capstone > Leaf Map > Cliff Ecosystem.
/// </summary>
public static class LeafCliffEcosystemSetup
{
    private const string GroupName = "03b Cliff ecosystem — springs, birds, webs";

    private static void Menu()
    {
        string problem = Build(out string report);
        EditorUtility.DisplayDialog("Cliff Ecosystem", problem ?? report + "\n\nSave the scene to keep it.", "OK");
    }

    public static string Build(out string report)
    {
        report = null;
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null) return "No ProceduralWorld in the open scene.";
        if (world.UsingSculptedTerrain) LeafMapTerrain.Scale = LeafMapTerrain.ReadScale(world.SculptData.Layout);
        if (!world.LayoutReady) TerrainSculptWindow.ShowTerrain(world);
        int terrain = 1 << world.TerrainLayer;

        float Ground(Vector2 p, out Vector3 normal)
        {
            normal = Vector3.up;
            float best = float.NaN;
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(p.x, 300f, p.y), Vector3.down, 700f, terrain, QueryTriggerInteraction.Ignore))
                if (world.OwnsCollider(h.collider) && (float.IsNaN(best) || h.point.y > best)) { best = h.point.y; normal = h.normal; }
            return best;
        }

        // ---- Find the rim: walking east (towards the meadow) from the plateau, where the ground falls away ----
        var rims = new List<(Vector3 top, Vector3 foot, Vector3 down)>();
        for (float dz = -20f; dz <= 60f; dz += 4f)
        {
            Vector3 lastTop = Vector3.zero;
            for (float dx = -85f; dx < -20f; dx += 1f)
            {
                Vector2 a = LeafMapTerrain.ToWorld(new Vector2(dx, dz)), b = LeafMapTerrain.ToWorld(new Vector2(dx + 6f, dz));
                float ha = Ground(a, out _), hb = Ground(b, out _);
                if (float.IsNaN(ha) || float.IsNaN(hb)) continue;
                if (ha - hb > 8f)
                {
                    // The foot: keep walking until the ground levels out.
                    Vector2 f = b;
                    float hf = hb;
                    for (int k = 0; k < 12; k++)
                    {
                        Vector2 n = f + (b - a).normalized * 2f;
                        float hn = Ground(n, out _);
                        if (float.IsNaN(hn) || hf - hn < 0.4f) break;
                        f = n; hf = hn;
                    }
                    Vector3 down = new Vector3(b.x - a.x, 0f, b.y - a.y).normalized;
                    rims.Add((new Vector3(a.x, ha, a.y), new Vector3(f.x, hf, f.y), down));
                    break;
                }
            }
        }
        if (rims.Count < 3) return "Could not find the west cliff's rim (is this the Leaf Map scene?). Found " + rims.Count + " rim points.";
        // The three tallest drops, kept apart, carry the springs.
        rims.Sort((p, q) => (q.top.y - q.foot.y).CompareTo(p.top.y - p.foot.y));
        var springs = new List<(Vector3 top, Vector3 foot, Vector3 down)>();
        foreach (var r in rims)
        {
            bool near = false;
            foreach (var s in springs) if ((s.top - r.top).magnitude < 22f) near = true;
            if (!near) springs.Add(r);
            if (springs.Count == 3) break;
        }

        // ---- Group ----
        Undo.IncrementCurrentGroup();
        GameObject oldGroup = GameObject.Find(GroupName);
        if (oldGroup != null) Undo.DestroyObjectImmediate(oldGroup);
        var group = new GameObject(GroupName).transform;
        Undo.RegisterCreatedObjectUndo(group.gameObject, "Cliff Ecosystem");
        var random = new System.Random(31);
        float R(float lo, float hi) => Mathf.Lerp(lo, hi, (float)random.NextDouble());

        // ---- Water: springs a step back from the rim, flowing over it ----
        var fluid = new GameObject("Cliff Springs (erosive water)").AddComponent<ErosiveFluidSystem>();
        fluid.transform.SetParent(group, false);
        var fluidSettings = new SerializedObject(fluid);
        fluidSettings.FindProperty("world").objectReferenceValue = world;
        fluidSettings.FindProperty("dropletMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Fluid/MAT_ErosiveWater.mat");
        fluidSettings.FindProperty("debrisMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Fluid/MAT_ErosionDebris.mat");
        fluidSettings.FindProperty("rippleMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Fluid/MAT_ErosionRipple.mat");
        fluidSettings.FindProperty("maximumDroplets").intValue = 520;
        fluidSettings.FindProperty("contactAgeMultiplier").floatValue = 1f; // creatures drink here; the water must not age them
        fluidSettings.FindProperty("contactAgeFloor").floatValue = 0f;
        fluidSettings.FindProperty("erosiveRain").boolValue = false;
        fluidSettings.ApplyModifiedPropertiesWithoutUndo();
        int s_ = 0;
        foreach (var spring in springs)
        {
            var source = new GameObject("Spring " + (++s_)).AddComponent<ErosiveFluidSource>();
            source.transform.SetParent(fluid.transform, false);
            source.transform.SetPositionAndRotation(spring.top - spring.down * 2.5f + Vector3.up * 0.6f, Quaternion.LookRotation(spring.down, Vector3.up));
            var ss = new SerializedObject(source);
            ss.FindProperty("fluid").objectReferenceValue = fluid;
            ss.FindProperty("dropletsPerSecond").floatValue = 9f;
            ss.FindProperty("lifetime").floatValue = 22f;
            ss.FindProperty("dropletRadius").floatValue = 0.32f;
            ss.FindProperty("spread").floatValue = 0.5f;
            ss.FindProperty("initialVelocity").vector3Value = new Vector3(0f, 0f, 1.6f);
            ss.FindProperty("erosionRate").floatValue = 0.15f; // slow: grooves over minutes, not a canyon in seconds
            ss.FindProperty("erosionRadius").floatValue = 0.8f;
            ss.ApplyModifiedPropertiesWithoutUndo();
            // A spring mouth: a ring of pale stones round where the water wells up.
            foreach (GameObject stone in LoadAll("Cairns", "Pebbles"))
            {
                Place(stone, group, spring.top - spring.down * 3.6f, Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.7f, 1.1f), "Spring stones");
                break;
            }
        }

        // ---- Pools at the foot: wet growths and goo ----
        GameObject[] wet = LoadAll("ShellCarpets", "GlowSacs", "Fronds", "Mushrooms");
        var goo = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/GooCreature.prefab");
        foreach (var spring in springs)
        {
            for (int k = 0; k < 7; k++)
            {
                Vector3 at = spring.foot + spring.down * R(0f, 4f) + Vector3.Cross(Vector3.up, spring.down) * R(-4f, 4f);
                float g = Ground(new Vector2(at.x, at.z), out Vector3 n);
                if (float.IsNaN(g) || n.y < 0.6f) continue;
                Place(wet[random.Next(wet.Length)], group, new Vector3(at.x, g, at.z), Quaternion.FromToRotation(Vector3.up, n) * Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.6f, 1.2f), "Pool growth");
            }
            if (goo != null)
            {
                Vector3 at = spring.foot + spring.down * 2.5f;
                float g = Ground(new Vector2(at.x, at.z), out _);
                if (!float.IsNaN(g)) Place(goo, group, new Vector3(at.x, g + 0.4f, at.z), Quaternion.identity, 1f, "Goo — at the cliff pool");
            }
        }

        // ---- Tentacles beside the falls (copies of the scene's own tentacle, so they keep its settings) ----
        TentacleCreature template = null;
        foreach (TentacleCreature t in Object.FindObjectsByType<TentacleCreature>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.gameObject.scene == group.gameObject.scene) { template = t; break; }
        int tentacles = 0;
        if (template != null)
            foreach (var spring in springs)
            {
                Vector3 side = Vector3.Cross(Vector3.up, spring.down) * (tentacles % 2 == 0 ? 3.5f : -3.5f);
                Vector3 at = Vector3.Lerp(spring.top, spring.foot, 0.35f) + side;
                // On the face itself: from out in the air, towards the cliff.
                if (Physics.Raycast(at + spring.down * 6f, -spring.down, out RaycastHit face, 14f, terrain, QueryTriggerInteraction.Ignore))
                {
                    var copy = (GameObject)Object.Instantiate(template.gameObject, face.point + face.normal * 0.6f, Quaternion.LookRotation(face.normal), group);
                    copy.name = "Tentacle — beside the falls";
                    copy.SetActive(true);
                    Undo.RegisterCreatedObjectUndo(copy, "Cliff Ecosystem");
                    tentacles++;
                }
            }

        // ---- Webs across the gullies between the falls ----
        var spider = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/SkySpider.prefab");
        int spiders = 0;
        for (int i = 0; i + 1 < springs.Count && spider != null; i++)
        {
            Vector3 mid = Vector3.Lerp(springs[i].top, springs[i + 1].top, 0.5f) + Vector3.Lerp(springs[i].down, springs[i + 1].down, 0.5f) * 3f;
            float g = Ground(new Vector2(mid.x, mid.z), out _);
            if (float.IsNaN(g)) continue;
            Place(spider, group, new Vector3(mid.x, g + 2f, mid.z), Quaternion.identity, 1f, "Sky spider — between the falls");
            spiders++;
        }

        // ---- Birds nesting in the cliff and flying along it ----
        var bird = AssetDatabase.LoadAssetAtPath<BirdCreature>("Assets/Prefabs/Creatures/BirdCreature.prefab");
        if (bird != null)
        {
            Vector3 centre = Vector3.zero;
            foreach (var s in springs) centre += Vector3.Lerp(s.top, s.foot, 0.5f);
            centre /= springs.Count;
            var area = new GameObject("Birds along the cliff").AddComponent<BirdSpawnArea>();
            area.transform.SetParent(group, false);
            area.transform.position = centre + springs[0].down * 8f + Vector3.up * 6f;
            area.Configure(bird, 8, 14f, 4401);
            Undo.RegisterCreatedObjectUndo(area.gameObject, "Cliff Ecosystem");
            // Nest hollows: clusters of veils and shells hanging in the face where the birds start.
            GameObject[] nests = LoadAll("HangingVeils", "Cages");
            foreach (var s in springs)
            {
                Vector3 at = Vector3.Lerp(s.top, s.foot, 0.55f) - Vector3.Cross(Vector3.up, s.down) * 6f;
                if (Physics.Raycast(at + s.down * 6f, -s.down, out RaycastHit face, 14f, terrain, QueryTriggerInteraction.Ignore))
                    Place(nests[random.Next(nests.Length)], group, face.point, Quaternion.FromToRotation(Vector3.up, face.normal), R(0.8f, 1.3f), "Nest hollow");
            }
        }

        Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
        EditorSceneManager.MarkSceneDirty(group.gameObject.scene);
        report = springs.Count + " springs on the west cliff rim (drops of " + string.Join(", ", springs.ConvertAll(s => (s.top.y - s.foot.y).ToString("0") + " m")) +
                 "), pools with goo and wet growth at the foot, " + tentacles + " tentacles beside the falls, " + spiders + " sky spiders between them, 8 birds along the cliff.";
        foreach (var s in springs) Debug.Log("[CliffEcosystem] spring at " + s.top + ", foot " + s.foot);
        return null;
    }

    private static void Place(GameObject prefab, Transform parent, Vector3 at, Quaternion rotation, float scale, string name)
    {
        if (prefab == null) return;
        var made = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(made, "Cliff Ecosystem");
        made.transform.SetPositionAndRotation(at, rotation);
        made.transform.localScale = prefab.transform.localScale * scale;
        made.name = name;
    }

    private static GameObject[] LoadAll(params string[] kinds)
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

    /// <summary>For batch tests: builds it in Scene_LeafMap and saves (only in an isolated batch copy).</summary>
    public static void BuildForTest()
    {
        if (!Application.isBatchMode) throw new System.InvalidOperationException("Run in an isolated batch project.");
        EditorSceneManager.OpenScene("Assets/Scenes/Scene_LeafMap.unity", OpenSceneMode.Single);
        string problem = Build(out string report);
        if (problem != null) throw new System.InvalidOperationException(problem);
        Debug.Log("[CliffEcosystem] " + report);
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
    }
}
