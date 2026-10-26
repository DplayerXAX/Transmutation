using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Bakes the supplied FBX into a centered, two-tone visual without changing squirrel physics.</summary>
public static class SquirrelModelSetup
{
    private const string Source = "Assets/Models/Squirrels.fbx";
    private const string MeshPath = "Assets/Models/SquirrelFacingMesh.asset";
    private const string PrefabPath = "Assets/Prefabs/Creatures/Squirrel.prefab";

    [MenuItem("Tools/Creatures/Apply Squirrel FBX Model")]
    public static void Configure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Leave Play Mode before replacing the squirrel visual.");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        if (source == null) throw new InvalidOperationException("Missing squirrel FBX: " + Source);
        var imported = UnityEngine.Object.Instantiate(source);
        Mesh baked;
        try { baked = Bake(imported); }
        finally { UnityEngine.Object.DestroyImmediate(imported); }
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (saved == null) { AssetDatabase.CreateAsset(baked, MeshPath); saved = baked; }
        else { EditorUtility.CopySerialized(baked, saved); UnityEngine.Object.DestroyImmediate(baked); }
        var front = Material("SquirrelFront", new Color(0.2f, 0.8f, 0.42f));
        var back = Material("SquirrelBack", new Color(0.17f, 0.34f, 0.8f));
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            foreach (string name in new[] { "Front (green)", "Back (blue)", "Squirrel Visual" })
            {
                Transform old = root.transform.Find(name);
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            // The old cube was drawn on the root. Keep its collider, body and behavior components.
            if (root.TryGetComponent<MeshRenderer>(out var renderer)) UnityEngine.Object.DestroyImmediate(renderer);
            if (root.TryGetComponent<MeshFilter>(out var filter)) UnityEngine.Object.DestroyImmediate(filter);
            var visual = new GameObject("Squirrel Visual");
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = saved;
            visual.AddComponent<MeshRenderer>().sharedMaterials = new[] { front, back };
            if (root.GetComponent<Squirrel>() == null || root.GetComponent<SquirrelMotor>() == null ||
                root.GetComponent<Rigidbody>() == null || root.GetComponent<BoxCollider>() == null)
                throw new InvalidOperationException("Squirrel gameplay components must remain on the prefab root.");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Debug.Log($"Applied squirrel FBX: {saved.vertexCount} vertices, green +Z front / blue -Z back, height {saved.bounds.size.y:F3}m. Root physics retained.");
    }

    private static Mesh Bake(GameObject model)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        // Source toes point toward -Z. Rotate the visual so toes and the motor both face +Z.
        Matrix4x4 facing = Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f));
        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;
            int offset = vertices.Count;
            Matrix4x4 matrix = facing * model.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            Vector3[] sourceVertices = mesh.vertices, sourceNormals = mesh.normals;
            Vector2[] sourceUV = mesh.uv;
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                vertices.Add(matrix.MultiplyPoint3x4(sourceVertices[i]));
                normals.Add(sourceNormals.Length == sourceVertices.Length ? normalMatrix.MultiplyVector(sourceNormals[i]).normalized : Vector3.up);
                uv.Add(sourceUV.Length == sourceVertices.Length ? sourceUV[i] : Vector2.zero);
            }
            foreach (int triangle in mesh.triangles) triangles.Add(triangle + offset);
        }
        if (vertices.Count == 0) throw new InvalidOperationException("The squirrel FBX contains no static mesh geometry.");
        var bounds = new Bounds(vertices[0], Vector3.zero);
        foreach (Vector3 vertex in vertices) bounds.Encapsulate(vertex);
        float scale = 1f / Mathf.Max(0.001f, bounds.size.y);
        for (int i = 0; i < vertices.Count; i++) vertices[i] = (vertices[i] - bounds.center) * scale;
        var front = new List<int>();
        var back = new List<int>();
        for (int i = 0; i < triangles.Count; i += 3)
        {
            ClipTriangle(triangles[i], triangles[i + 1], triangles[i + 2], 1f, front, vertices, normals, uv);
            ClipTriangle(triangles[i], triangles[i + 1], triangles[i + 2], -1f, back, vertices, normals, uv);
        }
        if (front.Count == 0 || back.Count == 0) throw new InvalidOperationException("Both squirrel facing regions must contain triangles.");
        var result = new Mesh { name = "Squirrel FBX - green front blue back", indexFormat = IndexFormat.UInt32 };
        result.SetVertices(vertices); result.SetNormals(normals); result.SetUVs(0, uv);
        result.subMeshCount = 2;
        result.SetTriangles(front, 0); result.SetTriangles(back, 1);
        result.RecalculateBounds();
        return result;
    }

    private static void ClipTriangle(int a, int b, int c, float side, List<int> output,
        List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv)
    {
        // Cut triangles at the center plane instead of assigning whole crossing triangles.
        // The interpolated seam is straight and preserves the FBX's smooth surface normals.
        int[] triangle = { a, b, c };
        var polygon = new List<int>(4);
        for (int i = 0; i < 3; i++)
        {
            int from = triangle[i], to = triangle[(i + 1) % 3];
            float fromZ = vertices[from].z, toZ = vertices[to].z;
            bool fromInside = fromZ * side >= 0f, toInside = toZ * side >= 0f;
            if (fromInside) polygon.Add(from);
            if (fromInside == toInside) continue;
            float t = fromZ / (fromZ - toZ);
            Vector3 point = Vector3.Lerp(vertices[from], vertices[to], t);
            point.z = 0f;
            polygon.Add(vertices.Count);
            vertices.Add(point);
            normals.Add(Vector3.Lerp(normals[from], normals[to], t).normalized);
            uv.Add(Vector2.Lerp(uv[from], uv[to], t));
        }
        for (int i = 1; i + 1 < polygon.Count; i++)
        { output.Add(polygon[0]); output.Add(polygon[i]); output.Add(polygon[i + 1]); }
    }

    private static Material Material(string name, Color color)
    {
        string path = "Assets/Prefabs/Creatures/SquirrelMaterials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Smoothness", 0.25f);
        EditorUtility.SetDirty(material);
        return material;
    }
}
