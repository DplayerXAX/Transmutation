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
        return Sample(point).r;
    }
    public Color Sample(Vector3 point)
    {
        if(Map==null) return new Color(point.y,0,0,1);
        float x=Mathf.Clamp01((point.x-WorldRect.x)/WorldRect.z)*(Map.width-1);
        float z=Mathf.Clamp01((point.z-WorldRect.y)/WorldRect.w)*(Map.height-1);
        int ix=Mathf.FloorToInt(x),iz=Mathf.FloorToInt(z);
        return Color.Lerp(Color.Lerp(Map.GetPixel(ix,iz),Map.GetPixel(Mathf.Min(ix+1,Map.width-1),iz),x-ix),
            Color.Lerp(Map.GetPixel(ix,Mathf.Min(iz+1,Map.height-1)),Map.GetPixel(Mathf.Min(ix+1,Map.width-1),Mathf.Min(iz+1,Map.height-1)),x-ix),z-iz);
    }
}
