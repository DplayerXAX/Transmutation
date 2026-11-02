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
        if (Luminosity <= 0f) SetLuminosity(50f);
        GetComponent<SphereCollider>().radius = 0.18f;
        GetComponent<Rigidbody>().mass = 0.2f;
    }
    public override string CarriedPrompt => "[LMB] Coat aimed creature / surface with glowing honey";
    public override string InteractionPrompt => !IsCarried && !IsDead ? "Eat honey    [Hold LMB] Coat yourself" : null;
    public override void ReceiveInteraction(CreatureInteraction interaction)
    {
        if (!IsCarried && interaction.Source != null)
            TryUse(Use.Drink, interaction.Source.GetComponentInParent<PlayerCreatureCarrier>());
    }
    public bool TryUse(Use use)
        => TryUse(use, Carrier);

    public bool TryUse(Use use, PlayerCreatureCarrier player)
    {
        if (used || IsDead || IsConsumptionPending || !SimulationEnabled || player == null ||
            (IsCarried && Carrier != player) ||
            !isActiveAndEnabled || (use != Use.Drink && use != Use.Coat)) return false;
        var effects = PlayerHoneyEffects.Ensure(player);
        if (effects == null || !effects.TryApply(use)) return false;
        return FinishUse();
    }

    public bool TryCoat(GameObject target, Vector3 point)
    {
        if (used || IsDead || IsConsumptionPending || !SimulationEnabled || !IsCarried ||
            !isActiveAndEnabled || target == null || !target.activeInHierarchy ||
            target.scene != gameObject.scene || target.transform.IsChildOf(transform)) return false;
        HoneyLuminosityEffect.Apply(target, 50f, 60f, point);
        return FinishUse();
    }

    private bool FinishUse()
    {
        used = true;
        CompleteConsumption(null);
        return true;
    }
    protected override void OnCarryStarted()
    {
        transform.SetParent(null, true);
        SetMovementPhysics(false, true);
    }
    protected override void TickCreature(float deltaTime) { }
}
