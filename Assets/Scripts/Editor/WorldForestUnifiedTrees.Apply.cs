using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
public static partial class WorldForestUnifiedTrees
{
 sealed class Shape
 {
  public Mesh crown,farCrown;public List<Ball> balls;public Vector3 foot;public float height,radius;public Ball primary;
 }
 static Shape Normalize(Mesh crown,Mesh farCrown,Vector3 foot,float height,Func<Mesh,Mesh> save)
 {
  var balls=Balls(crown);if(balls.Count==0)throw new Exception("No authored crown sockets in "+crown.name);
  float radius=balls.Max(b=>b.radius);float scale=Mathf.Min(height*.16f/radius,height*.76f/crown.bounds.size.y);var main=balls.OrderByDescending(b=>b.radius).First();var offset=foot+Vector3.up*(height-(crown.bounds.max.y-main.center.y)*scale);
  Vector3 Map(Vector3 p)=>offset+(p-main.center)*scale;
  Mesh Copy(Mesh source){var m=new Mesh{name=source.name+" unified proportions"};m.vertices=source.vertices.Select(Map).ToArray();m.normals=source.normals;m.colors=source.colors;m.uv=source.uv;m.triangles=source.triangles;m.RecalculateBounds();return save(m);}
  foreach(var ball in balls){ball.center=Map(ball.center);ball.radius*=scale;}
  return new Shape{crown=Copy(crown),farCrown=Copy(farCrown),balls=balls,foot=foot,height=height,radius=radius*scale,primary=balls.Where(b=>b.radius>=radius*scale*.63f).OrderByDescending(b=>b.center.y).First()};
 }
 static Vector3 Vine(Shape shape,int strand,int count,float t,Vector3 toe)
 {
  float phase=strand*Mathf.PI*2/count;float angle=phase+t*Mathf.PI*2*1.45f;float orbit=shape.radius*.095f;
  var end=shape.primary.center-Vector3.up*shape.primary.radius*.35f;var core=Vector3.Lerp(shape.foot,end,t);var p=core+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*orbit;
  float spread=1-Mathf.SmoothStep(0,1,Mathf.Clamp01(t/.055f));var start=shape.foot+new Vector3(Mathf.Cos(phase),0,Mathf.Sin(phase))*orbit;p+=(toe-start)*spread;
  return p;
 }
 static Mesh Vines(Shape shape,int count,bool far,Vector3[] toes,out float maxSocketError)
 {
  var b=new SilkForestGeometry.MeshBuffer();int samples=far?12:25,sides=far?4:6;var tint=new Color(.82f,.9f,.92f);maxSocketError=0;
  for(int strand=0;strand<count;strand++)
  {
   var path=Enumerable.Range(0,samples).Select(i=>i/(float)(samples-1)).Concat(new[]{.012f,.026f,.043f,.055f}).Distinct().OrderBy(t=>t).Select(t=>Vine(shape,strand,count,t,toes[strand])).ToArray();b.Tube(path,shape.radius*.052f,sides,tint,false);
   maxSocketError=Mathf.Max(maxSocketError,Vector3.Distance(path[path.Length-1],shape.primary.center)/shape.primary.radius);
  }
  // Small fibres follow the vines; they have separate tips, with no ring or tied root junction.
  for(int strand=0;strand<(far?1:2);strand++)
  {int at=strand%count;var path=Enumerable.Range(0,far?8:15).Select(i=>{float t=i/(float)((far?8:15)-1);return Vine(shape,at,count,t,toes[at])+new Vector3(Mathf.Cos(t*21+strand),0,Mathf.Sin(t*21+strand))*shape.radius*.045f;}).ToArray();b.Tube(path,shape.radius*.011f,3,tint,false);}
  foreach(var ball in shape.balls.Where(ball=>ball!=shape.primary))
  {
   float t=Mathf.Clamp01((ball.center.y-shape.foot.y)/(shape.primary.center.y-shape.foot.y));var nearest=Vine(shape,0,count,t,toes[0]);
   if(Vector3.Distance(nearest,ball.center)<ball.radius*.65f)continue;
   var end=ball.center-Vector3.up*ball.radius*.32f;float begin=Mathf.Clamp01(t-.11f);int branches=far?1:2;
   for(int branch=0;branch<branches;branch++)
   {
    var start=Vine(shape,branch,count,begin,toes[branch]);var control=new Vector3(start.x,Mathf.Lerp(start.y,end.y,.65f),start.z);var path=Enumerable.Range(0,far?5:9).Select(i=>{float u=i/(float)((far?5:9)-1);return (1-u)*(1-u)*start+2*(1-u)*u*control+u*u*end;}).ToArray();b.Tube(path,shape.radius*.038f,far?3:5,tint,true);
   }
   maxSocketError=Mathf.Max(maxSocketError,Vector3.Distance(end,ball.center)/ball.radius);
  }
  return b.CreateMesh(count+" continuous winding vines with independent spreading ends");
 }
 static Vector3 Foot(CapsuleCollider tree)=>tree.transform.TransformPoint(tree.center-Vector3.up*tree.height*.5f);
 [MenuItem("Tools/Capstone/World Map/Unify Connected Vine Trees And Thin Forest")]
 static void Apply()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play Mode first");if(AssetDatabase.IsValidFolder(Folder))throw new Exception("Unified forest already applied");
  var scene=SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");var forest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="02 Placeholder forest");var world=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ProceduralWorld>(true)).First();if(!world.LayoutReady)WorldMapBuild.PrepareSampler(world);
  var trees=forest.GetComponentsInChildren<CapsuleCollider>(true).Where(c=>c.GetComponentInParent<LODGroup>()!=null).ToArray();
  bool Hive(CapsuleCollider c)=>c.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="Small bee hive anchor - bare trunk");
  var kept=new HashSet<CapsuleCollider>();var points=new List<Vector3>();var random=new System.Random(2237);var ordered=trees.Select(t=>(tree:t,order:random.Next())).OrderByDescending(t=>Hive(t.tree)).ThenByDescending(t=>t.tree.GetComponent<SquirrelTree>()!=null).ThenBy(t=>t.order).Select(t=>t.tree);
  foreach(var tree in ordered){var at=Foot(tree);if(Hive(tree)||points.All(p=>Vector3.ProjectOnPlane(p-at,Vector3.up).sqrMagnitude>=4.8f*4.8f)){kept.Add(tree);points.Add(at);}}
  // Filter every serialized ecosystem reference before removing a tree.
  foreach(var habitat in forest.GetComponentsInChildren<WorldForestHabitat>(true))
  {
   var roots=habitat.treeRoots.Where(r=>r!=null&&kept.Contains(r.GetComponent<CapsuleCollider>())).ToArray();if(roots.Length<12)throw new Exception("Too few retained low-crown roots");habitat.treeRoots=roots;EditorUtility.SetDirty(habitat);
   var config=new SerializedObject(habitat.squirrels);var refs=config.FindProperty("trees");refs.arraySize=roots.Length;for(int i=0;i<roots.Length;i++)refs.GetArrayElementAtIndex(i).objectReferenceValue=roots[i];config.ApplyModifiedPropertiesWithoutUndo();
  }
  foreach(var tree in trees.Where(t=>!kept.Contains(t)))Object.DestroyImmediate(tree.GetComponentInParent<LODGroup>().gameObject);
  AssetDatabase.CreateFolder("Assets/Generated","WorldForestUnified");SilkForestMeshLibrary library=null;int assetCount=0;
  Mesh Save(Mesh mesh){if(assetCount%48==0){library=ScriptableObject.CreateInstance<SilkForestMeshLibrary>();AssetDatabase.CreateAsset(library,Folder+"/Meshes"+(assetCount/48).ToString("000")+".asset");}AssetDatabase.AddObjectToAsset(mesh,library);EditorUtility.SetDirty(library);assetCount++;return mesh;}
  var shapes=new Dictionary<(Mesh,Vector3,float),Shape>();long near=0,far=0;int doubles=0,triples=0,connections=0;float error=0,minHeight=1000,maxHeight=0;var report=new List<string>();
  int index=0;foreach(var tree in trees.Where(kept.Contains))
  {
   var group=tree.GetComponentInParent<LODGroup>();var filters=group.GetComponentsInChildren<MeshFilter>(true);var crown=filters.First(f=>f.name=="Crown membranes");var farCrown=filters.First(f=>f.name=="Crown membranes distant");var trunk=filters.First(f=>f.name=="Tensioned silk strands");var farTrunk=filters.First(f=>f.name=="Tensioned silk strands distant");
   var localFoot=tree.center-Vector3.up*tree.height*.5f;var worldFoot=Foot(tree);float localHeight=crown.sharedMesh.bounds.max.y-localFoot.y;var key=(crown.sharedMesh,localFoot,localHeight);
   if(!shapes.TryGetValue(key,out var shape)){shape=Normalize(crown.sharedMesh,farCrown.sharedMesh,localFoot,localHeight,Save);shapes.Add(key,shape);}
   crown.sharedMesh=shape.crown;farCrown.sharedMesh=shape.farCrown;int count=(index++%3==0)?3:2;if(count==2)doubles++;else triples++;
   var toes=new Vector3[count];for(int strand=0;strand<count;strand++)
   {
    float phase=strand*Mathf.PI*2/count;var local=localFoot+new Vector3(Mathf.Cos(phase),0,Mathf.Sin(phase))*shape.radius*.45f;var at=tree.transform.TransformPoint(local);
    if(world.TryFindSurfaceBelow(new Vector3(at.x,worldFoot.y+3,at.z),out float y)&&Mathf.Abs(y-worldFoot.y)<2)at.y=y+.015f;else at.y=worldFoot.y;
    toes[strand]=tree.transform.InverseTransformPoint(at);
   }
   trunk.sharedMesh=Save(Vines(shape,count,false,toes,out float socketError));farTrunk.sharedMesh=Save(Vines(shape,count,true,toes,out float farError));error=Mathf.Max(error,Mathf.Max(socketError,farError));connections+=shape.balls.Count;
   if(socketError>=.8f||farError>=.8f)throw new Exception("Vine endpoint does not enter crown");
   foreach(var pair in new[]{(near:crown,far:farCrown),(near:trunk,far:farTrunk)})
   {
    pair.far.transform.SetPositionAndRotation(pair.near.transform.position,pair.near.transform.rotation);var ws=pair.near.transform.lossyScale;var ps=pair.far.transform.parent.lossyScale;pair.far.transform.localScale=new Vector3(ws.x/ps.x,ws.y/ps.y,ws.z/ps.z);EditorUtility.SetDirty(pair.near);EditorUtility.SetDirty(pair.far);EditorUtility.SetDirty(pair.far.transform);
   }
   tree.height=shape.height;tree.center=localFoot+Vector3.up*shape.height*.5f;tree.radius=shape.radius*.16f;EditorUtility.SetDirty(tree);var root=tree.GetComponent<SquirrelTree>();if(root!=null){root.SetRootPoint(localFoot);EditorUtility.SetDirty(root);}
   foreach(var anchor in tree.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Small bee hive anchor - bare trunk"))
   {
    float y=Mathf.Clamp(anchor.position.y-worldFoot.y,.7f,1.4f)/Mathf.Abs(tree.transform.lossyScale.y);float t=Mathf.Clamp01(y/(shape.primary.center.y-localFoot.y));var onVine=Vine(shape,0,count,t,toes[0]);var facing=Vector3.ProjectOnPlane(anchor.forward,Vector3.up).normalized;anchor.position=tree.transform.TransformPoint(onVine)+facing*(shape.radius*.052f*Mathf.Abs(tree.transform.lossyScale.x)+.11f);EditorUtility.SetDirty(anchor);
   }
   group.RecalculateBounds();EditorUtility.SetDirty(group);var lods=group.GetLODs();near+=lods[0].renderers.Sum(r=>(long)r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3);far+=lods[1].renderers.Sum(r=>(long)r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3);float h=shape.height*Mathf.Abs(tree.transform.lossyScale.y);minHeight=Mathf.Min(minHeight,h);maxHeight=Mathf.Max(maxHeight,h);
  }
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Directory.CreateDirectory("ArtCaptures/WorldForestUnified");File.WriteAllText("ArtCaptures/WorldForestUnified/Report.txt",$"Trees {trees.Length} -> {kept.Count}, removed {trees.Length-kept.Count} ({100.0*(trees.Length-kept.Count)/trees.Length:F1}%). 4.8m root spacing, occupied hive trees protected. Two-vine trees {doubles}; three-vine trees {triples}. Crown connections {connections}; maximum endpoint distance / crown radius {error:F3} (<1 means inside). Height range {minHeight:F2}-{maxHeight:F2}m. Crown radius <=16% tree height; vine radius 5.2% main crown radius. Independent spreading vine ends sampled to terrain; no extra root fork or common tied foot. Near triangles {near:N0}; far {far:N0}.\n");
 }
}
