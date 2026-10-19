using System.Collections.Generic;
using UnityEngine;

/// <summary>Tracks food and assigns exclusive reservations to foraging squirrels.</summary>
[DisallowMultipleComponent]
public sealed class FoodManager : MonoBehaviour
{
    [SerializeField] private List<SquirrelFood> foods = new List<SquirrelFood>();
    [Tooltip("Initial food population, using the first scene food as a template. No automatic refill.")]
    [SerializeField, Min(0)] private int initialFoodCount = 15;
    [SerializeField, Min(0f)] private float spawnRadius = 8f;
    public IReadOnlyList<SquirrelFood> Foods => foods;

    private void Start()
    {
        foods.RemoveAll(food => food == null);
        if (foods.Count == 0) return;
        var template = foods[0];
        while (foods.Count < initialFoodCount)
        {
            Vector2 offset = Random.insideUnitCircle * spawnRadius;
            var food = Instantiate(template, template.transform.position + new Vector3(offset.x, 0f, offset.y),
                template.transform.rotation, template.transform.parent);
            food.name = template.name + " " + (foods.Count + 1);
            Register(food);
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
            if (!food.IsAvailable) continue;
            float distance = (food.transform.position - squirrel.transform.position).sqrMagnitude;
            if (distance > best) continue;
            best = distance;
            nearest = food;
        }
        return nearest != null && nearest.Reserve(squirrel) ? nearest : null;
    }
}
