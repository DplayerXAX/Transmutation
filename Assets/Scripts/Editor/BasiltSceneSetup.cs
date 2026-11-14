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

/// <summary>Authors a new, isolated exploration scene using the project's sculptable terrain pipeline.</summary>
public static partial class BasiltSceneSetup
{
    public const string ScenePath="Assets/Scenes/Scene Basilt.unity";
    public const string Folder="Assets/Generated/Basilt";
    private static string geometryPath=>Folder+"/BasiltDetails.asset";
    private static int meshIndex;
    private static Material vine,bone,basalt;

    [MenuItem("Tools/Capstone/Basilt/Create Scene Basilt")]
    public static void Create()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play mode first.");
        if(File.Exists(ScenePath))throw new InvalidOperationException("Scene Basilt already exists. Open it to edit; creation never overwrites it.");
        Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
        var previous=SceneManager.GetActiveScene();
        if(previous.IsValid() && previous.isDirty && !EditorSceneManager.SaveScene(previous))throw new IOException("Could not preserve the current scene before creating Basilt.");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            var ground=Clone("Assets/Materials/World/MAT_World_Surface.mat","Basilt Ground - world ink lines");
            ground.SetColor("_LineColor",new Color(0.55f,0.57f,0.55f));ground.SetColor("_LineColor2",new Color(0.65f,0.67f,0.63f));
            basalt=Clone("Assets/Materials/World/MAT_HeightStippleGround.mat","Basilt Columns - height stipple");
            basalt.SetFloat("_HeightLow",-8);basalt.SetFloat("_HeightHigh",110);basalt.SetFloat("_BandCount",18);
            basalt.SetColor("_HighColor",new Color(0.88f,0.89f,0.87f));basalt.SetColor("_GroundColor",new Color(0.45f,0.46f,0.45f));
            basalt.SetColor("_LowColor",new Color(0.19f,0.20f,0.20f));basalt.SetFloat("_FacetStrength",0.85f);
            basalt.SetFloat("_ContourStrength",0.22f);
            vine=new Material(Shader.Find("Capstone/World/BasiltVine")){name="Basilt Vine - living bark"};
            AssetDatabase.CreateAsset(vine,Folder+"/BasiltVines.mat");
            bone=new Material(Shader.Find("Capstone/Landmarks/BoneMarble")){name="Basilt Fossil - pale mineral"};
            bone.SetColor("_ColorA",new Color(0.85f,0.85f,0.79f));bone.SetColor("_ColorB",new Color(0.46f,0.48f,0.43f));
            bone.SetColor("_ColorMid",new Color(0.67f,0.69f,0.62f));bone.SetFloat("_Scale",0.045f);bone.SetFloat("_Warp",1.5f);
            bone.SetFloat("_Bands",2);bone.SetFloat("_FringeStrength",0);bone.SetFloat("_GlowStrength",0);bone.SetFloat("_Flow",0);
            AssetDatabase.CreateAsset(bone,Folder+"/BasiltFossil.mat");
            RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Sky_Black.mat");
            RenderSettings.ambientMode=AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(0.49f,0.52f,0.54f);
            RenderSettings.ambientEquatorColor=new Color(0.34f,0.36f,0.35f);
            RenderSettings.ambientGroundColor=new Color(0.15f,0.17f,0.16f);
            RenderSettings.fog=true;RenderSettings.fogMode=FogMode.Linear;
            RenderSettings.fogColor=new Color(0.12f,0.15f,0.15f);RenderSettings.fogStartDistance=240;RenderSettings.fogEndDistance=850;
            var sun=new GameObject("Low raking daylight").AddComponent<Light>();sun.type=LightType.Directional;
            sun.intensity=1.6f;sun.color=new Color(0.94f,0.96f,0.91f);sun.shadows=LightShadows.Soft;
            sun.transform.rotation=Quaternion.Euler(33,-38,0);RenderSettings.sun=sun;
            NewTerrain("Basilt - Ground and hexagonal basalt",TerrainRecipeKind.Basilt,ground,basalt,new Vector2(-12,98),1.25f);
            NewTerrain("Basilt - Walkable vine terrain",TerrainRecipeKind.BasiltVines,vine,vine,new Vector2(-10,34),1);
            var meshRoot=new Mesh(){name="Basilt generated detail meshes"};AssetDatabase.CreateAsset(meshRoot,geometryPath);meshIndex=0;
            var details=new GameObject("Placed World Objects").transform;
            var thorns=new GameObject("Vine side thorns - clear walking crowns").transform;thorns.SetParent(details);
            MakeThorns(thorns);
            var fossil=new GameObject("The Unnamed Remnant - incomplete branching rib vault").transform;fossil.SetParent(details);
            MakeFossil(fossil);
            // Very small columns use explicit hex meshes, below the terrain lattice's reliable feature size.
            var chips=new GameObject("Human scale basalt fragments").transform;chips.SetParent(details);
            var meshColumns=new GameObject("Mesh basalt field - compare with terrain columns to the west").transform;meshColumns.SetParent(details);
            foreach(var c in new BasiltDensity(1147,false).Columns.Where(BasiltDensity.UseMeshColumn))
            {
                float baseY=BasiltDensity.GroundHeight(c.centre.x,c.centre.y)-0.3f;
                Hex(meshColumns,new Vector3(c.centre.x,baseY,c.centre.y),c.radius,c.top-baseY);
            }
            var random=new System.Random(107);
            for(int i=0;i<50;i++)
            {
                float x=-28-(float)random.NextDouble()*84,z=-100+(float)random.NextDouble()*62;
                float h=0.5f+(float)random.NextDouble()*1.9f,r=0.4f+(float)random.NextDouble()*0.8f;
                Hex(chips,new Vector3(x,BasiltDensity.GroundHeight(x,z)-0.2f,z),r,h);
            }
            AddPlayer(scene);
            // Named bookmarks are plain transforms, useful while editing and for automated validation.
            Bookmark("01 - Arrival",new Vector3(-8,4,-126),new Vector3(-48,16,-30));
            Bookmark("02 - Vine crossing",new Vector3(10,5,-62),new Vector3(-44,18,-29));
            Bookmark("03 - Fossil plain",new Vector3(47,5,28),new Vector3(82,27,105));
            Bookmark("04 - Overview",new Vector3(188,137,-235),new Vector3(-25,19,12));
            Bookmark("05 - Basalt mesh versus terrain",new Vector3(15,17,-115),new Vector3(-60,6,-65));
            AssetDatabase.SaveAssets();
            if(!EditorSceneManager.SaveScene(scene,ScenePath))throw new IOException("Could not save Scene Basilt.");
            // The previous scene was saved before authoring, and must not add a second player/lighting rig here.
            if(previous.IsValid())EditorSceneManager.CloseScene(previous,true);
            UpgradeToJungle();
            FrameOverview();
            Debug.Log("Scene Basilt created: sculptable basalt and vine roads, separate materials, distant fossil vault.");
        }
        catch
        {
            EditorSceneManager.CloseScene(scene,true);if(previous.IsValid())SceneManager.SetActiveScene(previous);throw;
        }
    }
    private static Material Clone(string path,string name)
    {
        var source=AssetDatabase.LoadAssetAtPath<Material>(path);if(source==null)throw new InvalidOperationException("Missing material "+path);
        var m=new Material(source){name=name};AssetDatabase.CreateAsset(m,Folder+"/"+(name.Contains("Columns")?"BasiltColumns":"BasiltGround")+".mat");return m;
    }
    private static void NewTerrain(string name,TerrainRecipeKind kind,Material surface,Material inner,Vector2 bounds,float spacing)
    {
        var go=new GameObject(name);go.layer=7;go.tag="Ground";var w=go.AddComponent<ProceduralWorld>();var s=new SerializedObject(w);
        s.FindProperty("worldSize").vector2Value=new Vector2(320,384);s.FindProperty("cellSize").floatValue=spacing;
        s.FindProperty("chunkCells").intValue=24;s.FindProperty("seed").intValue=1147;
        s.FindProperty("surfaceMaterial").objectReferenceValue=surface;s.FindProperty("innerMaterial").objectReferenceValue=inner;
        s.FindProperty("sinkholeCount").intValue=0;s.FindProperty("firstSinkholeDistance").floatValue=0;
        s.FindProperty("placePlayerOnSurface").boolValue=false;s.FindProperty("respawnFallingPlayer").boolValue=kind==TerrainRecipeKind.Basilt;
        s.FindProperty("blockingRadius").floatValue=70;s.FindProperty("meshesPerFrame").intValue=16;
        var recipe=s.FindProperty("generationRecipe");recipe.FindPropertyRelative("kind").enumValueIndex=(int)kind;
        recipe.FindPropertyRelative("verticalBounds").vector2Value=bounds;recipe.FindPropertyRelative("safeSpawn").vector3Value=new Vector3(-8,3,-126);
        s.ApplyModifiedPropertiesWithoutUndo();
        string stem=kind==TerrainRecipeKind.Basilt?"BasiltLand":"BasiltRoots";
        var data=ScriptableObject.CreateInstance<TerrainSculptData>();
        var ds=new SerializedObject(data);ds.FindProperty("headroom").floatValue=6;ds.ApplyModifiedPropertiesWithoutUndo();
        data.Freeze(w.CaptureLayout(out var checks),checks,"Basilt v1 / "+kind);AssetDatabase.CreateAsset(data,Folder+"/"+stem+".asset");
        s.Update();s.FindProperty("sculptData").objectReferenceValue=data;s.ApplyModifiedPropertiesWithoutUndo();
        File.WriteAllBytes(Folder+"/"+stem+"Chunks.bytes",w.BuildChunkCache());AssetDatabase.ImportAsset(Folder+"/"+stem+"Chunks.bytes");
        w.ChunkCache=AssetDatabase.LoadAssetAtPath<TextAsset>(Folder+"/"+stem+"Chunks.bytes");w.Generate(true);
    }
    private static void AddPlayer(Scene scene)
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/PlayerObjects.prefab");
        var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);root.name="PlayerObjects";
        var player=root.GetComponentInChildren<SmoothFirstPersonController>();
        Vector3 spawn=new Vector3(-8,BasiltDensity.GroundHeight(-8,-126)+1.25f,-126);
        root.transform.position+=spawn-player.transform.position;player.SetLook(-12,0);
        var camera=root.GetComponentInChildren<Camera>(true);
        foreach(var c in root.GetComponentsInChildren<Camera>(true)){c.enabled=true;c.farClipPlane=650;c.clearFlags=CameraClearFlags.Skybox;}
        foreach(var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if(component==null)continue;var settings=new SerializedObject(component);var p=settings.GetIterator();
            while(p.NextVisible(true))if(p.propertyType==SerializedPropertyType.ObjectReference && p.type.Contains("Camera"))p.objectReferenceValue=camera;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
    }
    public static void RefreshGeneratedLayout()
    {
        if(GameObject.Find(JungleRoot)!=null){UpgradeToJungle();return;}
        if(Application.isPlaying || SceneManager.GetActiveScene().path!=ScenePath)throw new InvalidOperationException("Open Basilt in Edit mode.");
        var worlds=Object.FindObjectsByType<ProceduralWorld>(FindObjectsSortMode.None);
        foreach(var w in worlds)
        {
            if(w.SculptData.Edits.Count>0)throw new InvalidOperationException("Preserve authored sculpt edits before replacing the generated layout.");
            w.SculptData.Freeze(w.CaptureLayout(out var checks),checks,"Basilt v1 / connected roads");EditorUtility.SetDirty(w.SculptData);
            string stem=w.name.Contains("vine")?"BasiltRoots":"BasiltLand";
            File.WriteAllBytes(Folder+"/"+stem+"Chunks.bytes",w.BuildChunkCache());AssetDatabase.ImportAsset(Folder+"/"+stem+"Chunks.bytes",ImportAssetOptions.ForceUpdate);
            w.ChunkCache=AssetDatabase.LoadAssetAtPath<TextAsset>(Folder+"/"+stem+"Chunks.bytes");w.Generate(true);
        }
        vine=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/BasiltVines.mat");basalt=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/BasiltColumns.mat");meshIndex=10000;
        var thorns=GameObject.Find("Vine side thorns - clear walking crowns").transform;
        var field=GameObject.Find("Mesh basalt field - compare with terrain columns to the west").transform;
        foreach(var group in new[]{thorns,field})
        {
            var meshes=group.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).Distinct().ToArray();
            while(group.childCount>0)Object.DestroyImmediate(group.GetChild(0).gameObject);
            foreach(var mesh in meshes)Object.DestroyImmediate(mesh,true);
        }
        MakeThorns(thorns);
        foreach(var c in new BasiltDensity(1147,false).Columns.Where(BasiltDensity.UseMeshColumn))
        {float baseY=BasiltDensity.GroundHeight(c.centre.x,c.centre.y)-0.3f;Hex(field,new Vector3(c.centre.x,baseY,c.centre.y),c.radius,c.top-baseY);}
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }
    public static void RefreshDetailFrames()
    {
        if(Application.isPlaying || SceneManager.GetActiveScene().path!=ScenePath)throw new InvalidOperationException("Open Basilt in Edit mode.");
        vine=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/BasiltVines.mat");bone=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/BasiltFossil.mat");meshIndex=20000;
        var thorns=GameObject.Find("Vine side thorns - clear walking crowns").transform;
        var fossil=GameObject.Find("The Unnamed Remnant - incomplete branching rib vault").transform;
        foreach(var group in new[]{thorns,fossil})
        {
            var meshes=group.GetComponentsInChildren<MeshFilter>().Select(f=>f.sharedMesh).Distinct().ToArray();
            while(group.childCount>0)Object.DestroyImmediate(group.GetChild(0).gameObject);
            foreach(var mesh in meshes)Object.DestroyImmediate(mesh,true);
        }
        MakeThorns(thorns);MakeFossil(fossil);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }
    private static void Bookmark(string name,Vector3 at,Vector3 target)
    {var go=new GameObject(name);go.transform.position=at;go.transform.LookAt(target);}
    public static void FrameOverview()
    {
        var view=SceneView.lastActiveSceneView;if(view==null)return;
        view.sceneLighting=true;view.drawGizmos=false;
        view.LookAt(new Vector3(-19,20,10),Quaternion.LookRotation(new Vector3(-19,20,10)-new Vector3(188,137,-235)),245,false,true);view.Repaint();
    }
    private static void MakeThorns(Transform parent)
    {
        for(int r=0;r<BasiltDensity.Roads.Length;r++)
        {
            Vector3[] road=BasiltDensity.SampleRoad(r);float radius=BasiltDensity.RoadRadii[r];
            for(int i=5;i<road.Length-5;i+=4)
            {
                Vector3 p=road[i];if(p.y<2)continue;
                Vector3 forward=(road[i+1]-road[i-1]).normalized;Vector3 side=Vector3.Cross(Vector3.up,forward).normalized;
                foreach(float sign in new[]{-1f,1f})
                {
                    Vector3 dir=(side*sign-Vector3.up*0.16f).normalized;Vector3 at=p+dir*(radius-0.2f);
                    Tube("Hooked vine thorn",parent,new[]{at,at+dir*1.2f-forward*0.25f,at+dir*2.5f-forward*1.4f},new[]{0.65f,0.48f,0.015f},vine,false,7);
                }
            }
        }
    }
    private static void MakeFossil(Transform parent)
    {
        // An elevated, wandering dorsal beam with irregular bifurcating ribs; no skull or limbs.
        var spine=new List<Vector3>();var radii=new List<float>();
        for(int i=0;i<=20;i++)
        {float z=55+i*5;spine.Add(new Vector3(82+5*Mathf.Sin(i*0.35f),43+8*Mathf.Sin(i*0.19f),z));radii.Add(2.8f+0.6f*Mathf.Sin(i*0.55f));}
        Tube("Suspended mineral spine",parent,spine.ToArray(),radii.ToArray(),bone,true,12);
        for(int rib=0;rib<10;rib++)
        {
            float z=62+rib*9.5f,peak=43+8*Mathf.Sin((z-55)/5*0.19f);
            float cx=82+5*Mathf.Sin((z-55)/5*0.35f),width=23+6*Mathf.Sin(rib*0.57f);
            for(int side=-1;side<=1;side+=2)
            {
                bool broken=(rib==2&&side==1)||(rib==7&&side==-1);
                var keys=new[]{new Vector3(cx+side*width,-1,z-9),new Vector3(cx+side*(width+5),peak*0.32f,z-13),new Vector3(cx+side*width*0.92f,peak*0.77f,z-7),new Vector3(cx+side*width*0.45f,peak+2,z+2),new Vector3(cx,peak,z+5)};
                var points=Smooth(keys,8);int count=broken?points.Length*3/4:points.Length;
                var path=points.Take(count).ToArray();var rr=new float[count];
                for(int i=0;i<count;i++)rr[i]=Mathf.Lerp(3.2f,1.6f,i/(float)(count-1))*(1+0.12f*Mathf.Sin(i*0.55f));
                Tube("Rib "+(rib+1)+(side<0?" west":" east")+(broken?" - fractured":""),parent,path,rr,bone,true,10);
                if(rib%2==0)
                {
                    Vector3 p=keys[2];
                    Tube("Forked crown spur",parent,Smooth(new[]{p,p+new Vector3(side*6,6,3),p+new Vector3(side*10,10,-3)},7),null,bone,true,9,1.45f,0.12f);
                }
            }
        }
        for(int i=0;i<6;i++)
        {
            Vector3 p=new Vector3(45+i*9,2,151+(i%2)*10);
            Tube("Half buried fossil fragment",parent,Smooth(new[]{p,p+new Vector3(3,3,4),p+new Vector3(11,1,7)},6),null,bone,true,10,2.3f,0.7f);
        }
    }
    private static Vector3[] Smooth(Vector3[] keys,int steps)
    {
        var result=new List<Vector3>();
        for(int i=0;i<keys.Length-1;i++)for(int j=0;j<steps;j++)
        {float t=j/(float)steps;Vector3 a=keys[Mathf.Max(0,i-1)],b=keys[i],c=keys[i+1],d=keys[Mathf.Min(keys.Length-1,i+2)];result.Add(0.5f*(2*b+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t));}
        result.Add(keys[keys.Length-1]);return result.ToArray();
    }
    private static void Tube(string name,Transform parent,Vector3[] path,float[] radii,Material mat,bool collider,int sides,float start=1,float end=1)
    {
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var tri=new List<int>();float length=0;Vector3 previousRight=Vector3.zero;
        for(int i=0;i<path.Length;i++)
        {
            if(i>0)length+=Vector3.Distance(path[i],path[i-1]);
            Vector3 tangent=(path[Mathf.Min(path.Length-1,i+1)]-path[Mathf.Max(0,i-1)]).normalized;
            // Parallel-transport the frame; switching the reference axis at a vertical tangent
            // used to flip entire rings and pinch the ribs into unintended fractures.
            Vector3 right=i==0?Vector3.Cross(Mathf.Abs(tangent.y)>0.95f?Vector3.forward:Vector3.up,tangent).normalized:Vector3.ProjectOnPlane(previousRight,tangent).normalized;
            if(right.sqrMagnitude<0.01f)right=Vector3.Cross(Vector3.forward,tangent).normalized;
            previousRight=right;
            Vector3 up=Vector3.Cross(tangent,right);float radius=radii!=null?radii[i]:Mathf.Lerp(start,end,i/(float)(path.Length-1));
            for(int j=0;j<sides;j++)
            {float a=j*2*Mathf.PI/sides;vertices.Add(path[i]+(right*Mathf.Cos(a)+up*Mathf.Sin(a))*radius);uv.Add(new Vector2(i/(float)(path.Length-1),j/(float)sides));}
            if(i==0)continue;
            for(int j=0;j<sides;j++)
            {int a=(i-1)*sides+j,b=(i-1)*sides+(j+1)%sides,c=i*sides+j,d=i*sides+(j+1)%sides;tri.Add(a);tri.Add(b);tri.Add(c);tri.Add(b);tri.Add(d);tri.Add(c);}
        }
        int first=vertices.Count;vertices.Add(path[0]);uv.Add(Vector2.zero);int last=vertices.Count;vertices.Add(path[path.Length-1]);uv.Add(Vector2.one);
        for(int j=0;j<sides;j++){tri.Add(first);tri.Add((j+1)%sides);tri.Add(j);int a=(path.Length-1)*sides;tri.Add(last);tri.Add(a+j);tri.Add(a+(j+1)%sides);}
        MeshObject(name,parent,vertices,tri,uv,mat,collider);
    }
    private static void Hex(Transform parent,Vector3 p,float r,float height)
    {
        var v=new List<Vector3>();var t=new List<int>();var uv=new List<Vector2>();
        for(int layer=0;layer<2;layer++)for(int j=0;j<6;j++){float a=j*Mathf.PI/3;v.Add(p+new Vector3(Mathf.Cos(a)*r,layer*height,Mathf.Sin(a)*r));uv.Add(Vector2.zero);}
        for(int j=0;j<6;j++){int n=(j+1)%6;t.Add(j);t.Add(j+6);t.Add(n);t.Add(n);t.Add(j+6);t.Add(n+6);}
        for(int j=1;j<5;j++){t.Add(6);t.Add(6+j+1);t.Add(6+j);}
        // Duplicate vertices per triangle so cap/side normals stay sharp, unlike the terrain comparison.
        var flat=new List<Vector3>();var flatUv=new List<Vector2>();var flatIndices=new List<int>();
        foreach(int index in t){flat.Add(v[index]);flatUv.Add(uv[index]);flatIndices.Add(flatIndices.Count);}
        MeshObject("Mesh basalt hex",parent,flat,flatIndices,flatUv,basalt,true);
    }
    private static void MeshObject(string name,Transform parent,List<Vector3> vertices,List<int> indices,List<Vector2> uv,Material mat,bool collider)
    {
        var mesh=new Mesh(){name=name+" "+meshIndex++,indexFormat=IndexFormat.UInt32};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateBounds();
        AssetDatabase.AddObjectToAsset(mesh,geometryPath);
        var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.layer=7;go.tag="Ground";go.transform.SetParent(parent,false);
        go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=mat;
        if(collider)go.AddComponent<MeshCollider>().sharedMesh=mesh;
    }
}
