using System.Collections.Generic;
using UnityEngine;

/// <summary>Tracks food and assigns exclusive reservations to foraging squirrels.</summary>
[DisallowMultipleComponent]
public sealed class FoodManager : MonoBehaviour
{
    [SerializeField] private List<SquirrelFood> foods = new List<SquirrelFood>();
    [Tooltip("Initial food population, using the first scene food as a template. No automatic refill.")]
    [SerializeField, Min(0)] private int initialFoodCount = 15;
    [SerializeField, Min(0.1f)] private float spawnRadius = 6f;
    [SerializeField, Min(0.1f)] private float minimumTreeDistance = 1.5f;
    public IReadOnlyList<SquirrelFood> Foods => foods;

    public void GenerateAroundTrees(IReadOnlyList<SquirrelTree> trees)
    {
        foods.RemoveAll(food => food == null);
        if (foods.Count == 0 || trees == null || trees.Count == 0) return;
        var template = foods[0];
        while (foods.Count < initialFoodCount)
        {
            var food = Instantiate(template, template.transform.position,
                template.transform.rotation, template.transform.parent);
            food.name = template.name + " " + (foods.Count + 1);
            Register(food);
        }
        for (int i = 0; i < foods.Count; i++)
        {
            var food = foods[i];
            if (!food.IsAvailable) continue;
            var tree = trees[i % trees.Count];
            if (tree == null) continue;
            float outer = Mathf.Max(minimumTreeDistance, spawnRadius);
            SquirrelTree.PlaceOnGround(food.transform, tree.SampleGround(minimumTreeDistance, outer));
        }
    }

    public void Register(SquirrelFood food)
    {
        if (food != null && !foods.Contains(food)) foods.Add(food);
    }

    public SquirrelFood ReserveNearest(Squirrel squirrel, float radius)
    {
        SquirrelFood nearest = null;
        float best = radius * radius;
        for (int i = foods.Count - 1; i >= 0; i--)
        {
            var food = foods[i];
            if (food == null) { foods.RemoveAt(i); continue; }
            // Reserved ground food remains a valid goal: rivals can contest it on arrival.
            if (food.IsStored || !food.isActiveAndEnabled || !squirrel.ContainsInTerritory(food.transform.position)) continue;
            float distance = (food.transform.position - squirrel.transform.position).sqrMagnitude;
            if (distance > best) continue;
            best = distance;
            nearest = food;
        }
        if (nearest != null && nearest.IsAvailable) nearest.Reserve(squirrel);
        return nearest;
    }
}
