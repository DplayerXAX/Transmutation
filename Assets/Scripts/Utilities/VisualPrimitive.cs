using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Unity primitive shape for display only, with no collider. Use it instead of CreatePrimitive followed by destroying
/// the collider: in Scene_World each collider added and removed costs about 5 ms, which added up to seconds at load.
/// </summary>
public static class VisualPrimitive
{
    private static readonly Dictionary<PrimitiveType, Mesh> meshes = new Dictionary<PrimitiveType, Mesh>();
    private static Material defaultMaterial;

    public static GameObject Create(PrimitiveType type)
    {
        if (!meshes.TryGetValue(type, out Mesh mesh) || mesh == null || defaultMaterial == null)
        {
            // Read once from a real primitive, which is then removed.
            GameObject source = GameObject.CreatePrimitive(type);
            mesh = source.GetComponent<MeshFilter>().sharedMesh;
            defaultMaterial = source.GetComponent<MeshRenderer>().sharedMaterial;
            meshes[type] = mesh;
            Object.DestroyImmediate(source);
        }
        var shape = new GameObject(type.ToString(), typeof(MeshFilter), typeof(MeshRenderer));
        shape.GetComponent<MeshFilter>().sharedMesh = mesh;
        shape.GetComponent<MeshRenderer>().sharedMaterial = defaultMaterial;
        return shape;
    }
}
