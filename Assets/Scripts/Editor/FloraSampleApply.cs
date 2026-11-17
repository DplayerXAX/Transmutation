using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Request-file helper for the flower field flora sample (no menu): write a command to Temp/Flora.request and the
/// open editor runs it, reporting to Temp/Flora.status.
///   apply   - makes the flora materials and lays the wildflower meadow tiles over the open Scene_World flower field (not saved)
///   capture - renders review shots of the patch to ArtCaptures/Flora
/// </summary>
[InitializeOnLoad]
public static class FloraSampleApply
{
    private const string Request = "Temp/Flora.request", Status = "Temp/Flora.status";
    private const string Folder = "Assets/Generated/WorldMap/Flora";
    private const string MeadowName = "Flower meadow (instanced wildflower tiles)";
    public static readonly Vector2 SampleSpot = new Vector2(-38f, 18f);

    static FloraSampleApply() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try
        {
            if (command == "apply") File.WriteAllText(Status, "applied: " + Apply());
            else if (command == "capture") File.WriteAllText(Status, "captured: " + Capture());
            else File.WriteAllText(Status, "unknown command " + command);
        }
        catch (System.Exception e) { File.WriteAllText(Status, "error: " + e); }
    }

    private static Material Mat(string name, Color colour, float glow, float smooth = 0.3f)
    {
        Directory.CreateDirectory(Folder);
        string path = Folder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
        mat.SetColor("_BaseColor", colour);
        mat.SetFloat("_Smoothness", smooth);
        mat.SetFloat("_Cull", 0f);
        mat.doubleSidedGI = true;
        mat.enableInstancing = true;
        if (glow > 0f) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", colour * glow); }
        else mat.DisableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static string Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.path.EndsWith("Scene_World.unity")) return "open Scene_World first (active: " + scene.path + ")";
        var tones = new[]
        {
            // Meadow palette matched to the lily field (purple to ice blue on black), in band order;
            // the last colour is a rare warm accent (FloraPatch.rareLastTone).
            Mat("Flora_DeepViolet", new Color(0.36f, 0.14f, 0.9f), 1.0f),
            Mat("Flora_Violet", new Color(0.56f, 0.34f, 0.98f), 0.95f),
            Mat("Flora_Lilac", new Color(0.76f, 0.62f, 1f), 0.8f),
            Mat("Flora_Periwinkle", new Color(0.5f, 0.6f, 1f), 0.9f),
            Mat("Flora_Ice", new Color(0.8f, 0.92f, 1f), 0.75f),
            Mat("Flora_Coral", new Color(1f, 0.36f, 0.42f), 1.0f),
        };
        Material stem = Mat("Flora_Stem", new Color(0.07f, 0.26f, 0.28f), 0.06f, 0.4f);
        Material centre = Mat("Flora_Centre", new Color(1f, 0.86f, 0.5f), 0.75f, 0.5f);
        AssetDatabase.SaveAssets();

        // One meadow root beside the lily field: square tiles over the flower oval, plus the ambient motes.
        var lilies = Object.FindObjectsByType<LilyField>(FindObjectsSortMode.None).FirstOrDefault();
        foreach (var oldTile in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<FloraPatch>(true)).ToArray())
            if (oldTile != null && oldTile.transform.parent != null && oldTile.transform.parent.name != MeadowName) Undo.DestroyObjectImmediate(oldTile.gameObject);
            else if (oldTile != null && oldTile.transform.parent == null) Undo.DestroyObjectImmediate(oldTile.gameObject);
        var root = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == MeadowName)?.gameObject;
        if (root != null) Undo.DestroyObjectImmediate(root);
        root = new GameObject(MeadowName);
        Undo.RegisterCreatedObjectUndo(root, "Add flower meadow");
        if (lilies != null) root.transform.SetParent(lilies.transform.parent, true);
        root.layer = lilies != null ? lilies.gameObject.layer : 0;

        Vector2 centreXZ = new Vector2(-55f, 45f), radii = new Vector2(95f, 85f);
        const float Tile = 32f;
        int made = 0;
        for (float z = centreXZ.y - radii.y; z < centreXZ.y + radii.y; z += Tile)
        for (float x = centreXZ.x - radii.x; x < centreXZ.x + radii.x; x += Tile)
        {
            Vector2 c = new Vector2(x + Tile * 0.5f, z + Tile * 0.5f);
            // Skip tiles wholly outside the oval (flowers fade out by 0.72-1.0 of it anyway).
            Vector2 near = new Vector2(Mathf.Clamp(centreXZ.x, x, x + Tile), Mathf.Clamp(centreXZ.y, z, z + Tile));
            if (new Vector2((near.x - centreXZ.x) / radii.x, (near.y - centreXZ.y) / radii.y).magnitude > 1.05f) continue;
            var go = new GameObject($"Meadow tile ({c.x:0}, {c.y:0})");
            go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(c.x, WorldMapTerrain.Height(c.x, c.y), c.y);
            go.layer = root.layer;
            var tile = go.AddComponent<FloraPatch>();
            var so = new SerializedObject(tile);
            var list = so.FindProperty("tones");
            list.arraySize = tones.Length;
            for (int i = 0; i < tones.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = tones[i];
            so.FindProperty("stem").objectReferenceValue = stem;
            so.FindProperty("centre").objectReferenceValue = centre;
            so.FindProperty("rareLastTone").boolValue = true;
            so.FindProperty("halfSize").floatValue = Tile * 0.5f;
            so.FindProperty("seed").intValue = 1000 + made * 17;
            so.FindProperty("meadowCentre").vector2Value = centreXZ;
            so.FindProperty("meadowRadii").vector2Value = radii;
            so.FindProperty("coolToward").vector2Value = WorldMapTerrain.MotherHole;
            so.ApplyModifiedPropertiesWithoutUndo();
            made++;
        }

        var motesGo = new GameObject("Meadow motes (pollen and light)");
        motesGo.transform.SetParent(root.transform, false);
        motesGo.layer = root.layer;
        var motes = motesGo.AddComponent<MeadowMotes>();
        var mso = new SerializedObject(motes);
        mso.FindProperty("material").objectReferenceValue = MoteMaterial();
        mso.FindProperty("areaCentre").vector2Value = centreXZ;
        mso.FindProperty("areaRadius").floatValue = 95f;
        mso.ApplyModifiedPropertiesWithoutUndo();
        motes.enabled = false; motes.enabled = true;
        EditorSceneManager.MarkSceneDirty(scene);
        return $"{made} meadow tiles under {root.transform.parent?.name ?? "root"}; scene NOT saved";
    }

    // Soft round dot, additive: reads as a glowing speck of pollen or light.
    private static Material MoteMaterial()
    {
        Directory.CreateDirectory(Folder);
        string texPath = Folder + "/Mote_Dot.png";
        if (!File.Exists(texPath))
        {
            var dot = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float d = new Vector2(x - 31.5f, y - 31.5f).magnitude / 31.5f;
                float a = Mathf.Clamp01(1f - d); a = a * a * (0.6f + 0.4f * Mathf.Clamp01(1f - d * 2.2f) * 2f);
                dot.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
            File.WriteAllBytes(texPath, dot.EncodeToPNG());
            Object.DestroyImmediate(dot);
            AssetDatabase.ImportAsset(texPath);
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        string path = Folder + "/Mote_Additive.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(mat, path); }
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 2f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    private static string Capture()
    {
        var world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world != null && !world.LayoutReady)
            typeof(ProceduralWorld).GetMethod("PreviewInEditor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(world, null);
        Directory.CreateDirectory("ArtCaptures/Flora");
        Vector3 centre = new Vector3(SampleSpot.x, WorldMapTerrain.Height(SampleSpot.x, SampleSpot.y), SampleSpot.y);
        var shots = new (string name, Vector3 from, Vector3 look, float fov)[]
        {
            ("01_eye_level", centre + new Vector3(-14f, 1.8f, -9f), centre + new Vector3(0f, 0.8f, 0f), 60f),
            ("02_close", centre + new Vector3(-5f, 1.4f, -3f), centre + new Vector3(0f, 0.4f, 0f), 55f),
            ("03_overview", centre + new Vector3(-18f, 16f, -18f), centre, 50f),
            ("04_far", centre + new Vector3(-45f, 8f, -35f), centre, 45f),
            ("05_meadow_high", new Vector3(-55f, 75f, -60f), new Vector3(-55f, 10f, 45f), 55f),
            ("06_toward_mother", new Vector3(-60f, 22f, 30f), new Vector3(0f, 5f, 140f), 60f),
        };
        FloraPatch.GrowAll();
        foreach (var m in Object.FindObjectsByType<MeadowMotes>(FindObjectsSortMode.None)) m.FillAround(centre + new Vector3(-8f, 0f, -5f));
        var go = new GameObject("Flora capture camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>();
        var main = Camera.main;
        if (main != null) { camera.CopyFrom(main); camera.targetTexture = null; }
        camera.nearClipPlane = 0.05f; camera.farClipPlane = 800f;
        var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        string done = "";
        try
        {
            foreach (var shot in shots)
            {
                // Lift each camera over the real ground (the analytic height ignores sculpting and overhangs).
                Vector3 from = shot.from;
                if (world != null && world.LayoutReady && world.TryFindSurfaceBelow(new Vector3(from.x, centre.y + 14f, from.z), out float ground))
                    from.y = Mathf.Max(from.y, ground + (from.y - centre.y));
                camera.transform.position = from;
                camera.transform.LookAt(shot.look);
                camera.fieldOfView = shot.fov;
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                image.Apply();
                File.WriteAllBytes($"ArtCaptures/Flora/{shot.name}.png", image.EncodeToPNG());
                done += shot.name + " ";
            }
        }
        finally
        {
            RenderTexture.active = null; camera.targetTexture = null;
            Object.DestroyImmediate(go); Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
        }
        var tiles = Object.FindObjectsByType<FloraPatch>(FindObjectsSortMode.None);
        return done + $"tiles={tiles.Length} flowers={tiles.Sum(t => t.FlowerCount)} grow ms max={tiles.Max(t => t.GrowMilliseconds):0} total={tiles.Sum(t => t.GrowMilliseconds):0}";
    }
}
