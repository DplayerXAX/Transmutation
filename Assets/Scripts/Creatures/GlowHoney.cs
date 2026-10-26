using UnityEngine;

/// <summary>A luminous, carryable Plant food produced only after nectar fermentation.</summary>
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class GlowHoney : Creature
{
    protected override void InitializeCreature()
    {
        SetCreatureName("Glow Honey");
        SetFoodAndDiet(FoodTag.Plant, FoodTag.None);
        GetComponent<SphereCollider>().radius = 0.18f;
        GetComponent<Rigidbody>().mass = 0.2f;
    }
    public override string CarriedPrompt => "Glowing honey — a portable light and food";
    protected override void OnCarryStarted()
    {
        transform.SetParent(null, true);
        SetMovementPhysics(false, true);
    }
    protected override void TickCreature(float deltaTime) { }
}
