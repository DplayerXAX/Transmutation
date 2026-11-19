using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
public static class WorldForestTwistedTrees
{
 const string Folder="Assets/Generated/WorldForestTwisted";
 static Mesh Crown(Vector3 foot,float h,bool stack,int scoops,bool low,bool far)
 {
  var b=new SilkForestGeometry.MeshBuffer();int segments=far?10:16,rings=far?6:10;
  void Ball(Vector3 center,float radius,int seed)
  {
   for(int y=0;y<rings;y++)for(int x=0;x<segments;x++)
   {
    // Sparse openings retain the silk crown silhouette without modelling thick pore rims.
    if(y>1&&y<rings-2&&((x*7+y*11+seed)%13==0))continue;
    Vector3 P(int a,int c){float latitude=Mathf.PI*a/rings,longitude=2*Mathf.PI*c/segments;return center+radius*new Vector3(Mathf.Sin(latitude)*Mathf.Cos(longitude),Mathf.Cos(latitude),Mathf.Sin(latitude)*Mathf.Sin(longitude));}
    b.Quad(P(y,x),P(y,x+1),P(y+1,x+1),P(y+1,x),new Color(.82f,.9f,.92f),center);
   }
  }
  float r=h*(stack?.165f:.205f);float top=h-r*(stack?.8f:1);
  for(int i=0;i<(stack?scoops:1);i++){float size=r*Mathf.Lerp(1,.8f,stack?i/(float)(scoops-1):0);float cy=stack?top-(scoops-1-i)*r*1.45f:top;Ball(foot+Vector3.up*cy,size,17+i);}
  if(low){Ball(foot+new Vector3(h*.045f,h*.31f,0),h*.07f,4);Ball(foot+new Vector3(-h*.045f,h*.48f,0),h*.065f,7);}
  return b.CreateMesh("Low polygon silk crown");
 }
 static Mesh Trunk(Vector3 foot,float h,bool stack,int scoops,bool low,bool far)
 {
  var b=new SilkForestGeometry.MeshBuffer();int steps=far?9:17,sides=far?4:6;float end=stack?h-h*.165f*.8f-(scoops-1)*h*.165f*1.45f:h*.80f;
  for(int strand=0;strand<4;strand++)
  {
   bool thick=strand<2;int samples=thick?steps:far?6:11;var path=new Vector3[samples];float orbit=h*(thick?.019f:.037f);
   for(int i=0;i<samples;i++){float t=i/(float)(samples-1),a=t*Mathf.PI*2*1.35f+strand*Mathf.PI;path[i]=foot+new Vector3(Mathf.Cos(a)*orbit*Mathf.Sin(t*Mathf.PI*.9f),end*t,Mathf.Sin(a)*orbit*Mathf.Sin(t*Mathf.PI*.9f));}
   b.Tube(path,h*(thick?.018f:.0045f),thick?sides:3,new Color(.82f,.9f,.92f),false);
  }
  if(!far)for(int i=0;i<4;i++){float a=i*Mathf.PI*.5f; b.Tube(new[]{foot+Vector3.up*h*.07f,foot+new Vector3(Mathf.Cos(a)*h*.08f,0,Mathf.Sin(a)*h*.08f)},h*.01f,4,new Color(.82f,.9f,.92f),true);}
  if(low)foreach(float level in new[]{.31f,.48f})b.Tube(new[]{foot+Vector3.up*h*(level-.055f),foot+new Vector3(h*(level<.4f?.045f:-.045f),h*level,0)},h*.009f,4,new Color(.82f,.9f,.92f),true);
  return b.CreateMesh("Two thick winding strands and two fine threads");
 }
 [MenuItem("Tools/Capstone/World Map/Apply Twisted Trees And Player Height Distribution")]
 public static void Apply()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play first");if(AssetDatabase.IsValidFolder(Folder))throw new Exception("Already applied");
  var scene=SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");var forest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="02 Placeholder forest");
  var player=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<SmoothFirstPersonController>(true)).First();var body=player.GetComponent<CapsuleCollider>();float playerHeight=body.height*Mathf.Abs(body.transform.lossyScale.y);
  var trees=forest.GetComponentsInChildren<CapsuleCollider>(true).Where(c=>c.GetComponentInParent<LODGroup>()!=null).ToArray();AssetDatabase.CreateFolder("Assets/Generated","WorldForestTwisted");var library=ScriptableObject.CreateInstance<SilkForestMeshLibrary>();AssetDatabase.CreateAsset(library,Folder+"/Meshes.asset");var cache=new Dictionary<(int,bool,int,bool,bool),(Mesh,Mesh)>();var random=new System.Random(713);long near=0,far=0;int stacked=0,central=0;float min=100,max=0;int maxFaces=0;
  for(int index=0;index<trees.Length;index++)
  {
   var tree=trees[index];var group=tree.GetComponentInParent<LODGroup>();foreach(var old in group.transform.Cast<Transform>().Where(t=>t.name=="Budget distant meshes").ToArray())Object.DestroyImmediate(old.gameObject);
   float ratio=index==0?2:index==1?5:index%10<8?3+(float)random.NextDouble():index%2==0?2+(float)random.NextDouble():4+(float)random.NextDouble();float target=playerHeight*ratio;
   var foot=tree.center-Vector3.up*tree.height*.5f;var worldFoot=tree.transform.TransformPoint(foot);float scale=target/(tree.height*Mathf.Abs(tree.transform.lossyScale.y));tree.transform.localScale*=scale;tree.transform.position+=worldFoot-tree.transform.TransformPoint(foot);
   bool low=tree.GetComponent<SquirrelTree>()!=null;var filters=tree.GetComponentsInChildren<MeshFilter>(true);var crown=filters.First(f=>f.name=="Crown membranes");var trunk=filters.First(f=>f.name=="Tensioned silk strands");bool stack=!low&&ratio>=3.7f&&(crown.sharedMesh.name.Contains("overlapping")||index%3==0);int scoops=ratio>=4.35f?3:2;if(stack)stacked++;if(ratio>=3&&ratio<=4)central++;min=Mathf.Min(min,ratio);max=Mathf.Max(max,ratio);
   var holder=new GameObject("Budget distant meshes").transform;holder.SetParent(group.transform,false);var close=new List<Renderer>();var distant=new List<Renderer>();int faces=0;
   foreach(bool isFar in new[]{false,true})
   {
    var key=(Mathf.RoundToInt(tree.height*1000),stack,scoops,low,isFar);
    if(!cache.TryGetValue(key,out var meshes)){meshes=(Crown(foot,tree.height,stack,scoops,low,isFar),Trunk(foot,tree.height,stack,scoops,low,isFar));AssetDatabase.AddObjectToAsset(meshes.Item1,library);AssetDatabase.AddObjectToAsset(meshes.Item2,library);cache.Add(key,meshes);}
    int count=(meshes.Item1.triangles.Length+meshes.Item2.triangles.Length)/3;if(isFar)far+=count;else{near+=count;faces=count;}
    for(int part=0;part<2;part++)
    {
     var original=part==0?crown:trunk;var mesh=part==0?meshes.Item1:meshes.Item2;
     if(!isFar){original.sharedMesh=mesh;var r=original.GetComponent<MeshRenderer>();r.enabled=true;close.Add(r);EditorUtility.SetDirty(original);}
     else{var copy=new GameObject(original.name+" distant");copy.transform.SetParent(original.transform,false);copy.AddComponent<MeshFilter>().sharedMesh=mesh;var r=copy.AddComponent<MeshRenderer>();r.sharedMaterials=original.GetComponent<MeshRenderer>().sharedMaterials;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;distant.Add(r);copy.transform.SetParent(holder,true);}
    }
   }
   maxFaces=Math.Max(maxFaces,faces);foreach(var filter in filters)if(filter!=crown&&filter!=trunk){filter.sharedMesh=null;var r=filter.GetComponent<Renderer>();if(r!=null)r.enabled=false;EditorUtility.SetDirty(filter);}
   foreach(var anchor in tree.GetComponentsInChildren<Transform>().Where(t=>t.name=="Small bee hive anchor - bare trunk")){var local=tree.transform.InverseTransformPoint(anchor.position)-foot;var dir=Vector3.ProjectOnPlane(local,Vector3.up).normalized;anchor.position=worldFoot+Vector3.up*Mathf.Clamp(local.y*Mathf.Abs(tree.transform.lossyScale.y),.7f,1.4f)+tree.transform.TransformDirection(dir)*(.14f+target*.035f);}
   group.enabled=true;group.SetLODs(new[]{new LOD(.09f,close.ToArray()),new LOD(.012f,distant.ToArray())});group.RecalculateBounds();EditorUtility.SetDirty(group);EditorUtility.SetDirty(tree.transform);
  }
  EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Directory.CreateDirectory("ArtCaptures/WorldForestTwisted");File.WriteAllText("ArtCaptures/WorldForestTwisted/Report.txt",$"Player height {playerHeight:F2}m; {trees.Length} trees; range {min:F2}-{max:F2}x player; {central} between 3-4x; {stacked} tall stacked trees; short trees single crowns. Near triangles {near:N0}; far triangles {far:N0}; maximum near tree {maxFaces}. Two thick winding strands, two fine strands, no straight central pillar.\n");
 }
}
