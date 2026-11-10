using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Brings teammates' content into Scene_World, moved by the same offsets as their grafted terrain (WorldMapTerrain).
/// Source scenes are opened additively and closed without saving. Teammates' components are copied as they are;
/// only references into their old scene (world, player, HUD) are pointed at Scene_World's, and the few world-space
/// fields (bird sky bounds, colossus routes) are moved by the offset.
/// </summary>
public static class WorldMapImport
{
    public static void All(Scene scene, Transform root, List<string> log)
    {
        Goo(scene, root, log);
        Mushrooms(scene, root, log);
        Island(scene, root, log);
    }

    // ---------------- yoyo: Scene_Goo, buried under the forest ----------------

    private static void Goo(Scene scene, Transform root, List<string> log)
    {
        Vector3 offset = WorldMapTerrain.GooOffset;
        Transform group = Group(root, "03 Goo cave — Scene_Goo (yoyo), under the forest", offset);
        Copy(scene, "Assets/Scenes/Scene_Goo.unity", group, new[] { "Goo Level Content" }, copy =>
        {
            foreach (var world in copy.GetComponentsInChildren<ProceduralWorld>(true)) if (world != null) Object.DestroyImmediate(world.gameObject);
            // Ground decor of the old open tundra that would now poke through the cave roof is left out.
            var tundra = copy.transform.Find("Rocky tundra — scattered stone and ground shadows");
            int dropped = 0;
            if (tundra != null)
                foreach (Transform piece in tundra.Cast<Transform>().ToArray())
                {
                    Vector3 local = piece.position - offset;
                    float top = TopOf(piece.gameObject, offset);
                    if (top > WorldMapTerrain.GooCeiling(local.x, local.z) - 0.5f) { Object.DestroyImmediate(piece.gameObject); dropped++; }
                }
            log.Add("Goo: tundra decor under the roof kept, " + dropped + " pieces through the roof left out.");
        }, offset, log);
    }

    // ---------------- marcus: Scene_FungalHollow, open to the sky in the basin ----------------

    private static void Mushrooms(Scene scene, Transform root, List<string> log)
    {
        Vector3 offset = WorldMapTerrain.MushroomOffset;
        Transform group = Group(root, "06 Mushroom basin — Scene_FungalHollow (marcus)", offset);
        Copy(scene, "Assets/Scenes/Scene_FungalHollow.unity", group, new[] { "Fungal Hollow" }, copy =>
        {
            Destroy(copy.transform, "Cavern — floor, slime walls, ceiling/Ceiling and Sky Vent");
            Destroy(copy.transform, "Interaction Hints");
            // The basin is open: the slime walls facing the flower field go, so the ramp walks straight in; the rest
            // are cut down to stand ~10-20 m over the rim instead of 84 m to the old ceiling.
            var walls = copy.transform.Find("Cavern — floor, slime walls, ceiling/Slime Walls");
            int removed = 0;
            if (walls != null)
                foreach (Transform wall in walls.Cast<Transform>().ToArray())
                {
                    Vector3 local = wall.localPosition;
                    float angle = Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg;
                    if (Mathf.Abs(Mathf.DeltaAngle(angle, 180f)) < 62f) { Object.DestroyImmediate(wall.gameObject); removed++; continue; }
                    const float bottom = -12f, top = 40f;
                    Vector3 s = wall.localScale;
                    s.y = top - bottom; wall.localScale = s;
                    wall.localPosition = new Vector3(local.x, (top + bottom) * 0.5f, local.z);
                }
            log.Add("Mushroom basin: ceiling removed, " + removed + " west slime walls opened, the rest cut to 52 m.");
        }, offset, log);
    }

    // ---------------- marcus: Scene_CisternBasin on the floating crystal island ----------------

    private static void Island(Scene scene, Transform root, List<string> log)
    {
        Vector3 offset = WorldMapTerrain.IslandOffset;
        Transform group = Group(root, "08 Crystal sky island — Scene_CisternBasin (marcus)", offset);
        Copy(scene, "Assets/Scenes/Scene_CisternBasin.unity", group, new[] { "Cistern Basin", "Cistern Basin Decor", "Erosive Fluid" }, copy =>
        {
            Destroy(copy.transform, "Interaction Hints");
            int dropped = 0;
            // Decor beyond the island's edge would float in the air.
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if (t == null || t == copy.transform || t.GetComponentInChildren<Creature>(true) != null) continue;
                if (t.GetComponent<Renderer>() == null && t.childCount > 0) continue;
                Vector3 local = t.position - offset;
                if (WorldMapBuild.IslandRadial(local.x, local.z) > WorldMapTerrain.IslandRadius - 3f) { Object.DestroyImmediate(t.gameObject); dropped++; }
            }
            if (copy.GetComponent<ErosiveFluidSystem>() != null && copy.GetComponent<FluidNearPlayer>() == null) copy.AddComponent<FluidNearPlayer>();
            foreach (var snail in copy.GetComponentsInChildren<CisternSnail>(true))
                if (snail.GetComponent<SettleWhenGroundReady>() == null) snail.gameObject.AddComponent<SettleWhenGroundReady>();
            if (dropped > 0) log.Add("Island: " + copy.name + " — " + dropped + " pieces past the island edge left out.");
        }, offset, log);
    }

    // ---------------- Copying ----------------

    private static Transform Group(Transform root, string name, Vector3 offset)
    {
        var old = root.Find(name); if (old != null) Object.DestroyImmediate(old.gameObject);
        var group = new GameObject(name).transform; group.SetParent(root, false); group.position = offset;
        return group;
    }

    private static void Destroy(Transform root, string path) { var t = root.Find(path); if (t != null) Object.DestroyImmediate(t.gameObject); }

    private static float TopOf(GameObject go, Vector3 offset)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        return renderers.Length == 0 ? go.transform.position.y - offset.y : renderers.Max(r => r.bounds.max.y) - offset.y;
    }

    /// <summary>Copies named root objects of a donor scene under group (moved by offset) and fixes their references.</summary>
    private static void Copy(Scene target, string donorPath, Transform group, string[] roots, Action<GameObject> trim, Vector3 offset, List<string> log)
    {
        Scene source = EditorSceneManager.OpenScene(donorPath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(target);
        try
        {
            foreach (string name in roots)
            {
                var original = source.GetRootGameObjects().FirstOrDefault(g => g.name == name);
                if (original == null) { log.Add("MISSING in " + donorPath + ": " + name); continue; }
                var copy = Object.Instantiate(original, group, false);
                copy.name = original.name;
                copy.transform.localPosition = original.transform.position;
                copy.transform.localRotation = original.transform.rotation;
                int fixedRefs = Remap(copy, source, target, out int cleared);
                Shift(copy, offset);
                trim(copy);
                log.Add(donorPath + " / " + name + ": " + copy.GetComponentsInChildren<Creature>(true).Length + " creatures, " +
                        fixedRefs + " references moved to Scene_World, " + cleared + " cleared.");
            }
        }
        finally { EditorSceneManager.CloseScene(source, true); SceneManager.SetActiveScene(target); }
    }

    /// <summary>Points references into the donor scene at Scene_World's world, player, HUD, ... (same type).</summary>
    private static int Remap(GameObject copy, Scene source, Scene target, out int cleared)
    {
        int moved = 0; cleared = 0;
        // Scene_World's own world is a root object; a donor world may already sit inside the fresh copy.
        var world = target.GetRootGameObjects().Select(r => r.GetComponent<ProceduralWorld>()).First(w => w != null);
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
                // The donor's own world may sit inside the copied content (Scene_Goo); it is removed, so point at ours.
                bool donorWorld = owner != null && owner.GetComponent<ProceduralWorld>() != null && owner != world.gameObject;
                if (owner == null || (owner.scene != source && !donorWorld)) continue;
                Object replacement = null;
                if (donorWorld)
                    replacement = value is GameObject ? world.gameObject : value is Transform ? world.transform : (Object)world;
                else if (value is Component comp)
                {
                    var match = target.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren(comp.GetType(), true)).FirstOrDefault();
                    replacement = match;
                }
                it.objectReferenceValue = replacement;
                changed = true;
                if (replacement != null) moved++; else cleared++;
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }
        return moved;
    }

    /// <summary>World-space fields of teammates' components that must move with their content.</summary>
    private static void Shift(GameObject copy, Vector3 offset)
    {
        foreach (var flock in copy.GetComponentsInChildren<BirdFlockManager>(true))
        {
            Bounds b = flock.SkyBounds; b.center += offset; flock.SkyBounds = b;
            EditorUtility.SetDirty(flock);
        }
        foreach (var colossus in copy.GetComponentsInChildren<OreColossus>(true))
        {
            var so = new SerializedObject(colossus);
            var route = so.FindProperty("route");
            for (int i = 0; i < route.arraySize; i++) route.GetArrayElementAtIndex(i).vector3Value += offset;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
