using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DanielColorExperimentSetup
{
    [MenuItem("Tools/Capstone/Daniel/Build Color Experiment")]
    public static void Build()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/Scene_Daniel.unity" || Application.isPlaying) throw new System.InvalidOperationException("Open Daniel in Edit Mode.");
        if(GameObject.Find("Daniel Color Experiment")!=null)
        {
            DanielExperimentFinish.Build();
            DanielExperimentPolish.Apply();
            DanielExperimentGrounding.Apply();
            DanielProceduralOpening.Apply();
            return;
        }
        AsuraPrototypeSetup.CreateAssets();
        foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/Prefabs/Creatures/Asura"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);
            var prefab=PrefabUtility.LoadPrefabContents(path);
            if(prefab.GetComponent<Creature>() is BlastCore || prefab.GetComponent<Creature>() is MeleeAsura || prefab.GetComponent<Creature>() is RangedAsura || prefab.GetComponent<Creature>() is GeneralAsura)
                if(prefab.GetComponent<AsuraRingVisual>()==null) prefab.AddComponent<AsuraRingVisual>();
            var presentation=prefab.GetComponent<AsuraPresentation>();
            if(presentation!=null)
            {
                var so=new SerializedObject(presentation);
                so.FindProperty("lightColor").colorValue=new Color(1,.65f,.79f);
                so.FindProperty("darkColor").colorValue=new Color(.13f,.035f,.22f);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(prefab,path); PrefabUtility.UnloadPrefabContents(prefab);
        }
        var root=new GameObject("Daniel Color Experiment").transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject,"Build Daniel experiment");
        var player=Object.FindFirstObjectByType<SmoothFirstPersonController>();
        Vector3 spawn=player.transform.position;
        var spire=Object.FindFirstObjectByType<HangingSpire>();
        Vector3 forward=Vector3.ProjectOnPlane(spire.transform.position-spawn,Vector3.up).normalized;
        Quaternion rotation=Quaternion.LookRotation(forward);
        var cave=new GameObject("Opening Cave").transform; cave.SetParent(root,false); cave.position=spawn-Vector3.up*1.15f; cave.rotation=rotation;
        Material stone=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Decor_Stone.mat");
        Box(cave,"Cave floor",new Vector3(0,-1,14),new Vector3(18,2,36),stone);
        Box(cave,"Left cave wall",new Vector3(-9,5,14),new Vector3(2,12,36),stone);
        Box(cave,"Right cave wall",new Vector3(9,5,14),new Vector3(2,12,36),stone);
        Box(cave,"Cave roof",new Vector3(0,11,14),new Vector3(20,2,36),stone);
        Box(cave,"Cave back",new Vector3(0,5,-4),new Vector3(20,12,2),stone);
        Box(cave,"Mushroom exit ledge",new Vector3(0,2,31),new Vector3(18,4,6),stone);
        for(int i=0;i<3;i++) Spawn("Assets/Prefabs/Creatures/BounceShroom.prefab",cave,new Vector3((i-1)*3,.5f,24),"Cave BounceShroom");
        var basin=new GameObject("Asura Basin").transform; basin.SetParent(root,false);
        Vector3 left=Vector3.Cross(forward,Vector3.up);
        basin.position=new Vector3(spawn.x,spawn.y-1.15f,spawn.z)+forward*24+left*78;
        basin.rotation=rotation;
        Box(basin,"Basin floor",new Vector3(0,-1,0),new Vector3(72,2,72),stone);
        var entrance=new GameObject("Encounter Entrance").transform; entrance.SetParent(basin,false); entrance.localPosition=new Vector3(0,0,-32);
        var encounter=basin.gameObject.AddComponent<DanielBasinEncounter>(); encounter.entrance=entrance;
        string[] roles={"MeleeAsura","RangedAsura","GeneralAsura"}; int[] counts={12,6,3};
        for(int role=0;role<3;role++) for(int i=0;i<counts[role];i++)
        {
            var unit=Spawn("Assets/Prefabs/Creatures/Asura/"+roles[role]+".prefab",basin,new Vector3((i%6-2.5f)*6,.15f,-12+role*13+(i/6)*5),roles[role]);
            var so=new SerializedObject(unit.GetComponent<Creature>()); so.FindProperty("initialPolarity").enumValueIndex=(int)(i%2==0?CreaturePolarity.Light:CreaturePolarity.Dark); so.ApplyModifiedPropertiesWithoutUndo();
        }
        for(int i=0;i<3;i++) Spawn("Assets/Prefabs/Creatures/Asura/BlastCore.prefab",basin,new Vector3(-5+i*3,.7f,-29),"Spare Ring Launcher");
        Vector3 start=cave.TransformPoint(new Vector3(0,4,35)); Vector3 end=basin.TransformPoint(new Vector3(0,0,-32));
        var bridge=Box(root,"Connection to Asura basin",(start+end)*.5f,new Vector3(8,1,Vector3.Distance(start,end)+4),stone);
        bridge.transform.rotation=Quaternion.LookRotation(end-start);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        DanielExperimentFinish.Build();
        DanielExperimentPolish.Apply();
        DanielExperimentGrounding.Apply();
        DanielProceduralOpening.Apply();
    }
    private static GameObject Spawn(string path,Transform parent,Vector3 position,string name)
    {
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path); if(asset==null) throw new System.InvalidOperationException(path);
        var g=(GameObject)PrefabUtility.InstantiatePrefab(asset,parent); g.name=name; g.transform.localPosition=position; return g;
    }
    private static GameObject Box(Transform parent,string name,Vector3 position,Vector3 scale,Material material)
    {
        var g=GameObject.CreatePrimitive(PrimitiveType.Cube); g.name=name; g.layer=7; g.transform.SetParent(parent,false); g.transform.localPosition=position; g.transform.localScale=scale; g.GetComponent<Renderer>().sharedMaterial=material; return g;
    }
}
