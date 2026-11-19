using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class WorldForestEcosystemSetup
{
    static readonly Vector2[] Centers={new Vector2(-120,-110),new Vector2(-185,-210),new Vector2(145,-175)};
    static Vector3 Foot(Transform tree)
    {var c=tree.GetComponent<CapsuleCollider>();return tree.TransformPoint(c.center-Vector3.up*c.height*.5f);}
    static bool OpenRoute(Vector2 p)
    {
        return Vector2.Distance(p,WorldMapTerrain.Spawn)<23||WorldMapTerrain.InGooExterior(p.x,p.y)||
            WorldMapTerrain.Distance(p,WorldMapTerrain.Spawn,new Vector2(10,120),out _) < 5||
            WorldMapTerrain.Distance(p,WorldMapTerrain.Spawn,WorldMapTerrain.SpawnExit,out _) < 7;
    }
    [MenuItem("Tools/Capstone/World Map/Import Dense Squirrel Forest Ecosystem")]
    public static void Apply()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play Mode first.");
        var worldScene=SceneManager.GetSceneByPath("Assets/Scenes/Scene_World.unity");
        if(!worldScene.IsValid()||!worldScene.isLoaded)throw new Exception("Open Scene_World first.");
        var forest=worldScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="02 Placeholder forest");
        foreach(var hive in forest.GetComponentsInChildren<Transform>().Where(t=>t.name=="Small bee hive anchor - bare trunk").ToArray())Object.DestroyImmediate(hive.gameObject);
        var previousImport=forest.Find("Squirrel forest habitats");if(previousImport!=null)Object.DestroyImmediate(previousImport.gameObject);
        var world=worldScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<ProceduralWorld>()).First();
        WorldMapBuild.PrepareSampler(world);Debug.Log("Forest world sampling bounds: "+world.VolumeBounds);
        bool Ground(float x,float z,out Vector3 point)
        {
            point=Vector3.zero;if(OpenRoute(new Vector2(x,z)))return false;
            if(!world.TryFindSurfaceBelow(new Vector3(x,world.VolumeBounds.max.y,z),out float y))return false;
            float slope=Mathf.Max(Mathf.Abs(WorldMapTerrain.Height(x+.5f,z)-WorldMapTerrain.Height(x-.5f,z)),Mathf.Abs(WorldMapTerrain.Height(x,z+.5f)-WorldMapTerrain.Height(x,z-.5f)));
            if(slope>1.6f)return false;point=new Vector3(x,y,z);return true;
        }
        var source=SceneManager.GetSceneByPath(SquirrelForestSetup.ScenePath);bool opened=!source.IsValid()||!source.isLoaded;
        if(opened)source=EditorSceneManager.OpenScene(SquirrelForestSetup.ScenePath,OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(worldScene);
            var sourceRoots=source.GetRootGameObjects();var sourceForest=sourceRoots.SelectMany(g=>g.GetComponentsInChildren<SilkForestManager>()).Single();
            var sourceTrees=sourceForest.GeneratedRoot.Cast<Transform>().Where(t=>t.GetComponent<CapsuleCollider>()!=null).ToArray();
            var eligible=new HashSet<string>(sourceTrees.Where(t=>t.GetComponent<SquirrelTree>()!=null).Select(t=>t.name));
            var sourceMeadow=sourceRoots.Single(g=>g.name=="Forest flowers and glow bee meadow");
            var sourceManager=sourceRoots.SelectMany(g=>g.GetComponentsInChildren<SquirrelSystemManager>()).Single();
            var templates=forest.GetComponentsInChildren<CapsuleCollider>().Select(c=>c.transform).Where(t=>t.GetComponentInParent<LODGroup>()!=null).ToArray();
            var positions=templates.Select(Foot).ToList();var random=new System.Random(191126);float R(float a,float b)=>Mathf.Lerp(a,b,(float)random.NextDouble());
            var imported=new GameObject("Squirrel forest habitats").transform;imported.SetParent(forest,false);
            var report=new List<string>();int totalAdded=0,flowers=0,colonies=0;
            for(int district=0;district<Centers.Length;district++)
            {
                var center=Centers[district];var habitat=new GameObject("Dense forest habitat "+(district+1)).transform;habitat.SetParent(imported,false);
                bool Inside(Vector3 p)=>Mathf.Abs(p.x-center.x)<48&&Mathf.Abs(p.z-center.y)<48;
                int count=positions.Count(Inside),added=0;
                for(int attempt=0;count<700&&attempt<180000;attempt++)
                {
                    float x=center.x+R(-48,48),z=center.y+R(-48,48);
                    if(!Ground(x,z,out var point)||positions.Any(p=>new Vector2(p.x-x,p.z-z).sqrMagnitude<1.8f*1.8f))continue;
                    var template=templates[random.Next(templates.Length)];var holder=new GameObject("Dense world silk tree "+district+"-"+(++added)).transform;holder.SetParent(habitat,false);
                    var tree=Object.Instantiate(template.gameObject,holder).transform;tree.name=template.name;tree.position+=point-Foot(tree);tree.RotateAround(point,Vector3.up,R(0,360));
                    foreach(var anchor in tree.GetComponentsInChildren<Transform>().Where(t=>t.name=="Small bee hive anchor - bare trunk").ToArray())Object.DestroyImmediate(anchor.gameObject);
                    var lod=holder.gameObject.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.02f,tree.GetComponentsInChildren<Renderer>())});lod.RecalculateBounds();
                    positions.Add(point);count++;
                }
                if(count<700)throw new Exception($"Could only place {count} trees in district {district+1}.");
                totalAdded+=added;
                var trunks=forest.GetComponentsInChildren<CapsuleCollider>().Select(c=>c.transform).Where(t=>Inside(Foot(t))).ToArray();var anchors=new List<SquirrelTree>();
                foreach(var tree in trunks.Where(t=>eligible.Contains(t.name)))
                {var anchor=tree.GetComponent<SquirrelTree>()??tree.gameObject.AddComponent<SquirrelTree>();anchor.SetRootPoint(tree.InverseTransformPoint(Foot(tree)));anchors.Add(anchor);}
                if(anchors.Count<6)throw new Exception("Insufficient low-crown trees for squirrel nests.");
                var meadow=Object.Instantiate(sourceMeadow,habitat).transform;meadow.name="Imported spaced hyacinths and pale meadow";
                var detached=new List<GameObject>();
                foreach(var child in meadow.Cast<Transform>().ToArray())
                {
                    if(child.GetComponent<GlowBeeColony>()!=null)continue;
                    Vector3 at=child.position;
                    if(!Ground(at.x+center.x,at.z+center.y,out var point)){detached.Add(child.gameObject);continue;}
                    child.position+=point-at;
                }
                foreach(var flower in meadow.GetComponentsInChildren<NectarFlower>())
                {
                    if(detached.Any(g=>flower.transform.IsChildOf(g.transform)))continue;
                    var at=flower.transform.position;if(Ground(at.x,at.z,out var point))flower.transform.position=point;
                }
                var used=new HashSet<Transform>();
                foreach(var colony in meadow.GetComponentsInChildren<GlowBeeColony>())
                {
                    colony.enabled=false;var settings=new SerializedObject(colony);var food=settings.FindProperty("authoredFlowers");
                    var surviving=Enumerable.Range(0,food.arraySize).Select(i=>food.GetArrayElementAtIndex(i).objectReferenceValue as NectarFlower).Where(f=>f!=null&&!detached.Any(g=>f.transform.IsChildOf(g.transform))).ToArray();
                    food.arraySize=surviving.Length;for(int i=0;i<surviving.Length;i++)food.GetArrayElementAtIndex(i).objectReferenceValue=surviving[i];
                    if(surviving.Length==0){detached.Add(colony.gameObject);continue;}
                    var sites=settings.FindProperty("nestAnchors");sites.arraySize=2;Vector3 flowerCenter=surviving.Aggregate(Vector3.zero,(v,f)=>v+f.transform.position)/surviving.Length;
                    for(int i=0;i<2;i++)
                    {
                        var tree=trunks.Where(t=>!eligible.Contains(t.name)&&!used.Contains(t)).OrderBy(t=>(Foot(t)-flowerCenter).sqrMagnitude).First();used.Add(tree);
                        var c=tree.GetComponent<CapsuleCollider>();var foot=Foot(tree);var direction=Vector3.ProjectOnPlane(flowerCenter-foot,Vector3.up).normalized;if(direction.sqrMagnitude<.1f)direction=Vector3.forward;
                        var hive=new GameObject("Small bee hive anchor - bare trunk").transform;hive.SetParent(tree,false);hive.position=foot+Vector3.up*Mathf.Clamp(c.height*Mathf.Abs(tree.lossyScale.y)*.5f,.65f,1.6f)+direction*(c.radius*Mathf.Abs(tree.lossyScale.x)+.2f);hive.rotation=Quaternion.LookRotation(direction);
                        sites.GetArrayElementAtIndex(i).objectReferenceValue=hive;
                    }
                    settings.FindProperty("mountHivesOnAnchors").boolValue=true;settings.FindProperty("hiveScale").floatValue=.5f;settings.ApplyModifiedPropertiesWithoutUndo();colonies++;
                }
                foreach(var go in detached.Distinct())if(go!=null)Object.DestroyImmediate(go);
                flowers+=meadow.GetComponentsInChildren<NectarFlower>().Length;
                var manager=Object.Instantiate(sourceManager.gameObject,habitat).GetComponent<SquirrelSystemManager>();manager.name="Squirrel ecosystem - spaced low-crown homes";manager.enabled=false;
                foreach(var child in manager.transform.Cast<Transform>().ToArray())Object.DestroyImmediate(child.gameObject);
                var config=new SerializedObject(manager);config.FindProperty("autoDiscoverScene").boolValue=false;
                foreach(string field in new[]{"squirrels","nests","foods"})config.FindProperty(field).arraySize=0;
                config.FindProperty("population").intValue=6;config.FindProperty("initialFoodCount").intValue=60;config.FindProperty("spreadNestLocations").boolValue=true;config.FindProperty("minimumNestSeparation").floatValue=28;config.FindProperty("scatterFoodOnSide").boolValue=true;
                var trees=config.FindProperty("trees");trees.arraySize=anchors.Count;for(int i=0;i<anchors.Count;i++)trees.GetArrayElementAtIndex(i).objectReferenceValue=anchors[i];config.ApplyModifiedPropertiesWithoutUndo();
                var ready=habitat.gameObject.AddComponent<WorldForestHabitat>();ready.squirrels=manager;ready.treeRoots=anchors.ToArray();ready.colonies=meadow.GetComponentsInChildren<GlowBeeColony>();
                report.Add($"Habitat {district+1}, centre {center}: {count} trees in 96x96 m ({added} added); {anchors.Count} low-crown root anchors; {ready.colonies.Length} bee colonies; {meadow.GetComponentsInChildren<NectarFlower>().Length} nectar flowers.");
            }
            // Check all cross-scene references that drive ecology before unloading the donor.
            foreach(var colony in imported.GetComponentsInChildren<GlowBeeColony>())
            {
                var settings=new SerializedObject(colony);
                foreach(string field in new[]{"authoredFlowers","nestAnchors"})
                {var list=settings.FindProperty(field);for(int i=0;i<list.arraySize;i++){var c=list.GetArrayElementAtIndex(i).objectReferenceValue as Component;if(c==null||c.gameObject.scene!=worldScene)throw new Exception("Bee reference points outside Scene_World.");}}
            }
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(worldScene);EditorSceneManager.SaveScene(worldScene);
            Directory.CreateDirectory("ArtCaptures/WorldForestEcosystem");report.Insert(0,$"Added {totalAdded} trees, {flowers} nectar flowers, {colonies} bee colonies. 18 squirrels, 18 separated root-side nests, 180 sideways foods generated after terrain colliders are ready. Source scene not saved or changed. Existing repaired tree meshes shared.");
            File.WriteAllLines("ArtCaptures/WorldForestEcosystem/Import.txt",report);Debug.Log(report[0]);
        }
        finally{SceneManager.SetActiveScene(worldScene);if(opened)EditorSceneManager.CloseScene(source,true);}
    }
}
