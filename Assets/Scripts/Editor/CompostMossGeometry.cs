using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Deterministic, repeatable prototype geometry. All mesh coordinates are in root local space.</summary>
public static class CompostMossGeometry
{
    public const float Width = 3f, Length = 4f, Height = 1f, TailLength = 2f;
    private const string MeshFolder = "Assets/Meshes/Creatures";

    public static void Build(GameObject root)
    {
        root.layer = LayerMask.NameToLayer("Ground");
        // Retain the deck object and collider identity so existing references survive the redesign.
        Transform deckTransform = root.transform.Find("Flat Compost Back");
        for (int i = root.transform.childCount - 1; i >= 0; i--)
            if (root.transform.GetChild(i) != deckTransform) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
        GameObject deck = deckTransform != null ? deckTransform.gameObject : Child(root.transform, "Flat Compost Back");
        deck.layer = root.layer;
        deck.transform.localPosition = Vector3.zero;
        deck.transform.localRotation = Quaternion.identity;
        deck.transform.localScale = Vector3.one;
        var back = deck.GetComponent<BoxCollider>();
        if (back == null) back = deck.AddComponent<BoxCollider>();
        back.center = new Vector3(0f, 0.95f, 0f); back.size = new Vector3(Width, 0.1f, Length);
        var solid = root.GetComponent<BoxCollider>();
        solid.center = new Vector3(0f, 0.46f, 0f); solid.size = new Vector3(2.9f, 0.88f, 3.9f);
        Material[] moss = {
            Material("Moss", new Color(0.19f, 0.30f, 0.075f)),
            Material("MossLight", new Color(0.25f, 0.35f, 0.10f)),
            Material("MossDeep", new Color(0.13f, 0.23f, 0.06f)),
            Material("Earth", new Color(0.105f, 0.13f, 0.055f)) };
        SetMesh(deck, SaveMesh("CompostMossBack", SurfaceMesh(false)), moss);
        SetMesh(Child(root.transform, "Moss Skirt"), SaveMesh("CompostMossSkirt", SkirtMesh()), moss);
        var tail = Child(root.transform, "Moss Tail Ramp");
        SetMesh(tail, SaveMesh("CompostMossRamp", SurfaceMesh(true)), moss);
        var ramp = tail.AddComponent<MeshCollider>();
        ramp.sharedMesh = SaveMesh("CompostRampCollision", RampCollision()); ramp.convex = true;
        var rampSides = Object.Instantiate(ramp.sharedMesh);
        int[] faces = rampSides.triangles;
        // The green top is a separate patch mesh; draw only the earthy sides and underside here.
        var sideTriangles = new int[faces.Length - 6];
        System.Array.Copy(faces, 6, sideTriangles, 0, sideTriangles.Length);
        rampSides.triangles = sideTriangles;
        rampSides.RecalculateNormals();
        SetMesh(Child(tail.transform, "Tail Earth"), SaveMesh("CompostRampSides", rampSides), new[] { moss[3] });
        Material eyes = Material("Eyes", new Color(0.035f, 0.045f, 0.02f));
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            eye.name = side < 0 ? "Eye Left" : "Eye Right"; eye.layer = root.layer;
            eye.transform.SetParent(root.transform, false);
            eye.transform.localPosition = new Vector3(side * 0.48f, 0.57f, 1.98f);
            eye.transform.localScale = new Vector3(0.095f, 0.075f, 0.08f);
            Object.DestroyImmediate(eye.GetComponent<Collider>());
            eye.GetComponent<Renderer>().sharedMaterial = eyes;
        }
        using (var fields = new SerializedObject(root.GetComponent<CompostRideSurface>()))
        {
            fields.FindProperty("back").objectReferenceValue = back;
            fields.FindProperty("ramp").objectReferenceValue = ramp;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }
        using (var fields = new SerializedObject(root.GetComponent<CompostCreature>()))
        {
            fields.FindProperty("back").objectReferenceValue = back;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static GameObject Child(Transform parent, string name)
    {
        var child = new GameObject(name); child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false); return child;
    }

    private static Material Material(string suffix, Color color)
    {
        string path = "Assets/Materials/Creatures/MAT_Compost_" + suffix + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Compost " + suffix };
        material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", 0.08f);
        AssetDatabase.CreateAsset(material, path); return material;
    }

    private static void SetMesh(GameObject target, Mesh mesh, Material[] materials)
    {
        var filter = target.GetComponent<MeshFilter>();
        if (filter == null) filter = target.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = target.GetComponent<MeshRenderer>();
        if (renderer == null) renderer = target.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
    }

    private static Mesh SaveMesh(string name, Mesh generated)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Meshes")) AssetDatabase.CreateFolder("Assets", "Meshes");
        if (!AssetDatabase.IsValidFolder(MeshFolder)) AssetDatabase.CreateFolder("Assets/Meshes", "Creatures");
        string path = MeshFolder + "/" + name + ".asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        generated.name = name;
        if (existing == null) { AssetDatabase.CreateAsset(generated, path); return generated; }
        EditorUtility.CopySerialized(generated, existing);
        Object.DestroyImmediate(generated); EditorUtility.SetDirty(existing); return existing;
    }

    private static Mesh SurfaceMesh(bool ramp)
    {
        const int columns = 24, rows = 32;
        var vertices = new List<Vector3>();
        var triangles = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        for (int row = 0; row <= rows; row++)
        {
            float t = row / (float)rows;
            float z = ramp ? Mathf.Lerp(-4f, -2f, t) : Mathf.Lerp(-2f, 2f, t);
            float cornerZ = Mathf.Max(0f, Mathf.Abs(z) - 1.85f);
            float halfWidth = ramp ? Mathf.Lerp(0.9f, 1.2f, t) : 1.35f + Mathf.Sqrt(Mathf.Max(0f, 0.0225f - cornerZ * cornerZ));
            for (int column = 0; column <= columns; column++)
                vertices.Add(new Vector3(Mathf.Lerp(-halfWidth, halfWidth, column / (float)columns),
                    ramp ? Mathf.Lerp(0.015f, 1f, t) : 1f, z));
        }
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            int a = row * (columns + 1) + column, b = a + 1, c = a + columns + 1, d = c + 1;
            Vector3 p = vertices[a];
            float patch = Mathf.PerlinNoise(p.x * 2.8f + 17f, p.z * 2.8f + 31f);
            int material = patch < 0.40f ? 2 : patch > 0.59f ? 1 : 0;
            triangles[material].AddRange(new[] { a, c, b, b, c, d });
        }
        return Mesh(vertices, triangles);
    }

    private static Mesh SkirtMesh()
    {
        // Rounded rectangular rings; irregular lower rings make a ground-hugging organic skirt.
        const int segments = 96, rings = 5;
        var vertices = new List<Vector3>();
        var triangles = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        for (int ring = 0; ring < rings; ring++)
        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            float roundX = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 0.25f);
            float roundZ = Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), 0.25f);
            float noise = Mathf.PerlinNoise(c * 4f + 12f, s * 4f + ring * 0.6f);
            float width = ring == 0 ? 1.38f : ring == 4 ? 1.49f : 1.44f + noise * 0.05f;
            float length = ring == 0 ? 1.86f : ring == 4 ? 1.99f : 1.94f + noise * 0.05f;
            float height = ring == 0 ? 0.025f : ring == 4 ? 0.99f : ring * 0.23f + (noise - 0.5f) * 0.09f;
            vertices.Add(new Vector3(roundX * width, height, roundZ * length));
        }
        for (int ring = 0; ring < rings - 1; ring++)
        for (int i = 0; i < segments; i++)
        {
            int a = ring * segments + i, b = ring * segments + (i + 1) % segments, c = a + segments, d = b + segments;
            int material = ring == 0 ? 3 : (i / 4 + ring) % 3;
            triangles[material].AddRange(new[] { a, c, b, b, c, d });
        }
        return Mesh(vertices, triangles);
    }

    private static Mesh Mesh(List<Vector3> vertices, List<int>[] triangles)
    {
        var mesh = new Mesh { subMeshCount = triangles.Length };
        mesh.SetVertices(vertices);
        for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }

    private static Mesh RampCollision()
    {
        var mesh = new Mesh();
        mesh.vertices = new[] {
            new Vector3(-1.2f, 1f, -2f), new Vector3(1.2f, 1f, -2f),
            new Vector3(-0.9f, 0.015f, -4f), new Vector3(0.9f, 0.015f, -4f),
            new Vector3(-1.2f, 0.005f, -2f), new Vector3(1.2f, 0.005f, -2f),
            new Vector3(-0.9f, 0.005f, -4f), new Vector3(0.9f, 0.005f, -4f) };
        mesh.triangles = new[] { 0,1,2, 1,3,2, 4,6,5, 5,6,7, 0,4,1, 1,4,5,
            2,3,6, 3,7,6, 0,2,4, 2,6,4, 1,5,3, 3,5,7 };
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
    }
}
