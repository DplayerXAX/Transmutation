using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(AsuraMotor), typeof(CreatureCombat))]
[RequireComponent(typeof(ProjectileAttack))]
public sealed class RangedAsura : Creature
{
    private CreatureCombat combat;
    protected override void InitializeCreature()
    { SetCreatureName("Ranged Asura"); SetFoodAndDiet(FoodTag.Meat, FoodTag.None); combat = GetComponent<CreatureCombat>(); }
    protected override void TickCreature(float deltaTime) => combat.Tick(deltaTime);
    protected override void OnDeath() => combat.Cancel();
    protected override void OnSimulationChanged() { if (!SimulationEnabled && combat != null) combat.Cancel(); }
}
