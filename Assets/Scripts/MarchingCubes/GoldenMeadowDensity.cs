using UnityEngine;
using System.Collections.Concurrent;

/// <summary>A compact rolling meadow and a genuine cantilevered overlook, sampled by the shared sculpt/erosion tools.</summary>
public sealed class GoldenMeadowDensity
{
    private readonly float offset;
    private readonly Vector2 size;
    private readonly ConcurrentDictionary<Vector2Int,float> columns=new ConcurrentDictionary<Vector2Int,float>();
    public GoldenMeadowDensity(int seed, Vector2 worldSize) { offset = (seed % 1000) * 0.371f; size = worldSize; }
    public float Height(float x, float z)
    {
        float broad = (Mathf.PerlinNoise(x * 0.011f + offset, z * 0.012f + 83) - 0.5f) * 13;
        float soft = (Mathf.PerlinNoise(x * 0.026f + 31, z * 0.023f + offset) - 0.5f) * 3.5f;
        float h = 7 + broad + soft + Mathf.Sin(z * 0.025f + x * 0.008f) * 2.2f;
        // An open foreground, with ridges at the distant and lateral boundaries.
        float edge = Mathf.Max(Mathf.Abs(x) / (size.x * 0.5f), (z + 18) / (size.y * 0.5f));
        float rise = Mathf.SmoothStep(0,1,(edge-0.66f)/0.32f);
        h += rise * (24 + 24 * Mathf.PerlinNoise(x * 0.017f + 12,z * 0.018f + 19));
        h += 26 * Mathf.Exp(-x*x/2100 - (z+174)*(z+174)/950);
        return h;
    }
    public float Sample(Vector3 p)
    {
        var key=new Vector2Int(System.BitConverter.SingleToInt32Bits(p.x),System.BitConverter.SingleToInt32Bits(p.z));
        if(!columns.TryGetValue(key,out float height)) { height=Height(p.x,p.z); columns.TryAdd(key,height); }
        float ground = height-p.y;
        float t = Mathf.Clamp01((p.z+174)/88);
        float width = Mathf.Lerp(24,5,t);
        float roof = 36 + (1-t)*2.5f - Mathf.Abs(p.x)*0.04f;
        float depth = Mathf.Lerp(22,3.5f,t);
        float sides = width - Mathf.Abs(p.x) - Mathf.Max(0,p.y-roof+1.5f)*0.8f;
        float ledge = Mathf.Min(Mathf.Min(sides, Mathf.Min(p.z+177,-86-p.z)),Mathf.Min(roof-p.y,p.y-(roof-depth)));
        return Mathf.Max(ground,ledge);
    }
}
