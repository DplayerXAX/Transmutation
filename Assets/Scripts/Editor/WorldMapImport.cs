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
        SpawnCave(scene, root, log);
        RecycleSites(scene, root, log);
        RestSites(scene, root, log);
    }

    // ---------------- yoyo: Scene_Goo, exterior at the world surface ----------------

    private static void Goo(Scene scene, Transform root, List<string> log)
    {
        Vector3 offset = WorldMapTerrain.GooOffset;
        Transform group = Group(root, "03 Goo canyon and mountain — Scene_Goo (yoyo)", offset);
        var world = scene.GetRootGameObjects().Select(r => r.GetComponent<ProceduralWorld>()).First(w => w != null);
        var fieldFlower = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Generated/LeafMap/MeadowFlower.prefab");
        Transform fieldLook = fieldFlower != null ? fieldFlower.transform.Find("Bubble lily") : null;
        Copy(scene, "Assets/Scenes/Scene_Goo.unity", group, new[] { "Goo Level Content" }, copy =>
        {
            foreach (var donorWorld in copy.GetComponentsInChildren<ProceduralWorld>(true)) if (donorWorld != null) Object.DestroyImmediate(donorWorld.gameObject);
            int removed = 0, flowers = 0;
            foreach (Transform tundra in copy.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Rocky tundra")).ToArray())
            {
                removed += tundra.childCount;
                Object.DestroyImmediate(tundra.gameObject);
            }
            foreach (var flower in copy.GetComponentsInChildren<BubbleFlower>(true))
            {
                if (fieldLook == null) break;
                foreach (var renderer in flower.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.transform.parent == flower.transform) renderer.enabled = false;
                if (flower.transform.Find("Bubble lily") == null)
                {
                    var look = Object.Instantiate(fieldLook.gameObject, flower.transform, false);
                    look.name = "Bubble lily";
                }
                flower.ConfigureGroundEmission(true, new Vector2(12f, 20f));
                flowers++;
            }
            log.Add("Goo: " + removed + " tundra shards and pebbles removed; " + flowers + " bubble flowers use the field lily model and slower firing.");
            log.Add("Goo: exterior tundra, spiky canyon, cave and mountain kept above the world surface.");
        }, offset, log);
        var leafPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LeafGardenShowcaseSetup.PrefabPath);
        if (leafPrefab != null)
        {
            var gardens = new[] { new Vector2(-43f, -133f), new Vector2(42f, -111f) };
            for (int i = 0; i < gardens.Length; i++)
            {
                Vector2 p = gardens[i];
                if (!world.TryFindSurfaceBelow(new Vector3(p.x, 35f, p.y), out float y)) y = WorldMapTerrain.Height(p.x, p.y);
                var placed = (GameObject)PrefabUtility.InstantiatePrefab(leafPrefab, scene);
                placed.name = "Leaf garden — Goo exterior " + (i + 1);
                placed.transform.SetParent(group, true);
                placed.transform.position = new Vector3(p.x, y + 0.12f, p.y);
                var garden = placed.GetComponent<LeafGarden>();
                garden.Area = new Vector2(90f, 110f); garden.Seed = 3217 + i * 131;
                garden.RandomizeEventSizes = true; garden.RandomizeFirstDelay = true;
                garden.FirstDelaySeconds = new Vector2(35f, 80f);
                garden.SmallHeightRange = new Vector2(8f, 14f);
                garden.MediumChance = 0.08f; garden.LargeChance = 0f;
                var leaves = placed.GetComponent<LeafGardenPresentation>();
                leaves.LeafCount = 25000; leaves.Coverage = 0.7f; leaves.OrganicEdge = 1f;
            }
            log.Add("Goo exterior: two broad, irregular Leaf Gardens placed after clearing tundra clutter.");
        }
    }

    /// <summary>Bake the Goo exterior's actual ground into the leaf shader so the broad scatter follows slopes.</summary>
    public static void SpreadGooLeaves(Scene scene, List<string> log = null)
    {
        var gardens = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LeafGarden>(true))
            .Where(g => g.name.StartsWith("Leaf garden — Goo exterior")).ToArray();
        if (gardens.Length == 0) return;
        var world = scene.GetRootGameObjects().Select(r => r.GetComponent<ProceduralWorld>()).First(w => w != null);
        if (!world.GenerationComplete || world.VolumeBounds.size.sqrMagnitude < 1f) world.Generate(true);
        const string folder = "Assets/Generated/WorldMap";
        const string mapPath = folder + "/GooLeafGround.asset";
        const string surfacePath = folder + "/GooLeafSurface.asset";
        const int width = 181, depth = 143;
        const float left = -90f, back = -192f, spanX = 180f, spanZ = 142f;
        var map = AssetDatabase.LoadAssetAtPath<Texture2D>(mapPath);
        if (map == null)
        {
            map = new Texture2D(width, depth, TextureFormat.RGBAFloat, false, true)
                { name = "Goo exterior leaf ground", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            AssetDatabase.CreateAsset(map, mapPath);
        }
        var surface = AssetDatabase.LoadAssetAtPath<LeafGardenSurface>(surfacePath);
        if (surface == null)
        {
            surface = ScriptableObject.CreateInstance<LeafGardenSurface>();
            AssetDatabase.CreateAsset(surface, surfacePath);
        }
        var pixels = new Color[width * depth];
        var hits = new RaycastHit[16];
        float low = float.MaxValue, high = float.MinValue;
        Physics.SyncTransforms();
        for (int z = 0; z < depth; z++) for (int x = 0; x < width; x++)
        {
            float wx = left + x * spanX / (width - 1), wz = back + z * spanZ / (depth - 1);
            int count = Physics.RaycastNonAlloc(new Vector3(wx, 80f, wz), Vector3.down, hits, 180f,
                1 << world.TerrainLayer, QueryTriggerInteraction.Ignore);
            bool found = false; RaycastHit best = default;
            for (int i = 0; i < count; i++) if (world.OwnsCollider(hits[i].collider) && (!found || hits[i].point.y > best.point.y))
            { found = true; best = hits[i]; }
            bool leafy = found && best.point.y < 27f && best.normal.y > 0.72f;
            pixels[z * width + x] = leafy
                ? new Color(best.point.y, best.normal.x, best.normal.z, 1f)
                : new Color(found ? best.point.y : 0f, 0f, 0f, 0f);
            if (leafy) { low = Mathf.Min(low, best.point.y); high = Mathf.Max(high, best.point.y); }
        }
        map.SetPixels(pixels); map.Apply(false, false);
        surface.Map = map; surface.WorldRect = new Vector4(left, back, spanX, spanZ);
        surface.HeightRange = low <= high ? new Vector2(low, high) : Vector2.zero;
        EditorUtility.SetDirty(map); EditorUtility.SetDirty(surface);
        foreach (var garden in gardens)
        {
            garden.Surface = surface;
            garden.Area = new Vector2(90f, 110f);
            var leaves = garden.GetComponent<LeafGardenPresentation>();
            leaves.LeafCount = 25000; leaves.Coverage = 0.7f; leaves.OrganicEdge = 1f;
            EditorUtility.SetDirty(garden); EditorUtility.SetDirty(leaves);
        }
        AssetDatabase.SaveAssets();
        log?.Add("Goo leaves: terrain-following scatter over two 90x110m irregular patches.");
    }

    // ---------------- marcus: Scene_FungalHollow, open to the sky in the basin ----------------

    private static void Mushrooms(Scene scene, Transform root, List<string> log)
    {
        Vector3 offset = WorldMapTerrain.MushroomOffset;
        Transform group = Group(root, "06 Mushroom basin — Scene_FungalHollow (marcus)", offset);
        Copy(scene, "Assets/Scenes/Scene_FungalHollow.unity", group, new[] { "Fungal Hollow" }, copy =>
        {
            Destroy(copy.transform, "Cavern — floor, slime walls, ceiling");
            Destroy(copy.transform, "Interaction Hints");
            var capMesh = MushroomTreeSetup.CapMesh();
            var capMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Realm_MushroomBlue.mat");
            int caps = 0, removed = 0;
            foreach (var f in copy.GetComponentsInChildren<MeshFilter>(true).ToArray())
            {
                if (f == null || f.sharedMesh == null || f.sharedMesh.name != "Cube") continue;
                if (f.name.StartsWith("Vent ")) { Object.DestroyImmediate(f.gameObject); removed++; continue; }
                var t = f.transform;
                Vector3 size = t.localScale, pos = t.localPosition;
                t.localPosition = pos + Vector3.up * size.y * 0.5f;
                t.localScale = new Vector3(Mathf.Max(0.75f, size.x * 0.55f), Mathf.Max(1.5f, size.x * 0.22f), Mathf.Max(0.75f, size.z * 0.55f));
                f.sharedMesh = capMesh;
                f.GetComponent<MeshRenderer>().sharedMaterial = capMat;
                var box = f.GetComponent<BoxCollider>(); if (box != null) Object.DestroyImmediate(box);
                f.gameObject.AddComponent<MeshCollider>().sharedMesh = capMesh;
                caps++;
            }
            log.Add("Mushroom basin: floor, box walls and ceiling replaced by world terrain; " + caps +
                " block ledges replaced by generated mushroom caps, " + removed + " obsolete vent blocks removed.");
        }, offset, log);
        SlopeMushrooms(group, log);
    }

    private static void SlopeMushrooms(Transform parent, List<string> log)
    {
        var capMesh = MushroomTreeSetup.CapMesh();
        var stalkMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Generated/MushroomTree/StalkUnit.asset");
        var stalkMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/MushroomTree/Stalk.mat");
        var capMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Realm_MushroomBlue.mat");
        if (stalkMesh == null || stalkMat == null || capMat == null) { log.Add("Mushroom slope: source meshes or materials unavailable."); return; }
        var spots = new[] {
            new Vector3(44f, 0f, 16f), new Vector3(54f, 0f, 51f),
            new Vector3(49f, 0f, 76f), new Vector3(66f, 0f, 18f) };
        float[] heights = { 6f, 9f, 7.5f, 11f };
        float[] radii = { 3.2f, 4.4f, 3.7f, 5.1f };
        var grove = new GameObject("Mushrooms beside the flower-field descent").transform;
        grove.SetParent(parent, false);
        for (int i = 0; i < spots.Length; i++)
        {
            Vector3 p = spots[i]; p.y = WorldMapTerrain.Height(p.x, p.z) - 0.6f;
            var mushroom = new GameObject("Slope mushroom " + (i + 1)).transform;
            mushroom.SetParent(grove, false); mushroom.position = p;
            var stalk = new GameObject("Stalk", typeof(MeshFilter), typeof(MeshRenderer), typeof(CapsuleCollider));
            stalk.transform.SetParent(mushroom, false);
            stalk.transform.localScale = new Vector3(radii[i] * 0.17f, heights[i], radii[i] * 0.17f);
            stalk.GetComponent<MeshFilter>().sharedMesh = stalkMesh;
            stalk.GetComponent<MeshRenderer>().sharedMaterial = stalkMat;
            var stemCollider = stalk.GetComponent<CapsuleCollider>(); stemCollider.center = Vector3.up * 0.5f; stemCollider.height = 1f;
            var cap = new GameObject("Standable cap", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            cap.layer = 7; cap.tag = "Ground";
            cap.transform.SetParent(mushroom, false);
            cap.transform.localPosition = Vector3.up * heights[i];
            cap.transform.localScale = new Vector3(radii[i], radii[i] * 0.7f, radii[i]);
            cap.GetComponent<MeshFilter>().sharedMesh = capMesh;
            cap.GetComponent<MeshRenderer>().sharedMaterial = capMat;
            cap.GetComponent<MeshCollider>().sharedMesh = capMesh;
        }
        log.Add("Mushroom slope: " + spots.Length + " standable side mushrooms placed along the west descent.");
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
            var fluid = copy.GetComponent<ErosiveFluidSystem>();
            if (fluid != null)
            {
                var proximity = copy.GetComponent<FluidNearPlayer>();
                if (proximity == null) proximity = copy.AddComponent<FluidNearPlayer>();
                proximity.activeRange = 190f;
                proximity.margin = 25f;
            }
            if (fluid != null && fluid.Rain == null)
            {
                var cloud = new GameObject("Erosive Rain — crystal island and world below");
                cloud.transform.SetParent(fluid.transform, false);
                cloud.AddComponent<ErosiveFluidSource>().ConfigureForRain();
                var rain = cloud.AddComponent<ErosiveRain>();
                var settings = new SerializedObject(rain);
                settings.FindProperty("favorSkyIsland").boolValue = true;
                settings.FindProperty("islandStormShare").floatValue = 5f / 7f;
                settings.FindProperty("stormRadius").floatValue = 9f;
                settings.FindProperty("firstStormDelay").floatValue = 15f;
                settings.FindProperty("clearDuration").vector2Value = new Vector2(40f, 80f);
                settings.FindProperty("stormDuration").vector2Value = new Vector2(12f, 23f);
                settings.ApplyModifiedPropertiesWithoutUndo();
                log.Add("Crystal island: authored corrosive rain with five of seven storms above and two below.");
            }
            foreach (var snail in copy.GetComponentsInChildren<CisternSnail>(true))
                if (snail.GetComponent<SettleWhenGroundReady>() == null) snail.gameObject.AddComponent<SettleWhenGroundReady>();
            if (dropped > 0) log.Add("Island: " + copy.name + " — " + dropped + " pieces past the island edge left out.");
        }, offset, log);
    }

    // ---------------- forest cave tutorial and Recycle landmarks ----------------

    private static void SpawnCave(Scene scene, Transform root, List<string> log)
    {
        var group = Group(root, "01 Forest cave — mushroom exit tutorial", Vector3.zero);
        group.gameObject.AddComponent<WorldMapPromptGate>();
        var treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MushroomTreeSetup.TreePath);
        var bouncePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/Traversal/BounceShroom.prefab");
        if (treePrefab == null || bouncePrefab == null) { log.Add("Forest cave: mushroom creature prefabs missing."); return; }
        GameObject Place(GameObject prefab, string name, Vector3 at)
        {
            var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            placed.name = name;
            placed.transform.SetParent(group, true);
            placed.transform.position = at;
            return placed;
        }
        Vector2 s = WorldMapTerrain.Spawn;
        var tree = Place(treePrefab, "Small mushroom tree", new Vector3(s.x - 5f, -10.25f, s.y + 4f));
        tree.transform.localScale = Vector3.one * 0.58f;
        var still = Place(bouncePrefab, "Bounce shroom — stationary", new Vector3(s.x + 5f, -10.2f, s.y - 2.5f));
        var stillSettings = new SerializedObject(still.GetComponent<BounceShroom>());
        stillSettings.FindProperty("hops").boolValue = false;
        stillSettings.ApplyModifiedPropertiesWithoutUndo();
        Place(bouncePrefab, "Bounce shroom — hopping", new Vector3(s.x + 22f, -10.2f, s.y + 8f));
        void CaveLight(string name, Vector3 at, Color color, float range, float intensity)
        {
            var lamp = new GameObject(name, typeof(Light));
            lamp.transform.SetParent(group, false);
            lamp.transform.position = at;
            var light = lamp.GetComponent<Light>();
            light.type = LightType.Point; light.color = color; light.range = range; light.intensity = intensity;
            light.shadows = LightShadows.Soft;
        }
        CaveLight("Soft cave light", new Vector3(s.x - 3f, -2.2f, s.y), new Color(0.53f, 0.83f, 1f), 20f, 5f);
        CaveLight("Daylight at the ledge", new Vector3(s.x + 27f, 3f, s.y + 8f), new Color(1f, 0.84f, 0.62f), 22f, 6f);
        log.Add("Forest cave: small mushroom tree, stationary bounce shroom and hopping bounce shroom beneath the eastern exit ledge.");
    }

    private static void RecycleSites(Scene scene, Transform root, List<string> log)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CompostCreatureSetup.PrefabPath);
        if (prefab == null) { log.Add("Recycle sites: creature prefab missing."); return; }
        var group = Group(root, "09 Recycle creatures — world landmarks", Vector3.zero);
        var sites = new[] {
            ("Recycle — tower approach", new Vector2(22f, 228f), 0.5f),
            ("Recycle — floating island start", new Vector2(-154f, 62f), 0.5f),
            ("Recycle — beneath the sky island", new Vector2(-84f, 72f), 0.5f),
            ("Recycle — mushroom slope wall", new Vector2(66f, 18f), 11.8f)
        };
        foreach (var site in sites)
        {
            var placed = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            placed.name = site.Item1;
            placed.transform.SetParent(group, true);
            placed.transform.position = new Vector3(site.Item2.x, WorldMapTerrain.Height(site.Item2.x, site.Item2.y) + site.Item3, site.Item2.y);
        }
        log.Add("Recycle sites: four authored creatures at the tower, island start, beneath the island and mushroom wall.");
    }

    private static void RestSites(Scene scene, Transform root, List<string> log)
    {
        var world = scene.GetRootGameObjects().Select(r => r.GetComponent<ProceduralWorld>()).First(w => w != null);
        var group = Group(root, "10 Meditation resting places — world route", Vector3.zero);
        group.gameObject.AddComponent<WorldMapRestSites>();
        var sites = new[] {
            ("Outside the forest cave", "Seat", WorldMapTerrain.SpawnExit + new Vector2(4f, 0f), 95f),
            ("Flower field", "Lookout", new Vector2(-70f, 42f), 45f),
            ("Tower foot", "Seat", new Vector2(23f, 238f), 300f),
            ("Beneath the sky island", "Hollow", new Vector2(-5f, 54f), 270f),
            ("Goo cave entrance", "Seat", WorldMapTerrain.CaveMouth + new Vector2(32f, 7f), 250f),
            ("Mushroom basin lower edge", "Hollow", new Vector2(84f, 52f), 70f)
        };
        foreach (var site in sites)
        {
            Vector2 p = site.Item3;
            float y = WorldMapTerrain.Height(p.x, p.y);
            if (world.TryFindSurfaceBelow(new Vector3(p.x, y + 18f, p.y), out float surface) && surface >= y - 4f) y = surface;
            var marker = new GameObject("Rest site — " + site.Item1, typeof(WorldMapRestSite));
            marker.transform.SetParent(group, false);
            marker.transform.SetPositionAndRotation(new Vector3(p.x, y + 0.15f, p.y), Quaternion.Euler(0f, site.Item4, 0f));
            marker.GetComponent<WorldMapRestSite>().Kind = site.Item2;
        }
        log.Add("Meditation: six scene-authored resting places along the cave, flower, tower, island, Goo and mushroom routes.");
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
        // The authored crawlers have an initialized home stored as a world position.
        // Their eggs inherit it, so leaving it in donor coordinates prevents hatching in Scene World.
        foreach (var life in copy.GetComponentsInChildren<CeilingBatBugLife>(true))
        {
            var so = new SerializedObject(life);
            var home = so.FindProperty("inheritedHome");
            home.vector3Value += offset;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
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
