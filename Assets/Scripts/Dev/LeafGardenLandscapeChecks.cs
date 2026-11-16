using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEngine;

/// <summary>Verifies the actual landscape, terrain support and independent natural event controllers.</summary>
public sealed class LeafGardenLandscapeChecks : MonoBehaviour
{
    private struct State { public Vector4 home,position,velocity,rotation; }
    private readonly List<string> checks=new List<string>();
    private bool running;
    public void Run() { if(!running) StartCoroutine(Check()); }
    private void Assert(bool ok,string name) => checks.Add((ok?"PASS ":"FAIL ")+name);
    private IEnumerator Check()
    {
        running=true;checks.Clear();var show=GetComponent<LeafGardenShowcase>();
        yield return new WaitForSeconds(1);
        var world=FindFirstObjectByType<ProceduralWorld>();
        Assert(world!=null && world.UsingSculptedTerrain && world.GenerationComplete,"Shared sculptable ProceduralWorld is fully generated");
        Assert(world.UsedChunkCache,"Saved chunk cache is used at runtime");
        Assert(show.Gardens.Length==3,"Three independent leaf creatures cover the meadow");
        Assert(RenderSettings.skybox!=null && RenderSettings.skybox.name=="MAT_Sky_Black","Scene uses Scene_World's actual sky material");
        int frame=Time.frameCount;yield return null;Assert(Time.frameCount>frame,"Player loop advances");
        int alive=0,invalid=0,cliffLeaves=0;float low=1000,high=-1000,worst=0;
        foreach(var patch in show.Gardens)
        {
            var visual=patch.GetComponent<LeafGardenPresentation>();
            Assert(visual.Capacity==100000,"Colony has its configured GPU leaf population");
            var state=new State[visual.Capacity];visual.StateBuffer.GetData(state);
            for(int i=0;i<state.Length;i+=13)
            {
                var leaf=state[i];if(leaf.position.w<=0) continue;alive++;
                if(leaf.home.z<-80 && (Mathf.Abs(leaf.home.x)<35 || leaf.home.y>24.2f)) cliffLeaves++;
                if(float.IsNaN(leaf.position.y)) invalid++;
                low=Mathf.Min(low,leaf.home.y);high=Mathf.Max(high,leaf.home.y);
                worst=Mathf.Max(worst,Mathf.Abs(leaf.home.y-patch.Surface.HeightAt(leaf.home)));
            }
        }
        Assert(alive>500 && invalid==0,"Visible GPU leaves have finite positions");
        Assert(high-low>8,"Leaf coverage follows substantial terrain height variation");
        Assert(worst<0.2f,"GPU leaf homes agree with the baked collider support map");
        Assert(cliffLeaves==0,"Cliff and approach contain no resting leaves");
        Vector3 tip=new Vector3(0,80,-94);bool top=world.TryFindSurfaceBelow(tip,out float roof);
        Assert(top && roof>30 && roof<45,"Overlook has a modest walkable rock roof");
        Assert(world.SampleDensity(new Vector3(0,roof-1,-94))>0 && world.SampleDensity(new Vector3(0,roof-8,-94))<0,"Cliff tip has real empty space below its overhang");
        foreach(var patch in show.Gardens) patch.ResetGarden();
        Assert(show.Gardens[1].Phase==LeafGarden.GardenPhase.Resting,"Scene starts with a resting meadow");
        float deadline=Time.time+31;
        while(show.Gardens[1].EventNumber==0 && Time.time<deadline) yield return null;
        Assert(show.Gardens[1].EventNumber>0,"First tornado actually starts from its automatic timer");
        Assert(show.Gardens[1].Surface.Sample(show.Gardens[1].TornadoCenter).a>0.99f,"Automatic tornado avoids the bare cliff");
        var roll=typeof(LeafGarden).GetMethod("RollAutomaticHeight",BindingFlags.Instance|BindingFlags.NonPublic);
        var probe=new GameObject("Natural tornado distribution probe").AddComponent<LeafGarden>();
        int small=0,medium=0,large=0;float min=100,max=0;
        for(int i=0;i<10000;i++)
        {
            float h=(float)roll.Invoke(probe,null);min=Mathf.Min(min,h);max=Mathf.Max(max,h);
            if(h<=18)small++;else if(h<=40)medium++;else large++;
        }
        Destroy(probe.gameObject);
        Assert(small>7800 && small<8600 && large>350 && large<650,"Seeded event distribution favors small tornadoes and makes large ones rare");
        Assert(min>=8 && max<=80,"Natural tornado heights stay within the configured ranges");
        for(int i=0;i<3;i++) {var patch=show.Gardens[i];patch.Automatic=false;patch.SetPreset(i);patch.TriggerAt(patch.transform.position);}
        yield return new WaitForSeconds(6);
        Assert(show.Gardens.All(p=>p.Phase==LeafGarden.GardenPhase.Active),"Three colonies can sustain different tornadoes simultaneously");
        Assert(Mathf.Abs(show.Gardens[0].TornadoCenter.y-show.Gardens[0].Surface.HeightAt(show.Gardens[0].TornadoCenter))<0.1f,"Tornado source follows the actual hillside elevation");
        foreach(var patch in show.Gardens) {patch.ResetGarden();patch.Automatic=true;}
        checks.Add($"MEASURED sampled leaves={alive}; ground height range={low:F2}..{high:F2}; worst support error={worst:F3}m; size distribution={small}/{medium}/{large}; sizes={min:F2}..{max:F2}m");
        Directory.CreateDirectory("ArtCaptures/LeafGarden");File.WriteAllLines("ArtCaptures/LeafGarden/landscape-checks.txt",checks);running=false;
    }
}
