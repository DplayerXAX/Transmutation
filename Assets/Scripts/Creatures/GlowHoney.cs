using UnityEngine;

/// <summary>A luminous, carryable Plant food produced only after nectar fermentation.</summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class GlowHoney : Creature
{
    public enum Use { Drink, Coat }
    private bool used;
    public override bool CanUseWhileCarried => !IsDead && !used;
    protected override void InitializeCreature()
    {
        SetCreatureName("Glow Honey");
        SetFoodAndDiet(FoodTag.Plant, FoodTag.None);
        GetComponent<SphereCollider>().radius = 0.18f;
        GetComponent<Rigidbody>().mass = 0.2f;
    }
    public override string CarriedPrompt => "[LMB] Drink: night vision    [F] Apply: glow and scent";
    public override string InteractionPrompt => IsCarried && !IsDead ? "Drink honey" : null;
    public override void ReceiveInteraction(CreatureInteraction interaction) => TryUse(Use.Drink);
    public bool TryUse(Use use)
    {
        if (used || IsDead || IsConsumptionPending || !SimulationEnabled || !IsCarried ||
            !isActiveAndEnabled || (use != Use.Drink && use != Use.Coat)) return false;
        var effects = PlayerHoneyEffects.Ensure(Carrier);
        if (effects == null || !effects.TryApply(use)) return false;
        used = true;
        CompleteConsumption(null);
        return true;
    }
    protected override void OnCarryStarted()
    {
        transform.SetParent(null, true);
        SetMovementPhysics(false, true);
    }
    protected override void TickCreature(float deltaTime)
    {
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (Application.isFocused && keyboard != null && keyboard.fKey.wasPressedThisFrame &&
            !BoneAwakeningCutscene.Playing && BoneRide.Mount == null) TryUse(Use.Coat);
    }
}
