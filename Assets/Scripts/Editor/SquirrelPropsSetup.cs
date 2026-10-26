using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Authored acorn and woven nest visuals, baked into meshes rather than runtime geometry.</summary>
public static class SquirrelPropsSetup
{
    [MenuItem("Tools/Creatures/Build Squirrel Acorn And Woven Nest")]
    public static void Configure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Material nut = Material("AcornShell", new Color(0.52f, 0.24f, 0.075f));
        Material cap = Material("AcornCap", new Color(0.23f, 0.105f, 0.04f));
        Material twig = Material("NestTwigs", new Color(0.32f, 0.16f, 0.065f));
        Material straw = Material("NestStraw", new Color(0.72f, 0.53f, 0.23f));
        var staging = new GameObject("Squirrel prop mesh staging");
        try
        {
            Part(staging.transform, PrimitiveType.Sphere, new Vector3(0f, -0.035f, 0f), new Vector3(0.26f, 0.34f, 0.26f), nut);
            Part(staging.transform, PrimitiveType.Sphere, new Vector3(0f, 0.11f, 0f), new Vector3(0.3f, 0.13f, 0.3f), cap);
            Tube(staging.transform, new Vector3(0f, 0.155f, 0f), new Vector3(0.025f, 0.25f, 0f), 0.022f, cap);
            for (int row = 0; row < 2; row++)
            for (int i = 0; i < 14; i++)
            {
                float a = (i + row * 0.5f) * Mathf.PI * 2f / 14f;
                Part(staging.transform, PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * 0.128f,
                    0.095f + row * 0.034f, Mathf.Sin(a) * 0.128f), new Vector3(0.045f, 0.025f, 0.045f), cap);
            }
            Apply("Assets/Prefabs/Creatures/Food.prefab", "Acorn Visual", staging, "SquirrelAcornMesh", false);
            Clear(staging);
            Part(staging.transform, PrimitiveType.Sphere, Vector3.down * 0.12f, new Vector3(0.75f, 0.07f, 0.75f), straw);
            for (int layer = 0; layer < 5; layer++)
            {
                float radius = 0.34f + layer * 0.035f;
                float y = -0.12f + layer * 0.06f;
                for (int i = 0; i < 28; i++)
                {
                    float a = (i + layer * 0.4f) * Mathf.PI * 2f / 28f;
                    float b = a + Mathf.PI * 2f / 28f;
                    Vector3 from = new Vector3(Mathf.Cos(a) * radius, y + Mathf.Sin(a * 5f) * 0.009f, Mathf.Sin(a) * radius);
                    Vector3 to = new Vector3(Mathf.Cos(b) * radius, y + Mathf.Sin(b * 5f) * 0.009f, Mathf.Sin(b) * radius);
                    Tube(staging.transform, from, to, 0.023f, (i + layer) % 4 == 0 ? straw : twig);
                }
            }
            for (int i = 0; i < 18; i++)
            {
                float a = i * Mathf.PI * 2f / 18f;
                Tube(staging.transform, new Vector3(Mathf.Cos(a) * 0.2f, -0.14f, Mathf.Sin(a) * 0.2f),
                    new Vector3(Mathf.Cos(a + 0.22f) * 0.49f, 0.14f, Mathf.Sin(a + 0.22f) * 0.49f), 0.018f, straw);
            }
            Apply("Assets/Prefabs/Creatures/Nest.prefab", "Woven Nest Visual", staging, "SquirrelWovenNestMesh", true);
        }
        finally { Object.DestroyImmediate(staging); }
        AssetDatabase.SaveAssets();
        Debug.Log("Squirrel props updated: collectible acorn and shallow woven twig nest.");
    }

    private static void Apply(string path, string visualName, GameObject staging, string meshName, bool nest)
    {
        var groups = new Dictionary<Material, List<CombineInstance>>();
        foreach (MeshFilter part in staging.GetComponentsInChildren<MeshFilter>())
        {
            Material material = part.GetComponent<Renderer>().sharedMaterial;
            if (!groups.TryGetValue(material, out var parts)) groups[material] = parts = new List<CombineInstance>();
            parts.Add(new CombineInstance { mesh = part.sharedMesh, transform = staging.transform.worldToLocalMatrix * part.transform.localToWorldMatrix });
        }
        var materials = new List<Material>();
        var combined = new List<CombineInstance>();
        foreach (var group in groups)
        {
            var section = new Mesh { indexFormat = IndexFormat.UInt32 };
            section.CombineMeshes(group.Value.ToArray(), true, true);
            materials.Add(group.Key);
            combined.Add(new CombineInstance { mesh = section, transform = Matrix4x4.identity });
        }
        var mesh = new Mesh { name = meshName, indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(combined.ToArray(), false, false);
        foreach (var section in combined) Object.DestroyImmediate(section.mesh);
        string meshPath = "Assets/Models/" + meshName + ".asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (saved == null) { AssetDatabase.CreateAsset(mesh, meshPath); saved = mesh; }
        else { EditorUtility.CopySerialized(mesh, saved); Object.DestroyImmediate(mesh); }
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (root.TryGetComponent<MeshRenderer>(out var renderer)) Object.DestroyImmediate(renderer);
            if (root.TryGetComponent<MeshFilter>(out var filter)) Object.DestroyImmediate(filter);
            foreach (string name in new[] { visualName, "Squirrel Acorn", "Squirrel Woven Nest" })
            {
                Transform old = root.transform.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            var visual = new GameObject(visualName);
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = saved;
            visual.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
            if (nest)
            {
                foreach (Collider collider in root.GetComponents<Collider>()) Object.DestroyImmediate(collider);
                var shape = root.AddComponent<BoxCollider>();
                shape.size = new Vector3(1.05f, 0.34f, 1.05f);
                shape.center = Vector3.zero;
            }
            else
            {
                var shape = root.GetComponent<CapsuleCollider>();
                shape.radius = 0.15f; shape.height = 0.47f; shape.center = new Vector3(0f, 0.02f, 0f);
            }
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void Clear(GameObject root)
    { while (root.transform.childCount > 0) Object.DestroyImmediate(root.transform.GetChild(0).gameObject); }
    private static Material Material(string label, Color color)
    {
        string path = "Assets/Materials/Creatures/Squirrel" + label + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
        mat.SetColor("_BaseColor", color); mat.SetFloat("_Smoothness", 0.2f); EditorUtility.SetDirty(mat);
        return mat;
    }
    private static void Part(Transform parent, PrimitiveType type, Vector3 point, Vector3 scale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.transform.SetParent(parent, false); part.transform.localPosition = point; part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(part.GetComponent<Collider>());
    }
    private static void Tube(Transform parent, Vector3 from, Vector3 to, float radius, Material material)
    {
        Part(parent, PrimitiveType.Cylinder, (from + to) * 0.5f,
            new Vector3(radius * 2f, (to - from).magnitude * 0.5f, radius * 2f), material);
        parent.GetChild(parent.childCount - 1).rotation = Quaternion.FromToRotation(Vector3.up, to - from);
    }
}
