using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Check the revised open-world environment, actual terrain support and mesh/material separation.</summary>
public static class BasiltSceneChecks
{
    public static object Validate()
    {
        Require(SceneManager.GetActiveScene().path==BasiltSceneSetup.ScenePath,"Open Scene Basilt first.");
        var worlds=UnityEngine.Object.FindObjectsByType<ProceduralWorld>(FindObjectsSortMode.None);
        Require(worlds.Length==2 && worlds.All(w=>w.GenerationComplete && w.UsingSculptedTerrain),"Both terrain volumes must be complete.");
        var land=worlds.Single(w=>w.name.Contains("hexagonal"));
        var jungle=GameObject.Find(BasiltSceneSetup.JungleRoot);
        Require(jungle!=null,"The megavine jungle is missing.");
        var columns=jungle.transform.Find("Individual basalt - enclosing walls and shared terraces");
        var canopy=jungle.transform.Find("Independent giant arches and canopy sweeps");
        var thorns=jungle.transform.Find("Radial hooked thorns - all around the vines");
        Require(columns!=null && columns.childCount>400,"The enclosing basalt formations are missing.");
        Require(canopy!=null && canopy.childCount==BasiltDensity.Canopy.Length,"Some independent arches are missing.");
        Require(thorns!=null && thorns.childCount==BasiltDensity.Roads.Length+BasiltDensity.Canopy.Length,"Some radial thorn belts are missing.");
        Require(columns.GetComponentsInChildren<MeshRenderer>().All(r=>r.sharedMaterial.shader.name=="Capstone/World/BlackFlowContours"),"Basalt must use BlackFlowContours.");
        Physics.SyncTransforms();int supported=0;float min=999,max=-999;
        for(int z=-240;z<=240;z+=30)for(int x=-210;x<=210;x+=30)
        {
            float h=BasiltDensity.GroundHeight(x,z);min=Mathf.Min(min,h);max=Mathf.Max(max,h);
            bool hit=Physics.RaycastAll(new Vector3(x,140,z),Vector3.down,180,1<<7).Any(p=>land.OwnsCollider(p.collider));
            Require(hit,"Generated ground is missing at "+x+", "+z);supported++;
        }
        Require(max-min>20,"The ground needs substantial elevation changes.");
        var player=UnityEngine.Object.FindFirstObjectByType<SmoothFirstPersonController>();Require(player!=null,"Player is missing.");
        var playerGround=Physics.RaycastAll(player.transform.position+Vector3.up*2,Vector3.down,10,1<<7).Where(h=>land.OwnsCollider(h.collider)).ToArray();
        Require(playerGround.Length>0,"The arrival has no ground support.");
        return new {worldSize=land.WorldSize,allCached=worlds.All(w=>w.UsedChunkCache),majorVines=BasiltDensity.Roads.Length+canopy.childCount,canopyArches=canopy.childCount,thickestDiameter=BasiltDensity.Canopy.Max(v=>v.radius)*2,basaltColumns=columns.childCount,radialThornBelts=thorns.childCount,supportedGroundSamples=supported,groundHeightRange=new Vector2(min,max),playerSupported=true,basaltShader="BlackFlowContours"};
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
