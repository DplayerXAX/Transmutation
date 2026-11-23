using System;
using System.Collections;
using UnityEngine;

public sealed class GooThrowBreakProbe : MonoBehaviour
{
    public GameObject prefab;
    public static string Result;
    IEnumerator Start()
    {
        Result="Running";
        var frame=FindFirstObjectByType<MovingFrame>();var player=FindFirstObjectByType<SmoothFirstPersonController>();
        var carrier=FindFirstObjectByType<PlayerCreatureCarrier>();
        var member=player.GetComponent<MovementFrameMember>();
        var thrower=carrier.GetComponent<CreatureThrower>()??carrier.gameObject.AddComponent<CreatureThrower>();
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Goo static test floor";floor.layer=7;
        floor.transform.position=new Vector3(1000,-.5f,1000);floor.transform.localScale=new Vector3(50,1,50);
        for(int location=0;location<2;location++)
        {
            bool aboard=location==1;
            member.Leave();member.allowAutomaticEntry=aboard;
            Vector3 start=aboard?frame.transform.TransformPoint(new Vector3(0,1,-15)):new Vector3(1000,1,1000);
            player.Body.position=start;player.Body.linearVelocity=aboard?frame.VelocityAt(start):Vector3.zero;
            if(aboard)member.Join(frame);
            player.SetLook(aboard?frame.transform.eulerAngles.y:0,0);
            yield return new WaitForSeconds(.5f);
            // Drop beside the player, not onto their head (a creature contact has
            // different coating rules and cannot validate a ground splat).
            Vector3 side=aboard?frame.transform.right:Vector3.right;
            var dropped=Instantiate(prefab,player.Body.position+side*4+Vector3.up*3,Quaternion.identity).GetComponent<GooCreature>();
            if(aboard)dropped.GetComponent<Rigidbody>().linearVelocity=frame.VelocityAt(dropped.transform.position);
            float until=Time.time+3;
            while(dropped && !dropped.IsDead && Time.time<until)yield return null;
            Check(!dropped||dropped.IsDead,(aboard?"island":"static")+" ordinary drop splats");
            var goo=Instantiate(prefab,player.Body.position+Vector3.up,Quaternion.identity).GetComponent<GooCreature>();
            Check(carrier.TryCarryFromInteraction(goo),"pickup full-force throw fixture");
            yield return new WaitForSeconds(.25f);thrower.Throw(1);
            until=Time.time+4;
            while(goo&&!goo.IsDead&&Time.time<until)yield return null;
            Check(!goo||goo.IsDead,(aboard?"island":"static")+" full-force throw splats; last hit "+thrower.LastHit);
        }
        Destroy(floor);member.allowAutomaticEntry=true;
        Result="PASS: ordinary drops and actual full-force scripted throws splat on static ground and moving island";
    }
    static void Check(bool ok,string label){if(ok)return;Result="FAIL: "+label;throw new InvalidOperationException(Result);}
}
