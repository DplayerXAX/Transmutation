using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class DanielExperimentPolish
{
    public static void Apply()
    {
        if(Application.isPlaying) throw new System.InvalidOperationException("Edit Mode required.");
        var root=GameObject.Find("Daniel Color Experiment").transform;
        if(root.Find("Experiment polished v3")!=null) return;
        var cave=root.Find("Opening Cave");
        var world=Object.FindFirstObjectByType<ProceduralWorld>();world.Generate(true);
        Undo.RecordObject(world.SculptData,"Clear cave interior and settle habitats");world.BeginSculptStroke();
        try
        {
            for(int i=0;i<=6;i++)
                world.Sculpt(TerrainSculptTool.Carve,cave.TransformPoint(new Vector3(0,7,i*5)),9,100,0);
            var habitat=root.Find("Starting Basin Squirrel Grove");
            world.Sculpt(TerrainSculptTool.Flatten,habitat.position,17,1,habitat.position.y);
            world.Sculpt(TerrainSculptTool.Flatten,habitat.position,17,1,habitat.position.y);
            var colony=root.Find("Starting Basin Pearl Bees");
            world.Sculpt(TerrainSculptTool.Flatten,colony.position,10,1,colony.position.y);
            world.Sculpt(TerrainSculptTool.Flatten,colony.position,10,1,colony.position.y);
        }
        finally {world.EndSculptStroke();EditorUtility.SetDirty(world.SculptData);}
        int moved=0;
        foreach(var creature in Object.FindObjectsByType<Creature>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            if(creature.transform.IsChildOf(root)) continue;
            Vector3 local=cave.InverseTransformPoint(creature.transform.position);
            if(Mathf.Abs(local.x)>10 || local.z < -5 || local.z>34 || creature is BoneChild) continue;
            creature.transform.position=cave.TransformPoint(new Vector3(-12+(moved%4)*7,5,45+(moved/4)*5));moved++;
            PrefabUtility.RecordPrefabInstancePropertyModifications(creature.transform);
        }
        int k=0;foreach(var shroom in cave.GetComponentsInChildren<BounceShroom>())
        {
            shroom.transform.localPosition=new Vector3((k++-1)*4,.35f,25);
            PrefabUtility.RecordPrefabInstancePropertyModifications(shroom.transform);
        }
        var guard=root.GetComponent<DanielHabitatReady>();if(guard==null)guard=root.gameObject.AddComponent<DanielHabitatReady>();
        guard.world=world;guard.generators=new MonoBehaviour[]{root.GetComponentInChildren<SquirrelSystemManager>(),root.GetComponentInChildren<GlowBeeColony>()};
        new GameObject("Experiment polished v3").transform.SetParent(root,false);
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);EditorSceneManager.SaveScene(root.gameObject.scene);AssetDatabase.SaveAssets();
    }
}
