#if UNITY_EDITOR
using UnityEngine;

/// <summary>Disposable fixture for shared carried-input overrides and throw preparation.</summary>
public sealed class CarryThrowValidationProbe : Creature
{
    public bool ConsumeRightMouse;
    public bool Usable;
    public int Preparations;
    public int Impacts;
    public int CollisionEntries;
    public CreatureImpact LastImpact;
    public Vector3 LastCollisionRelativeVelocity;
    public System.Action<CreatureImpact> ImpactAction;
    public override bool CanUseWhileCarried => Usable;
    public override bool TryHandleCarriedRightMouse(PlayerCreatureCarrier carrier, bool pressed, bool held, bool released)
        => ConsumeRightMouse;
    public override void PrepareForThrow(PlayerCreatureCarrier carrier) => Preparations++;
    public override void ReceiveImpact(CreatureImpact impact)
    {
        Impacts++;
        LastImpact = impact;
        ImpactAction?.Invoke(impact);
    }
    private void OnCollisionEnter(Collision collision)
    {
        CollisionEntries++;
        LastCollisionRelativeVelocity = collision.relativeVelocity;
    }
    protected override void TickCreature(float deltaTime) { }
}
#endif
