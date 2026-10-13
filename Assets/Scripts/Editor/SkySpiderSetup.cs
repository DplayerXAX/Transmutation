using UnityEditor;
using UnityEngine;

/// <summary>Explicit, repeatable asset creation. Does not open, edit, or save a scene.</summary>
public static class SkySpiderSetup
{
    public const string SpiderPath = "Assets/Prefabs/Creatures/SkySpider.prefab";
    public const string WebPath = "Assets/Prefabs/Creatures/SkyWeb.prefab";

    [MenuItem("Tools/Creatures/Set Sky Web Strands Black")]
    public static void SetStrandsBlack()
    {
        Material strands = CreateMaterial("SkyWebStrands", "Capstone/Creatures/SkyWebStrands");
        strands.SetColor("_SilkColor", Color.black);
        strands.SetColor("_SupportColor", Color.black);
        EditorUtility.SetDirty(strands);
        AssetDatabase.SaveAssetIfDirty(strands);
    }

    [MenuItem("Tools/Creatures/Create Sky Spider Assets")]
    public static void CreateAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("Exit Play Mode before creating Sky Spider assets.");
        Material strands = CreateMaterial("SkyWebStrands", "Capstone/Creatures/SkyWebStrands");
        Material patches = CreateMaterial("SkyWebPatches", "Capstone/Creatures/SkyWebPatches");
        Material spiderMaterial = CreateMaterial("SkySpider", "Universal Render Pipeline/Lit");
        if (spiderMaterial.HasProperty("_BaseColor") && spiderMaterial.GetColor("_BaseColor") == Color.white)
        {
            spiderMaterial.SetColor("_BaseColor", new Color(0.18f, 0.10f, 0.24f));
            spiderMaterial.SetFloat("_Smoothness", 0.25f);
        }

        // Existing assets retain artist edits on repeated setup runs.
        GameObject webPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WebPath);
        if (webPrefab == null)
        {
            var root = new GameObject("SkyWeb");
            try
            {
                SkyWeb web = root.AddComponent<SkyWeb>();
                var fields = new SerializedObject(web);
                fields.FindProperty("creatureName").stringValue = "Sky Web";
                fields.FindProperty("strandMaterial").objectReferenceValue = strands;
                fields.FindProperty("patchMaterial").objectReferenceValue = patches;
                fields.ApplyModifiedPropertiesWithoutUndo();
                webPrefab = PrefabUtility.SaveAsPrefabAsset(root, WebPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(SpiderPath) == null)
        {
            var root = new GameObject("SkySpider");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "Body";
                block.transform.SetParent(root.transform, false);
                block.transform.localScale = new Vector3(0.65f, 0.45f, 0.65f);
                block.GetComponent<Renderer>().sharedMaterial = spiderMaterial;
                SkySpider spider = root.AddComponent<SkySpider>();
                var fields = new SerializedObject(spider);
                fields.FindProperty("creatureName").stringValue = "Sky Spider";
                fields.FindProperty("webPrefab").objectReferenceValue = webPrefab.GetComponent<SkyWeb>();
                fields.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, SpiderPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        AssetDatabase.SaveAssets();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(SpiderPath);
        Debug.Log("Sky Spider assets ready. Place the SkySpider prefab at the desired web center; scene files were not changed.");
    }

    private static Material CreateMaterial(string name, string shaderName)
    {
        string path = "Assets/Materials/Creatures/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        Shader shader = Shader.Find(shaderName);
        if (shader == null || ShaderUtil.ShaderHasError(shader))
            throw new System.InvalidOperationException("Sky Spider shader missing or invalid: " + shaderName);
        material = new Material(shader) { name = name };
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
