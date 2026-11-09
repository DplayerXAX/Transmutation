using UnityEngine;

public sealed partial class ProceduralWorld
{
    public bool IsVolumeTerrain => field != null ? field.IsVolumeRecipe : generationRecipe != null && generationRecipe.kind != TerrainRecipeKind.Original;

    /// <summary>Find support below a point, including stacked islands, sculpting and erosion.</summary>
    public bool TryFindSurfaceBelow(Vector3 worldPoint, out float worldY) => FindVolumeCrossing(worldPoint, -1f, out worldY);

    /// <summary>Find overhead rock, including cave ceilings and island undersides.</summary>
    public bool TryFindSurfaceAbove(Vector3 worldPoint, out float worldY) => FindVolumeCrossing(worldPoint, 1f, out worldY);

    private bool FindVolumeCrossing(Vector3 point, float direction, out float worldY)
    {
        worldY = 0f;
        if (!LayoutReady) return false;
        Bounds bounds = VolumeBounds;
        if (point.x < bounds.min.x || point.x > bounds.max.x || point.z < bounds.min.z || point.z > bounds.max.z) return false;
        float start = Mathf.Clamp(point.y, bounds.min.y, bounds.max.y);
        float stop = direction < 0f ? bounds.min.y : bounds.max.y;
        float step = CellSize * 0.5f * direction;
        bool previousSolid = SampleDensity(new Vector3(point.x,start,point.z)) > 0f;
        for (float at = start + step; direction < 0f ? at >= stop : at <= stop; at += step)
        {
            bool solid = SampleDensity(new Vector3(point.x,at,point.z)) > 0f;
            if (solid && !previousSolid)
            {
                float air = at - step, rock = at;
                for (int i=0; i<9; i++)
                {
                    float middle = (air+rock)*0.5f;
                    if (SampleDensity(new Vector3(point.x,middle,point.z)) > 0f) rock=middle; else air=middle;
                }
                worldY=(air+rock)*0.5f; return true;
            }
            previousSolid=solid;
        }
        return false;
    }

    private void VolumeColumnHeights(Vector3 point, out float surface, out float ceiling, out float floor)
    {
        // Keep "surface" as highest ground for existing callers. Placement code can explicitly ask
        // for support below its elevation instead of accidentally selecting a sky island.
        float top = VolumeBounds.max.y;
        if (!TryFindSurfaceBelow(new Vector3(point.x,top,point.z),out surface)) surface=VolumeBounds.min.y;
        if (!TryFindSurfaceAbove(point,out ceiling)) ceiling=top;
        if (!TryFindSurfaceBelow(point,out floor)) floor=VolumeBounds.min.y;
    }
}
