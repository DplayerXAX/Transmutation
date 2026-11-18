using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class AsuraTerritorySetup
{
    public const string RootName="Asura territory battleground";
    private const string Folder="Assets/Generated/AsuraTerritory";
    [MenuItem("Tools/Creatures/Asura/Build Basilt Territory")]
    public static object Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path!=BasiltSceneSetup.ScenePath)
            throw new InvalidOperationException("Open Basilt in Edit mode.");
        if(GameObject.Find(RootName)!=null)throw new InvalidOperationException("Territory already exists. Preserve or remove that authored root before rebuilding.");
        if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Generated","AsuraTerritory");
        Physics.SyncTransforms();var map=Bake();
        var routes=new List<int[]>();
        Vector3[] targets={Ground(-165,-190),new Vector3(-100,32,-115),new Vector3(-72,36,-42),Ground(168,194),new Vector3(12,36,-42)};
        int front=map.Nearest(targets[2],25);
        var reachable=map.Distances(front,2000);
        // Corner coordinates can land underneath a solid column. Anchor every seed to the
        // battle shelf's connected component instead of accepting the nearest hidden floor.
        int[] ids=targets.Select((p,index)=>reachable.Keys
            .Where(i=>index!=0 && index!=3 || !Physics.CheckCapsule(map.surfaces[i].position+Vector3.up*2.7f,map.surfaces[i].position+Vector3.up*4.7f,2.5f,1<<7,QueryTriggerInteraction.Ignore))
            .OrderBy(i=>(map.surfaces[i].position-p).sqrMagnitude).First()).ToArray();
        for(int i=0;i<ids.Length;i++)if(Vector3.Distance(map.surfaces[ids[i]].position,targets[i])>60)
            throw new InvalidOperationException("No nearby reachable surface for starting site "+i);
        foreach(var pair in new[]{new[]{ids[0],ids[1]},new[]{ids[1],ids[2]},new[]{ids[3],ids[4]}})
        {
            var path=map.Path(pair[0],pair[1]);if(path.Count==0)throw new InvalidOperationException("No connected surface path between authored outposts: "+pair[0]+" / "+pair[1]);routes.Add(path.ToArray());
        }
        var light=Material("Home Light",AsuraBloodPalette.Light);var dark=Material("Home Dark",AsuraBloodPalette.Dark);
        var tendril=Material("Living tendril",Color.white,"Universal Render Pipeline/Particles/Unlit");
        var homePrefab=MakeOrganism(true,light);var linkPrefab=MakeOrganism(false,light);
        var root=new GameObject(RootName);var territory=root.AddComponent<AsuraTerritory>();
        territory.surfaceMap=map;territory.linkagePrefab=linkPrefab.GetComponent<AsuraLinkage>();territory.tendrilMaterial=tendril;
        AddCoverageOverlay(territory);
        territory.fighterPrefabs=new[]{AssetDatabase.LoadAssetAtPath<MeleeAsura>(AsuraPrototypeSetup.Folder+"/MeleeAsura.prefab") as Creature,AssetDatabase.LoadAssetAtPath<RangedAsura>(AsuraPrototypeSetup.Folder+"/RangedAsura.prefab")};
        for(int side=0;side<2;side++)
        {
            int index=ids[side==0?0:3];var homeObject=(GameObject)PrefabUtility.InstantiatePrefab(homePrefab);
            homeObject.transform.SetParent(root.transform);homeObject.transform.position=map.surfaces[index].position+Vector3.up*0.1f;
            homeObject.name=side==0?"Light Asura Home":"Dark Asura Home";
            var home=homeObject.GetComponent<AsuraHome>();home.home=home;home.surfaceIndex=index;
            SetPolarity(home,side==0?CreaturePolarity.Light:CreaturePolarity.Dark);
            foreach(var r in home.GetComponentsInChildren<Renderer>())r.sharedMaterial=side==0?light:dark;
            var fullPath=side==0?routes[0].Concat(routes[1].Skip(1)).ToArray():routes[2];
            float distance=0;int previous=fullPath[0];
            for(int i=1;i<fullPath.Length;i++)
            {
                distance+=Vector3.Distance(map.surfaces[previous].position,map.surfaces[fullPath[i]].position);previous=fullPath[i];
                if(distance<80 && i!=fullPath.Length-1)continue;
                // Avoid a redundant last node when the previous outpost is already near the endpoint.
                if(i==fullPath.Length-1 && distance<40)continue;
                var go=(GameObject)PrefabUtility.InstantiatePrefab(linkPrefab);go.transform.SetParent(root.transform);
                go.transform.position=map.surfaces[fullPath[i]].position+Vector3.up*0.1f;go.name=homeObject.name+" / initial linkage "+i;
                var node=go.GetComponent<AsuraLinkage>();node.home=home;node.surfaceIndex=fullPath[i];SetPolarity(node,side==0?CreaturePolarity.Light:CreaturePolarity.Dark);
                foreach(var r in node.GetComponentsInChildren<Renderer>())r.sharedMaterial=side==0?light:dark;
                distance=0;
            }
        }
        Undo.RegisterCreatedObjectUndo(root,"Create Asura territory");Selection.activeGameObject=root;
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        return new {surfaces=map.surfaces.Length,nodes=root.GetComponentsInChildren<AsuraLinkage>().Length,paths=routes.Select(p=>p.Length).ToArray()};
    }
    public static void AddCoverageOverlay(AsuraTerritory territory)
    {
        var overlay=territory.GetComponent<AsuraTerritoryOverlay>();if(overlay==null)overlay=territory.gameObject.AddComponent<AsuraTerritoryOverlay>();
        const string path=Folder+"/Coverage overlay.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(Shader.Find("Capstone/Asura/TerritoryOverlay"));AssetDatabase.CreateAsset(material,path);}
        overlay.overlayMaterial=material;overlay.showCoverage=false;EditorUtility.SetDirty(overlay);
    }
    private static Vector3 Ground(float x,float z)=>new Vector3(x,BasiltDensity.GroundHeight(x,z),z);
    private static void SetPolarity(Creature creature,CreaturePolarity polarity)
    {var s=new SerializedObject(creature);s.FindProperty("initialPolarity").enumValueIndex=(int)polarity;s.ApplyModifiedPropertiesWithoutUndo();}
    private static Material Material(string name,Color color,string shader="Universal Render Pipeline/Lit")
    {
        string path=Folder+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(m==null){m=new Material(Shader.Find(shader)){name=name};AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",color);m.SetColor("_Color",color);EditorUtility.SetDirty(m);return m;
    }
    private static GameObject MakeOrganism(bool home,Material material)
    {
        string path=AsuraPrototypeSetup.Folder+(home?"/AsuraHome.prefab":"/AsuraLinkage.prefab");
        var existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(existing!=null)return existing;
        var root=new GameObject(home?"AsuraHome":"AsuraLinkage");root.SetActive(false);root.layer=8;
        try
        {
            var body=root.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var collider=root.AddComponent<CapsuleCollider>();collider.radius=home?2.5f:1.1f;collider.height=home?7:3.4f;collider.center=Vector3.up*collider.height*0.5f;
            if(home)root.AddComponent<AsuraHome>();else root.AddComponent<AsuraLinkage>();
            float scale=home?2:1;
            Part(root.transform,PrimitiveType.Sphere,"Living core",new Vector3(0,1.6f*scale,0),new Vector3(1.7f,2.3f,1.7f)*scale,material);
            for(int claw=0;claw<5;claw++)
            {
                float angle=claw*Mathf.PI*2/5;Vector3 direction=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                Vector3[] p={direction*2.5f*scale+Vector3.up*0.1f,direction*1.7f*scale+Vector3.up*2.4f*scale,direction*0.7f*scale+Vector3.up*3.7f*scale};
                for(int i=0;i<2;i++)
                {
                    var part=Part(root.transform,PrimitiveType.Cylinder,"Root claw",(p[i]+p[i+1])*0.5f,new Vector3((i==0?0.38f:0.2f)*scale,Vector3.Distance(p[i],p[i+1])*0.5f,(i==0?0.38f:0.2f)*scale),material);
                    part.up=(p[i+1]-p[i]).normalized;
                }
            }
            root.SetActive(true);return PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally{Object.DestroyImmediate(root);}
    }
    private static Transform Part(Transform parent,PrimitiveType type,string name,Vector3 p,Vector3 scale,Material material)
    {
        var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localScale=scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=material;return go.transform;
    }
    public static AsuraSurfaceMap Bake()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Bake surfaces in Edit mode.");
        // A reload can leave GenerationComplete true while generated chunk children are absent.
        foreach(var world in Object.FindObjectsByType<ProceduralWorld>(FindObjectsSortMode.None))
            if(world.gameObject.scene==SceneManager.GetActiveScene())world.Generate(true);
        Physics.SyncTransforms();
        var map=AssetDatabase.LoadAssetAtPath<AsuraSurfaceMap>(Folder+"/BasiltSurfaces.asset");
        if(map==null){map=ScriptableObject.CreateInstance<AsuraSurfaceMap>();AssetDatabase.CreateAsset(map,Folder+"/BasiltSurfaces.asset");}
        map.bounds=new Bounds(new Vector3(0,55,0),new Vector3(432,170,504));map.spacing=6;
        var samples=new List<AsuraSurfaceMap.Surface>();var cells=new Dictionary<Vector2Int,List<int>>();
        for(int z=-252;z<=252;z+=6)for(int x=-216;x<=216;x+=6)
        {
            var list=new List<int>();float previous=-1000;
            foreach(var hit in Physics.RaycastAll(new Vector3(x,140,z),Vector3.down,170,1<<7,QueryTriggerInteraction.Ignore).OrderBy(h=>h.point.y))
            {
                if(hit.normal.y<0.72f || hit.point.y-previous<1.5f || !map.bounds.Contains(hit.point))continue;
                // Bake static surfaces only; authored homes or transient fighters must not alter indices.
                if(Physics.CheckCapsule(hit.point+Vector3.up*1.05f,hit.point+Vector3.up*1.85f,0.7f,1<<7,QueryTriggerInteraction.Ignore))continue;
                var renderer=hit.collider.GetComponent<Renderer>();
                bool vine=renderer!=null && renderer.sharedMaterial!=null && renderer.sharedMaterial.shader.name.Contains("BasiltVine");
                previous=hit.point.y;list.Add(samples.Count);samples.Add(new AsuraSurfaceMap.Surface{position=hit.point,area=36/Mathf.Max(0.72f,hit.normal.y),vine=vine});
            }
            cells[new Vector2Int(x,z)]=list;
        }
        foreach(var cell in cells)foreach(int index in cell.Value)
        {
            var neighbours=new List<int>();var a=samples[index].position;
            for(int dz=-6;dz<=6;dz+=6)for(int dx=-6;dx<=6;dx+=6)
            {
                if(dx==0 && dz==0)continue;
                if(!cells.TryGetValue(cell.Key+new Vector2Int(dx,dz),out var others))continue;
                foreach(int other in others)
                {
                    var b=samples[other].position;if(Mathf.Abs(a.y-b.y)>new Vector2(dx,dz).magnitude*0.65f)continue;
                    // Floor continuity alone would incorrectly connect ground through a basalt wall.
                    bool valid=!Physics.Linecast(a+Vector3.up*1.2f,b+Vector3.up*1.2f,1<<7,QueryTriggerInteraction.Ignore);
                    if(!valid)continue;
                    for(int j=1;j<=3;j++)
                    {
                        var p=Vector3.Lerp(a,b,j/4f);
                        if(!Physics.RaycastAll(p+Vector3.up*1.3f,Vector3.down,2.6f,1<<7,QueryTriggerInteraction.Ignore).Any(h=>h.normal.y>=0.65f && Mathf.Abs(h.point.y-p.y)<1.3f)) {valid=false;break;}
                    }
                    if(valid)neighbours.Add(other);
                }
            }
            samples[index].neighbours=neighbours.ToArray();
        }
        map.surfaces=samples.ToArray();EditorUtility.SetDirty(map);AssetDatabase.SaveAssets();return map;
    }
}
