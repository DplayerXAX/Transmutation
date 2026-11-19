using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
public static partial class WorldForestUnifiedTrees
{
 [MenuItem("Tools/Capstone/World Map/Preview Unified Vine Tree Connections")]
 static void Preview()
 {
  var scene=SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");var forest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="02 Placeholder forest");var trees=forest.GetComponentsInChildren<CapsuleCollider>(true).Where(c=>c.GetComponentInParent<LODGroup>()!=null).ToArray();
  float Height(CapsuleCollider c)=>c.height*Mathf.Abs(c.transform.lossyScale.y);bool Triple(CapsuleCollider c)=>c.GetComponentsInChildren<MeshFilter>().First(f=>f.name=="Tensioned silk strands").sharedMesh.name.StartsWith("3 ");
  var samples=new[]{trees.OrderBy(Height).First(),trees.Where(c=>!Triple(c)).OrderBy(c=>Mathf.Abs(Height(c)-11)).First(),trees.Where(Triple).OrderBy(c=>Mathf.Abs(Height(c)-14)).First()};
  var preview=new PreviewRenderUtility();bool async=ShaderUtil.allowAsyncCompilation;
  try
  {
   ShaderUtil.allowAsyncCompilation=false;
   for(int i=0;i<samples.Length;i++)foreach(var filter in samples[i].GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name=="Crown membranes"||f.name=="Tensioned silk strands"||f.name=="Tree brown outline"))
   {
    var copy=new GameObject("Unified tree preview",typeof(MeshFilter),typeof(MeshRenderer));copy.transform.SetPositionAndRotation(filter.transform.position-Foot(samples[i])+new Vector3((i-1)*9,0,0),filter.transform.rotation);copy.transform.localScale=filter.transform.lossyScale;copy.GetComponent<MeshFilter>().sharedMesh=filter.sharedMesh;copy.GetComponent<MeshRenderer>().sharedMaterials=filter.GetComponent<MeshRenderer>().sharedMaterials;preview.AddSingleGO(copy);
   }
   preview.camera.fieldOfView=40;preview.camera.nearClipPlane=.1f;preview.camera.farClipPlane=180;preview.camera.transform.position=new Vector3(15,11,37);preview.camera.transform.LookAt(new Vector3(0,7,0));preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.055f,.065f,.08f);preview.lights[0].intensity=1.5f;preview.lights[0].transform.rotation=Quaternion.Euler(30,-35,0);preview.ambientColor=new Color(.4f,.43f,.44f);
   preview.BeginPreview(new Rect(0,0,1800,1200),GUIStyle.none);preview.Render(true);var rt=(RenderTexture)preview.EndPreview();var active=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1800,1200,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1800,1200),0,0);image.Apply();File.WriteAllBytes("ArtCaptures/WorldForestUnified/TreeConnections.png",image.EncodeToPNG());RenderTexture.active=active;Object.DestroyImmediate(image);
  }
  finally{preview.Cleanup();ShaderUtil.allowAsyncCompilation=async;}
 }
}
