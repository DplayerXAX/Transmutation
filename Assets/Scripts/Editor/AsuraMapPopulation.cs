using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class AsuraMapPopulation
{
    public static object Populate()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Exit Play mode first.");
        var t=UnityEngine.Object.FindFirstObjectByType<AsuraTerritory>();
        var map=AsuraTerritorySetup.Bake();t.surfaceMap=map;
        var homes=t.GetComponentsInChildren<AsuraHome>();
        foreach(var n in t.GetComponentsInChildren<AsuraLinkage>())if(!n.Permanent)UnityEngine.Object.DestroyImmediate(n.gameObject);
        var allCovered=new HashSet<int>();var reachable=new HashSet<int>();int count=0;
        foreach(var home in homes)
        {
            home.surfaceIndex=map.Nearest(home.transform.position);home.populationCap=180;home.spawnsPerSecond=2.5f;
            bool light=home.name.StartsWith("Light");
            var distance=map.Distances(home.surfaceIndex,4000);foreach(int i in distance.Keys)reachable.Add(i);
            var covered=new HashSet<int>();var seeds=new List<int>{home.surfaceIndex};
            Action<int> cover=i=>{foreach(int j in map.Distances(i,t.influenceRadius).Keys){covered.Add(j);allCovered.Add(j);}};
            cover(home.surfaceIndex);
            Action<int> place=i=>{
                var go=(GameObject)PrefabUtility.InstantiatePrefab(t.linkagePrefab.gameObject);go.transform.SetParent(t.transform);
                go.transform.position=map.surfaces[i].position+Vector3.up*0.1f;go.name=home.name+" / area outpost "+count++;
                var n=go.GetComponent<AsuraLinkage>();n.home=home;n.surfaceIndex=i;
                var so=new SerializedObject(n);so.FindProperty("initialPolarity").enumValueIndex=(int)(light?CreaturePolarity.Light:CreaturePolarity.Dark);so.ApplyModifiedPropertiesWithoutUndo();
                var mat=home.GetComponentInChildren<Renderer>().sharedMaterial;foreach(var r in go.GetComponentsInChildren<Renderer>())r.sharedMaterial=mat;
                seeds.Add(i);cover(i);
            };
            foreach(int target in distance.Keys.OrderBy(i=>distance[i]))
            {
                var p=map.surfaces[target].position;
                if((p.x/216f+p.z/252f<=0)!=light || covered.Contains(target))continue;
                int nearest=seeds.OrderBy(i=>(map.surfaces[i].position-p).sqrMagnitude).First();
                var path=map.Path(nearest,target);float travel=0;
                for(int k=1;k<path.Count;k++)
                {
                    travel+=Vector3.Distance(map.surfaces[path[k-1]].position,map.surfaces[path[k]].position);
                    if(travel>=75 || k==path.Count-1){place(path[k]);travel=0;}
                }
            }
            EditorUtility.SetDirty(home);
        }
        // Prune redundant seeds only when both coverage and the root-connected network survive.
        foreach(var home in homes)
        {
            var group=t.GetComponentsInChildren<AsuraLinkage>().Where(n=>n.home==home).ToList();
            var ranges=group.ToDictionary(n=>n,n=>map.Distances(n.surfaceIndex,t.maximumLinkDistance));
            var patches=group.ToDictionary(n=>n,n=>ranges[n].Where(e=>e.Value<=t.influenceRadius).Select(e=>e.Key).ToArray());
            var counts=new Dictionary<int,int>();foreach(var n in group)foreach(int i in patches[n])counts[i]=counts.TryGetValue(i,out int c)?c+1:1;
            foreach(var n in group.Where(n=>!n.Permanent).OrderBy(n=>patches[n].Length).ToArray())
            {
                if(patches[n].Any(i=>counts[i]<=1))continue;
                var connected=new HashSet<AsuraLinkage>{home};bool changed=true;
                while(changed){changed=false;foreach(var other in group)if(other!=n && !connected.Contains(other) && connected.Any(p=>ranges[p].ContainsKey(other.surfaceIndex))){connected.Add(other);changed=true;}}
                if(connected.Count!=group.Count-1)continue;
                foreach(int i in patches[n])counts[i]--;group.Remove(n);UnityEngine.Object.DestroyImmediate(n.gameObject);count--;
            }
        }
        t.initialPopulation=140;t.maximumNodesPerHome=Mathf.Max(40,t.GetComponentsInChildren<AsuraLinkage>().Length);EditorUtility.SetDirty(t);
        AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(t.gameObject.scene);
        return new {outposts=count,reachable=reachable.Count,covered=allCovered.Count,initialPerHome=t.initialPopulation};
    }
}
