using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
public static class WorldForestRestoreCrowns
{
 const string Folder="Assets/Generated/WorldForestRestoredCrowns";
 static Mesh Surface(Mesh source,bool far)
 {
  var p=source.vertices;var n=source.normals;var colors=source.colors;var uv=source.uv;var triangles=source.triangles;var parent=Enumerable.Range(0,p.Length).ToArray();int Root(int a){while(parent[a]!=a){parent[a]=parent[parent[a]];a=parent[a];}return a;}
  for(int i=0;i<triangles.Length;i+=3){parent[Root(triangles[i+1])]=Root(triangles[i]);parent[Root(triangles[i+2])]=Root(triangles[i]);}
  var groups=Enumerable.Range(0,p.Length).GroupBy(Root);var b=new SilkForestGeometry.MeshBuffer();int[] angles=far?new[]{0,4,8,12,16}:new[]{0,2,4,6,8,10,12,14,16};
  foreach(var group in groups)
  {
   var ids=group.OrderBy(i=>i).ToArray();if(ids.Length!=102)throw new Exception("Unexpected crown pore component "+ids.Length+" in "+source.name);
   int start=ids[0];if(Enumerable.Range(0,51).All(i=>Vector3.Dot(n[start],n[start+i])>.998f))continue;int[] inner=new int[angles.Length],outer=new int[angles.Length];
   for(int i=0;i<angles.Length;i++){int a=start+angles[i],c=start+34+angles[i];inner[i]=b.Vertex(p[a],colors.Length>a?colors[a]:Color.white,uv.Length>a?uv[a]:Vector2.zero,n[a]);outer[i]=b.Vertex(p[c],colors.Length>c?colors[c]:Color.white,uv.Length>c?uv[c]:Vector2.zero,n[c]);}
   void Face(int a,int c,int d){if(Vector3.Dot(Vector3.Cross(b.vertices[c]-b.vertices[a],b.vertices[d]-b.vertices[a]),n[start])<0)b.Triangle(a,d,c);else b.Triangle(a,c,d);}
   for(int i=0;i<angles.Length-1;i++){Face(inner[i],outer[i],outer[i+1]);Face(inner[i],outer[i+1],inner[i+1]);}
  }
  return b.CreateMesh(source.name+(far?" restored far surface":" restored near surface"));
 }
 [MenuItem("Tools/Capstone/World Map/Finalize Restored Crown Budget")]
 static void FinalizeBudget()
 {
  var sources=AssetDatabase.FindAssets("",new[]{"Assets/Generated/WorldForestArtPass"}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>p.EndsWith(".asset")).SelectMany(p=>AssetDatabase.LoadAllAssetsAtPath(p).OfType<Mesh>()).Where(m=>m.name=="Crown membranes lower poly broad tall"||m.name.Contains("overlapping silk scoops")).ToArray();
  var scene=SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");var forest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="02 Placeholder forest");var library=AssetDatabase.LoadAssetAtPath<SilkForestMeshLibrary>(Folder+"/Meshes.asset");var cache=new Dictionary<Mesh,Mesh>();
  foreach(var filter in forest.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null&&f.sharedMesh.name.Contains("restored")))
  {
   var old=filter.sharedMesh;if(!cache.TryGetValue(old,out var mesh)){var source=sources.OrderBy(m=>(m.bounds.center-old.bounds.center).sqrMagnitude+(m.bounds.size-old.bounds.size).sqrMagnitude).First();mesh=Surface(source,old.name.Contains("far surface"));AssetDatabase.AddObjectToAsset(mesh,library);cache.Add(old,mesh);}filter.sharedMesh=mesh;EditorUtility.SetDirty(filter);
  }
  long near=0,far=0;foreach(var group in forest.GetComponentsInChildren<LODGroup>(true)){var lods=group.GetLODs();near+=lods[0].renderers.Sum(r=>(long)r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3);far+=lods[1].renderers.Sum(r=>(long)r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3);group.RecalculateBounds();EditorUtility.SetDirty(group);}
  EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.AppendAllText("ArtCaptures/WorldForestRestoredCrowns/Report.txt",$"Final surface budget: near {near:N0}, far {far:N0}. Internal flat curtains removed; authored outer crowns and pore shapes retained.\n");
 }
 [MenuItem("Tools/Capstone/World Map/Restore Organic Crowns And Taller Forest")]
 static void Apply()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play first");if(File.Exists("ArtCaptures/WorldForestRestoredCrowns/Report.txt"))throw new Exception("Already restored");
  var sources=AssetDatabase.FindAssets("",new[]{"Assets/Generated/WorldForestArtPass"}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>p.EndsWith(".asset")).SelectMany(p=>AssetDatabase.LoadAllAssetsAtPath(p).OfType<Mesh>()).Where(m=>m.name=="Crown membranes lower poly broad tall"||m.name.Contains("overlapping silk scoops")).ToArray();
  var scene=SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");var forest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="02 Placeholder forest");var trees=forest.GetComponentsInChildren<CapsuleCollider>(true).Where(c=>c.GetComponentInParent<LODGroup>()!=null).ToArray();
  if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets/Generated","WorldForestRestoredCrowns");var library=AssetDatabase.LoadAssetAtPath<SilkForestMeshLibrary>(Folder+"/Meshes.asset");if(library==null){library=ScriptableObject.CreateInstance<SilkForestMeshLibrary>();AssetDatabase.CreateAsset(library,Folder+"/Meshes.asset");}var cache=new Dictionary<(Mesh,bool),Mesh>();Mesh Save(Mesh source,bool far){var key=(source,far);if(cache.TryGetValue(key,out var m))return m;m=Surface(source,far);AssetDatabase.AddObjectToAsset(m,library);cache.Add(key,m);return m;}
  long nearTotal=0,farTotal=0;float min=1000,max=0;var heights=new List<float>();int stacks=0;
  foreach(var tree in trees)
  {
   var foot=tree.center-Vector3.up*tree.height*.5f;var worldFoot=tree.transform.TransformPoint(foot);float previous=tree.height*Mathf.Abs(tree.transform.lossyScale.y);float target=previous*1.6f;bool stack=previous>=7.4f&&tree.GetComponent<SquirrelTree>()==null;int scoops=previous>=8.7f?3:2;
   // Restore actual authored crown surfaces, including asymmetry and rounded pores.
   var candidates=sources.Where(m=>stack?m.name.StartsWith(scoops+" overlapping"):m.name=="Crown membranes lower poly broad tall");var source=candidates.OrderBy(m=>Vector3.ProjectOnPlane(m.bounds.center-foot,Vector3.up).sqrMagnitude*10+Mathf.Abs(m.bounds.max.y-foot.y-tree.height)*.1f).First();if(stack)stacks++;
   var filters=tree.GetComponentInParent<LODGroup>().GetComponentsInChildren<MeshFilter>(true);var near=filters.First(f=>f.name=="Crown membranes");var far=filters.First(f=>f.name=="Crown membranes distant");near.sharedMesh=Save(source,false);far.sharedMesh=Save(source,true);EditorUtility.SetDirty(near);EditorUtility.SetDirty(far);
   var anchors=tree.GetComponentsInChildren<Transform>().Where(t=>t.name=="Small bee hive anchor - bare trunk").Select(t=>(t,t.position,t.rotation,t.lossyScale)).ToArray();
   float actual=(source.bounds.max.y-foot.y)*Mathf.Abs(tree.transform.lossyScale.y);float factor=target/actual;tree.transform.localScale*=factor;tree.transform.position+=worldFoot-tree.transform.TransformPoint(foot);
   foreach(var distant in filters.Where(f=>f.name.EndsWith(" distant"))){var original=filters.First(f=>f.name==distant.name.Substring(0,distant.name.Length-8));distant.transform.SetPositionAndRotation(original.transform.position,original.transform.rotation);var ws=original.transform.lossyScale;var ps=distant.transform.parent.lossyScale;distant.transform.localScale=new Vector3(ws.x/ps.x,ws.y/ps.y,ws.z/ps.z);EditorUtility.SetDirty(distant.transform);}
   foreach(var tuple in anchors){var anchor=tuple.t;anchor.position=worldFoot+Vector3.up*Mathf.Clamp(tuple.position.y-worldFoot.y,.7f,1.4f)+Vector3.ProjectOnPlane(tuple.position-worldFoot,Vector3.up)*factor;anchor.rotation=tuple.rotation;var scale=anchor.parent.lossyScale;anchor.localScale=new Vector3(tuple.lossyScale.x/scale.x,tuple.lossyScale.y/scale.y,tuple.lossyScale.z/scale.z);EditorUtility.SetDirty(anchor);}
   var group=tree.GetComponentInParent<LODGroup>();group.RecalculateBounds();EditorUtility.SetDirty(group);EditorUtility.SetDirty(tree.transform);heights.Add(target);min=Mathf.Min(min,target);max=Mathf.Max(max,target);
   foreach(var lod in group.GetLODs().Select((l,i)=>(l,i))){long count=lod.l.renderers.Sum(r=>(long)r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3);if(lod.i==0)nearTotal+=count;else farTotal+=count;}
  }
  EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Directory.CreateDirectory("ArtCaptures/WorldForestRestoredCrowns");File.WriteAllText("ArtCaptures/WorldForestRestoredCrowns/Report.txt",$"Restored authored organic crown surfaces and rounded holes. Twisted trunks retained. {trees.Length} trees; rendered heights {min:F2}-{max:F2}m, {heights.Count(h=>h>=9.6f&&h<=12.8f)} between 9.6-12.8m; {stacks} tall stack trees. Near triangles {nearTotal:N0}, far {farTotal:N0}; mutually exclusive LODs. No high-poly originals assigned to renderers.\n");
 }
}
