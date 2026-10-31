using UnityEngine;

/// <summary>Starts the authored encounter once the player reaches its entrance.</summary>
public sealed class DanielBasinEncounter : MonoBehaviour
{
    public Transform entrance;
    public float activationDistance = 24f;
    private Creature[] units;
    private bool activated;
    private Rigidbody[] bodies;
    private bool[] kinematic;
    private ProceduralWorld world;
    public bool Activated => activated;
    private void Start()
    {
        world = FindFirstObjectByType<ProceduralWorld>();
        units=GetComponentsInChildren<Creature>();
        foreach(var unit in units) unit.SetSimulationEnabled(false);
        bodies = GetComponentsInChildren<Rigidbody>();
        kinematic = new bool[bodies.Length];
        for (int i = 0; i < bodies.Length; i++)
        {
            kinematic[i] = bodies[i].isKinematic;
            bodies[i].isKinematic = true;
        }
    }
    private void Update()
    {
        if(activated || entrance==null || Camera.main==null) return;
        if(world!=null && !world.IsRegionReady(transform.position,32f)) return;
        if(Vector3.Distance(Camera.main.transform.position,entrance.position)>activationDistance) return;
        activated=true;
        for(int i=0;i<bodies.Length;i++) if(bodies[i]!=null) bodies[i].isKinematic=kinematic[i];
        foreach(var unit in units) if(unit!=null && !unit.IsDead) unit.SetSimulationEnabled(true);
    }
}
