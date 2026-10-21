public sealed class BasicProjectile : ProjectileCreature
{
    protected override void OnFlightImpact(Creature struck)
    {
        // Commit before callbacks: compound contacts and listener re-entry cannot apply another hit.
        SetFlightState(ProjectileFlightState.Resolved);
        if (struck != null) struck.ApplyDamage(ImpactDamage, Emitter);
        Die();
    }
}
