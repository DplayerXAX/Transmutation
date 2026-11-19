using System.Collections;
using UnityEngine;

/// <summary>Starts each habitat only after its streamed world terrain has physics surfaces.</summary>
[DefaultExecutionOrder(1600)]
public sealed class WorldForestHabitat : MonoBehaviour
{
    public SquirrelSystemManager squirrels;
    public GlowBeeColony[] colonies;
    public SquirrelTree[] treeRoots;
    public bool Ready { get; private set; }
    void Awake()
    {
        if(squirrels!=null)squirrels.enabled=false;
        foreach(var colony in colonies)if(colony!=null)colony.enabled=false;
    }
    IEnumerator Start()
    {
        while(!Ready)
        {
            int grounded=0;
            for(int i=0;i<treeRoots.Length;i+=Mathf.Max(1,treeRoots.Length/8))
            {
                if(treeRoots[i]==null)continue;var point=treeRoots[i].GroundPosition;
                if(gameObject.scene.GetPhysicsScene().Raycast(point+Vector3.up*4,Vector3.down,out var hit,9,1<<7,QueryTriggerInteraction.Ignore)&&Mathf.Abs(hit.point.y-point.y)<1.5f)grounded++;
            }
            if(grounded>=3)
            {
                squirrels.enabled=true;squirrels.InitializeSystem();
                if(squirrels.IsInitialized)
                {
                    foreach(var colony in colonies)if(colony!=null)colony.enabled=true;
                    Ready=true;
                }
            }
            if(!Ready)yield return new WaitForSeconds(.5f);
        }
    }
}
