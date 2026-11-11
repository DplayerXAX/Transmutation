using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Batch helper: writes the hierarchy of the scenes merged into Scene_World to Temp/WorldMap.inspect.txt.</summary>
public static class WorldMapInspect
{
    public static readonly string[] Scenes = {
        "Assets/Scenes/Scene_Goo.unity", "Assets/Scenes/Scene_FungalHollow.unity",
        "Assets/Scenes/Scene_CisternBasin.unity", "Assets/Scenes/Scene_LeafMap.unity" };

    public static void Run()
    {
        var text = new StringBuilder();
        foreach (string path in Scenes)
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            text.AppendLine("==== " + path);
            foreach (var root in scene.GetRootGameObjects()) Describe(root.transform, text, 0);
        }
        File.WriteAllText("Logs/WorldMap.inspect.txt", text.ToString());
    }

    private static void Describe(Transform t, StringBuilder text, int depth)
    {
        var renderers = t.GetComponentsInChildren<Renderer>(true);
        string bounds = "";
        if (renderers.Length > 0)
        {
            var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
            bounds = " bounds " + V(b.min) + ".." + V(b.max);
        }
        int creatures = t.GetComponentsInChildren<Creature>(true).Length;
        text.AppendLine(new string(' ', depth * 2) + t.name + " @" + V(t.position) + (t.gameObject.activeSelf ? "" : " [off]")
            + " {" + string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name)) + "}"
            + " kids " + t.childCount + (creatures > 0 ? " creatures " + creatures : "") + bounds);
        if (depth >= 2 || t.childCount > 40) return;
        foreach (Transform child in t) Describe(child, text, depth + 1);
    }
    private static string V(Vector3 v) => "(" + v.x.ToString("0") + "," + v.y.ToString("0") + "," + v.z.ToString("0") + ")";
}
