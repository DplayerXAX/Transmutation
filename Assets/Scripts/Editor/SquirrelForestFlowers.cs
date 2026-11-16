using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SquirrelForestFlowers
{
    const string Folder = "Assets/Generated/SquirrelForest/Flowers";
    const string Request = "Temp/SquirrelForest.flowers";
    static SquirrelForestFlowers() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Apply(); } catch (Exception e) { File.WriteAllText("ArtCaptures/SquirrelForest/Flowers.txt", e.ToString()); Debug.LogException(e); }
    }

    [MenuItem("Tools/Capstone/Squirrel Forest/Plant Flowers And Glow Bees")]
    public static void Apply()
    {
        var previous = SceneManager.GetActiveScene(); var scene = SceneManager.GetSceneByPath(SquirrelForestSetup.ScenePath);
        bool loaded = scene.IsValid() && scene.isLoaded;
        if (!loaded) scene = EditorSceneManager.OpenScene(SquirrelForestSetup.ScenePath, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var roots = scene.GetRootGameObjects();
            var forest = roots.SelectMany(g => g.GetComponentsInChildren<SilkForestManager>()).Single();
            var ground = roots.SelectMany(g => g.GetComponentsInChildren<MeshCollider>()).First(c => c.gameObject.layer == 7);
            var manager = roots.SelectMany(g => g.GetComponentsInChildren<SquirrelSystemManager>()).Single();
            var args = new object[] { 6, null };
            typeof(SquirrelSystemManager).GetMethod("TryPlanSpreadHomes", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(manager, args);
            var homes = (List<Vector3>)args[1];
            var old = roots.FirstOrDefault(g => g.name == "Forest flowers and glow bee meadow"); if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("Forest flowers and glow bee meadow").transform;
            var random = new System.Random(16026); float R(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
            bool Ground(float x, float z, out RaycastHit hit)
            {
                hit = default;
                if (Mathf.Abs(x - Mathf.Sin(z * 0.065f) * 2.3f) < 1.3f) return false;
                if (!ground.Raycast(new Ray(new Vector3(x, 180, z), Vector3.down), out hit, 360) || hit.normal.y < 0.8f) return false;
                Vector3 point = hit.point;
                return !homes.Any(p => Vector3.ProjectOnPlane(p - point, Vector3.up).sqrMagnitude < 2.6f * 2.6f);
            }
            Material Mat(string name, Color color, float emission)
            {
                string path = Folder + "/" + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
                m.SetColor("_BaseColor", color); m.SetFloat("_Smoothness", 0.35f); m.SetFloat("_Cull", 0);
                m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * emission); m.enableInstancing = true; EditorUtility.SetDirty(m); return m;
            }
            Mesh Asset(string name, SilkForestGeometry.MeshBuffer buffer)
            {
                var mesh = buffer.CreateMesh(name); string path = Folder + "/" + name + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing == null) AssetDatabase.CreateAsset(mesh, path);
                else { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(mesh); }
                return mesh;
            }
            var green = Mat("HyacinthLeaves", new Color(0.17f, 0.36f, 0.29f), 0.07f);
            var colors = new[] { new Color(0.48f, 0.32f, 0.89f), new Color(0.4f, 0.62f, 1f), new Color(0.89f, 0.55f, 0.76f), new Color(0.89f, 0.91f, 0.75f) };
            var petals = colors.Select((c, i) => Mat("Petals_" + i, c, 0.22f)).ToArray();
            var stalk = new SilkForestGeometry.MeshBuffer(); var bells = new SilkForestGeometry.MeshBuffer();
            stalk.Tube(new[] { Vector3.zero, new Vector3(0.025f, 0.55f, 0), new Vector3(0, 1.05f, 0) }, 0.017f, 6, Color.white, true);
            for (int leaf = 0; leaf < 5; leaf++)
            {
                float a = leaf * 2.4f; Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); Vector3 side = Vector3.Cross(d, Vector3.up) * 0.055f;
                Vector3 mid = d * 0.12f + Vector3.up * 0.28f, tip = d * 0.29f + Vector3.up * (0.42f + leaf * 0.035f);
                stalk.Quad(Vector3.zero, mid + side, tip, mid - side, Color.white);
            }
            for (int b = 0; b < 26; b++)
            {
                float a = b * 2.39996f; Vector3 outward = new Vector3(Mathf.Cos(a), -0.45f, Mathf.Sin(a)).normalized;
                Vector3 center = new Vector3(Mathf.Cos(a) * 0.035f, 0.48f + b * 0.0205f, Mathf.Sin(a) * 0.035f);
                Bell(bells, center, outward, 0.13f, 0.058f);
            }
            var stemMesh = Asset("HyacinthStemLeaves", stalk); var bellMesh = Asset("HyacinthBellSpike", bells);
            var daisy = new SilkForestGeometry.MeshBuffer();
            daisy.Tube(new[] { Vector3.zero, Vector3.up * 0.38f }, 0.012f, 5, Color.white);
            for (int i = 0; i < 7; i++)
            {
                float a = i * Mathf.PI * 2 / 7; Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)), side = Vector3.Cross(d, Vector3.up) * 0.045f, c = Vector3.up * 0.38f;
                daisy.Quad(c, c + d * 0.09f + side + Vector3.up * 0.025f, c + d * 0.2f, c + d * 0.09f - side + Vector3.up * 0.025f, Color.white);
            }
            var originalVertices = daisy.vertices.ToArray(); var originalIndices = daisy.indices.ToArray();
            for (int head = 1; head < 5; head++)
            {
                int start = daisy.vertices.Count; float angle = head * 2.39996f;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 0.28f;
                foreach (var vertex in originalVertices) daisy.Vertex(vertex * (0.65f + head * 0.06f) + offset, Color.white, Vector2.zero);
                foreach (int index in originalIndices) daisy.indices.Add(start + index);
            }
            var smallMesh = Asset("ForestStarFlower", daisy);
            var cups = new SilkForestGeometry.MeshBuffer(); var smallBells = new SilkForestGeometry.MeshBuffer();
            for (int flower = 0; flower < 5; flower++)
            {
                float a = flower * 2.4f; Vector3 foot = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (flower == 0 ? 0 : 0.27f);
                Vector3 top = foot + Vector3.up * (0.22f + flower * 0.035f);
                cups.Tube(new[] { foot, top }, 0.009f, 5, Color.white);
                smallBells.Tube(new[] { foot, top + Vector3.up * 0.16f }, 0.009f, 5, Color.white);
                Bell(smallBells, top + Vector3.up * 0.17f, new Vector3(0.3f, -0.8f, 0.1f).normalized, 0.12f, 0.075f);
                for (int petal = 0; petal < 5; petal++)
                {
                    float angle = petal * Mathf.PI * 0.4f; Vector3 d = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    Vector3 side = Vector3.Cross(d, Vector3.up) * 0.075f;
                    cups.Quad(top, top + d * 0.1f + side + Vector3.up * 0.04f,
                        top + d * 0.16f + Vector3.up * 0.09f, top + d * 0.1f - side + Vector3.up * 0.04f, Color.white);
                }
            }
            var meadowMeshes = new[] { smallMesh, Asset("ForestCupFlowers", cups), Asset("ForestBellFlowers", smallBells) };
            void Part(Transform parent, Mesh mesh, Material material, string name)
            {
                var go = new GameObject(name); go.transform.SetParent(parent, false); go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off;
            }
            var centers = new List<Vector3>(); var patches = new List<List<NectarFlower>>();
            for (int attempt = 0; centers.Count < 24 && attempt < 30000; attempt++)
            {
                if (!Ground(R(-42, 42), R(-42, 42), out var hit) || centers.Any(p => Vector3.ProjectOnPlane(p - hit.point, Vector3.up).sqrMagnitude < 13f * 13f)) continue;
                centers.Add(hit.point); var flowers = new List<NectarFlower>(); patches.Add(flowers);
                var patch = new GameObject($"Hyacinth patch {centers.Count:00}").transform; patch.SetParent(root, false); patch.position = hit.point;
                for (int i = 0; i < 10; i++)
                {
                    float a = i * 2.4f, radius = Mathf.Sqrt(i / 10f) * 1.4f;
                    if (!Ground(hit.point.x + Mathf.Cos(a) * radius, hit.point.z + Mathf.Sin(a) * radius, out var floor)) continue;
                    var flower = new GameObject("Hyacinth nectar flower"); flower.transform.SetParent(patch, false);
                    flower.transform.SetPositionAndRotation(floor.point, Quaternion.Euler(0, R(0, 360), 0)); flower.transform.localScale = Vector3.one * R(0.72f, 1.12f);
                    Part(flower.transform, stemMesh, green, "Lance leaves and stem"); Part(flower.transform, bellMesh, petals[(centers.Count / 2) % petals.Length], "Dense hanging six-lobed bells");
                    var nectar = flower.AddComponent<NectarFlower>(); var settings = new SerializedObject(nectar);
                    settings.FindProperty("sippingOffset").vector3Value = new Vector3(0.14f, 0.83f, 0); settings.FindProperty("capacity").floatValue = 6f; settings.ApplyModifiedPropertiesWithoutUndo(); flowers.Add(nectar);
                }
            }
            if (centers.Count != 24) throw new InvalidOperationException("Could not place 24 spaced hyacinth patches.");
            int carpet = 0, decor = 0; string[] kinds = { "Fronds", "Creepers", "NeedleBeards", "Polyps", "GlowSacs", "Ribbons" };
            for (int attempt = 0; carpet < 1600 && attempt < 30000; attempt++)
            {
                if (!Ground(R(-47, 47), R(-47, 47), out var hit) || centers.Any(p => Vector3.ProjectOnPlane(p - hit.point, Vector3.up).sqrMagnitude < 2.1f * 2.1f)) continue;
                var flower = new GameObject("Low wildflower " + carpet); flower.transform.SetParent(root, false); flower.transform.position = hit.point;
                flower.transform.rotation = Quaternion.Euler(0, R(0, 360), 0); flower.transform.localScale = Vector3.one * R(0.5f, 1.25f);
                Part(flower.transform, meadowMeshes[carpet % meadowMeshes.Length], petals[carpet % petals.Length], "Meadow flowers"); carpet++;
                if (carpet % 4 != 0) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/WorldDecor/Decor_{kinds[decor % kinds.Length]}_{random.Next(6)}.prefab");
                if (prefab == null) continue;
                var plant = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root); plant.transform.position = hit.point + Vector3.right * 0.3f; plant.transform.rotation = Quaternion.Euler(0, R(0, 360), 0);
                var renderers = plant.GetComponentsInChildren<Renderer>(); var bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                plant.transform.localScale *= R(0.35f, 0.75f) / Mathf.Max(0.1f, bounds.size.y);
                foreach (var c in plant.GetComponentsInChildren<Collider>()) c.enabled = false;
                foreach (Transform t in plant.GetComponentsInChildren<Transform>()) t.gameObject.layer = 0;
                SquirrelTree.PlaceOnGround(plant.transform, hit.point - Vector3.up * 0.02f); decor++;
            }
            for (int c = 0; c < 6; c++)
            {
                var colony = new GameObject("Glow bee colony " + (c + 1)).AddComponent<GlowBeeColony>(); colony.transform.SetParent(root, false);
                var settings = new SerializedObject(colony); settings.FindProperty("nestCount").intValue = 2; settings.FindProperty("beesPerNest").intValue = 6;
                settings.FindProperty("spawnSpider").boolValue = false; settings.FindProperty("seed").intValue = 814 + c;
                var flowerList = patches.Skip(c * 4).Take(4).SelectMany(p => p).ToArray(); var sources = settings.FindProperty("authoredFlowers"); sources.arraySize = flowerList.Length;
                for (int i = 0; i < flowerList.Length; i++) sources.GetArrayElementAtIndex(i).objectReferenceValue = flowerList[i];
                var anchors = settings.FindProperty("nestAnchors"); anchors.arraySize = 2;
                for (int n = 0; n < 2; n++)
                {
                    Vector3 near = centers[c * 4 + n * 2]; bool found = false;
                    for (int a = 0; a < 24; a++)
                    {
                        Vector3 point = near + Quaternion.Euler(0, a * 137.5f, 0) * Vector3.forward * (2.8f + a * 0.12f);
                        if (!Ground(point.x, point.z, out var hit) || Physics.CheckSphere(hit.point + Vector3.up * 0.6f, 0.55f, (1 << 0) | (1 << 7), QueryTriggerInteraction.Ignore)) continue;
                        var anchor = new GameObject("Concealed hive ground anchor").transform; anchor.SetParent(colony.transform, false); anchor.position = hit.point;
                        anchors.GetArrayElementAtIndex(n).objectReferenceValue = anchor; found = true; break;
                    }
                    if (!found) throw new InvalidOperationException("No clear hive anchor near patch.");
                }
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            Physics.SyncTransforms(); AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            SquirrelForestRefinement.Capture(scene, forest);
            File.WriteAllText("ArtCaptures/SquirrelForest/Flowers.txt", $"PASS\nHyacinths: 24 patches, centres at least 13 m apart; {patches.Sum(p => p.Count)} nectar stalks.\nFlower carpet: {carpet} low star flowers, four colours; {decor} additional Scene_World plants.\nBees: 6 colonies, 12 authored hive anchors, 72 runtime glow bees.\nOnly authored hyacinths supply nectar to these colonies.\nWalkway and squirrel nest clearings retained.\n");
        }
        finally { if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); if (!loaded) EditorSceneManager.CloseScene(scene, true); }
    }

    static void Bell(SilkForestGeometry.MeshBuffer mesh, Vector3 root, Vector3 axis, float length, float radius)
    {
        Vector3 side = Vector3.Cross(axis, Vector3.up).normalized, up = Vector3.Cross(side, axis).normalized;
        int start = mesh.vertices.Count;
        for (int ring = 0; ring < 4; ring++) for (int v = 0; v < 12; v++)
        {
            float a = v * Mathf.PI / 6f, r = radius * (ring == 0 ? 0.35f : ring == 1 ? 0.65f : ring == 2 ? 0.85f : v % 2 == 0 ? 1.2f : 0.8f);
            mesh.Vertex(root + axis * (ring * length / 3f + (ring == 3 && v % 2 == 0 ? 0.025f : 0)) + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * r, Color.white, Vector2.zero);
            if (ring == 0) continue; int i = start + ring * 12 + v, next = start + ring * 12 + (v + 1) % 12;
            mesh.Triangle(i - 12, i, next); mesh.Triangle(i - 12, next, next - 12);
        }
    }
}
