using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Swaps the placeholder conifers of Scene_World's "02 Placeholder forest" for the silk trees of Scene_SquirrelForest.
/// Each placeholder tree (one "Tree trunk collider" per tree) becomes a copy of a randomly chosen tree from the squirrel
/// forest, with that tree's own size, its foot set on the ground where the conifer stood and a random turn. The meshes
/// are shared with the squirrel forest (no new mesh assets). Only the trees are copied: no vines, squirrels or
/// squirrel anchors. Each tree gets a LODGroup that stops drawing it once it is small on screen.
/// </summary>
public static class WorldMapSilkTrees
{
    private const string SourceScene = "Assets/Scenes/Scene_SquirrelForest.unity";
    private const string ForestGroup = "02 Placeholder forest";
    private const string TrunkName = "Tree trunk collider";
    /// <summary>Share of the screen height below which a tree is not drawn.</summary>
    private const float CullHeight = 0.035f;

    [MenuItem("Tools/Capstone/World Map/Replace Placeholder Forest With Silk Trees")]
    public static void ReplaceInOpenWorld()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Exit Play Mode first.");
        Scene scene = SceneManager.GetSceneByPath(WorldMapBuild.ScenePath);
        if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(WorldMapBuild.ScenePath, OpenSceneMode.Additive);
        Transform root = scene.GetRootGameObjects().First(g => g.name == "World — combined map").transform;
        var log = new List<string>();
        Replace(scene, root, log);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log(string.Join("\n", log));
    }

    public static void Replace(Scene scene, Transform root, List<string> log)
    {
        Transform group = root.Find(ForestGroup);
        if (group == null) { log.Add("Silk trees: no " + ForestGroup + "."); return; }
        var spots = group.Cast<Transform>().Where(t => t.name == TrunkName).Select(t => (t.position, t.rotation.eulerAngles.y)).ToList();
        if (spots.Count == 0) { log.Add("Silk trees: no placeholder trees left to replace."); return; }
        var world = scene.GetRootGameObjects().Select(r => r.GetComponent<ProceduralWorld>()).First(w => w != null);
        if (!world.LayoutReady) WorldMapBuild.PrepareSampler(world);

        Scene source = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var forest = source.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SilkForestManager>(true)).Single();
            var trees = forest.GeneratedRoot != null ? forest.GeneratedRoot : forest.transform.GetChild(0);
            var templates = trees.Cast<Transform>().Where(t => t.GetComponent<CapsuleCollider>() != null && t.GetComponentInChildren<MeshRenderer>() != null).ToList();
            if (templates.Count == 0) { log.Add("Silk trees: none found in " + SourceScene + "."); return; }

            // The placeholder conifers (batched meshes and trunk colliders) go; everything else in the group stays.
            foreach (Transform child in group.Cast<Transform>().ToArray())
                if (child.name == TrunkName || child.name.StartsWith("Forest cell ")) Object.DestroyImmediate(child.gameObject);

            var random = new System.Random(4207);
            int placed = 0;
            foreach (var (position, yaw) in spots)
            {
                Transform template = templates[random.Next(templates.Count)];
                Vector3 foot = Foot(template);
                float y = position.y + 0.4f;
                if (world.TryFindSurfaceBelow(new Vector3(position.x, y + 6f, position.z), out float ground) && Mathf.Abs(ground - y) < 8f) y = ground;

                var tree = new GameObject("Silk tree " + (placed + 1).ToString("000")).transform;
                tree.SetParent(group, false);
                tree.SetPositionAndRotation(new Vector3(position.x, y - 0.15f, position.z), Quaternion.Euler(0f, yaw, 0f));
                var copy = Object.Instantiate(template.gameObject, tree, false);
                copy.name = template.name;
                copy.transform.localRotation = template.rotation;
                copy.transform.localScale = template.lossyScale;
                copy.transform.localPosition = template.position - foot;
                foreach (var anchor in copy.GetComponentsInChildren<SquirrelTree>(true)) Object.DestroyImmediate(anchor);

                var lod = tree.gameObject.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(CullHeight, copy.GetComponentsInChildren<Renderer>(true)) });
                lod.RecalculateBounds();
                placed++;
            }
            log.Add("Silk trees: " + placed + " placeholder conifers replaced with " + templates.Count + " squirrel-forest trees (culled under " +
                    (CullHeight * 100f).ToString("0.#") + "% of the screen).");
        }
        finally { EditorSceneManager.CloseScene(source, true); SceneManager.SetActiveScene(scene); }
    }

    /// <summary>World point at the bottom of a source tree's trunk (its capsule collider).</summary>
    private static Vector3 Foot(Transform tree)
    {
        var capsule = tree.GetComponent<CapsuleCollider>();
        Vector3 axis = capsule.direction == 0 ? Vector3.right : capsule.direction == 2 ? Vector3.forward : Vector3.up;
        Vector3 local = capsule.center - axis * (capsule.height * 0.5f);
        return tree.TransformPoint(local);
    }
}
