using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class Bloodstone : Creature
{
    public override bool CanBeConsumed => false;
    protected override void InitializeCreature()
    {
        SetCreatureName("Bloodstone");
        SetResolvedPolarity(CreaturePolarity.None);
        SetFoodAndDiet(FoodTag.Matter, FoodTag.None);
        ConfigureLifespan(300f, 0f);
    }
    protected override void TickCreature(float dt) { }
}
