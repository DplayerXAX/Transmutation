using UnityEngine;

/// <summary>Editor-baked collision topology for an imported mesh whose CPU copy is unavailable in a player.</summary>
[DisallowMultipleComponent]
public sealed class SurfaceMeshProxy : MonoBehaviour
{
    public Mesh source;
    [HideInInspector] public Vector3[] vertices;
    [HideInInspector] public int[] triangles;
}
