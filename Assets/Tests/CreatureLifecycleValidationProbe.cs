#if UNITY_EDITOR
using UnityEngine;

/// <summary>Runtime-attachable fixture, excluded from player builds and never used by game assets.</summary>
public sealed class CreatureLifecycleValidationProbe : Creature
{
    public int DeathCount { get; private set; }
    public int AgeDeathCount { get; private set; }
    public bool AgeDeathObservedCommittedState { get; private set; }
    public int ConsumptionCount { get; private set; }
    public Creature LastConsumer { get; private set; }

    public override void Consume(Creature consumer)
    {
        ConsumptionCount++;
        LastConsumer = consumer;
    }

    public override void Die()
    {
        if (IsDead) return;
        DeathCount++;
        base.Die();
    }

    protected override void TickCreature(float deltaTime) { }

    protected override void OnAgeDeath()
    {
        AgeDeathCount++;
        AgeDeathObservedCommittedState = IsDead && !SimulationEnabled && Age == 0f;
    }
}
#endif
