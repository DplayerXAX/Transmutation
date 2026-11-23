using System.Collections;
using UnityEngine;

/// <summary>Opt-in sampling of idle physics versus rendered local motion; no saved scene attachment.</summary>
[DefaultExecutionOrder(20000)]
public sealed class IslandMotionSamplingProbe : MonoBehaviour
{
    public static string Result;
    class Sample
    {
        public Rigidbody body;
        public Vector3 lastRender, lastPhysics;
        public float renderSq, physicsSq, maxRender, maxPhysics, fixedTime=-1;
        public int renders, physics;
        public bool ready;
    }
    MovingFrame frame;
    Sample kinetic, dynamic;
    bool sampling;
    int oldTarget;
    GameObject Make(string name, Vector3 local, bool kinematic)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube); go.name=name;
        go.transform.position=frame.transform.TransformPoint(local);
        var body=go.AddComponent<Rigidbody>(); body.isKinematic=kinematic;body.useGravity=!kinematic;
        body.interpolation=kinematic?RigidbodyInterpolation.None:RigidbodyInterpolation.Interpolate;
        body.constraints=RigidbodyConstraints.FreezeRotation;
        if(!kinematic)body.linearVelocity=frame.VelocityAt(body.position);
        go.AddComponent<MovementFrameMember>().Join(frame);
        return go;
    }
    IEnumerator Start()
    {
        Result="Running";oldTarget=Application.targetFrameRate;
        frame=FindFirstObjectByType<MovingFrame>();
        kinetic=new Sample{body=Make("Sampling kinematic passenger",new Vector3(-18,2,-19),true).GetComponent<Rigidbody>()};
        dynamic=new Sample{body=Make("Sampling dynamic passenger",new Vector3(-18,1,-23),false).GetComponent<Rigidbody>()};
        yield return new WaitForSeconds(5);
        Result="";
        foreach(int rate in new[]{30,60,120})
        {
            Application.targetFrameRate=rate;
            Reset(kinetic);Reset(dynamic);sampling=true;
            yield return new WaitForSeconds(3);
            sampling=false;
            Result += $"target {rate}: kine {Describe(kinetic)}; dynamic {Describe(dynamic)}\n";
        }
        Result="COMPLETE\n"+Result;
        Application.targetFrameRate=oldTarget;
        Destroy(kinetic.body.gameObject);Destroy(dynamic.body.gameObject);
    }
    static void Reset(Sample s){s.ready=false;s.renderSq=s.physicsSq=s.maxRender=s.maxPhysics=0;s.renders=s.physics=0;s.fixedTime=-1;}
    static string Describe(Sample s)=>$"render RMS/max {Mathf.Sqrt(s.renderSq/Mathf.Max(1,s.renders)):F5}/{s.maxRender:F5}m, physics RMS/max {Mathf.Sqrt(s.physicsSq/Mathf.Max(1,s.physics)):F5}/{s.maxPhysics:F5}m, render samples {s.renders}";
    void LateUpdate(){if(sampling){Measure(kinetic);Measure(dynamic);}}
    void Measure(Sample s)
    {
        var platform=frame.GetComponent<Rigidbody>();
        Vector3 render=frame.transform.InverseTransformPoint(s.body.transform.position);
        Vector3 physics=Quaternion.Inverse(platform.rotation)*(s.body.position-platform.position);
        if(s.ready)
        {
            float step=(render-s.lastRender).magnitude;s.renderSq+=step*step;s.maxRender=Mathf.Max(s.maxRender,step);s.renders++;
            if(s.fixedTime!=Time.fixedTime){step=(physics-s.lastPhysics).magnitude;s.physicsSq+=step*step;s.maxPhysics=Mathf.Max(s.maxPhysics,step);s.physics++;}
        }
        s.lastRender=render;
        if(s.fixedTime!=Time.fixedTime){s.lastPhysics=physics;s.fixedTime=Time.fixedTime;}
        s.ready=true;
    }
    void OnDestroy(){Application.targetFrameRate=oldTarget;}
}
