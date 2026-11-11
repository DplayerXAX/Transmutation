using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>Checks the live garden and the shared launch/player contract, rather than mocking them.</summary>
public sealed class LeafGardenRuntimeChecks : MonoBehaviour
{
    private readonly List<string> results=new List<string>();
    private bool running;
    private struct LeafState { public Vector4 home,position,velocity,rotation; }
    public void Run() { if(!running) StartCoroutine(Check()); }
    private void Assert(bool pass,string name) { results.Add((pass?"PASS ":"FAIL ")+name); }
    private IEnumerator Check()
    {
        running=true; results.Clear(); var show=GetComponent<LeafGardenShowcase>();
        show.ResetShowcase(); show.Garden.Automatic=false; show.SetView(2);
        yield return null; int frame=Time.frameCount; yield return null;
        Assert(Time.frameCount>frame,"Player loop advances");
        Assert(show.Leaves.Capacity>=25000,"GPU leaf buffer allocated");
        Assert(!show.Garden.Contains(Vector3.zero),"Resting carpet has no launch volume");
        Assert(!show.Garden.CanBeCarried && !show.Garden.CanBeCaptured && !show.Garden.CanBeConsumed,"Garden is protected from carrying, capture and whole-patch consumption");
        Assert(show.Garden.GetComponent<Rigidbody>()==null && show.Garden.GetComponent<Collider>()==null,"Collective garden has no physical body or leaf colliders");
        yield return new WaitForSeconds(1.2f);
        Assert(Mathf.Abs(show.DemoBodies[0].linearVelocity.x)>1,"Demonstration creature walks across the garden");
        var state=new LeafState[show.Leaves.Capacity]; show.Leaves.StateBuffer.GetData(state);
        int disturbed=0;
        foreach(var leaf in state) if(leaf.position.y-leaf.home.y>0.12f) disturbed++;
        Assert(disturbed>20,"Moving creatures visibly displace actual GPU leaves");
        show.Trigger(); yield return new WaitForSeconds(show.Garden.GatherSeconds+0.5f);
        Assert(show.Garden.Phase==LeafGarden.GardenPhase.Active,"Gathering becomes active tornado");
        Assert(show.Garden.Contains(show.Garden.TornadoCenter+Vector3.up),"Tornado center is active");
        Assert(!show.Garden.Contains(show.Garden.TornadoCenter+Vector3.right*45),"Distant carpet remains outside tornado");
        Assert(!show.Garden.Contains(show.Garden.TornadoCenter+Vector3.up*(show.Garden.Height+5)),"Volume does not extend above tornado");
        show.Leaves.StateBuffer.GetData(state); float peak=0; int invalid=0,airborne=0;
        foreach(var leaf in state)
        {
            if(float.IsNaN(leaf.position.y) || float.IsInfinity(leaf.position.y)) invalid++;
            peak=Mathf.Max(peak,leaf.position.y); if(leaf.position.y>2) airborne++;
        }
        Assert(invalid==0,"GPU leaf positions remain finite");
        Assert(peak>show.Garden.Height*0.85f,"Leaf geometry reaches the selected tornado height");
        Assert(airborne>100 && airborne<state.Length*0.15f,"Only a local fraction of the carpet becomes airborne");
        var obj=new GameObject("Leaf verification compound body"); var rb=obj.AddComponent<Rigidbody>(); rb.useGravity=false;
        var c=obj.AddComponent<BoxCollider>(); c.size=Vector3.one*0.3f;
        var child=new GameObject("Second collider"); child.transform.SetParent(obj.transform,false); child.AddComponent<SphereCollider>().radius=0.2f;
        rb.position=show.Garden.TornadoCenter+Vector3.up*3; rb.linearVelocity=Vector3.right*4;
        ReflectionPhysics.SyncTransforms(); int launches=show.Launch.LaunchCount;
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert(rb.linearVelocity.y>show.Launch.LaunchSpeed-0.5f,"Dynamic body receives upward launch");
        Assert(Mathf.Abs(rb.linearVelocity.x-4)<0.1f,"Launch preserves horizontal momentum");
        float initial=rb.linearVelocity.y; yield return new WaitForFixedUpdate();
        Assert(rb.linearVelocity.y<=initial+0.01f,"Compound colliders and staying inside do not stack impulses");
        Assert(show.Launch.LaunchCount>=launches+1,"Launch is recorded");
        rb.position=show.Garden.TornadoCenter+Vector3.right*45; rb.linearVelocity=Vector3.zero;
        ReflectionPhysics.SyncTransforms(); yield return new WaitForFixedUpdate();
        yield return new WaitForSeconds(show.Launch.ReentryCooldown+0.1f);
        rb.position=show.Garden.TornadoCenter+Vector3.up*3; ReflectionPhysics.SyncTransforms();
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert(rb.linearVelocity.y>20,"Exit and re-entry permit another launch");
        rb.isKinematic=true; Assert(!LeafTornadoLaunchVolume.Eligible(rb),"Anchored bodies are excluded"); Destroy(obj);
        show.MovePlayerToTornado(); ReflectionPhysics.SyncTransforms();
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        Assert(show.Player.GetComponent<Rigidbody>().linearVelocity.y>10,"Actual project player receives external upward motion");
        var playerBody=show.Player.GetComponent<Rigidbody>();
        float playerPeak=playerBody.position.y, startFlight=Time.time;
        // Follow the real player's first ascent under the project's actual -30 m/s² gravity.
        while(playerBody.linearVelocity.y>0 && Time.time-startFlight<10)
        { playerPeak=Mathf.Max(playerPeak,playerBody.position.y); yield return new WaitForFixedUpdate(); }
        Assert(playerPeak>=show.Garden.TornadoCenter.y+show.Garden.Height,"Actual player reaches at least the full tornado height");
        results.Add($"MEASURED player first apex {playerPeak-show.Garden.TornadoCenter.y:F2} m; tornado {show.Garden.Height:F0} m; target apex {show.Launch.TargetApexY-show.Garden.TornadoCenter.y:F2} m");
        show.SetView(2);
        yield return new WaitForSeconds(show.Garden.ActiveSeconds+show.Garden.SettleSeconds+1);
        Assert(show.Garden.Phase==LeafGarden.GardenPhase.Resting,"Tornado settles back to resting carpet");
        Assert(!show.Garden.Contains(show.Garden.TornadoCenter+Vector3.up),"Finished event has no lingering launch volume");
        Directory.CreateDirectory("ArtCaptures/LeafGarden"); File.WriteAllLines("ArtCaptures/LeafGarden/runtime-checks.txt",results);
        running=false;
    }
}
