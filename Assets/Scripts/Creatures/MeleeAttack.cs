using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class MeleeAttack : CreatureAttack
{
    private Collider[] overlaps = new Collider[16];
    private RaycastHit[] hits = new RaycastHit[16];
    public override bool Release(Creature owner, Creature target, Transform muzzle)
    {
        if (target == null || !CreatureSpatialQuery.Visible(owner, target, owner.AimPosition, ref hits)) return false;
        Vector3 delta = target.AimPosition - owner.AimPosition;
        if (delta.sqrMagnitude > Range * Range) return false;
        Vector3 center = owner.AimPosition + delta.normalized * Range * 0.5f;
        int count;
        var physics = owner.gameObject.scene.GetPhysicsScene();
        while ((count = physics.OverlapSphere(center, Range * 0.5f + 0.1f, overlaps, ~0,
                   QueryTriggerInteraction.Ignore)) == overlaps.Length) Array.Resize(ref overlaps, overlaps.Length * 2);
        for (int i = 0; i < count; i++)
            if (CreatureSpatialQuery.Owner(overlaps[i]) == target) return target.ApplyDamage(Damage, owner);
        return false;
    }
}
