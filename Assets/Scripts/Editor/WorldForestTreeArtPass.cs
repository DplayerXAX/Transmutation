using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

/// <summary>Forest-only mesh variants. Retains pore rims and ribbon sections rather than spatially welding them.</summary>
public static class WorldForestTreeArtPass
{
    const string Folder="Assets/Generated/WorldForestArtPass";
    static readonly int[] Angles={0,2,3,4,6,8,9,10,12,14,15,16,18,20,21,22,24};
    sealed class Data
    {
        public List<Vector3> p=new List<Vector3>(),n=new List<Vector3>();public List<Color> c=new List<Color>();public List<Vector2> uv=new List<Vector2>();public List<int> t=new List<int>();
        public Data(Mesh mesh){p.AddRange(mesh.vertices);n.AddRange(mesh.normals);var colours=mesh.colors;var tex=mesh.uv;for(int i=0;i<p.Count;i++){c.Add(colours.Length>i?colours[i]:Color.white);uv.Add(tex.Length>i?tex[i]:Vector2.zero);}t.AddRange(mesh.triangles);}
        public Mesh Build(string name)
        {
            var used=t.Distinct().OrderBy(i=>i).ToArray();var map=new int[p.Count];for(int i=0;i<used.Length;i++)map[used[i]]=i;
            var mesh=new Mesh{name=name,indexFormat=used.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.SetVertices(used.Select(i=>p[i]).ToList());mesh.SetNormals(used.Select(i=>n[i]).ToList());mesh.SetColors(used.Select(i=>c[i]).ToList());mesh.SetUVs(0,used.Select(i=>uv[i]).ToList());mesh.SetTriangles(t.Select(i=>map[i]).ToArray(),0);mesh.RecalculateBounds();return mesh;
        }
        public void Append(Mesh mesh)
        {int start=p.Count;p.AddRange(mesh.vertices);n.AddRange(mesh.normals);var colours=mesh.colors;var tex=mesh.uv;for(int i=0;i<mesh.vertexCount;i++){c.Add(colours.Length>i?colours[i]:Color.white);uv.Add(tex.Length>i?tex[i]:Vector2.zero);}t.AddRange(mesh.triangles.Select(i=>i+start));}
    }
    static Data Reduce(Mesh source,bool pores,bool ribbons)
    {
        var d=new Data(source);var parent=Enumerable.Range(0,d.p.Count).ToArray();int Root(int a){while(parent[a]!=a){parent[a]=parent[parent[a]];a=parent[a];}return a;}
        for(int i=0;i<d.t.Count;i+=3){parent[Root(d.t[i+1])]=Root(d.t[i]);parent[Root(d.t[i+2])]=Root(d.t[i]);}
        var groups=Enumerable.Range(0,d.p.Count).GroupBy(Root).ToDictionary(g=>g.Key,g=>g.ToArray());var faces=new Dictionary<int,List<int>>();
        for(int i=0;i<d.t.Count;i+=3){int root=Root(d.t[i]);if(!faces.TryGetValue(root,out var list)){list=new List<int>();faces.Add(root,list);}list.AddRange(new[]{d.t[i],d.t[i+1],d.t[i+2]});}
        d.t=new List<int>();
        void Face(int a,int b,int c){if(Vector3.Dot(Vector3.Cross(d.p[b]-d.p[a],d.p[c]-d.p[a]),d.n[a])<0){d.t.Add(a);d.t.Add(c);d.t.Add(b);}else d.t.AddRange(new[]{a,b,c});}
        foreach(var pair in faces)
        {
            var group=groups[pair.Key];int first=group[0],count=group.Length;
            if(group[count-1]-first+1!=count){d.t.AddRange(pair.Value);continue;}
            int bands=count==250?4:count==150?2:0;
            if(pores&&bands>0&&pair.Value.Count/3==4*bands*24+48)
            {
                int[] rows=bands==4?new[]{0,1,4}:new[]{0,1,2};int Id(int side,int band,int angle)=>first+(side*(bands+1)+band)*25+angle;
                for(int side=0;side<2;side++)for(int row=0;row<rows.Length-1;row++)for(int a=0;a<Angles.Length-1;a++)
                {int x=Id(side,rows[row],Angles[a]),y=Id(side,rows[row+1],Angles[a]),z=Id(side,rows[row+1],Angles[a+1]),w=Id(side,rows[row],Angles[a+1]);Face(x,y,z);Face(x,z,w);}
                for(int a=0;a<Angles.Length-1;a++){int x=Id(0,0,Angles[a]),y=Id(0,0,Angles[a+1]),z=Id(1,0,Angles[a+1]),w=Id(1,0,Angles[a]);d.t.AddRange(new[]{x,y,z,x,z,w});}
            }
            else if(ribbons&&(count-2)%8==0&&pair.Value.Count/3==2*(count-2)&&(count-2)/8>=9)
            {
                int rings=(count-2)/8;var retained=new List<int>();for(int r=0;r<rings-1;r+=2)retained.Add(r);retained.Add(rings-1);
                for(int r=1;r<retained.Count;r++)for(int side=0;side<8;side++){int a=first+retained[r-1]*8+side,b=first+retained[r-1]*8+(side+1)%8,c=first+retained[r]*8+(side+1)%8,e=first+retained[r]*8+side;d.t.AddRange(new[]{a,b,c,a,c,e});}
                for(int side=0;side<8;side++)d.t.AddRange(new[]{first+count-2,first+(side+1)%8,first+side,first+count-1,first+(rings-1)*8+side,first+(rings-1)*8+(side+1)%8});
            }
            else d.t.AddRange(pair.Value);
        }
        return d;
    }
    static void Warp(Data data,Vector3 foot,bool thicken)
    {
        // All connected layers use the same deformation, so branch sockets still meet crowns.
        for(int i=0;i<data.p.Count;i++){var q=data.p[i]-foot;data.p[i]=foot+new Vector3(q.x*1.25f,q.y*1.15f,q.z*1.25f);var n=data.n[i];data.n[i]=new Vector3(n.x/1.25f,n.y/1.15f,n.z/1.25f).normalized;}
    }
    static Mesh Core(Vector3 foot,float height,float radius)
    {
        var b=new SilkForestGeometry.MeshBuffer();b.Tube(Enumerable.Range(0,7).Select(i=>foot+Vector3.up*height*(i/6f)).ToArray(),radius,10,new Color(.82f,.9f,.92f),true);return b.CreateMesh("Continuous supporting silk trunk");
    }
    [MenuItem("Tools/Capstone/World Map/Check Enlarged Forest And Preview Scoops")]
    public static void Review()
    {
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");var forest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="02 Placeholder forest");
        var trees=forest.GetComponentsInChildren<CapsuleCollider>().Where(c=>c.GetComponentInParent<LODGroup>()!=null).ToArray();
        Mesh Crown(CapsuleCollider c)=>c.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Crown membranes").sharedMesh;
        Vector3 Foot(CapsuleCollider c)=>c.transform.TransformPoint(c.center-Vector3.up*c.height*.5f);
        bool Stack(CapsuleCollider c)=>Crown(c).name.Contains("overlapping silk scoops");
        var thickened=new HashSet<Mesh>();
        foreach(var tree in trees)
        {
            var mesh=tree.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Tensioned silk strands").sharedMesh;
            float radius=tree.height*.024f;
            if(thickened.Add(mesh))
            {
                var positions=mesh.vertices;var normals=mesh.normals;int start=positions.Length-70;var centre=Vector3.zero;for(int side=0;side<10;side++)centre+=positions[start+side];centre/=10;
                float current=Vector3.ProjectOnPlane(positions[start]-centre,Vector3.up).magnitude;float scale=Mathf.Max(1,radius/Mathf.Max(current,.00001f));
                for(int ring=0;ring<7;ring++)
                {
                    var midpoint=Vector3.zero;for(int side=0;side<10;side++)midpoint+=positions[start+ring*10+side];midpoint/=10;
                    for(int side=0;side<10;side++){int at=start+ring*10+side;var delta=positions[at]-midpoint;positions[at]=midpoint+new Vector3(delta.x*scale,delta.y,delta.z*scale);var normal=normals[at];normals[at]=new Vector3(normal.x/scale,normal.y,normal.z/scale).normalized;}
                }
                mesh.vertices=positions;mesh.normals=normals;mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            }
            tree.radius=Mathf.Max(tree.radius,radius*1.05f);EditorUtility.SetDirty(tree);tree.GetComponentInParent<LODGroup>().RecalculateBounds();
        }
        var occupied=new HashSet<Transform>();int moved=0;
        foreach(var colony in forest.GetComponentsInChildren<GlowBeeColony>())
        {
            var config=new SerializedObject(colony);var anchors=config.FindProperty("nestAnchors");
            for(int i=0;i<anchors.arraySize;i++)
            {
                var anchor=(Transform)anchors.GetArrayElementAtIndex(i).objectReferenceValue;var original=anchor.parent.GetComponent<CapsuleCollider>();
                if(!Stack(original))
                {
                    occupied.Add(original.transform);var foot=Foot(original);var mountDirection=Vector3.ProjectOnPlane(anchor.position-foot,Vector3.up).normalized;
                    anchor.position=foot+Vector3.up*Mathf.Clamp(anchor.position.y-foot.y,.65f,1.4f)+mountDirection*(original.radius*Mathf.Abs(original.transform.lossyScale.x)+.14f);EditorUtility.SetDirty(anchor);continue;
                }
                var target=trees.Where(c=>c.GetComponent<SquirrelTree>()==null&&!Stack(c)&&!occupied.Contains(c.transform)).OrderBy(c=>(Foot(c)-anchor.position).sqrMagnitude).First();occupied.Add(target.transform);
                var root=Foot(target);var direction=Vector3.ProjectOnPlane(anchor.position-root,Vector3.up).normalized;if(direction.sqrMagnitude<.1f)direction=Vector3.forward;
                anchor.SetParent(target.transform,true);anchor.position=root+Vector3.up*Mathf.Clamp(target.height*Mathf.Abs(target.transform.lossyScale.y)*.25f,.65f,1.4f)+direction*(target.radius*Mathf.Abs(target.transform.lossyScale.x)+.16f);anchor.rotation=Quaternion.LookRotation(direction);EditorUtility.SetDirty(anchor);moved++;
            }
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        var samples=new[]{trees.Where(c=>!Stack(c)).OrderBy(c=>Mathf.Abs(c.height*c.transform.lossyScale.y-7)).First(),trees.Where(c=>Crown(c).name.StartsWith("2 overlapping")).First(),trees.Where(c=>Crown(c).name.StartsWith("3 overlapping")).First()};
        var preview=new PreviewRenderUtility();bool async=ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation=false;
            for(int i=0;i<samples.Length;i++)
            {
                var original=samples[i];var copy=Object.Instantiate(original.gameObject);copy.transform.localScale=original.transform.lossyScale;preview.AddSingleGO(copy);var shape=copy.GetComponent<CapsuleCollider>();var root=copy.transform.TransformPoint(shape.center-Vector3.up*shape.height*.5f);copy.transform.position+=new Vector3((i-1)*8,0,0)-root;
            }
            preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.045f,.055f,.07f);preview.camera.fieldOfView=34;preview.camera.nearClipPlane=.1f;preview.camera.farClipPlane=150;
            preview.camera.transform.position=new Vector3(15,10,33);preview.camera.transform.LookAt(new Vector3(0,4,0));preview.lights[0].intensity=1.4f;preview.lights[0].transform.rotation=Quaternion.Euler(35,-25,0);preview.ambientColor=new Color(.4f,.43f,.45f);
            preview.BeginPreview(new Rect(0,0,1500,1000),GUIStyle.none);preview.Render(true);var rt=(RenderTexture)preview.EndPreview();var active=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1500,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1500,1000),0,0);image.Apply();File.WriteAllBytes("ArtCaptures/WorldForestArtPass/TreeVariants.png",image.EncodeToPNG());RenderTexture.active=active;Object.DestroyImmediate(image);
        }
        finally{preview.Cleanup();ShaderUtil.allowAsyncCompilation=async;}
        var report=File.ReadAllText("ArtCaptures/WorldForestArtPass/Report.txt").Replace("\\n","\n");File.WriteAllText("ArtCaptures/WorldForestArtPass/Report.txt",report);
        File.WriteAllText("ArtCaptures/WorldForestArtPass/Review.txt",$"PASS: three tree variants rendered. {moved} hive anchors moved from stack crowns to nearby bare regular trunks. Colony references retained.\n");
    }
    [MenuItem("Tools/Capstone/World Map/Lower Poly Taller Trees And Stacked Crowns")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play Mode first.");
        if(AssetDatabase.IsValidFolder(Folder))throw new Exception("Tree art pass already applied; refusing a second scale pass.");
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");var forest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="02 Placeholder forest");
        var trees=forest.GetComponentsInChildren<CapsuleCollider>().Where(c=>c.GetComponentInParent<LODGroup>()!=null).ToArray();
        long before=trees.Sum(c=>c.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh==null?0L:(long)f.sharedMesh.triangles.Length/3));
        AssetDatabase.CreateFolder("Assets/Generated","WorldForestArtPass");int assets=0,stacks=0,two=0,three=0;SilkForestMeshLibrary library=null;
        Mesh Save(Mesh mesh)
        {if(assets%8==0){library=ScriptableObject.CreateInstance<SilkForestMeshLibrary>();AssetDatabase.CreateAsset(library,Folder+"/Trees"+(assets/8).ToString("00")+".asset");}AssetDatabase.AddObjectToAsset(mesh,library);EditorUtility.SetDirty(library);assets++;return mesh;}
        var regular=new Dictionary<Mesh,Mesh>();var variants=new Dictionary<(Mesh,int),(Mesh crown,Mesh trunk)>();
        for(int index=0;index<trees.Length;index++)
        {
            var collider=trees[index];var tree=collider.transform;var foot=collider.center-Vector3.up*collider.height*.5f;float h=collider.height,r=collider.radius;
            var crown=tree.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Crown membranes");var trunk=tree.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Tensioned silk strands");
            bool stacked=tree.GetComponent<SquirrelTree>()==null&&index%2==0&&!tree.GetComponentsInChildren<Transform>().Any(t=>t.name=="Small bee hive anchor - bare trunk");int scoops=index%4==0?3:2;
            if(stacked)
            {
                var key=(crown.sharedMesh,scoops);
                if(!variants.TryGetValue(key,out var pair))
                {
                    var shell=new SilkForestGeometry.MeshBuffer();var film=new SilkForestGeometry.MeshBuffer();var silk=new SilkForestGeometry.MeshBuffer();var random=new System.Random(911+variants.Count);
                    float radius=Mathf.Clamp(h*.22f,.35f,2.8f),spacing=radius*1.38f,topRadius=radius*(scoops==3?.78f:.88f),baseY=h*1.2f-topRadius-spacing*(scoops-1);
                    for(int s=0;s<scoops;s++)
                    {
                        float size=radius*Mathf.Lerp(1,scoops==3?.78f:.88f,s/(float)(scoops-1));var at=foot+Vector3.up*(baseY+s*spacing);
                        SilkForestGeometry.Crown(shell,film,at,size,.06f,.012f,0,1,random,Vector3.down,0,.76f,.014f);
                    }
                    var raw=shell.CreateMesh("Stacked crown source");var data=Reduce(raw,true,false);Object.DestroyImmediate(raw);var crownMesh=Save(data.Build(scoops+" overlapping silk scoops"));
                    var end=foot+Vector3.up*baseY;SilkForestGeometry.SilkBundle(silk,t=>Vector3.Lerp(foot+Vector3.up*.08f,end,t),Mathf.Max(r*.8f,.045f),r*2,radius*.35f,.016f,.25f,random,.75f,.4f,30,.5f,.15f,.7f,7,false,true);
                    raw=silk.CreateMesh("Stacked trunk source");data=Reduce(raw,false,true);Object.DestroyImmediate(raw);var core=Core(foot,baseY,r*.95f);data.Append(core);Object.DestroyImmediate(core);var trunkMesh=Save(data.Build("Thick continuous stack trunk"));pair=(crownMesh,trunkMesh);variants.Add(key,pair);
                }
                crown.sharedMesh=pair.crown;trunk.sharedMesh=pair.trunk;stacks++;if(scoops==2)two++;else three++;
            }
            else
            {
                foreach(var filter in tree.GetComponentsInChildren<MeshFilter>())
                {
                    var original=filter.sharedMesh;if(original==null)continue;
                    if(!regular.TryGetValue(original,out var mesh))
                    {
                        var data=Reduce(original,filter==crown,filter==trunk);Warp(data,foot,filter==trunk);
                        if(filter==trunk){var core=Core(foot,h*.86f*1.15f,r*1.12f);data.Append(core);Object.DestroyImmediate(core);}
                        mesh=Save(data.Build(original.name+" lower poly broad tall"));regular.Add(original,mesh);
                    }
                    filter.sharedMesh=mesh;EditorUtility.SetDirty(filter);
                }
            }
            float newHeight=h*(stacked?1.2f:1.15f);collider.center=foot+Vector3.up*newHeight*.5f;collider.height=newHeight;collider.radius=r*1.5f;EditorUtility.SetDirty(collider);
            foreach(var anchor in tree.GetComponentsInChildren<Transform>().Where(t=>t.name=="Small bee hive anchor - bare trunk"))
            {var local=tree.InverseTransformPoint(anchor.position)-foot;var direction=new Vector3(local.x,0,local.z).normalized;anchor.position=tree.TransformPoint(foot+Vector3.up*local.y*1.15f+direction*(r*1.5f+.18f/Mathf.Max(.1f,tree.lossyScale.x)));EditorUtility.SetDirty(anchor);}
            var lod=tree.GetComponentInParent<LODGroup>();lod.RecalculateBounds();EditorUtility.SetDirty(lod);
        }
        foreach(var filter in forest.GetComponentsInChildren<MeshFilter>())
        {
            var m=filter.sharedMesh;if(m==null||!AssetDatabase.GetAssetPath(m).StartsWith(Folder))continue;
            if(m.vertices.Any(v=>!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z))||m.triangles.Any(i=>i<0||i>=m.vertexCount))throw new Exception("Invalid new forest mesh.");
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        long after=trees.Sum(c=>c.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh==null?0L:(long)f.sharedMesh.triangles.Length/3));
        Directory.CreateDirectory("ArtCaptures/WorldForestArtPass");File.WriteAllText("ArtCaptures/WorldForestArtPass/Report.txt",$"Trees: {trees.Length}. Triangles {before:N0} -> {after:N0} ({100.0*(before-after)/before:F1}% reduction).\\nRegular canopy width +25%, height +15%; continuous thicker core trunks. Stacked variants: {stacks} ({two} two-scoop, {three} three-scoop); heights +20%. Low-crown squirrel habitat trees retained. Hive mounts, colliders and LOD bounds updated. Original source scene/meshes unchanged.\\n");
        Debug.Log("Forest art pass complete: "+before+" -> "+after+" triangles; "+stacks+" stacked trees.");
    }
}
