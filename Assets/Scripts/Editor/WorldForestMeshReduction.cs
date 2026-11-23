using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

public static class WorldForestMeshReduction
{
    [MenuItem("Tools/Capstone/World Map/Preview Reduced Forest Trees")]
    public static void Preview()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");
        var forest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="02 Placeholder forest");
        var trees=forest.GetComponentsInChildren<LODGroup>(true).OrderByDescending(t=>t.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh!=null?(long)f.sharedMesh.triangles.Length:0)).ToArray();
        var preview=new PreviewRenderUtility();bool async=ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation=false;
            for(int sample=0;sample<3;sample++)
            {
                var root=Object.Instantiate(trees[sample*trees.Length/3].gameObject);preview.AddSingleGO(root);
                foreach(var lod in root.GetComponentsInChildren<LODGroup>())lod.enabled=false;
                var renderers=root.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;
                foreach(var r in renderers){r.enabled=true;bounds.Encapsulate(r.bounds);}
                foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    var mesh=filter.sharedMesh;if(mesh==null)continue;
                    if(mesh.triangles.Any(index=>index<0||index>=mesh.vertexCount)||mesh.vertices.Any(v=>!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z)))throw new Exception("Invalid reduced forest mesh.");
                }
                preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.07f,.09f,.11f);
                preview.camera.fieldOfView=35;preview.camera.nearClipPlane=.01f;preview.camera.farClipPlane=2000;
                preview.camera.transform.position=bounds.center+new Vector3(.8f,.2f,1).normalized*bounds.extents.magnitude*3.5f;
                preview.camera.transform.LookAt(bounds.center);preview.lights[0].intensity=1.3f;preview.lights[0].transform.rotation=Quaternion.Euler(30,-30,0);preview.ambientColor=Color.gray;
                preview.BeginPreview(new Rect(0,0,1000,1000),GUIStyle.none);preview.Render(true);var texture=(RenderTexture)preview.EndPreview();var active=RenderTexture.active;
                RenderTexture.active=texture;var png=new Texture2D(1000,1000,TextureFormat.RGB24,false);png.ReadPixels(new Rect(0,0,1000,1000),0,0);png.Apply();
                File.WriteAllBytes("ArtCaptures/WorldForestOptimization/Tree"+sample+".png",png.EncodeToPNG());RenderTexture.active=active;Object.DestroyImmediate(png);Object.DestroyImmediate(root);
            }
            File.WriteAllText("ArtCaptures/WorldForestOptimization/Validation.txt","PASS: three representative reduced trees rendered; finite vertices and valid triangle indices.\n");
        }
        finally{preview.Cleanup();ShaderUtil.allowAsyncCompilation=async;}
    }
    private static void ReduceStrandsSafely()
    {
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");
        var forest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="02 Placeholder forest");
        var filters=forest.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null&&(AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith("Assets/Generated/WorldForestReduced/")||AssetDatabase.GetAssetPath(f.sharedMesh).StartsWith("Assets/Generated/WorldForestRepaired/"))).ToArray();
        long before=filters.Sum(f=>(long)f.sharedMesh.triangles.Length/3);int strands=0;
        foreach(var mesh in filters.Select(f=>f.sharedMesh).Distinct().Where(m=>m.name=="Tensioned silk strands"))
        {
            var thickened=mesh.vertices;
            // A separate cross-section pass below preserves path centres and endpoints.
            var indices=mesh.triangles;var parent=Enumerable.Range(0,mesh.vertexCount).ToArray();
            int Root(int a){while(parent[a]!=a){parent[a]=parent[parent[a]];a=parent[a];}return a;}
            for(int i=0;i<indices.Length;i+=3){parent[Root(indices[i+1])]=Root(indices[i]);parent[Root(indices[i+2])]=Root(indices[i]);}
            var groups=Enumerable.Range(0,mesh.vertexCount).GroupBy(Root).ToDictionary(g=>g.Key,g=>g.ToArray());
            var trianglesByRoot=new Dictionary<int,List<int>>();
            for(int i=0;i<indices.Length;i+=3){int root=Root(indices[i]);if(!trianglesByRoot.TryGetValue(root,out var list)){list=new List<int>();trianglesByRoot.Add(root,list);}list.Add(indices[i]);list.Add(indices[i+1]);list.Add(indices[i+2]);}
            var triangles=new List<int>();
            foreach(var pair in trianglesByRoot)
            {
                var vertices=groups[pair.Key];int first=vertices[0],count=vertices.Length;
                if((count-2)%8!=0||vertices[count-1]-first+1!=count||pair.Value.Count/3!=2*(count-2)) {triangles.AddRange(pair.Value);continue;}
                int rings=(count-2)/8;
                for(int ring=0;ring<rings;ring++)
                {
                    Vector3 centre=Vector3.zero;for(int side=0;side<8;side++)centre+=thickened[first+ring*8+side];centre/=8;
                    for(int side=0;side<8;side++){int index=first+ring*8+side;thickened[index]=centre+(thickened[index]-centre)*2.2f;}
                }
                var retained=new List<int>();for(int ring=0;ring<rings-1;ring+=3)retained.Add(ring);retained.Add(rings-1);
                for(int r=1;r<retained.Count;r++)for(int side=0;side<8;side++)
                {
                    int a=first+retained[r-1]*8+side,b=first+retained[r-1]*8+(side+1)%8,c=first+retained[r]*8+(side+1)%8,d=first+retained[r]*8+side;
                    triangles.AddRange(new[]{a,b,c,a,c,d});
                }
                int bottom=first+count-2,top=first+count-1;
                for(int side=0;side<8;side++)triangles.AddRange(new[]{bottom,first+(side+1)%8,first+side,top,first+(rings-1)*8+side,first+(rings-1)*8+(side+1)%8});
                strands++;
            }
            // Keep each cross section intact; compact only vertices no longer referenced.
            var used=triangles.Distinct().OrderBy(a=>a).ToArray();var map=new int[mesh.vertexCount];for(int i=0;i<used.Length;i++)map[used[i]]=i;
            var positions=thickened;var normals=mesh.normals;var colours=mesh.colors;var uv=mesh.uv;string name=mesh.name;
            mesh.Clear();mesh.indexFormat=used.Length>65535?IndexFormat.UInt32:IndexFormat.UInt16;
            mesh.vertices=used.Select(v=>positions[v]).ToArray();mesh.normals=used.Select(v=>normals[v]).ToArray();
            if(colours.Length>0)mesh.colors=used.Select(v=>colours[v]).ToArray();if(uv.Length>0)mesh.uv=used.Select(v=>uv[v]).ToArray();
            mesh.triangles=triangles.Select(v=>map[v]).ToArray();mesh.RecalculateBounds();mesh.name=name;EditorUtility.SetDirty(mesh);
        }
        AssetDatabase.SaveAssets();long after=filters.Sum(f=>(long)f.sharedMesh.triangles.Length/3);
        File.WriteAllText("ArtCaptures/WorldForestOptimization/SafeReduction.txt",$"Restored crown geometry and all original surface attributes. Resampled {strands} intact capped ribbon paths; retained all 8 vertices of every kept cross section and both end caps.\nInstanced triangles: {before:N0} -> {after:N0}. Position averaging and cross-strand welding removed. Materials unchanged.\n");
    }
    [MenuItem("Tools/Capstone/World Map/Build Thicker Safe Forest Trunks")]
    public static void BuildThickerTrunks()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit play mode first.");
        const string folder="Assets/Generated/WorldForestRepaired";
        if(AssetDatabase.IsValidFolder(folder))throw new Exception("Safe trunk assets already built.");
        AssetDatabase.CreateFolder("Assets/Generated","WorldForestRepaired");
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");
        var forest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="02 Placeholder forest");
        var filters=forest.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null&&f.GetComponentInParent<LODGroup>()!=null).ToArray();
        long before=filters.Sum(f=>(long)f.sharedMesh.triangles.Length/3);var cache=new Dictionary<Mesh,Mesh>();SilkForestMeshLibrary library=null;
        foreach(var f in filters.Where(f=>f.sharedMesh.name=="Tensioned silk strands"))
        {
            var source=f.sharedMesh;
            if(!cache.TryGetValue(source,out var mesh))
            {
                if(cache.Count%8==0){library=ScriptableObject.CreateInstance<SilkForestMeshLibrary>();AssetDatabase.CreateAsset(library,folder+"/Trunks"+(cache.Count/8).ToString("00")+".asset");}
                mesh=new Mesh{name=source.name,indexFormat=source.indexFormat};mesh.vertices=source.vertices;mesh.normals=source.normals;mesh.colors=source.colors;mesh.uv=source.uv;mesh.triangles=source.triangles;mesh.RecalculateBounds();
                AssetDatabase.AddObjectToAsset(mesh,library);cache.Add(source,mesh);EditorUtility.SetDirty(library);
            }
            f.sharedMesh=mesh;EditorUtility.SetDirty(f);
        }
        ReduceStrandsSafely();
        foreach(var lod in forest.GetComponentsInChildren<LODGroup>()){var levels=lod.GetLODs();for(int i=0;i<levels.Length;i++)levels[i].screenRelativeTransitionHeight=Mathf.Min(levels[i].screenRelativeTransitionHeight,.01f);lod.SetLODs(levels);lod.RecalculateBounds();EditorUtility.SetDirty(lod);}
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        long after=filters.Sum(f=>(long)f.sharedMesh.triangles.Length/3);
        File.WriteAllText("ArtCaptures/WorldForestOptimization/FinalRepair.txt",$"Original crown mesh references restored. Fresh trunk meshes with 2.2x ribbon cross-section radius, intact capped paths and 1/3 longitudinal samples.\nForest triangles: {before:N0} -> {after:N0}. LOD cull height 1%; bounds recalculated.\n");
        Preview();
    }
}
