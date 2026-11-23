using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(20000)]
public sealed class IslandFlyingMotionProbe : MonoBehaviour
{
    public static string Result;
    SeaAngelCreature fish; MovingFrame frame;
    Vector3 previous; bool measuring;
    float maxExcess, maxSpeed;int count,oldCap;
    IEnumerator Start()
    {
        Result="Running"; oldCap=Application.targetFrameRate;
        fish=FindFirstObjectByType<SeaAngelCreature>(); frame=fish.GetComponent<MovementFrameMember>().Frame;
        CreatureSimulation.Wake(fish,30);
        yield return new WaitForSeconds(2);
        Result="";
        foreach(int rate in new[]{30,60,120})
        {
            Application.targetFrameRate=rate; maxExcess=maxSpeed=0;count=0;
            previous=frame.transform.InverseTransformPoint(fish.transform.position);measuring=true;
            yield return new WaitForSeconds(2);measuring=false;
            Result+=$"{rate}: max local speed {maxSpeed:F3}m/s; excess above .65m/s envelope {maxExcess:F5}m; samples {count}\n";
        }
        Application.targetFrameRate=oldCap;Result="COMPLETE\n"+Result;
    }
    void LateUpdate()
    {
        if(!measuring||!fish||Time.deltaTime<=0)return;
        Vector3 point=frame.transform.InverseTransformPoint(fish.transform.position);
        float step=Vector3.Distance(point,previous);previous=point;count++;
        maxSpeed=Mathf.Max(maxSpeed,step/Time.deltaTime);
        maxExcess=Mathf.Max(maxExcess,step-.65f*Time.deltaTime);
    }
    void OnDestroy(){Application.targetFrameRate=oldCap;}
}
