using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
public static class ForestCrownInspect
{
 [MenuItem("Tools/Capstone/World Map/Inspect Forest Crown Sources")]
 static void Run(){var paths=AssetDatabase.FindAssets("",new[]{"Assets/Generated/WorldForestArtPass"}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>p.EndsWith(".asset"));File.WriteAllLines("Temp/ForestCrownSources.txt",paths.SelectMany(p=>AssetDatabase.LoadAllAssetsAtPath(p).OfType<Mesh>()).Select(m=>$"{AssetDatabase.GetAssetPath(m)} | {m.name} | {m.bounds.center} | {m.bounds.size}"));var scene=SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");File.WriteAllLines("Temp/ForestTreeHeights.txt",scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<LODGroup>(true)).Where(g=>g.GetComponentInChildren<CapsuleCollider>()!=null).Take(20).Select(g=>{var c=g.GetComponentInChildren<CapsuleCollider>();return $"{g.name} capsuleLocal={c.height} scale={c.transform.lossyScale} bounds={g.GetComponentsInChildren<Renderer>().Where(r=>r.name=="Crown membranes").FirstOrDefault()?.bounds} foot={c.center-Vector3.up*c.height*.5f}";}));}
}
