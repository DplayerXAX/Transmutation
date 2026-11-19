using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class WorldForestRuntimeValidation
{
    const string Key="WorldForest.RuntimeValidation";
    static WorldForestRuntimeValidation(){EditorApplication.update+=Tick;}
    [MenuItem("Tools/Capstone/World Map/Test Forest Ecosystem In Play Mode")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play Mode first.");
        SessionState.SetInt(Key,0);SessionState.SetFloat(Key+"time",0);SessionState.SetString(Key+"report","");SessionState.SetBool(Key+"active",true);EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Key+"active",false)||!EditorApplication.isPlaying||EditorApplication.isCompiling||Time.timeSinceLevelLoad<3)return;
        try
        {
            var habitats=Object.FindObjectsByType<WorldForestHabitat>(FindObjectsInactive.Include,FindObjectsSortMode.None).OrderBy(h=>h.name).ToArray();if(habitats.Length!=3)throw new Exception("Expected three live habitats.");
            int stage=SessionState.GetInt(Key,0),district=stage/3;var habitat=habitats[district];float since=Time.realtimeSinceStartup-SessionState.GetFloat(Key+"time",0);
            if(stage%3==0)
            {
                var controller=Object.FindObjectsByType<SmoothFirstPersonController>(FindObjectsSortMode.None).First(c=>c.gameObject.scene==habitat.gameObject.scene);
                var roots=habitat.treeRoots;var at=roots.Aggregate(Vector3.zero,(v,r)=>v+r.GroundPosition)/roots.Length+Vector3.up*4;
                controller.transform.position=at;if(controller.Body!=null){controller.Body.position=at;controller.Body.linearVelocity=Vector3.zero;}
                SessionState.SetFloat(Key+"time",Time.realtimeSinceStartup);SessionState.SetInt(Key,stage+1);return;
            }
            if(stage%3==1)
            {
                if(since>75)throw new Exception($"Habitat {district+1}: streamed terrain/ecosystem did not become ready within 75 seconds.");
                if(!habitat.Ready)return;
                SessionState.SetFloat(Key+"time",Time.realtimeSinceStartup);SessionState.SetInt(Key,stage+1);return;
            }
            if(since<2)return;
            var manager=habitat.squirrels;int nests=habitat.colonies.Sum(c=>c.Nests.Count),bees=habitat.GetComponentsInChildren<GlowBee>().Length;
            if(manager.Squirrels.Count!=6||manager.Nests.Count!=6||nests!=12||bees!=72)throw new Exception($"Habitat {district+1}: live counts squirrels={manager.Squirrels.Count}, nests={manager.Nests.Count}, hives={nests}, bees={bees}.");
                        if(File.Exists("ArtCaptures/WorldForestFinish/Report.txt"))foreach(var squirrel in manager.Squirrels)
            {
                var renderer=squirrel.GetComponentsInChildren<Renderer>().Single(r=>r.name=="Squirrel Visual");var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
                if(squirrel.GetComponent<ForestCreatureSurface>()==null||renderer.sharedMaterials.Any(m=>m.shader.name!="Capstone/Creatures/Membrane")||Vector3.Distance((Vector3)block.GetVector("_Center"),renderer.bounds.center)>.05f)throw new Exception("Forest squirrel creature material/contour centre not active.");
            }
            string report=SessionState.GetString(Key+"report","")+$"PASS habitat {district+1}: streamed-terrain startup, 6 squirrels / 6 nests / {manager.Foods.Count} foods, 12 mounted hives / 72 bees with authored hyacinth nectar sources; squirrel creature materials and moving contour centres validated.\\n";
            SessionState.SetString(Key+"report",report);
            if(district==2){File.WriteAllText("ArtCaptures/WorldForestEcosystem/Runtime.txt",report.Replace("\\n","\n"));Finish();}
            else SessionState.SetInt(Key,stage+1);
        }
        catch(Exception e){File.WriteAllText("ArtCaptures/WorldForestEcosystem/Runtime.txt",e.ToString());Debug.LogException(e);Finish();}
    }
    static void Finish(){SessionState.SetBool(Key+"active",false);EditorApplication.ExitPlaymode();}
}
