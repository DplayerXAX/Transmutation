#if UNITY_EDITOR
using UnityEngine;

/// <summary>Disposable fixture for shared carried-input overrides and throw preparation.</summary>
public sealed class CarryThrowValidationProbe : Creature
{
    public bool ConsumeRightMouse;
    public bool Usable;
    public int Preparations;
    public override bool CanUseWhileCarried => Usable;
    public override bool TryHandleCarriedRightMouse(PlayerCreatureCarrier carrier, bool pressed, bool held, bool released)
        => ConsumeRightMouse;
    public override void PrepareForThrow(PlayerCreatureCarrier carrier) => Preparations++;
    protected override void TickCreature(float deltaTime) { }
}
#endif
