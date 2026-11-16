public sealed class BasicProjectile : ProjectileCreature
{
    protected override void OnFlightImpact(ICombatTarget struck)
    {
        // Commit before callbacks: compound contacts and listener re-entry cannot apply another hit.
        SetFlightState(ProjectileFlightState.Resolved);
        if (CombatTarget.Exists(struck) && struck.ApplyDamage(ImpactDamage, Emitter) && struck.CombatRoot.TryGetComponent<UnityEngine.Rigidbody>(out var body))
            CreatureImpulse.Apply(body, transform.forward * 1.4f, 0.12f);
        Die();
    }
}
