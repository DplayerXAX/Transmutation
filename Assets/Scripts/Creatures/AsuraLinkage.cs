using System.Collections.Generic;
using UnityEngine;

public enum AsuraLinkState { Forming, Active, Disconnected, Collapsing }

/// <summary>A rooted, damageable territory organism. Permanent homes remain protected.</summary>
public class AsuraLinkage : Creature
{
    public AsuraHome home;
    public int surfaceIndex=-1;
    public AsuraLinkState state=AsuraLinkState.Active;
    public bool Connected {get; internal set;}
    public float Formation {get; internal set;}
    public float Pressure {get; internal set;}
    public float DisconnectedTime {get; internal set;}
    public virtual bool Permanent=>false;
    public override bool Invulnerable=>Permanent;
    public float LastAttackedAt {get; private set;}=float.NegativeInfinity;
    public Creature LastAttacker {get; private set;}
    public float SpawnWeight=>Time.time-LastAttackedAt<8?0.2f:1;
    public override bool CanBeCarried=>false;
    public override bool CanBeCaptured=>false;
    public override bool CanBeConsumed=>false;
    protected override bool KeepCorpseStationary=>true;
    internal Dictionary<int,float> distances;
    internal readonly List<int> patch=new List<int>();
    internal readonly List<int> vinePatch=new List<int>();
    private Renderer[] visuals;
    private MaterialPropertyBlock tint;
    protected override void InitializeCreature()
    {
        SetCreatureName(Permanent?"Asura Home":"Asura Linkage");SetFoodAndDiet(FoodTag.None,FoodTag.None);
        ConfigureLifespan(1,0);visuals=GetComponentsInChildren<Renderer>();tint=new MaterialPropertyBlock();
        Damaged-=OnDamaged;Damaged+=OnDamaged;
    }
    private void OnDamaged(Creature victim,float amount,Creature source)
    {LastAttackedAt=Time.time;LastAttacker=source;}
    protected override void OnDeath()
    {GetComponentInParent<AsuraTerritory>()?.LinkageDied(this);}
    protected override void TickCreature(float dt)
    {
        if(home==null)return;
        SetResolvedPolarity(home.Polarity);
        Color color=AsuraBloodPalette.For(home.Polarity);
        float intensity=state==AsuraLinkState.Disconnected?0.18f:state==AsuraLinkState.Forming?0.35f+Formation*0.07f:1;
        intensity*=1-Mathf.Clamp01(Pressure/12)*0.6f;
        color*=intensity*(0.94f+0.06f*Mathf.Sin(Time.time*2));
        tint.SetColor("_BaseColor",color);tint.SetColor("_Color",color);
        foreach(var visual in visuals)if(visual!=null)visual.SetPropertyBlock(tint);
    }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color=home!=null && home.Polarity==CreaturePolarity.Dark?Color.magenta:Color.yellow;
        var territory=GetComponentInParent<AsuraTerritory>();
        Gizmos.DrawWireSphere(transform.position,territory!=null?territory.influenceRadius:65);
    }
}
