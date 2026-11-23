using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

/// <summary>Isolates gait intent from combat decisions; never saved into a scene.</summary>
public sealed class AsuraIslandGaitProbe : MonoBehaviour
{
    public GameObject prefab;
    public static string Result;
    IEnumerator Start()
    {
        Result="Running";
        var frame=FindFirstObjectByType<MovingFrame>();
        var go=Instantiate(prefab,frame.transform.TransformPoint(new Vector3(-20,1,-12)),Quaternion.identity);
        var creature=go.GetComponent<Creature>(); creature.enabled=false;
        var body=go.GetComponent<Rigidbody>();body.linearVelocity=frame.VelocityAt(body.position);
        go.AddComponent<MovementFrameMember>().Join(frame);
        var motor=go.GetComponent<AsuraMotor>();motor.Stop();
        var presentation=go.GetComponent<AsuraPresentation>();
        var stride=typeof(AsuraPresentation).GetField("stride",BindingFlags.Instance|BindingFlags.NonPublic);
        yield return new WaitForSeconds(2);
        float phase=(float)stride.GetValue(presentation);
        yield return new WaitForSeconds(2);
        Check(!motor.Locomoting && Mathf.Abs((float)stride.GetValue(presentation)-phase)<.001f,"passive transport does not advance stride");
        float until=Time.time+1;
        while(Time.time<until){motor.SetIntent(frame.transform.forward*2,frame.transform.forward);yield return new WaitForFixedUpdate();}
        Check((float)stride.GetValue(presentation)>phase+.1f,"intentional walking advances stride");
        motor.Stop(); yield return new WaitForSeconds(.5f);
        phase=(float)stride.GetValue(presentation);
        yield return new WaitForSeconds(1);
        Check(Mathf.Abs((float)stride.GetValue(presentation)-phase)<.001f,"stop clears gait");
        Result="PASS: idle transport, intentional walking, stopped gait";
        Destroy(go);
    }
    static void Check(bool ok,string message){if(ok)return;Result="FAIL: "+message;throw new InvalidOperationException(Result);}
}
