using System.Collections.Generic;
using UnityEngine;

/// <summary>Owns squirrel population, randomized sizes, capacities and home assignments.</summary>
[DisallowMultipleComponent]
public sealed class SquirrelManager : MonoBehaviour
{
    [SerializeField] private FoodManager foodManager;
    [SerializeField] private List<Squirrel> squirrels = new List<Squirrel>();
    [SerializeField] private List<SquirrelNest> nests = new List<SquirrelNest>();
    [SerializeField] private Vector2 sizeRange = new Vector2(0.7f, 1.4f);
    [SerializeField, Min(1)] private int baseCapacity = 3;
    [Tooltip("Minimum population. Clones the first squirrel when fewer exist.")]
    [SerializeField, Min(1)] private int population = 3;
    [SerializeField, Min(0f)] private float spawnRadius = 2f;
    public IReadOnlyList<Squirrel> Squirrels => squirrels;
    public IReadOnlyList<SquirrelNest> Nests => nests;

    public void Register(Squirrel squirrel)
    {
        if (squirrel != null && !squirrels.Contains(squirrel)) squirrels.Add(squirrel);
    }

    public void Register(SquirrelNest nest)
    {
        if (nest != null && !nests.Contains(nest)) nests.Add(nest);
    }

    public void SetFoodManager(FoodManager manager) => foodManager = manager;

    private void Start()
    {
        squirrels.RemoveAll(squirrel => squirrel == null);
        nests.RemoveAll(nest => nest == null);
        if (foodManager == null || nests.Count == 0 || squirrels.Count == 0)
        {
            Debug.LogWarning("SquirrelManager needs a FoodManager, a Squirrel and a Nest in this scene.", this);
            return;
        }
        // Clone before randomizing so every squirrel starts from the authored model scale.
        Squirrel template = squirrels[0];
        while (squirrels.Count < population)
        {
            Vector2 offset = Random.insideUnitCircle * spawnRadius;
            var squirrel = Instantiate(template, template.transform.position + new Vector3(offset.x, 0f, offset.y),
                template.transform.rotation, template.transform.parent);
            squirrel.name = template.name + " " + (squirrels.Count + 1);
            squirrels.Add(squirrel);
        }
        float minimum = Mathf.Max(0.1f, Mathf.Min(sizeRange.x, sizeRange.y));
        float maximum = Mathf.Max(minimum, Mathf.Max(sizeRange.x, sizeRange.y));
        foreach (var squirrel in squirrels)
        {
            SquirrelNest nearest = nests[0];
            foreach (var home in nests)
                if ((home.transform.position - squirrel.transform.position).sqrMagnitude <
                    (nearest.transform.position - squirrel.transform.position).sqrMagnitude) nearest = home;
            squirrel.Initialize(foodManager, nearest, Random.Range(minimum, maximum), baseCapacity);
        }
    }
}
