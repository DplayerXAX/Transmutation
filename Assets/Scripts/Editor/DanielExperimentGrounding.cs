using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class DanielExperimentGrounding
{
    public static void Apply()
    {
        if(Application.isPlaying) throw new System.InvalidOperationException("Edit Mode required.");
        var root=GameObject.Find("Daniel Color Experiment").transform;
        if(root.Find("Encounter grounded v4")!=null) return;
        var world=Object.FindFirstObjectByType<ProceduralWorld>();world.Generate(true);
        var encounter=root.GetComponentInChildren<DanielBasinEncounter>();
        Undo.RecordObject(world.SculptData,"Level General Asura positions");world.BeginSculptStroke();
        try
        {
            foreach(var general in encounter.GetComponentsInChildren<GeneralAsura>())
                for(int i=0;i<3;i++) world.Sculpt(TerrainSculptTool.Flatten,general.transform.position,14,1,encounter.transform.position.y);
        }
        finally {world.EndSculptStroke();EditorUtility.SetDirty(world.SculptData);}
        Physics.SyncTransforms();
        foreach(var creature in encounter.GetComponentsInChildren<Creature>())
        {
            var hits=Physics.RaycastAll(creature.transform.position+Vector3.up*150,Vector3.down,300,1<<7)
                .Where(hit=>world.OwnsCollider(hit.collider)).OrderByDescending(hit=>hit.point.y).ToArray();
            if(hits.Length==0) throw new System.InvalidOperationException("No floor beneath "+creature.name);
            Vector3 position=creature.transform.position;position.y=hits[0].point.y+(creature is BlastCore ? .4f : .1f);
            creature.transform.position=position;PrefabUtility.RecordPrefabInstancePropertyModifications(creature.transform);
        }
        new GameObject("Encounter grounded v4").transform.SetParent(root,false);
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);EditorSceneManager.SaveScene(root.gameObject.scene);AssetDatabase.SaveAssets();
    }
}

