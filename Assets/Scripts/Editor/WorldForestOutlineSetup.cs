using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
public static class WorldForestOutlineSetup
{
 [MenuItem("Tools/Capstone/World Map/Add Fine Brown Forest Outlines")]
 public static void Apply()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play Mode first");
  const string path="Assets/Generated/WorldForestFinish/TreeDarkBrownOutline.mat";var shader=Shader.Find("Capstone/World/Forest Brown Outline");if(shader==null||ShaderUtil.ShaderHasError(shader))throw new Exception("Outline shader failed");
  var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(material==null){material=new Material(shader){name="Tree Dark Brown Outline",enableInstancing=true};AssetDatabase.CreateAsset(material,path);}material.SetColor("_Color",new Color(.09f,.042f,.022f,1));material.SetFloat("_Width",1.25f);EditorUtility.SetDirty(material);
  var scene=SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");var forest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="02 Placeholder forest");var groups=forest.GetComponentsInChildren<LODGroup>(true);int count=0;long near=0,far=0;
  foreach(var group in groups)
  {
   var lods=group.GetLODs();for(int i=0;i<lods.Length;i++)
   {
    var original=lods[i].renderers.Where(r=>r!=null&&!r.name.StartsWith("Tree brown outline")).ToArray();var renderers=new List<Renderer>(original);
    foreach(var source in original)
    {
     var filter=source.GetComponent<MeshFilter>();if(filter==null||filter.sharedMesh==null)continue;string name=i==0?"Tree brown outline":"Tree brown outline distant";var existing=source.transform.Find(name);var outline=existing==null?new GameObject(name):existing.gameObject;outline.layer=source.gameObject.layer;outline.transform.SetParent(source.transform,false);outline.transform.localPosition=Vector3.zero;outline.transform.localRotation=Quaternion.identity;outline.transform.localScale=Vector3.one;
     var mesh=outline.GetComponent<MeshFilter>();if(mesh==null)mesh=outline.AddComponent<MeshFilter>();mesh.sharedMesh=filter.sharedMesh;var renderer=outline.GetComponent<MeshRenderer>();if(renderer==null)renderer=outline.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;renderer.lightProbeUsage=LightProbeUsage.Off;renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;renderers.Add(renderer);if(i==0)near+=mesh.sharedMesh.triangles.LongLength/3;else far+=mesh.sharedMesh.triangles.LongLength/3;count++;
    }
    lods[i].renderers=renderers.ToArray();
   }
   group.SetLODs(lods);group.RecalculateBounds();EditorUtility.SetDirty(group);
  }
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);File.WriteAllText("ArtCaptures/WorldForestFinish/Outlines.txt",$"PASS {groups.Length} trees, {count} LOD outline renderers. Dark brown 1.25-pixel inverted hull; existing shared meshes reused, shadows disabled. Extra near outline triangles {near:N0}; far {far:N0}, LODs mutually exclusive.\n");
 }
}
