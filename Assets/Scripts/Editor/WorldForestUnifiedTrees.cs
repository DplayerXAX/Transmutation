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
 const string Folder="Assets/Generated/WorldForestUnified";
 sealed class Ball {public Vector3 center;public float radius;public int cells;}
 static List<Ball> Balls(Mesh mesh)
 {
  var p=mesh.vertices;var n=mesh.normals;var raw=new List<Ball>();
  // Restored near surfaces contain one 18-vertex annulus per authored pore.
  for(int start=0;start+17<p.Length;start+=18)
  {
   Vector3 meanP=Vector3.zero,meanN=Vector3.zero;for(int i=1;i<18;i+=2){meanP+=p[start+i];meanN+=n[start+i];}meanP/=9;meanN/=9;float numerator=0,denominator=0;
   for(int i=1;i<18;i+=2){numerator+=Vector3.Dot(p[start+i]-meanP,n[start+i]-meanN);denominator+=(n[start+i]-meanN).sqrMagnitude;}
   if(denominator<.005f)continue;float r=numerator/denominator;if(r<=0||r>mesh.bounds.size.magnitude)continue;raw.Add(new Ball{center=meanP-meanN*r,radius=r,cells=1});
  }
  var groups=new List<Ball>();foreach(var ball in raw.OrderByDescending(b=>b.radius))
  {var other=groups.FirstOrDefault(b=>Vector3.Distance(b.center,ball.center)<Mathf.Max(b.radius,ball.radius)*.4f&&Mathf.Abs(b.radius-ball.radius)<Mathf.Max(b.radius,ball.radius)*.4f);if(other==null)groups.Add(ball);else{other.center=(other.center*other.cells+ball.center)/(other.cells+1);other.radius=(other.radius*other.cells+ball.radius)/(other.cells+1);other.cells++;}}
  return groups.Where(b=>b.cells>=8).OrderByDescending(b=>b.radius).ToList();
 }
 [MenuItem("Tools/Capstone/World Map/Inspect Unified Crown Sockets")]
 static void Inspect()
 {
  var scene=SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");var filters=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshFilter>(true)).Where(f=>f.name=="Crown membranes"&&f.sharedMesh!=null&&f.sharedMesh.name.Contains("restored")).Select(f=>f.sharedMesh).Distinct().ToArray();
  File.WriteAllLines("Temp/UnifiedCrownSockets.txt",filters.Select(m=>m.name+" bounds="+m.bounds+" spheres="+string.Join("; ",Balls(m).Select(b=>$"{b.center} r={b.radius:F2} cells={b.cells}"))));
 }
}
