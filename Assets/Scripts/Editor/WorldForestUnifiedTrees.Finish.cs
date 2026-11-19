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
 const string FinishFolder="Assets/Generated/WorldForestFinish";
 static Material PlantMaterial(string name,Color body,Color shadow,bool crown)
 {
  var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/World/MAT_Decor_Organic.mat");var material=new Material(source){name=name,enableInstancing=true};
  material.SetColor("_Color",body);material.SetColor("_ShadowColor",shadow);material.SetColor("_LineColor",new Color(.035f,.055f,.04f));material.SetColor("_LineColor2",new Color(.06f,.08f,.06f));material.SetColor("_HatchColor",new Color(.13f,.17f,.13f));material.SetColor("_DotColor",new Color(.1f,.14f,.11f));
  material.SetColor("_GlitchColorA",new Color(.35f,.51f,.43f));material.SetColor("_GlitchColorB",new Color(.49f,.36f,.44f));
  material.SetFloat("_HatchStrength",crown?.42f:.32f);material.SetFloat("_HatchScale",crown?7:12);material.SetFloat("_HatchWidth",.65f);material.SetFloat("_DotChance",.18f);material.SetFloat("_GridChance",.05f);material.SetFloat("_GlitchAmount",.18f);material.SetFloat("_GlitchTint",.2f);material.SetFloat("_GlitchShift",0);material.SetFloat("_LineWidth",.65f);material.SetFloat("_RimStrength",.18f);material.SetFloat("_RingCount",0);material.SetFloat("_StrataFrequency",crown?2.6f:4.5f);material.SetFloat("_RingSpeed",0);material.SetFloat("_SwayAmount",0);material.SetFloat("_BreathAmount",0);material.SetFloat("_FakeLightAmount",.32f);material.SetFloat("_MainLightAmount",1);material.SetFloat("_ShadowLineDim",.65f);
  AssetDatabase.CreateAsset(material,FinishFolder+"/"+name+".mat");return material;
 }
 static Material CreatureMaterial(string name,Color color)
 {
  var source=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Creatures/MAT_Octopus_Membrane.mat");var material=new Material(source){name=name,enableInstancing=true};material.SetColor("_BaseColor",color);material.SetColor("_LineColor",new Color(.77f,.86f,.77f));material.SetColor("_InnerColor",color*.65f);material.SetFloat("_Cull",2);material.SetFloat("_TearAmount",0);material.SetFloat("_TearLip",.001f);material.SetFloat("_RingFrequency",10);material.SetFloat("_RingSpeed",.12f);material.SetFloat("_RingKeep",.65f);material.SetFloat("_LineOpacity",.55f);material.SetFloat("_LineWidth",.65f);material.SetFloat("_RimBrightness",.2f);material.SetFloat("_HighlightSize",.012f);material.SetFloat("_ShadeStrength",.28f);AssetDatabase.CreateAsset(material,FinishFolder+"/"+name+".mat");return material;
 }
 [MenuItem("Tools/Capstone/World Map/Tune Forest Plant Material Readability")]
 static void TunePlantMaterials()
 {
  foreach(bool crown in new[]{true,false})
  {
   var m=AssetDatabase.LoadAssetAtPath<Material>(FinishFolder+"/"+(crown?"ForestCrown_OrganicInk":"ForestVines_OrganicInk")+".mat");m.SetColor("_Color",crown?new Color(.78f,.81f,.72f):new Color(.62f,.68f,.56f));m.SetColor("_ShadowColor",crown?new Color(.43f,.48f,.39f):new Color(.30f,.36f,.27f));m.SetColor("_HatchColor",crown?new Color(.25f,.30f,.23f):new Color(.18f,.24f,.17f));m.SetFloat("_HatchStrength",.25f);m.SetFloat("_DotChance",0);m.SetFloat("_GridChance",0);m.SetFloat("_StrataFrequency",crown?.9f:1.7f);m.SetFloat("_LineWidth",.4f);m.SetFloat("_GlitchAmount",.12f);m.SetFloat("_RimStrength",.1f);EditorUtility.SetDirty(m);
  }
  foreach(string name in new[]{"SquirrelFront_LivingContour","SquirrelBack_LivingContour"}){var m=AssetDatabase.LoadAssetAtPath<Material>(FinishFolder+"/"+name+".mat");m.SetFloat("_Cull",2);m.SetColor("_BaseColor",name.StartsWith("SquirrelFront")?new Color(.57f,.64f,.56f):new Color(.29f,.35f,.41f));m.SetColor("_LineColor",new Color(.9f,.94f,.84f));m.SetFloat("_LineOpacity",.8f);m.SetFloat("_LineWidth",.8f);m.SetFloat("_RimBrightness",.3f);EditorUtility.SetDirty(m);}
  AssetDatabase.SaveAssets();
 }
 [MenuItem("Tools/Capstone/World Map/Lower Roots Enlarge Flowers And Finish Materials")]
 static void FinishForest()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play Mode first");if(AssetDatabase.IsValidFolder(FinishFolder))throw new Exception("Forest finish already applied");
  var scene=SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");var forest=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="02 Placeholder forest");var trees=forest.GetComponentsInChildren<CapsuleCollider>(true).Where(c=>c.GetComponentInParent<LODGroup>()!=null).ToArray();
  AssetDatabase.CreateFolder("Assets/Generated","WorldForestFinish");var crownMaterial=PlantMaterial("ForestCrown_OrganicInk",new Color(.54f,.59f,.48f),new Color(.14f,.18f,.14f),true);var vineMaterial=PlantMaterial("ForestVines_OrganicInk",new Color(.38f,.44f,.34f),new Color(.095f,.13f,.10f),false);var front=CreatureMaterial("SquirrelFront_LivingContour",new Color(.30f,.36f,.32f));var back=CreatureMaterial("SquirrelBack_LivingContour",new Color(.15f,.18f,.23f));
  var squirrelRoot=PrefabUtility.LoadPrefabContents("Assets/Prefabs/Creatures/Squirrels/Squirrel.prefab");GameObject squirrelPrefab;
  try
  {
   var visual=squirrelRoot.GetComponentsInChildren<Renderer>(true).Single(r=>r.name=="Squirrel Visual");visual.sharedMaterials=new[]{front,back};if(squirrelRoot.GetComponent<ForestCreatureSurface>()==null)squirrelRoot.AddComponent<ForestCreatureSurface>();squirrelPrefab=PrefabUtility.SaveAsPrefabAsset(squirrelRoot,FinishFolder+"/WorldForestSquirrel.prefab");
  }
  finally{PrefabUtility.UnloadPrefabContents(squirrelRoot);}
  foreach(var habitat in forest.GetComponentsInChildren<WorldForestHabitat>(true)){var config=new SerializedObject(habitat.squirrels);config.FindProperty("squirrelPrefab").objectReferenceValue=squirrelPrefab;config.ApplyModifiedPropertiesWithoutUndo();}
  int flowers=0;foreach(var flower in forest.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Low wildflower "))){flower.localScale*=1.75f;EditorUtility.SetDirty(flower);flowers++;}
  if(flowers==0)throw new Exception("No small forest flowers found");
  SilkForestMeshLibrary library=null;int meshes=0;Mesh Save(Mesh mesh){if(meshes%48==0){library=ScriptableObject.CreateInstance<SilkForestMeshLibrary>();AssetDatabase.CreateAsset(library,FinishFolder+"/Roots"+(meshes/48).ToString("000")+".asset");}AssetDatabase.AddObjectToAsset(mesh,library);EditorUtility.SetDirty(library);meshes++;return mesh;}
  float maxError=0;long nearCount=0,farCount=0;
  foreach(var tree in trees)
  {
   var group=tree.GetComponentInParent<LODGroup>();var filters=group.GetComponentsInChildren<MeshFilter>(true);var crown=filters.First(f=>f.name=="Crown membranes");var trunk=filters.First(f=>f.name=="Tensioned silk strands");var distant=filters.First(f=>f.name=="Tensioned silk strands distant");var foot=tree.center-Vector3.up*tree.height*.5f;var balls=Balls(crown.sharedMesh);if(balls.Count==0)throw new Exception("No crown sockets");float radius=balls.Max(b=>b.radius);var shape=new Shape{crown=crown.sharedMesh,foot=foot,height=tree.height,radius=radius,balls=balls,primary=balls.Where(b=>b.radius>=radius*.63f).OrderByDescending(b=>b.center.y).First()};int count=trunk.sharedMesh.name.StartsWith("3 ")?3:2;
   var original=trunk.sharedMesh.vertices;var toes=new Vector3[count];for(int strand=0;strand<count;strand++){for(int side=0;side<6;side++)toes[strand]+=original[strand*25*6+side];toes[strand]/=6;}
   trunk.sharedMesh=Save(Vines(shape,count,false,toes,out float nearError));distant.sharedMesh=Save(Vines(shape,count,true,toes,out float farError));maxError=Mathf.Max(maxError,Mathf.Max(nearError,farError));if(nearError>=.8f||farError>=.8f)throw new Exception("Crown connection lost");EditorUtility.SetDirty(trunk);EditorUtility.SetDirty(distant);
   foreach(var filter in filters.Where(f=>f.sharedMesh!=null&&(f.name.StartsWith("Crown membranes")||f.name.StartsWith("Tensioned silk strands")))){var renderer=filter.GetComponent<Renderer>();renderer.sharedMaterial=filter.name.StartsWith("Crown")?crownMaterial:vineMaterial;EditorUtility.SetDirty(renderer);}
   // Update hive sites to follow the revised lower vine curve.
   var worldFoot=Foot(tree);foreach(var anchor in tree.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="Small bee hive anchor - bare trunk")){float localY=Mathf.Clamp(anchor.position.y-worldFoot.y,.7f,1.4f)/Mathf.Abs(tree.transform.lossyScale.y);float t=Mathf.Clamp01(localY/(shape.primary.center.y-foot.y));var onVine=Vine(shape,0,count,t,toes[0]);var facing=Vector3.ProjectOnPlane(anchor.forward,Vector3.up).normalized;anchor.position=tree.transform.TransformPoint(onVine)+facing*(radius*.052f*Mathf.Abs(tree.transform.lossyScale.x)+.11f);EditorUtility.SetDirty(anchor);}
   group.RecalculateBounds();EditorUtility.SetDirty(group);var lods=group.GetLODs();nearCount+=lods[0].renderers.Sum(r=>(long)r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3);farCount+=lods[1].renderers.Sum(r=>(long)r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3);
  }
  foreach(var material in new[]{crownMaterial,vineMaterial,front,back})if(ShaderUtil.ShaderHasError(material.shader))throw new Exception("Material shader error: "+material.shader.name);
  AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);Directory.CreateDirectory("ArtCaptures/WorldForestFinish");File.WriteAllText("ArtCaptures/WorldForestFinish/Report.txt",$"{trees.Length} trees: lower root spread ends at 5.5% of vine length (previous 16%). Independent root tips retained, near/far updated. {flowers} small wildflower clumps enlarged 1.75x at existing ground pivots. Tree materials cloned from World Decor Organic (hatching, subdued foliage colours, static vertices). Squirrel prefab uses existing Octopus Membrane shader with creature-centred moving rings, no procedural skin tears; 3 habitat managers use the new prefab. Maximum crown endpoint ratio {maxError:F3}. Near triangles {nearCount:N0}; far {farCount:N0}.\n");
 }
}
