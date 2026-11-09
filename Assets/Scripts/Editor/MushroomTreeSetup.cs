using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Editor-only authoring for the Mushroom Tree and its Mushroom Essence: unit meshes and both prefabs.
/// The tree's body is a unit stalk and a unit cap that MushroomTree rescales as it grows.
/// </summary>
public static class MushroomTreeSetup
{
    public const string TreePath = "Assets/Prefabs/Creatures/MushroomTree.prefab";
    public const string EssencePath = "Assets/Prefabs/Creatures/MushroomEssence.prefab";
    private const string Generated = "Assets/Generated/MushroomTree";
    private const string ShroomPath = "Assets/Prefabs/Creatures/BounceShroom.prefab";

    [MenuItem("Tools/Creatures/Mushroom Tree/Build Prefabs")]
    public static void BuildPrefabsMenu() => BuildPrefabs();

    /// <summary>Builds (or rebuilds) the meshes and both prefabs. Returns the tree prefab.</summary>
    public static MushroomTree BuildPrefabs()
    {
        EnsureFolder(Generated);
        EnsureFolder("Assets/Prefabs/Creatures");
        var shroom = AssetDatabase.LoadAssetAtPath<BounceShroom>(ShroomPath);
        var bounce = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Creatures/BounceShroom.mat");
        var solid = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Realm_MushroomBlue.mat");
        var glow = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Realm_Mushroom.mat");
        var stalkMaterial = StalkMaterial();
        if (shroom == null || bounce == null || solid == null || glow == null || stalkMaterial == null)
            throw new InvalidOperationException("The Mushroom Tree needs the BounceShroom prefab and the mushroom materials.");
        Mesh cap = CapMesh(), stalk = SaveMesh(StalkMesh(), "StalkUnit");

        // Essence: a small bead, picked and eaten from the hand.
        var essenceRoot = new GameObject("MushroomEssence");
        var essenceBody = essenceRoot.AddComponent<Rigidbody>();
        essenceBody.mass = 0.15f;
        essenceBody.interpolation = RigidbodyInterpolation.Interpolate;
        // A bead, not a ball: it stays where it is dropped instead of rolling downhill.
        essenceBody.constraints = RigidbodyConstraints.FreezeRotation;
        essenceRoot.AddComponent<SphereCollider>().radius = 0.18f;
        var essence = essenceRoot.AddComponent<MushroomEssence>();
        var bead = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        bead.name = "Bead";
        Object.DestroyImmediate(bead.GetComponent<Collider>());
        bead.transform.SetParent(essenceRoot.transform, false);
        bead.transform.localScale = new Vector3(0.36f, 0.42f, 0.36f);
        bead.GetComponent<Renderer>().sharedMaterial = glow;
        var essenceFields = new SerializedObject(essence);
        essenceFields.FindProperty("creatureName").stringValue = "Mushroom Essence";
        essenceFields.FindProperty("initialPolarity").enumValueIndex = (int)CreaturePolarity.None;
        essenceFields.FindProperty("condensesInto").objectReferenceValue = shroom;
        essenceFields.ApplyModifiedPropertiesWithoutUndo();
        var essencePrefab = PrefabUtility.SaveAsPrefabAsset(essenceRoot, EssencePath).GetComponent<MushroomEssence>();
        Object.DestroyImmediate(essenceRoot);

        // Tree: rescaled stalk and cap; the cap's Platform is what you stand (or bounce) on.
        var root = new GameObject("MushroomTree");
        var body = root.AddComponent<Rigidbody>();
        body.mass = 2f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        var stalkObject = new GameObject("Stalk", typeof(MeshFilter), typeof(MeshRenderer), typeof(CapsuleCollider));
        stalkObject.transform.SetParent(root.transform, false);
        stalkObject.GetComponent<MeshFilter>().sharedMesh = stalk;
        stalkObject.GetComponent<MeshRenderer>().sharedMaterial = stalkMaterial;
        var capsule = stalkObject.GetComponent<CapsuleCollider>();
        capsule.center = Vector3.up * 0.5f;
        capsule.height = 1f;
        capsule.radius = 1f;
        var capObject = new GameObject("Cap", typeof(MeshFilter), typeof(MeshRenderer));
        capObject.transform.SetParent(root.transform, false);
        capObject.GetComponent<MeshFilter>().sharedMesh = cap;
        capObject.GetComponent<MeshRenderer>().sharedMaterial = bounce;
        var platform = new GameObject("Platform", typeof(BoxCollider));
        platform.transform.SetParent(capObject.transform, false);
        platform.layer = LayerMask.NameToLayer("Ground");
        platform.tag = "Ground";
        platform.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        platform.transform.localScale = new Vector3(1.5f, 0.3f, 1.5f);
        var spring = new SerializedObject(root.AddComponent<BouncySurface>());
        spring.FindProperty("platformCollider").objectReferenceValue = platform.GetComponent<BoxCollider>();
        spring.FindProperty("launchSpeed").floatValue = 22f;
        spring.ApplyModifiedPropertiesWithoutUndo();
        var tree = root.AddComponent<MushroomTree>();
        var fields = new SerializedObject(tree);
        fields.FindProperty("creatureName").stringValue = "Mushroom Tree";
        fields.FindProperty("initialPolarity").enumValueIndex = (int)CreaturePolarity.None;
        fields.FindProperty("stalk").objectReferenceValue = stalkObject.transform;
        fields.FindProperty("cap").objectReferenceValue = capObject.transform;
        fields.FindProperty("capRenderer").objectReferenceValue = capObject.GetComponent<MeshRenderer>();
        fields.FindProperty("bounceCapMaterial").objectReferenceValue = bounce;
        fields.FindProperty("solidCapMaterial").objectReferenceValue = solid;
        fields.FindProperty("essencePrefab").objectReferenceValue = essencePrefab;
        fields.FindProperty("shroomPrefab").objectReferenceValue = shroom;
        fields.ApplyModifiedPropertiesWithoutUndo();
        tree.ApplyShape();
        var treePrefab = PrefabUtility.SaveAsPrefabAsset(root, TreePath).GetComponent<MushroomTree>();
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        return treePrefab;
    }

    [MenuItem("Tools/Creatures/Mushroom Tree/Add Young Tree To Open Scene")]
    public static void AddToScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before editing the scene.");
        var prefab = AssetDatabase.LoadAssetAtPath<MushroomTree>(TreePath);
        if (prefab == null) prefab = BuildPrefabs();
        var tree = (MushroomTree)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(tree.gameObject, "Add Mushroom Tree");
        Vector3 pivot = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        tree.transform.position = Physics.Raycast(pivot + Vector3.up * 200f, Vector3.down, out RaycastHit hit, 600f, 1 << 7,
            QueryTriggerInteraction.Ignore) ? hit.point : pivot;
        EditorSceneManager.MarkSceneDirty(tree.gameObject.scene);
        Selection.activeGameObject = tree.gameObject;
    }

    [MenuItem("Tools/Creatures/Mushroom Tree/Grow Essence On Every Grown Tree (Play Mode)")]
    public static void SpawnEssenceEverywhere()
    {
        foreach (MushroomTree tree in Object.FindObjectsByType<MushroomTree>(FindObjectsSortMode.None)) tree.SpawnEssence();
    }

    [MenuItem("Tools/Creatures/Mushroom Tree/Grow Essence On Every Grown Tree (Play Mode)", true)]
    private static bool CanSpawnEssence() => Application.isPlaying;

    // ---------------- Meshes ----------------

    /// <summary>The shared unit cap (radius 1, origin at the rim), saved once and reused.</summary>
    public static Mesh CapMesh()
    {
        EnsureFolder(Generated);
        string path = Generated + "/CapUnit.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        return existing != null ? existing : SaveMesh(BuildCap(), "CapUnit");
    }

    /// <summary>A unit cap: domed top, a little curled lip, and a shallow gilled underside. Origin at the rim.</summary>
    private static Mesh BuildCap()
    {
        Vector2[] profile = { new Vector2(0f, 0.45f), new Vector2(0.35f, 0.42f), new Vector2(0.6f, 0.35f), new Vector2(0.8f, 0.24f),
            new Vector2(0.93f, 0.12f), new Vector2(1f, 0.02f), new Vector2(1.03f, -0.06f), new Vector2(0.75f, -0.1f),
            new Vector2(0.4f, -0.13f), new Vector2(0.15f, -0.14f), new Vector2(0f, -0.14f) };
        const int sides = 18;
        var rings = new Vector3[profile.Length, sides];
        for (int i = 0; i < profile.Length; i++)
            for (int j = 0; j < sides; j++)
            {
                float angle = j * Mathf.PI * 2f / sides;
                float wobble = 1f + 0.035f * Mathf.Sin(j * 2.7f + i * 1.3f);
                rings[i, j] = new Vector3(Mathf.Cos(angle) * profile[i].x * wobble, profile[i].y, Mathf.Sin(angle) * profile[i].x * wobble);
            }
        var flat = new List<Vector3>();
        for (int i = 0; i < profile.Length - 1; i++)
            for (int j = 0; j < sides; j++)
            {
                int k = (j + 1) % sides;
                flat.AddRange(new[] { rings[i, j], rings[i, k], rings[i + 1, j], rings[i, k], rings[i + 1, k], rings[i + 1, j] });
            }
        var indices = new int[flat.Count];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;
        var mesh = new Mesh { name = "MushroomCap", vertices = flat.ToArray(), triangles = indices };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// A unit stalk (height 1, radius 1): flared foot, slim neck. Straight on purpose: the tree stretches it
    /// many times taller than wide, and any lean tilts the rings, which that stretch turns into spikes.
    /// </summary>
    private static Mesh StalkMesh()
    {
        var path = new List<Vector3>();
        for (int i = 0; i <= 8; i++) path.Add(new Vector3(0f, i / 8f, 0f));
        var mesh = new RealmMeshes();
        mesh.Tube(path, t => 1f + 0.6f * (1f - t) * (1f - t) * (1f - t) - 0.15f * t, 10, 0f, 1f, 0.5f);
        return mesh.Build("MushroomStalk");
    }

    private static Material StalkMaterial()
    {
        string path = Generated + "/Stalk.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        var original = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Decor_Organic.mat");
        if (original == null) return null;
        material = new Material(original) { name = "Stalk" };
        foreach (string property in new[] { "_DotChance", "_GridChance", "_GlitchAmount", "_RingCount", "_SwayAmount", "_BreathAmount", "_StrataFrequency" })
            if (material.HasProperty(property)) material.SetFloat(property, 0f);
        if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(0.82f, 0.8f, 0.72f));
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>
    /// Saves the mesh as an asset, replacing an older build's copy in place so references survive. The data is
    /// written through the Mesh API: EditorUtility.CopySerialized left the GPU drawing the old vertices.
    /// </summary>
    private static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = Generated + "/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        existing.Clear();
        existing.indexFormat = mesh.indexFormat;
        existing.vertices = mesh.vertices;
        existing.normals = mesh.normals;
        existing.uv = mesh.uv;
        existing.uv2 = mesh.uv2;
        existing.triangles = mesh.triangles;
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
