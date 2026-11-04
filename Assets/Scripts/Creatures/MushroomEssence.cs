using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A glowing bead that forms on a grown Mushroom Tree's cap. Picked and eaten (LMB while held), it gives
/// stronger jumps for a while. Left alone, enough of it condenses into a BounceShroom: on the cap the tree
/// does that, and loose essence resting together on the ground does it by itself.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class MushroomEssence : Creature
{
    [Header("Eating")]
    [Tooltip("Jump speed multiplier while the effect lasts (jump height grows with its square).")]
    [Min(1f)] [SerializeField] private float jumpMultiplier = 1.5f;
    [Min(1f)] [SerializeField] private float boostSeconds = 20f;

    [Header("Condensing")]
    [SerializeField] private BounceShroom condensesInto;
    [Tooltip("Loose essence resting this close together condenses...")]
    [Min(0.1f)] [SerializeField] private float condenseRadius = 1.2f;
    [Tooltip("...once there is this much of it.")]
    [Min(2)] [SerializeField] private int condenseCount = 3;

    [Header("Runtime (Read Only)")]
    [SerializeField] private bool onStalk;

    private Rigidbody body;
    private float nextCheck;
    private static readonly Collider[] nearby = new Collider[32];

    /// <summary>Still held on the cap where it formed; the first pickup frees it.</summary>
    public bool OnStalk => onStalk;
    public float JumpMultiplier => jumpMultiplier;
    public float BoostSeconds => boostSeconds;

    public override bool CanUseWhileCarried => !IsDead;
    public override string InteractionPrompt => IsCarried && !IsDead ? "Eat essence" : null;
    public override string CarriedPrompt => "[LMB] Eat: higher jumps for a while";

    protected override void InitializeCreature()
    {
        SetCreatureName("Mushroom Essence");
        SetFoodAndDiet(FoodTag.Plant, FoodTag.None);
        body = GetComponent<Rigidbody>();
    }

    /// <summary>Held where it formed, like fruit on a stem, until picked.</summary>
    public void HoldOnStalk()
    {
        onStalk = true;
        SetMovementPhysics(true, false);
    }

    public override void ReceiveInteraction(CreatureInteraction interaction) => TryEat();

    public bool TryEat()
    {
        if (!IsCarried || IsDead || IsConsumptionPending || !SimulationEnabled) return false;
        SmoothFirstPersonController controller = Carrier.PlayerController;
        if (controller == null) return false;
        JumpBoostEffect.Apply(controller, jumpMultiplier, boostSeconds);
        CompleteConsumption(null);
        return true;
    }

    /// <summary>Removed into a BounceShroom that someone else spawns.</summary>
    internal void CondenseAway()
    {
        if (!IsConsumptionPending) CompleteConsumption(null);
    }

    protected override void OnCarryStarted()
    {
        onStalk = false;
        SetMovementPhysics(false, true);
    }

    protected override void TickCreature(float deltaTime)
    {
        if (onStalk || IsCarried || condensesInto == null || Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f + Random.value * 0.2f;
        if (!Resting(this)) return;

        var group = new List<MushroomEssence> { this };
        int count = Physics.OverlapSphereNonAlloc(transform.position, condenseRadius, nearby, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var other = nearby[i].GetComponentInParent<MushroomEssence>();
            if (other == null || other == this || group.Contains(other) || other.onStalk || other.IsCarried ||
                other.IsConsumptionPending || other.IsDead || !Resting(other)) continue;
            // One essence speaks for the pile, so it condenses once.
            if (other.GetInstanceID() < GetInstanceID()) return;
            group.Add(other);
        }
        if (group.Count < condenseCount) return;

        Vector3 centre = Vector3.zero;
        for (int i = 0; i < condenseCount; i++) centre += group[i].transform.position;
        centre /= condenseCount;
        BounceShroom prefab = condensesInto;
        for (int i = condenseCount - 1; i >= 0; i--) group[i].CondenseAway();
        SpawnShroom(prefab, centre + Vector3.up * 0.3f);
    }

    /// <summary>A BounceShroom condensed from essence.</summary>
    internal static BounceShroom SpawnShroom(BounceShroom prefab, Vector3 at)
    {
        BounceShroom shroom = Instantiate(prefab, at, Quaternion.identity);
        shroom.name = "BounceShroom (condensed)";
        return shroom;
    }

    private static bool Resting(MushroomEssence essence) =>
        essence.body == null || essence.body.isKinematic || essence.body.linearVelocity.sqrMagnitude < 0.05f;
}
