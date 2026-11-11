using UnityEngine;

/// <summary>Shared baked terrain support. R=world height, GB=normal XZ, A=valid coverage.</summary>
[CreateAssetMenu(menuName="Capstone/Leaf Garden Surface")]
public sealed class LeafGardenSurface : ScriptableObject
{
    public Texture2D Map;
    public Vector4 WorldRect;
    public Vector2 HeightRange;
    public float HeightAt(Vector3 point)
    {
        if(Map==null) return point.y;
        return Map.GetPixelBilinear((point.x-WorldRect.x)/WorldRect.z,(point.z-WorldRect.y)/WorldRect.w).r;
    }
}
