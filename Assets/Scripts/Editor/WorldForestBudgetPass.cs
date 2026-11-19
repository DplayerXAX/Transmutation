using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
public static class WorldForestBudgetPass
{
 const string Folder="Assets/Generated/WorldForestBudget";
 static Mesh Cluster(Mesh source,int budget)
 {
  var p=source.vertices;var ns=source.normals;var cs=source.colors;var us=source.uv;var ts=source.triangles;Mesh result=null;
  for(int resolution=32;resolution>=2;resolution--)
  {
   var cells=new Dictionary<Vector3Int,int>();var map=new int[p.Length];var points=new List<Vector3>();var normals=new List<Vector3>();var colors=new List<Color>();var uv=new List<Vector2>();var counts=new List<int>();float step=source.bounds.size.maxComponent()/resolution;
   for(int i=0;i<p.Length;i++)
   {
    var q=p[i]/Mathf.Max(step,.00001f);var cell=new Vector3Int(Mathf.RoundToInt(q.x),Mathf.RoundToInt(q.y),Mathf.RoundToInt(q.z));
    if(!cells.TryGetValue(cell,out int at)){at=points.Count;cells.Add(cell,at);points.Add(Vector3.zero);normals.Add(Vector3.zero);colors.Add(Color.clear);uv.Add(Vector2.zero);counts.Add(0);}
    map[i]=at;points[at]+=p[i];normals[at]+=ns.Length>i?ns[i]:Vector3.up;colors[at]+=cs.Length>i?cs[i]:Color.white;uv[at]+=us.Length>i?us[i]:Vector2.zero;counts[at]++;
   }
   var tris=new List<int>();var unique=new HashSet<(int,int,int)>();
   for(int i=0;i<ts.Length;i+=3){int a=map[ts[i]],b=map[ts[i+1]],c=map[ts[i+2]];if(a==b||b==c||c==a)continue;int lo=Math.Min(a,Math.Min(b,c)),hi=Math.Max(a,Math.Max(b,c));if(unique.Add((lo,a+b+c-lo-hi,hi)))tris.AddRange(new[]{a,b,c});}
   if(tris.Count/3>budget&&resolution>2)continue;
   for(int i=0;i<points.Count;i++){points[i]/=counts[i];colors[i]/=counts[i];uv[i]/=counts[i];normals[i]=normals[i].sqrMagnitude>.001f?normals[i].normalized:(points[i]-source.bounds.center).normalized;}
   result=new Mesh{name=source.name+" budget "+budget};result.SetVertices(points);result.SetNormals(normals);result.SetColors(colors);result.SetUVs(0,uv);result.SetTriangles(tris,0);result.RecalculateBounds();break;
  }
  return result;
 }
 static Mesh Trunk(Mesh source,int budget)
 {
  var p=source.vertices;int start=p.Length-70;var points=new List<Vector3>();float radius=0;
  for(int ring=0;ring<7;ring++){var centre=Vector3.zero;for(int s=0;s<10;s++)centre+=p[start+ring*10+s];centre/=10;if(ring==0)radius=Vector3.Distance(centre,p[start]);if(ring==0||ring==3||ring==6)points.Add(centre);}
  var b=new SilkForestGeometry.MeshBuffer();b.Tube(points.ToArray(),radius,budget>100?10:6,new Color(.82f,.9f,.92f),true);
  if(budget>100)for(int i=0;i<5;i++){float a=i*Mathf.PI*2/5;var foot=points[0];var end=foot+new Vector3(Mathf.Cos(a),-.03f,Mathf.Sin(a))*radius*4;b.Tube(new[]{foot+Vector3.up*radius*2,end},radius*.45f,5,new Color(.82f,.9f,.92f),true);}
  return b.CreateMesh(source.name+" budget trunk");
 }
 static float maxComponent(this Vector3 v)=>Mathf.Max(v.x,Mathf.Max(v.y,v.z));
 [MenuItem("Tools/Capstone/World Map/Apply Strict Forest Triangle Budget")]
 public static void Apply()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play first");
  if(AssetDatabase.IsValidFolder(Folder))throw new Exception("Budget pass already applied");
  var scene=SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");var forest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="02 Placeholder forest");
  var groups=forest.GetComponentsInChildren<LODGroup>(true);long before=groups.Sum(g=>g.GetComponentsInChildren<MeshFilter>(true).Sum(f=>f.sharedMesh==null?0L:f.sharedMesh.triangles.LongLength/3));
  AssetDatabase.CreateFolder("Assets/Generated","WorldForestBudget");var library=ScriptableObject.CreateInstance<SilkForestMeshLibrary>();AssetDatabase.CreateAsset(library,Folder+"/Meshes.asset");
  var cache=new Dictionary<(Mesh,int),Mesh>();Mesh Save(Mesh source,int budget){var key=(source,budget);if(cache.TryGetValue(key,out var found))return found;var mesh=source.name.Contains("trunk")||source.name.Contains("strands")?Trunk(source,budget):Cluster(source,budget);AssetDatabase.AddObjectToAsset(mesh,library);cache.Add(key,mesh);return mesh;}
  long near=0,far=0;int max=0;
  foreach(var group in groups)
  {
   foreach(var old in group.transform.Cast<Transform>().Where(t=>t.name=="Budget distant meshes").ToArray())Object.DestroyImmediate(old.gameObject);
   var filters=group.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh!=null).ToArray();var close=new List<Renderer>();var distant=new List<Renderer>();var holder=new GameObject("Budget distant meshes").transform;holder.SetParent(group.transform,false);int count=0;
   foreach(var filter in filters)
   {
    var source=filter.sharedMesh;bool crown=filter.name=="Crown membranes";int budget=crown?1200:filter.name=="Tensioned silk strands"?350:80;
    var mesh=Save(source,budget);filter.sharedMesh=mesh;EditorUtility.SetDirty(filter);var renderer=filter.GetComponent<MeshRenderer>();if(renderer==null)continue;close.Add(renderer);count+=mesh.triangles.Length/3;
    var copy=new GameObject(filter.name+" distant");copy.transform.SetParent(holder,false);copy.transform.SetPositionAndRotation(filter.transform.position,filter.transform.rotation);copy.transform.localScale=filter.transform.lossyScale;var parentScale=holder.lossyScale;copy.transform.localScale=new Vector3(copy.transform.localScale.x/parentScale.x,copy.transform.localScale.y/parentScale.y,copy.transform.localScale.z/parentScale.z);
    var low=Save(source,crown?160:48);copy.AddComponent<MeshFilter>().sharedMesh=low;var r=copy.AddComponent<MeshRenderer>();r.sharedMaterials=renderer.sharedMaterials;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=renderer.receiveShadows;distant.Add(r);far+=low.triangles.Length/3;
   }
   near+=count;max=Math.Max(max,count);group.enabled=true;group.fadeMode=LODFadeMode.None;group.SetLODs(new[]{new LOD(.09f,close.ToArray()),new LOD(.012f,distant.ToArray())});group.RecalculateBounds();EditorUtility.SetDirty(group);
  }
  EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
  Directory.CreateDirectory("ArtCaptures/WorldForestBudget");File.WriteAllText("ArtCaptures/WorldForestBudget/Report.txt",$"Trees {groups.Length}; previous summed triangles {before:N0}; near LOD total {near:N0}; far LOD total {far:N0}; maximum near tree {max:N0}. Reduction {100.0*(before-near)/before:F2}%. Near/far transition 9% screen height; cull 1.2%. LODs are mutually exclusive.\n");Debug.Log("Strict forest budget complete "+near+" near / "+far+" far");
 }
}
