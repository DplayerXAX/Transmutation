using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class SquirrelForestBeeCheck
{
    const string Key = "SquirrelForest.BeeCheck";
    static SquirrelForestBeeCheck() { EditorApplication.update += Tick; }
    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists("Temp/SquirrelForest.bees"))
        {
            File.Delete("Temp/SquirrelForest.bees"); SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".owned", !EditorApplication.isPlaying);
            SessionState.SetBool(Key + ".gathered", false); SessionState.SetBool(Key + ".returned", false);
            if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
        }
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || Time.timeSinceLevelLoad < 2f) return;
        try
        {
            var scene = SceneManager.GetSceneByPath(SquirrelForestSetup.ScenePath); var roots = scene.GetRootGameObjects();
            var colonies = roots.SelectMany(g => g.GetComponentsInChildren<GlowBeeColony>()).ToArray();
            var bees = roots.SelectMany(g => g.GetComponentsInChildren<GlowBee>()).ToArray();
            var flowers = roots.SelectMany(g => g.GetComponentsInChildren<NectarFlower>()).ToArray();
            if (bees.Any(b => b.State == GlowBee.Activity.Gathering)) SessionState.SetBool(Key + ".gathered", true);
            if (colonies.SelectMany(c => c.Nests).Any(n => n.StoredNectar > 0 || n.HoneyCount > 0)) SessionState.SetBool(Key + ".returned", true);
            if (Time.timeSinceLevelLoad < 40f) return;
            bool gathered = SessionState.GetBool(Key + ".gathered", false), returned = SessionState.GetBool(Key + ".returned", false);
            if (colonies.Length != 6 || bees.Length != 72 || colonies.Sum(c => c.Nests.Count) != 12 || !gathered || !returned)
                throw new Exception($"Bee checks failed: colonies={colonies.Length}, bees={bees.Length}, hives={colonies.Sum(c => c.Nests.Count)}, gathered={gathered}, returned={returned}");
            if (colonies.Any(c => c.Flowers.Count == 0 || c.Flowers.Any(f => !f.name.Contains("Hyacinth")))) throw new Exception("Non-hyacinth nectar targets.");
            int mountedHives = 0;
            foreach (var colony in colonies)
            {
                if (!new SerializedObject(colony).FindProperty("mountHivesOnAnchors").boolValue) continue;
                foreach (var hive in colony.Nests)
                {
                    var tree = hive.GetComponentInParent<CapsuleCollider>();
                    if (tree == null || tree.GetComponent<SquirrelTree>() != null || !tree.name.Contains("0 buds"))
                        throw new Exception("Hive is not mounted on a bare trunk.");
                    if (Mathf.Abs(hive.transform.lossyScale.x - 0.5f) > 0.01f) throw new Exception("Mounted hive is not half size.");
                    if (Vector3.Distance(hive.Entrance, hive.GetComponent<SphereCollider>().ClosestPoint(hive.Entrance)) < 0.15f)
                        throw new Exception("Mounted hive entrance lacks bee clearance.");
                    mountedHives++;
                }
            }
            var forest = roots.SelectMany(g => g.GetComponentsInChildren<SilkForestManager>()).Single();
            SquirrelForestRefinement.Capture(scene, forest);
            File.WriteAllText("ArtCaptures/SquirrelForest/Bees.txt", $"PASS: 40-second Play Mode observation.\nSix colonies; twelve active hives; {bees.Length} glow bees; {flowers.Length} authored hyacinth nectar sources.\nObserved gathering from hyacinths and returning nectar to hives.\nHoney products at observation end: {colonies.Sum(c => c.Nests.Sum(n => n.HoneyCount))}.\nAll colony flower references are authored hyacinths.\n");
            File.AppendAllText("ArtCaptures/SquirrelForest/Bees.txt", $"Validated {mountedHives} half-size mounted hives on bare trunks and accessible entrances.\n");
            SessionState.SetBool(Key, false); if (SessionState.GetBool(Key + ".owned", false)) EditorApplication.ExitPlaymode();
        }
        catch (Exception e)
        {
            File.WriteAllText("ArtCaptures/SquirrelForest/Bees.txt", e.ToString()); SessionState.SetBool(Key, false);
            if (SessionState.GetBool(Key + ".owned", false)) EditorApplication.ExitPlaymode();
        }
    }
}
