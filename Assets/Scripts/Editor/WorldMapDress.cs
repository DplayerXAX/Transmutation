using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Scene_World dressing (Daniel, 2026-11-10): an irregular floating-stone climb to the crystal island that gets harder
/// higher up, "colourful black" terrain and a mushroom basin that matches it, the bubble-lily flower field, region
/// decor, and saved chunk meshes so Play does not stall on terrain generation.
/// </summary>
public static class WorldMapDress
{
    private const string Folder = "Assets/Generated/WorldMap";
    private const string DecorFolder = "Assets/Prefabs/WorldDecor";

    public static void All(Scene scene, Transform root, List<string> log)
    {
        var world = scene.GetRootGameObjects().Select(r => r.GetComponent<ProceduralWorld>()).First(w => w != null);
        Materials(world, root, log);
        Stones(root, log);
        Flowers(root, log);
        Decor(root, log);
    }

    // ---------------- Colourful black ----------------

    private static Material Variant(string source, string name, System.Action<Material> edit)
    {
        string path = Folder + "/" + name + ".mat";
        var src = AssetDatabase.LoadAssetAtPath<Material>(source);
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(src) { name = name }; AssetDatabase.CreateAsset(m, path); }
        else m.CopyPropertiesFromMaterial(src);
        edit(m);
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssetIfDirty(m);
        return m;
    }

    private static void Materials(ProceduralWorld world, Transform root, List<string> log)
    {
        // The ground keeps the leaf map's ink look, with slow oil-slick tints in the black and its lines.
        var ground = Variant("Assets/Generated/LeafMap/Leaf_InkStone.mat", "World_InkStone", m =>
        {
            m.SetFloat("_IridescentAmount", 0f); m.SetFloat("_IridescentLines", 0f); m.SetFloat("_IridescentScale", 0.008f);
        });
        var so = new SerializedObject(world);
        so.FindProperty("surfaceMaterial").objectReferenceValue = ground;
        so.ApplyModifiedPropertiesWithoutUndo();

        log.Add("Colourful black: the continuous world terrain also forms the mushroom floor and walls.");
    }

    // ---------------- Floating stones ----------------

    /// <summary>
    /// A wide, wandering climb from the flower field to the island, mixing small steps with broad resting platforms.
    /// More outlying islands break up the single tight column of stones.
    /// </summary>
    private static void Stones(Transform root, List<string> log)
    {
        var islands = root.GetComponentInChildren<SkyIslands>(true);
        if (islands == null) { log.Add("No SkyIslands component to reuse."); return; }
        islands.transform.SetParent(root, true);
        islands.transform.position = Vector3.zero;
        islands.name = "07 Floating stones — flower field up to the crystal island";

        float edgeX = -WorldMapTerrain.IslandRadius + 6f;
        float top = IslandTop(new Vector3(edgeX, 0f, 26f));
        Vector3 altar = WorldMapTerrain.IslandOffset + new Vector3(edgeX - 8f, top + 0.4f, 26f);
        var random = new System.Random(20261110);
        float R(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
        Vector2 island = new Vector2(WorldMapTerrain.IslandOffset.x, WorldMapTerrain.IslandOffset.z);

        var stones = new List<Vector3>(); var radii = new List<float>(); var scenery = new List<bool>();
        Vector2 start2 = new Vector2(-150f, 62f);
        Vector3 at = new Vector3(start2.x, WorldMapTerrain.Height(start2.x, start2.y) + 1.6f, start2.y);
        float startY = at.y;
        stones.Add(at); radii.Add(3.4f); scenery.Add(false);
        // The broad landings are pauses; some smaller gaps deliberately need a bubble flower.
        // The bends make the ascent visible across the field rather than as one narrow stack.
        Vector3[] bends = {
            new Vector3(-185f, 0.13f, 45f), new Vector3(-215f, 0.25f, 62f),
            new Vector3(-185f, 0.37f, 115f), new Vector3(-140f, 0.49f, 120f),
            new Vector3(-116f, 0.60f, 96f), new Vector3(-160f, 0.71f, 70f),
            new Vector3(-128f, 0.82f, 30f), new Vector3(-104f, 0.92f, 42f),
            new Vector3(altar.x, 1f, altar.z) };
        foreach (Vector3 bend in bends)
        {
            Vector3 target = new Vector3(bend.x, Mathf.Lerp(startY, altar.y, bend.y), bend.z);
            int steps = Mathf.CeilToInt(Mathf.Max(Vector2.Distance(new Vector2(at.x, at.z), new Vector2(target.x, target.z)) / 14f,
                Mathf.Abs(target.y - at.y) / 1.8f));
            Vector3 from = at;
            for (int step = 1; step <= steps; step++)
            {
                Vector3 next = Vector3.Lerp(from, target, step / (float)steps);
                if (bend.y < 1f) next += new Vector3(Mathf.Sin(stones.Count * 1.7f) * 0.4f, 0f,
                    Mathf.Cos(stones.Count * 1.3f) * 0.4f);
                float radius = stones.Count % 7 == 0 ? R(7f, 10f) :
                    stones.Count % 3 == 0 ? R(4f, 6f) : R(2.4f, 3.6f);
                if (bend.y >= 1f && step == steps) { next = altar; radius = 5.5f; }
                stones.Add(next); radii.Add(radius); scenery.Add(false);
            }
            at = target;
        }
        // Leave deliberate open-air crossings between otherwise close stepping stones.
        // A guided bubble occupies each gap; the broad islands remain as safe pauses.
        int thinned = 0;
        for (int i = stones.Count - 3; i >= 3; i--)
        {
            if (i % 3 != 1 || i % 7 == 0) continue;
            float gap = Vector2.Distance(new Vector2(stones[i - 1].x, stones[i - 1].z),
                new Vector2(stones[i + 1].x, stones[i + 1].z)) - radii[i - 1] - radii[i + 1];
            float rise = stones[i + 1].y - stones[i - 1].y;
            if (gap < 6f || gap > 16f || rise > 3.8f) continue;
            stones.RemoveAt(i); radii.RemoveAt(i); scenery.RemoveAt(i); thinned++;
        }
        log.Add("Floating stones: wide route reached " + at.y.ToString("0") + " m across " + stones.Count +
            " platforms; " + thinned + " intermediary stones removed for bubble crossings.");
        int path = stones.Count;
        float maxRise = 0f, maxGap = 0f;
        var bridgeIndices = new List<int>();
        for (int i = 1; i < path; i++)
        {
            float rise = stones[i].y - stones[i - 1].y;
            float gap = Vector2.Distance(new Vector2(stones[i].x, stones[i].z), new Vector2(stones[i - 1].x, stones[i - 1].z)) - radii[i] - radii[i - 1];
            maxRise = Mathf.Max(maxRise, rise);
            maxGap = Mathf.Max(maxGap, gap);
            if (gap > 5.4f || rise > 1.7f) bridgeIndices.Add(i - 1);
        }
        log.Add("Floating stones: largest rise " + maxRise.ToString("0.0") + " m, widest gap " + maxGap.ToString("0.0") +
            " m, " + bridgeIndices.Count + " bubble-assisted crossings.");

        // Outlying islands of several sizes spread across the air between flowers and the main island.
        for (int i = 0; i < 95; i++)
        {
            Vector3 near = stones[random.Next(path - 1)];
            float radius = random.NextDouble() < 0.22 ? R(7f, 11f) : random.NextDouble() < 0.5 ? R(3.5f, 6.5f) : R(1.5f, 3f);
            Vector3 decoy = near + new Vector3(R(-42f, 42f), R(-8f, 10f), R(-40f, 40f));
            // Southern outliers sit on the ground instead of hanging over the Goo approach.
            if (decoy.z < 35f) decoy.y = WorldMapTerrain.Height(decoy.x, decoy.z) + radius * 1.95f;
            if (decoy.y < WorldMapTerrain.Height(decoy.x, decoy.z) + Mathf.Min(radius * 1.6f, 12f)) continue;
            if (WorldMapBuild.IslandRadial(decoy.x - island.x, decoy.z - island.y) < WorldMapTerrain.IslandRadius + 5f &&
                decoy.y < WorldMapTerrain.IslandOffset.y - 8f) continue;
            if (stones.Any(s => Vector3.Distance(s, decoy) < radius + 5f)) continue;
            stones.Insert(stones.Count - 1, decoy); radii.Insert(radii.Count - 1, radius); scenery.Insert(scenery.Count - 1, true);
        }
        islands.stones = stones.ToArray(); islands.radii = radii.ToArray(); islands.scenery = scenery.ToArray();
        islands.Rebuild();
        HangingTentacles(root, islands, stones, radii, log);

        // Every gap beyond a normal jump gets a flower on its departure island and a guided bubble in the gap.
        var flowers = islands.GetComponentsInChildren<BubbleFlower>(true);
        if (flowers.Length > 0)
            while (flowers.Length < bridgeIndices.Count)
            {
                Object.Instantiate(flowers[0].gameObject, islands.transform).name = "Bridge bubble flower";
                flowers = islands.GetComponentsInChildren<BubbleFlower>(true);
            }
        var guide = islands.GetComponentInChildren<SkyIslandBubbleGuide>(true);
        if (guide != null) guide.lanes.Clear();
        var flowerStones = new List<int>(bridgeIndices);
        var upper = Enumerable.Range(0, path - 1)
            .Where(i => !flowerStones.Contains(i) && (stones[i].y - startY) / (altar.y - startY) > 0.35f).ToList();
        for (int f = flowerStones.Count; f < flowers.Length && upper.Count > 0; f++)
            flowerStones.Add(upper[Mathf.Min(upper.Count - 1,
                (f - bridgeIndices.Count) * upper.Count / Mathf.Max(1, flowers.Length - bridgeIndices.Count))]);
        for (int f = 0; f < flowers.Length; f++)
        {
            if (f >= flowerStones.Count) { flowers[f].gameObject.SetActive(false); continue; }
            flowers[f].gameObject.SetActive(true);
            int i = flowerStones[f];
            int next = i + 1; while (next < stones.Count && scenery[next]) next++;
            flowers[f].transform.position = stones[i] + new Vector3(radii[i] * 0.3f, 0.12f, 0f);
            if (flowers[f].GetComponent<SettleWhenGroundReady>() == null) flowers[f].gameObject.AddComponent<SettleWhenGroundReady>().lift = 0.12f;
            if (guide != null && next < stones.Count)
                guide.lanes.Add(new SkyIslandBubbleGuide.Lane { flower = flowers[f], target = Vector3.Lerp(stones[i], stones[next], 0.55f) });
        }
        EditorUtility.SetDirty(islands); if (guide != null) EditorUtility.SetDirty(guide);
        log.Add("Floating stones: " + path + " on the route (" + startY.ToString("0") + " m to " + altar.y.ToString("0") + " m), " +
            (stones.Count - path) + " outlying islands, " + flowers.Length + " bubble flowers, each assisted crossing guided.");
    }

    private static void HangingTentacles(Transform root, SkyIslands islands, List<Vector3> stones,
        List<float> radii, List<string> log)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/TentacleCreature.prefab");
        if (prefab == null) { log.Add("Hanging tentacles: prefab missing."); return; }
        var old = root.Find("07b Tentacles hanging from sky stones");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var group = new GameObject("07b Tentacles hanging from sky stones").transform;
        group.SetParent(root, false);
        int[] preferred = { 12, 19, 26, 33, 40, 46 };
        int count = 0;
        foreach (int index in preferred)
        {
            if (index >= stones.Count - 1 || index >= islands.Built.childCount) continue;
            Vector3 p = stones[index]; float radius = radii[index];
            var stone = islands.Built.GetChild(index);
            var collider = stone.GetComponent<Collider>();
            if (collider == null || collider.bounds.min.y < WorldMapTerrain.Height(p.x, p.z) + 4f) continue;
            var made = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.gameObject.scene);
            made.name = "Hanging tentacle — sky stone " + index;
            made.transform.SetParent(group, true);
            made.transform.SetPositionAndRotation(new Vector3(p.x, collider.bounds.min.y - 1.35f, p.z),
                Quaternion.Euler(180f, index * 47f, 0f));
            var anchor = made.AddComponent<WorldMapHangingTentacle>();
            anchor.Islands = islands; anchor.StoneIndex = index;
            count++;
        }
        log.Add("Sky islands: " + count + " tentacle creatures authored beneath generated stone undersides.");
    }

    private static float IslandTop(Vector3 local)
    {
        Scene source = EditorSceneManager.OpenScene("Assets/Scenes/Scene_CisternBasin.unity", OpenSceneMode.Additive);
        try
        {
            var donor = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ProceduralWorld>(true)).Single();
            const System.Reflection.BindingFlags P = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(ProceduralWorld).GetMethod("UseSavedLayout", P).Invoke(donor, null);
            typeof(ProceduralWorld).GetMethod("PrepareGrid", P).Invoke(donor, new object[] { donor.SculptData.Headroom });
            typeof(ProceduralWorld).GetMethod("LoadSculpt", P).Invoke(donor, null);
            donor.ColumnHeights(local, out float top, out _, out _);
            return top;
        }
        finally { EditorSceneManager.CloseScene(source, true); }
    }

    // ---------------- Flower field ----------------

    private static void Flowers(Transform root, List<string> log)
    {
        var field = root.GetComponentInChildren<LilyField>(true);
        if (field == null) { log.Add("No LilyField to reuse."); return; }
        field.transform.SetParent(root, true);
        field.name = "05b Flower field — bubble lilies, purple to ice blue";
        var random = new System.Random(77);
        float R(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
        var values = new List<float>();
        Vector2 c = WorldMapTerrain.FlowerCentre;
        for (float z = c.y - 100f; z < c.y + 100f; z += 0.85f)
            for (float x = c.x - 110f; x < c.x + 110f; x += 0.85f)
            {
                float px = x + R(-0.4f, 0.4f), pz = z + R(-0.4f, 0.4f);
                float wx = px + (Mathf.PerlinNoise(px * 0.03f, pz * 0.03f) - 0.5f) * 30f, wz = pz + (Mathf.PerlinNoise(px * 0.03f + 9f, pz * 0.03f + 4f) - 0.5f) * 30f;
                float oval = new Vector2((wx - c.x) / 95f, (wz - c.y) / 85f).magnitude;
                float mask = 1f - Mathf.SmoothStep(0f, 1f, (oval - 0.55f) / 0.45f);
                if (mask <= 0f) continue;
                float clump = Mathf.PerlinNoise(wx * 0.045f + 3f, wz * 0.045f + 11f);
                float chance = mask * Mathf.Lerp(0.05f, 0.95f, Mathf.SmoothStep(0.3f, 0.62f, clump));
                if (random.NextDouble() > chance) continue;
                var p2 = new Vector2(px, pz);
                if (Vector2.Distance(p2, WorldMapTerrain.Spawn) < 14f) continue;
                if (WorldMapTerrain.InGooExterior(px, pz)) continue;
                if (WorldMapTerrain.CrackDistance(p2, out float t) < WorldMapTerrain.CrackHalfWidth(t) + 3f) continue;
                if (Vector2.Distance(p2, WorldMapTerrain.MushroomCentre) < WorldMapTerrain.MushroomRadius + 6f) continue;
                float h = WorldMapTerrain.Height(px, pz);
                Vector3 n = new Vector3(WorldMapTerrain.Height(px - 0.6f, pz) - WorldMapTerrain.Height(px + 0.6f, pz), 1.2f,
                                        WorldMapTerrain.Height(px, pz - 0.6f) - WorldMapTerrain.Height(px, pz + 0.6f)).normalized;
                if (n.y < 0.78f) continue;
                // Purple in the low east (by the mushrooms), violet in the middle, ice blue high in the north-west.
                float tone01 = Mathf.Clamp01(Mathf.InverseLerp(4f, 34f, h) + (Mathf.PerlinNoise(px * 0.05f, pz * 0.05f) - 0.5f) * 0.35f);
                int tone = tone01 < 0.38f ? 0 : tone01 < 0.68f ? 1 : 2;
                float size = R(0.55f, 0.82f) * Mathf.Lerp(0.85f, 1.55f, Mathf.SmoothStep(0.45f, 0.85f, clump));
                values.AddRange(new[] { px, h, pz, R(0f, 360f), size, (float)tone });
            }
        int count = values.Count / 6;
        const int Budget = 16000;
        if (count > Budget)
        {
            var keep = Enumerable.Range(0, count).OrderBy(_ => random.Next()).Take(Budget).OrderBy(i => i).ToList();
            var thinned = new List<float>(Budget * 6);
            foreach (int i in keep) thinned.AddRange(values.GetRange(i * 6, 6));
            values = thinned; count = Budget;
        }
        var bytes = new byte[values.Count * 4];
        System.Buffer.BlockCopy(values.ToArray(), 0, bytes, 0, bytes.Length);
        string path = Folder + "/WorldLilies.bytes";
        File.WriteAllBytes(path, bytes);
        AssetDatabase.ImportAsset(path);
        var so = new SerializedObject(field);
        so.FindProperty("data").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        so.ApplyModifiedPropertiesWithoutUndo();
        log.Add("Flower field: " + count + " bubble lilies (all pickable near the player).");
    }

    // ---------------- Field bubbles ----------------

    /// <summary>
    /// Batch, incremental (keeps every other edit in Scene_World): the flower field's instanced lilies are quiet until
    /// picked, so as in Scene_LeafMap the bubbles come from real emitting MeadowFlowers, two per 28 m area of field,
    /// fired in turn by a BubbleCascade. -executeMethod WorldMapDress.AddFieldBubbles
    /// </summary>
    public static void AddFieldBubbles()
    {
        var scene = EditorSceneManager.OpenScene(WorldMapBuild.ScenePath, OpenSceneMode.Single);
        var root = scene.GetRootGameObjects().First(g => g.name == "World — combined map").transform;
        var log = new List<string>();
        FieldBubbles(root, log);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("ArtCaptures/WorldMap");
        File.WriteAllText("ArtCaptures/WorldMap/FieldBubbles.txt", string.Join("\n", log));
    }

    private static void FieldBubbles(Transform root, List<string> log)
    {
        const string GroupName = "05c Field bubble zones — emitters by area";
        var old = root.Find(GroupName); if (old != null) Object.DestroyImmediate(old.gameObject);
        var field = root.GetComponentInChildren<LilyField>(true);
        var data = field != null ? (TextAsset)new SerializedObject(field).FindProperty("data").objectReferenceValue : null;
        if (data == null) { log.Add("No lily field data."); return; }
        var values = new float[data.bytes.Length / 4];
        System.Buffer.BlockCopy(data.bytes, 0, values, 0, data.bytes.Length);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Generated/LeafMap/MeadowFlower.prefab");
        var group = new GameObject(GroupName).transform; group.SetParent(root, false);
        var random = new System.Random(5);
        const float Area = 28f;
        var emitters = new List<BubbleFlower>();
        var cells = Enumerable.Range(0, values.Length / 6)
            .GroupBy(i => new Vector2Int(Mathf.FloorToInt(values[i * 6] / Area), Mathf.FloorToInt(values[i * 6 + 2] / Area)));
        foreach (var cell in cells)
        {
            if (cell.Count() < 25) continue;
            int placed = 0;
            foreach (int i in cell.OrderBy(_ => random.Next()))
            {
                if (placed >= 2) break;
                var at = new Vector3(values[i * 6], values[i * 6 + 1], values[i * 6 + 2]);
                if (emitters.Any(e => (new Vector2(e.transform.position.x, e.transform.position.z) - new Vector2(at.x, at.z)).sqrMagnitude < 49f)) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                go.name = "Field emitter " + (emitters.Count + 1);
                go.transform.rotation = Quaternion.Euler(0f, values[i * 6 + 3], 0f);
                SquirrelTree.PlaceOnGround(go.transform, at + Vector3.up * 0.02f);
                var flower = go.GetComponent<BubbleFlower>();
                flower.ConfigureGroundEmission(false, new Vector2(8f, 14f));
                EditorUtility.SetDirty(flower);
                PrefabUtility.RecordPrefabInstancePropertyModifications(flower);
                if (go.GetComponent<SettleWhenGroundReady>() == null) go.AddComponent<SettleWhenGroundReady>().lift = 0.02f;
                emitters.Add(flower); placed++;
            }
        }
        group.gameObject.AddComponent<BubbleCascade>().Configure(emitters.OrderBy(e => e.transform.position.z).ToList(), 9f);

        // The emitters take the place of the instanced lilies right under them.
        var kept = new List<float>(values.Length);
        int removed = 0;
        for (int i = 0; i < values.Length / 6; i++)
        {
            var p = new Vector2(values[i * 6], values[i * 6 + 2]);
            if (emitters.Any(e => (new Vector2(e.transform.position.x, e.transform.position.z) - p).sqrMagnitude < 1.44f)) { removed++; continue; }
            for (int k = 0; k < 6; k++) kept.Add(values[i * 6 + k]);
        }
        var bytes = new byte[kept.Count * 4];
        System.Buffer.BlockCopy(kept.ToArray(), 0, bytes, 0, bytes.Length);
        string path = AssetDatabase.GetAssetPath(data);
        File.WriteAllBytes(path, bytes);
        AssetDatabase.ImportAsset(path);
        log.Add("Field bubbles: " + emitters.Count + " emitting MeadowFlowers (2 per 28 m area), BubbleCascade every 9 s; " + removed + " lilies under them removed.");
    }

    // ---------------- Decor ----------------

    private static void Decor(Transform root, List<string> log)
    {
        const string GroupName = "09 Region decor — WorldDecor prefabs";
        var old = root.Find(GroupName); if (old != null) Object.DestroyImmediate(old.gameObject);
        var group = new GameObject(GroupName).transform; group.SetParent(root, false);
        var random = new System.Random(1110);
        float R(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
        GameObject[] Load(params string[] kinds) => kinds.SelectMany(k => Enumerable.Range(0, 6)
            .Select(v => AssetDatabase.LoadAssetAtPath<GameObject>(DecorFolder + "/Decor_" + k + "_" + v + ".prefab"))).Where(p => p != null).ToArray();

        var regions = new (string name, GameObject[] prefabs, int count, System.Func<Vector2, bool> where, Vector2 scale)[]
        {
            ("Flower field", Load("Fronds", "GlowSacs", "Polyps", "Ribbons"), 140, p => Oval(p, WorldMapTerrain.FlowerCentre, 92f, 82f) < 0.95f, new Vector2(0.8f, 1.6f)),
            ("Mushroom basin rim", Load("Mushrooms", "GlowSacs", "Bladders", "Polyps"), 90,
                p => { float r = Vector2.Distance(p, WorldMapTerrain.MushroomCentre); return r > WorldMapTerrain.MushroomRadius + 4f && r < WorldMapTerrain.MushroomRadius + 34f; }, new Vector2(0.9f, 1.8f)),
            ("Forest floor", Load("Creepers", "CavePebbles", "Pebbles", "Fronds", "NeedleBeards"), 170, p => p.y < -20f && Oval(p, WorldMapTerrain.FlowerCentre, 92f, 82f) > 1f, new Vector2(0.8f, 1.5f)),
            ("Crack lips", Load("Crystals", "Shards", "NeedleBeards", "HangingVeils"), 110,
                p => { float d = WorldMapTerrain.CrackDistance(p, out float t); float w = WorldMapTerrain.CrackHalfWidth(t); return d > w + 1.5f && d < w + 14f; }, new Vector2(0.9f, 2f)),
            ("Ruins", Load("Obelisks", "Totems", "Cairns", "Halos", "Arches"), 80,
                p => p.y > 190f && Vector2.Distance(p, WorldMapTerrain.SpireSpot) > 36f, new Vector2(1f, 2.2f)),
        };
        int total = 0;
        foreach (var region in regions)
        {
            if (region.prefabs.Length == 0) continue;
            var sub = new GameObject(region.name).transform; sub.SetParent(group, false);
            int placed = 0;
            for (int tries = 0; tries < region.count * 40 && placed < region.count; tries++)
            {
                var p = new Vector2(R(-250f, 250f), R(-280f, 300f));
                if (!region.where(p)) continue;
                if (WorldMapTerrain.InGooExterior(p.x, p.y)) continue;
                if (Vector2.Distance(p, WorldMapTerrain.Spawn) < 12f || Vector2.Distance(p, WorldMapTerrain.CaveMouth) < WorldMapTerrain.GooMouthRadius + 4f) continue;
                if (WorldMapTerrain.CrackDistance(p, out float t) < WorldMapTerrain.CrackHalfWidth(t) + 1.5f) continue;
                if (Vector2.Distance(p, WorldMapTerrain.MushroomCentre) < WorldMapTerrain.MushroomRadius + 2f) continue;
                float h = WorldMapTerrain.Height(p.x, p.y);
                if (h > 45f) continue;
                var prefab = region.prefabs[random.Next(region.prefabs.Length)];
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, sub);
                go.transform.SetPositionAndRotation(new Vector3(p.x, h - 0.15f, p.y), Quaternion.Euler(0f, R(0f, 360f), 0f));
                go.transform.localScale *= R(region.scale.x, region.scale.y);
                placed++;
            }
            total += placed;
        }
        log.Add("Region decor: " + total + " WorldDecor prefabs.");
    }

    private static float Oval(Vector2 p, Vector2 c, float rx, float rz) => new Vector2((p.x - c.x) / rx, (p.y - c.y) / rz).magnitude;

    // ---------------- Saved chunk meshes ----------------

    /// <summary>Saves every chunk mesh so Play only uploads them (run after the terrain and grafts are final).</summary>
    public static void BakeChunks(ProceduralWorld world, List<string> log)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        byte[] bytes = world.BuildChunkCache();
        string path = Folder + "/TerrainChunks.bytes";
        File.WriteAllBytes(path, bytes);
        AssetDatabase.ImportAsset(path);
        world.ChunkCache = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
        EditorUtility.SetDirty(world);
        log.Add("Saved chunk meshes: " + (bytes.Length / 1048576f).ToString("0.0") + " MB in " + timer.Elapsed.TotalSeconds.ToString("0") + " s.");
    }
}
