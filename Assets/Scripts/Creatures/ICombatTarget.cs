using UnityEngine;

/// <summary>A damage receiver, independent of creature food, capture and corpse ownership.</summary>
public interface ICombatTarget
{
    Component TargetComponent { get; }
    Transform CombatRoot { get; }
    Vector3 AimPosition { get; }
    CreaturePolarity Polarity { get; }
    bool IsCombatActive { get; }
    bool CanBeAITargeted { get; }
    bool ApplyDamage(float amount, Creature source = null);
}

public static class CombatTarget
{
    // Interface != null alone does not honor Unity's destroyed-object semantics.
    public static bool Exists(ICombatTarget target) => target != null && target.TargetComponent != null;
    public static bool Eligible(Creature owner, ICombatTarget target) => owner != null && Exists(target) &&
        target.TargetComponent != owner && target.IsCombatActive && target.CanBeAITargeted &&
        target.CombatRoot.gameObject.scene == owner.gameObject.scene && owner.CanBeAITargeted &&
        target.Polarity != owner.Polarity;

    public static ICombatTarget Owner(Collider collider)
    {
        if (collider == null) return null;
        // A carried creature keeps its own damage identity, even while parented to a player.
        var creature = collider.GetComponentInParent<Creature>();
        if (creature != null) return creature;
        return collider.GetComponentInParent<PlayerCreatureStats>();
    }
}
