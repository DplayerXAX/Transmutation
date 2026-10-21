using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody), typeof(AsuraMotor), typeof(CreatureCombat))]
[RequireComponent(typeof(ProjectileAttack))]
public sealed class GeneralAsura : Creature
{
    [SerializeField] private BlastCore blastCorePrefab;
    private CreatureCombat combat;
    public BlastCore DroppedCore { get; private set; }
    protected override void InitializeCreature()
    { SetCreatureName("General Asura"); SetFoodAndDiet(FoodTag.Meat, FoodTag.None); combat = GetComponent<CreatureCombat>(); }
    protected override void TickCreature(float deltaTime) => combat.Tick(deltaTime);
    protected override void OnSimulationChanged() { if (!SimulationEnabled && combat != null) combat.Cancel(); }
    protected override void OnDeath()
    {
        combat.Cancel();
        if (DeathCause != CreatureDeathCause.Damage || blastCorePrefab == null) return;
        Vector3 drop = AimPosition + Vector3.up * 0.6f;
        if (CreatureSpatialQuery.TryGetSolid(this, out var solid)) drop.y = solid.bounds.max.y + 0.4f;
        DroppedCore = Instantiate(blastCorePrefab, drop, Quaternion.identity);
        SceneManager.MoveGameObjectToScene(DroppedCore.gameObject, gameObject.scene);
        if (DroppedCore.TryGetComponent<Rigidbody>(out var body)) body.linearVelocity = Vector3.up * 2f;
    }
}
