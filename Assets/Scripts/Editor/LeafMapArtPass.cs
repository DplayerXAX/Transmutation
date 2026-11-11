using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Dense original purple bubble flowers, adjacent habitats and a climbable rift, using the ArtReference palette.</summary>
public static class LeafMapArtPass
{
    private const string Folder="Assets/Generated/LeafMap/ArtPass2";
    private const string GroupName="09 Dense meadow and stratified rift";
    private static Transform map, root;
    private static ProceduralWorld world;
    private static LeafMapEcology ecology;
    private static System.Random random;
    private static Material petals, stems, centres, moss, soil, stone, water, molten, shaft, nativePetals, nativeStems;
    private static GameObject nativeFlower;
    private static int serial, blooms;
    private static readonly Vector4[] Beds={
        new Vector4(-19,-84,12,9),new Vector4(17,-83,13,9),new Vector4(-25,-70,16,11),new Vector4(22,-68,17,12),
        new Vector4(-23,-52,17,12),new Vector4(23,-49,18,12),new Vector4(-20,-35,12,10),new Vector4(24,-32,12,10),new Vector4(39,-24,14,12),
        new Vector4(0,-77,23,12),new Vector4(0,-61,26,15),new Vector4(1,-43,26,15),new Vector4(30,-28,23,14)
    };

    [MenuItem("Tools/Capstone/Leaf Map/5 Dense Meadow And Geological Rift")]
    public static void ApplyAndCapture()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Leave Play Mode first.");
        var scene=SceneManager.GetSceneByPath(LeafMapSetup.ScenePath);
        if(!scene.IsValid() || !scene.isLoaded)scene=EditorSceneManager.OpenScene(LeafMapSetup.ScenePath,OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        // This pass is written in the first layout's coordinates; the grown leaf is dressed by LeafMapTimePass.
        var grown=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ProceduralWorld>()).FirstOrDefault();
        if(grown!=null && grown.SculptData!=null && LeafMapTerrain.ReadScale(grown.SculptData.Layout)>1f)
            throw new InvalidOperationException("The leaf map has been grown. Use '6 Grow Map, Flower Field And Time Decor' instead (or restore ArtCaptures/LeafMap/BeforeTimePass).");
        string backup="ArtCaptures/LeafMap/BeforeArtPass2";Directory.CreateDirectory(backup);
        if(!File.Exists(backup+"/Scene_LeafMap.unity"))
        {
            File.Copy(LeafMapSetup.ScenePath,backup+"/Scene_LeafMap.unity");
            foreach(string name in new[]{"01_Layout","04_FlowerMeadow","06_BonesAndRift"})
                if(File.Exists("ArtCaptures/LeafMap/"+name+".png"))File.Copy("ArtCaptures/LeafMap/"+name+".png",backup+"/"+name+".png");
        }
        EnsureFolder(Folder);random=new System.Random(211102);serial=0;blooms=0;
        map=scene.GetRootGameObjects().First(g=>g.name.StartsWith("Leaf Map —")).transform;
        world=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ProceduralWorld>()).First();
        ecology=map.GetComponent<LeafMapEcology>();
        LandmarkScenePreview.ClearAll();RelayoutHabitats();LeafMapTutorialGraft.Apply(world,map);
        var old=map.Find(GroupName);if(old!=null)Object.DestroyImmediate(old.gameObject);
        root=Group(GroupName,map);
        Materials();world.Generate(true);Physics.SyncTransforms();
        RetireOldDecoration();FlowerPrototype();DenseBeds();MatureFlowers();FungalGroves();GeologicalRift();Routes();FloatingPlatforms();Traversal();Views();
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
        LeafMapSetup.Preview();Validate();LeafMapSetup.Capture();LeafMapSetup.Review();
        File.WriteAllText("ArtCaptures/LeafMap/ArtPass2.txt","Dense meadow blooms: "+blooms+"\nInteractive flowers: "+ecology.FlowerCount+
            "\nOriginal purple four-petal flower prefab restored.\nSix floating stone traversal nodes.\n03 adjoining the meadow northwest / 05 northeast / 06 horizontal above 05.\nGenerated mesh assets: "+serial+
            "\nFinite geometry / main route / vent clearance: PASS\nOriginal ArtReference scene and materials preserved.");
    }

    private static void Materials()
    {
        petals=Variant("Assets/Materials/HabitatBlock/HB_GrowthMushroom.mat","PearlPetals",m=>{
            m.SetColor("_Top",new Color(0.96f,0.96f,0.87f));m.SetColor("_Shade",new Color(0.49f,0.61f,0.57f));
            m.SetColor("_Under",new Color(0.48f,0.78f,0.4f));m.SetColor("_Rim",new Color(0.69f,0.9f,0.86f));
            m.SetFloat("_Glow",0.24f);m.SetFloat("_SpotAmount",0.09f);m.SetFloat("_GillCount",18);m.SetFloat("_Sway",0.035f);m.SetFloat("_Cull",0);
        });
        stems=Variant("Assets/Materials/World/MAT_Realm_Root.mat","MeadowStems",m=>{
            m.SetColor("_Color",new Color(0.24f,0.38f,0.23f));m.SetColor("_ShadowColor",new Color(0.08f,0.16f,0.13f));
            m.SetColor("_GrainColor",new Color(0.025f,0.065f,0.06f));m.SetFloat("_GrainCount",5);m.SetFloat("_Sway",0.025f);
        });
        centres=Variant("Assets/Materials/HabitatBlock/HB_GrowthMushroom.mat","SeedCentres",m=>{
            m.SetColor("_Top",new Color(0.72f,0.75f,0.39f));m.SetColor("_Shade",new Color(0.22f,0.3f,0.15f));
            m.SetColor("_Rim",new Color(0.6f,0.75f,0.36f));m.SetColor("_Under",new Color(0.3f,0.55f,0.25f));m.SetFloat("_Glow",0.1f);m.SetFloat("_SpotAmount",0.1f);
        });
        moss=Variant("Assets/Materials/HabitatBlock/HB_GrowthMushroom.mat","MossAndBuds",m=>{
            m.SetColor("_Top",new Color(0.31f,0.48f,0.16f));m.SetColor("_Shade",new Color(0.08f,0.18f,0.12f));
            m.SetColor("_Rim",new Color(0.45f,0.65f,0.22f));m.SetColor("_Under",new Color(0.2f,0.3f,0.14f));m.SetFloat("_Glow",0.13f);m.SetFloat("_SpotAmount",0.05f);
        });
        stone=Mat("Assets/Materials/HabitatBlock/HB_Ground.mat");
        soil=stone;
        water=Mat("Assets/Generated/LeafMap/Leaf_CyanVein.mat");molten=Mat("Assets/Materials/HabitatBlock/HB_MoltenVein.mat");
        nativeFlower=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/BubbleFlower.prefab");
        nativePetals=Mat("Assets/Materials/Creatures/BubbleFlowerPetals.mat");nativeStems=Mat("Assets/Materials/Creatures/BubbleFlowerStem.mat");
        shaft=Variant("Assets/Materials/HabitatBlock/HB_FX_LightShaft.mat","RiftShaft",m=>{
            m.SetColor("_Color",new Color(0.4f,0.68f,0.84f));m.SetColor("_Color2",new Color(0.65f,0.84f,0.94f));m.SetFloat("_Intensity",0.22f);
        });
        // These two materials belong only to the leaf scene.
        var surface=Mat("Assets/Generated/LeafMap/Leaf_InkStone.mat");
        EditorUtility.CopySerialized(stone,surface);EditorUtility.SetDirty(surface);
        var path=Mat("Assets/Generated/LeafMap/Leaf_PathStone.mat");EditorUtility.CopySerialized(stone,path);EditorUtility.SetDirty(path);
        Edit(world,s=>{s.FindProperty("surfaceMaterial").objectReferenceValue=stone;s.FindProperty("innerMaterial").objectReferenceValue=stone;});
        RenderSettings.skybox=Mat(AssetDatabase.GUIDToAssetPath("02ec9149f3b94035aff10c37f9eaf9a3"));
    }
    private static void RelayoutHabitats()
    {
        string layout=System.Text.RegularExpressions.Regex.Replace(world.SculptData.Layout,"\"sinkholes\":\\[.*?\\]",
            "\"sinkholes\":[{\"position\":{\"x\":0,\"y\":10},\"radius\":5},{\"position\":{\"x\":45,\"y\":-9},\"radius\":4.5}]");
        world.SculptData.SetLayout(layout,Array.Empty<float>());EditorUtility.SetDirty(world.SculptData);
        var mother=map.GetComponentInChildren<BoneKiln>();mother.transform.position=Ground(new Vector2(45,-9));
        var west=map.Find("03 West cliffs — tentacle ledges and alternate ascent");
        if(west!=null)
        {
            var positions=new[]{new Vector2(-35,-29),new Vector2(-41,-18),new Vector2(-45,-5),new Vector2(-47,11),new Vector2(-34,24)};
            int n=0;
            foreach(var t in west.GetComponentsInChildren<TentacleCreature>())
            {
                if(t.name.Contains("gap lookout")){t.transform.position=new Vector3(-26,17.5f,-26);continue;}
                var p=positions[Mathf.Min(n++,positions.Length-1)];t.transform.position=Ground(p)+Vector3.up*0.9f;
            }
            n=0;var goo=new[]{new Vector2(-30,-34),new Vector2(-39,-20),new Vector2(-44,1),new Vector2(-34,25)};
            foreach(var g in west.GetComponentsInChildren<GooCreature>())g.transform.position=Ground(goo[Mathf.Min(n++,goo.Length-1)])+Vector3.up*0.3f;
            foreach(var spider in west.GetComponentsInChildren<SkySpider>())spider.transform.position=Ground(new Vector2(-58,8))+Vector3.up*2;
            foreach(var flower in west.GetComponentsInChildren<BubbleFlower>())flower.transform.position=Ground(new Vector2(-32,-30));
            foreach(var mf in west.GetComponentsInChildren<MeshFilter>())if(mf.name=="Grip crag" || mf.name.StartsWith("Cross-gap landing"))mf.gameObject.SetActive(false);
        }
        var quiet=map.Find("05b Quiet bone meadow — rest and approach");
        if(quiet!=null)
        {
            int i=0;var at=new[]{new Vector2(31,-27),new Vector2(59,-21)};
            foreach(var f in quiet.GetComponentsInChildren<BubbleFlower>())f.transform.position=Ground(at[Mathf.Min(i++,1)]);
            foreach(var t in quiet.GetComponentsInChildren<TentacleCreature>())t.transform.position=Ground(new Vector2(31,-21))+Vector3.up*0.8f;
        }
        foreach(var tree in map.GetComponentsInChildren<SquirrelTree>())
        {
            Vector3 p=tree.transform.position;p.y=LeafMapTerrain.Height(p.x,p.z)+3;tree.transform.position=p;
        }
        var gatherers=map.Find("02b Seed gatherers — meadow ecology");
        if(gatherers!=null)foreach(var creature in gatherers.GetComponentsInChildren<Creature>())
        {
            Vector3 p=creature.transform.position;SquirrelTree.PlaceOnGround(creature.transform,Ground(new Vector2(p.x,p.z)));
        }
        var meadow=map.Find("02 Bubble meadow — seeds and rising canopies");
        if(meadow!=null)foreach(var creature in meadow.GetComponentsInChildren<Creature>())
        {
            if(creature is LeashBubble)continue;
            Vector3 p=creature.transform.position;SquirrelTree.PlaceOnGround(creature.transform,Ground(new Vector2(p.x,p.z)));Record(creature.transform);
        }
        // Keep the visible transition thread on the flower side of the mother's resting body.
        var thread=map.GetComponentsInChildren<MeshFilter>().FirstOrDefault(m=>m.name.StartsWith("Cyan time beside"));
        if(thread!=null)thread.gameObject.SetActive(false);
    }
    private static void AppendNative(RealmMeshes target,MeshFilter original,Vector3 at,Quaternion turn,float scale)
    {
        Mesh source=original.sharedMesh;var vertices=source.vertices;var normals=source.normals;var uv=source.uv;
        Matrix4x4 matrix=Matrix4x4.TRS(at,turn,Vector3.one*scale)*original.transform.localToWorldMatrix;
        Matrix4x4 normal=matrix.inverse.transpose;int first=target.VertexCount;
        for(int i=0;i<vertices.Length;i++)target.Vertex(matrix.MultiplyPoint3x4(vertices[i]),normal.MultiplyVector(normals[i]).normalized,uv.Length>i?uv[i]:Vector2.zero,Vector2.zero);
        var triangles=source.triangles;for(int i=0;i<triangles.Length;i+=3)target.Triangle(first+triangles[i],first+triangles[i+1],first+triangles[i+2]);
    }
    private static void FloatingPlatforms()
    {
        var group=Group("06 Floating stone ground — traversal chains",root);
        var west=new[]{new Vector3(-28,12,-31),new Vector3(-26,15.6f,-26),new Vector3(-24,19,-19)};
        for(int i=0;i<west.Length;i++)
        {
            Vector3 p=ClearFloat(west[i],2.4f,2.4f,1.15f);Boulder(group,p,new Vector3(2.4f,1.15f,2.4f),"West floating stepping stone "+(i+1));
            if(i==1)
            {
                var teaching=map.GetComponentsInChildren<TentacleCreature>().FirstOrDefault(t=>t.name.Contains("gap lookout"));
                if(teaching!=null)teaching.transform.position=p+Vector3.up*2.1f;
            }
        }
        Vector3 centre=LeafMapTerrain.RiftPoint(0.63f),side=LeafMapTerrain.RiftSide(0.63f);
        for(int i=0;i<3;i++)
        {
            Vector3 p=centre+side*(6.2f-i*6.2f);p.y=15.5f+i*1.8f;
            p=ClearFloat(p,2.8f,2.25f,1.15f);Boulder(group,p,new Vector3(2.8f,1.15f,2.25f),"Rift floating stepping stone "+(i+1));
        }
        var path=new List<Vector3>{new Vector3(26,0,-26),new Vector3(34,0,-25),new Vector3(42,0,-23),new Vector3(55,0,-20)};
        for(int i=0;i<path.Count;i++){var p=path[i];p.y=LeafMapTerrain.Height(p.x,p.z)+0.06f;path[i]=p;}
        Ribbon(group,"Flower meadow into bone sanctuary",path,1.3f,Mat("Assets/Generated/LeafMap/Leaf_PathStone.mat"),true);
    }
    private static Vector3 ClearFloat(Vector3 p,float x,float z,float halfHeight)
    {
        float high=LeafMapTerrain.Height(p.x,p.z);
        foreach(float dx in new[]{-x,x})foreach(float dz in new[]{-z,z})high=Mathf.Max(high,LeafMapTerrain.Height(p.x+dx,p.z+dz));
        p.y=Mathf.Max(p.y,high+halfHeight+1.1f);return p;
    }
    private static void RetireOldDecoration()
    {
        foreach(var filter in map.GetComponentsInChildren<MeshFilter>(true))
        {
            string name=filter.name;
            if(name=="Leaf" || name=="Fronds — open route between clusters" || name=="Tall bubble flower canopy — stem" || name=="Iridescent canopy pearl"
                || name=="Heat at the bottom of the incision" || name=="Rift viewing perch")filter.gameObject.SetActive(false);
        }
        var routes=map.Find("Leaf veins — readable walking routes");if(routes!=null)routes.gameObject.SetActive(false);
        foreach(var tree in map.GetComponentsInChildren<SquirrelTree>())tree.GetComponent<Renderer>().enabled=false;
    }
    private static void FlowerPrototype()
    {
        const string path="Assets/Generated/LeafMap/MeadowFlower.prefab";
        var prefab=PrefabUtility.LoadPrefabContents(path);
        try { PresentFlower(prefab.GetComponent<BubbleFlower>());PrefabUtility.SaveAsPrefabAsset(prefab,path); }
        finally {PrefabUtility.UnloadPrefabContents(prefab);}
    }
    private static void PresentFlower(BubbleFlower flower)
    {
        foreach(string name in new[]{"Pearl bloom stalk","Pearl bloom petals"})
        {
            var old=flower.transform.Find(name);if(old!=null)Object.DestroyImmediate(old.gameObject);
        }
        foreach(var original in nativeFlower.GetComponentsInChildren<MeshRenderer>(true))
        {
            var target=flower.transform.Find(original.name);if(target==null)continue;
            var renderer=target.GetComponent<MeshRenderer>();renderer.enabled=original.enabled;renderer.sharedMaterials=original.sharedMaterials;
            var filter=target.GetComponent<MeshFilter>();filter.sharedMesh=original.GetComponent<MeshFilter>().sharedMesh;
            Record(renderer);Record(filter);
        }
    }
    private static void DenseBeds()
    {
        var group=Group("01 Original purple flower drifts — batched low flowers",root);
        // Small, dense flowers merge into readable drifts. Their stems do not block walking.
        for(int iz=0;iz<5;iz++)for(int ix=0;ix<4;ix++)
        {
            var head=new RealmMeshes();var stalk=new RealmMeshes();var heart=new RealmMeshes();var carpet=new RealmMeshes();var turf=new RealmMeshes();int count=0;
            float x0=-44+ix*22,z0=-96+iz*16;
            for(float z=z0;z<z0+16;z+=0.77f)for(float x=x0;x<x0+22;x+=0.77f)
            {
                float x1=Mathf.Min(x+0.77f,x0+22),z1=Mathf.Min(z+0.77f,z0+16);var p=new Vector2((x+x1)*0.5f,(z+z1)*0.5f);
                if(Bed(p)>0.97f || LeafMapTerrain.RouteDistance(p)<0.9f || LeafMapTerrain.Height(p.x,p.y)>16 || p.y>-18 || Vector2.Distance(p,new Vector2(45,-9))<12)continue;
                int a=turf.VertexCount;
                foreach(var q in new[]{new Vector2(x,z),new Vector2(x1,z),new Vector2(x,z1),new Vector2(x1,z1)})
                    turf.Vertex(Ground(q)+Vector3.up*0.055f,Vector3.up,Vector2.zero,Vector2.zero);
                turf.Triangle(a,a+2,a+1);turf.Triangle(a+1,a+2,a+3);
            }
            for(float z=z0;z<z0+16;z+=0.77f)for(float x=x0;x<x0+22;x+=0.77f)
            {
                var p=new Vector2(x+R(-0.25f,0.25f),z+R(-0.25f,0.25f));float bed=Bed(p);
                if(bed>1f || LeafMapTerrain.RouteDistance(p)<0.75f || LeafMapTerrain.Footprint(p.x,p.y)<9 || Vector2.Distance(p,new Vector2(45,-9))<12 || R(0,1)>Mathf.Lerp(0.95f,0.43f,Mathf.Pow(bed,4)))continue;
                if(p.y>-18 || LeafMapTerrain.Height(p.x,p.y)>16)continue;
                Vector3 at=Ground(p);float scale=R(0.72f,1.35f),piece=R(0,1);Quaternion turn=Quaternion.Euler(0,R(0,360),0);
                foreach(var child in nativeFlower.GetComponentsInChildren<MeshFilter>())
                {
                    if(child.name=="Head" || child.name=="Mouth")continue;
                    AppendNative(child.name=="Stem"?stalk:head,child,at,turn,scale);
                }
                // A distant LOD keeps the original rounded centre and four petals without thousands of rigidbodies.
                head.Sphere(at+Vector3.up*(0.65f*scale),Vector3.one*(0.15f*scale),turn,3,8,1,piece);
                if(count%3==0)carpet.Sphere(at+Vector3.up*0.025f,new Vector3(R(0.22f,0.42f),R(0.07f,0.14f),R(0.18f,0.32f)),Quaternion.identity,3,6,1,piece);
                count++;blooms++;
            }
            if(count==0)continue;
            Part("Low meadow turf",group,turf,soil);
            Part("Original four-petal flower drift "+ix+" "+iz,group,head,nativePetals);Part("Original green stems",group,stalk,nativeStems);Part("Low moss carpet",group,carpet,moss);
        }
    }
    private static float Bed(Vector2 p)
    {
        float d=float.MaxValue;
        foreach(var bed in Beds)d=Mathf.Min(d,new Vector2((p.x-bed.x)/bed.z,(p.y-bed.y)/bed.w).magnitude);
        return d+(Mathf.PerlinNoise(p.x*0.13f+7,p.y*0.13f+13)-0.5f)*0.18f;
    }
    private static void MatureFlowers()
    {
        var group=Group("02 Mature bubble flowers — interactive",root);
        _=ecology.FlowerCount;
        var flowers=map.GetComponentsInChildren<BubbleFlower>().Where(f=>f.isActiveAndEnabled).ToList();
        foreach(var flower in flowers)
        {
            PresentFlower(flower);Vector3 p=flower.transform.position;
            SquirrelTree.PlaceOnGround(flower.transform,Ground(new Vector2(p.x,p.z)));Record(flower.transform);
        }
        for(int attempt=0;flowers.Count<49 && attempt<1200;attempt++)
        {
            var p=new Vector2(R(-36,36),R(-89,-28));
            if(Bed(p)>0.83f || LeafMapTerrain.RouteDistance(p)<2.8f || Vector2.Distance(p,new Vector2(-12,-50))<5)continue;
            Vector3 at=Ground(p);
            if(flowers.Any(f=>Vector3.Distance(f.transform.position,at)<3.3f))continue;
            var go=Prefab("Assets/Generated/LeafMap/MeadowFlower.prefab",group,at,"Mature pearl bubble flower");
            SquirrelTree.PlaceOnGround(go.transform,at+Vector3.up*0.02f);
            var flower=go.GetComponent<BubbleFlower>();ecology.RegisterFlower(flower);flowers.Add(flower);
        }
        Edit(ecology,s=>s.FindProperty("maxFlowers").intValue=72);
        // Only one in four visible interactive blooms produces ambient bubbles. All retain carried shooting.
        var ordered=flowers.OrderBy(f=>f.transform.position.z).ThenBy(f=>f.transform.position.x).ToArray();
        for(int i=0;i<ordered.Length;i++)
        {
            ordered[i].ConfigureGroundEmission(i%4==0,new Vector2(12,22));
            EditorUtility.SetDirty(ordered[i]);Record(ordered[i]);
        }
        Edit(ecology,s=>s.FindProperty("ambientBubbleBudget").intValue=24);
        // A few real bubbles make the first view legible; the normal emitter budget includes them.
        for(int i=0;i<3;i++)
        {
            Vector2 p=new Vector2(i%2==0?-18:18,-83+i*10);
            Prefab("Assets/Generated/LeafMap/MeadowBubble.prefab",group,Ground(p)+Vector3.up*R(3.5f,7),"Meadow bubble — initial drift");
        }
    }
    private static void FungalGroves()
    {
        var group=Group("03 Native fungal forms — quiet transitions",root);var fungi=new RealmMeshes();var undergrowth=new RealmMeshes();
        foreach(var tree in map.GetComponentsInChildren<SquirrelTree>())
        {
            Vector3 p=tree.transform.position;Vector3 at=Ground(new Vector2(p.x,p.z));
            Stem(undergrowth,at,5.8f,0.7f,R(0,1));
            fungi.Sphere(at+Vector3.up*5.8f,new Vector3(2.75f,0.65f,2.6f),Quaternion.Euler(R(-8,8),R(0,360),R(-8,8)),9,22,1,R(0,1));
        }
        foreach(var centre in new[]{new Vector2(-34,-40),new Vector2(34,-23),new Vector2(58,-23),new Vector2(-20,96),new Vector2(23,98)})
        for(int i=0;i<14;i++)
        {
            var p=centre+new Vector2(R(-7,7),R(-6,6));
            if(LeafMapTerrain.RouteDistance(p)<3 || LeafMapTerrain.Footprint(p.x,p.y)<8)continue;
            RealmFlora.Mushroom(fungi,Ground(p),Vector3.up,R(1.2f,3.6f),random);
        }
        Part("Pale gilled mushrooms",group,fungi,petals);Part("Engraved fungal trunks",group,undergrowth,stems);
        // Slender pearl reeds reuse the reference's stalk/orb silhouette without palm-like leaves.
        var reeds=new RealmMeshes();var pearls=new RealmMeshes();
        foreach(var p in new[]{new Vector2(-32,-77),new Vector2(31,-74),new Vector2(-31,-43),new Vector2(31,-37),new Vector2(61,-21)})
        {
            var at=Ground(p);float h=R(3.8f,6.2f);var path=new List<Vector3>();
            for(int k=0;k<=8;k++)path.Add(at+new Vector3(Mathf.Sin(k/8f*1.7f)*0.65f,k/8f*h,0));
            reeds.Tube(path,t=>Mathf.Lerp(0.075f,0.035f,t),6,0,1,R(0,1));
            pearls.Sphere(path.Last(),Vector3.one*R(0.35f,0.57f),Quaternion.identity,8,12,1,R(0,1));
        }
        Part("Thin pearl reeds",group,reeds,stems);Part("Iridescent reed pearls",group,pearls,Mat("Assets/Materials/HabitatBlock/HB_Bubble.mat"));
    }
    private static void GeologicalRift()
    {
        var group=Group("04 Rift — broken strata and rising heat",root);
        var warm=Mat("Assets/Materials/Creatures/FireBubble.mat");
        const string ventPath="Assets/Generated/LeafMap/RiftFireBubble.prefab";
        var vent=PrefabUtility.LoadPrefabContents(ventPath);
        try {foreach(var renderer in vent.GetComponentsInChildren<Renderer>()){renderer.sharedMaterial=warm;Record(renderer);}PrefabUtility.SaveAsPrefabAsset(vent,ventPath);}
        finally {PrefabUtility.UnloadPrefabContents(vent);}
        for(int i=0;i<9;i++)for(int side=-1;side<=1;side+=2)for(int layer=0;layer<3;layer++)
        {
            float t=0.13f+i*0.084f+R(-0.025f,0.025f);Vector3 at=LeafMapTerrain.RiftPoint(t);
            Vector3 size=new Vector3(R(2.4f,4.2f),R(2.2f,4.5f),R(2.4f,4.8f));
            at+=LeafMapTerrain.RiftSide(t)*side*(LeafMapTerrain.RiftHalfWidth(t)+size.x*0.36f);
            at.y=LeafMapTerrain.RiftFloor(t)+4+layer*9+R(-2,2);
            Boulder(group,at,size,"Projecting wall stratum");
        }
        for(int i=0;i<54;i++)
        {
            float t=R(0.17f,0.86f);Vector3 at=LeafMapTerrain.RiftPoint(t);float r=R(0.25f,1.3f);
            at+=LeafMapTerrain.RiftSide(t)*R(-6,6);at.y=LeafMapTerrain.RiftFloor(t)+r*0.2f;
            Boulder(group,at,new Vector3(r,r*R(0.5f,1),r*R(0.7f,1.4f)),"Talus on the rift floor");
        }
        var floorPath=Enumerable.Range(0,32).Select(i=>{
            float t=Mathf.Lerp(0.12f,0.9f,i/31f);Vector3 p=LeafMapTerrain.RiftPoint(t);p.y=LeafMapTerrain.RiftFloor(t)+0.1f;p+=LeafMapTerrain.RiftSide(t)*Mathf.Sin(t*24)*1.4f;return p;
        }).ToArray();
        Ribbon(group,"Narrow molten vein",floorPath,0.7f,molten);
        for(int i=0;i<3;i++)
        {
            float t=0.24f+i*0.26f;Vector3 p=LeafMapTerrain.RiftPoint(t);p.y=LeafMapTerrain.RiftFloor(t)+0.3f;
            var beam=Cone(p,36f,1.0f,2.1f);Part("Cool light into the rift",group,beam,shaft);
            var light=Group("Cool reflected light",group).gameObject.AddComponent<Light>();
            light.transform.position=p+Vector3.up*14;light.type=LightType.Point;light.color=new Color(0.27f,0.6f,0.87f);light.range=24;light.intensity=22;
        }
        var old=map.Find("06 Fire rift — rising fuel and cliff traversal");
        if(old!=null)
        {
            foreach(var f in old.GetComponentsInChildren<FireFlower>())
            {
                f.transform.position=FloorPoint(0.33f)+Vector3.up*0.15f;
                Edit(f,s=>s.FindProperty("simulationEnabled").boolValue=false);
            }
            foreach(var f in old.GetComponentsInChildren<FireSource>())f.transform.position=FloorPoint(0.36f)+Vector3.up*1.0f;
            foreach(var light in old.GetComponentsInChildren<Light>()){light.transform.position=FloorPoint(0.38f)+Vector3.up*5;light.intensity=36;light.range=20;}
        }
        var initial=map.Find("Rising fire — initial catchable bubbles");
        if(initial!=null)for(int i=0;i<initial.childCount;i++)
        {
            Vector3 p=LeafMapTerrain.RiftPoint(0.28f+i*0.17f);p.y=-15+i*11;initial.GetChild(i).position=p;
            foreach(var renderer in initial.GetChild(i).GetComponentsInChildren<Renderer>()){renderer.sharedMaterial=warm;Record(renderer);}
        }
        var crystals=new RealmMeshes();
        for(int i=0;i<18;i++)
        {
            float t=R(0.2f,0.85f);Vector3 at=FloorPoint(t);at+=LeafMapTerrain.RiftSide(t)*(i%2==0?-1:1)*(LeafMapTerrain.RiftHalfWidth(t)-0.8f);at.y+=R(2,14);
            crystals.Crystal(at,Quaternion.Euler(R(-35,35),R(0,360),R(-30,30)),R(0.35f,1.1f),R(0.1f,0.3f),5,R(0,1),random);
        }
        Part("Sparse fluorite in wall seams",group,crystals,Mat("Assets/Materials/HabitatBlock/HB_Crystal_Fluorite.mat"));
    }
    private static Vector3 FloorPoint(float t){var p=LeafMapTerrain.RiftPoint(t);p.y=LeafMapTerrain.RiftFloor(t);return p;}
    private static RealmMeshes Cone(Vector3 foot,float height,float bottom,float top)
    {
        var mesh=new RealmMeshes();int start=mesh.VertexCount;
        for(int i=0;i<=8;i++)for(int j=0;j<=18;j++)
        {
            float t=i/8f,a=j/18f*Mathf.PI*2;Vector3 n=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
            mesh.Vertex(foot+Vector3.up*t*height+n*Mathf.Lerp(bottom,top,t),n,new Vector2(j/18f,1-t),Vector2.zero);
        }
        for(int i=0;i<8;i++)for(int j=0;j<18;j++){int a=start+i*19+j;mesh.Triangle(a,a+19,a+1);mesh.Triangle(a+1,a+19,a+20);}
        return mesh;
    }
    private static void Routes()
    {
        var group=Group("05 Soft-edged leaf-vein paths",root);var material=Mat("Assets/Generated/LeafMap/Leaf_PathStone.mat");
        foreach(var route in new[]{LeafMapTerrain.MainRoute,LeafMapTerrain.WestRoute,LeafMapTerrain.EastRoute,LeafMapTerrain.NorthRoute})
        {
            var points=new List<Vector3>();
            for(int i=1;i<route.Length;i++)for(int j=0;j<20;j++)
            {var p=Vector3.Lerp(route[i-1],route[i],j/19f);p.y=LeafMapTerrain.Height(p.x,p.z)+0.055f;points.Add(p);}
            Ribbon(group,"Quiet walking vein",points,1.25f,material,true);
        }
    }
    private static void Traversal()
    {
        var group=Group("07 Habitat traversal — ability routes and recovery ledges",root);
        // Flowers at the foot of each optional four-metre ascent; the ground path still connects the basin.
        var blooms=map.GetComponentsInChildren<BubbleFlower>().Where(f=>f.isActiveAndEnabled).OrderBy(f=>f.transform.position.z).ToArray();
        var launch=new[]{new Vector2(-2,-78),new Vector2(0,-65),new Vector2(2,-51)};
        for(int i=0;i<3;i++)
        {
            SquirrelTree.PlaceOnGround(blooms[i].transform,Ground(launch[i]));blooms[i].ConfigureGroundEmission(true,new Vector2(12,22));Record(blooms[i].transform);Record(blooms[i]);
        }
        int ambient=blooms.Count(f=>f.AutoEmitOnGround);
        foreach(var f in blooms.Reverse().Where(f=>f.AutoEmitOnGround && Array.IndexOf(blooms,f)>=3))
        {
            if(ambient<=13)break;f.ConfigureGroundEmission(false,new Vector2(12,22));Record(f);ambient--;
        }
        // Reachable rests on the west cliff, placed on the climbing line rather than scattered scenery.
        foreach(var p in new[]{new Vector3(-37,10,-23),new Vector3(-43,17,-10),new Vector3(-48,24,3)})
            Boulder(group,p,new Vector3(3,1,2.6f),"West climbing rest ledge");
        // Landings reward using a warmed child to carry height toward the rift.
        foreach(var p in new[]{new Vector3(57,13,-12),new Vector3(65,20,-1),new Vector3(70,25,12)})
            Boulder(group,ClearFloat(p,3,3,1),new Vector3(3,1,3),"Bone flight landing");
        // The canyon has a descent and recovery chain, not only a gap viewed from above.
        for(int i=0;i<5;i++)
        {
            float t=.25f+i*.09f;var p=LeafMapTerrain.RiftPoint(t)+LeafMapTerrain.RiftSide(t)*(i%2==0?6:-6);
            p.y=LeafMapTerrain.RiftFloor(t)+4+i*7;
            Boulder(group,p,new Vector3(3.5f,1,2.6f),"Canyon recovery ledge "+i);
        }
        // A short bounce/canopy route in the expanded northern grove.
        var northern=new[]{new Vector2(5,96),new Vector2(10,102),new Vector2(6,108),new Vector2(12,114)};
        for(int i=0;i<northern.Length;i++)
        {
            var p=Ground(northern[i]);p.y+=3+i*2;
            Boulder(group,ClearFloat(p,2.8f,2.8f,1),new Vector3(2.8f,1,2.8f),"Canopy hopping stone "+i);
        }
        Prefab("Assets/Prefabs/Creatures/BounceShroom.prefab",group,Ground(new Vector2(5,91)),"Canopy route — bounce launch");
        // Native ArtReference cold flora connects the flower edge to the mother's resting area.
        var frost=new RealmMeshes();var rng=new System.Random(45);
        for(int i=0;i<62;i++)
        {
            var p=new Vector2(R(24,69),R(-39,-13));
            if(LeafMapTerrain.RouteDistance(p)<2.4f || Vector2.Distance(p,new Vector2(45,-9))<16)continue;
            RealmFlora.Mushroom(frost,Ground(p),Vector3.up,R(.5f,1.7f),rng);
        }
        Part("Blue native frost garden — flower to bone transition",group,frost,Mat("Assets/Materials/World/MAT_Realm_Frost.mat"));
        File.WriteAllText("ArtCaptures/LeafMap/Traversal.txt","Meadow: three 4 m bubble terraces with launch flowers; walkable return.\nWest: tentacle ledges into high plateau.\nBones: three nearby children and ascending flight landings.\nRift: five recovery ledges from vent floor to upper bank.\nNorth: bounce launch and four canopy stones.");
    }
    public static void PreviewChildren(BoneKiln mother)
    {
        if(mother==null || mother.Root==null || mother.Root.GetComponentsInChildren<BoneChild>().Length>0)return;
        var bone=Mat("Assets/Materials/Landmarks/MAT_Landmark_BoneMarble.mat");
        for(int i=0;i<3;i++)
        {
            var at=LeafMapTerrain.ChildRest(i);
            var child=BoneChild.Spawn(mother.Root,at,180,83+i,bone,bone,mother.transform.position);
            child.name="[Preview] Nearby bone child "+(i+1);
            foreach(var t in child.GetComponentsInChildren<Transform>(true))t.gameObject.hideFlags=HideFlags.DontSave;
            foreach(var mf in child.GetComponentsInChildren<MeshFilter>())mf.sharedMesh.hideFlags=HideFlags.DontSave;
        }
    }
    private static void Ribbon(Transform group,string name,IList<Vector3> points,float width,Material material,bool ground=false)
    {
        var mesh=new RealmMeshes();var rows=new List<Vector3[]>();
        for(int i=0;i<points.Count;i++)
        {
            Vector3 tangent=(points[Mathf.Min(i+1,points.Count-1)]-points[Mathf.Max(i-1,0)]);tangent.y=0;
            if(tangent.sqrMagnitude<0.01f)tangent=Vector3.forward;Vector3 side=Vector3.Cross(Vector3.up,tangent).normalized;
            var row=new Vector3[7];float edge=width*(0.9f+Mathf.PerlinNoise(points[i].x*0.2f,points[i].z*0.2f)*0.2f);
            for(int j=0;j<7;j++) {var p=points[i]+side*(j/6f*2-1)*edge;if(ground)p.y=LeafMapTerrain.Height(p.x,p.z)+0.06f;row[j]=p;}rows.Add(row);
        }
        mesh.Grid(rows,0.6f);Part(name,group,mesh,material);
    }
    private static void Views()
    {
        var views=map.Find("08 Review cameras — disabled during play");
        var flower=views.Find("04_FlowerMeadow");flower.position=new Vector3(-21,6.15f,-85);flower.LookAt(new Vector3(-8,7.2f,-42));flower.GetComponent<Camera>().fieldOfView=64;
        var inside=views.Find("09_RiftInterior");if(inside==null){inside=Group("09_RiftInterior",views);EditorUtility.CopySerialized(flower.GetComponent<Camera>(),inside.gameObject.AddComponent<Camera>());}
        var at=FloorPoint(0.1f)+Vector3.up*9;var target=FloorPoint(0.68f)+Vector3.up*24;
        inside.SetPositionAndRotation(at,Quaternion.LookRotation(target-at));inside.GetComponent<Camera>().enabled=false;inside.GetComponent<Camera>().fieldOfView=72;
        var bones=views.Find("06_BonesAndRift");bones.position=new Vector3(93,42,-56);bones.LookAt(new Vector3(44,5,7));
        var cliffs=views.Find("05_WestCliffs");cliffs.position=new Vector3(-68,35,-60);cliffs.LookAt(new Vector3(-40,19,-9));
        var floating=views.Find("10_FloatingTraversal");
        if(floating==null){floating=Group("10_FloatingTraversal",views);EditorUtility.CopySerialized(flower.GetComponent<Camera>(),floating.gameObject.AddComponent<Camera>());}
        floating.position=new Vector3(-9,20,-43);floating.LookAt(new Vector3(-34,18,-12));floating.GetComponent<Camera>().enabled=false;
        var exit=views.Find("03_CaveExit");exit.position=new Vector3(0,12,-94);exit.LookAt(new Vector3(-2,5,-57));exit.GetComponent<Camera>().fieldOfView=70;
        var top=views.Find("01_Layout");top.position=new Vector3(0,310,-23);top.GetComponent<Camera>().orthographicSize=205;
        var overview=views.Find("02_Overview");overview.position=new Vector3(155,163,-221);overview.LookAt(new Vector3(0,0,-26));
        var cave=views.Find("11_OriginalTutorial");
        if(cave==null){cave=Group("11_OriginalTutorial",views);EditorUtility.CopySerialized(flower.GetComponent<Camera>(),cave.gameObject.AddComponent<Camera>());}
        cave.position=new Vector3(0,-17,-198);cave.LookAt(new Vector3(-6,-16,-177));cave.GetComponent<Camera>().enabled=false;
        var plateau=views.Find("12_WestPlateau");
        if(plateau==null){plateau=Group("12_WestPlateau",views);EditorUtility.CopySerialized(flower.GetComponent<Camera>(),plateau.gameObject.AddComponent<Camera>());}
        plateau.position=new Vector3(-63,32,19);plateau.LookAt(new Vector3(-21,10,-43));plateau.GetComponent<Camera>().enabled=false;
    }
    private static void Stem(RealmMeshes mesh,Vector3 at,float h,float radius,float piece)
    {
        mesh.Tube(new[]{at-Vector3.up*0.04f,at+new Vector3(0.025f,h*0.5f,0),at+Vector3.up*h},t=>radius*(1.1f-t*0.35f),5,0,0.9f,piece);
    }
    private static void Corolla(RealmMeshes mesh,Vector3 at,float radius,float piece)
    {
        for(int petal=0;petal<7;petal++)
        {
            float angle=petal/7f*Mathf.PI*2f;var dir=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));var side=Vector3.Cross(Vector3.up,dir);
            int first=mesh.VertexCount;
            for(int i=0;i<=4;i++)for(int j=0;j<3;j++)
            {
                float t=i/4f,u=j-1,r=radius*(0.11f+0.89f*t);
                float width=radius*0.27f*Mathf.Pow(Mathf.Max(0,Mathf.Sin(t*Mathf.PI)),0.7f);
                float y=radius*(0.03f+Mathf.Sin(t*Mathf.PI)*0.14f+t*t*0.08f+u*u*0.04f);
                mesh.Vertex(at+dir*r+side*(width*u)+Vector3.up*y,Vector3.up,new Vector2(0.95f,piece),new Vector2(petal/7f,t));
            }
            for(int i=0;i<4;i++)for(int j=0;j<2;j++){int a=first+i*3+j;mesh.Triangle(a,a+3,a+1);mesh.Triangle(a+1,a+3,a+4);}
        }
    }
    private static void Boulder(Transform parent,Vector3 at,Vector3 size,string name)
    {
        var mesh=new RealmMeshes();const int sides=8;var rings=new Vector3[5][];
        float[] levels={-1,-0.68f,0.12f,0.72f,1},radii={0.52f,0.95f,1,0.86f,0.43f};
        float turn=R(0,6.28f);var radial=Enumerable.Range(0,sides).Select(i=>R(0.83f,1.14f)).ToArray();
        for(int i=0;i<5;i++)
        {
            rings[i]=new Vector3[sides];var offset=new Vector3(R(-0.14f,0.14f),0,R(-0.14f,0.14f));
            for(int j=0;j<sides;j++){float a=turn+j/(float)sides*Mathf.PI*2;var v=new Vector3(Mathf.Cos(a)*radii[i]*radial[j],levels[i]+R(-0.055f,0.055f),Mathf.Sin(a)*radii[i]*radial[j])+offset;rings[i][j]=Vector3.Scale(v,size);}
        }
        for(int i=0;i<4;i++)for(int j=0;j<sides;j++){int k=(j+1)%sides;Face(rings[i][j],rings[i+1][j],rings[i][k]);Face(rings[i][k],rings[i+1][j],rings[i+1][k]);}
        for(int j=0;j<sides;j++){int k=(j+1)%sides;Face(Vector3.down*size.y,rings[0][j],rings[0][k]);Face(Vector3.up*size.y,rings[4][k],rings[4][j]);}
        var go=Part(name,parent,mesh,stone,true);go.transform.position=at;
        void Face(Vector3 a,Vector3 b,Vector3 c)
        {
            Vector3 n=Vector3.Cross(b-a,c-a).normalized;if(Vector3.Dot(n,(a+b+c)/3)<0){var swap=b;b=c;c=swap;n=-n;}
            int start=mesh.VertexCount;mesh.Vertex(a,n,Vector2.zero,Vector2.zero);mesh.Vertex(b,n,Vector2.zero,Vector2.zero);mesh.Vertex(c,n,Vector2.zero,Vector2.zero);mesh.Triangle(start,start+1,start+2);
        }
    }
    private static void Validate()
    {
        Physics.SyncTransforms();
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>())foreach(var v in filter.sharedMesh.vertices)
            if(!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z))throw new InvalidOperationException("Non-finite art mesh: "+filter.name);
        foreach(var p in LeafMapTerrain.MainRoute)
            if(!Physics.Raycast(new Vector3(p.x,LeafMapTerrain.Height(p.x,p.z)+1.7f,p.z),Vector3.down,5,1<<7))
            {
                Physics.Raycast(new Vector3(p.x,140,p.z),Vector3.down,out var hit,240,1<<7);
                throw new InvalidOperationException("No ground on entrance at "+p+"; predicted="+LeafMapTerrain.Height(p.x,p.z)+"; actual="+hit.point+"; collider="+hit.collider);
            }
        for(int i=2;i<9;i++)
        {
            Vector3 p=LeafMapTerrain.RiftPoint(i/10f);p.y=LeafMapTerrain.RiftFloor(i/10f)+5;
            if(world.SampleDensity(p)>-1)throw new InvalidOperationException("Vent corridor is blocked: "+p);
        }
    }
    private static GameObject Part(string name,Transform parent,RealmMeshes mesh,Material material,bool solid=false)=>MeshObject(name,parent,SaveMesh(mesh.Build(name)),material,solid);
    private static GameObject MeshObject(string name,Transform parent,Mesh mesh,Material material,bool solid)
    {
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=material;
        if(solid){go.layer=7;go.tag="Ground";go.AddComponent<MeshCollider>().sharedMesh=mesh;}
        return go;
    }
    private static Mesh SaveMesh(Mesh mesh)
    {
        mesh.hideFlags=HideFlags.None;string path=Folder+"/Mesh_"+(serial++).ToString("D3")+".asset";
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved==null){AssetDatabase.CreateAsset(mesh,path);return mesh;}
        // Mesh setters invalidate the live vertex/index buffers as well as the serialized asset.
        saved.Clear();saved.indexFormat=mesh.indexFormat;saved.SetVertices(mesh.vertices);saved.SetNormals(mesh.normals);
        saved.SetUVs(0,mesh.uv);saved.SetUVs(1,mesh.uv2);saved.SetTriangles(mesh.triangles,0);saved.bounds=mesh.bounds;
        saved.UploadMeshData(false);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);return saved;
    }
    private static Material Variant(string source,string name,Action<Material> tune)
    {
        string path=Folder+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(Mat(source)){name=name};AssetDatabase.CreateAsset(material,path);}tune(material);EditorUtility.SetDirty(material);return material;
    }
    private static GameObject Prefab(string path,Transform parent,Vector3 at,string name)
    {
        var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),parent);go.name=name;go.transform.position=at;return go;
    }
    private static void Edit(Object o,Action<SerializedObject> edit){var s=new SerializedObject(o);edit(s);s.ApplyModifiedPropertiesWithoutUndo();}
    private static void Record(Object o){if(PrefabUtility.IsPartOfPrefabInstance(o))PrefabUtility.RecordPrefabInstancePropertyModifications(o);}
    private static Material Mat(string path)=>AssetDatabase.LoadAssetAtPath<Material>(path)??throw new InvalidOperationException("Missing material "+path);
    private static Transform Group(string name,Transform parent){var go=new GameObject(name);go.transform.SetParent(parent,false);return go.transform;}
    private static Vector3 Ground(Vector2 p)
    {
        float y=LeafMapTerrain.Height(p.x,p.y);
        // The cave graft retains the donor's sculpted surface. Edge flowers must follow that mesh as well.
        if(p.y< -86 && world.GenerationComplete && Physics.Raycast(new Vector3(p.x,110,p.y),Vector3.down,out var hit,180,1<<7)
            && world.OwnsCollider(hit.collider))y=hit.point.y;
        return new Vector3(p.x,y+0.02f,p.y);
    }
    private static float R(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
    private static void EnsureFolder(string path){if(AssetDatabase.IsValidFolder(path))return;var parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
}
