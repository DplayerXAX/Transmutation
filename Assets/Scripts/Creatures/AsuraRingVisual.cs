using UnityEngine;

/// <summary>One combined sculpture per Asura, preserving combat and collision roots.</summary>
[DisallowMultipleComponent]
public sealed class AsuraRingVisual : MonoBehaviour
{
    private Transform rings;
    private Creature owner;
    private bool launcher;
    private Mesh mesh;
    private void Start()
    {
        owner=GetComponent<Creature>(); if(owner==null) return;
        foreach(var renderer in GetComponentsInChildren<Renderer>()) renderer.enabled=false;
        launcher=owner is BlastCore;
        bool general=owner is GeneralAsura, projectile=owner is ProjectileCreature;
        float size=general ? .78f : launcher ? .24f : projectile ? .1f : .46f;
        var art=new CreatureSculpture();
        art.Orb(Vector3.zero,Vector3.one*size*.55f,CreatureArtPalette.Pearl);
        int count=general?4:owner is MeleeAsura?2:3;
        for(int j=0;j<count;j++)
        {
            Quaternion turn=launcher||projectile?Quaternion.identity:Quaternion.Euler(j*48,j*57,j*20);
            Vector3 center=launcher||projectile?Vector3.forward*(j-1)*size:Vector3.zero;
            float radius=size*(1+j*.18f);
            art.Ring(center,turn,radius,size*.045f,j%2==0?Color.white:new Color(.6f,.62f,.75f));
            for(int i=0;i<5;i++)
            {
                float a=i*Mathf.PI*2/5;
                art.Orb(center+turn*new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*radius,Vector3.one*size*.12f,CreatureArtPalette.Pearl);
            }
        }
        if(owner is MeleeAsura) for(int i=-1;i<=1;i+=2)
            art.Orb(new Vector3(i*size,-size*.7f,0),new Vector3(size*.4f,size*.3f,size*.7f),Color.white);
        var sculpture=art.Attach(transform,"Ring Sculpture"); rings=sculpture.transform;
        rings.localPosition=Vector3.up*(launcher||projectile?0:general ? .95f : .62f);
        if(owner is RangedAsura) rings.localScale=new Vector3(.8f,1.2f,.85f);
        mesh=sculpture.GetComponent<MeshFilter>().sharedMesh;
        GetComponent<AsuraPresentation>()?.BindVisual(rings,sculpture.GetComponents<Renderer>());
    }
    private void Update()
    {
        if(rings==null||owner==null||owner.IsDead||!owner.SimulationEnabled) return;
        rings.Rotate(launcher||owner is ProjectileCreature?Vector3.forward:Vector3.up,12*Time.deltaTime,Space.Self);
    }
    private void OnDestroy() { if(mesh!=null) Destroy(mesh); }
}

