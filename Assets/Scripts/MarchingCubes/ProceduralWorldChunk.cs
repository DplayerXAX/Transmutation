using UnityEngine;

/// <summary>Identity of the actual terrain collider, deliberately absent from decoration children.</summary>
[DisallowMultipleComponent]
public sealed class ProceduralWorldChunk : MonoBehaviour
{
    public ProceduralWorld Owner { get; private set; }
    public Vector3Int Coordinate { get; private set; }
    public void Initialize(ProceduralWorld owner, Vector3Int coordinate) { Owner = owner; Coordinate = coordinate; }
}
