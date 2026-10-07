using UnityEngine;

/// <summary>
/// Music channel for the underground: fades in as the listener goes below the surface
/// and is fully on once under the inner world's ceiling. Put it on the ProceduralWorld.
/// </summary>
[RequireComponent(typeof(ProceduralWorld))]
public sealed class CaveMusicChannel : MusicChannel
{
    [Tooltip("Depth below the surface where the layer starts to fade in.")]
    [Min(0f)] public float startDepth = 2f;

    private ProceduralWorld world;

    private void Reset() => layerEvent = "BGM_cave";

    protected override void OnEnable()
    {
        world = GetComponent<ProceduralWorld>();
        base.OnEnable();
    }

    public override float Weight(Vector3 listener)
    {
        world.ColumnHeights(listener, out float surface, out float ceiling, out _);
        float top = surface - startDepth;
        if (listener.y >= top) return 0f;
        if (listener.y <= ceiling) return 1f;
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(top, ceiling, listener.y));
    }
}
