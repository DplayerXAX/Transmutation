using UnityEngine;

public sealed class DimensionStone : Creature
{
    [Min(0)] public float playerRange = 20, fishRange = 24;
    public override bool CanBeCarried => false;
    protected override void TickCreature(float dt) { }
    internal MeshFilter Window { get; private set; }
    protected override void OnEnable()
    {
        base.OnEnable();
        Window = GetComponent<MeshFilter>();
        if (Application.isPlaying) DimensionalSchool.For(this).Stones.Add(this);
    }
    protected override void OnDisable()
    {
        DimensionalSchool.Existing(this)?.Stones.Remove(this);
        base.OnDisable();
    }
}
