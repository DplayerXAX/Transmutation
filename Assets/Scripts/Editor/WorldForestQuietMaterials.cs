using System;
using System.IO;
using UnityEditor;
using UnityEngine;
public static class WorldForestQuietMaterials
{
 const string Folder="Assets/Generated/WorldForestFinish/";
 [MenuItem("Tools/Capstone/World Map/Simplify Forest Material Patterns")]
 public static void Apply()
 {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play Mode first");
  foreach(bool crown in new[]{true,false})
  {
   var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+(crown?"ForestCrown_OrganicInk":"ForestVines_OrganicInk")+".mat");if(material==null)throw new Exception("Forest material missing");
   var color=crown?new Color(.72f,.75f,.68f):new Color(.54f,.59f,.50f);material.SetColor("_Color",color);material.SetColor("_ShadowColor",crown?new Color(.45f,.49f,.43f):new Color(.32f,.37f,.30f));material.SetColor("_LineColor",color*.82f);material.SetColor("_LineColor2",color*.82f);
   foreach(string property in new[]{"_HatchStrength","_DotChance","_GlitchAmount","_GlitchTint","_GlitchShift","_GlitchSplit","_RingCount","_RingSpeed","_StrataFrequency","_SheenAmount","_SwayAmount","_BreathAmount"})material.SetFloat(property,0);
   // These thresholds use step(), so a negative sentinel also excludes exact-zero UVs.
   material.SetFloat("_GridChance",-.001f);material.SetFloat("_InvertChance",-.001f);material.SetFloat("_RimStrength",.04f);material.SetFloat("_ShadeLevels",4);EditorUtility.SetDirty(material);
   if(ShaderUtil.ShaderHasError(material.shader))throw new Exception("Tree shader failed");
  }
  foreach(bool front in new[]{true,false})
  {
   var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+(front?"SquirrelFront_LivingContour":"SquirrelBack_LivingContour")+".mat");if(material==null)throw new Exception("Squirrel material missing");var color=front?new Color(.57f,.62f,.55f):new Color(.32f,.37f,.40f);material.SetColor("_BaseColor",color);material.SetColor("_LineColor",color*1.15f);material.SetFloat("_LineOpacity",0);material.SetFloat("_RingFrequency",0);material.SetFloat("_RingSpeed",0);material.SetFloat("_RimBrightness",.08f);material.SetFloat("_HighlightSize",0);material.SetFloat("_ShadeStrength",.2f);material.SetFloat("_TearAmount",0);EditorUtility.SetDirty(material);
   if(ShaderUtil.ShaderHasError(material.shader))throw new Exception("Squirrel shader failed");
  }
  AssetDatabase.SaveAssets();File.WriteAllText("ArtCaptures/WorldForestFinish/QuietMaterials.txt","PASS: tree hatch/dots/grid/strata/rings/glitch/inversion disabled; soft muted green-grey shading and subtle rim only. Squirrel moving rings/highlight flashes disabled, retained simple creature shading. Geometry, flowers and ecosystem configuration unchanged.\n");
 }
}
