using UnityEditor;
using UnityEngine;

public static class SquirrelReactionSetup
{
    [MenuItem("Tools/Creatures/Configure Squirrel Reaction Prefab")]
    public static void Configure()
    {
        const string path = "Assets/Prefabs/Creatures/Squirrel.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (root.GetComponent<Squirrel>() == null) root.AddComponent<Squirrel>();
            Marker(root, "Front (green)", new Vector3(0, 0, 0.51f), new Vector3(0.75f, 0.7f, 0.04f), new Color(0.15f, 0.95f, 0.35f), "SquirrelFront");
            Marker(root, "Back (blue)", new Vector3(0, 0, -0.51f), new Vector3(0.75f, 0.7f, 0.04f), new Color(0.15f, 0.35f, 1f), "SquirrelBack");
            Marker(root, "Warning flash", new Vector3(0, 0.9f, 0), Vector3.one * 0.25f, Color.red, "SquirrelWarning").enabled = false;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Debug.Log("Squirrel reaction prefab configured: green front, blue back, warning marker and optional warning audio slot.");
    }
    private static Renderer Marker(GameObject root, string name, Vector3 position, Vector3 scale, Color color, string materialName)
    {
        const string folder = "Assets/Prefabs/Creatures/SquirrelMaterials";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Prefabs/Creatures", "SquirrelMaterials");
        string materialPath = folder + "/" + materialName + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.color = color;
            AssetDatabase.CreateAsset(material, materialPath);
        }
        var found = root.transform.Find(name);
        var marker = found != null ? found.gameObject : GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = name;
        marker.transform.SetParent(root.transform, false);
        marker.transform.localPosition = position; marker.transform.localScale = scale;
        if (marker.GetComponent<Collider>() is Collider collider) Object.DestroyImmediate(collider);
        var renderer = marker.GetComponent<Renderer>(); renderer.sharedMaterial = material;
        return renderer;
    }
}
