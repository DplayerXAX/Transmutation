using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Menu commands for the saved, sculptable terrain (Tools > Capstone > Terrain):
///   - Bake Open Scene World To Sculpted Terrain: freezes the current ProceduralWorld layout into a
///     TerrainSculptData asset (Assets/Terrain/Sculpted/) and assigns it, so it can be sculpted.
///   - Show Terrain In Scene View: builds the world in Edit Mode so it can be sculpted and placed on.
///   - Make Decor Prefabs: saves the WorldDecorator's shapes as prefabs (Assets/Prefabs/WorldDecor/) for hand placing.
/// </summary>
public static class TerrainSculptSetup
{
    public const string TerrainFolder = "Assets/Terrain/Sculpted";
    public const string DecorFolder = "Assets/Prefabs/WorldDecor";
    private const string MaterialFolder = "Assets/Materials/World/";
    private const int DefaultVariants = 3;

    [MenuItem("Tools/Capstone/Terrain/Bake Open Scene World To Sculpted Terrain")]
    private static void BakeMenu()
    {
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null) { EditorUtility.DisplayDialog("Terrain", "No ProceduralWorld found in the open scene.", "OK"); return; }
        Bake(world);
    }

    [MenuItem("Tools/Capstone/Terrain/Show Terrain In Scene View")]
    private static void ShowMenu()
    {
        ProceduralWorld world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world == null) { EditorUtility.DisplayDialog("Terrain", "No ProceduralWorld found in the open scene.", "OK"); return; }
        TerrainSculptWindow.ShowTerrain(world);
    }

    /// <summary>Freezes the world's current settings into its saved terrain asset (made if missing).</summary>
    public static void Bake(ProceduralWorld world)
    {
        if (Application.isPlaying) return;
        TerrainSculptData data = world.SculptData;
        if (data != null && data.HasLayout && !EditorUtility.DisplayDialog("Terrain",
                "This world already uses the saved terrain '" + data.name + "'.\n\nBaking again freezes the current inspector " +
                "settings into it and removes every sculpt edit.", "Bake Again", "Cancel"))
            return;

        if (data == null)
        {
            EnsureFolder(TerrainFolder);
            string sceneName = string.IsNullOrEmpty(world.gameObject.scene.name) ? "Untitled" : world.gameObject.scene.name;
            string path = AssetDatabase.GenerateUniqueAssetPath(TerrainFolder + "/" + sceneName + "_Terrain.asset");
            data = ScriptableObject.CreateInstance<TerrainSculptData>();
            AssetDatabase.CreateAsset(data, path);
        }
        else Undo.RegisterCompleteObjectUndo(data, "Bake Terrain");

        string layout = world.CaptureLayout(out float[] checks);
        data.Freeze(layout, checks, world.gameObject.scene.name + " / " + world.name + " (" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")");
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);

        var serialized = new SerializedObject(world);
        serialized.FindProperty("sculptData").objectReferenceValue = data;
        serialized.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(world.gameObject.scene);

        TerrainSculptWindow.ShowTerrain(world);
        Selection.activeObject = world.gameObject;
        Debug.Log("Saved terrain baked to " + AssetDatabase.GetAssetPath(data) + ". Save the scene to keep the link. " +
                  "Sculpt with Tools > Capstone > Terrain > Sculpt And Place Window.");
    }

    // ---------------- Decor prefabs ----------------

    private struct KindInfo
    {
        public string name;
        public DecorShapes.Shape shape;
        public WorldDecorator.Group group;
    }

    /// <summary>Prefab path for one decorator kind and shape variant (made by MakeDecorPrefabs).</summary>
    public static string DecorPrefabPath(string kindName, DecorShapes.Shape shape, int variant) =>
        DecorFolder + "/Decor_" + SafeName(kindName, shape) + "_" + variant + ".prefab";

    private static string SafeName(string kindName, DecorShapes.Shape shape) =>
        Regex.Replace(string.IsNullOrEmpty(kindName) ? shape.ToString() : kindName, "[^A-Za-z0-9]", "");

    [MenuItem("Tools/Capstone/Terrain/Make Decor Prefabs (From World Decorator)")]
    public static void MakeDecorPrefabs()
    {
        WorldDecorator decorator = Object.FindFirstObjectByType<WorldDecorator>(FindObjectsInactive.Include);
        var materials = new Dictionary<WorldDecorator.Group, Material>
        {
            { WorldDecorator.Group.Stone, AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "MAT_Decor_Stone.mat") },
            { WorldDecorator.Group.Organic, AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "MAT_Decor_Organic.mat") },
            { WorldDecorator.Group.Inner, AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "MAT_Decor_InnerShell.mat") },
            { WorldDecorator.Group.InnerGlow, AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "MAT_Decor_InnerGlow.mat") },
        };
        var kinds = new List<KindInfo>();
        int seed = 7, layer = 7, variantsPerKind = DefaultVariants;
        var colliders = new Dictionary<WorldDecorator.Group, bool>
        {
            { WorldDecorator.Group.Stone, true }, { WorldDecorator.Group.Organic, true },
            { WorldDecorator.Group.Inner, true }, { WorldDecorator.Group.InnerGlow, true },
        };

        if (decorator != null)
        {
            // Same kinds, seeds and materials as the decorator in the open scene.
            var serialized = new SerializedObject(decorator);
            seed = serialized.FindProperty("seed").intValue;
            // One prefab per variant the decorator uses, so baked decor matches it piece for piece.
            variantsPerKind = decorator.VariantsPerShape;
            layer = serialized.FindProperty("decorLayer").intValue;
            Override(materials, WorldDecorator.Group.Stone, serialized.FindProperty("stoneMaterial"));
            Override(materials, WorldDecorator.Group.Organic, serialized.FindProperty("organicMaterial"));
            Override(materials, WorldDecorator.Group.Inner, serialized.FindProperty("innerMaterial"));
            Override(materials, WorldDecorator.Group.InnerGlow, serialized.FindProperty("innerGlowMaterial"));
            colliders[WorldDecorator.Group.Stone] = serialized.FindProperty("stoneColliders").boolValue;
            colliders[WorldDecorator.Group.Organic] = serialized.FindProperty("organicColliders").boolValue;
            colliders[WorldDecorator.Group.Inner] = colliders[WorldDecorator.Group.InnerGlow] = serialized.FindProperty("innerColliders").boolValue;
            SerializedProperty list = serialized.FindProperty("kinds");
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty kind = list.GetArrayElementAtIndex(i);
                kinds.Add(new KindInfo
                {
                    name = kind.FindPropertyRelative("name").stringValue,
                    shape = (DecorShapes.Shape)kind.FindPropertyRelative("shape").intValue,
                    group = (WorldDecorator.Group)kind.FindPropertyRelative("group").intValue,
                });
            }
        }
        else
        {
            // No decorator in the scene: one prefab set per shape, faceted shapes as stone.
            foreach (DecorShapes.Shape shape in System.Enum.GetValues(typeof(DecorShapes.Shape)))
                kinds.Add(new KindInfo { name = shape.ToString(), shape = shape,
                    group = DecorShapes.IsFaceted(shape) ? WorldDecorator.Group.Stone : WorldDecorator.Group.Organic });
        }

        EnsureFolder(DecorFolder);
        EnsureFolder(DecorFolder + "/Meshes");
        int made = 0;
        try
        {
            for (int k = 0; k < kinds.Count; k++)
            {
                KindInfo kind = kinds[k];
                EditorUtility.DisplayProgressBar("Decor Prefabs", kind.name, (float)k / kinds.Count);
                string safe = SafeName(kind.name, kind.shape);
                for (int v = 0; v < variantsPerKind; v++)
                {
                    // Same seed formula as WorldDecorator, so these match the procedural pieces.
                    DecorMesh shape = DecorShapes.Build(kind.shape, seed * 7919 + v * 104729 + (int)kind.shape * 1299709);
                    Mesh mesh = SaveMesh(shape, DecorFolder + "/Meshes/Decor_" + safe + "_" + v + ".asset", (v + 0.5f) / variantsPerKind);

                    var piece = new GameObject("Decor_" + safe + "_" + v) { layer = layer };
                    piece.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var meshRenderer = piece.AddComponent<MeshRenderer>();
                    meshRenderer.sharedMaterial = materials[kind.group] != null ? materials[kind.group] : materials[WorldDecorator.Group.Stone];
                    if (kind.group == WorldDecorator.Group.Inner || kind.group == WorldDecorator.Group.InnerGlow)
                        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    if (colliders[kind.group]) piece.AddComponent<MeshCollider>().sharedMesh = mesh;
                    PrefabUtility.SaveAsPrefabAsset(piece, DecorFolder + "/Decor_" + safe + "_" + v + ".prefab");
                    Object.DestroyImmediate(piece);
                    made++;
                }
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        AssetDatabase.SaveAssets();
        Debug.Log(made + " decor prefabs saved in " + DecorFolder + ". Place them with Tools > Capstone > Terrain > Sculpt And Place Window (Place tab).");
    }

    private static void Override(Dictionary<WorldDecorator.Group, Material> materials, WorldDecorator.Group group, SerializedProperty property)
    {
        if (property != null && property.objectReferenceValue is Material material) materials[group] = material;
    }

    private static Mesh SaveMesh(DecorMesh shape, string path, float pieceRandom)
    {
        var mesh = new Mesh { indexFormat = shape.vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(shape.vertices);
        mesh.SetNormals(shape.normals);
        var uvs = new List<Vector2>(shape.lengths.Count);
        foreach (float length in shape.lengths) uvs.Add(new Vector2(length, pieceRandom));
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(shape.triangles, 0);
        mesh.RecalculateBounds();
        // Room for the sway and breathing done in the decor shader.
        Bounds bounds = mesh.bounds;
        bounds.Expand(2f);
        mesh.bounds = bounds;
        mesh.name = System.IO.Path.GetFileNameWithoutExtension(path);

        // Overwrite in place so prefabs and scenes that use it keep their reference.
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        existing.Clear();
        EditorUtility.CopySerialized(mesh, existing);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }
}

/// <summary>ProceduralWorld inspector: notes when a saved terrain is in use, plus shortcuts.</summary>
[CustomEditor(typeof(ProceduralWorld))]
public sealed class ProceduralWorldEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var world = (ProceduralWorld)target;
        if (world.UsingSculptedTerrain)
            EditorGUILayout.HelpBox("Built from the saved terrain '" + world.SculptData.name + "'. The Size, Surface, Inner World " +
                "and Caves settings were frozen into it and are ignored here. To use new settings, bake again (this drops the sculpting).",
                MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Sculpt And Place Window")) TerrainSculptWindow.Open();
            if (!Application.isPlaying && GUILayout.Button("Show In Scene View")) TerrainSculptWindow.ShowTerrain(world);
        }
        DrawDefaultInspector();
    }
}
