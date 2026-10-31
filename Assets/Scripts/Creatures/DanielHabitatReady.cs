using UnityEngine;

/// <summary>Delays only the authored habitat until procedural ground colliders exist.</summary>
[DefaultExecutionOrder(-1000)]
public sealed class DanielHabitatReady : MonoBehaviour
{
    public ProceduralWorld world;
    public MonoBehaviour[] generators;
    private void Awake()
    {
        foreach(var generator in generators) if(generator!=null) generator.enabled=false;
    }
    private void Update()
    {
        if(world==null || !world.LayoutReady) return;
        foreach(var generator in generators)
            if(generator!=null && !world.IsRegionReady(generator.transform.position,12f)) return;
        foreach(var generator in generators) if(generator!=null) generator.enabled=true;
        enabled=false;
    }
}
