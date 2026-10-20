using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Value snapshot taken before food-specific consumption effects and destruction.</summary>
internal readonly struct BirdMeal
{
    internal readonly float Nutrition;
    internal readonly float SharingRadius;
    internal readonly Vector3 Position;
    internal readonly Creature.FoodTag FoodTags;
    internal readonly Type FoodType;
    internal readonly Scene Scene;

    internal BirdMeal(Creature food, float sharingRadius)
    {
        Nutrition = food.Nutrition;
        SharingRadius = Mathf.Max(0f, sharingRadius);
        Position = food.TryGetComponent<Rigidbody>(out var body) ? body.position : food.transform.position;
        FoodTags = food.FoodTags;
        FoodType = food.GetType();
        Scene = food.gameObject.scene;
    }
}
