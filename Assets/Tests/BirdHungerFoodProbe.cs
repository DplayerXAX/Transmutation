#if UNITY_EDITOR
using System.Reflection;
using UnityEngine;

/// <summary>Fixture for meal callbacks that mutate or immediately destroy their food.</summary>
public sealed class BirdHungerFoodProbe : Creature
{
    public int ConsumptionCount { get; private set; }
    public bool MutateOnConsume;
    public bool DestroyOnConsume;

    public override void Consume(Creature consumer)
    {
        ConsumptionCount++;
        if (MutateOnConsume)
        {
            SetFoodAndDiet(FoodTag.Meat, FoodTag.None);
            transform.position += Vector3.one * 1000f;
            typeof(Creature).GetField("nutrition", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(this, 0f);
        }
        if (DestroyOnConsume) DestroyImmediate(gameObject);
    }

    protected override void TickCreature(float deltaTime) { }
}
#endif
