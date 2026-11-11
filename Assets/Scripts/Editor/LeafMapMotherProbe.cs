using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Lists the solid colliders round the bone mother that her ribs would bump into (request `motherprobe`).</summary>
public static class LeafMapMotherProbe
{
    public static void Run()
    {
        var world = Object.FindFirstObjectByType<ProceduralWorld>();
        if (world != null && world.SculptData != null) LeafMapTerrain.Scale = LeafMapTerrain.ReadScale(world.SculptData.Layout);
        Vector3 mother = LeafMapTerrain.MotherSpot;
        var lines = SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<Collider>())
            .Where(c => c.enabled && !c.isTrigger && c.GetComponentInParent<Creature>() == null && c.GetComponentInParent<ProceduralWorld>() == null && !Path(c.transform).StartsWith("[Preview]"))
            .Select(c => new { c, d = Vector3.Distance(c.ClosestPointOnBounds(mother), mother) })
            .Where(x => x.d < 24f).OrderBy(x => x.d)
            .Select(x => x.d.ToString("0.0") + " m  layer " + x.c.gameObject.layer + "  " + Path(x.c.transform)).ToList();
        lines.Insert(0, "Mother at " + mother + ", " + lines.Count + " solid colliders within 24 m");
        File.WriteAllLines("ArtCaptures/LeafMap/MotherProbe.txt", lines);
    }
    private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
}
