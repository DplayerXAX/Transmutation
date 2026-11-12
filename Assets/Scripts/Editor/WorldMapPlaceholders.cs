using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Placeholder set dressing for Scene_World (Daniel: placeholder forest and mountains are fine): a dark conifer forest
/// round the spawn and the Goo cave, and broken columns, wall stubs and arches on the ruin terraces round the spire.
/// Meshes are batched per 60 m cell and saved under Assets/Generated/WorldMap; trunks and ruins get simple colliders.
/// </summary>
public static class WorldMapPlaceholders
{
    private const string Folder = "Assets/Generated/WorldMap";
    private const string ForestGroup = "02 Placeholder forest";
    private const string RuinGroup = "04b Placeholder ruins — broken columns and walls";

    public static void Build(Transform root, List<string> log)
    {
        Directory.CreateDirectory(Folder + "/Meshes");
        foreach (string old in AssetDatabase.FindAssets("t:Mesh", new[] { Folder + "/Meshes" }))
            AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(old));
        Material bark = Mat("WorldPlaceholder_Bark", new Color(0.16f, 0.13f, 0.17f));
        Material needles = Mat("WorldPlaceholder_Canopy", new Color(0.11f, 0.24f, 0.27f));
        Material stone = Mat("WorldPlaceholder_RuinStone", new Color(0.62f, 0.6f, 0.66f));
        log.Add("Forest trees: " + Forest(root, bark, needles));
        log.Add("Ruin pieces: " + Ruins(root, stone));
    }

    private static Material Mat(string name, Color colour)
    {
        string path = Folder + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", colour); m.SetFloat("_Smoothness", 0.15f);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------------- Forest ----------------

    private static int Forest(Transform root, Material bark, Material needles)
    {
        var old = root.Find(ForestGroup); if (old != null) Object.DestroyImmediate(old.gameObject);
        var group = new GameObject(ForestGroup).transform; group.SetParent(root, false);
        var random = new System.Random(4207);
        var cells = new Dictionary<Vector2Int, (List<CombineInstance> trunks, List<CombineInstance> crowns)>();
        int count = 0;
        for (int i = 0; i < 20000 && count < 520; i++)
        {
            float x = Mathf.Lerp(-250f, 250f, (float)random.NextDouble());
            float z = Mathf.Lerp(-290f, 140f, (float)random.NextDouble());
            if (!ForestAt(x, z, random)) continue;
            float y = WorldMapTerrain.Height(x, z);
            float size = Mathf.Lerp(0.8f, 1.6f, (float)random.NextDouble());
            float height = Mathf.Lerp(9f, 17f, (float)random.NextDouble()) * size;
            Quaternion spin = Quaternion.Euler(((float)random.NextDouble() - 0.5f) * 6f, (float)random.NextDouble() * 360f, ((float)random.NextDouble() - 0.5f) * 6f);
            Vector3 at = new Vector3(x, y - 0.4f, z);
            var key = new Vector2Int(Mathf.FloorToInt(x / 60f), Mathf.FloorToInt(z / 60f));
            if (!cells.TryGetValue(key, out var lists)) cells[key] = lists = (new List<CombineInstance>(), new List<CombineInstance>());
            lists.trunks.Add(new CombineInstance { mesh = Prism(6, 0.38f * size, 0.16f * size, height * 0.55f, 0f), transform = Matrix4x4.TRS(at, spin, Vector3.one) });
            int tiers = 3 + random.Next(2);
            for (int t = 0; t < tiers; t++)
            {
                float f = t / (float)tiers;
                float baseY = height * (0.28f + f * 0.58f), radius = Mathf.Lerp(3.1f, 1.2f, f) * size, tall = height * 0.36f;
                lists.crowns.Add(new CombineInstance { mesh = Cone(9, radius, tall, (float)random.NextDouble()), transform = Matrix4x4.TRS(at + spin * Vector3.up * baseY, spin, Vector3.one) });
            }
            var trunk = new GameObject("Tree trunk collider"); trunk.transform.SetParent(group, false);
            trunk.transform.SetPositionAndRotation(at, spin);
            var capsule = trunk.AddComponent<CapsuleCollider>(); capsule.radius = 0.35f * size; capsule.height = height * 0.6f; capsule.center = Vector3.up * height * 0.3f;
            count++;
        }
        int n = 0;
        foreach (var pair in cells)
        {
            Batch(group, "Forest cell " + pair.Key, pair.Value.trunks, bark, n++);
            Batch(group, "Forest cell " + pair.Key + " crowns", pair.Value.crowns, needles, n++);
        }
        return count;
    }

    /// <summary>Clumpy forest round the spawn and behind it, thinning into the flower slope; never on routes or regions.</summary>
    private static bool ForestAt(float x, float z, System.Random random)
    {
        var p = new Vector2(x, z);
        if (Vector2.Distance(p, WorldMapTerrain.Spawn) < 23f) return false;
        if (WorldMapTerrain.Distance(p, WorldMapTerrain.Spawn, WorldMapTerrain.Spawn + new Vector2(13f, 8f), out _) < 11f) return false;
        if (WorldMapTerrain.Distance(p, WorldMapTerrain.Spawn + new Vector2(13f, 8f), WorldMapTerrain.SpawnExit, out _) < 11f) return false;
        if (WorldMapTerrain.InGooExterior(x, z)) return false;
        if (Vector2.Distance(p, WorldMapTerrain.MushroomCentre) < 118f) return false;
        if (new Vector2((x - WorldMapTerrain.FlowerCentre.x) / 100f, (z - WorldMapTerrain.FlowerCentre.y) / 92f).magnitude < 0.85f) return false;
        if (WorldMapTerrain.CrackDistance(p, out float t) < WorldMapTerrain.CrackHalfWidth(t) + 10f) return false;
        // Keep the walk from the spawn to the regions open.
        if (WorldMapTerrain.Distance(p, new Vector2(-10f, -40f), new Vector2(-70f, 62f), out _) < 9f) return false;
        if (WorldMapTerrain.Distance(p, new Vector2(5f, -20f), new Vector2(82f, 40f), out _) < 10f) return false;
        if (WorldMapTerrain.Distance(p, WorldMapTerrain.Spawn, new Vector2(10f, 120f), out _) < 9f) return false;
        float h = WorldMapTerrain.Height(x, z);
        if (h > 48f) return false;
        float clump = Mathf.PerlinNoise(x * 0.025f + 17f, z * 0.025f + 4f);
        float south = Mathf.SmoothStep(0f, 1f, (40f - z) / 90f);
        float chance = Mathf.Clamp01((clump - 0.32f) * 2.2f) * Mathf.Lerp(0.15f, 1f, south);
        return random.NextDouble() < chance;
    }

    // ---------------- Ruins ----------------

    private static int Ruins(Transform root, Material stone)
    {
        var old = root.Find(RuinGroup); if (old != null) Object.DestroyImmediate(old.gameObject);
        var group = new GameObject(RuinGroup).transform; group.SetParent(root, false);
        var random = new System.Random(911);
        var pieces = new List<CombineInstance>();
        int count = 0;
        Vector2 spire = WorldMapTerrain.SpireSpot;
        // Rings of colonnades round the spire plaza, most columns broken, some fallen.
        for (int ring = 0; ring < 3; ring++)
        {
            float rx = 44f + ring * 34f, rz = 26f + ring * 18f;
            int columns = 14 + ring * 6;
            for (int i = 0; i < columns; i++)
            {
                if (random.NextDouble() < 0.22) continue;
                float a = (i + (float)random.NextDouble() * 0.3f) / columns * Mathf.PI * 2f;
                float x = spire.x + Mathf.Cos(a) * rx, z = spire.y + Mathf.Sin(a) * rz;
                if (WorldMapTerrain.CrackDistance(new Vector2(x, z), out float t) < WorldMapTerrain.CrackHalfWidth(t) + 6f) continue;
                float y = WorldMapTerrain.Height(x, z);
                bool fallen = random.NextDouble() < 0.18;
                float tall = fallen ? Mathf.Lerp(6f, 12f, (float)random.NextDouble()) : Mathf.Lerp(3f, 16f, (float)random.NextDouble()) * (1f + ring * 0.25f);
                float radius = Mathf.Lerp(0.9f, 1.5f, (float)random.NextDouble());
                Quaternion rot = fallen ? Quaternion.Euler(88f, (float)random.NextDouble() * 360f, 0f) : Quaternion.Euler(((float)random.NextDouble() - 0.5f) * 8f, (float)random.NextDouble() * 360f, ((float)random.NextDouble() - 0.5f) * 8f);
                Vector3 at = new Vector3(x, y - (fallen ? -radius * 0.8f : 0.6f), z);
                pieces.Add(new CombineInstance { mesh = Prism(8, radius, radius * 0.92f, tall, fallen ? 0f : 0.35f + (float)random.NextDouble() * 0.5f), transform = Matrix4x4.TRS(at, rot, Vector3.one) });
                pieces.Add(new CombineInstance { mesh = Prism(8, radius * 1.35f, radius * 1.3f, 0.9f, 0f), transform = Matrix4x4.TRS(at, rot, Vector3.one) });
                Collider(group, at, rot, new Vector3(radius * 1.7f, tall, radius * 1.7f), Vector3.up * tall * 0.5f);
                count++;
            }
        }
        // Wall stubs and broken arches on the terraces, facing the spire.
        for (int i = 0; i < 26; i++)
        {
            float a = (float)random.NextDouble() * Mathf.PI * 2f, d = Mathf.Lerp(0.35f, 1.05f, (float)random.NextDouble());
            float x = spire.x + Mathf.Cos(a) * 150f * d, z = spire.y - 10f + Mathf.Sin(a) * 72f * d;
            if (WorldMapTerrain.CrackDistance(new Vector2(x, z), out float t) < WorldMapTerrain.CrackHalfWidth(t) + 8f) continue;
            if (Vector2.Distance(new Vector2(x, z), spire) < 34f) continue;
            float y = WorldMapTerrain.Height(x, z);
            Quaternion rot = Quaternion.LookRotation(new Vector3(spire.x - x, 0f, spire.y - z)) * Quaternion.Euler(0f, 90f, 0f);
            Vector3 at = new Vector3(x, y - 0.5f, z);
            float length = Mathf.Lerp(8f, 22f, (float)random.NextDouble()), tall = Mathf.Lerp(3f, 10f, (float)random.NextDouble());
            bool arch = random.NextDouble() < 0.35;
            if (arch)
            {
                float span = length * 0.5f;
                for (int side = -1; side <= 1; side += 2)
                    pieces.Add(new CombineInstance { mesh = Box(new Vector3(1.6f, tall + 4f, 1.8f), 0.6f, random), transform = Matrix4x4.TRS(at + rot * new Vector3(side * span * 0.5f, 0f, 0f), rot, Vector3.one) });
                if (random.NextDouble() < 0.6)
                    pieces.Add(new CombineInstance { mesh = Box(new Vector3(span + 2.4f, 1.4f, 1.8f), 0.4f, random), transform = Matrix4x4.TRS(at + rot * new Vector3(0f, tall + 4f, 0f), rot, Vector3.one) });
                Collider(group, at + rot * new Vector3(-span * 0.5f, 0, 0), rot, new Vector3(1.6f, tall + 4f, 1.8f), Vector3.up * (tall + 4f) * 0.5f);
                Collider(group, at + rot * new Vector3(span * 0.5f, 0, 0), rot, new Vector3(1.6f, tall + 4f, 1.8f), Vector3.up * (tall + 4f) * 0.5f);
            }
            else
            {
                pieces.Add(new CombineInstance { mesh = Box(new Vector3(length, tall, 1.5f), tall * 0.45f, random), transform = Matrix4x4.TRS(at, rot, Vector3.one) });
                Collider(group, at, rot, new Vector3(length, tall * 0.8f, 1.5f), Vector3.up * tall * 0.4f);
            }
            count++;
        }
        for (int i = 0; i < pieces.Count; i += 60)
            Batch(group, "Ruin stones " + i / 60, pieces.GetRange(i, Mathf.Min(60, pieces.Count - i)), stone, 1000 + i);
        return count;
    }

    private static void Collider(Transform group, Vector3 at, Quaternion rot, Vector3 size, Vector3 centre)
    {
        var go = new GameObject("Ruin collider"); go.transform.SetParent(group, false); go.transform.SetPositionAndRotation(at, rot);
        go.layer = 7;
        var box = go.AddComponent<BoxCollider>(); box.size = size; box.center = centre;
    }

    // ---------------- Meshes ----------------

    private static void Batch(Transform group, string name, List<CombineInstance> parts, Material material, int index)
    {
        if (parts.Count == 0) return;
        var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(parts.ToArray(), true, true);
        mesh.RecalculateBounds();
        foreach (var part in parts) Object.DestroyImmediate(part.mesh);
        AssetDatabase.CreateAsset(mesh, Folder + "/Meshes/Placeholder_" + index.ToString("0000") + ".asset");
        var go = new GameObject(name); go.transform.SetParent(group, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);
    }

    /// <summary>Tapered prism, flat shaded; brokenTop tilts the top cut like a snapped column.</summary>
    private static Mesh Prism(int sides, float bottom, float top, float height, float brokenTop)
    {
        var v = new List<Vector3>(); var tri = new List<int>();
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
            float y0 = height * (1f - brokenTop * 0.5f * (1f + d0.x)), y1 = height * (1f - brokenTop * 0.5f * (1f + d1.x));
            int b = v.Count;
            v.Add(d0 * bottom); v.Add(d1 * bottom); v.Add(d1 * top + Vector3.up * y1); v.Add(d0 * top + Vector3.up * y0);
            tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            int c = v.Count;
            v.Add(new Vector3(0, height * (1f - brokenTop * 0.5f), 0)); v.Add(d1 * top + Vector3.up * y1); v.Add(d0 * top + Vector3.up * y0);
            tri.AddRange(new[] { c, c + 1, c + 2 });
        }
        return Finish(v, tri);
    }

    private static Mesh Cone(int sides, float radius, float height, float seed)
    {
        var v = new List<Vector3>(); var tri = new List<int>();
        Vector3 tip = Vector3.up * height;
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
            float r0 = radius * (0.85f + 0.3f * Mathf.PerlinNoise(seed * 10f + i, 0.5f)), r1 = radius * (0.85f + 0.3f * Mathf.PerlinNoise(seed * 10f + (i + 1) % sides, 0.5f));
            Vector3 p0 = new Vector3(Mathf.Cos(a0) * r0, -0.4f, Mathf.Sin(a0) * r0), p1 = new Vector3(Mathf.Cos(a1) * r1, -0.4f, Mathf.Sin(a1) * r1);
            int b = v.Count; v.Add(p0); v.Add(tip); v.Add(p1); tri.AddRange(new[] { b, b + 1, b + 2 });
            int c = v.Count; v.Add(p0); v.Add(p1); v.Add(Vector3.up * height * 0.12f); tri.AddRange(new[] { c, c + 1, c + 2 });
        }
        return Finish(v, tri);
    }

    /// <summary>Box with a ragged, broken top edge.</summary>
    private static Mesh Box(Vector3 size, float ragged, System.Random random)
    {
        int steps = Mathf.Max(2, Mathf.RoundToInt(size.x / 2f));
        var heights = new float[steps + 1];
        for (int i = 0; i <= steps; i++) heights[i] = size.y - (float)random.NextDouble() * ragged;
        var v = new List<Vector3>(); var tri = new List<int>();
        float hx = size.x * 0.5f, hz = size.z * 0.5f;
        for (int i = 0; i < steps; i++)
        {
            float x0 = -hx + size.x * i / steps, x1 = -hx + size.x * (i + 1) / steps;
            foreach (float zz in new[] { -hz, hz })
            {
                int b = v.Count;
                v.Add(new Vector3(x0, 0, zz)); v.Add(new Vector3(x1, 0, zz)); v.Add(new Vector3(x1, heights[i + 1], zz)); v.Add(new Vector3(x0, heights[i], zz));
                if (zz < 0) tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 }); else tri.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            int c = v.Count;
            v.Add(new Vector3(x0, heights[i], -hz)); v.Add(new Vector3(x1, heights[i + 1], -hz)); v.Add(new Vector3(x1, heights[i + 1], hz)); v.Add(new Vector3(x0, heights[i], hz));
            tri.AddRange(new[] { c, c + 2, c + 1, c, c + 3, c + 2 });
        }
        foreach (float xx in new[] { -hx, hx })
        {
            float top = xx < 0 ? heights[0] : heights[steps];
            int b = v.Count;
            v.Add(new Vector3(xx, 0, -hz)); v.Add(new Vector3(xx, 0, hz)); v.Add(new Vector3(xx, top, hz)); v.Add(new Vector3(xx, top, -hz));
            if (xx < 0) tri.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 }); else tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }
        return Finish(v, tri);
    }

    private static Mesh Finish(List<Vector3> v, List<int> tri)
    {
        var mesh = new Mesh(); mesh.SetVertices(v); mesh.SetTriangles(tri, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }
}
