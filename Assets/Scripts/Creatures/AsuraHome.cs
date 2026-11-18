using UnityEngine;

public sealed class AsuraHome : AsuraLinkage
{
    [Min(1)] public int populationCap=42;
    [Min(0.01f)] public float spawnsPerSecond=0.65f;
    public override bool Permanent=>true;
    [SerializeField] private int living;
    [SerializeField] private float territoryArea;
    public int Living {get=>living; internal set=>living=value;}
    public float TerritoryArea {get=>territoryArea; internal set=>territoryArea=value;}
    public int Spawned {get; internal set;}
    internal float budget, expansionClock;
    protected override void InitializeCreature(){base.InitializeCreature();home=this;}
}
