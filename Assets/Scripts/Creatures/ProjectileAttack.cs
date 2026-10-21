using UnityEngine;

public sealed class ProjectileAttack : CreatureAttack
{
    [SerializeField] private ProjectileCreature projectilePrefab;
    [Min(0.1f)] [SerializeField] private float projectileSpeed = 18f;
    [Min(0.1f)] [SerializeField] private float blastRadius = 4f;
    [Min(0f)] [SerializeField] private float blastPush = 12f;
    private RaycastHit[] hits = new RaycastHit[16];
    public ProjectileCreature ProjectilePrefab => projectilePrefab;

    public override bool Release(Creature owner, Creature target, Transform muzzle)
    {
        if (projectilePrefab == null || target == null || muzzle == null ||
            !CreatureSpatialQuery.Visible(owner, target, muzzle.position, ref hits)) return false;
        return Fire(owner, muzzle.position, (target.AimPosition - muzzle.position).normalized) != null;
    }

    public ProjectileCreature Fire(Creature owner, Vector3 origin, Vector3 direction, GameObject firingPlayer = null)
    {
        if (projectilePrefab == null || direction.sqrMagnitude < 0.001f) return null;
        return ProjectileCreature.Spawn(projectilePrefab, owner, firingPlayer, origin, direction.normalized * projectileSpeed,
            Damage, blastRadius, blastPush);
    }
}
