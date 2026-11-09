using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>
/// Grows the leaf map 1.4x around the tutorial exit, joins the purple drifts into one flowing flower field,
/// brings the bone mother to its east rim and dresses the basin in the ArtReference "time" palette:
/// translucent veils, glass ribs, webs, iridescent bubbles, blue fungi, birds and cold light.
/// </summary>
public static class LeafMapTimePass
{
    public const float TargetScale=1.4f;
    private const string Folder="Assets/Generated/LeafMap/TimePass";
    private const string MovedFolder=Folder+"/Migrated";
    private const string GroupName="10 Time — flower field, bones and sky";
    private const string Backup="ArtCaptures/LeafMap/BeforeTimePass";
    private const string OldDrifts="01 Original purple flower drifts — batched low flowers";
    private const int FlowerVertexBudget=3600000;
    private static readonly HashSet<string> Untouched=new HashSet<string>{
        "01 Tutorial — original YoYoTutorialFall cave","01 Tutorial cavern — stem and first reveal","Creature templates (inactive)",GroupName,OldDrifts};
    private static Transform map, root;
    private static ProceduralWorld world;
    private static System.Random random;
    private static string meshPrefix="Mesh_";
    private static int serial, movedSerial, blooms;
    private static Material marble, comb, flora, bubble, ribbon, thread, strand, film, fungus, pool, shaft, nativePetals, nativeStems, violet, ice;
    private static Material[] veils;
    private static GameObject nativeFlower;
    private static readonly List<string> report=new List<string>();

    [MenuItem("Tools/Capstone/Leaf Map/6 Grow Map, Flower Field And Time Decor")]
    public static void ApplyAndCapture()
    {
        Apply();
        LeafMapSetup.Capture();
        LeafMapSetup.Review();
    }

    // Sculpting redraws the Scene view after every dab; hiding the flowers, veils and bones there keeps
    // it responsive. Only the Scene view changes: the game and the saved scene are untouched.
    [MenuItem("Tools/Capstone/Leaf Map/Toggle Decoration In Scene View (Sculpting)")]
    public static void ToggleDecoration()
    {
        var scene=SceneManager.GetSceneByPath(LeafMapSetup.ScenePath);
        if(!scene.IsValid() || !scene.isLoaded)return;
        var leaf=scene.GetRootGameObjects().First(g=>g.name.StartsWith("Leaf Map —")).transform;
        var groups=new[]{GroupName,"09 Dense meadow and stratified rift"}.Select(n=>leaf.Find(n)).Where(t=>t!=null).Select(t=>t.gameObject).ToArray();
        if(groups.Length==0)return;
        var visibility=SceneVisibilityManager.instance;
        bool hide=!visibility.IsHidden(groups[0]);
        foreach(var go in groups){if(hide)visibility.Hide(go,true);else visibility.Show(go,true);}
        LilyField.Hidden=hide;SceneView.RepaintAll();
    }

    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Leave Play Mode first.");
        var scene=SceneManager.GetSceneByPath(LeafMapSetup.ScenePath);
        if(!scene.IsValid() || !scene.isLoaded)scene=EditorSceneManager.OpenScene(LeafMapSetup.ScenePath,OpenSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        map=scene.GetRootGameObjects().First(g=>g.name.StartsWith("Leaf Map —")).transform;
        world=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ProceduralWorld>(true)).First();
        report.Clear();random=new System.Random(1102);movedSerial=0;
        EnsureFolder(MovedFolder);
        BackupOnce();
        LandmarkScenePreview.ClearAll();
        LeafMapTerrain.Scale=LeafMapTerrain.ReadScale(world.SculptData.Layout);
        RetireOldField();
        if(LeafMapTerrain.Scale<TargetScale-0.001f)Grow();
        else report.Add("Map already grown to "+LeafMapTerrain.Scale+"; only the decoration was rebuilt.");
        // Re-grafts only when the cave's height or the scale changed.
        LeafMapTutorialGraft.Apply(world,map);
        PlaceLandmarks();
        world.Generate(true);Physics.SyncTransforms();
        ClearMotherRest();
        Resettle();

        var old=map.Find(GroupName);if(old!=null)Object.DestroyImmediate(old.gameObject);
        root=Group(GroupName,map);random=new System.Random(20261102);serial=0;blooms=0;
        // Every mesh of this pass is rebuilt, so the old mesh assets go first (none is shared).
        foreach(string path in Directory.GetFiles(Folder,"Mesh_*.asset"))AssetDatabase.DeleteAsset(path.Replace('\\','/'));
        Materials();
        SeedGlow();
        BubbleGroves();
        BubbleLily();
        FlowerField();
        Veils();
        Bubbles();
        GlowGroves();
        FieldGroves();
        GroundTide();
        SkyAndBirds();
        Lights();
        if(LeafMapTerrain.HasRift)Rift();else Shaft();
        Atmosphere();
        Views();
        Validate();
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        LeafMapSetup.Preview();
        report.Insert(0,"Leaf scale: "+LeafMapTerrain.Scale+"  world size: "+world.WorldSize);
        report.Add("Field blooms: "+blooms);
        report.Add("Generated meshes: "+serial+" (+"+movedSerial+" migrated)");
        File.WriteAllText("ArtCaptures/LeafMap/TimePass.txt",string.Join("\n",report));
    }

    // ---------------- Growing the map ----------------

    private static void BackupOnce()
    {
        if(File.Exists(Backup+"/Scene_LeafMap.unity"))return;
        Directory.CreateDirectory(Backup);
        File.Copy(LeafMapSetup.ScenePath,Backup+"/Scene_LeafMap.unity");
        File.Copy(AssetDatabase.GetAssetPath(world.SculptData),Backup+"/LeafTerrain.asset");
        foreach(string name in new[]{"01_Layout","02_Overview","04_FlowerMeadow","06_BonesAndRift"})
            if(File.Exists("ArtCaptures/LeafMap/"+name+".png"))File.Copy("ArtCaptures/LeafMap/"+name+".png",Backup+"/"+name+".png");
        File.WriteAllText(Backup+"/README.txt","Copies taken before '6 Grow Map, Flower Field And Time Decor'.\n"+
            "To go back: close Unity, copy Scene_LeafMap.unity over Assets/Scenes/Scene_LeafMap.unity and LeafTerrain.asset over Assets/Generated/LeafMap/LeafTerrain.asset.\n"+
            "The terrain code reads leafScale from that asset, so the old copy restores the first layout.");
        report.Add("Backup: "+Backup);
    }

    private static void RetireOldField()
    {
        // The earlier separate drifts are replaced by one field; they stay in the scene, switched off.
        foreach(var t in map.GetComponentsInChildren<Transform>(true))
            if(t.name==OldDrifts || t.name=="Blue native frost garden — flower to bone transition" || t.name=="Flower meadow into bone sanctuary"
                || t.name=="Meadow runnel" || t.name=="Thin pearl reeds" || t.name=="Iridescent reed pearls"
                || t.name=="05 Soft-edged leaf-vein paths" || t.name=="Leaf veins — readable walking routes"
                || (!t.IsChildOf(map.Find(GroupName)??map) && (t.name=="04 Rift — broken strata and rising heat" || t.name.StartsWith("Rift floating stepping stone") || t.name.StartsWith("Canyon recovery ledge"))))
                t.gameObject.SetActive(false);
    }

    private static void Grow()
    {
        float k=TargetScale/LeafMapTerrain.Scale;
        var placed=new List<(Transform item,float ground)>();
        int moved=0,baked=0;
        foreach(Transform child in map.Cast<Transform>().ToArray())Visit(child);
        void Visit(Transform t)
        {
            if(!t.gameObject.activeSelf || Untouched.Contains(t.name))return;
            bool group=t.GetComponents<Component>().Length==1 && t.localPosition.sqrMagnitude<1e-4f && t.localScale==Vector3.one;
            if(group){foreach(Transform c in t.Cast<Transform>().ToArray())Visit(c);return;}
            var filter=t.GetComponent<MeshFilter>();
            if(filter!=null && t.position.sqrMagnitude<1e-4f && t.childCount==0 && t.localScale==Vector3.one){if(Rebake(filter,k))baked++;return;}
            placed.Add((t,LeafMapTerrain.Height(t.position.x,t.position.z)));
            t.position=Map(t.position,k);Record(t);moved++;
        }
        foreach(var flock in map.GetComponentsInChildren<BirdFlockManager>(true))
        {
            var b=flock.SkyBounds;flock.SkyBounds=new Bounds(Map(b.center,k),new Vector3(b.size.x*k,b.size.y,b.size.z*k));EditorUtility.SetDirty(flock);
        }
        foreach(var camera in map.GetComponentsInChildren<Camera>(true))if(camera.orthographic){camera.orthographicSize*=k;EditorUtility.SetDirty(camera);}

        // Terrain: the layout remembers the scale, the grid grows to hold the leaf, the cave is grafted again.
        string layout=world.SculptData.Layout;
        string value=TargetScale.ToString("0.0###",CultureInfo.InvariantCulture);
        layout=layout.Contains("\"leafScale\"")
            ? Regex.Replace(layout,"\"leafScale\":[-0-9.eE]+","\"leafScale\":"+value)
            : layout.Replace("\"leafMap\":true","\"leafMap\":true,\"leafScale\":"+value);
        if(!layout.Contains("\"leafScale\":"+value))throw new InvalidOperationException("Could not write leafScale into the saved layout.");
        LeafMapTerrain.Scale=TargetScale;
        layout=LeafMapTutorialGraft.WithWorldSize(layout);
        world.SculptData.SetLayout(layout,Array.Empty<float>());world.SculptData.ClearEdits();EditorUtility.SetDirty(world.SculptData);
        // Heights are unchanged by growing, except round the old and new resting places of the mother:
        // whatever rested on the ground there follows the new ground.
        int resettled=0;
        foreach(var (item,was) in placed)
        {
            float now=LeafMapTerrain.Height(item.position.x,item.position.z),lift=item.position.y-was;
            if(Mathf.Abs(now-was)<0.05f || lift>3.5f || lift<-2f)continue;
            item.position+=Vector3.up*(now-was);Record(item);resettled++;
        }
        report.Add("Resettled on changed ground: "+resettled);
        LeafMapTutorialGraft.Apply(world,map);
        report.Add("Grown x"+k+": "+moved+" placed objects moved, "+baked+" merged meshes re-laid.");
    }

    private static Vector3 Map(Vector3 p,float k)
    {
        Vector2 q=LeafMapTerrain.Pivot+(new Vector2(p.x,p.z)-LeafMapTerrain.Pivot)*k;
        return new Vector3(q.x,p.y,q.y);
    }

    // A merged decoration mesh: each separate piece moves with its centre, long ground ribbons stretch.
    private static bool Rebake(MeshFilter filter,float k)
    {
        Mesh mesh=filter.sharedMesh;
        if(mesh==null || !mesh.isReadable)return false;
        var vertices=mesh.vertices;var triangles=mesh.triangles;int n=vertices.Length;
        var parent=Enumerable.Range(0,n).ToArray();
        int FindRoot(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
        void Join(int a,int b){a=FindRoot(a);b=FindRoot(b);if(a!=b)parent[a]=b;}
        for(int i=0;i<triangles.Length;i+=3){Join(triangles[i],triangles[i+1]);Join(triangles[i],triangles[i+2]);}
        var weld=new Dictionary<Vector3Int,int>();
        for(int i=0;i<n;i++)
        {
            var key=Vector3Int.RoundToInt(vertices[i]*200f);
            if(weld.TryGetValue(key,out int other))Join(i,other);else weld[key]=i;
        }
        var pieces=new Dictionary<int,Bounds>();
        for(int i=0;i<n;i++)
        {
            int r=FindRoot(i);
            if(pieces.TryGetValue(r,out var b)){b.Encapsulate(vertices[i]);pieces[r]=b;}else pieces[r]=new Bounds(vertices[i],Vector3.zero);
        }
        for(int i=0;i<n;i++)
        {
            var b=pieces[FindRoot(i)];
            if(Mathf.Max(b.size.x,b.size.z)>12f)vertices[i]=Map(vertices[i],k);
            else vertices[i]+=Map(new Vector3(b.center.x,0,b.center.z),k)-new Vector3(b.center.x,0,b.center.z);
        }
        var copy=Object.Instantiate(mesh);copy.name=mesh.name;copy.vertices=vertices;copy.RecalculateBounds();
        string path=MovedFolder+"/Moved_"+(movedSerial++).ToString("D3")+".asset";
        if(AssetDatabase.LoadAssetAtPath<Mesh>(path)!=null)AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(copy,path);
        filter.sharedMesh=copy;
        var collider=filter.GetComponent<MeshCollider>();if(collider!=null)collider.sharedMesh=copy;
        return true;
    }

    private static void PlaceLandmarks()
    {
        var mother=map.GetComponentInChildren<BoneKiln>(true);
        var spire=map.GetComponentInChildren<HangingSpire>(true);
        mother.transform.position=LeafMapTerrain.MotherSpot;Record(mother.transform);
        Vector3 s=spire.transform.position,m=mother.transform.position;
        string holes=string.Format(CultureInfo.InvariantCulture,"\"sinkholes\":[{{\"position\":{{\"x\":{0:0.###},\"y\":{1:0.###}}},\"radius\":5.0}},{{\"position\":{{\"x\":{2:0.###},\"y\":{3:0.###}}},\"radius\":4.5}}]",s.x,s.z,m.x,m.z);
        string layout=Regex.Replace(world.SculptData.Layout,"\"sinkholes\":\\[.*?\\]",holes);
        if(layout!=world.SculptData.Layout){world.SculptData.SetLayout(layout,Array.Empty<float>());EditorUtility.SetDirty(world.SculptData);}
        AssetDatabase.SaveAssetIfDirty(world.SculptData);
    }

    // Nothing interactive is left inside the mother's resting body.
    private static void ClearMotherRest()
    {
        Vector3 mother=LeafMapTerrain.MotherSpot;int pushed=0;
        var things=map.GetComponentsInChildren<Creature>().Select(c=>c.transform).Concat(map.GetComponentsInChildren<SquirrelTree>().Select(t=>t.transform))
            .Where(t=>t.GetComponentInParent<BoneKiln>()==null && t.parent!=null && t.parent.GetComponentInParent<Creature>()==null).Distinct().ToArray();
        foreach(var t in things)
        {
            Vector2 offset=new Vector2(t.position.x-mother.x,t.position.z-mother.z);
            if(offset.magnitude>15f || t.GetComponent<BirdCreature>()!=null)continue;
            if(offset.sqrMagnitude<0.01f)offset=Vector2.left;
            Vector2 to=new Vector2(mother.x,mother.z)+offset.normalized*Range(16f,19f);
            float lift=t.position.y-LeafMapTerrain.Height(t.position.x,t.position.z);
            Vector3 ground=Ground(to);
            if(t.GetComponent<SquirrelTree>()!=null){t.position=ground+Vector3.up*lift;}
            else SquirrelTree.PlaceOnGround(t,ground+Vector3.up*0.03f);
            Record(t);pushed++;
        }
        report.Add("Mother moved to "+mother+"; "+pushed+" nearby objects set aside.");
    }

    // ---------------- Materials ----------------

    private static void Materials()
    {
        marble=Mat("Assets/Materials/Landmarks/MAT_Landmark_BoneMarble.mat");
        comb=Mat("Assets/Materials/HabitatBlock/HB_BoneComb.mat");
        flora=Mat("Assets/Materials/HabitatBlock/HB_Flora.mat");
        bubble=Mat("Assets/Materials/HabitatBlock/HB_Bubble.mat");
        ribbon=Mat("Assets/Materials/HabitatBlock/HB_TimeRibbon.mat");
        thread=Mat("Assets/Materials/World/MAT_Realm_Thread.mat");
        strand=Mat("Assets/Materials/World/MAT_Realm_WebStrand.mat");
        film=Mat("Assets/Materials/World/MAT_Realm_WebFilm.mat");
        fungus=Mat("Assets/Materials/World/MAT_Realm_MushroomBlue.mat");
        pool=Mat("Assets/Materials/HabitatBlock/HB_FX_TimeRipples.mat");
        veils=Enumerable.Range(0,4).Select(i=>Mat("Assets/Materials/HabitatBlock/HB_FX_Veil"+i+".mat")).ToArray();
        nativeFlower=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/BubbleFlower.prefab");
        nativePetals=Mat("Assets/Materials/Creatures/BubbleFlowerPetals.mat");nativeStems=Mat("Assets/Materials/Creatures/BubbleFlowerStem.mat");
        // The purple field cools towards the mother: violet, then ice blue that glows a little at dusk.
        violet=Variant(nativePetals,"TimePetals_Violet",m=>{SetBase(m,new Color(0.5f,0.43f,0.97f));m.SetFloat("_Smoothness",0.4f);});
        ice=Variant(nativePetals,"TimePetals_Ice",m=>{
            SetBase(m,new Color(0.46f,0.74f,1f));m.SetFloat("_Smoothness",0.55f);
            m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",new Color(0.06f,0.2f,0.42f));m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;
        });
        // The leaf's own copy of the ArtReference ground, with its polar line centre grown with the map
        // so the rings stay outside the leaf, as in the first layout.
        var ground=Mat("Assets/Generated/LeafMap/Leaf_InkStone.mat");
        EditorUtility.CopySerialized(Mat("Assets/Materials/HabitatBlock/HB_Ground.mat"),ground);ground.name="Leaf_InkStone";
        Vector2 centre=LeafMapTerrain.ToWorld(new Vector2(110,150));ground.SetVector("_Center",new Vector4(centre.x,0,centre.y,0));
        // Calmer ground for a big walkable map: no grey hatch grid, fewer and smoother contour lines.
        ground.SetFloat("_HatchStrength",0f);ground.SetFloat("_RingDistort",0f);
        ground.SetFloat("_RingFreq",5f);ground.SetFloat("_RayPhaseScale",5f);ground.SetFloat("_RayPhaseProximityBoost",0f);ground.SetFloat("_FloorFreq",1.5f);
        EditorUtility.SetDirty(ground);
        // The inner world under the crust keeps Scene_Daniel's own underground look.
        var inner=Mat("Assets/Materials/World/MAT_World_Inner.mat");
        var settings=new SerializedObject(world);settings.FindProperty("surfaceMaterial").objectReferenceValue=ground;settings.FindProperty("innerMaterial").objectReferenceValue=inner;settings.ApplyModifiedPropertiesWithoutUndo();
        foreach(var renderer in world.GetComponentsInChildren<MeshRenderer>(true))
            renderer.sharedMaterials=renderer.sharedMaterials.Select((_,i)=>i==0?ground:inner).ToArray();
        // Rocks of the earlier passes use the same calm ground, so they read as the terrain.
        var original=Mat("Assets/Materials/HabitatBlock/HB_Ground.mat");
        foreach(var renderer in map.GetComponentsInChildren<MeshRenderer>(true))
            if(renderer.sharedMaterials.Contains(original))renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>m==original?ground:m).ToArray();
        shaft=Variant(Mat("Assets/Materials/HabitatBlock/HB_FX_LightShaft.mat"),"TimeShaft",m=>{
            m.SetColor("_Color",new Color(0.3f,0.52f,0.9f));m.SetColor("_Color2",new Color(0.6f,0.8f,1f));m.SetFloat("_Intensity",0.055f);
        });
    }
    private static void SetBase(Material m,Color c){m.SetColor("_BaseColor",c);m.SetColor("_Color",c);}

    // ---------------- One flowing flower field ----------------

    // Positive inside the field (design metres): the basin, a tongue to the mother, a ring round her flank,
    // a spill below the tutorial exit and one up the western terrace.
    private static float FieldMask(Vector2 d)
    {
        Vector2 m=LeafMapTerrain.MotherDesign;
        float body=1f-new Vector2((d.x+2f)/52f,(d.y+58f)/40f).magnitude;
        float tongue=1f-Capsule(d,new Vector2(4,-62),m+new Vector2(-4,-2))/14f;
        Vector2 fromMother=d-m;float ring=fromMother.magnitude;
        float facing=Vector2.Dot(fromMother.normalized,new Vector2(-0.8f,-0.6f));
        float wrap=(1f-Mathf.Abs(ring-13f)/7f)*Smooth(-0.55f,0.1f,facing);
        float exit=1f-Capsule(d,new Vector2(1,-90),new Vector2(-1,-74))/8f;
        float west=1f-Capsule(d,new Vector2(-30,-60),new Vector2(-40,-34))/11f;
        float mask=Mathf.Max(Mathf.Max(body*2.2f,tongue),Mathf.Max(wrap,Mathf.Max(exit,west)));
        return mask+(Mathf.PerlinNoise(d.x*0.09f+3.1f,d.y*0.09f+8.7f)-0.5f)*0.42f;
    }
    // Lines of the flow run east-north-east, from the exit towards the mother.
    private static float Flow(Vector2 d)=>(d.y-0.45f*d.x)*0.16f+1.8f*Mathf.PerlinNoise(d.x*0.035f+40f,d.y*0.035f+12f);
    private static float Clump(Vector2 d)
    {
        float wx=Mathf.PerlinNoise(d.x*0.04f+11f,d.y*0.04f+3f)-0.5f,wz=Mathf.PerlinNoise(d.x*0.04f+29f,d.y*0.04f+17f)-0.5f;
        Vector2 q=d+new Vector2(wx,wz)*18f;
        return Mathf.PerlinNoise(q.x*0.085f+5f,q.y*0.085f+9f)*0.75f+Mathf.PerlinNoise(q.x*0.23f+2f,q.y*0.23f+7f)*0.25f;
    }
    private static float Capsule(Vector2 p,Vector2 a,Vector2 b)=>LeafMapTerrain.Distance(p,a,b,out _);

    private static void FlowerField()
    {
        var group=Group("01 One flowing flower field — purple to time blue",root);
        int perFlower=lilyField.vertexCount;
        Vector3 mother=LeafMapTerrain.MotherSpot,spire=LeafMapTerrain.SpireSpot;
        var points=new List<(Vector2 w,float tone,float scale)>();
        // The real bubble flowers and their seeds stand in small clearings, so they read among the carpet.
        var keep=map.GetComponentsInChildren<BubbleFlower>().Select(f=>(Flat(f.transform.position),1.8f))
            .Concat(map.GetComponentsInChildren<PlantableBubbleSeed>().Select(f=>(Flat(f.transform.position),1.4f))).ToArray();
        Vector2 lo=LeafMapTerrain.ToWorld(new Vector2(-62,-100)),hi=LeafMapTerrain.ToWorld(new Vector2(58,-12));
        const float step=0.6f;
        for(float z=lo.y;z<hi.y;z+=step)for(float x=lo.x;x<hi.x;x+=step)
        {
            Vector2 w=new Vector2(x+Range(-0.3f,0.3f),z+Range(-0.3f,0.3f));Vector2 d=LeafMapTerrain.ToDesign(w);
            float mask=FieldMask(d);if(mask<=0f)continue;
            float flow=Flow(d);
            // Natural drifts rather than lines: domain-warped clumps of different sizes, thick in the middle,
            // breaking into stragglers at their edges, with small glades between them.
            float clump=Clump(d);
            float chance=Smooth(0f,0.3f,mask)*Mathf.Lerp(0.08f,1f,Smooth(0.3f,0.62f,clump));
            if(Range(0,1)>chance)continue;
            float route=LeafMapTerrain.RouteDistance(w);
            if(LeafMapTerrain.RiftDistance(w,out float rt)<LeafMapTerrain.RiftHalfWidth(rt)+2.5f || keep.Any(k=>(k.Item1-w).sqrMagnitude<k.Item2*k.Item2))continue;
            if(LeafMapTerrain.Footprint(w.x,w.y)<6f || Vector2.Distance(w,new Vector2(mother.x,mother.z))<9.5f
                || Vector2.Distance(w,new Vector2(spire.x,spire.z))<30f || LeafMapTerrain.Height(w.x,w.y)>19f)continue;
            float near=Mathf.InverseLerp(36f,9f,Vector2.Distance(d,LeafMapTerrain.MotherDesign));
            float tone=Mathf.Clamp01(near*1.15f+0.22f*Mathf.Sin(flow*3f+1.3f)+(Mathf.PerlinNoise(d.x*0.2f,d.y*0.2f)-0.5f)*0.3f);
            // Taller in the heart of a drift, low at its edge, so each drift has a soft dome.
            float size=Range(0.55f,0.82f)*Mathf.Lerp(0.85f,1.6f,Smooth(0.45f,0.85f,clump));
            points.Add((w,tone,size));
        }
        int budget=FlowerVertexBudget/Mathf.Max(1,perFlower);
        if(points.Count>budget)points=points.OrderBy(_=>random.Next()).Take(budget).ToList();
        // Every point becomes an instanced lily (LilyField); a few per area are real emitting flowers.
        var field=new List<(Vector3 at,float yaw,float scale,int tone)>();
        foreach(var p in points)
        {
            Vector3 at=Ground(p.w,out Vector3 normal);
            // No flowers on the steep walls of the bubble terraces.
            if(normal.y<0.74f)continue;
            field.Add((at,Range(0,360),p.scale,p.tone<0.38f?0:p.tone<0.68f?1:2));
        }
        FieldEmitters(field);
        blooms=field.Count;
        var bytes=new byte[field.Count*6*4];var values=new float[field.Count*6];
        for(int k=0;k<field.Count;k++){var f=field[k];values[k*6]=f.at.x;values[k*6+1]=f.at.y;values[k*6+2]=f.at.z;values[k*6+3]=f.yaw;values[k*6+4]=f.scale;values[k*6+5]=f.tone;}
        System.Buffer.BlockCopy(values,0,bytes,0,bytes.Length);
        string dataPath=Folder+"/LilyField.bytes";File.WriteAllBytes(dataPath,bytes);AssetDatabase.ImportAsset(dataPath);
        var lilies=group.gameObject.AddComponent<LilyField>();
        var settings=new SerializedObject(lilies);
        settings.FindProperty("data").objectReferenceValue=AssetDatabase.LoadAssetAtPath<TextAsset>(dataPath);
        settings.FindProperty("mesh").objectReferenceValue=lilyField;
        settings.FindProperty("stem").objectReferenceValue=lilyStem;settings.FindProperty("inner").objectReferenceValue=lilyInner;settings.FindProperty("bud").objectReferenceValue=lilyBud;
        var outerList=settings.FindProperty("outer");outerList.arraySize=lilyFieldTones.Length;for(int k=0;k<lilyFieldTones.Length;k++)outerList.GetArrayElementAtIndex(k).objectReferenceValue=lilyFieldTones[k];
        var liveList=settings.FindProperty("liveOuter");liveList.arraySize=lilyOuter.Length;for(int k=0;k<lilyOuter.Length;k++)liveList.GetArrayElementAtIndex(k).objectReferenceValue=lilyOuter[k];
        settings.FindProperty("livePool").intValue=32;settings.FindProperty("livePrefab").objectReferenceValue=AssetDatabase.LoadAssetAtPath<BubbleFlower>("Assets/Generated/LeafMap/MeadowFlower.prefab");
        settings.ApplyModifiedPropertiesWithoutUndo();
        lilies.Load();
        report.Add("Field: "+blooms+" instanced lilies, "+perFlower+" vertices each; the ones within reach of the player turn real.");
    }
    // The field is cut into areas; each area gets a few real flowers that the cascade fires in turn, so bubbles
    // rise all over the field rather than only where flowers happened to be placed.
    private static void FieldEmitters(List<(Vector3 at,float yaw,float scale,int tone)> field)
    {
        var ecology=map.GetComponent<LeafMapEcology>();
        var group=Group("13 Field bubble zones — emitters by area",root);
        var real=map.GetComponentsInChildren<BubbleFlower>().Select(f=>Flat(f.transform.position)).ToList();
        var emitters=new List<BubbleFlower>();
        const float area=28f;
        foreach(var cell in field.Select((f,i)=>(f,i)).GroupBy(e=>new Vector2Int(Mathf.FloorToInt(e.f.at.x/area),Mathf.FloorToInt(e.f.at.z/area))).ToList())
        {
            if(cell.Count()<25)continue;
            int placed=0;
            foreach(var e in cell.OrderBy(_=>random.Next()))
            {
                if(placed>=2)break;
                Vector2 w=Flat(e.f.at);
                if(real.Any(r=>(r-w).sqrMagnitude<7f*7f))continue;
                var go=Prefab("Assets/Generated/LeafMap/MeadowFlower.prefab",group,e.f.at,"Field emitter "+(emitters.Count+1));
                go.transform.rotation=Quaternion.Euler(0,e.f.yaw,0);
                SquirrelTree.PlaceOnGround(go.transform,e.f.at+Vector3.up*0.02f);
                var flower=go.GetComponent<BubbleFlower>();
                if(ecology!=null)ecology.RegisterFlower(flower);
                flower.ConfigureGroundEmission(false,new Vector2(8f,14f));EditorUtility.SetDirty(flower);Record(flower);
                emitters.Add(flower);real.Add(w);placed++;
            }
        }
        // The emitters take the place of instances: drop the field points right under them.
        field.RemoveAll(f=>emitters.Any(e=>(Flat(e.transform.position)-Flat(f.at)).sqrMagnitude<1.2f*1.2f));
        group.gameObject.AddComponent<BubbleCascade>().Configure(emitters.OrderBy(e=>e.transform.position.z).ToList(),9f);
        if(ecology!=null)
        {
            var settings=new SerializedObject(ecology);
            settings.FindProperty("ambientBubbleBudget").intValue=60;settings.FindProperty("maxFlowers").intValue=160;
            var range=settings.FindProperty("emissionRange");if(range!=null)range.floatValue=80f;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
        report.Add("Field zones: "+emitters.Count+" real emitters, two at most per 28 m area, fired in turn every 9 s.");
    }
    private static void AppendNative(RealmMeshes target,MeshFilter original,Vector3 at,Quaternion turn,float scale)
    {
        Mesh source=original.sharedMesh;var vertices=source.vertices;var normals=source.normals;var uv=source.uv;
        Matrix4x4 matrix=Matrix4x4.TRS(at,turn,Vector3.one*scale)*nativeFlower.transform.worldToLocalMatrix*original.transform.localToWorldMatrix;
        Matrix4x4 normal=matrix.inverse.transpose;int first=target.VertexCount;
        for(int i=0;i<vertices.Length;i++)target.Vertex(matrix.MultiplyPoint3x4(vertices[i]),normal.MultiplyVector(normals.Length>i?normals[i]:Vector3.up).normalized,uv.Length>i?uv[i]:Vector2.zero,Vector2.zero);
        var triangles=source.triangles;for(int i=0;i<triangles.Length;i+=3)target.Triangle(first+triangles[i],first+triangles[i+1],first+triangles[i+2]);
    }

    // ---------------- Time veils ----------------

    private static void Veils()
    {
        var group=Group("04 Time veils — flowing from the mother over the field and into the sky",root);
        Vector2 m=LeafMapTerrain.MotherDesign;
        // Composition: one leading veil from the cave overlook down over the field into the mother, her
        // spiral as the vertical hero, one high ribbon across the leaf. Everything else stays open.
        var paths=new List<(Vector2[] path,float[] lift,float width)>{
            (new[]{new Vector2(-4,-88),new Vector2(0,-76),new Vector2(10,-66),new Vector2(20,-54),m+new Vector2(-6,-4)},new[]{6f,7f,5.5f,4.5f,4f},3f),
        };
        var threads=new RealmMeshes();
        for(int v=0;v<paths.Count;v++)
        {
            var keys=new List<Vector3>();
            for(int i=0;i<paths[v].path.Length;i++)
            {
                Vector2 w=LeafMapTerrain.ToWorld(paths[v].path[i]);
                keys.Add(new Vector3(w.x,Highest(w,paths[v].width)+paths[v].lift[i],w.y));
            }
            var path=Clearance(CatmullRom(keys,14),paths[v].width*0.5f+0.6f);
            Shadowless(Part("Time veil "+(v+1),group,Veil(path,paths[v].width,v*0.37f),veils[v%veils.Length]));
            threads.Tube(path.Select((p,i)=>p+Vector3.up*Mathf.Sin(i*0.21f+v)*paths[v].width*0.45f).ToList(),t=>0.022f,4,0f,0.1f,Range(0,1));
        }
        // One high ribbon carries the time colour on from the spire to the leaf tip.
        foreach(var (path,lift,width,index) in new[]{
            (new[]{new Vector2(10,18),new Vector2(-2,52),new Vector2(-14,86),new Vector2(-4,112),new Vector2(8,132)},new[]{26f,32f,30f,24f,20f},5f,0)})
        {
            var keys=path.Select((d,i)=>{Vector2 w=LeafMapTerrain.ToWorld(d);return new Vector3(w.x,Highest(w,width)+lift[i],w.y);}).ToList();
            var line=Clearance(CatmullRom(keys,14),width*0.5f+0.6f);
            Shadowless(Part("Time veil — spire to leaf tip",group,Veil(line,width,2.3f+index),veils[index]));
            threads.Tube(line.Select((q,i)=>q+Vector3.up*Mathf.Sin(i*0.17f)*width*0.4f).ToList(),t=>0.024f,4,0f,0.1f,Range(0,1));
        }
        // A spiral of veil climbing out of the mother's back towards the open sky.
        Vector3 mother=LeafMapTerrain.MotherSpot;var spiral=new List<Vector3>();
        for(int i=0;i<=90;i++)
        {
            float t=i/90f,a=t*Mathf.PI*4.2f+0.6f,r=Mathf.Lerp(7f,15f,t);
            spiral.Add(mother+new Vector3(Mathf.Cos(a)*r,Mathf.Lerp(4f,46f,t*t*0.6f+t*0.4f),Mathf.Sin(a)*r));
        }
        spiral=Clearance(spiral,2.2f);
        Shadowless(Part("Time veil — spiral into the sky",group,Veil(spiral,3.4f,1.7f),veils[3]));
        threads.Tube(spiral,t=>0.026f,4,0f,0.1f,0.4f);
        // A jelly ribbon winds through the arch's ribs and on into the mother, as in the reference board.
        var jelly=new RealmMeshes();
        foreach(var through in new[]{
            new[]{new Vector2(8,-70),new Vector2(13,-65),new Vector2(18,-60),new Vector2(25,-52),m+new Vector2(-9,-3)}})
        {
            var keys=through.Select(d=>{Vector2 w=LeafMapTerrain.ToWorld(d);return new Vector3(w.x,Highest(w,1f)+Range(2.2f,4.2f),w.y);}).ToList();
            var path=Clearance(CatmullRom(keys,20),0.8f);var ups=new List<Vector3>();
            for(int i=0;i<path.Count;i++){Vector3 f=(path[Mathf.Min(i+1,path.Count-1)]-path[Mathf.Max(i-1,0)]).normalized;ups.Add(Quaternion.AngleAxis(i*9f,f)*Vector3.up);}
            jelly.Ribbon(path,ups,0.42f,6,Range(0,1));
            threads.Tube(path.Select((p,i)=>p+ups[i]*0.35f).ToList(),t=>0.016f,4,0f,0.1f,Range(0,1));
        }
        Shadowless(Part("Jelly time ribbons through the bones",group,jelly,ribbon));
        Shadowless(Part("Bright threads along the veils",group,threads,thread));
    }

    // ---------------- Glowing groves for the rest of the leaf ----------------

    // Bioluminescent groves on the new knolls round the leaf (after the night-forest reference): a dark curved
    // trunk with shelves of glowing violet caps, luminous seed puffs on tall stalks, tufts of glowing mint blades
    // and small blue mushrooms at their feet. Each grove is one composed vignette with open ground between.
    private static void GlowGroves()
    {
        var group=Group("06 Glowing groves on the knolls",root);
        Material trunk=Mat("Assets/Materials/World/MAT_Realm_Root.mat");
        Material cap=Variant(nativePetals,"Glow_Cap",m=>{SetBase(m,new Color(0.52f,0.28f,0.95f));m.SetFloat("_Smoothness",0.6f);m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",new Color(0.45f,0.12f,0.85f));m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;});
        Material puff=Variant(nativePetals,"Glow_Puff",m=>{SetBase(m,new Color(0.85f,0.95f,1f));m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",new Color(0.55f,0.85f,1.1f));m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;});
        Material blade=Variant(nativePetals,"Glow_Blade",m=>{SetBase(m,new Color(0.35f,0.95f,0.8f));m.SetFloat("_Cull",0f);m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",new Color(0.12f,0.7f,0.55f));m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;});
        int groves=0;
        foreach(var knoll in LeafMapTerrain.KnollDesign)
        {
            Vector2 c=LeafMapTerrain.ToWorld(new Vector2(knoll.x,knoll.y));
            if(LeafMapTerrain.RouteDistance(c)<6f)c+=(c-LeafMapTerrain.ToWorld(new Vector2(0,10))).normalized*6f;
            var g=Group("Grove "+(++groves),group);
            var trunks=new RealmMeshes();var caps=new RealmMeshes();var puffs=new RealmMeshes();var threads=new RealmMeshes();var blades=new RealmMeshes();var shrooms=new RealmMeshes();
            // One or two curved trunks carrying shelves of caps.
            for(int k=0,n=random.Next(2,4);k<n;k++)
            {
                Vector3 foot=Ground(c+new Vector2(Range(-4,4),Range(-4,4)));float h=Range(7f,12f);Vector3 lean=new Vector3(Range(-1,1),0,Range(-1,1)).normalized*Range(1f,2.4f);
                var path=Enumerable.Range(0,9).Select(i=>{float t=i/8f;return foot+Vector3.up*t*h+lean*t*t;}).ToList();
                trunks.Tube(path,t=>Mathf.Lerp(0.75f,0.22f,t),8,0f,0.3f,Range(0,1));
                for(int i=2;i<9;i++)
                {
                    if(Range(0,1)<0.25f)continue;
                    float a=Range(0,6.28f);Vector3 o=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                    float rr=Range(0.8f,1.9f)*(1.25f-i/10f);
                    caps.Sphere(path[i]+o*(0.25f+rr*0.6f),new Vector3(rr,rr*0.28f,rr*0.85f),Quaternion.Euler(Range(-8,8),a*Mathf.Rad2Deg,0),5,12,0.5f,Range(0,1));
                }
            }
            // Luminous seed puffs: a glowing core with fine filaments on a slender stalk.
            for(int k=0,n=random.Next(7,12);k<n;k++)
            {
                Vector3 foot=Ground(c+new Vector2(Range(-7,7),Range(-7,7)));float h=Range(1.4f,3.6f);
                Vector3 top=foot+Vector3.up*h+new Vector3(Range(-0.3f,0.3f),0,Range(-0.3f,0.3f));
                threads.Tube(new[]{foot,Vector3.Lerp(foot,top,0.5f)+Vector3.right*0.1f,top},t=>0.03f,4,0f,0.4f,Range(0,1));
                float size=Range(0.6f,1.1f);puffs.Sphere(top,Vector3.one*size*0.35f,Quaternion.identity,6,10,0.5f,Range(0,1));
                for(int f=0;f<22;f++)
                {
                    Vector3 dir=Random3();if(dir.y<-0.3f)dir.y=-dir.y;
                    threads.Tube(new[]{top,top+dir*size*0.6f,top+dir*size},t=>0.008f,3,0f,0.2f,Range(0,1));
                    if(f%3==0)puffs.Sphere(top+dir*size,Vector3.one*0.035f,Quaternion.identity,3,5,0.5f,0.5f);
                }
            }
            // Tufts of glowing mint blades round the grove's edge.
            for(int k=0,n=random.Next(12,18);k<n;k++)
            {
                float a=Range(0,6.28f),rr=Range(2.5f,10f);Vector2 tuft=c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*rr;
                for(int b=0,m=random.Next(8,15);b<m;b++)
                {
                    Vector3 foot=Ground(tuft+new Vector2(Range(-0.7f,0.7f),Range(-0.7f,0.7f)));float h=Range(0.7f,2f);
                    Vector3 bend=new Vector3(Range(-0.3f,0.3f),0,Range(-0.3f,0.3f));
                    blades.Tube(new[]{foot,foot+Vector3.up*h*0.5f+bend*0.3f,foot+Vector3.up*h+bend},t=>Mathf.Lerp(0.07f,0.01f,t),4,0f,0.6f,Range(0,1));
                }
            }
            for(int k=0,n=random.Next(14,22);k<n;k++)RealmFlora.Mushroom(shrooms,Ground(c+new Vector2(Range(-6,6),Range(-6,6))),Vector3.up,Range(0.8f,2.4f),random);
            Part("Trunks",g,trunks,trunk);Shadowless(Part("Glowing cap shelves",g,caps,cap));Shadowless(Part("Seed puff cores",g,puffs,puff));
            Shadowless(Part("Seed puff filaments",g,threads,thread));Shadowless(Part("Glowing blades",g,blades,blade));Part("Blue mushrooms",g,shrooms,fungus);
            Lamp(g,Ground(c)+Vector3.up*3f,new Color(0.5f,0.35f,1f),6f,14f);
        }
        report.Add("Glowing groves: "+groves+" on the knolls.");
    }


    // ---------------- Field groves (Daniel 2026-11-09: the knoll groves' trunks, seed puffs, blades and mushrooms in the flower field too) ----------------

    private const string FieldGroveName="06b Glowing groves in the flower field";

    /// <summary>Batch or editor: adds (or rebuilds) only the field groves in Scene_LeafMap; nothing else of the pass is touched.</summary>
    public static void AddFieldGroves()
    {
        var scene=SceneManager.GetSceneByPath(LeafMapSetup.ScenePath);
        if(!scene.IsValid() || !scene.isLoaded)scene=EditorSceneManager.OpenScene(LeafMapSetup.ScenePath,OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        map=scene.GetRootGameObjects().First(g=>g.name.StartsWith("Leaf Map —")).transform;
        world=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ProceduralWorld>(true)).First();
        LeafMapTerrain.Scale=LeafMapTerrain.ReadScale(world.SculptData.Layout);
        if(!world.LayoutReady)world.Generate(true);
        Physics.SyncTransforms();
        root=map.Find(GroupName);
        if(root==null)throw new InvalidOperationException("Run the time pass first.");
        nativePetals=Mat("Assets/Materials/Creatures/BubbleFlowerPetals.mat");
        thread=Mat("Assets/Materials/World/MAT_Realm_Thread.mat");
        fungus=Mat("Assets/Materials/World/MAT_Realm_MushroomBlue.mat");
        foreach(string path in Directory.GetFiles(Folder,"FieldGrove_*.asset"))AssetDatabase.DeleteAsset(path.Replace('\\','/'));
        meshPrefix="FieldGrove_";serial=0;random=new System.Random(911);
        try{FieldGroves();}
        finally{meshPrefix="Mesh_";}
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    private static void FieldGroves()
    {
        var old=root.Find(FieldGroveName);if(old!=null)Object.DestroyImmediate(old.gameObject);
        var group=Group(FieldGroveName,root);
        Material trunk=Mat("Assets/Materials/World/MAT_Realm_Root.mat");
        Material cap=Variant(nativePetals,"Glow_Cap",m=>{SetBase(m,new Color(0.52f,0.28f,0.95f));m.SetFloat("_Smoothness",0.6f);m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",new Color(0.45f,0.12f,0.85f));m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;});
        Material puff=Variant(nativePetals,"Glow_Puff",m=>{SetBase(m,new Color(0.85f,0.95f,1f));m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",new Color(0.55f,0.85f,1.1f));m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;});
        Material blade=Variant(nativePetals,"Glow_Blade",m=>{SetBase(m,new Color(0.35f,0.95f,0.8f));m.SetFloat("_Cull",0f);m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",new Color(0.12f,0.7f,0.55f));m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;});
        // Small groves dotted through the field: open ground between them, off the walking routes, clear of the mother.
        var centres=new List<Vector2>();
        Vector2 mother=LeafMapTerrain.ToWorld(LeafMapTerrain.MotherDesign);
        for(int tries=0;tries<400 && centres.Count<9;tries++)
        {
            Vector2 d=new Vector2(Range(-40,34),Range(-90,-36));
            if(FieldMask(d)<0.2f)continue;
            Vector2 c=LeafMapTerrain.ToWorld(d);
            if(LeafMapTerrain.RouteDistance(c)<5f || (c-mother).magnitude<26f)continue;
            if(centres.Any(o=>(o-c).magnitude<20f))continue;
            centres.Add(c);
        }
        int index=0;
        foreach(Vector2 c in centres)
        {
            var g=Group("Field grove "+(++index),group);
            var trunks=new RealmMeshes();var caps=new RealmMeshes();var puffs=new RealmMeshes();var threads=new RealmMeshes();var blades=new RealmMeshes();var shrooms=new RealmMeshes();
            // Every other grove has a trunk with cap shelves; the rest are puffs, blades and mushrooms only.
            int trunkCount=index%2==1?random.Next(1,3):0;
            for(int k=0;k<trunkCount;k++)
            {
                Vector3 foot=Ground(c+new Vector2(Range(-3,3),Range(-3,3)));float h=Range(5f,9f);Vector3 lean=new Vector3(Range(-1,1),0,Range(-1,1)).normalized*Range(0.8f,2f);
                var path=Enumerable.Range(0,9).Select(i=>{float t=i/8f;return foot+Vector3.up*t*h+lean*t*t;}).ToList();
                trunks.Tube(path,t=>Mathf.Lerp(0.6f,0.18f,t),8,0f,0.3f,Range(0,1));
                for(int i=2;i<9;i++)
                {
                    if(Range(0,1)<0.3f)continue;
                    float a=Range(0,6.28f);Vector3 o=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                    float rr=Range(0.6f,1.5f)*(1.25f-i/10f);
                    caps.Sphere(path[i]+o*(0.2f+rr*0.6f),new Vector3(rr,rr*0.28f,rr*0.85f),Quaternion.Euler(Range(-8,8),a*Mathf.Rad2Deg,0),5,12,0.5f,Range(0,1));
                }
            }
            for(int k=0,n=random.Next(4,8);k<n;k++)
            {
                Vector3 foot=Ground(c+new Vector2(Range(-6,6),Range(-6,6)));float h=Range(1.2f,3f);
                Vector3 top=foot+Vector3.up*h+new Vector3(Range(-0.3f,0.3f),0,Range(-0.3f,0.3f));
                threads.Tube(new[]{foot,Vector3.Lerp(foot,top,0.5f)+Vector3.right*0.1f,top},t=>0.03f,4,0f,0.4f,Range(0,1));
                float size=Range(0.5f,1f);puffs.Sphere(top,Vector3.one*size*0.35f,Quaternion.identity,6,10,0.5f,Range(0,1));
                for(int f=0;f<18;f++)
                {
                    Vector3 dir=Random3();if(dir.y<-0.3f)dir.y=-dir.y;
                    threads.Tube(new[]{top,top+dir*size*0.6f,top+dir*size},t=>0.008f,3,0f,0.2f,Range(0,1));
                    if(f%3==0)puffs.Sphere(top+dir*size,Vector3.one*0.035f,Quaternion.identity,3,5,0.5f,0.5f);
                }
            }
            for(int k=0,n=random.Next(6,11);k<n;k++)
            {
                float a=Range(0,6.28f),rr=Range(1.5f,8f);Vector2 tuft=c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*rr;
                for(int b=0,m=random.Next(6,12);b<m;b++)
                {
                    Vector3 foot=Ground(tuft+new Vector2(Range(-0.6f,0.6f),Range(-0.6f,0.6f)));float h=Range(0.6f,1.7f);
                    Vector3 bend=new Vector3(Range(-0.3f,0.3f),0,Range(-0.3f,0.3f));
                    blades.Tube(new[]{foot,foot+Vector3.up*h*0.5f+bend*0.3f,foot+Vector3.up*h+bend},t=>Mathf.Lerp(0.07f,0.01f,t),4,0f,0.6f,Range(0,1));
                }
            }
            for(int k=0,n=random.Next(8,14);k<n;k++)RealmFlora.Mushroom(shrooms,Ground(c+new Vector2(Range(-5,5),Range(-5,5))),Vector3.up,Range(0.7f,2f),random);
            if(trunkCount>0){Part("Trunks",g,trunks,trunk);Shadowless(Part("Glowing cap shelves",g,caps,cap));}
            Shadowless(Part("Seed puff cores",g,puffs,puff));Shadowless(Part("Seed puff filaments",g,threads,thread));
            Shadowless(Part("Glowing blades",g,blades,blade));Part("Blue mushrooms",g,shrooms,fungus);
        }
        Debug.Log("[FieldGroves] "+centres.Count+" groves in the flower field.");
    }

    // ---------------- Bubbles, fungi, ground tide ----------------

    private static void Bubbles()
    {
        var group=Group("05 Iridescent dew over the flowers",root);
        // The rising columns are now the solid bubble hops (BubbleHops); only dew stays here.
        // Dew on the field: small clusters of beads resting on the flower heads.
        var dew=new RealmMeshes();
        for(int c=0;c<10;c++)
        {
            Vector2 d=new Vector2(Range(-36,32),Range(-86,-40));if(FieldMask(d)<0.15f)continue;
            Vector3 at=Ground(LeafMapTerrain.ToWorld(d))+Vector3.up*0.75f;
            for(int i=0,n=random.Next(3,8);i<n;i++)dew.Sphere(at+new Vector3(Range(-0.7f,0.7f),Range(0,0.4f),Range(-0.7f,0.7f)),Vector3.one*Range(0.08f,0.2f),Quaternion.identity,6,8,0.5f,Range(0,1));
        }
        Shadowless(Part("Dew beads over the field",group,dew,bubble));
    }

    // A slow pool of time lying on the ground round the mother and the arch, bleeding into the flowers.
    // Daniel (2026-11-09): no time ripples spreading over the ground. On = build them again.
    private static bool groundTide = false;
    private static void GroundTide()
    {
        if(!groundTide)return;
        var group=Group("07 Ground tide — rings flowing out from the mother",root);
        Vector3 mother=LeafMapTerrain.MotherSpot;
        Disc(group,"Time tide round the mother",new Vector2(mother.x,mother.z),24f);

    }
    private static void Disc(Transform group,string name,Vector2 centre,float radius)
    {
        const int rings=36,round=90;var mesh=new RealmMeshes();
        for(int j=0;j<=rings;j++)for(int i=0;i<=round;i++)
        {
            float r=j/(float)rings,a=i/(float)round*Mathf.PI*2f;
            Vector2 p=centre+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r*radius;
            mesh.Vertex(Ground(p)+Vector3.up*0.07f,Vector3.up,new Vector2(i/(float)round,r),Vector2.zero);
        }
        for(int j=0;j<rings;j++)for(int i=0;i<round;i++){int a=j*(round+1)+i,b=a+round+1;mesh.Triangle(a,b,a+1);mesh.Triangle(a+1,b,b+1);}
        Shadowless(Part(name,group,mesh,pool));
    }

    // ---------------- Sky, birds and light ----------------

    private static void SkyAndBirds()
    {
        var group=Group("08 Sky — birds over the bones, threads to the veils",root);
        Vector3 mother=LeafMapTerrain.MotherSpot;
        var prefab=AssetDatabase.LoadAssetAtPath<BirdCreature>("Assets/Prefabs/Creatures/BirdCreature.prefab");
        if(prefab!=null)
        {
            var area=Group("Birds circling the mother",group).gameObject.AddComponent<BirdSpawnArea>();
            area.transform.position=mother+new Vector3(-8,24,-4);area.Configure(prefab,6,16,1102);
            var arch=Group("Birds over the flower field",group).gameObject.AddComponent<BirdSpawnArea>();
            arch.transform.position=Ground(LeafMapTerrain.ToWorld(new Vector2(-6,-62)))+Vector3.up*20;arch.Configure(prefab,5,20,2211);
        }
        foreach(var flock in map.GetComponentsInChildren<BirdFlockManager>(true))
        {
            Vector2 c=LeafMapTerrain.ToWorld(new Vector2(0,-20));
            flock.SkyBounds=new Bounds(new Vector3(c.x,34,c.y),new Vector3(220*LeafMapTerrain.Scale,44,240*LeafMapTerrain.Scale));EditorUtility.SetDirty(flock);
        }
        // A sky spider lives in the arch; it settles once the terrain under it has loaded.
        var spider=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Creatures/SkySpider.prefab"),group);
        if(spider!=null)
        {
            spider.name="Sky spider — ribcage arch";spider.transform.position=Ground(LeafMapTerrain.ToWorld(new Vector2(13,-60)))+Vector3.up*1.5f;
            if(spider.GetComponent<SettleWhenGroundReady>()==null)spider.AddComponent<SettleWhenGroundReady>();
            var creature=spider.GetComponent<Creature>();
            if(creature!=null){var s=new SerializedObject(creature);var age=s.FindProperty("ageRate");if(age!=null){age.floatValue=0;s.ApplyModifiedPropertiesWithoutUndo();}}
        }
    }

    private static void Lights()
    {
        var group=Group("09 Cold light — shafts onto the bones",root);
        Vector3 mother=LeafMapTerrain.MotherSpot;Vector3 arch=Ground(LeafMapTerrain.ToWorld(new Vector2(15,-63)));
        foreach(var (at,height,radius) in new[]{(mother+new Vector3(-3,0,-2),48f,3.6f),(arch,40f,2.8f)})
        {
            var cone=new RealmMeshes();int first=cone.VertexCount;
            for(int i=0;i<=8;i++)for(int j=0;j<=18;j++)
            {
                float t=i/8f,a=j/18f*Mathf.PI*2;Vector3 n=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                cone.Vertex(at+Vector3.up*t*height+n*Mathf.Lerp(radius,radius*1.9f,t)+new Vector3(t*height*0.18f,0,t*height*0.08f),n,new Vector2(j/18f,1-t),Vector2.zero);
            }
            for(int i=0;i<8;i++)for(int j=0;j<18;j++){int a=first+i*19+j;cone.Triangle(a,a+19,a+1);cone.Triangle(a+1,a+19,a+20);}
            Shadowless(Part("Cold shaft of light",group,cone,shaft)).SetActive(false);
        }
        Lamp(group,mother+new Vector3(-6,5,-5),new Color(0.32f,0.62f,1f),10,24);
        Lamp(group,arch+Vector3.up*4,new Color(0.45f,0.72f,1f),7,16);
        Lamp(group,Ground(LeafMapTerrain.ToWorld(new Vector2(-21,-69)))+Vector3.up*3,new Color(0.55f,0.5f,1f),8,13);
    }
    private static void Lamp(Transform group,Vector3 at,Color color,float intensity,float range)
    {
        var light=Group("Cold time light",group).gameObject.AddComponent<Light>();light.transform.position=at;
        light.type=LightType.Point;light.color=color;light.intensity=intensity;light.range=range;light.shadows=LightShadows.None;
    }

    private static void Views()
    {
        var views=map.Find("08 Review cameras — disabled during play");
        var template=views.Find("04_FlowerMeadow").GetComponent<Camera>();
        Vector3 mother=LeafMapTerrain.MotherSpot;
        // Each view stands a set height above the real ground, looking at a point above the ground.
        // The cave mouth looks out from the highland over the basin.
        Vector3 exit=YoYoTutorialFallFlow.ExitRoute[YoYoTutorialFallFlow.ExitRoute.Length-1]+LeafMapTutorialGraft.Offset;
        View("03_CaveExit",exit+new Vector3(0,2.4f,1.5f),Above(new Vector2(4,-58),2f),70);
        View("11_OriginalTutorial",new Vector3(0,-17,-198)+Vector3.up*LeafMapTerrain.CaveLift,new Vector3(-6,-16,-177)+Vector3.up*LeafMapTerrain.CaveLift,64);
        View("13_TimeField",Above(new Vector2(-4,-90),7f),Above(new Vector2(16,-52),3f),66);
        if(LeafMapTerrain.HasRift)
        {
            View("06_BonesAndRift",Above(new Vector2(14,-62),26f),LeafMapTerrain.RiftPoint(0.35f)+Vector3.down*6,62);
            View("09_RiftInterior",LeafMapTerrain.RiftPoint(0.2f)+Vector3.up*(LeafMapTerrain.MotherSpot.y+12f),FloorAt(0.62f)+Vector3.up*8,70);
        }
        else
        {
            View("06_BonesAndRift",mother+new Vector3(-16f,14f,-16f),mother+Vector3.down*2f,62);
            Vector3 below=Ground(Flat(world.SinkholeCentre(MotherHole(),-40f,out _)));
            View("09_RiftInterior",below+new Vector3(14f,4f,10f),below+Vector3.up*16f,70);
        }
        var lily=map.GetComponentsInChildren<BubbleFlower>().FirstOrDefault(f=>f.name.StartsWith("Cascade bubble flower"));
        if(lily!=null){Vector3 f=lily.transform.position;View("20_BubbleLily",f+new Vector3(2.2f,1.7f,-1.6f),f+Vector3.up*1.0f,55);}
        Vector3 knoll=Above(new Vector2(45,85),0f);
        View("21_GlowGrove",knoll+new Vector3(-13f,5f,-14f),knoll+Vector3.up*4f,62);
        View("19_BubbleHops",Above(new Vector2(-6,-76),8f),Above(new Vector2(-40,-44),18f),68);
        View("18_CliffAboveTheBasin",Above(new Vector2(-6,-64),4f),Above(new Vector2(0,-90),14f),64);
        View("14_MotherAndField",Above(new Vector2(-6,-58),6f),mother+Vector3.up*5,60);
        View("15_SkyVeils",Above(new Vector2(-58,-92),42f),Above(new Vector2(12,-50),6f),58);
        View("16_UnderTheArch",Above(new Vector2(6,-75),2.2f),Above(new Vector2(22,-54),5f),70);
        View("17_FieldFromAbove",Above(new Vector2(-30,-100),70f),Above(new Vector2(4,-58),0f),55);
        var top=views.Find("01_Layout");if(top!=null){Vector2 c=LeafMapTerrain.ToWorld(new Vector2(0,-23));top.position=new Vector3(c.x,320,c.y);top.GetComponent<Camera>().orthographicSize=205*LeafMapTerrain.Scale;}
        var overview=views.Find("02_Overview");
        if(overview!=null){overview.position=LeafMapTerrain.ToWorld(new Vector3(155,175,-221));overview.LookAt(LeafMapTerrain.ToWorld(new Vector3(0,0,-26)));overview.GetComponent<Camera>().farClipPlane=1200;}
        Vector3 Above(Vector2 design,float lift)=>Ground(LeafMapTerrain.ToWorld(design))+Vector3.up*lift;
        void View(string name,Vector3 at,Vector3 target,float fov)
        {
            var t=views.Find(name);
            if(t==null){t=Group(name,views);EditorUtility.CopySerialized(template,t.gameObject.AddComponent<Camera>());}
            t.SetPositionAndRotation(at,Quaternion.LookRotation(target-at));
            var camera=t.GetComponent<Camera>();camera.enabled=false;camera.fieldOfView=fov;camera.farClipPlane=900;
        }
    }

    private static void Validate()
    {
        Physics.SyncTransforms();
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>())foreach(var v in filter.sharedMesh.vertices)
            if(!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z))throw new InvalidOperationException("Non-finite time-pass mesh: "+filter.name);
        foreach(var p in LeafMapTerrain.MainRoute)
            if(!Physics.Raycast(new Vector3(p.x,LeafMapTerrain.Height(p.x,p.z)+1.7f,p.z),Vector3.down,6,1<<7))
                report.Add("WARNING: no ground found close to the main route at "+p);
        var player=map.gameObject.scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlayerCreatureCarrier>(true)).FirstOrDefault();
        if(player!=null)report.Add("Player start: "+player.transform.root.position);
        report.Add("Validation: finite meshes PASS");
    }

    // ---------------- Highland, rift and settling after the terrain changed ----------------

    // Ground creatures follow the reshaped ground; anything the new rift opened under is set beside it.
    private static void Resettle()
    {
        int moved=0;
        foreach(var creature in map.GetComponentsInChildren<Creature>())
        {
            var t=creature.transform;
            if(t.parent!=null && t.parent.GetComponentInParent<Creature>()!=null)continue;
            if(t.GetComponentsInParent<Transform>(true).Any(p=>Untouched.Contains(p.name)))continue;
            if(creature is BirdCreature || creature is TrappingBubble || creature is FireBubble || creature is LeashBubble || creature is SkySpider
                || creature is FireSource || creature is FireFlower || t.GetComponentInParent<BoneKiln>()!=null)continue;
            Vector2 w=Flat(t.position);
            float inside=LeafMapTerrain.RiftDistance(w,out float rt),edge=LeafMapTerrain.RiftHalfWidth(rt)+3f;
            if(inside<edge)
            {
                Vector3 side=LeafMapTerrain.RiftSide(rt),centre=LeafMapTerrain.RiftPoint(rt);
                float sign=Mathf.Sign(Vector3.Dot(t.position-centre,side));
                w=Flat(centre+side*sign*(edge+0.5f));
            }
            var renderers=t.GetComponentsInChildren<Renderer>();
            float bottom=renderers.Length>0?renderers.Min(r=>r.bounds.min.y):t.position.y;
            Vector3 ground=Ground(w);
            float gap=bottom-ground.y;
            if(inside>=edge && (Mathf.Abs(gap)<0.2f || gap>3f))continue;
            t.position=new Vector3(w.x,t.position.y,w.y);
            SquirrelTree.PlaceOnGround(t,ground+Vector3.up*0.03f);Record(t);moved++;
        }
        // Merged decoration pieces of the earlier passes drop or rise with the ground under them.
        int pieces=0;
        foreach(var filter in map.GetComponentsInChildren<MeshFilter>())
        {
            var t=filter.transform;
            if(t.position.sqrMagnitude>1e-4f || t.childCount>0 || t.GetComponentsInParent<Transform>(true).Any(p=>Untouched.Contains(p.name)))continue;
            pieces+=Settle(filter);
        }
        report.Add("Resettled after reshaping: "+moved+" creatures, "+pieces+" decoration pieces.");
    }
    private static int Settle(MeshFilter filter)
    {
        Mesh mesh=filter.sharedMesh;
        if(mesh==null || !mesh.isReadable)return 0;
        var vertices=mesh.vertices;var roots=Pieces(vertices,mesh.triangles,out var bounds);
        var lift=new Dictionary<int,float>();
        var ribbons=new HashSet<int>();
        foreach(var pair in bounds)
        {
            var b=pair.Value;
            if(Mathf.Max(b.size.x,b.size.z)>12f)
            {
                // A long piece lying on the ground (a path or stream) is laid on the new ground vertex by vertex.
                var offsets=new List<float>();
                for(int i=0;i<vertices.Length;i+=7)if(roots[i]==pair.Key)offsets.Add(Mathf.Abs(vertices[i].y-Ground(Flat(vertices[i])).y));
                offsets.Sort();
                if(offsets.Count>0 && offsets[offsets.Count/2]<0.5f && offsets[offsets.Count-1]>0.3f)ribbons.Add(pair.Key);
                continue;
            }
            float gap=Ground(new Vector2(b.center.x,b.center.z)).y-b.min.y;
            if(gap>-0.6f && gap<1.2f)continue;
            lift[pair.Key]=gap+0.02f;
        }
        if(lift.Count==0 && ribbons.Count==0)return 0;
        for(int i=0;i<vertices.Length;i++)
        {
            if(lift.TryGetValue(roots[i],out float y))vertices[i].y+=y;
            else if(ribbons.Contains(roots[i]))vertices[i].y=Ground(Flat(vertices[i])).y+0.06f;
        }
        var copy=Object.Instantiate(mesh);copy.name=mesh.name;copy.vertices=vertices;copy.RecalculateBounds();
        AssetDatabase.CreateAsset(copy,AssetDatabase.GenerateUniqueAssetPath(MovedFolder+"/Settled.asset"));
        filter.sharedMesh=copy;var collider=filter.GetComponent<MeshCollider>();if(collider!=null)collider.sharedMesh=copy;
        return lift.Count+ribbons.Count;
    }
    // Connected pieces of a merged mesh (shared indices or touching positions): root per vertex, bounds per root.
    private static int[] Pieces(Vector3[] vertices,int[] triangles,out Dictionary<int,Bounds> bounds)
    {
        int n=vertices.Length;var parent=Enumerable.Range(0,n).ToArray();
        int FindRoot(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
        void Join(int a,int b){a=FindRoot(a);b=FindRoot(b);if(a!=b)parent[a]=b;}
        for(int i=0;i<triangles.Length;i+=3){Join(triangles[i],triangles[i+1]);Join(triangles[i],triangles[i+2]);}
        var weld=new Dictionary<Vector3Int,int>();
        for(int i=0;i<n;i++){var key=Vector3Int.RoundToInt(vertices[i]*200f);if(weld.TryGetValue(key,out int other))Join(i,other);else weld[key]=i;}
        var roots=new int[n];bounds=new Dictionary<int,Bounds>();
        for(int i=0;i<n;i++)
        {
            int r=roots[i]=FindRoot(i);
            if(bounds.TryGetValue(r,out var b)){b.Encapsulate(vertices[i]);bounds[r]=b;}else bounds[r]=new Bounds(vertices[i],Vector3.zero);
        }
        return roots;
    }

    // The bubble flower's seeds carry a small iridescent shell so they can be found in the flowers.
    private static void SeedGlow()
    {
        const string path="Assets/Generated/LeafMap/BubbleFlowerSeed.prefab";
        var prefab=PrefabUtility.LoadPrefabContents(path);
        try
        {
            if(prefab.transform.Find("Seed glow shell")!=null)return;
            var renderers=prefab.GetComponentsInChildren<Renderer>();
            var b=renderers[0].bounds;foreach(var r in renderers)b.Encapsulate(r.bounds);
            string meshPath=Folder+"/SeedShell.asset";var shell=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(shell==null){var m=new RealmMeshes();m.Sphere(Vector3.zero,Vector3.one,Quaternion.identity,12,18,0.5f,0.3f);shell=m.Build("Seed shell");AssetDatabase.CreateAsset(shell,meshPath);}
            var go=new GameObject("Seed glow shell",typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(prefab.transform,false);
            go.transform.position=b.center;go.transform.localScale=Vector3.one*Mathf.Max(0.22f,b.extents.magnitude*1.5f);
            go.GetComponent<MeshFilter>().sharedMesh=shell;var renderer=go.GetComponent<MeshRenderer>();renderer.sharedMaterial=bubble;renderer.shadowCastingMode=ShadowCastingMode.Off;
            PrefabUtility.SaveAsPrefabAsset(prefab,path);report.Add("Seeds: iridescent shell added to the seed prefab.");
        }
        finally {PrefabUtility.UnloadPrefabContents(prefab);}
    }

    // The fire rift opens out of the mother's crack and runs east, cut through the crust into the inner
    // world. Rebuild its walls, floor and crossings along it.
    private static void Rift()
    {
        foreach(var t in map.GetComponentsInChildren<Transform>(true))
            if(t.name=="04 Rift — broken strata and rising heat" || t.name.StartsWith("Rift floating stepping stone") || t.name.StartsWith("Canyon recovery ledge"))
                if(!t.IsChildOf(root))t.gameObject.SetActive(false);
        var group=Group("11 The mother's crack — one rift down to the inner world",root);
        Material rock=Mat("Assets/Generated/LeafMap/Leaf_InkStone.mat");
        float Rim(float t){float h=LeafMapTerrain.RiftHalfWidth(t)+5f;Vector3 c=LeafMapTerrain.RiftPoint(t),side=LeafMapTerrain.RiftSide(t);return Mathf.Min(Ground(Flat(c+side*h)).y,Ground(Flat(c-side*h)).y);}
        // Strata jut from both walls of the crust, between the rim and the inner world's ceiling.
        for(int i=0;i<11;i++)for(int side=-1;side<=1;side+=2)for(int layer=0;layer<3;layer++)
        {
            float t=0.2f+i*0.072f+Range(-0.02f,0.02f);Vector3 size=new Vector3(Range(2.8f,5f),Range(1.8f,3.6f),Range(3f,6f));
            Vector3 at=LeafMapTerrain.RiftPoint(t)+LeafMapTerrain.RiftSide(t)*side*(LeafMapTerrain.RiftHalfWidth(t)+size.x*0.25f);
            at.y=Mathf.Lerp(-26f,Rim(t)-3f,(layer+Range(0.1f,0.8f))/3f);
            if(Range(0,1)<0.3f)continue;
            Boulder(group,at,size,rock,"Projecting wall stratum");
        }
        // Rock fallen through to the inner floor, gathered where the crack is deepest.
        for(int i=0;i<46;i++)
        {
            float t=Mathf.Lerp(0.3f,0.92f,Mathf.Pow(Range(0,1),0.8f));float r=Range(0.5f,2.2f);
            Vector3 at=LeafMapTerrain.RiftPoint(t)+LeafMapTerrain.RiftSide(t)*Range(-0.8f,0.8f)*LeafMapTerrain.RiftHalfWidth(t);
            at.y=Ground(Flat(at)).y+r*0.2f;
            Boulder(group,at,new Vector3(r,r*Range(0.5f,1f),r*Range(0.7f,1.4f)),rock,"Fallen rock on the inner floor");
        }
        var vein=new RealmMeshes();var rows=new List<Vector3[]>();
        for(int i=0;i<50;i++)
        {
            float t=Mathf.Lerp(0.3f,0.95f,i/49f);Vector3 side=LeafMapTerrain.RiftSide(t);Vector3 p=LeafMapTerrain.RiftPoint(t)+side*Mathf.Sin(t*22f)*3f;
            var row=new Vector3[5];for(int k=0;k<5;k++){Vector3 q=p+side*(k/4f*2-1)*1f;q.y=Ground(Flat(q)).y+0.08f;row[k]=q;}rows.Add(row);
        }
        vein.Grid(rows,0.6f);Shadowless(Part("Molten vein on the inner floor",group,vein,Mat("Assets/Materials/HabitatBlock/HB_MoltenVein.mat")));
        var crystals=new RealmMeshes();
        for(int i=0;i<18;i++)
        {
            float t=Range(0.25f,0.9f);Vector3 at=LeafMapTerrain.RiftPoint(t)+LeafMapTerrain.RiftSide(t)*(i%2==0?-1:1)*(LeafMapTerrain.RiftHalfWidth(t)-0.6f);
            at.y=Mathf.Lerp(-24f,Rim(t)-4f,Range(0,1));
            crystals.Crystal(at,Quaternion.Euler(Range(-35,35),Range(0,360),Range(-30,30)),Range(0.5f,1.5f),Range(0.14f,0.38f),5,Range(0,1),random);
        }
        Part("Fluorite in the rift walls",group,crystals,Mat("Assets/Materials/HabitatBlock/HB_Crystal_Fluorite.mat"));
        // A floating crossing over the middle, and a ledge stair from the inner floor back up one wall.
        const float cross=0.58f;Vector3 middle=LeafMapTerrain.RiftPoint(cross),across=LeafMapTerrain.RiftSide(cross),along=Vector3.Cross(across,Vector3.up);
        float half=LeafMapTerrain.RiftHalfWidth(cross),rim=Rim(cross);
        for(int k=0;k<5;k++)
        {
            Vector3 p=middle+across*Mathf.Lerp(-half+3f,half-3f,k/4f)+along*Range(-1.5f,1.5f);p.y=rim+Range(0.5f,2.5f);
            Boulder(group,p,new Vector3(2.8f,1.1f,2.4f),rock,"Rift floating stepping stone "+(k+1));
        }
        for(int i=0;i<12;i++)
        {
            float t=0.42f+i*0.035f;float floor=Ground(Flat(LeafMapTerrain.RiftPoint(t))).y;
            Vector3 p=LeafMapTerrain.RiftPoint(t)+LeafMapTerrain.RiftSide(t)*(i%2==0?1:-1)*(LeafMapTerrain.RiftHalfWidth(t)-2.2f);
            p.y=Mathf.Lerp(floor+5f,Rim(t)-2f,i/11f);
            Boulder(group,p,new Vector3(3.6f,1f,2.8f),rock,"Canyon recovery ledge "+i);
        }
        // The vent, its fire flower and the first rising bubbles move to the inner floor under the rift.
        var fire=map.Find("06 Fire rift — rising fuel and cliff traversal");
        if(fire!=null)
        {
            foreach(var f in fire.GetComponentsInChildren<FireFlower>(true)){f.transform.position=FloorAt(0.5f)+Vector3.up*0.15f;Record(f.transform);}
            foreach(var f in fire.GetComponentsInChildren<FireSource>(true)){f.transform.position=FloorAt(0.53f)+Vector3.up*1f;Record(f.transform);}
            foreach(var light in fire.GetComponentsInChildren<Light>(true))light.transform.position=FloorAt(0.52f)+Vector3.up*6;
        }
        var rising=map.Find("Rising fire — initial catchable bubbles");
        if(rising!=null)for(int i=0;i<rising.childCount;i++){Vector3 p=FloorAt(0.45f+i*0.15f);p.y=-30+i*12;rising.GetChild(i).position=p;Record(rising.GetChild(i));}
        foreach(float t in new[]{0.35f,0.65f,0.9f})Lamp(group,FloorAt(t)+Vector3.up*14,new Color(0.3f,0.58f,0.9f),16,30);
        report.Add("Rift: opens east from the mother into the inner world, "+(half*2).ToString("0")+" m wide at the crossing.");
    }
    // The mother's crack as in Scene_Daniel: the round sinkhole shaft she dives into, neck down through the
    // crust into the inner world. The old rift dressing is switched off; the fire vent, its flower and the
    // first rising bubbles move to a small hollow east of her, where the rift used to open.
    private static void Shaft()
    {
        foreach(var t in map.GetComponentsInChildren<Transform>(true))
            if(t.name=="04 Rift — broken strata and rising heat" || t.name.StartsWith("Rift floating stepping stone") || t.name.StartsWith("Canyon recovery ledge"))
                if(!t.IsChildOf(root))t.gameObject.SetActive(false);
        var group=Group("11 The mother's shaft — down to the inner world",root);
        int hole=MotherHole();
        Vector3 mouth=world.SinkholeCentre(hole,LeafMapTerrain.MotherSpot.y,out float radius);
        // A few fluorite crystals in the shaft wall and a cool lamp at its foot read the depth from above.
        var crystals=new RealmMeshes();
        for(int i=0;i<10;i++)
        {
            float y=Mathf.Lerp(mouth.y-4f,-30f,Range(0,1));Vector3 c=world.SinkholeCentre(hole,y,out _);
            float a=Range(0,6.28f);Vector3 at=c+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*(radius*1.05f);
            crystals.Crystal(at,Quaternion.Euler(Range(-35,35),Range(0,360),Range(-30,30)),Range(0.4f,1.1f),Range(0.12f,0.3f),5,Range(0,1),random);
        }
        Part("Fluorite in the shaft wall",group,crystals,Mat("Assets/Materials/HabitatBlock/HB_Crystal_Fluorite.mat"));
        Vector3 foot=Ground(Flat(world.SinkholeCentre(hole,-40f,out _)));
        Lamp(group,foot+Vector3.up*6,new Color(0.3f,0.58f,0.9f),12,26);
        Vector3 vent=LeafMapTerrain.VentSpot;
        var fire=map.Find("06 Fire rift — rising fuel and cliff traversal");
        if(fire!=null)
        {
            foreach(var f in fire.GetComponentsInChildren<FireFlower>(true)){f.transform.position=Ground(Flat(vent))+Vector3.up*0.15f;Record(f.transform);}
            foreach(var f in fire.GetComponentsInChildren<FireSource>(true)){f.transform.position=Ground(Flat(vent+new Vector3(2.2f,0,1.2f)))+Vector3.up*1f;Record(f.transform);}
            foreach(var light in fire.GetComponentsInChildren<Light>(true))light.transform.position=vent+Vector3.up*5;
        }
        var rising=map.Find("Rising fire — initial catchable bubbles");
        if(rising!=null)for(int i=0;i<rising.childCount;i++){Vector3 p=vent+new Vector3(Range(-2,2),4+i*5,Range(-2,2));rising.GetChild(i).position=p;Record(rising.GetChild(i));}
        report.Add("Shaft: the mother's sinkhole (radius "+radius.ToString("0.0")+" m) opens to the inner world; the fire vent moved east of her.");
    }
    // The sinkhole nearest the mother (the one she dives into).
    private static int MotherHole()
    {
        Vector3 m=LeafMapTerrain.MotherSpot;int best=0;float bestDistance=float.MaxValue;
        for(int i=0;i<world.SinkholeCount;i++)
        {
            Vector3 c=world.SinkholeCentre(i,m.y,out _);float d=new Vector2(c.x-m.x,c.z-m.z).sqrMagnitude;
            if(d<bestDistance){bestDistance=d;best=i;}
        }
        return best;
    }
    // The real floor under the rift's centre line (the inner world's ground where it is cut through).
    private static Vector3 FloorAt(float t)=>Ground(Flat(LeafMapTerrain.RiftPoint(t)));
    private static void Boulder(Transform parent,Vector3 at,Vector3 size,Material material,string name)
    {
        var mesh=new RealmMeshes();mesh.Sphere(Vector3.zero,size,Quaternion.Euler(Range(-8,8),Range(0,360),Range(-8,8)),5,8,0.5f,Range(0,1));
        var go=Part(name,parent,mesh,material);go.transform.position=at;Solid(go);
    }

    // ---------------- Bubble hops and atmosphere ----------------

    // ---------------- Bubble lily: the interactive bubble flower's own look ----------------

    // The interactive bubble flowers get a look of their own, so they stand out of the decorative carpet that
    // borrows the plain flower model: a taller curved teal stem, a cup of pearl petals that glow faintly, and
    // a small iridescent bubble already forming in the cup (where the bubbles now leave). The mesh is
    // generated once into an asset with three parts (stem, petals, bud); at runtime it is one ordinary static
    // renderer per flower, fewer than the seven primitives it replaces.
    private const float LilyHeight=0.5f;
    private static Material lilyStem,lilyInner,lilyBud;
    private static Material[] lilyOuter;
    private static Mesh lilyField;
    private static Material[] lilyFieldTones;
    // One instanced material per tone: base colour and glow come from two baked 4 x 1 strips.
    private static Material FieldToneMaterial(int tone)
    {
        Color Base(Material m)=>m.GetColor("_BaseColor");Color Glow(Material m)=>m.IsKeywordEnabled("_EMISSION")?m.GetColor("_EmissionColor"):Color.black;
        Texture2D Strip(string name,Color[] colours)
        {
            string path=Folder+"/"+name+".asset";var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(texture==null){texture=new Texture2D(4,1,TextureFormat.RGBA32,false){name=name};AssetDatabase.CreateAsset(texture,path);}
            texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;texture.SetPixels(colours);texture.Apply();EditorUtility.SetDirty(texture);return texture;
        }
        var albedo=Strip("LilyFieldColour_"+tone,new[]{Base(lilyStem),Base(lilyOuter[tone]),Base(lilyInner),Base(lilyBud)});
        var glow=Strip("LilyFieldGlow_"+tone,new[]{Glow(lilyStem),Glow(lilyOuter[tone]),Glow(lilyInner),Glow(lilyBud)});
        return Variant(lilyOuter[tone],"LilyField_Tone"+tone,m=>{
            SetBase(m,Color.white);m.SetTexture("_BaseMap",albedo);m.SetTexture("_MainTex",albedo);
            m.EnableKeyword("_EMISSION");m.SetTexture("_EmissionMap",glow);m.SetColor("_EmissionColor",Color.white);m.enableInstancing=true;m.SetFloat("_Cull",0f);
        });
    }
    private static void BubbleLily()
    {
        Mesh lily=LilyMesh("BubbleLily_v4",1f);lilyField=LilyMesh("BubbleLily_Field_v2",0f);
        Material Petal(string name,Color colour,Color glow)=>Variant(nativePetals,name,m=>{
            SetBase(m,colour);m.SetFloat("_Smoothness",0.78f);m.SetFloat("_Cull",0f);m.enableInstancing=true;
            m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",glow);m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;
        });
        // The outer petals carry the field's flow from purple to ice blue; the inner layer is a pale glow.
        lilyOuter=new[]{
            Petal("BubbleLily_Petal_Purple",new Color(0.66f,0.38f,1f),new Color(0.3f,0.08f,0.46f)),
            Petal("BubbleLily_Petal_Violet",new Color(0.6f,0.6f,1f),new Color(0.16f,0.18f,0.48f)),
            Petal("BubbleLily_Petal",new Color(0.86f,0.9f,1f),new Color(0.3f,0.52f,0.85f))};
        lilyInner=Petal("BubbleLily_Inner",new Color(0.96f,0.97f,1f),new Color(0.45f,0.58f,0.82f));
        lilyStem=Variant(nativeStems,"BubbleLily_Stem",m=>{SetBase(m,new Color(0.1f,0.34f,0.38f));m.SetFloat("_Smoothness",0.45f);m.enableInstancing=true;});
        lilyBud=Variant(nativePetals,"BubbleLily_Bud",m=>{
            SetBase(m,new Color(0.9f,0.95f,1f));m.SetFloat("_Smoothness",0.95f);m.enableInstancing=true;
            m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",new Color(0.42f,0.3f,0.55f));m.globalIlluminationFlags=MaterialGlobalIlluminationFlags.None;
        });
        lilyFieldTones=Enumerable.Range(0,lilyOuter.Length).Select(k=>FieldToneMaterial(k)).ToArray();
        // Real flowers keep the iridescent bubble in the cup; the instanced field uses the lit bud.
        var materials=new[]{lilyStem,lilyOuter[2],lilyInner,bubble};
        // The leaf's own flower prefab (planted and cascade flowers) ...
        const string path="Assets/Generated/LeafMap/MeadowFlower.prefab";
        var prefab=PrefabUtility.LoadPrefabContents(path);
        try {Dress(prefab,lily,materials,false);PrefabUtility.SaveAsPrefabAsset(prefab,path);}
        finally {PrefabUtility.UnloadPrefabContents(prefab);}
        // ... and the scene's flowers that are instances of the shared Creatures prefab.
        int dressed=0;
        foreach(var flower in map.GetComponentsInChildren<BubbleFlower>(true))
        {
            var source=PrefabUtility.GetCorrespondingObjectFromSource(flower.gameObject);
            if(source!=null && AssetDatabase.GetAssetPath(source)==path)continue;
            Dress(flower.gameObject,lily,materials,true);dressed++;
        }
        report.Add("Bubble lily look on the leaf flower prefab and "+dressed+" scene flowers.");
    }
    private static void Dress(GameObject flower,Mesh lily,Material[] materials,bool record)
    {
        // The plain primitives stay in the prefab (BubbleFlower may use them) but no longer draw.
        foreach(var renderer in flower.GetComponentsInChildren<MeshRenderer>(true))
            if(renderer.transform.parent==flower.transform && renderer.name!="Bubble lily"){renderer.enabled=false;if(record)Record(renderer);}
        var look=flower.transform.Find("Bubble lily");
        if(look==null)
        {
            look=new GameObject("Bubble lily",typeof(MeshFilter),typeof(MeshRenderer)).transform;
            look.SetParent(flower.transform,false);
        }
        look.GetComponent<MeshFilter>().sharedMesh=lily;
        var lilyRenderer=look.GetComponent<MeshRenderer>();lilyRenderer.sharedMaterials=materials;lilyRenderer.shadowCastingMode=ShadowCastingMode.Off;
        var muzzle=flower.transform.Find("Muzzle");
        if(muzzle!=null){muzzle.localPosition=new Vector3(0,LilyHeight+0.12f,0);if(record)Record(muzzle);}
        var capsule=flower.GetComponent<CapsuleCollider>();
        if(capsule!=null){capsule.height=LilyHeight+0.2f;capsule.center=new Vector3(0,(LilyHeight+0.2f)*0.5f,0);capsule.radius=0.3f;if(record)Record(capsule);}
        if(record){EditorUtility.SetDirty(flower);}
    }
    // quality 1: the real flower; 0: the low-poly field instance. Submeshes: stem, outer petals, inner petals, bud.
    private static Mesh LilyMesh(string name,float quality)
    {
        string path=Folder+"/"+name+".asset";
        var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved!=null)return saved;
        bool high=quality>0.5f;
        var stem=new RealmMeshes();var outer=new RealmMeshes();var inner=new RealmMeshes();var bud=new RealmMeshes();
        int rings=high?9:4;
        var line=Enumerable.Range(0,rings).Select(i=>{float t=i/(rings-1f);return new Vector3(Mathf.Sin(t*2.2f)*0.07f,t*LilyHeight,Mathf.Sin(t*1.3f)*0.03f);}).ToList();
        stem.Tube(line,t=>Mathf.Lerp(0.045f,0.028f,t),high?7:4,0f,0.2f,0.3f);
        Vector3 top=line[line.Count-1];
        // Outer layer: six wide petals opening into a cup. Inner layer: five narrower petals standing up
        // between them, turned half a petal, so the flower reads in two tiers.
        Petals(outer,top,6,0.3f,0.19f,0.3f,0.05f,0.3f,high?6:3,high?4:2);
        Petals(inner,top+Vector3.up*0.02f,5,0.3f+Mathf.PI/5f,0.11f,0.17f,0.02f,0.36f,high?5:2,high?3:2);
        bud.Sphere(top+Vector3.up*0.13f,Vector3.one*0.12f,Quaternion.identity,high?10:4,high?14:6,0.5f,0.5f);
        var parts=new[]{stem.Build("Lily stem"),outer.Build("Lily outer petals"),inner.Build("Lily inner petals"),bud.Build("Lily bud")};
        var mesh=new Mesh{name=name};
        if(!high)
        {
            // The field instance is one submesh: each part samples its own texel of a baked 4 x 1 colour strip
            // (stem, outer petals, inner petals, bud), so a whole tile draws in one call per tone.
            for(int k=0;k<parts.Length;k++)parts[k].uv=Enumerable.Repeat(new Vector2((k+0.5f)/4f,0.5f),parts[k].vertexCount).ToArray();
        }
        mesh.CombineMeshes(parts.Select(m=>new CombineInstance{mesh=m,transform=Matrix4x4.identity}).ToArray(),!high,true);
        foreach(var m in parts)Object.DestroyImmediate(m);
        mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);
        return mesh;
    }
    private static void Petals(RealmMeshes mesh,Vector3 top,int count,float turn,float halfWidth,float length,float start,float rise,int rows,int across)
    {
        for(int p=0;p<count;p++)
        {
            float a=p/(float)count*Mathf.PI*2f+turn;Vector3 outward=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),side=Vector3.Cross(Vector3.up,outward);
            int first=mesh.VertexCount;
            for(int i=0;i<=rows;i++)for(int j=0;j<=across;j++)
            {
                float t=i/(float)rows,u=j/(float)across*2f-1f;
                float width=halfWidth*Mathf.Sin(Mathf.Lerp(0.15f,1f,t)*Mathf.PI*0.85f)*(1f-0.35f*t*t);
                Vector3 at=top+outward*(start+t*length)+Vector3.up*(t*t*rise+0.02f)+side*(u*width)+outward*(-u*u*width*0.35f);
                Vector3 normal=Vector3.Cross(side,(outward*0.2f+Vector3.up*(0.52f*t)).normalized).normalized;
                mesh.Vertex(at,normal,new Vector2(t,p/(float)count),new Vector2(u,t));
            }
            for(int i=0;i<rows;i++)for(int j=0;j<across;j++)
            {int a0=first+i*(across+1)+j,b0=a0+across+1;mesh.Triangle(a0,b0,a0+1);mesh.Triangle(a0+1,b0,b0+1);}
        }
    }

    // ---------------- Bubble groves: real bubble flowers for bubble hopping ----------------

    // The bubbles to jump through are the real ones the bubble flowers shoot (as in Scene_Daniel), not
    // models. Groves of emitting flowers stand on ground of different heights (the terrace crowns, the
    // basin floor, the mother's rim), and their bubbles rise for a varied time before hovering, so the
    // air above holds bubbles at staggered heights: escape one bubble (Space) and drop into the next.
    private static void BubbleGroves()
    {
        foreach(string asset in new[]{"HopShell","HopCore"})if(AssetDatabase.LoadAssetAtPath<Mesh>(Folder+"/"+asset+".asset")!=null)AssetDatabase.DeleteAsset(Folder+"/"+asset+".asset");
        const string bubblePath="Assets/Generated/LeafMap/MeadowBubble.prefab";
        var prefab=PrefabUtility.LoadPrefabContents(bubblePath);
        try
        {
            var bubbleSettings=new SerializedObject(prefab.GetComponent<TrappingBubble>());
            bubbleSettings.FindProperty("upwardSpeed").floatValue=0.45f;
            bubbleSettings.FindProperty("riseDuration").vector2Value=new Vector2(3f,17f);
            bubbleSettings.FindProperty("lifetime").vector2Value=new Vector2(28f,42f);
            bubbleSettings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(prefab,bubblePath);
        }
        finally {PrefabUtility.UnloadPrefabContents(prefab);}
        var ecology=map.GetComponent<LeafMapEcology>();
        var group=Group("12 Bubble cascade — mesas and flowers firing in turn",root);
        // Flowers on each cascade mesa plus a few in the field at its foot; sorted from low to high.
        var spots=new List<Vector3>();
        foreach(var d in new[]{new Vector2(-16,-70),new Vector2(-19,-62),new Vector2(-28,-72),new Vector2(-14,-64)})spots.Add(Ground(LeafMapTerrain.ToWorld(d)));
        foreach(var tier in LeafMapTerrain.BubbleTierDesign)
        {
            Vector2 c=LeafMapTerrain.ToWorld(new Vector2(tier.x,tier.y));float r=tier.z*LeafMapTerrain.Scale*0.42f;
            float a0=Range(0f,6.28f);
            for(int k=0;k<3;k++){float a=a0+k*2.1f+Range(-0.3f,0.3f);spots.Add(Ground(c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r*Range(0.55f,1f)));}
        }
        var cascade=new List<BubbleFlower>();int planted=0;
        foreach(var at in spots.OrderBy(p=>p.y))
        {
            Vector2 w=Flat(at);Ground(w,out Vector3 normal);
            if(normal.y<0.75f || LeafMapTerrain.RouteDistance(w)<2.2f)continue;
            var go=Prefab("Assets/Generated/LeafMap/MeadowFlower.prefab",group,at,"Cascade bubble flower "+(++planted));
            SquirrelTree.PlaceOnGround(go.transform,at+Vector3.up*0.02f);
            var flower=go.GetComponent<BubbleFlower>();
            if(ecology!=null)ecology.RegisterFlower(flower);
            // The cascade fires them; their own timers stay off.
            flower.ConfigureGroundEmission(false,new Vector2(5f,9f));EditorUtility.SetDirty(flower);Record(flower);
            cascade.Add(flower);
        }
        group.gameObject.AddComponent<BubbleCascade>().Configure(cascade,7f);
        if(ecology!=null)
        {
            var settings=new SerializedObject(ecology);settings.FindProperty("ambientBubbleBudget").intValue=60;settings.FindProperty("maxFlowers").intValue=160;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
        report.Add("Bubble cascade: "+planted+" flowers on "+LeafMapTerrain.BubbleTierDesign.Length+" mesas, firing in turn every 7 s; bubbles hover 1.5–8 m up; ambient budget 64.");
    }
    private static GameObject Prefab(string path,Transform parent,Vector3 at,string name)
    {
        var go=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),parent);go.name=name;go.transform.position=at;return go;
    }

    // Depth and mood: blue-grey depth fog, a cool fill light against the sun, and gentle post effects in
    // this scene only (the player's camera here renders them; the prefab is unchanged).
    private static void Atmosphere()
    {
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;
        RenderSettings.fogColor=new Color(0.25f,0.3f,0.4f);RenderSettings.fogDensity=0.0042f;
        var fill=Group("Cool fill light — against the sun",root).gameObject.AddComponent<Light>();
        fill.type=LightType.Directional;fill.color=new Color(0.45f,0.6f,1f);fill.intensity=0.35f;fill.shadows=LightShadows.None;
        var sun=RenderSettings.sun;fill.transform.rotation=sun!=null?Quaternion.Euler(28,sun.transform.eulerAngles.y+160f,0):Quaternion.Euler(28,150,0);
        string path=Folder+"/LeafMood.asset";var profile=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(path);
        if(profile==null)
        {
            profile=ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();AssetDatabase.CreateAsset(profile,path);
            var bloom=profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);bloom.threshold.Override(1.05f);bloom.intensity.Override(0.55f);bloom.scatter.Override(0.68f);
            var vignette=profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);vignette.intensity.Override(0.24f);vignette.smoothness.Override(0.45f);
            var grade=profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);grade.saturation.Override(8f);grade.contrast.Override(6f);
            foreach(var component in profile.components)AssetDatabase.AddObjectToAsset(component,profile);
            EditorUtility.SetDirty(profile);
        }
        var volume=Group("Leaf mood — global post effects",root).gameObject.AddComponent<UnityEngine.Rendering.Volume>();
        volume.isGlobal=true;volume.sharedProfile=profile;volume.priority=1;
        var scene=map.gameObject.scene;
        foreach(var carrier in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PlayerCreatureCarrier>(true)))
            foreach(var camera in carrier.transform.root.GetComponentsInChildren<Camera>(true))
            {
                var data=camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                if(data==null)continue;data.renderPostProcessing=true;Record(data);
            }
        report.Add("Atmosphere: exp2 fog, cool fill light, scene post volume (bloom, vignette, grade).");
        // Far tentacles pause until the player comes near (they were the costliest scripts here).
        Group("Creature sleep — far tentacles pause",root).gameObject.AddComponent<CreatureSleep>();
    }

    // ---------------- Geometry helpers ----------------

    // A thin translucent sheet along path (uv.x across, uv.y metres along; uv1 = random, length) for HabitatFX Veil.
    private static Mesh Veil(List<Vector3> path,float width,float twist)
    {
        var vertices=new List<Vector3>();var uvs=new List<Vector2>();var extra=new List<Vector2>();var triangles=new List<int>();
        var along=new List<float>();float length=0f;
        for(int i=0;i<path.Count;i++){if(i>0)length+=Vector3.Distance(path[i-1],path[i]);along.Add(length);}
        const int across=8;
        for(int i=0;i<path.Count;i++)
        {
            Vector3 forward=(path[Mathf.Min(i+1,path.Count-1)]-path[Mathf.Max(i-1,0)]).normalized;
            Vector3 side=Vector3.Cross(Vector3.up,forward).normalized;if(side.sqrMagnitude<0.01f)side=Vector3.right;
            Vector3 up=Quaternion.AngleAxis(Mathf.Sin(along[i]*0.12f+twist*10f)*55f,forward)*Vector3.up;
            for(int k=0;k<=across;k++)
            {
                float u=k/(float)across;
                vertices.Add(path[i]+up*(u-0.5f)*width+side*Mathf.Sin(u*Mathf.PI)*width*0.25f);
                uvs.Add(new Vector2(u,along[i]));extra.Add(new Vector2(twist,length));
            }
        }
        for(int i=0;i+1<path.Count;i++)for(int k=0;k<across;k++)
        {int a=i*(across+1)+k,b=a+across+1;triangles.AddRange(new[]{a,b,a+1,a+1,b,b+1});}
        var mesh=new Mesh{name="Time veil",indexFormat=IndexFormat.UInt32};
        mesh.SetVertices(vertices);mesh.SetUVs(0,uvs);mesh.SetUVs(1,extra);mesh.SetTriangles(triangles,0);
        mesh.RecalculateNormals();mesh.RecalculateBounds();var bounds=mesh.bounds;bounds.Expand(2f);mesh.bounds=bounds;
        return mesh;
    }

    // Lifts every point of a floating path so it stays clear of the real terrain round it, and above head
    // height where it crosses a walking route.
    private static List<Vector3> Clearance(List<Vector3> path,float clear)
    {
        var result=new List<Vector3>(path);
        for(int i=0;i<result.Count;i++)
        {
            Vector2 w=new Vector2(result[i].x,result[i].z);float floor=Highest(w,clear)+clear;
            if(LeafMapTerrain.RouteDistance(w)<clear+3f)floor+=2.4f;
            if(result[i].y<floor)result[i]=new Vector3(result[i].x,floor,result[i].z);
        }
        // Smooth the lifts so the sheet never kinks.
        for(int pass=0;pass<3;pass++)for(int i=1;i+1<result.Count;i++)
        {
            float y=Mathf.Max(result[i].y,(result[i-1].y+result[i+1].y)*0.5f);result[i]=new Vector3(result[i].x,y,result[i].z);
        }
        return result;
    }
    private static float Highest(Vector2 w,float radius)
    {
        float h=Ground(w).y;
        for(int i=0;i<8;i++){float a=i*Mathf.PI*0.25f;h=Mathf.Max(h,Ground(w+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius).y);}
        return h;
    }
    private static List<Vector3> CatmullRom(List<Vector3> keys,int steps)
    {
        var result=new List<Vector3>();
        for(int i=0;i<keys.Count-1;i++)
        {
            Vector3 p0=keys[Mathf.Max(i-1,0)],p1=keys[i],p2=keys[i+1],p3=keys[Mathf.Min(i+2,keys.Count-1)];
            for(int s=0;s<steps;s++)
            {
                float t=s/(float)steps,t2=t*t,t3=t2*t;
                result.Add(0.5f*(2f*p1+(-p0+p2)*t+(2f*p0-5f*p1+4f*p2-p3)*t2+(-p0+3f*p1-3f*p2+p3)*t3));
            }
        }
        result.Add(keys[keys.Count-1]);
        return result;
    }

    // The real terrain under a world column: the first world chunk hit from above, else the height function.
    private static Vector3 Ground(Vector2 w)=>Ground(w,out _);
    private static Vector3 Ground(Vector2 w,out Vector3 normal)
    {
        float y=LeafMapTerrain.Height(w.x,w.y);normal=Vector3.up;
        var hits=Physics.RaycastAll(new Vector3(w.x,150,w.y),Vector3.down,260,1<<7);
        float best=float.NegativeInfinity;
        foreach(var hit in hits)if(world.OwnsCollider(hit.collider) && hit.point.y>best){best=hit.point.y;normal=hit.normal;}
        if(!float.IsNegativeInfinity(best))y=best;
        return new Vector3(w.x,y,w.y);
    }
    private static Vector2 Flat(Vector3 p)=>new Vector2(p.x,p.z);
    private static float Smooth(float from,float to,float x)=>Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(from,to,x));
    private static Vector3 Random3()=>new Vector3(Range(-1,1),Range(-1,1),Range(-1,1)).normalized;
    private static Mesh Combine(List<Mesh> meshes,string name)
    {
        var combine=meshes.Select(m=>new CombineInstance{mesh=m,transform=Matrix4x4.identity}).ToArray();
        var mesh=new Mesh{name=name,indexFormat=IndexFormat.UInt32};mesh.CombineMeshes(combine,true,true);
        foreach(var m in meshes)Object.DestroyImmediate(m);
        return mesh;
    }
    private static Mesh Keep(Mesh mesh){mesh.hideFlags=HideFlags.None;return mesh;}
    private static GameObject Part(string name,Transform parent,RealmMeshes mesh,Material material)=>Part(name,parent,mesh.Build(name),material);
    private static GameObject Part(string name,Transform parent,Mesh mesh,Material material)
    {
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);
        go.GetComponent<MeshFilter>().sharedMesh=Save(mesh);go.GetComponent<MeshRenderer>().sharedMaterial=material;
        return go;
    }
    private static void Solid(GameObject go){go.layer=7;go.tag="Ground";go.AddComponent<MeshCollider>().sharedMesh=go.GetComponent<MeshFilter>().sharedMesh;}
    private static GameObject Shadowless(GameObject go){go.GetComponent<MeshRenderer>().shadowCastingMode=ShadowCastingMode.Off;return go;}
    private static Mesh Save(Mesh mesh)
    {
        mesh.hideFlags=HideFlags.None;string path=Folder+"/"+meshPrefix+(serial++).ToString("D3")+".asset";
        if(AssetDatabase.LoadAssetAtPath<Mesh>(path)!=null)AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    private static Material Variant(Material source,string name,Action<Material> tune)
    {
        string path=Folder+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(source){name=name};AssetDatabase.CreateAsset(material,path);}
        tune(material);EditorUtility.SetDirty(material);return material;
    }
    private static Material Mat(string path)=>AssetDatabase.LoadAssetAtPath<Material>(path)??throw new InvalidOperationException("Missing material "+path);
    private static Transform Group(string name,Transform parent){var go=new GameObject(name);go.transform.SetParent(parent,false);return go.transform;}
    private static float Range(float a,float b)=>Mathf.Lerp(a,b,(float)(random??new System.Random(1)).NextDouble());
    private static void Record(Object o){if(PrefabUtility.IsPartOfPrefabInstance(o))PrefabUtility.RecordPrefabInstancePropertyModifications(o);}
    private static void EnsureFolder(string path){if(AssetDatabase.IsValidFolder(path))return;var parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
}
