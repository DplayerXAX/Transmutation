using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;
public static class WorldForestMaterialPreview
{
 [MenuItem("Tools/Capstone/World Map/Preview Forest Squirrel Materials")]
 static void Render()
 {
  var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Generated/WorldForestFinish/WorldForestSquirrel.prefab");var original=prefab.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name=="Squirrel Visual");var preview=new PreviewRenderUtility();bool async=ShaderUtil.allowAsyncCompilation;
  try
  {
   ShaderUtil.allowAsyncCompilation=false;
   for(int i=0;i<2;i++)
   {
    var copy=new GameObject("Squirrel material preview",typeof(MeshFilter),typeof(MeshRenderer));copy.GetComponent<MeshFilter>().sharedMesh=original.sharedMesh;var r=copy.GetComponent<MeshRenderer>();r.sharedMaterials=original.GetComponent<Renderer>().sharedMaterials;copy.transform.localScale=original.transform.lossyScale;copy.transform.rotation=Quaternion.Euler(0,i==0?-35:145,0);copy.transform.position=new Vector3((i==0?-1:1)*1.0f,0,0)-r.bounds.center;var block=new MaterialPropertyBlock();block.SetVector("_Center",r.bounds.center);block.SetFloat("_Activity",.2f);r.SetPropertyBlock(block);preview.AddSingleGO(copy);
   }
   preview.camera.fieldOfView=34;preview.camera.nearClipPlane=.05f;preview.camera.farClipPlane=80;preview.camera.transform.position=new Vector3(0,.7f,4.5f);preview.camera.transform.LookAt(Vector3.zero);preview.camera.clearFlags=CameraClearFlags.SolidColor;preview.camera.backgroundColor=new Color(.07f,.08f,.09f);preview.lights[0].intensity=1.5f;preview.lights[0].transform.rotation=Quaternion.Euler(35,-35,0);preview.ambientColor=new Color(.4f,.42f,.43f);
   preview.BeginPreview(new Rect(0,0,1500,1000),GUIStyle.none);preview.Render(true);var rt=(RenderTexture)preview.EndPreview();var active=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1500,1000,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1500,1000),0,0);image.Apply();File.WriteAllBytes("ArtCaptures/WorldForestFinish/SquirrelMaterials.png",image.EncodeToPNG());RenderTexture.active=active;Object.DestroyImmediate(image);
  }
  finally{preview.Cleanup();ShaderUtil.allowAsyncCompilation=async;}
 }
}
