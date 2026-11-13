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

        // The mushroom basin floor uses the same ground shader (so it reads as the same land), more strongly tinted.
        var basinFloor = Variant("Assets/Generated/LeafMap/Leaf_InkStone.mat", "World_FungalFloor", m =>
        {
            m.SetFloat("_IridescentAmount", 0f); m.SetFloat("_IridescentLines", 0f); m.SetFloat("_IridescentScale", 0.015f);
        });
        var basin = root.Find("06 Mushroom basin — Scene_FungalHollow (marcus)");
        int walls = 0;
        if (basin != null)
        {
            foreach (var r in basin.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name == "Cavern Floor") { r.sharedMaterial = basinFloor; continue; }
                if (r.GetComponent<SlipperySurface>() != null || r.name.StartsWith("Rough Chimney"))
                {
                    var src = r.sharedMaterial;
                    bool rough = r.name.StartsWith("Rough");
                    r.sharedMaterial = Variant(AssetDatabase.GetAssetPath(src), rough ? "World_FungalRoughDark" : "World_FungalSlimeDark", m =>
                    {
                        // Near-black with a coloured gloss, instead of the bright green walls.
                        Color tint = rough ? new Color(0.07f, 0.05f, 0.09f) : new Color(0.03f, 0.07f, 0.08f);
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
                        if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
                        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", rough ? 0.2f : 0.85f);
                        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", rough ? 0f : 0.6f);
                        if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", rough ? new Color(0.05f, 0.02f, 0.08f) : new Color(0.0f, 0.06f, 0.07f)); }
                    });
                    walls++;
                }
            }
        }
        log.Add("Colourful black: terrain World_InkStone, basin floor World_FungalFloor, " + walls + " basin walls darkened.");
    }

    // ---------------- Floating stones ----------------

    /// <summary>
    /// A wandering climb from the flower field to the crystal island's west edge: easy, wide stones close together at
    /// the bottom; small, far-apart stones with big height changes near the top (bubble flowers help there).
    /// Decoy stones float round the route. Uses the leaf map's SkyIslands component and bubble flowers.
    /// </summary>
    private static void Stones(Transform root, List<string> log)
    {
        var islands = root.GetComponentInChildren<SkyIslands>(true);
        if (islands == null) { log.Add("No SkyIslands component to reuse."); return; }
        islands.transform.SetParent(root, true);
        islands.transform.position = Vector3.zero;
        islands.name = "07 Floating stones — flower field up to the crystal island";

        float top = IslandTop(new Vector3(-92f, 0f, 22f));
        Vector3 altar = WorldMapTerrain.IslandOffset + new Vector3(-104f, top + 0.4f, 26f);
        var random = new System.Random(20261110);
        float R(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
        Vector2 island = new Vector2(WorldMapTerrain.IslandOffset.x, WorldMapTerrain.IslandOffset.z);

        var stones = new List<Vector3>(); var radii = new List<float>(); var scenery = new List<bool>();
        Vector2 start2 = new Vector2(-150f, 62f);
        Vector3 at = new Vector3(start2.x, WorldMapTerrain.Height(start2.x, start2.y) + 1.6f, start2.y);
        float startY = at.y;
        stones.Add(at); radii.Add(3.4f); scenery.Add(false);
        // An irregular, wobbling spiral round a column of air west of the island: its radius breathes, it sometimes
        // turns back on itself, steps go up and now and then down. Easy (wide, close, low) at the bottom; small, far
        // and steep near the top. Every hop stays within the player's reach (gap + 1.4 x rise <= 5.4 m, rise <= 1.7 m).
        Vector2 column = new Vector2(-140f, 80f);
        float angle = Mathf.Atan2(at.z - column.y, at.x - column.x), ring = Mathf.Clamp(Vector2.Distance(new Vector2(at.x, at.z), column), 10f, 24f);
        float turn = 1f;
        int guard = 0;
        float stopAt = altar.y - 7f;
        while (at.y < stopAt && guard++ < 600)
        {
            float t = Mathf.Clamp01((at.y - startY) / (altar.y - startY));
            float d = Mathf.Pow(t, 1.15f);
            float radius = Mathf.Lerp(3.1f, 1.35f, d) * R(0.8f, 1.2f);
            float gap = Mathf.Lerp(0.8f, 3.3f, d) + R(0f, Mathf.Lerp(0.8f, 1.3f, d));
            float rise = random.NextDouble() < Mathf.Lerp(0.18f, 0.1f, d) ? R(-1.6f, -0.4f) : R(0.3f, Mathf.Lerp(1.1f, 1.7f, d));
            if (gap + Mathf.Max(0f, rise) * 1.4f > 5.4f) rise = Mathf.Max(0.2f, (5.4f - gap) / 1.4f);
            rise = Mathf.Min(rise, 1.7f);
            if (random.NextDouble() < 0.12) turn = -turn;
            ring = Mathf.Clamp(ring + R(-5f, 5f), 9f, 26f);
            float hop = radii[radii.Count - 1] + gap + radius;
            float nextAngle = angle + turn * hop / ring * R(0.85f, 1.15f);
            Vector3 next = new Vector3(column.x + Mathf.Cos(nextAngle) * ring, at.y + rise, column.y + Mathf.Sin(nextAngle) * ring);
            // Keep the real hop length (the ring may have changed) within reach.
            Vector2 flatStep = new Vector2(next.x - at.x, next.z - at.z);
            float edgeGap = flatStep.magnitude - radii[radii.Count - 1] - radius;
            if (edgeGap > 4.6f || edgeGap < 0.4f || edgeGap + Mathf.Max(0f, rise) * 1.4f > 5.6f) continue;
            float ground = WorldMapTerrain.Height(next.x, next.z);
            if (next.y < ground + 1.2f) next.y = Mathf.Min(ground + 1.2f, at.y + 1.7f);
            if (next.y < ground + 0.6f) continue;
            if (stones.Any(s => Vector3.Distance(s, next) < radius + 2.6f)) continue;
            at = next; angle = nextAngle;
            stones.Add(at); radii.Add(radius); scenery.Add(false);
        }
        log.Add("Floating stones: spiral reached " + at.y.ToString("0") + " m (" + stones.Count + " stones, " + guard + " tries).");
        // Last small, far hops across to the altar stone at the island's edge, still climbing.
        while (guard++ < 700)
        {
            Vector3 step = altar - at; step.y = 0f;
            float left = step.magnitude - radii[radii.Count - 1] - 5.5f;
            if (left <= 4.4f && altar.y - at.y <= 1.7f) break;
            float radius = R(1.3f, 1.7f);
            float hop = radii[radii.Count - 1] + Mathf.Min(R(2.6f, 3.6f), Mathf.Max(0.6f, left - radius * 2f)) + radius;
            at += step.normalized * Mathf.Min(hop, step.magnitude - 6f);
            at.y = Mathf.Min(at.y + R(0.6f, 1.5f), altar.y - 0.3f);
            at += new Vector3(R(-1.2f, 1.2f), 0f, R(-1.2f, 1.2f));
            stones.Add(at); radii.Add(radius); scenery.Add(false);
        }
        stones.Add(altar); radii.Add(5.5f); scenery.Add(false);
        int path = stones.Count;
        float maxRise = 0f, maxGap = 0f;
        for (int i = 1; i < path; i++)
        {
            maxRise = Mathf.Max(maxRise, stones[i].y - stones[i - 1].y);
            maxGap = Mathf.Max(maxGap, Vector2.Distance(new Vector2(stones[i].x, stones[i].z), new Vector2(stones[i - 1].x, stones[i - 1].z)) - radii[i] - radii[i - 1]);
        }
        log.Add("Floating stones: largest rise " + maxRise.ToString("0.0") + " m, widest gap " + maxGap.ToString("0.0") + " m.");

        // Decoys: drifting stones round the route at every height, some almost reachable.
        for (int i = 0; i < 45; i++)
        {
            Vector3 near = stones[random.Next(path - 1)];
            Vector3 decoy = near + new Vector3(R(-14f, 14f), R(-6f, 7f), R(-14f, 14f));
            if (decoy.y < WorldMapTerrain.Height(decoy.x, decoy.z) + 3f) continue;
            if (stones.Any(s => Vector3.Distance(s, decoy) < 6f)) continue;
            stones.Insert(stones.Count - 1, decoy); radii.Insert(radii.Count - 1, R(1.2f, 3.2f)); scenery.Insert(scenery.Count - 1, true);
        }
        islands.stones = stones.ToArray(); islands.radii = radii.ToArray(); islands.scenery = scenery.ToArray();

        // Bubble flowers on the hardest stones (top half), each aiming its bubbles at the gap to the next stone.
        var flowers = islands.GetComponentsInChildren<BubbleFlower>(true);
        var guide = islands.GetComponentInChildren<SkyIslandBubbleGuide>(true);
        if (guide != null) guide.lanes.Clear();
        var hard = Enumerable.Range(0, path - 1).Where(i => !scenery[i] && (stones[i].y - startY) / (altar.y - startY) > 0.35f).ToList();
        for (int f = 0; f < flowers.Length; f++)
        {
            if (hard.Count == 0) { flowers[f].gameObject.SetActive(false); continue; }
            int i = hard[Mathf.Min(hard.Count - 1, f * hard.Count / Mathf.Max(1, flowers.Length))];
            int next = i + 1; while (next < stones.Count && scenery[next]) next++;
            flowers[f].transform.position = stones[i] + new Vector3(radii[i] * 0.3f, 0.12f, 0f);
            if (flowers[f].GetComponent<SettleWhenGroundReady>() == null) flowers[f].gameObject.AddComponent<SettleWhenGroundReady>().lift = 0.12f;
            if (guide != null && next < stones.Count)
                guide.lanes.Add(new SkyIslandBubbleGuide.Lane { flower = flowers[f], target = Vector3.Lerp(stones[i], stones[next], 0.55f) });
        }
        EditorUtility.SetDirty(islands); if (guide != null) EditorUtility.SetDirty(guide);
        log.Add("Floating stones: " + path + " on the route (" + startY.ToString("0") + " m to " + altar.y.ToString("0") + " m), " + (stones.Count - path) + " decoys, " + flowers.Length + " bubble flowers on the upper half.");
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
