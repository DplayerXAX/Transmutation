using UnityEngine;

public sealed class MeleeAttack : CreatureAttack
{
    [SerializeField, Min(0f)] private float contactGap = 0.18f;
    [SerializeField, Min(0f)] private float knockback = 3.2f;
    private RaycastHit[] hits = new RaycastHit[16];

    public override bool InReach(Creature owner, Creature target)
    {
        if (!CreatureSpatialQuery.TryGetSolid(owner, out var a) || !CreatureSpatialQuery.TryGetSolid(target, out var b)) return false;
        Vector3 pointB = b.ClosestPoint(a.bounds.center);
        Vector3 pointA = a.ClosestPoint(pointB);
        pointB = b.ClosestPoint(pointA);
        return (pointB - pointA).sqrMagnitude <= contactGap * contactGap;
    }

    public override bool Release(Creature owner, Creature target, Transform muzzle)
    {
        if (!InReach(owner, target) || !CreatureSpatialQuery.Visible(owner, target, owner.AimPosition, ref hits)) return false;
        Vector3 direction = Vector3.ProjectOnPlane(target.AimPosition - owner.AimPosition, Vector3.up).normalized;
        if (direction.sqrMagnitude < 0.001f) direction = owner.transform.forward;
        if (Vector3.Dot(owner.transform.forward, direction) < 0.25f || !target.ApplyDamage(Damage, owner)) return false;
        if (target.TryGetComponent<Rigidbody>(out var body))
        {
            bool grounded = target.TryGetComponent<AsuraMotor>(out var motor) && motor.Grounded;
            float hop = grounded ? Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * 0.08f) : 0f;
            CreatureImpulse.Apply(body, direction * knockback + Vector3.up * hop, 0.18f);
        }
        return true;
    }
}
