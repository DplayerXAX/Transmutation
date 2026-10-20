public sealed class ExplosiveProjectile : ProjectileCreature
{
    protected override void OnFlightImpact(Creature struck)
    {
        Detonate();
        Die();
    }
    protected override void OnDeath()
    {
        if (DeathCause == CreatureDeathCause.Damage) Detonate();
    }
    private void Detonate()
    {
        if (FlightState != ProjectileFlightState.Flying) return;
        SetFlightState(ProjectileFlightState.Resolved);
        ExplosionResolver.Request(gameObject.scene, Body.position, BlastRadius, ImpactDamage, BlastPush, Emitter, this);
    }
}
