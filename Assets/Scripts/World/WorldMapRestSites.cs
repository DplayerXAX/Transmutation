using System.Collections;
using UnityEngine;

/// <summary>Authored Scene World meditation seats, created when the generated terrain is ready.</summary>
public sealed class WorldMapRestSites : MonoBehaviour
{
    private IEnumerator Start()
    {
        var world = FindFirstObjectByType<ProceduralWorld>();
        while (world == null || !world.GenerationComplete || RestSpotSystem.Instance == null)
        {
            yield return null;
            if (world == null) world = FindFirstObjectByType<ProceduralWorld>();
        }
        foreach (var site in GetComponentsInChildren<WorldMapRestSite>())
        {
            Vector3 at = site.transform.position;
            if (world.TryFindSurfaceBelow(at + Vector3.up * 6f, out float ground) && ground >= at.y - 4f) at.y = ground;
            RestSpotSystem.Instance.AddSpot(site.Kind, at, site.transform.eulerAngles.y, snap: false);
        }
    }
}

