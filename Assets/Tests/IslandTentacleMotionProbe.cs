using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

[DefaultExecutionOrder(20000)]
public sealed class IslandTentacleMotionProbe : MonoBehaviour
{
    public GameObject prefab;
    public static string Result;
    IEnumerator Start()
    {
        Result="Running";var frame=FindFirstObjectByType<MovingFrame>();
        var go=Instantiate(prefab,frame.transform.TransformPoint(new Vector3(-18,1,-10)),Quaternion.identity);
        var creature=go.GetComponent<TentacleCreature>();
        foreach(string field in new[]{"moveSpeed","wanderRadius","bobAmplitude"})
            typeof(TentacleCreature).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(creature,0f);
        go.AddComponent<MovementFrameMember>().Join(frame);
        yield return new WaitForSeconds(3);
        Vector3 pinLocal=new(-17,0,-8);int arm=creature.PinArm(frame.transform.TransformPoint(pinLocal),frame.deck.transform);
        Check(arm>=0,"arm can attach to deck");
        yield return new WaitForSeconds(.5f);
        float until=Time.time+3,maxStep=0,maxPinError=0; Vector3 maximumDelta=Vector3.zero;
        Vector3 previous=frame.transform.InverseTransformPoint(go.transform.position);
        while(Time.time<until)
        {
            yield return null;
            Vector3 current=frame.transform.InverseTransformPoint(go.transform.position);
            if(Vector3.Distance(current,previous)>maxStep){maxStep=Vector3.Distance(current,previous);maximumDelta=current-previous;}previous=current;
            Check(creature.ArmPin(arm,out var pin),"attachment retained");
            maxPinError=Mathf.Max(maxPinError,Vector3.Distance(frame.transform.InverseTransformPoint(pin),pinLocal));
        }
        Check(maxStep<.015f && maxPinError<.001f,$"steady body and support-local pin: {maxStep:F5}/{maxPinError:F5}m; delta {maximumDelta:F5}");
        Result=$"PASS: rendered tentacle body max step {maxStep:F5}m; pin error {maxPinError:F5}m";
        Destroy(go);
    }
    static void Check(bool ok,string label){if(ok)return;Result="FAIL: "+label;throw new InvalidOperationException(Result);}
}
