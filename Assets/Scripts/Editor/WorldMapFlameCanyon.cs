using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Puts Flame Canyon (Scene_FlameCanyon) into Scene_World where the great crack used to be.
///
/// Terrain: the donor canyon is turned so it runs west to east (WorldMapTerrain.CanyonRotation) and its floor set at
/// WorldMapTerrain.CanyonFloor. It is cut into the world's own ground: inside the canyon the donor's air wins, and the
/// cut is carried straight up from the donor's rim to wherever the world's ground is, so the walls meet the plateau
/// at any height. A floor slab copied from the donor keeps the canyon closed over the inner world.
/// Content: the "Flame Canyon" and "Flame Canyon Decor" roots are copied under one group that carries the same
/// turn and drop, and their few world-space fields (steam seeps, rim and floor heights, the bone hollow) are moved.
///
/// GraftTerrain and ImportContent are part of WorldMapBuild.Run; ApplyToOpenWorld runs them on the saved
/// Scene_World in place (keeping everything teammates have changed in it), moves the bone mother beside the canyon
/// and rebakes the chunk meshes.
/// </summary>
public static class WorldMapFlameCanyon
{
    public const string DonorPath = "Assets/Scenes/Scene_FlameCanyon.unity";
    public const string GroupName = "11 Flame Canyon — Scene_FlameCanyon (marcus)";
    public const string MotherGroup = "05 Bone mother — sinkhole beside Flame Canyon";
    private const string OldMotherGroup = "05 Great crack — bone mother wedged in it";
    private const string FireRockPath = "Assets/Materials/World/MAT_FlameCanyon_Inner.mat";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly string[] DonorRoots = { "Flame Canyon", "Flame Canyon Decor" };

    /// <summary>Height of the donor plateau above its floor; the steam's rim height is measured from it.</summary>
    private const float DonorRim = 42f;
    /// <summary>The floor slab under the canyon, in donor metres below its floor.</summary>
    private const float SlabDepth = 10f;

    [MenuItem("Tools/Capstone/World Map/Replace Crack With Flame Canyon (in place)")]
    public static void ApplyToOpenWorld()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        Scene scene = SceneManager.GetSceneByPath(WorldMapBuild.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(WorldMapBuild.ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var log = new List<string>();
        var world = scene.GetRootGameObjects().Select(r => r.GetComponent<ProceduralWorld>()).First(w => w != null);
        Transform root = scene.GetRootGameObjects().First(g => g.name == "World — combined map").transform;

        // Snapshot the things that stood in the crack before the ground changes under them.
        var crackObjects = LegacyCrackObjects(root);

        Relayout(world, log);
        WorldMapBuild.PrepareSampler(world);
        GraftTerrain(world, log);
        AssetDatabase.SaveAssetIfDirty(world.SculptData);
        world.Generate(true);
        Physics.SyncTransforms();

        ImportContent(scene, root, log);
        MoveMother(scene, world, log);
        Resettle(crackObjects, world, log);
        ReviewCamera(root);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        WorldMapDress.BakeChunks(world, log);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Flame Canyon in Scene_World:\n" + string.Join("\n", log));
    }

    // ---------------- Terrain ----------------

    /// <summary>Moves the mother's sinkhole (1) beside the canyon and re-stamps the generator checks (the crack is gone).</summary>
    private static void Relayout(ProceduralWorld world, List<string> log)
    {
        TerrainSculptData data = world.SculptData;
        Type fieldType = typeof(ProceduralWorld).GetNestedType("DensityField", BindingFlags.NonPublic);
        string holes = "\"sinkholes\":[" + Hole(WorldMapTerrain.SpireSpot, WorldMapTerrain.SpireHoleRadius) + "," +
                       Hole(WorldMapTerrain.MotherHole, WorldMapTerrain.MotherHoleRadius) + "]";
        string layout = System.Text.RegularExpressions.Regex.Replace(data.Layout, "\"sinkholes\":\\[.*?\\]", holes);
        object field = JsonUtility.FromJson(layout, fieldType);
        var checks = (float[])typeof(ProceduralWorld).GetMethod("CheckValues", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new[] { field });
        data.SetLayout(layout, checks);
        EditorUtility.SetDirty(data);
        log.Add("Layout: mother's sinkhole moved to " + WorldMapTerrain.MotherHole + "; generator checks updated.");
    }

    private static string Hole(Vector2 at, float radius) =>
        "{\"position\":{\"x\":" + F(at.x) + ",\"y\":" + F(at.y) + "},\"radius\":" + F(radius) + "}";
    private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Cuts the donor canyon into the world's saved terrain (on top of the edits already there).</summary>
    public static void GraftTerrain(ProceduralWorld target, List<string> log)
    {
        Scene own = target.gameObject.scene;
        Scene source = EditorSceneManager.OpenScene(DonorPath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(own);
        try
        {
            var donor = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ProceduralWorld>(true)).Single();
            WorldMapBuild.PrepareSampler(donor); WorldMapBuild.PrepareSampler(target);
            object field = typeof(ProceduralWorld).GetField("field", Private).GetValue(target);
            var height = (Func<float, float, float>)Delegate.CreateDelegate(typeof(Func<float, float, float>), field, "SurfaceHeight");
            var ceiling = (Func<float, float>)Delegate.CreateDelegate(typeof(Func<float, float>), field, "CeilingHeight");
            var floor = (Func<float, float, float, float>)Delegate.CreateDelegate(typeof(Func<float, float, float, float>), field, "InnerGroundHeight");
            var density = (Func<Vector3, float, float, float, float>)Delegate.CreateDelegate(typeof(Func<Vector3, float, float, float, float>), field, "Density");
            // The donor's plateau before its canyon was sculpted (donor-local heights; its world sits Lift up).
            object donorField = typeof(ProceduralWorld).GetField("field", Private).GetValue(donor);
            var donorHeight = (Func<float, float, float>)Delegate.CreateDelegate(typeof(Func<float, float, float>), donorField, "SurfaceHeight");
            Vector3 donorOrigin = donor.transform.position;
            TerrainSculptData data = target.SculptData;
            var edits = data.Edits;
            Vector3 min = target.GridMinimum;
            float cell = target.CellSize;

            // Box round the canyon (world x, z), from below the slab to above the highest ground over it.
            Vector2 lo2 = new Vector2(float.MaxValue, float.MaxValue), hi2 = new Vector2(float.MinValue, float.MinValue);
            foreach (var corner in new[] { new Vector3(-62f, 0f, -142f), new Vector3(62f, 0f, -142f), new Vector3(-62f, 0f, 142f), new Vector3(62f, 0f, 142f) })
            {
                Vector3 w = WorldMapTerrain.FromCanyon(corner);
                lo2 = Vector2.Min(lo2, new Vector2(w.x, w.z)); hi2 = Vector2.Max(hi2, new Vector2(w.x, w.z));
            }
            float top = float.MinValue;
            for (float x = lo2.x; x <= hi2.x; x += 4f) for (float z = lo2.y; z <= hi2.y; z += 4f)
                if (WorldMapTerrain.CanyonDistance(new Vector2(x, z)) < 10f) top = Mathf.Max(top, height(x, z));
            float bottom = WorldMapTerrain.CanyonFloor - SlabDepth - 3f;
            Vector3Int lo = Vector3Int.FloorToInt((new Vector3(lo2.x, bottom, lo2.y) - min) / cell);
            Vector3Int hi = Vector3Int.CeilToInt((new Vector3(hi2.x, top + 4f, hi2.y) - min) / cell);

            int changed = 0, columns = 0;
            for (int z = lo.z; z <= hi.z; z++)
            {
                if ((z - lo.z) % 8 == 0) EditorUtility.DisplayProgressBar("Flame Canyon", "Cutting the canyon into Scene_World", (z - lo.z) / (float)(hi.z - lo.z + 1));
                for (int x = lo.x; x <= hi.x; x++)
                {
                    Vector3 column = min + new Vector3(x * cell, 0f, z * cell);
                    float rimDistance = WorldMapTerrain.CanyonDistance(new Vector2(column.x, column.z));
                    if (rimDistance > 10f) continue;
                    columns++;
                    float h = height(column.x, column.z), c = ceiling(h), f = floor(column.x, column.z, h);
                    Vector3 q0 = WorldMapTerrain.ToCanyon(column);
                    float donorSurface = donorOrigin.y + donorHeight(q0.x - donorOrigin.x, q0.z - donorOrigin.z);
                    float extrudeFrom = donorSurface - 1.5f;
                    for (int y = lo.y; y <= hi.y; y++)
                    {
                        var key = new Vector3Int(x, y, z);
                        Vector3 p = min + (Vector3)key * cell;
                        float baseValue = density(p, h, c, f);
                        float current = edits.TryGetValue(key, out float e) ? baseValue + e : baseValue;
                        Vector3 q = WorldMapTerrain.ToCanyon(p);
                        bool inSlab = q.y > -SlabDepth && q.y < 4f && rimDistance < 6f;
                        if (current <= -2.5f && !inSlab) continue;
                        // The donor's rock, its rim carried straight up so the cut reaches the world's ground.
                        float donorRock = donor.SampleDensity(new Vector3(q.x, Mathf.Min(q.y, extrudeFrom), q.z), false);
                        float result = Mathf.Min(current, donorRock);
                        if (inSlab) result = Mathf.Max(result, Mathf.Min(Mathf.Min(donorRock, q.y + SlabDepth), Mathf.Min(4f - q.y, 6f - rimDistance)));
                        if ((result > 2.5f && current > 2.5f) || (result < -2.5f && current < -2.5f)) continue;
                        if (Mathf.Abs(result - current) < 1e-4f) continue;
                        data.SetEdit(key, result - baseValue); changed++;
                    }
                }
            }
            EditorUtility.SetDirty(data);
            log.Add("Flame Canyon terrain: " + columns + " columns, " + changed + " samples changed; floor at y " +
                    WorldMapTerrain.CanyonFloor + ", ground over it up to y " + top.ToString("0.0") + ".");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            EditorSceneManager.CloseScene(source, true);
            SceneManager.SetActiveScene(own);
        }

        // The canyon's walls and floor in the donor's fire colours.
        var fireRock = AssetDatabase.LoadAssetAtPath<Material>(FireRockPath);
        if (fireRock != null)
        {
            Vector3 a = WorldMapTerrain.FromCanyon(new Vector3(-50f, -SlabDepth, -130f)), b = WorldMapTerrain.FromCanyon(new Vector3(50f, 10f, 130f));
            var zone = new Bounds((a + b) * 0.5f, new Vector3(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y), Mathf.Abs(a.z - b.z)));
            var zones = target.InnerMaterialZones.Where(z => z.material != fireRock).ToList();
            zones.Add(new ProceduralWorld.InnerMaterialZone { bounds = zone, material = fireRock });
            target.InnerMaterialZones = zones.ToArray();
            EditorUtility.SetDirty(target);
            log.Add("Flame Canyon rock material on chunks inside " + zone);
        }
    }

    // ---------------- Content ----------------

    /// <summary>Copies the canyon's life and decor under one group turned and lowered like the terrain.</summary>
    public static void ImportContent(Scene scene, Transform root, List<string> log)
    {
        var old = root.Find(GroupName); if (old != null) Object.DestroyImmediate(old.gameObject);
        var group = new GameObject(GroupName).transform;
        group.SetParent(root, false);
        group.SetPositionAndRotation(WorldMapTerrain.FromCanyon(Vector3.zero), WorldMapTerrain.CanyonRotation);
        var world = scene.GetRootGameObjects().Select(r => r.GetComponent<ProceduralWorld>()).First(w => w != null);

        Scene source = EditorSceneManager.OpenScene(DonorPath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            foreach (string name in DonorRoots)
            {
                var original = source.GetRootGameObjects().FirstOrDefault(g => g.name == name);
                if (original == null) { log.Add("MISSING in " + DonorPath + ": " + name); continue; }
                var copy = Object.Instantiate(original, group, false);
                copy.name = original.name;
                copy.transform.localPosition = original.transform.position;
                copy.transform.localRotation = original.transform.rotation;
                int moved = Remap(copy, source, scene, world, out int cleared);
                log.Add("Flame Canyon / " + name + ": " + copy.GetComponentsInChildren<Creature>(true).Length + " creatures, " +
                        moved + " references moved to Scene_World, " + cleared + " cleared.");
            }
        }
        finally { EditorSceneManager.CloseScene(source, true); SceneManager.SetActiveScene(scene); }

        // Scene_World has its own hints and population limits.
        var hints = group.GetComponentsInChildren<InteractionPromptHUD>(true).Select(h => h.gameObject).ToArray();
        foreach (var h in hints) Object.DestroyImmediate(h);
        foreach (var population in group.GetComponentsInChildren<RealmPopulation>(true)) Object.DestroyImmediate(population);

        float rim = RimHeight();
        foreach (var steam in group.GetComponentsInChildren<CanyonSteam>(true))
        {
            var so = new SerializedObject(steam);
            var seeps = so.FindProperty("seeps");
            for (int i = 0; i < seeps.arraySize; i++)
                seeps.GetArrayElementAtIndex(i).vector3Value = WorldMapTerrain.FromCanyon(seeps.GetArrayElementAtIndex(i).vector3Value);
            so.FindProperty("rimHeight").floatValue = rim;
            so.FindProperty("floorHeight").floatValue = WorldMapTerrain.CanyonFloor;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        foreach (var life in group.GetComponentsInChildren<FlameCanyonLife>(true))
        {
            var so = new SerializedObject(life);
            foreach (string field in new[] { "hollowCentre", "nestPosition" })
                so.FindProperty(field).vector3Value = WorldMapTerrain.FromCanyon(so.FindProperty(field).vector3Value);
            so.FindProperty("world").objectReferenceValue = world;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        log.Add("Flame Canyon content at " + group.position + ", turned " + WorldMapTerrain.CanyonYaw + "°; steam rim y " +
                rim.ToString("0.0") + ", floor y " + WorldMapTerrain.CanyonFloor + ".");
    }

    /// <summary>The world's ground along the canyon's rim (median), where the steam gathers and the updraft lets go.</summary>
    private static float RimHeight()
    {
        var heights = new List<float>();
        for (float x = -140f; x <= 140f; x += 3f) for (float z = 90f; z <= 235f; z += 3f)
        {
            float d = WorldMapTerrain.CanyonDistance(new Vector2(x, z));
            if (d > 0f && d < 3f) heights.Add(WorldMapTerrain.Height(x, z));
        }
        if (heights.Count == 0) return WorldMapTerrain.CanyonFloor + DonorRim;
        heights.Sort();
        return Mathf.Min(heights[heights.Count / 2], WorldMapTerrain.CanyonFloor + DonorRim + 6f) - 2f;
    }

    /// <summary>References into the donor scene (outside the copy) are pointed at Scene_World's object of the same type.</summary>
    private static int Remap(GameObject copy, Scene source, Scene target, ProceduralWorld world, out int cleared)
    {
        int moved = 0; cleared = 0;
        foreach (var component in copy.GetComponentsInChildren<Component>(true))
        {
            if (component == null || component is Transform) continue;
            var so = new SerializedObject(component);
            var it = so.GetIterator();
            bool changed = false;
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null || it.name == "m_GameObject") continue;
                Object value = it.objectReferenceValue;
                GameObject owner = value is GameObject g ? g : value is Component c ? c.gameObject : null;
                if (owner == null || owner.scene != source) continue;
                Object replacement = null;
                if (value is ProceduralWorld) replacement = world;
                else if (value is Component comp)
                    replacement = target.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren(comp.GetType(), true)).FirstOrDefault();
                it.objectReferenceValue = replacement;
                changed = true;
                if (replacement != null) moved++; else cleared++;
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }
        return moved;
    }

    // ---------------- The bone mother and what stood in the crack ----------------

    private static void MoveMother(Scene scene, ProceduralWorld world, List<string> log)
    {
        var mother = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BoneKiln>(true)).FirstOrDefault();
        if (mother == null) { log.Add("No bone mother in Scene_World."); return; }
        Vector2 m = WorldMapTerrain.MotherHole;
        float y = WorldMapTerrain.Height(m.x, m.y);
        if (world.TryFindSurfaceBelow(new Vector3(m.x + WorldMapTerrain.MotherHoleRadius + 3f, y + 20f, m.y), out float ground)) y = ground;
        mother.transform.position = new Vector3(m.x, y, m.y);
        var so = new SerializedObject(mother);
        so.FindProperty("sinkholeOverride").intValue = 1;
        so.FindProperty("placeAtThisObject").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
        if (mother.transform.parent != null && (mother.transform.parent.name == OldMotherGroup || mother.transform.parent.name.StartsWith("05 ")))
            mother.transform.parent.name = MotherGroup;
        log.Add("Bone mother on sinkhole 1 at " + mother.transform.position + ".");
    }

    // The great crack's centre line before it was filled (only to find what stood in or beside it).
    private static readonly Vector2[] OldCrack = {
        new Vector2(-262f, 141f), new Vector2(-205f, 152f), new Vector2(-150f, 146f), new Vector2(-95f, 158f),
        new Vector2(-45f, 155f), new Vector2(-14f, 161f), new Vector2(22f, 161f), new Vector2(60f, 158f),
        new Vector2(105f, 168f), new Vector2(155f, 163f), new Vector2(205f, 174f), new Vector2(262f, 168f) };

    private static float OldCrackDistance(Vector2 p)
    {
        float best = float.MaxValue;
        for (int i = 1; i < OldCrack.Length; i++) best = Mathf.Min(best, WorldMapTerrain.Distance(p, OldCrack[i - 1], OldCrack[i], out _));
        return best;
    }

    /// <summary>Placed scenery (leaf objects with a renderer or collider) in or at the lip of the old crack.</summary>
    private static List<Transform> LegacyCrackObjects(Transform root)
    {
        var found = new List<Transform>();
        foreach (Transform group in root)
        {
            if (group.name == GroupName || group.name.StartsWith("Review cameras") || group.GetComponentInChildren<BoneKiln>(true) != null) continue;
            foreach (Transform t in group.GetComponentsInChildren<Transform>(true))
            {
                if (t == group || t.parent == null) continue;
                // Whole placed pieces: the topmost object below a plain grouping object.
                if (t.parent != group && (t.parent.GetComponent<Renderer>() != null || t.parent.GetComponent<Collider>() != null ||
                                          t.parent.GetComponents<MonoBehaviour>().Length > 0)) continue;
                if (t.GetComponentInChildren<Renderer>(true) == null && t.GetComponentInChildren<Collider>(true) == null && t.GetComponent<MonoBehaviour>() == null) continue;
                if (t.position.z < 110f || t.position.z > 210f) continue;
                if (OldCrackDistance(new Vector2(t.position.x, t.position.z)) < 30f) found.Add(t);
            }
        }
        return found;
    }

    /// <summary>Things that stood in the crack: removed if the canyon now opens under them, else set on the new ground.</summary>
    private static void Resettle(List<Transform> objects, ProceduralWorld world, List<string> log)
    {
        int removed = 0, moved = 0;
        foreach (Transform t in objects)
        {
            if (t == null) continue;
            Vector3 p = t.position;
            if (WorldMapTerrain.CanyonDistance(new Vector2(p.x, p.z)) < 2f) { Object.DestroyImmediate(t.gameObject); removed++; continue; }
            if (!world.TryFindSurfaceBelow(new Vector3(p.x, WorldMapTerrain.Height(p.x, p.z) + 30f, p.z), out float ground)) continue;
            if (Mathf.Abs(p.y - ground) < 1f) continue;
            t.position = new Vector3(p.x, ground, p.z);
            moved++;
        }
        log.Add("Old crack: " + removed + " placed pieces removed (now over the canyon), " + moved + " set onto the filled ground.");
    }

    private static void ReviewCamera(Transform root)
    {
        var group = root.Find("Review cameras — disabled during play");
        if (group == null) return;
        var cam = group.Cast<Transform>().FirstOrDefault(t => t.name.Contains("Crack"));
        if (cam == null) return;
        cam.name = "04_FlameCanyonAndMother";
        cam.position = new Vector3(-150f, 60f, 120f);
        cam.rotation = Quaternion.LookRotation(new Vector3(0f, -20f, 162f) - cam.position);
    }
}
