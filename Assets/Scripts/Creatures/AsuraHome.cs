using UnityEngine;

public sealed class AsuraHome : AsuraLinkage
{
    [Min(1)] public int populationCap=42;
    [Min(0.01f)] public float spawnsPerSecond=0.65f;
    public override bool Permanent=>true;
    public int Living {get; internal set;}
    public float TerritoryArea {get; internal set;}
    public int Spawned {get; internal set;}
    internal float budget, expansionClock;
    protected override void InitializeCreature(){base.InitializeCreature();home=this;}
}
